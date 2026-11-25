using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 꼬리 둘레 경계선을 따라 구멍을 낸다. 삼각형을 통째로 지우면 구멍이 옷 메시 격자를 따라 각지므로(Lumelana 꼬리 위 계단 톱니),
    /// 옷 정점마다 꼬리 표면까지의 부호 있는 거리(안쪽은 음수)를 구해 이 값이 margin이 되는 선에서 삼각형을 쪼갠다.
    /// 안쪽 판정은 일반화 와인딩 수(Jacobson 외 2013, 열린 메시에도 동작)로 한다. 새 정점의 UV·법선·본 가중치·블렌드셰이프는 변 양 끝을 선형 보간한다.
    /// 꼬리가 실제로 뚫은 삼각형(EdgeGrid)에서 이어진 영역만 자른다(옷 위에 얹혀 있기만 한 꼬리 옆을 잘못 뚫지 않게).
    /// 구멍이 기존보다 크게 벌어지면 null을 돌려 호출부가 기존 방식(삼각형 삭제 + 가장자리 붙이기)을 쓰게 한다.
    /// </summary>
    public static class ContourHoleCutter
    {
        /// <summary>뚫린 삼각형에서 몇 고리 이웃까지 자를 수 있는지. 구멍은 기존(뚫린 삼각형 삭제)보다 이만큼만 넓어질 수 있다
        /// (inuinu처럼 옷이 짧은 꼬리를 덮고 있으면 꼬리 근처 옷 전체가 margin 안에 들어 넓게 잘려 나감).</summary>
        const int MaxRings = 1;

        /// <summary>새 구멍 넓이가 기존(뚫린 삼각형 삭제) 넓이의 이 배수를 넘으면 쓰지 않는다(옷이 성겨 구멍이 크게 벌어지는 경우).</summary>
        const float MaxAreaRatio = 2f;

        /// <summary>경계선 위치를 다시 재서 맞추는 횟수. 변 양 끝 값만으로 직선 보간하면 둥근 꼬리 둘레에서 경계점이 안쪽으로 최대 6mm 들어갔다(테스트 격자).</summary>
        const int RefineSteps = 4;

        /// <summary>양 끝 거리로 경계가 갈리지 않을 때(지우는 끝이 꼬리 깊은 안쪽) 경계를 찾으려고 변을 나누는 칸 수</summary>
        const int ScanSteps = 8;

        /// <summary>와인딩 수가 이 값보다 크면 꼬리 안쪽</summary>
        const float InsideWinding = 0.5f;

        /// <summary>
        /// 경계선을 따라 자른 새 메시. 자를 곳이 없으면 null. changed는 지우거나 쪼갠 삼각형 수.
        /// partEdges는 AppendageHoleCutter가 삼각형마다 (a,b),(b,c),(c,a) 순서로 넣은 꼬리 변이다(세 개씩 묶어 삼각형으로 되돌린다).
        /// </summary>
        public static Mesh Cut(Mesh source, Vector3[] inRoot, IReadOnlyList<(Vector3 a, Vector3 b)> partEdges, EdgeGrid grid, float margin,
            string meshName, out int changed)
        {
            changed = 0;
            List<(Vector3 a, Vector3 b, Vector3 c)> tail = Enumerable.Range(0, partEdges.Count / 3)
                .Select(k => (partEdges[3 * k].a, partEdges[3 * k + 1].a, partEdges[3 * k + 2].a))
                .Where(t => Vector3.Cross(t.Item2 - t.Item1, t.Item3 - t.Item1).sqrMagnitude > 1e-16f).ToList();
            if (tail.Count == 0) return null;
            // 정점당 본 가중치가 4개를 넘으면 새 메시에 옮길 때 줄어들어 구멍과 무관한 부위 변형이 바뀌므로 기존 방식에 맡긴다.
            bool hasManyWeights = source.GetBonesPerVertex().Any(n => n > 4);
            if (hasManyWeights) return null;

            int[] all = PenetrationFixer.TriangleIndices(source);
            bool[] isPierced = new bool[all.Length / 3];
            for (int t = 0; t < isPierced.Length; t++)
                isPierced[t] = grid.Pierces(inRoot[all[3 * t]], inRoot[all[3 * t + 1]], inRoot[all[3 * t + 2]]);
            if (!isPierced.Any(x => x)) return null;
            int[] groupOf = PenetrationFixer.WeldGroups(source.vertices, out int groupCount);
            List<int>[] trianglesOf = TrianglesOfGroup(all, groupOf, groupCount);
            // 거리는 자를 수 있는 정점(뚫린 삼각형과 MaxRings 고리 이웃의 정점)만 잰다. 꼬리 근처 옷 전체를 재면 맞추기가 1.4초 늘었다.
            bool[] isCandidate = RingVertices(all, isPierced, groupOf, trianglesOf, out bool[] isNearTriangle);
            var surface = new TailSurface(tail, FocusOf(inRoot, all, isNearTriangle, margin));
            float[] field = SignedField(inRoot, surface, margin, isCandidate);
            bool[] isDropped = KeepOnlyPiercedRegions(field, all, isPierced, groupOf, trianglesOf, margin);

            // 넓이 판정은 직선 보간 경계점으로 먼저 하고(값싼 첫 판), 통과할 때만 경계점을 다시 재서 자른다(inuinu처럼 결국 쓰지 않는 경우에 0.6~1초 들었음).
            MeshBuilder rough = ClipAll(source, inRoot, groupOf, surface, margin, field, isDropped, refine: false, out changed);
            if (changed == 0) return null;
            float oldArea = Enumerable.Range(0, isPierced.Length).Where(t => isPierced[t]).Sum(t => Area(inRoot[all[3 * t]], inRoot[all[3 * t + 1]], inRoot[all[3 * t + 2]]));
            if (rough.RemovedArea > oldArea * MaxAreaRatio) return null;
            MeshBuilder fine = ClipAll(source, inRoot, groupOf, surface, margin, field, isDropped, refine: true, out changed);
            if (fine.RemovedArea > oldArea * MaxAreaRatio) return null;
            Mesh mesh = fine.Build();
            mesh.name = meshName;
            return mesh;
        }

        /// <summary>모든 삼각형을 field ≥ 0 쪽만 남게 자른 결과. refine이면 경계점을 실제 거리로 다시 재서 놓는다.</summary>
        static MeshBuilder ClipAll(Mesh source, Vector3[] inRoot, int[] groupOf, TailSurface surface, float margin, float[] field, bool[] isDropped, bool refine,
            out int changed)
        {
            var builder = new MeshBuilder(source, inRoot, groupOf, surface, margin, refine);
            changed = 0;
            int index = 0;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                if (source.GetTopology(s) != MeshTopology.Triangles) continue;
                int[] tris = source.GetTriangles(s);
                var kept = new List<int>(tris.Length);
                for (int t = 0; t + 2 < tris.Length; t += 3, index++)
                {
                    if (isDropped[index])
                    {
                        builder.RemovedArea += builder.Area(tris[t], tris[t + 1], tris[t + 2]);
                        changed++;
                        continue;
                    }
                    changed += Clip(tris[t], tris[t + 1], tris[t + 2], field, builder, kept) ? 1 : 0;
                }
                builder.SetTriangles(s, kept);
            }
            return builder;
        }

        /// <summary>
        /// 정점마다 꼬리 표면까지의 부호 있는 거리 - margin(TailSurface.Field). 후보가 아니거나 꼬리 근처가 아니거나 위치를 모르면 +무한대.
        /// isCandidate가 null이면 모든 정점을 잰다.
        /// </summary>
        static float[] SignedField(Vector3[] positions, TailSurface surface, float margin, bool[] isCandidate)
        {
            Bounds near = surface.Extent;
            near.Expand(margin * 4f);
            var field = new float[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                Vector3 p = positions[i];
                bool isNear = (isCandidate == null || isCandidate[i]) && !float.IsInfinity(p.x) && near.Contains(p);
                field[i] = isNear ? surface.Field(p, margin) : float.PositiveInfinity;
            }
            return field;
        }

        /// <summary>
        /// 자를 수 있는 삼각형(뚫린 삼각형과 그 고리 이웃)을 감싸는 상자를 margin x FocusMargins만큼 넓힌 것. 거리 계산은 이 상자에 닿는 꼬리 삼각형만 본다.
        /// 상자 밖 꼬리 면은 상자 안 점에서 이 거리보다 멀어 경계선(margin)의 판정을 바꾸지 않는다.
        /// </summary>
        static Bounds FocusOf(Vector3[] inRoot, int[] all, bool[] isNearTriangle, float margin)
        {
            var points = new List<Vector3>();
            for (int t = 0; t < isNearTriangle.Length; t++)
                if (isNearTriangle[t]) for (int k = 0; k < 3; k++) if (!float.IsInfinity(inRoot[all[3 * t + k]].x)) points.Add(inRoot[all[3 * t + k]]);
            Bounds focus = EdgeGrid.BoundsOf(points);
            focus.Expand(margin * FocusMargins * 2f);
            return focus;
        }

        /// <summary>거리 계산에 쓰는 꼬리 삼각형을 고를 때 자를 영역 상자를 넓히는 정도(margin 배수)</summary>
        const float FocusMargins = 4f;

        /// <summary>
        /// 꼬리 삼각형. 거리는 자를 영역 근처 삼각형(near)만, 안팎 확인(와인딩 수)은 전체로 한다(Lumelana 꼬리 4,414개를 정점마다 모두 훑어 맞추기가 0.5초 늘었음).
        /// </summary>
        sealed class TailSurface
        {
            readonly List<(Vector3 a, Vector3 b, Vector3 c)> triangles, near;
            readonly Vector3[] min, max;
            public readonly Bounds Extent;
            /// <summary>
            /// 근처 삼각형을 닿는 칸에 담은 격자(Bentley 외의 격자 최근접 탐색). 점의 칸에서 고리를 넓히다 더 가까운 면이 있을 수 없으면 멈춘다.
            /// 근처 삼각형 1,000~3,000개를 점마다 모두 확인해 호출당 10~70ms 걸렸다(PRD AC8). 결과는 모두 확인한 것과 같다.
            /// </summary>
            readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
            readonly float cellSize;
            readonly int maxRing;
            readonly int[] lastQuery;
            int query;

            public TailSurface(List<(Vector3 a, Vector3 b, Vector3 c)> triangles, Bounds focus)
            {
                this.triangles = triangles;
                near = triangles.Where(t => EdgeGrid.BoundsOf(new[] { t.a, t.b, t.c }).Intersects(focus)).ToList();
                if (near.Count == 0) near = triangles;
                min = near.Select(t => Vector3.Min(t.a, Vector3.Min(t.b, t.c))).ToArray();
                max = near.Select(t => Vector3.Max(t.a, Vector3.Max(t.b, t.c))).ToArray();
                Extent = EdgeGrid.BoundsOf(triangles.SelectMany(t => new[] { t.a, t.b, t.c }));
                lastQuery = new int[near.Count];
                // 칸은 근처 삼각형 상자 대각선 평균의 두 배
                cellSize = Mathf.Max(Enumerable.Range(0, near.Count).Average(i => (max[i] - min[i]).magnitude) * 2f, EdgeGrid.MinEdgeLength);
                Bounds nearExtent = EdgeGrid.BoundsOf(near.SelectMany(t => new[] { t.a, t.b, t.c }));
                maxRing = Mathf.CeilToInt(nearExtent.size.magnitude / cellSize) + 1;
                for (int i = 0; i < near.Count; i++)
                {
                    Vector3Int lo = Cell(min[i]), hi = Cell(max[i]);
                    for (int x = lo.x; x <= hi.x; x++)
                    for (int y = lo.y; y <= hi.y; y++)
                    for (int z = lo.z; z <= hi.z; z++)
                    {
                        var key = new Vector3Int(x, y, z);
                        if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>();
                        list.Add(i);
                    }
                }
            }

            Vector3Int Cell(Vector3 p) => new Vector3Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));

            /// <summary>꼬리 표면까지 거리</summary>
            public float Distance(Vector3 p) => Distance(p, out _, out _);

            /// <summary>꼬리 표면까지 거리와, 가장 가까운 면이 바깥(앞면 방향)에서 본 점인지</summary>
            /// <summary>꼬리 표면까지 거리, 가장 가까운 면 앞쪽(바깥)인지, 가장 가까운 점이 면 안쪽(모서리·꼭짓점이 아님)인지</summary>
            float Distance(Vector3 p, out bool isInFrontOfNearest, out bool isOnFaceInterior)
            {
                float best = float.MaxValue;
                isInFrontOfNearest = true;
                isOnFaceInterior = false;
                int nearestIndex = -1;
                Vector3 nearestPoint = p;
                query++;
                Vector3Int center = Cell(p);
                bool isSettled = false;
                for (int ring = 0; ring <= maxRing && !isSettled; ring++)
                {
                    for (int x = -ring; x <= ring; x++)
                    for (int y = -ring; y <= ring; y++)
                    for (int z = -ring; z <= ring; z++)
                    {
                        bool isOnShell = Mathf.Max(Mathf.Abs(x), Mathf.Max(Mathf.Abs(y), Mathf.Abs(z))) == ring;
                        if (!isOnShell || !cells.TryGetValue(new Vector3Int(center.x + x, center.y + y, center.z + z), out List<int> list)) continue;
                        foreach (int i in list)
                        {
                            if (lastQuery[i] == query) continue;
                            lastQuery[i] = query;
                            Vector3 outside = Vector3.Max(min[i] - p, Vector3.Max(Vector3.zero, p - max[i]));
                            if (outside.sqrMagnitude >= best) continue;
                            var (a, b, c) = near[i];
                            Vector3 closest = ClosestOnTriangle(p, a, b, c);
                            float d = (closest - p).sqrMagnitude;
                            if (d >= best) continue;
                            best = d;
                            nearestIndex = i;
                            nearestPoint = closest;
                        }
                    }
                    // 고리 ring 바깥 칸의 점은 p에서 적어도 ring x 칸 크기만큼 떨어져 있다.
                    float reached = ring * cellSize;
                    isSettled = nearestIndex >= 0 && best <= reached * reached;
                }
                // 고리 상한 안에서 확정하지 못하면(점이 근처 삼각형 묶음에서 멀거나 대각선 방향) 모두 확인해 같은 결과를 보장한다.
                if (!isSettled)
                {
                    best = float.MaxValue;
                    nearestIndex = -1;
                    for (int i = 0; i < near.Count; i++)
                    {
                        var (a, b, c) = near[i];
                        Vector3 closest = ClosestOnTriangle(p, a, b, c);
                        float d = (closest - p).sqrMagnitude;
                        if (d >= best) continue;
                        best = d;
                        nearestIndex = i;
                        nearestPoint = closest;
                    }
                }
                if (nearestIndex < 0) return float.MaxValue;
                var (na, nb, nc) = near[nearestIndex];
                isInFrontOfNearest = Vector3.Dot(p - nearestPoint, Vector3.Cross(nb - na, nc - na)) >= 0f;
                isOnFaceInterior = IsInterior(nearestPoint, na, nb, nc);
                return Mathf.Sqrt(best);
            }

            /// <summary>일반화 와인딩 수로 본 꼬리 안쪽 여부</summary>
            public bool IsInside(Vector3 p)
            {
                float winding = 0f;
                foreach (var (a, b, c) in triangles) winding += SolidAngle(p, a, b, c);
                return Mathf.Abs(winding) / (4f * Mathf.PI) > InsideWinding;
            }

            /// <summary>
            /// 꼬리 바깥이면 (거리 - margin), 안쪽이면 (-거리 - margin). 표면에서 margin 안쪽이면 안팎과 상관없이 음수(지울 쪽)라
            /// 와인딩 수를 세지 않고 (거리 - margin)을 돌려준다(부호만 판정에 쓰고 크기는 경계점 보간의 시작값으로만 씀).
            /// 가장 가까운 꼬리 면의 앞쪽(바깥)에 있는 점은 바깥으로 보고, 뒤쪽이라고 나온 드문 점만 와인딩 수로 확인한다
            /// (구멍 난 메시·뒤집힌 면에서 면 방향만 믿지 않게).
            /// </summary>
            public float Field(Vector3 p, float margin)
            {
                float distance = Distance(p, out bool isInFront, out bool isOnFaceInterior);
                if (distance < margin) return distance - margin;
                // 가장 가까운 점이 면 안쪽이면 그 면 법선만으로 안팎이 정해진다(Bærentzen·Aanæs 2005). 모서리·꼭짓점일 때만 와인딩 수로 확인한다.
                bool isInside = isOnFaceInterior ? !isInFront : !isInFront && IsInside(p);
                return isInside ? -distance - margin : distance - margin;
            }
        }

        /// <summary>
        /// 음수(잘라낼) 정점 중 꼬리가 실제로 뚫은 삼각형에서 음수 삼각형으로 이어진 영역만 남기고, 나머지 음수는 margin(남김)으로 바꾼다.
        /// 뚫렸는데 음수 정점이 없는 삼각형(꼬리보다 큰 삼각형)은 기존처럼 통째로 지우도록 표시해 돌려준다(삼각형 순서는 TriangleIndices와 같음).
        /// </summary>
        static bool[] KeepOnlyPiercedRegions(float[] field, int[] all, bool[] isPierced, int[] groupOf, List<int>[] trianglesOf, float margin)
        {
            int groupCount = trianglesOf.Length;
            bool HasNegative(int t) => field[all[3 * t]] < 0f || field[all[3 * t + 1]] < 0f || field[all[3 * t + 2]] < 0f;

            var inRegion = new bool[isPierced.Length];
            var front = new List<int>();
            for (int t = 0; t < isPierced.Length; t++)
            {
                if (!isPierced[t]) continue;
                inRegion[t] = true;
                front.Add(t);
            }
            for (int ring = 0; ring < MaxRings; ring++)
            {
                var next = new List<int>();
                foreach (int t in front)
                    for (int k = 0; k < 3; k++)
                        foreach (int n in trianglesOf[groupOf[all[3 * t + k]]])
                        {
                            if (inRegion[n] || !HasNegative(n)) continue;
                            inRegion[n] = true;
                            next.Add(n);
                        }
                front = next;
            }
            var isCutGroup = new bool[groupCount];
            var isDropped = new bool[inRegion.Length];
            for (int t = 0; t < inRegion.Length; t++)
            {
                if (!inRegion[t]) continue;
                // 꼭짓점 값은 바꾸지 않는다: 바꾸면 밖에 있는 꼭짓점이 지울 쪽이 되어 이웃 변이 엉뚱한 곳(테스트에서 경계선 6mm 안쪽)에서 잘린다.
                isDropped[t] = isPierced[t] && !HasNegative(t);
                for (int k = 0; k < 3; k++)
                {
                    int v = all[3 * t + k];
                    if (field[v] < 0f) isCutGroup[groupOf[v]] = true;
                }
            }
            for (int v = 0; v < field.Length; v++)
            {
                bool isCut = isCutGroup[groupOf[v]];
                if (isCut && field[v] >= 0f) field[v] = -margin;
                else if (!isCut && field[v] < 0f) field[v] = margin;
            }
            return isDropped;
        }

        /// <summary>정점 묶음마다 그 묶음 정점을 쓰는 삼각형 번호</summary>
        static List<int>[] TrianglesOfGroup(int[] all, int[] groupOf, int groupCount)
        {
            var trianglesOf = new List<int>[groupCount];
            for (int t = 0; t < all.Length / 3; t++)
                for (int k = 0; k < 3; k++) (trianglesOf[groupOf[all[3 * t + k]]] ??= new List<int>()).Add(t);
            return trianglesOf;
        }

        /// <summary>뚫린 삼각형과 MaxRings 고리 이웃 삼각형에 속한 정점(묶음 단위로 펼침)</summary>
        static bool[] RingVertices(int[] all, bool[] isPierced, int[] groupOf, List<int>[] trianglesOf, out bool[] isNearTriangle)
        {
            isNearTriangle = (bool[])isPierced.Clone();
            var front = Enumerable.Range(0, isPierced.Length).Where(t => isPierced[t]).ToList();
            for (int ring = 0; ring < MaxRings; ring++)
            {
                var next = new List<int>();
                foreach (int t in front)
                    for (int k = 0; k < 3; k++)
                        foreach (int n in trianglesOf[groupOf[all[3 * t + k]]])
                        {
                            if (isNearTriangle[n]) continue;
                            isNearTriangle[n] = true;
                            next.Add(n);
                        }
                front = next;
            }
            var isGroup = new bool[trianglesOf.Length];
            for (int t = 0; t < isNearTriangle.Length; t++)
                if (isNearTriangle[t]) for (int k = 0; k < 3; k++) isGroup[groupOf[all[3 * t + k]]] = true;
            return groupOf.Select(g => isGroup[g]).ToArray();
        }

        /// <summary>삼각형을 field ≥ 0 쪽만 남게 자른다. 지우거나 쪼갰으면 true.</summary>
        static bool Clip(int a, int b, int c, float[] field, MeshBuilder builder, List<int> kept)
        {
            int[] v = { a, b, c };
            bool[] isKept = v.Select(i => field[i] >= 0f).ToArray();
            int keptCount = isKept.Count(x => x);
            if (keptCount == 3) { kept.AddRange(v); return false; }
            builder.RemovedArea += builder.Area(a, b, c);
            if (keptCount == 0) return true;
            // 회전해 감기 방향을 유지한 채 남는 정점(1개일 때)·지우는 정점(2개 남을 때)을 맨 앞에 둔다.
            int lone = keptCount == 1 ? System.Array.IndexOf(isKept, true) : System.Array.IndexOf(isKept, false);
            int p = v[lone], q = v[(lone + 1) % 3], r = v[(lone + 2) % 3];
            // Split은 늘 (남는 정점, 지우는 정점) 순서로 부른다(경계점 찾기가 남는 쪽 = 꼬리 바깥에서 출발해야 함).
            int pq = keptCount == 1 ? builder.Split(p, q, field) : builder.Split(q, p, field);
            int pr = keptCount == 1 ? builder.Split(p, r, field) : builder.Split(r, p, field);
            if (keptCount == 1) kept.AddRange(new[] { p, pq, pr });
            else kept.AddRange(new[] { pq, q, r, pq, r, pr });
            builder.RemovedArea -= keptCount == 1 ? builder.Area(p, pq, pr) : builder.Area(pq, q, r) + builder.Area(pq, r, pr);
            return true;
        }

        static float Area(Vector3 a, Vector3 b, Vector3 c) => float.IsInfinity(a.x + b.x + c.x) ? 0f : Vector3.Cross(b - a, c - a).magnitude * 0.5f;

        /// <summary>삼각형 위의 점 q가 모서리·꼭짓점이 아닌 면 안쪽인지(무게중심 좌표가 모두 InteriorBarycentric보다 큼)</summary>
        static bool IsInterior(Vector3 q, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            float area = n.sqrMagnitude;
            if (area <= 0f) return false;
            float u = Vector3.Dot(Vector3.Cross(c - b, q - b), n) / area;
            float v = Vector3.Dot(Vector3.Cross(a - c, q - c), n) / area;
            return u > InteriorBarycentric && v > InteriorBarycentric && 1f - u - v > InteriorBarycentric;
        }

        /// <summary>면 안쪽으로 볼 무게중심 좌표 하한(이보다 작으면 모서리·꼭짓점으로 보고 와인딩 수로 확인)</summary>
        const float InteriorBarycentric = 1e-3f;

        /// <summary>점 p에서 본 삼각형의 부호 있는 입체각(Van Oosterom–Strackee)</summary>
        static float SolidAngle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 va = a - p, vb = b - p, vc = c - p;
            float la = va.magnitude, lb = vb.magnitude, lc = vc.magnitude;
            float numerator = Vector3.Dot(va, Vector3.Cross(vb, vc));
            float denominator = la * lb * lc + Vector3.Dot(va, vb) * lc + Vector3.Dot(vb, vc) * la + Vector3.Dot(vc, va) * lb;
            return 2f * Mathf.Atan2(numerator, denominator);
        }

        /// <summary>삼각형 위 p에 가장 가까운 점(Ericson, Real-Time Collision Detection 5.1.5)</summary>
        public static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float denom = 1f / (va + vb + vc);
            return a + ab * (vb * denom) + ac * (vc * denom);
        }

        /// <summary>원본 메시 속성을 복사해 두고, 변 위에 보간한 새 정점을 덧붙여 새 메시를 만든다.</summary>
        sealed class MeshBuilder
        {
            readonly Mesh source;
            readonly List<Vector3> vertices, normals;
            readonly List<Vector4> tangents;
            readonly List<Color> colors;
            readonly List<Vector4>[] uvs = new List<Vector4>[8];
            readonly List<BoneWeight> weights;
            readonly List<(int a, int b, float t)> added = new List<(int, int, float)>();
            readonly Dictionary<(int, int), int> splitIndex = new Dictionary<(int, int), int>();
            /// <summary>
            /// 정점 묶음 쌍(남는 쪽, 지우는 쪽)마다 경계점 비율. UV 이음선 양쪽 삼각형은 다른 정점 번호로 같은 변을 쓰므로
            /// 묶음 기준으로 한 번만 재서 같은 위치에 놓는다(따로 재면 이음선에 틈이 생김).
            /// </summary>
            readonly Dictionary<(int, int), float> ratioOfGroups = new Dictionary<(int, int), float>();
            readonly int[] groupOf;
            /// <summary>UV 채널마다 원본 차원(2~4). 다시 쓸 때 같은 차원으로 써서 정점 버퍼가 커지지 않게 한다.</summary>
            readonly int[] uvDimensions = new int[8];
            readonly List<int>[] subTriangles;
            /// <summary>정점 위치(루트 기준). 새 정점도 같은 비율로 보간해 덧붙인다(지운 넓이 계산용).</summary>
            readonly List<Vector3> rootPositions;
            readonly TailSurface surface;
            readonly float margin;
            readonly bool refine;
            /// <summary>지운 넓이(루트 기준)</summary>
            public float RemovedArea;

            public float Area(int a, int b, int c) => ContourHoleCutter.Area(rootPositions[a], rootPositions[b], rootPositions[c]);

            public MeshBuilder(Mesh source, Vector3[] inRoot, int[] groupOf, TailSurface surface, float margin, bool refine)
            {
                this.source = source;
                this.groupOf = groupOf;
                this.surface = surface;
                this.margin = margin;
                this.refine = refine;
                rootPositions = inRoot.ToList();
                vertices = source.vertices.ToList();
                normals = source.normals.ToList();
                tangents = source.tangents.ToList();
                colors = source.colors.ToList();
                for (int ch = 0; ch < 8; ch++)
                {
                    var list = new List<Vector4>();
                    source.GetUVs(ch, list);
                    uvs[ch] = list;
                    var attribute = UnityEngine.Rendering.VertexAttribute.TexCoord0 + ch;
                    uvDimensions[ch] = source.HasVertexAttribute(attribute) ? source.GetVertexAttributeDimension(attribute) : 0;
                }
                weights = source.boneWeights.ToList();
                subTriangles = new List<int>[source.subMeshCount];
            }

            /// <summary>변 a(남는 쪽)-b(지우는 쪽) 위 field가 0인 점에 새 정점을 만든다(같은 변은 한 번만). a는 반드시 남는 쪽이다.</summary>
            public int Split(int a, int b, float[] field)
            {
                var key = a < b ? (a, b) : (b, a);
                if (splitIndex.TryGetValue(key, out int index)) return index;
                // 거리를 무한대로 둔 끝점(자르는 정점은 모두 후보라, 꼬리 근처 범위 밖 = 꼬리 바깥인 긴 변의 먼 끝)은 여기서 거리만 잰다.
                bool isKnown = !float.IsInfinity(rootPositions[a].x) && !float.IsInfinity(rootPositions[b].x);
                float fa = float.IsInfinity(field[a]) && isKnown ? surface.Distance(rootPositions[a]) - margin : field[a];
                float fb = float.IsInfinity(field[b]) && isKnown ? surface.Distance(rootPositions[b]) - margin : field[b];
                var groups = (groupOf[a], groupOf[b]);
                if (!ratioOfGroups.TryGetValue(groups, out float t))
                {
                    t = !isKnown ? 0.5f : refine ? Refine(rootPositions[a], rootPositions[b], fa, fb) : Mathf.Clamp01(fa / (fa - fb));
                    ratioOfGroups[groups] = t;
                }
                index = vertices.Count;
                vertices.Add(Vector3.Lerp(vertices[a], vertices[b], t));
                rootPositions.Add(isKnown ? Vector3.Lerp(rootPositions[a], rootPositions[b], t) : Vector3.positiveInfinity);
                if (normals.Count > 0) normals.Add(Vector3.Lerp(normals[a], normals[b], t).normalized);
                if (tangents.Count > 0) tangents.Add(LerpTangent(tangents[a], tangents[b], t));
                if (colors.Count > 0) colors.Add(Color.Lerp(colors[a], colors[b], t));
                foreach (List<Vector4> uv in uvs.Where(u => u.Count > 0)) uv.Add(Vector4.Lerp(uv[a], uv[b], t));
                if (weights.Count > 0) weights.Add(LerpWeights(weights[a], weights[b], t));
                added.Add((a, b, t));
                splitIndex[key] = index;
                return index;
            }

            /// <summary>
            /// 변 a-b에서 거리가 0이 되는 비율. 양 끝 값(영역 정리로 바뀌었을 수 있음)의 직선 보간에서 시작해, 실제 값의 부호가 양 끝에서 갈리면
            /// 거짓 위치법(Illinois)으로 RefineSteps번 좁힌다. 경계선은 늘 꼬리 바깥 margin 거리에 있으므로 와인딩 수 없이 (거리 - margin)만 쓴다.
            /// </summary>
            float Refine(Vector3 pa, Vector3 pb, float fa, float fb)
            {
                float linear = Mathf.Clamp01(fa / (fa - fb));
                float Gap(Vector3 p) => surface.Distance(p) - margin;
                float lo = 0f, hi = 1f;
                float ga = Gap(pa), gb = Gap(pb);
                if ((ga >= 0f) == (gb >= 0f))
                {
                    // 남는 끝 a는 꼬리 바깥이고 지우는 끝 b가 꼬리 깊은 안쪽이면, 경계선은 a에서 꼬리 표면으로 가는 사이에 있다.
                    // 와인딩 수(꼬리 삼각형 전체) 대신 거리만으로 변을 ScanSteps칸 훑어 처음 margin 안에 드는 칸을 찾는다.
                    for (int k = 1; k < ScanSteps; k++)
                    {
                        float t = (float)k / ScanSteps;
                        float g = Gap(Vector3.Lerp(pa, pb, t));
                        if ((g >= 0f) == (ga >= 0f)) { lo = t; ga = g; continue; }
                        hi = t;
                        gb = g;
                        break;
                    }
                }
                bool isBracketed = (ga >= 0f) != (gb >= 0f);
                if (!isBracketed) return linear;
                int side = 0;
                for (int step = 0; step < RefineSteps; step++)
                {
                    float t = Mathf.Clamp01(lo + (hi - lo) * ga / (ga - gb));
                    float g = Gap(Vector3.Lerp(pa, pb, t));
                    bool isSameAsA = (g >= 0f) == (ga >= 0f);
                    if (isSameAsA) { lo = t; ga = g; if (side == -1) gb *= 0.5f; side = -1; }
                    else { hi = t; gb = g; if (side == 1) ga *= 0.5f; side = 1; }
                }
                return Mathf.Clamp01(lo + (hi - lo) * ga / (ga - gb));
            }

            public void SetTriangles(int subMesh, List<int> triangles) => subTriangles[subMesh] = triangles;

            public Mesh Build()
            {
                Mesh mesh = Object.Instantiate(source);
                mesh.ClearBlendShapes();
                if (vertices.Count > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.SetVertices(vertices);
                if (normals.Count > 0) mesh.SetNormals(normals);
                if (tangents.Count > 0) mesh.SetTangents(tangents);
                if (colors.Count > 0) mesh.SetColors(colors);
                for (int ch = 0; ch < 8; ch++) if (uvs[ch].Count > 0) SetUVs(mesh, ch);
                if (weights.Count > 0) mesh.boneWeights = weights.ToArray();
                for (int s = 0; s < subTriangles.Length; s++)
                    if (subTriangles[s] != null) mesh.SetTriangles(subTriangles[s], s);
                CopyBlendShapes(mesh);
                mesh.RecalculateBounds();
                return mesh;
            }

            /// <summary>UV 채널을 원본 차원 그대로 쓴다.</summary>
            void SetUVs(Mesh mesh, int ch)
            {
                switch (uvDimensions[ch])
                {
                    case 2: mesh.SetUVs(ch, uvs[ch].Select(u => (Vector2)u).ToList()); break;
                    case 3: mesh.SetUVs(ch, uvs[ch].Select(u => (Vector3)u).ToList()); break;
                    default: mesh.SetUVs(ch, uvs[ch]); break;
                }
            }

            /// <summary>원본 블렌드셰이프를 프레임마다 읽어 새 정점 몫을 보간해 덧붙인다(메시를 실제로 만들 때만 읽음).</summary>
            void CopyBlendShapes(Mesh mesh)
            {
                int n = source.vertexCount, total = vertices.Count;
                var dv = new Vector3[n]; var dn = new Vector3[n]; var dt = new Vector3[n];
                var ov = new Vector3[total]; var on = new Vector3[total]; var ot = new Vector3[total];
                for (int s = 0; s < source.blendShapeCount; s++)
                    for (int f = 0; f < source.GetBlendShapeFrameCount(s); f++)
                    {
                        source.GetBlendShapeFrameVertices(s, f, dv, dn, dt);
                        System.Array.Copy(dv, ov, n); System.Array.Copy(dn, on, n); System.Array.Copy(dt, ot, n);
                        for (int k = 0; k < added.Count; k++)
                        {
                            var (a, b, t) = added[k];
                            ov[n + k] = Vector3.Lerp(dv[a], dv[b], t);
                            on[n + k] = Vector3.Lerp(dn[a], dn[b], t);
                            ot[n + k] = Vector3.Lerp(dt[a], dt[b], t);
                        }
                        mesh.AddBlendShapeFrame(source.GetBlendShapeName(s), source.GetBlendShapeFrameWeight(s, f), ov, on, ot);
                    }
            }

            static Vector4 LerpTangent(Vector4 a, Vector4 b, float t)
            {
                Vector3 dir = Vector3.Lerp(a, b, t).normalized;
                return new Vector4(dir.x, dir.y, dir.z, a.w);
            }

            /// <summary>두 정점의 본 가중치를 섞어 큰 4개만 남기고 합이 1이 되게 맞춘다.</summary>
            static BoneWeight LerpWeights(BoneWeight a, BoneWeight b, float t)
            {
                var sum = new Dictionary<int, float>();
                void Add(int bone, float w) { if (w > 0f) sum[bone] = (sum.TryGetValue(bone, out float x) ? x : 0f) + w; }
                Add(a.boneIndex0, a.weight0 * (1f - t)); Add(a.boneIndex1, a.weight1 * (1f - t)); Add(a.boneIndex2, a.weight2 * (1f - t)); Add(a.boneIndex3, a.weight3 * (1f - t));
                Add(b.boneIndex0, b.weight0 * t); Add(b.boneIndex1, b.weight1 * t); Add(b.boneIndex2, b.weight2 * t); Add(b.boneIndex3, b.weight3 * t);
                var top = sum.OrderByDescending(kv => kv.Value).Take(4).ToList();
                float total = top.Sum(kv => kv.Value);
                if (total <= 0f) return a;
                (int bone, float w) At(int i) => i < top.Count ? (top[i].Key, top[i].Value / total) : (0, 0f);
                return new BoneWeight
                {
                    boneIndex0 = At(0).bone, weight0 = At(0).w, boneIndex1 = At(1).bone, weight1 = At(1).w,
                    boneIndex2 = At(2).bone, weight2 = At(2).w, boneIndex3 = At(3).bone, weight3 = At(3).w,
                };
            }
        }
    }
}
