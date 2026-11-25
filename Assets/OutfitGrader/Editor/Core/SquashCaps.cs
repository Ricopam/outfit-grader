using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 부위별 한쪽 눌림 상한(굵기 배율 / 길이 배율). 넘으면 옷 길이를 남긴다. 맞춤 방식(FitStyles)에서 시작하고,
    /// 형태 검사(FormCheck)에서 무너진 부위만 상한을 낮춰(옷을 더 길게) 다시 맞춘다. 늘이지 않는 부위(다리 등)는 0.
    /// </summary>
    public sealed class SquashCaps
    {
        /// <summary>무너진 부위의 상한에 곱해 낮추는 비율</summary>
        public const float LowerFactor = 0.7f;
        /// <summary>상한을 이보다 낮추지 않는다(1에 가까울수록 옷이 길이를 거의 줄이지 않는다).</summary>
        public const float MinCap = 1.4f;

        static readonly BodyRegion[] StretchRegions =
        {
            BodyRegion.Torso, BodyRegion.LeftUpperArm, BodyRegion.LeftLowerArm, BodyRegion.RightUpperArm, BodyRegion.RightLowerArm,
        };

        readonly Dictionary<BodyRegion, float> caps;

        SquashCaps(Dictionary<BodyRegion, float> caps) => this.caps = caps;

        public static SquashCaps For(FitStyle style)
        {
            return new SquashCaps(StretchRegions.ToDictionary(r => r, r => FitStyles.MaxSquash(style, r)));
        }

        /// <summary>부위의 상한. 늘이지 않는 부위는 0.</summary>
        public float Of(BodyRegion region) => caps.TryGetValue(region, out float cap) ? cap : 0f;

        public bool CanLower(BodyRegion region) => Of(region) > MinCap;

        /// <summary>부위 상한을 한 단계(LowerFactor배, 하한 MinCap) 낮춘 값</summary>
        public static float Step(float cap) => Mathf.Max(MinCap, cap * LowerFactor);

        /// <summary>주어진 부위의 상한을 바꾼 새 표</summary>
        public SquashCaps With(IReadOnlyDictionary<BodyRegion, float> changes)
        {
            var next = new Dictionary<BodyRegion, float>(caps);
            foreach (var kv in changes) next[kv.Key] = kv.Value;
            return new SquashCaps(next);
        }

        /// <summary>주어진 부위 중 낮출 수 있는 부위만 상한을 한 단계 낮춘 새 표</summary>
        public SquashCaps Lowered(IEnumerable<BodyRegion> regions)
        {
            return With(regions.Where(CanLower).ToDictionary(r => r, r => Step(caps[r])));
        }

        /// <summary>보고용: 맞춤 방식 기본값과 달라진 부위의 상한</summary>
        public string Describe(FitStyle style)
        {
            var changed = caps.Where(kv => !Mathf.Approximately(kv.Value, FitStyles.MaxSquash(style, kv.Key)))
                .Select(kv => $"{kv.Key} {FitStyles.MaxSquash(style, kv.Key):0.0} -> {kv.Value:0.0}");
            return string.Join(", ", changed);
        }
    }
}
