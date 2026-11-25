using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 부위 원통 밖 본(머리·목, 손)에만 묶인 옷 부분을 모양 그대로 덩어리째 옮겨 이웃 몸판·소매 이음매에 붙인다(PRD 0장 형태 보존).
    /// 후드는 머리 본을 따라 이미 머리 크기 비율로 균등하게 커져 있지만 몸통 규칙으로 놓인 목둘레와 따로 움직여 이음매가 벌어졌고
    /// (inuinu 뒤 후드 아래 틈), 소맷부리는 손 본을 따라 원본 크기 그대로 손목 너머에 남아 소매가 팔의 2.25배가 됐다(inuinu).
    /// </summary>
    public static class PartAttach
    {
        /// <summary>이음매 쌍이 이보다 적은 덩어리는 옮기지 않는다(쌍 한두 개로는 벌어짐을 믿을 수 없음).</summary>
        public const int MinSeamPairs = 3;

        /// <summary>가중치가 있는 본이 모두 고른 본(isPartBone, 예: 머리·목 Humanoid 본에 연결)인 정점.</summary>
        public static int[] Members(BoneWeight[] weights, System.Func<int, bool> isPartBone)
        {
            bool IsPartOrUnused(int bone, float weight) => weight <= 0f || isPartBone(bone);
            return Enumerable.Range(0, weights.Length).Where(i =>
            {
                BoneWeight w = weights[i];
                return w.weight0 > 0f && IsPartOrUnused(w.boneIndex0, w.weight0) && IsPartOrUnused(w.boneIndex1, w.weight1)
                    && IsPartOrUnused(w.boneIndex2, w.weight2) && IsPartOrUnused(w.boneIndex3, w.weight3);
            }).ToArray();
        }

        /// <summary>
        /// 고른 정점을 삼각형 변으로 이어진 덩어리로 나눈다. 후드와 따로 떨어진 목걸이·리본 같은 덩어리가
        /// 후드 이음매 기준으로 함께 끌려가지 않게 하기 위함.
        /// </summary>
        public static List<int[]> Parts(int[] members, int[] triangles)
        {
            var parent = members.ToDictionary(v => v, v => v);
            int Root(int v) { while (parent[v] != v) v = parent[v] = parent[parent[v]]; return v; }
            for (int t = 0; t + 2 < triangles.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = triangles[t + k], b = triangles[t + (k + 1) % 3];
                    if (parent.ContainsKey(a) && parent.ContainsKey(b)) parent[Root(a)] = Root(b);
                }
            return members.GroupBy(Root).Select(g => g.ToArray()).ToList();
        }

        /// <summary>덩어리 정점과 삼각형 변으로 이어진 덩어리 밖 정점 쌍(목둘레 이음매). 위치만 같은 복제 정점은 잡지 않는다.</summary>
        public static (int part, int body)[] Seams(int[] part, int[] triangles)
        {
            var isPart = new HashSet<int>(part);
            var pairs = new HashSet<(int, int)>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = triangles[t + k], b = triangles[t + (k + 1) % 3];
                    if (isPart.Contains(a) && !isPart.Contains(b)) pairs.Add((a, b));
                    else if (isPart.Contains(b) && !isPart.Contains(a)) pairs.Add((b, a));
                }
            return pairs.ToArray();
        }

        /// <summary>
        /// 덩어리를 옮길 거리: 이음매 쌍마다 배치 뒤 벌어짐(몸판 쪽 - 덩어리 쪽)에서 원본에서 떨어져 있던 만큼(sourceOffset)을 뺀 값의
        /// 축별 중앙값(앞섶 끝·끈 구멍처럼 일부만 크게 벌어진 쌍에 끌려가지 않게). 쌍이 MinSeamPairs보다 적으면 false.
        /// </summary>
        public static bool TryShift(IReadOnlyList<(int part, int body)> seams, Vector3[] placed, System.Func<int, int, Vector3> sourceOffset, out Vector3 shift)
        {
            shift = Vector3.zero;
            if (seams.Count < MinSeamPairs) return false;
            Vector3[] gaps = seams.Select(p => placed[p.body] - placed[p.part] - sourceOffset(p.part, p.body)).ToArray();
            float Median(System.Func<Vector3, float> axis)
            {
                float[] values = gaps.Select(axis).OrderBy(x => x).ToArray();
                return values[values.Length / 2];
            }
            shift = new Vector3(Median(g => g.x), Median(g => g.y), Median(g => g.z));
            return true;
        }

        /// <summary>
        /// 오프셋(원본 루트 기준)을 원본 축 방향 성분은 along배, 수직 성분은 across배로 바꾼 뒤 원본 축에서 대상 축으로 돌린다.
        /// 소맷부리를 팔 방향으로는 팔 비율, 둘레로는 손을 덮는 비율로 맞추고, 원본·대상 팔 각도 차이(A·T 자세)를 따르게 하기 위함.
        /// </summary>
        public static Vector3 AxisStretch(Vector3 offset, Vector3 fromAxis, Vector3 toAxis, float along, float across)
        {
            Vector3 alongPart = fromAxis * Vector3.Dot(offset, fromAxis);
            return Quaternion.FromToRotation(fromAxis, toAxis) * (alongPart * along + (offset - alongPart) * across);
        }
    }
}
