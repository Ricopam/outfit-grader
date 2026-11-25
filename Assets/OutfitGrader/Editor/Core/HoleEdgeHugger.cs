using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 귀·꼬리 구멍 가장자리를 부위 표면에 붙인다. 삼각형을 지워 낸 구멍은 메시 격자를 따라 각지고 부위보다 넓어(Lumelana 꼬리 둘레에 살이 톱니처럼 보임)
    /// 새로 생긴 구멍 가장자리 정점을 가장 가까운 부위 변 위의 점에서 margin만큼 떨어진 곳으로 당기고, 이웃 정점에 이동을 나눠 매끈하게 오므린다.
    /// </summary>
    public static class HoleEdgeHugger
    {
        /// <summary>당긴 이동을 이웃 고리에 나눠 주는 비율(가장자리가 혼자 끌려가 뾰족해지지 않게)</summary>
        static readonly float[] NeighborFalloff = { 0.5f, 0.25f };
        /// <summary>정점이 부위 변 위에 거의 그대로 있으면(이 거리 제곱 이하) 바깥 방향을 알 수 없어 당기지 않는다.</summary>
        const float OnEdgeSqr = 1e-12f;

        /// <summary>
        /// before(구멍 내기 전) 삼각형에는 없고 after(구멍 낸 뒤)에 새로 생긴 경계 변의 정점을 부위 변 쪽으로 당긴 이동량(루트 기준, 정점마다)을 돌려준다.
        /// positions는 루트 기준 정점 위치(무한대는 건너뜀), weldKeys는 이음매에서 갈라진 같은 정점을 묶는 키(메시 원래 위치).
        /// maxPull보다 멀리 당겨야 하는 정점은 maxPull까지만 당긴다.
        /// </summary>
        public static Vector3[] Pull(Vector3[] positions, Vector3[] weldKeys, int[] before, int[] after, IReadOnlyList<(Vector3 a, Vector3 b)> partEdges,
            float margin, float maxPull)
        {
            int[] groupOf = PenetrationFixer.WeldGroups(weldKeys, out int count);
            var oldBorder = BorderEdges(before, groupOf);
            var newEdges = BorderEdges(after, groupOf).Where(e => !oldBorder.Contains(e)).ToList();
            var groupPosition = new Vector3[count];
            for (int i = 0; i < positions.Length; i++) groupPosition[groupOf[i]] = positions[i];
            var nearestEdge = new NearestEdgeGrid(partEdges);
            var targets = new Dictionary<int, Vector3>();
            foreach (int g in newEdges.SelectMany(e => new[] { e.Item1, e.Item2 }).Distinct())
                if (TryHug(groupPosition[g], nearestEdge, margin, out Vector3 target)) targets[g] = target;
            SmoothLoop(targets, newEdges, nearestEdge, margin);
            var deltas = new Vector3[count];
            // 한도보다 멀면 한도까지만 당긴다(버리면 큰 삼각형을 지운 치비 옷에서 구멍이 꼬리보다 넓게 남아 틈으로 몸이 보임: inuinu 가장자리 81개 중 59개).
            foreach (var kv in targets) deltas[kv.Key] = Vector3.ClampMagnitude(kv.Value - groupPosition[kv.Key], maxPull);
            int[] groupTriangles = after.Select(v => groupOf[v]).ToArray();
            Vector3[] spread = PenetrationFixer.SpreadToNeighbors(deltas, groupTriangles, NeighborFalloff);
            return groupOf.Select(g => spread[g]).ToArray();
        }

        /// <summary>가장자리 고리 다듬기 횟수와 한 번에 이웃 평균 쪽으로 옮기는 비율</summary>
        public const int SmoothPasses = 3;
        public const float SmoothWeight = 0.5f;

        /// <summary>점 p를 가장 가까운 부위 변 위 점에서 margin 떨어진 곳으로. 위치를 모르거나 변 위에 그대로 있으면 false.</summary>
        static bool TryHug(Vector3 p, NearestEdgeGrid partEdges, float margin, out Vector3 target)
        {
            target = p;
            if (float.IsInfinity(p.x) || !partEdges.TryNearest(p, out Vector3 q)) return false;
            Vector3 away = p - q;
            if (away.sqrMagnitude <= OnEdgeSqr) return false;
            target = q + away.normalized * margin;
            return true;
        }

        /// <summary>
        /// 새 구멍 가장자리 고리를 다듬는다: 정점마다 가장자리 이웃(같은 경계 변으로 이어진 정점) 평균 쪽으로 SmoothWeight만큼 옮긴 뒤 다시 부위 표면에 붙인다.
        /// 메시 격자를 따라 하나씩 튀어나온 가장자리 정점이 펴져 구멍 테두리가 둥글어진다(Lumelana 꼬리 위쪽 작은 톱니).
        /// </summary>
        static void SmoothLoop(Dictionary<int, Vector3> targets, List<(int, int)> edges, NearestEdgeGrid partEdges, float margin)
        {
            var neighbors = new Dictionary<int, List<int>>();
            foreach (var (a, b) in edges.Where(e => targets.ContainsKey(e.Item1) && targets.ContainsKey(e.Item2)))
            {
                (neighbors.TryGetValue(a, out var la) ? la : neighbors[a] = new List<int>()).Add(b);
                (neighbors.TryGetValue(b, out var lb) ? lb : neighbors[b] = new List<int>()).Add(a);
            }
            for (int pass = 0; pass < SmoothPasses; pass++)
            {
                var next = new Dictionary<int, Vector3>(targets);
                foreach (var kv in neighbors)
                {
                    Vector3 average = kv.Value.Aggregate(Vector3.zero, (sum, n) => sum + targets[n]) / kv.Value.Count;
                    Vector3 moved = Vector3.Lerp(targets[kv.Key], average, SmoothWeight);
                    if (TryHug(moved, partEdges, margin, out Vector3 hugged)) next[kv.Key] = hugged;
                }
                foreach (var kv in next) targets[kv.Key] = kv.Value;
            }
        }

        /// <summary>한 삼각형에만 속한 변(묶음 번호 쌍, 작은 번호 먼저)</summary>
        static HashSet<(int, int)> BorderEdges(int[] triangles, int[] groupOf)
        {
            var count = new Dictionary<(int, int), int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = groupOf[triangles[t + k]], b = groupOf[triangles[t + (k + 1) % 3]];
                    if (a == b) continue;
                    var key = a < b ? (a, b) : (b, a);
                    count[key] = count.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            return new HashSet<(int, int)>(count.Where(kv => kv.Value == 1).Select(kv => kv.Key));
        }

        /// <summary>부위 변들 위에서 p에 가장 가까운 점. 변이 없으면 false.</summary>
        public static bool TryNearest(IReadOnlyList<(Vector3 a, Vector3 b)> edges, Vector3 p, out Vector3 nearest)
        {
            nearest = p;
            float best = float.MaxValue;
            foreach (var (a, b) in edges) Closer(a, b, p, ref best, ref nearest);
            return best < float.MaxValue;
        }

        /// <summary>선분 a-b 위 p에 가장 가까운 점이 지금까지보다 가까우면 바꾼다.</summary>
        static void Closer(Vector3 a, Vector3 b, Vector3 p, ref float best, ref Vector3 nearest)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
            Vector3 q = a + ab * t;
            float d = (p - q).sqrMagnitude;
            if (d >= best) return;
            best = d;
            nearest = q;
        }

        /// <summary>
        /// 가장 가까운 부위 변을 찾는 격자(Bentley 외의 격자 최근접 탐색). 변을 닿는 칸마다 담고, 점의 칸에서 고리를 한 겹씩 넓혀
        /// 지금까지 찾은 거리보다 가까운 변이 더 있을 수 없는 고리에서 멈춘다. 결과는 전체를 훑은 것과 같다.
        /// 꼬리 변 전체를 정점마다 훑어 inuinu 가장자리 붙이기가 약 0.14초 걸렸다(PRD AC8).
        /// </summary>
        sealed class NearestEdgeGrid
        {
            readonly IReadOnlyList<(Vector3 a, Vector3 b)> edges;
            readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
            readonly float cellSize;
            readonly int maxRing;
            readonly int[] lastQuery;
            int query;

            public NearestEdgeGrid(IReadOnlyList<(Vector3 a, Vector3 b)> edges)
            {
                this.edges = edges;
                lastQuery = new int[edges.Count];
                if (edges.Count == 0) return;
                // 칸은 변 평균 길이의 두 배(EdgeGrid와 같은 기준)
                cellSize = Mathf.Max(edges.Average(e => Vector3.Distance(e.a, e.b)) * 2f, EdgeGrid.MinEdgeLength);
                Bounds extent = EdgeGrid.BoundsOf(edges.SelectMany(e => new[] { e.a, e.b }));
                maxRing = Mathf.CeilToInt(extent.size.magnitude / cellSize) + 1;
                for (int i = 0; i < edges.Count; i++)
                {
                    Vector3Int min = Cell(Vector3.Min(edges[i].a, edges[i].b)), max = Cell(Vector3.Max(edges[i].a, edges[i].b));
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

            Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));

            public bool TryNearest(Vector3 p, out Vector3 nearest)
            {
                nearest = p;
                if (edges.Count == 0) return false;
                float best = float.MaxValue;
                query++;
                Vector3Int center = Cell(p);
                bool isSettled = false;
                for (int ring = 0; ring <= maxRing && !isSettled; ring++)
                {
                    VisitRing(center, ring, p, ref best, ref nearest);
                    // 고리 ring 바깥 칸의 점은 p에서 적어도 ring x 칸 크기만큼 떨어져 있다.
                    float reached = ring * cellSize;
                    isSettled = best < float.MaxValue && best <= reached * reached;
                }
                // 고리 상한 안에서 확정하지 못하면(점이 변 묶음에서 멀거나 대각선 방향) 전체를 훑어 같은 결과를 보장한다.
                if (!isSettled) return HoleEdgeHugger.TryNearest(edges, p, out nearest);
                return true;
            }

            void VisitRing(Vector3Int c, int r, Vector3 p, ref float best, ref Vector3 nearest)
            {
                for (int x = -r; x <= r; x++)
                for (int y = -r; y <= r; y++)
                for (int z = -r; z <= r; z++)
                {
                    bool isOnShell = Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) == r;
                    if (!isOnShell || !cells.TryGetValue(new Vector3Int(c.x + x, c.y + y, c.z + z), out List<int> list)) continue;
                    foreach (int i in list)
                    {
                        if (lastQuery[i] == query) continue;
                        lastQuery[i] = query;
                        Closer(edges[i].a, edges[i].b, p, ref best, ref nearest);
                    }
                }
            }
        }
    }
}
