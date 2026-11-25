using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>원본·대상 아바타의 같은 구역(머리) 상자. 좌표는 각 아바타 루트 기준 축(월드 단위).</summary>
    public readonly struct RegionBox
    {
        public readonly bool Valid;
        public readonly Bounds Source;
        public readonly Bounds Target;
        /// <summary>구역 정점들. 얼굴 앞면 깊이처럼 상자만으로 알 수 없는 표면 위치를 찾을 때 쓴다.</summary>
        public readonly Vector3[] SourcePoints;
        public readonly Vector3[] TargetPoints;

        public RegionBox(Bounds source, Bounds target, Vector3[] sourcePoints = null, Vector3[] targetPoints = null)
        {
            Valid = true;
            Source = source;
            Target = target;
            SourcePoints = sourcePoints ?? System.Array.Empty<Vector3>();
            TargetPoints = targetPoints ?? System.Array.Empty<Vector3>();
        }

        /// <summary>좌우 폭 비율. 잴 수 없으면 대체값.</summary>
        public float Ratio(float fallback)
        {
            return Valid ? ScaleProfile.SafeRatio(Target.size.x, Source.size.x, fallback) : fallback;
        }
    }

    /// <summary>
    /// 원본 대비 대상 아바타의 크기 비율. 머리는 몸 전체 비율과 크게 다를 수 있어 따로 잰다.
    /// (치비 아바타는 키가 작아도 머리는 큼)
    /// </summary>
    public readonly struct ScaleProfile
    {
        public const float MinRatio = 0.05f;
        public const float MaxRatio = 20f;
        const float Epsilon = 1e-5f;

        public readonly float Body;
        public readonly float Head;
        public readonly RegionBox HeadBox;
        /// <summary>팔·몸통 마디별 굵기·길이 비율. 잴 수 없었던 마디는 없다(null일 수 있음).</summary>
        public readonly IReadOnlyDictionary<HumanBodyBones, SegmentRatio> Segments;

        public ScaleProfile(float body, float head, RegionBox headBox = default,
            IReadOnlyDictionary<HumanBodyBones, SegmentRatio> segments = null)
        {
            Body = body;
            Head = head;
            HeadBox = headBox;
            Segments = segments;
        }

        public static readonly ScaleProfile Identity = new ScaleProfile(1f, 1f);

        /// <summary>두 크기의 비율. 어느 한쪽을 잴 수 없으면 대체값을 쓴다.</summary>
        public static float SafeRatio(float targetSize, float sourceSize, float fallback)
        {
            bool measurable = sourceSize > Epsilon && targetSize > Epsilon;
            return measurable ? Mathf.Clamp(targetSize / sourceSize, MinRatio, MaxRatio) : fallback;
        }

        /// <summary>사용자 배율 조정. 구역 상자는 위치 기준이라 그대로 둔다.</summary>
        public ScaleProfile Multiply(float body, float head)
        {
            // 몸 배율 조정은 마디 굵기·길이에도 같이 적용한다.
            var segments = Segments?.ToDictionary(p => p.Key, p => new SegmentRatio(p.Value.Thickness * body, p.Value.Length * body));
            return new ScaleProfile(Body * body, Head * head, HeadBox, segments);
        }

        public ScaleProfile WithSegments(IReadOnlyDictionary<HumanBodyBones, SegmentRatio> segments)
        {
            return new ScaleProfile(Body, Head, HeadBox, segments);
        }

        public bool TryGetSegment(HumanBodyBones bone, out SegmentRatio ratio)
        {
            ratio = default;
            return Segments != null && Segments.TryGetValue(bone, out ratio);
        }

        /// <summary>대상 본이 머리 아래에 있으면 머리 비율, 아니면 몸 비율.</summary>
        public float RatioFor(AvatarRig target, Transform targetBone)
        {
            return target.IsDescendantOf(targetBone, HumanBodyBones.Head) ? Head : Body;
        }

        /// <summary>대상 본이 머리 아래면 머리 상자. 아니면 Valid=false.</summary>
        public RegionBox BoxFor(AvatarRig target, Transform targetBone)
        {
            return target.IsDescendantOf(targetBone, HumanBodyBones.Head) ? HeadBox : default;
        }
    }
}
