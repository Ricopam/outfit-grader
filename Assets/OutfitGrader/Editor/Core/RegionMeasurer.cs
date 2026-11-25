using System.Collections.Generic;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 아바타 메시를 실제로 재서 몸·머리 크기를 구한다.
    /// 머리는 머리 본(하위 Humanoid 본 포함)에 가장 크게 묶인 정점들의 상자를 재고, 비율은 좌우 폭으로 정한다.
    /// </summary>
    public static class RegionMeasurer
    {
        /// <summary>정점 묶음을 이미 모아 둔 경우. 분석은 아바타마다 정점을 한 번만 읽는다.</summary>
        /// <param name="headMeasured">머리 구역을 양쪽 모두 잴 수 있었는지</param>
        public static ScaleProfile Measure(BodyPointCloud source, BodyPointCloud target, out bool headMeasured)
        {
            float body = ScaleProfile.SafeRatio(HipHeight(target.Rig), HipHeight(source.Rig), 1f);
            RegionBox head = MeasureRegion(source, target, HumanBodyBones.Head);
            headMeasured = head.Valid;
            return new ScaleProfile(body, head.Ratio(body), head);
        }

        public static ScaleProfile Measure(AvatarRig source, Transform sourceExcluded, AvatarRig target, out bool headMeasured)
        {
            return Measure(BodyPointCloud.Collect(source, Single(sourceExcluded)), BodyPointCloud.Collect(target, null), out headMeasured);
        }

        static RegionBox MeasureRegion(BodyPointCloud source, BodyPointCloud target, HumanBodyBones region)
        {
            List<Vector3> sourcePoints = source.InRegion(region);
            List<Vector3> targetPoints = target.InRegion(region);
            bool measurable = TryBounds(sourcePoints, out Bounds sourceBox) & TryBounds(targetPoints, out Bounds targetBox);
            return measurable ? new RegionBox(sourceBox, targetBox, sourcePoints.ToArray(), targetPoints.ToArray()) : default;
        }

        /// <summary>바닥(루트)에서 엉덩이 본까지의 높이. 몸 전체 크기 대표값.</summary>
        public static float HipHeight(AvatarRig rig)
        {
            Transform hips = rig.GetHuman(HumanBodyBones.Hips);
            if (hips == null) return 0f;
            return Vector3.Dot(hips.position - rig.Root.position, rig.Root.up);
        }

        /// <summary>구역의 좌우 폭(월드 단위). 잴 수 없으면 0.</summary>
        public static float RegionWidth(AvatarRig rig, Transform excluded, HumanBodyBones region)
        {
            return TryRegionBounds(rig, excluded, region, out Bounds box) ? box.size.x : 0f;
        }

        /// <summary>구역 정점들의 상자. 좌표는 아바타 루트 기준 축(월드 단위)이다.</summary>
        public static bool TryRegionBounds(AvatarRig rig, Transform excluded, HumanBodyBones region, out Bounds box)
        {
            return TryBounds(BodyPointCloud.Collect(rig, Single(excluded)).InRegion(region), out box);
        }

        static bool TryBounds(List<Vector3> points, out Bounds box)
        {
            box = default;
            if (points.Count == 0) return false;
            box = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 p in points) box.Encapsulate(p);
            return box.size.x > 0f;
        }

        public static IReadOnlyCollection<Transform> Single(Transform excluded)
        {
            return excluded == null ? null : new[] { excluded };
        }
    }
}
