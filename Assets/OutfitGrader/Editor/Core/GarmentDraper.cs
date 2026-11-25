using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 풀어 준 상의 몸통 천을 중력으로 늘어뜨린다(풀어 주기 옵션의 마지막 단계, PRD 0장 형태 보존).
    /// 몸통이 아주 짧은 아바타는 남는 천이 옆구리에서 가로로 겹쳐 안감이 층처럼 드러난다. 몸통 위쪽(TopPin 위)만 지금 위치에 고정하고
    /// 아래쪽을 위치 기반 반복(변은 지금 길이, 접힘은 반쯤 폄, 몸 밖 유지, 천끼리 겹침 방지)으로 흘러내리게 하면 층이 세로로 매끈하게 펴진다.
    /// 남는 천이 아래로 빠지므로 아랫단이 길어진다(실측 inuinu 51mm, 엉덩이 아래 약 10cm).
    /// </summary>
    public static class GarmentDraper
    {
        public const int Steps = 60, Iterations = 8;
        /// <summary>
        /// 늘인 뒤 몸통 한쪽 눌림(굵기 배율 / 길이 배율)이 이 값 이상일 때만 늘어뜨린다. 몸통 비율이 원본에 가까우면 겹칠 남는 천이 없고
        /// 늘어뜨리면 오히려 앞판 아래가 벌어진다(실측 Lumelana 1.06, inuinu 2.89. 두 표본 사이 값은 미검증).
        /// </summary>
        public const float MinTorsoSquash = 2f;
        /// <summary>엉덩이(0)~목(1) 높이 중 이 값보다 위인 몸통 정점은 고정한다(어깨·가슴 윗부분이 제자리를 지키게).</summary>
        public const float TopPin = 0.75f;
        /// <summary>단계마다 더하는 중력·몸에서 띄울 거리·천 두께·정점별 최대 이동: 대상 몸통 반지름(중앙값) 대비</summary>
        public const float GravityToTorsoRadius = 0.004f, MarginToTorsoRadius = 0.04f, ThicknessToTorsoRadius = 0.033f, MaxMoveToTorsoRadius = 1f;
        /// <summary>몸 표면 가장 가까운 점 밀기를 적용하는 거리 상한: 몸통 반지름 대비</summary>
        public const float NearestReachToTorsoRadius = 0.5f;
        /// <summary>
        /// 변 강성(늘어나지 않게)과 접힘 강성, 접힘을 펴는 비율. 완전히 펴면(1) 몸통을 두른 원통 곡률까지 펴려 해 아랫단이 종처럼 퍼지므로 반만 편다.
        /// </summary>
        public const float EdgeStiffness = 1f, BendStiffness = 0.3f, BendFlatten = 0.5f;
        public const float Damping = 0.9f;
        /// <summary>천끼리 충돌에서 원래 이 거리(두께 배수) 안에 있던 정점 쌍은 이웃이라 밀어내지 않는다.</summary>
        const float NeighborToThickness = 3f;
        const float MinLength = 1e-8f;

        /// <summary>
        /// region이 몸통인 정점 중 TopPin 아래 정점을 늘어뜨리고 메시를 복제본으로 바꾼다. hipY·neckY는 루트 기준 엉덩이·목 높이.
        /// 움직인 정점 수를 돌려주고, drop에 아랫단이 내려간 거리(월드 단위, 올라가면 음수)를 낸다.
        /// </summary>
        public static int DrapeMesh(SkinnedMeshRenderer garment, Transform root, BodyRegion[] region, BodyCylinders body, BodySurface surface, float hipY, float neckY,
            out float drop)
        {
            drop = 0f;
            Mesh mesh = garment.sharedMesh;
            bool canDrape = mesh != null && region != null && region.Length == mesh.vertexCount && neckY > hipY;
            if (!canDrape) return 0;
            Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(garment, mesh);
            Vector3[] vertices = mesh.vertices;
            var positions = new Vector3[vertices.Length];
            var isFree = new bool[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                bool isInvertible = matrices[i].HasValue && Mathf.Abs(matrices[i].Value.determinant) >= SurfaceOffsetFitter.MinDeterminant;
                positions[i] = matrices[i].HasValue ? FitPlacement.ToRootFrame(root, matrices[i].Value.MultiplyPoint3x4(vertices[i])) : Vector3.zero;
                bool isLower = (positions[i].y - hipY) / (neckY - hipY) < TopPin;
                isFree[i] = isInvertible && region[i] == BodyRegion.Torso && isLower;
            }
            int[] triangles = Enumerable.Range(0, mesh.subMeshCount).Where(s => mesh.GetTopology(s) == MeshTopology.Triangles).SelectMany(mesh.GetTriangles).ToArray();
            int moved = Drape(positions, vertices, triangles, isFree, body, surface, out drop);
            if (moved == 0) return 0;
            Vector3[] draped = (Vector3[])vertices.Clone();
            for (int i = 0; i < vertices.Length; i++)
                if (isFree[i]) draped[i] = matrices[i].Value.inverse.MultiplyPoint3x4(FitPlacement.FromRootFrame(root, positions[i]));
            Mesh copy = Object.Instantiate(mesh);
            copy.name = mesh.name;
            copy.vertices = draped;
            copy.RecalculateBounds();
            PenetrationFixer.ReplaceMesh(garment, copy);
            return moved;
        }

        /// <summary>
        /// positions(루트 기준)를 그 자리에서 늘어뜨린다. weldKeys가 같은 정점은 함께 움직이고, 고정 정점과 이어지지 않은 조각(단추·리본 등)은
        /// 붙잡을 곳이 없어 떨어지므로 고정한다. 움직인 정점 수를 돌려주고 drop에 아랫단이 내려간 거리를 낸다.
        /// </summary>
        public static int Drape(Vector3[] positions, Vector3[] weldKeys, int[] triangles, bool[] isFree, BodyCylinders body, BodySurface surface, out float drop)
        {
            drop = 0f;
            int[] groupOf = PenetrationFixer.WeldGroups(weldKeys, out int count);
            var pos = new Vector3[count];
            var free = Enumerable.Repeat(true, count).ToArray();
            for (int i = 0; i < positions.Length; i++)
            {
                pos[groupOf[i]] = positions[i];
                free[groupOf[i]] &= isFree[i];
            }
            KeepOnlyHeldPieces(free, triangles, groupOf);
            if (!free.Any(f => f)) return 0;
            float lowestBefore = Lowest(pos, free);
            var constraints = Constraints(pos, triangles, groupOf, free);
            Simulate(pos, free, constraints, body, surface);
            int moved = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (!free[groupOf[i]]) continue;
                positions[i] = pos[groupOf[i]];
                moved++;
            }
            drop = lowestBefore - Lowest(pos, free);
            return moved;
        }

        static float Lowest(Vector3[] pos, bool[] free) => Enumerable.Range(0, pos.Length).Where(g => free[g]).Min(g => pos[g].y);

        /// <summary>자유 묶음 중 삼각형 변으로 고정 묶음까지 이어진 것만 자유로 남긴다(너비 우선 탐색).</summary>
        static void KeepOnlyHeldPieces(bool[] free, int[] triangles, int[] groupOf)
        {
            var neighbors = new List<int>[free.Length];
            for (int t = 0; t + 2 < triangles.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = groupOf[triangles[t + k]], b = groupOf[triangles[t + (k + 1) % 3]];
                    (neighbors[a] ??= new List<int>()).Add(b);
                    (neighbors[b] ??= new List<int>()).Add(a);
                }
            var held = new bool[free.Length];
            var queue = new Queue<int>(Enumerable.Range(0, free.Length).Where(g => !free[g]));
            while (queue.Count > 0)
            {
                int g = queue.Dequeue();
                if (neighbors[g] == null) continue;
                foreach (int n in neighbors[g])
                {
                    if (!free[n] || held[n]) continue;
                    held[n] = true;
                    queue.Enqueue(n);
                }
            }
            for (int g = 0; g < free.Length; g++) free[g] &= held[g];
        }

        /// <summary>변(지금 길이, EdgeStiffness)과 접힘(건너편 거리를 BendFlatten만큼 편 값, BendStiffness). 양 끝이 모두 고정인 변은 뺀다.</summary>
        static List<(int a, int b, float rest, float stiffness)> Constraints(Vector3[] pos, int[] triangles, int[] groupOf, bool[] free)
        {
            var opposite = new Dictionary<(int, int), List<int>>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
                for (int k = 0; k < 3; k++)
                {
                    int a = groupOf[triangles[t + k]], b = groupOf[triangles[t + (k + 1) % 3]], c = groupOf[triangles[t + (k + 2) % 3]];
                    if (a == b) continue;
                    var key = a < b ? (a, b) : (b, a);
                    if (!opposite.TryGetValue(key, out var list)) opposite[key] = list = new List<int>();
                    list.Add(c);
                }
            var result = new List<(int, int, float, float)>();
            foreach (var kv in opposite)
            {
                var (a, b) = kv.Key;
                if (!free[a] && !free[b]) continue;
                result.Add((a, b, (pos[b] - pos[a]).magnitude, EdgeStiffness));
                bool isHinge = kv.Value.Count == 2 && kv.Value[0] != kv.Value[1];
                if (isHinge) result.Add((kv.Value[0], kv.Value[1], GarmentRelaxer.FlattenedDistance(pos[a], pos[b], pos[kv.Value[0]], pos[kv.Value[1]], BendFlatten), BendStiffness));
            }
            return result;
        }

        static void Simulate(Vector3[] pos, bool[] free, List<(int a, int b, float rest, float stiffness)> constraints, BodyCylinders body, BodySurface surface)
        {
            float torsoRadius = body.MedianRadius(BodyRegion.Torso);
            float gravity = torsoRadius * GravityToTorsoRadius, margin = torsoRadius * MarginToTorsoRadius, maxMove = torsoRadius * MaxMoveToTorsoRadius;
            float thickness = torsoRadius * ThicknessToTorsoRadius, reach = torsoRadius * NearestReachToTorsoRadius;
            Vector3[] start = (Vector3[])pos.Clone(), previous = (Vector3[])pos.Clone();
            var layers = new LayerGrid(start, free, thickness);
            for (int step = 0; step < Steps; step++)
            {
                for (int g = 0; g < pos.Length; g++)
                {
                    if (!free[g]) continue;
                    Vector3 velocity = (pos[g] - previous[g]) * Damping;
                    previous[g] = pos[g];
                    pos[g] += velocity + Vector3.down * gravity;
                }
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    foreach (var (a, b, rest, stiffness) in constraints) Satisfy(pos, free, a, b, rest, stiffness);
                    bool isCollisionPass = iteration % 2 == 0;
                    if (isCollisionPass) layers.Separate(pos);
                    for (int g = 0; g < pos.Length; g++)
                        if (free[g]) pos[g] = start[g] + Vector3.ClampMagnitude(KeepOutside(pos[g], body, surface, margin, reach) - start[g], maxMove);
                }
            }
        }

        static void Satisfy(Vector3[] pos, bool[] free, int a, int b, float rest, float stiffness)
        {
            float wa = free[a] ? 1f : 0f, wb = free[b] ? 1f : 0f;
            if (wa + wb == 0f) return;
            Vector3 d = pos[b] - pos[a];
            float length = d.magnitude;
            if (length < MinLength) return;
            Vector3 correction = d * ((length - rest) / length * stiffness / (wa + wb));
            pos[a] += correction * wa;
            pos[b] -= correction * wb;
        }

        /// <summary>몸통·양 허벅지 원통 표와 몸 표면 가장 가까운 점 기준으로 몸 표면 + margin 밖에 둔다(아랫단이 다리를 뚫지 않게 허벅지도 본다).</summary>
        static Vector3 KeepOutside(Vector3 p, BodyCylinders body, BodySurface surface, float margin, float reach)
        {
            foreach (BodyRegion region in new[] { BodyRegion.Torso, BodyRegion.LeftUpperLeg, BodyRegion.RightUpperLeg })
            {
                bool isInside = body.TryClearance(region, p, out float clearance) && clearance < margin;
                if (isInside && body.TryOutward(region, p, out Vector3 outward)) p += outward * (margin - clearance);
            }
            if (surface == null || !surface.TryNearest(p, out Vector3 nearest, out Vector3 normal)) return p;
            float depth = Vector3.Dot(p - nearest, normal);
            bool isTooClose = depth < margin && (p - nearest).sqrMagnitude < reach * reach;
            return isTooClose ? p + normal * (margin - depth) : p;
        }

        /// <summary>
        /// 천끼리 겹침 방지: 공간 격자(칸 = 두께)의 같은·이웃 칸에서 두께보다 가까운 쌍을 두께만큼 떼어 놓는다.
        /// 처음에 이미 가까웠던 쌍(같은 천의 이웃)은 건드리지 않는다. 격자 그릇은 다시 써서 반복마다 새로 만들지 않는다.
        /// </summary>
        sealed class LayerGrid
        {
            const int CellCapacity = 4;
            readonly Vector3[] start;
            readonly bool[] free;
            readonly float thickness;
            readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();

            public LayerGrid(Vector3[] start, bool[] free, float thickness)
            {
                this.start = start;
                this.free = free;
                this.thickness = thickness;
            }

            public void Separate(Vector3[] pos)
            {
                if (thickness <= 0f) return;
                foreach (var list in cells.Values) list.Clear();
                for (int g = 0; g < pos.Length; g++)
                {
                    var key = Vector3Int.FloorToInt(pos[g] / thickness);
                    if (!cells.TryGetValue(key, out var list)) cells[key] = list = new List<int>(CellCapacity);
                    list.Add(g);
                }
                for (int a = 0; a < pos.Length; a++)
                    if (free[a]) SeparateFromNeighbors(pos, a);
            }

            void SeparateFromNeighbors(Vector3[] pos, int a)
            {
                Vector3Int key = Vector3Int.FloorToInt(pos[a] / thickness);
                float neighbor = thickness * NeighborToThickness;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (!cells.TryGetValue(key + new Vector3Int(dx, dy, dz), out var others)) continue;
                    foreach (int b in others)
                    {
                        // 자유끼리는 번호가 큰 쪽에서 한 번만 처리한다.
                        bool isOtherPass = b == a || (free[b] && b < a);
                        bool wereNeighbors = (start[a] - start[b]).sqrMagnitude < neighbor * neighbor;
                        if (!isOtherPass && !wereNeighbors) PushApart(pos, a, b);
                    }
                }
            }

            void PushApart(Vector3[] pos, int a, int b)
            {
                Vector3 d = pos[b] - pos[a];
                float length = d.magnitude;
                if (length >= thickness || length < MinLength) return;
                float wb = free[b] ? 1f : 0f;
                Vector3 correction = d * ((length - thickness) / length / (1f + wb));
                pos[a] += correction;
                pos[b] -= correction * wb;
            }
        }
    }
}
