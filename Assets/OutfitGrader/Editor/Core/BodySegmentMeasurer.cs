using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>몸 마디(팔·몸통 본 하나) 하나의 원본 대비 대상 비율</summary>
    public readonly struct SegmentRatio
    {
        /// <summary>본 축에 수직인 단면 굵기 비율</summary>
        public readonly float Thickness;
        /// <summary>본 축 방향 길이 비율</summary>
        public readonly float Length;

        public SegmentRatio(float thickness, float length)
        {
            Thickness = thickness;
            Length = length;
        }
    }

    /// <summary>
    /// 상의가 덮는 팔·몸통 마디마다 두 아바타의 굵기와 길이를 잰다.
    /// 몸 전체 비율 하나로 의상을 줄이면 굵기 비율이 다른 아바타에서 옷이 몸 안으로 묻히므로,
    /// 의상 본을 길이 방향과 단면 방향으로 따로 맞추기 위함.
    /// </summary>
    public static class BodySegmentMeasurer
    {
        /// <summary>마디와 그 축 끝 본. 축 끝 본이 없는 아바타는 해당 마디를 재지 않는다.</summary>
        static readonly (HumanBodyBones bone, HumanBodyBones end)[] Segments =
        {
            (HumanBodyBones.Hips, HumanBodyBones.Spine),
            (HumanBodyBones.Spine, HumanBodyBones.Chest),
            (HumanBodyBones.Chest, HumanBodyBones.Neck),
            (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
            (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
            (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
            (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
        };

        /// <summary>마디 길이가 엉덩이 높이의 이 비율보다 짧으면 길이를 믿지 않는다 (Hips와 Spine이 거의 겹친 아바타 등).</summary>
        public const float MinLengthToHipHeight = 0.03f;
        /// <summary>
        /// 굵기 비율은 몸 비율의 1/이 값 ~ 이 값 배 안으로 제한한다(측정 이상치로 옷이 극단적으로 찌그러지지 않게).
        /// 치비·동물형은 몸 비율이 작아도 팔다리가 굵어 정상값이 몸 비율의 7~8배에 이르므로 넓게 둔다.
        /// </summary>
        public const float MaxThicknessDeviation = 10f;
        /// <summary>길이 비율은 본 배치 관례 차이에 흔들리므로 굵기보다 좁게(몸 비율의 1/2~2배) 제한한다.</summary>
        public const float MaxLengthDeviation = 2f;
        /// <summary>
        /// 단면 굵기로 쓰는 반지름 분위: 본 축에서 떨어진 거리 중 이 비율 지점. 옷은 몸의 바깥 윤곽에 맞아야 하는데,
        /// 평균은 축 가까이 몰린 정점(겨드랑이·목 안쪽 등)에 끌려 작게 나온다(Lumelana 가슴: 평균 0.79배, 바깥 윤곽 0.98배).
        /// 최대값은 튀는 정점 하나에 흔들리므로 90%를 쓴다.
        /// </summary>
        public const float OutlineQuantile = 0.9f;
        /// <summary>
        /// 굵기는 마디 가운데 구간(축 방향 이 비율부터 1-이 비율까지)의 정점으로 잰다. 관절 근처에는 어깨·겨드랑이처럼
        /// 이웃 마디로 이어지는 정점이 섞여 단면이 실제보다 크게 잡힌다(재봉 치수를 마디 가운데 둘레로 재는 것과 같음).
        /// </summary>
        public const float JointMargin = 0.2f;
        /// <summary>가운데 구간 정점이 이보다 적으면(짧은 마디 등) 마디 전체 정점으로 잰다.</summary>
        const int MinMidPoints = 8;

        /// <summary>
        /// 몸통 마디. 길이는 본마다 재지 않고 Hips -> Neck 전체 길이 비율 하나를 쓴다.
        /// 척추·가슴 본을 어디에 두는지는 리그마다 달라(Lumelana 척추 5.5cm, フェリス 13.2cm) 본마다 재면 몸통이 눌린다.
        /// </summary>
        static readonly HumanBodyBones[] TorsoBones = { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest };

        /// <param name="bodyRatio">몸 비율. 잴 수 없는 값의 대체값이자 제한 기준</param>
        public static Dictionary<HumanBodyBones, SegmentRatio> Measure(BodyPointCloud source, BodyPointCloud target, float bodyRatio)
        {
            var ratios = new Dictionary<HumanBodyBones, SegmentRatio>();
            float sourceMinLength = RegionMeasurer.HipHeight(source.Rig) * MinLengthToHipHeight;
            float targetMinLength = RegionMeasurer.HipHeight(target.Rig) * MinLengthToHipHeight;
            float torso = ScaleProfile.SafeRatio(TorsoLength(target.Rig), TorsoLength(source.Rig), bodyRatio);
            foreach (var (bone, end) in Segments)
            {
                bool hasSource = TryMeasure(source, bone, end, out float sourceRadius, out float sourceLength);
                bool hasTarget = TryMeasure(target, bone, end, out float targetRadius, out float targetLength);
                if (!hasSource || !hasTarget) continue;

                bool isTorso = TorsoBones.Contains(bone);
                bool isLengthReliable = sourceLength >= sourceMinLength && targetLength >= targetMinLength;
                // 몸통은 마디가 짧아도(Hips와 Spine이 1mm 붙은 フェリス 등) 길이 축을 Hips -> Neck으로 판정하므로 몸통 비율을 쓴다.
                float length = isTorso ? torso
                    : isLengthReliable ? ScaleProfile.SafeRatio(targetLength, sourceLength, bodyRatio) : bodyRatio;
                float thickness = ScaleProfile.SafeRatio(targetRadius, sourceRadius, bodyRatio);
                ratios[bone] = new SegmentRatio(
                    LimitToBody(thickness, bodyRatio, MaxThicknessDeviation),
                    LimitToBody(length, bodyRatio, MaxLengthDeviation));
            }
            return ratios;
        }

        public static Dictionary<HumanBodyBones, SegmentRatio> Measure(AvatarRig source, Transform sourceExcluded, AvatarRig target, float bodyRatio)
        {
            return Measure(BodyPointCloud.Collect(source, RegionMeasurer.Single(sourceExcluded)), BodyPointCloud.Collect(target, null), bodyRatio);
        }

        /// <summary>Hips에서 Neck까지 거리(월드 단위). 둘 중 하나가 없으면 0(몸 비율로 대체됨).</summary>
        static float TorsoLength(AvatarRig rig)
        {
            Transform hips = rig.GetHuman(HumanBodyBones.Hips), neck = rig.GetHuman(HumanBodyBones.Neck);
            return hips == null || neck == null ? 0f : Vector3.Distance(hips.position, neck.position);
        }

        public static float LimitToBody(float ratio, float bodyRatio, float maxDeviation)
        {
            return Mathf.Clamp(ratio, bodyRatio / maxDeviation, bodyRatio * maxDeviation);
        }

        /// <summary>
        /// 마디 축의 방향(월드). 축 끝 본이 없으면 false.
        /// 몸통 마디는 Hips -> Neck 방향을 쓴다: 몸통 길이 비율을 그 방향에 적용하고, 1mm짜리 Hips 마디처럼 짧은 축이 엉뚱한 쪽을 가리키지 않게.
        /// </summary>
        public static bool TryGetAxis(AvatarRig rig, HumanBodyBones bone, out Vector3 axis)
        {
            axis = Vector3.zero;
            bool isTorso = TorsoBones.Contains(bone);
            Transform start = rig.GetHuman(isTorso ? HumanBodyBones.Hips : bone);
            Transform end = isTorso ? rig.GetHuman(HumanBodyBones.Neck) : EndOf(rig, bone);
            if (start == null || end == null) return false;
            axis = end.position - start.position;
            return axis.sqrMagnitude > 1e-10f;
        }

        static Transform EndOf(AvatarRig rig, HumanBodyBones bone)
        {
            foreach (var (b, end) in Segments)
            {
                if (b == bone) return rig.GetHuman(end);
            }
            return null;
        }

        static bool TryMeasure(BodyPointCloud cloud, HumanBodyBones bone, HumanBodyBones end, out float radius, out float length)
        {
            radius = length = 0f;
            AvatarRig rig = cloud.Rig;
            Transform start = rig.GetHuman(bone), endBone = rig.GetHuman(end);
            if (start == null || endBone == null) return false;

            Vector3 a = FitPlacement.ToRootFrame(rig.Root, start.position);
            Vector3 b = FitPlacement.ToRootFrame(rig.Root, endBone.position);
            length = Vector3.Distance(a, b);
            radius = OutlineRadius(cloud.OfBone(bone), a, b);
            return radius > 0f && length > 0f;
        }

        /// <summary>
        /// 점들이 a -> b 직선에서 떨어진 거리의 OutlineQuantile 지점(바깥 윤곽 단면 반지름). 마디 가운데 구간 정점으로 잰다.
        /// 점이 없거나 축이 없으면 0.
        /// </summary>
        public static float OutlineRadius(IReadOnlyList<Vector3> points, Vector3 a, Vector3 b)
        {
            Vector3 axis = b - a;
            if (points.Count == 0 || axis.sqrMagnitude < 1e-10f) return 0f;

            Vector3 direction = axis.normalized;
            float length = axis.magnitude;
            List<Vector3> mid = points.Where(p => IsMidSegment(Vector3.Dot(p - a, direction) / length)).ToList();
            IReadOnlyList<Vector3> measured = mid.Count >= MinMidPoints ? mid : points;
            float[] distances = measured.Select(p => Vector3.Cross(p - a, direction).magnitude).OrderBy(d => d).ToArray();
            return distances[Mathf.FloorToInt(OutlineQuantile * (distances.Length - 1))];
        }

        static bool IsMidSegment(float t)
        {
            return t >= JointMargin && t <= 1f - JointMargin;
        }
    }
}
