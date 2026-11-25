using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 선분(귀·꼬리·몸 메시의 변)을 격자 칸에 나눠 담아, 삼각형마다 겹치는 칸의 변만 검사한다.
    /// 모든 쌍을 비교하면 옷 삼각형 수천 x 꼬리 변 수만이 되어 맞추기가 8초까지 늘었다.
    /// </summary>
    public sealed class EdgeGrid
    {
        /// <summary>이보다 짧은 변(월드 단위)은 교차 검사에서 뺀다(크기 0으로 숨긴 꼬리 등은 격자 칸을 극단적으로 작게 만든다).</summary>
        public const float MinEdgeLength = 1e-4f;

        readonly IReadOnlyList<(Vector3 a, Vector3 b)> edges;
        /// <summary>변마다 감싸는 범위(최소·최대). 판정마다 다시 만들지 않게 미리 둔다.</summary>
        readonly Vector3[] edgeMin, edgeMax;
        /// <summary>
        /// 변마다 마지막으로 검사한 판정 번호. 삼각형이 여러 칸에 걸치면 같은 변이 칸마다 들어 있어
        /// 한 판정 안에서 같은 변을 두 번 검사하지 않게 한다(단일 스레드 전제).
        /// </summary>
        readonly int[] lastQuery;
        int query;
        readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
        readonly float cellSize;
        /// <summary>모든 변을 감싸는 범위. 큰 삼각형이 변이 없는 빈 칸까지 훑지 않게 이 범위로 자른다.</summary>
        readonly Bounds extent;

        public EdgeGrid(IReadOnlyList<(Vector3 a, Vector3 b)> edges)
        {
            this.edges = edges;
            int count = edges.Count;
            edgeMin = new Vector3[count];
            edgeMax = new Vector3[count];
            lastQuery = new int[count];
            // 변 약 10만 개(Lumelana 몸)에 LINQ·작은 배열을 쓰면 만들기만 약 46ms 걸려 반복문으로 한 번에 구한다(PRD AC8).
            double lengthSum = 0d;
            for (int i = 0; i < count; i++)
            {
                edgeMin[i] = Vector3.Min(edges[i].a, edges[i].b);
                edgeMax[i] = Vector3.Max(edges[i].a, edges[i].b);
                lengthSum += Vector3.Distance(edges[i].a, edges[i].b);
            }
            // 칸은 변 평균 길이의 두 배: 변 하나가 차지하는 칸이 몇 개로 끝나게 한다(Enumerable.Average와 같게 double로 평균).
            cellSize = Mathf.Max((float)(lengthSum / count) * 2f, MinEdgeLength);
            if (count > 0)
            {
                extent = new Bounds(edgeMin[0], Vector3.zero);
                for (int i = 0; i < count; i++) { extent.Encapsulate(edgeMin[i]); extent.Encapsulate(edgeMax[i]); }
            }
            for (int i = 0; i < count; i++)
            {
                Vector3Int min = Cell(edgeMin[i]), max = Cell(edgeMax[i]);
                for (int x = min.x; x <= max.x; x++)
                for (int y = min.y; y <= max.y; y++)
                for (int z = min.z; z <= max.z; z++)
                {
                    var key = new Vector3Int(x, y, z);
                    if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>();
                    list.Add(i);
                }
            }
        }

        /// <summary>변 하나라도 삼각형을 뚫고 지나가는지</summary>
        public bool Pierces(Vector3 a, Vector3 b, Vector3 c)
        {
            if (float.IsInfinity(a.x) || float.IsInfinity(b.x) || float.IsInfinity(c.x)) return false;
            Vector3 low = Vector3.Min(Vector3.Min(a, b), c), high = Vector3.Max(Vector3.Max(a, b), c);
            if (!Overlaps(low, high, extent.min, extent.max)) return false;
            query++;
            // 삼각형마다 수천 번 불려 반복자·배열을 만들지 않고 칸을 직접 훑는다.
            Vector3Int min = Cell(Vector3.Max(low, extent.min)), max = Cell(Vector3.Min(high, extent.max));
            for (int x = min.x; x <= max.x; x++)
            for (int y = min.y; y <= max.y; y++)
            for (int z = min.z; z <= max.z; z++)
            {
                if (cells.TryGetValue(new Vector3Int(x, y, z), out List<int> list) && HitsAny(list, low, high, a, b, c)) return true;
            }
            return false;
        }

        bool HitsAny(List<int> list, Vector3 low, Vector3 high, Vector3 a, Vector3 b, Vector3 c)
        {
            for (int k = 0; k < list.Count; k++)
            {
                int i = list[k];
                if (lastQuery[i] == query) continue;
                lastQuery[i] = query;
                if (Overlaps(edgeMin[i], edgeMax[i], low, high) && AppendageHoleCutter.SegmentHitsTriangle(edges[i].a, edges[i].b, a, b, c)) return true;
            }
            return false;
        }

        /// <summary>두 축 정렬 범위가 겹치는지(경계에 닿아도 겹침)</summary>
        static bool Overlaps(Vector3 minA, Vector3 maxA, Vector3 minB, Vector3 maxB)
        {
            return minA.x <= maxB.x && maxA.x >= minB.x && minA.y <= maxB.y && maxA.y >= minB.y && minA.z <= maxB.z && maxA.z >= minB.z;
        }

        Vector3Int Cell(Vector3 p)
        {
            return new Vector3Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));
        }

        public static Bounds SegmentBounds(Vector3 p, Vector3 q)
        {
            var bounds = new Bounds(p, Vector3.zero);
            bounds.Encapsulate(q);
            return bounds;
        }

        /// <summary>점들을 감싸는 범위. 점이 없으면 기본값.</summary>
        public static Bounds BoundsOf(IEnumerable<Vector3> points)
        {
            Bounds bounds = default;
            bool first = true;
            foreach (Vector3 p in points)
            {
                if (first) { bounds = new Bounds(p, Vector3.zero); first = false; }
                else bounds.Encapsulate(p);
            }
            return bounds;
        }
    }
}
