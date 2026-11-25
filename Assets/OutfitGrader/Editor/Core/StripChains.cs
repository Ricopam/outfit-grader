using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 끈처럼 가늘고 길게 매달린 의상 부품을 찾는다(PRD 0장 형태 보존). 끈은 몸 부위 규칙(굵기·길이를 따로 맞춤)을 따르면
    /// 한쪽으로 눌려 지그재그·가시 모양이 되므로, 매달린 지점 기준으로 모양을 그대로 두고 크기만 균등하게 바꾼다.
    /// 판별은 이름이 아닌 구조와 모양으로 한다: 옷 몸판 본(Humanoid 본 바로 아래 본)의 자식 사슬 중
    /// 길이 / 굵기가 기준 이상이고 굵기가 부위 반지름에 비해 가는 것.
    /// 실측(Felis 후드티): 끈 굵기 19mm·길쭉함 12.3, 소맷부리 고리 56mm·1.7. 치마 판자 가닥(폭 수 cm)은 굵기 조건으로 뺀다.
    /// </summary>
    public static class StripChains
    {
        /// <summary>사슬 정점들의 길이 / 굵기가 이 이상이면 끈 후보로 본다.</summary>
        public const float MinElongation = 3f;
        /// <summary>끈 굵기는 따르는 몸 부위 반지름(중앙값)의 이 비율 이하여야 한다(치마·옷자락 판자 가닥 제외).</summary>
        public const float MaxWidthToRadius = 0.35f;
        const float MinWidth = 1e-5f;
        /// <summary>축에서 떨어진 거리 평균(반지름)을 굵기(지름)로 바꾸는 배수</summary>
        const float RadiusToWidth = 2f;

        /// <summary>점들의 길쭉함: 가장 먼 두 점 거리 / 굵기. 점이 2개 미만이면 0.</summary>
        public static float Elongation(IReadOnlyList<Vector3> points)
        {
            return points.Count < 2 ? 0f : Measure(points, out float width) / Mathf.Max(width, MinWidth);
        }

        /// <summary>가장 먼 두 점 거리(길이)를 돌려주고, 그 축에서 떨어진 거리 평균의 2배를 굵기로 낸다.</summary>
        static float Measure(IReadOnlyList<Vector3> points, out float width)
        {
            Vector3 center = points.Aggregate(Vector3.zero, (sum, p) => sum + p) / points.Count;
            Vector3 end1 = Farthest(points, center), end2 = Farthest(points, end1);
            Vector3 axis = (end2 - end1).normalized;
            width = points.Average(p => Vector3.ProjectOnPlane(p - end1, axis).magnitude) * RadiusToWidth;
            return (end2 - end1).magnitude;
        }

        static Vector3 Farthest(IReadOnlyList<Vector3> points, Vector3 from)
        {
            Vector3 best = from;
            float bestDistance = -1f;
            foreach (Vector3 p in points)
            {
                float d = (p - from).sqrMagnitude;
                if (d > bestDistance) { bestDistance = d; best = p; }
            }
            return best;
        }

        /// <summary>
        /// 정점마다 끈 번호(-1: 끈 아님, 길이는 vertexCount). anchors[끈 번호]는 사슬 뿌리 본에 가장 가까운 정점(매달린 지점),
        /// regions[끈 번호]는 사슬이 따르는 몸 부위. 주 가중치(boneIndex0) 본으로 정점을 사슬에 묶는다.
        /// </summary>
        /// <param name="regionRadius">원본 몸의 부위 반지름 중앙값(굵기 조건). 0이면 그 부위의 끈을 판별하지 않는다.</param>
        public static int[] Find(FitAnalysis analysis, Transform[] bones, BoneWeight[] weights, Vector3[] sourceWorld,
            System.Func<BodyRegion, float> regionRadius, out int[] anchors, out BodyRegion[] regions)
        {
            var regionOf = analysis.Matches
                .Where(m => m.OutfitBone != null && (m.Kind == MatchKind.Humanoid || m.Kind == MatchKind.HumanoidFallback))
                .GroupBy(m => m.OutfitBone).ToDictionary(g => g.Key, g => BodyCylinders.RegionOf(g.First().HumanBone));
            var humanoid = new HashSet<Transform>(regionOf.Keys);
            Transform[] roots = bones.Select(b => ChainRoot(b, humanoid)).ToArray();
            bool IsChainVertex(int i)
            {
                bool hasChainBone = i < weights.Length && weights[i].weight0 > 0f && weights[i].boneIndex0 < roots.Length
                    && roots[weights[i].boneIndex0] != null;
                return hasChainBone && !float.IsInfinity(sourceWorld[i].x);
            }

            var stripOf = Enumerable.Repeat(-1, sourceWorld.Length).ToArray();
            var anchorList = new List<int>();
            var regionList = new List<BodyRegion>();
            foreach (var group in Enumerable.Range(0, sourceWorld.Length).Where(IsChainVertex).GroupBy(i => roots[weights[i].boneIndex0]))
            {
                Transform holder = HumanoidAncestor(group.Key, humanoid);
                if (holder == null) continue;
                BodyRegion region = regionOf[holder];
                List<Vector3> points = group.Select(i => sourceWorld[i]).ToList();
                if (!IsStrip(points, regionRadius(region))) continue;
                foreach (int i in group) stripOf[i] = anchorList.Count;
                Vector3 rootPosition = group.Key.position;
                anchorList.Add(group.Aggregate((a, b) => (sourceWorld[a] - rootPosition).sqrMagnitude <= (sourceWorld[b] - rootPosition).sqrMagnitude ? a : b));
                regionList.Add(region);
            }
            anchors = anchorList.ToArray();
            regions = regionList.ToArray();
            return stripOf;
        }

        /// <summary>길쭉하고(MinElongation 이상) 부위 반지름에 비해 가는(MaxWidthToRadius 이하) 점 묶음이면 끈.</summary>
        public static bool IsStrip(IReadOnlyList<Vector3> points, float regionRadius)
        {
            if (points.Count < 2 || regionRadius <= 0f) return false;
            float length = Measure(points, out float width);
            bool isElongated = length / Mathf.Max(width, MinWidth) >= MinElongation;
            bool isThin = width <= regionRadius * MaxWidthToRadius;
            return isElongated && isThin;
        }

        /// <summary>
        /// 매달린 사슬의 뿌리: Humanoid 본도, Humanoid 본 바로 아래 본(옷 몸판 본)도 아닌 본에서 위로 올라가
        /// 옷 몸판 본 바로 아래에서 멈춘 본. 해당하지 않으면 null.
        /// </summary>
        static Transform ChainRoot(Transform bone, HashSet<Transform> humanoid)
        {
            bool IsHumanoid(Transform t) => t != null && humanoid.Contains(t);
            bool IsBodyBone(Transform t) => t != null && IsHumanoid(t.parent);
            if (bone == null || IsHumanoid(bone) || IsBodyBone(bone)) return null;
            Transform root = bone;
            while (root.parent != null && !IsHumanoid(root.parent) && !IsBodyBone(root.parent)) root = root.parent;
            return IsBodyBone(root.parent) ? root : null;
        }

        static Transform HumanoidAncestor(Transform bone, HashSet<Transform> humanoid)
        {
            for (Transform t = bone.parent; t != null; t = t.parent)
                if (humanoid.Contains(t)) return t;
            return null;
        }
    }
}
