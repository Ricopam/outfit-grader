using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 맞춘 상의의 몸통·소매를 짧게 반복해 풀어, 길이로 크게 눌린 곳의 날카로운 접힘(톱니)을 부드럽게 편다(선택 옵션, PRD 0장 형태 보존).
    /// Pixar 리핏처럼 지금 위치에 붙인 채로, 변 길이는 지금 길이를 지키고 접힌 두 삼각형 건너편 거리만 편 거리 쪽으로 옮기며,
    /// 부위 원통 표(BodyCylinders)로 몸 안쪽을 판정해 바깥으로 민다. 위치 기반 반복(속도 감쇠, 중력 없음).
    /// 실측(inuinu, 아랫단 엉덩이까지): 몸 관통 272 -> 193, 소매가 매끈한 통이 되고 앞발을 덮음, 아바타당 약 1초.
    /// </summary>
    public static class GarmentRelaxer
    {
        /// <summary>반복 단계 수와 단계마다 제약 풀이 횟수</summary>
        public const int Steps = 10, Iterations = 8;
        /// <summary>몸통 접힌 두 삼각형 건너편 거리를 편 거리 쪽으로 옮기는 비율(0: 그대로, 1: 완전히 폄). 소매는 쉼 모양(원본 비율)을 따른다.</summary>
        public const float TorsoFlatten = 0.5f;
        /// <summary>반복마다 원래 위치로 되돌리는 비율: 몸통은 단단히(배가 옷을 뚫지 않게), 소매는 약하게(접힘이 풀리게)</summary>
        public const float TorsoAttach = 0.15f, SleeveAttach = 0.03f;
        /// <summary>속도 감쇠</summary>
        public const float Damping = 0.85f;
        /// <summary>몸 표면에서 띄울 거리: 대상 몸통 반지름(중앙값) 대비 비율</summary>
        public const float MarginToTorsoRadius = 0.04f;
        /// <summary>몸 표면 가장 가까운 점 밀기를 적용하는 거리 상한: 몸통 반지름 대비(멀리 떨어진 반대편 표면으로 끌려가지 않게)</summary>
        public const float NearestReachToTorsoRadius = 0.5f;
        const float MinLength = 1e-8f;

        /// <summary>
        /// positions(루트 기준)를 그 자리에서 푼다. isFree가 거짓인 정점(끈·후드·부위 밖)은 움직이지 않는다.
        /// weldKeys는 이음매에서 갈라진 같은 정점을 묶는 키(메시 원래 위치). 움직인 정점 수를 돌려준다.
        /// </summary>
        public static int Relax(Vector3[] positions, Vector3[] weldKeys, int[] triangles, bool[] isFree, BodyRegion[] region, BodyCylinders body,
            BodySurface surface = null, System.Func<int, int, float> restLength = null)
        {
            int[] groupOf = PenetrationFixer.WeldGroups(weldKeys, out int count);
            var pos = new Vector3[count];
            var free = Enumerable.Repeat(true, count).ToArray();
            var groupRegion = new BodyRegion[count];
            var representative = new int[count];
            for (int i = 0; i < positions.Length; i++)
            {
                int g = groupOf[i];
                pos[g] = positions[i];
                free[g] &= isFree[i];
                if (isFree[i]) groupRegion[g] = region[i];
                representative[g] = i;
            }
            float Rest(int ga, int gb)
            {
                float r = restLength != null && free[ga] && free[gb] ? restLength(representative[ga], representative[gb]) : -1f;
                return float.IsNaN(r) || float.IsInfinity(r) ? -1f : r;
            }
            var constraints = Constraints(pos, Rest, triangles, groupOf, free);
            Solve(pos, free, groupRegion, constraints, body, surface);
            int moved = 0;
            for (int i = 0; i < positions.Length; i++)
            {
                if (!free[groupOf[i]]) continue;
                positions[i] = pos[groupOf[i]];
                moved++;
            }
            return moved;
        }

        /// <summary>
        /// 스킨 메시를 지금 자세 위치에서 풀고 메시 정점을 바꾼다(겹침 정리까지 마친 뒤). region이 None인 정점은 고정한다.
        /// 메시는 맞춤이 만든 복제본이어야 한다(원본 에셋을 바꾸지 않게). 움직인 정점 수를 돌려준다.
        /// </summary>
        /// <param name="surface">대상 몸 표면(루트 기준, 가장 가까운 점과 바깥 법선). 원통 표가 놓치는 곳(마디 끝 너머, 겨드랑이)을 막는다. null이면 원통 표만.</param>
        /// <param name="restLength">두 정점(메시 정점 번호) 사이 쉼 거리. 음수를 돌려주거나 null이면 지금 거리(변)·반쯤 편 거리(접힘)를 쓴다.</param>
        public static int RelaxMesh(SkinnedMeshRenderer garment, Transform root, BodyRegion[] region, System.Func<int, int, float> restLength, BodyCylinders body,
            BodySurface surface)
        {
            Mesh mesh = garment.sharedMesh;
            if (mesh == null || region == null || region.Length != mesh.vertexCount) return 0;
            Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(garment, mesh);
            Vector3[] vertices = mesh.vertices;
            var positions = new Vector3[vertices.Length];
            var isFree = new bool[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                bool isInvertible = matrices[i].HasValue && Mathf.Abs(matrices[i].Value.determinant) >= SurfaceOffsetFitter.MinDeterminant;
                isFree[i] = isInvertible && region[i] != BodyRegion.None;
                positions[i] = matrices[i].HasValue ? FitPlacement.ToRootFrame(root, matrices[i].Value.MultiplyPoint3x4(vertices[i])) : Vector3.zero;
            }
            int[] triangles = Enumerable.Range(0, mesh.subMeshCount).Where(s => mesh.GetTopology(s) == MeshTopology.Triangles).SelectMany(mesh.GetTriangles).ToArray();
            int moved = Relax(positions, vertices, triangles, isFree, region, body, surface, restLength);
            if (moved == 0) return 0;
            Vector3[] relaxed = (Vector3[])vertices.Clone();
            for (int i = 0; i < vertices.Length; i++)
                if (isFree[i]) relaxed[i] = matrices[i].Value.inverse.MultiplyPoint3x4(FitPlacement.FromRootFrame(root, positions[i]));
            // 다른 단계처럼 복제본을 만들어 바꾼다(원본 에셋을 고치지 않게).
            Mesh copy = Object.Instantiate(mesh);
            copy.name = mesh.name;
            copy.vertices = relaxed;
            copy.RecalculateBounds();
            PenetrationFixer.ReplaceMesh(garment, copy);
            return moved;
        }

        /// <summary>
        /// 변과 접힘 제약. rest(묶음 쌍)가 0 이상이면 그 거리(소매: 원본 비율), 아니면 변은 지금 길이·접힘은 반쯤 편 거리. 같은 묶음끼리의 변은 뺀다.
        /// </summary>
        static List<(int a, int b, float rest)> Constraints(Vector3[] pos, System.Func<int, int, float> rest, int[] triangles, int[] groupOf, bool[] free)
        {
            var result = new List<(int, int, float)>();
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
            foreach (var kv in opposite)
            {
                var (a, b) = kv.Key;
                if (!free[a] && !free[b]) continue;
                float edge = rest(a, b);
                result.Add((a, b, edge >= 0f ? edge : (pos[b] - pos[a]).magnitude));
                bool isHinge = kv.Value.Count == 2 && kv.Value[0] != kv.Value[1];
                if (!isHinge) continue;
                int c = kv.Value[0], d = kv.Value[1];
                float hinge = edge >= 0f ? rest(c, d) : -1f;
                result.Add((c, d, hinge >= 0f ? hinge : FlattenedDistance(pos[a], pos[b], pos[c], pos[d], TorsoFlatten)));
            }
            return result;
        }

        /// <summary>공유 변(a, b)을 사이에 둔 두 삼각형 건너편 정점(c, d) 거리를 지금 거리와 평평하게 편 거리 사이에서 flatten 비율만큼 편 값</summary>
        public static float FlattenedDistance(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float flatten)
        {
            Vector3 axis = (b - a).normalized;
            Vector3 rc = c - a, rd = d - a;
            float tc = Vector3.Dot(rc, axis), td = Vector3.Dot(rd, axis);
            float hc = (rc - axis * tc).magnitude, hd = (rd - axis * td).magnitude;
            float flat = Mathf.Sqrt((tc - td) * (tc - td) + (hc + hd) * (hc + hd));
            float now = (d - c).magnitude;
            return Mathf.Lerp(now, Mathf.Max(now, flat), flatten);
        }

        static void Solve(Vector3[] pos, bool[] free, BodyRegion[] region, List<(int a, int b, float rest)> constraints, BodyCylinders body, BodySurface surface)
        {
            Vector3[] anchor = (Vector3[])pos.Clone(), previous = (Vector3[])pos.Clone();
            float torsoRadius = body.MedianRadius(BodyRegion.Torso);
            float margin = torsoRadius * MarginToTorsoRadius, reach = torsoRadius * NearestReachToTorsoRadius;
            for (int step = 0; step < Steps; step++)
            {
                for (int g = 0; g < pos.Length; g++)
                {
                    if (!free[g]) continue;
                    Vector3 velocity = (pos[g] - previous[g]) * Damping;
                    previous[g] = pos[g];
                    pos[g] += velocity;
                }
                for (int iteration = 0; iteration < Iterations; iteration++)
                {
                    foreach (var (a, b, rest) in constraints) SatisfyLength(pos, free, a, b, rest);
                    for (int g = 0; g < pos.Length; g++)
                    {
                        if (!free[g]) continue;
                        Vector3 p = KeepOutside(Vector3.Lerp(pos[g], anchor[g], region[g] == BodyRegion.Torso ? TorsoAttach : SleeveAttach), region[g], body, margin);
                        pos[g] = KeepOutside(p, surface, margin, reach);
                    }
                }
            }
        }

        static void SatisfyLength(Vector3[] pos, bool[] free, int a, int b, float rest)
        {
            float wa = free[a] ? 1f : 0f, wb = free[b] ? 1f : 0f;
            bool isUsable = wa + wb > 0f && !float.IsNaN(rest) && !float.IsInfinity(rest);
            if (!isUsable) return;
            Vector3 d = pos[b] - pos[a];
            float length = d.magnitude;
            if (length < MinLength) return;
            Vector3 correction = d * ((length - rest) / length / (wa + wb));
            pos[a] += correction * wa;
            pos[b] -= correction * wb;
        }

        /// <summary>몸 표면의 가장 가까운 점이 reach 안에 있고 그 바깥 법선 쪽으로 margin보다 덜 떨어졌으면 법선 방향으로 민다.</summary>
        static Vector3 KeepOutside(Vector3 p, BodySurface surface, float margin, float reach)
        {
            if (surface == null || !surface.TryNearest(p, out Vector3 nearest, out Vector3 normal)) return p;
            float depth = Vector3.Dot(p - nearest, normal);
            bool isTooClose = depth < margin && (p - nearest).sqrMagnitude < reach * reach;
            return isTooClose ? p + normal * (margin - depth) : p;
        }

        /// <summary>부위 원통 표에서 몸 표면 + margin 안쪽이면 축 바깥 방향으로 민다.</summary>
        static Vector3 KeepOutside(Vector3 p, BodyRegion region, BodyCylinders body, float margin)
        {
            bool isInside = body.TryClearance(region, p, out float clearance) && clearance < margin;
            if (!isInside || !body.TryOutward(region, p, out Vector3 outward)) return p;
            return p + outward * (margin - clearance);
        }
    }
}
