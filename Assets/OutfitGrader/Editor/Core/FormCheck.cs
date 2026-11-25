using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 옷 형태가 무너졌는지 검사한다(PRD 0장: 어떤 아바타·의상이든 사람이 봤을 때 형태가 무너지지 않아야 한다).
    /// 삼각형마다 원본에서 결과로 가는 변형이 한쪽으로 몇 배 눌렸는지(두 늘음 비율의 비)를 재고,
    /// 부위별로 크게 눌린 면의 비율이 기준을 넘으면 그 부위가 무너진 것으로 본다.
    /// 기준값은 실모델 측정으로 정했다: 형태가 괜찮은 Lumelana는 모든 부위가 3배 이상 눌린 면 10% 이하, 무너진 inuinu는 24~64%.
    /// </summary>
    public static class FormCheck
    {
        /// <summary>이 배수 이상 한쪽으로 눌린 삼각형을 무너진 면으로 센다.</summary>
        public const float CollapseSquash = 3f;
        /// <summary>한 부위에서 무너진 면이 이 비율을 넘으면 그 부위의 형태가 무너진 것으로 본다.</summary>
        public const float MaxCollapsedShare = 0.15f;
        const float MinArea = 1e-12f;
        /// <summary>작은 늘음 제곱이 큰 늘음 제곱의 이 비율 이하면 선으로 납작해진 면(가장 심한 무너짐)으로 본다.</summary>
        const float FlatRatio = 1e-8f;

        /// <summary>
        /// 원본 삼각형(a, b, c)에서 결과 삼각형(a2, b2, c2)으로 가는 변형의 한쪽 눌림(최대 늘음 / 최소 늘음).
        /// 원본 넓이가 없거나 점으로 줄었으면 1, 선으로 납작해졌으면 float.MaxValue.
        /// </summary>
        public static float Squash(Vector3 a, Vector3 b, Vector3 c, Vector3 a2, Vector3 b2, Vector3 c2)
        {
            Vector3 e1 = b - a, e2 = c - a;
            Vector3 normal = Vector3.Cross(e1, e2);
            if (normal.sqrMagnitude < MinArea || e1.sqrMagnitude < MinArea) return 1f;
            // 원본 삼각형 평면의 2D 좌표: e1 = (x1, 0), e2 = (x2, y2)
            Vector3 u = e1.normalized, v = Vector3.Cross(normal.normalized, u);
            float x1 = e1.magnitude, x2 = Vector3.Dot(e2, u), y2 = Vector3.Dot(e2, v);
            Vector3 c1 = (b2 - a2) / x1, c2v = ((c2 - a2) - c1 * x2) / y2;
            // 변형 행렬 F = [c1 c2]의 F^T F 고유값 = 두 늘음의 제곱
            float a11 = Vector3.Dot(c1, c1), a12 = Vector3.Dot(c1, c2v), a22 = Vector3.Dot(c2v, c2v);
            float half = (a11 + a22) * 0.5f, disc = Mathf.Sqrt(Mathf.Max(half * half - (a11 * a22 - a12 * a12), 0f));
            float big = half + disc, small = half - disc;
            if (big <= MinArea) return 1f;
            return small <= big * FlatRatio ? float.MaxValue : Mathf.Sqrt(big / small);
        }

        /// <summary>부위별 무너진 면(CollapseSquash 이상 눌린 삼각형) 비율. triangleRegion은 삼각형마다 부위(None은 세지 않음).</summary>
        public static Dictionary<BodyRegion, float> CollapsedShares(Vector3[] before, Vector3[] after, int[] triangles, BodyRegion[] triangleRegion)
        {
            var total = new Dictionary<BodyRegion, int>();
            var collapsed = new Dictionary<BodyRegion, int>();
            for (int t = 0; t < triangleRegion.Length; t++)
            {
                BodyRegion region = triangleRegion[t];
                if (region == BodyRegion.None) continue;
                int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
                total[region] = total.TryGetValue(region, out int n) ? n + 1 : 1;
                bool isCollapsed = Squash(before[a], before[b], before[c], after[a], after[b], after[c]) >= CollapseSquash;
                if (isCollapsed) collapsed[region] = collapsed.TryGetValue(region, out int m) ? m + 1 : 1;
            }
            return total.ToDictionary(kv => kv.Key, kv => (collapsed.TryGetValue(kv.Key, out int m) ? m : 0) / (float)kv.Value);
        }

        /// <summary>무너진 면 비율이 기준을 넘은 부위</summary>
        public static List<BodyRegion> Collapsed(Dictionary<BodyRegion, float> shares)
        {
            return shares.Where(kv => kv.Value > MaxCollapsedShare).Select(kv => kv.Key).OrderBy(r => r).ToList();
        }
    }
}
