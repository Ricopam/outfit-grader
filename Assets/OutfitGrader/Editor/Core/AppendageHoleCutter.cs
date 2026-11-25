using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 귀·꼬리처럼 몸에서 솟은 부위가 모자·옷을 뚫고 지나가는 자리의 삼각형을 지워, 구멍으로 나온 모습을 만든다.
    /// 수인용 모자·후드 판매자들이 권하는 "가려지는 부분의 폴리곤 삭제"를 자동으로 하는 것. 원본 메시는 바꾸지 않는다.
    /// 부위 정점 "근처"를 지우면 넓은 밑동 주변까지 지워져(실모델에서 모자 삼각형의 19%) 부위 메시의 변이 실제로
    /// 통과하는 삼각형만 지운다. 부위 단면 안쪽에 남는 삼각형은 부위에 가려 보이지 않는다.
    /// ponytail: 이름 규칙을 따르지 않는 귀·꼬리는 찾지 못해 구멍을 내지 않는다(결과는 기존과 같음). 필요하면 본 직접 지정으로 확장.
    /// </summary>
    public static class AppendageHoleCutter
    {
        /// <summary>선분과 삼각형 평면이 이 각도 사인보다 나란하면 교차로 보지 않는다(반올림 오차로 잘못 지우지 않게).</summary>
        const float ParallelSine = 1e-5f;

        /// <summary>
        /// 귀 본 이름: Ear.001.L, ear1.L, PB_Ear.L, CatEar, Kemomimi, 耳 등.
        /// "ear"가 한 단어일 때만 본다(앞은 이름 시작·구분자·소문자 뒤 대문자 E, 뒤는 소문자가 아님):
        /// Year·Gear·Hear·Earth·Earphone은 제외. 귀걸이(Earring, Ear_Ring)도 제외.
        /// </summary>
        internal static readonly Regex EarBoneName = new Regex(
            @"(?:(?<![A-Za-z])[Ee][Aa][Rr]|(?<=[a-z])E[Aa][Rr])(?![a-z])(?![_.\-\s]?[Rr][Ii][Nn][Gg])|[Mm][Ii][Mm][Ii]|耳");

        /// <summary>
        /// 꼬리 본 이름: Tail.001, Tail.Root, tail_01, CatTail, Shippo, しっぽ, 尻尾. 한 단어일 때만(Ponytail·Tailor 제외).
        /// 일본 제작 모델에서 쓰이는 철자 tale(inuinu의 tale.001)도 꼬리로 본다.
        /// </summary>
        internal static readonly Regex TailBoneName = new Regex(
            @"(?:(?<![A-Za-z])[Tt][Aa]([Ii][Ll]|[Ll][Ee])|(?<=[a-z])T[Aa]([Ii][Ll]|[Ll][Ee]))(?![a-z])|[Ss]hippo|しっぽ|シッポ|尻尾");

        /// <summary>귀 메시의 변(루트 기준 축). 가장 가까운 Humanoid 조상이 Head인 귀 이름 본에 묶인 삼각형만 쓴다.</summary>
        public static List<(Vector3 a, Vector3 b)> EarEdges(AvatarRig target, IReadOnlyCollection<Transform> excluded, SkinCache skins = null)
        {
            return AppendageEdges(target, excluded, HumanBodyBones.Head, EarBoneName, skins);
        }

        /// <summary>꼬리 메시의 변(루트 기준 축). 가장 가까운 Humanoid 조상이 Hips인 꼬리 이름 본에 묶인 삼각형만 쓴다(머리의 포니테일 제외).</summary>
        public static List<(Vector3 a, Vector3 b)> TailEdges(AvatarRig target, IReadOnlyCollection<Transform> excluded, SkinCache skins = null)
        {
            return AppendageEdges(target, excluded, HumanBodyBones.Hips, TailBoneName, skins);
        }

        /// <summary>
        /// 대상 아바타 부위 메시의 변. 세 정점이 모두 부위 본에 묶인 삼각형의 변만 쓴다.
        /// 이번에 입힌 의상(excluded)은 빼고, 보이는 메시만 쓴다.
        /// </summary>
        static List<(Vector3 a, Vector3 b)> AppendageEdges(AvatarRig target, IReadOnlyCollection<Transform> excluded,
            HumanBodyBones region, Regex boneName, SkinCache skins)
        {
            Transform regionBone = target.GetHuman(region);
            var edges = new List<(Vector3, Vector3)>();
            if (regionBone == null) return edges;

            foreach (SkinnedMeshRenderer renderer in target.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                bool isExcluded = excluded != null && excluded.Any(e => e != null && renderer.transform.IsChildOf(e));
                if (isExcluded || !renderer.enabled || renderer.sharedMesh == null) continue;

                bool[] isPartBone = renderer.bones.Select(b => IsAppendageBone(target, regionBone, b, boneName)).ToArray();
                if (isPartBone.Any(e => e)) AddEdges(renderer, isPartBone, target.Root, edges, skins);
            }
            return edges;
        }

        static void AddEdges(SkinnedMeshRenderer renderer, bool[] isPartBone, Transform root, List<(Vector3, Vector3)> edges, SkinCache skins)
        {
            Mesh mesh = renderer.sharedMesh;
            Matrix4x4?[] matrices = SkinCache.Of(skins, renderer, mesh);
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            var positions = new Vector3?[vertices.Length];
            var frame = new FitPlacement.RootFrame(root);
            for (int i = 0; i < vertices.Length && i < weights.Length; i++)
            {
                int main = weights[i].boneIndex0;
                bool isPartVertex = main < isPartBone.Length && isPartBone[main] && matrices[i].HasValue;
                if (isPartVertex) positions[i] = frame.Of(matrices[i].Value.MultiplyPoint3x4(vertices[i]));
            }
            foreach (int[] tri in Triangles(mesh))
            {
                Vector3? a = positions[tri[0]], b = positions[tri[1]], c = positions[tri[2]];
                if (!a.HasValue || !b.HasValue || !c.HasValue) continue;
                edges.Add((a.Value, b.Value));
                edges.Add((b.Value, c.Value));
                edges.Add((c.Value, a.Value));
            }
        }

        /// <summary>Humanoid가 아니고, 가장 가까운 Humanoid 조상이 부위 본(Head·Hips)이며, 그 사이 본 이름이 부위 이름인 본</summary>
        static bool IsAppendageBone(AvatarRig target, Transform regionBone, Transform bone, Regex boneName)
        {
            if (bone == null || target.TryGetHuman(bone, out _)) return false;
            bool hasPartName = false;
            for (Transform t = bone; t != null; t = t.parent)
            {
                if (target.TryGetHuman(t, out _)) return t == regionBone && hasPartName;
                hasPartName |= boneName.IsMatch(t.name);
            }
            return false;
        }

        /// <summary>구멍 가장자리를 부위에 붙이는 설정: 부위 표면에서 띄울 거리, 당길 수 있는 최대 거리, 몸 관통 재검사용 몸 표면(루트 기준)</summary>
        public sealed class HugSettings
        {
            public float Margin, MaxPull;
            public BodySurface Body;
        }

        /// <summary>부위 메시의 변이 통과하는 자리에 구멍을 낸 새 메시로 교체한다. 지우거나 잘라 낸 삼각형 수를 돌려준다.</summary>
        /// <param name="hug">null이 아니면(꼬리) 부위 둘레 경계선을 따라 삼각형을 잘라 구멍을 내고(ContourHoleCutter),
        /// 그 구멍이 너무 크게 벌어지면 삼각형을 지운 뒤 새 구멍 가장자리를 부위 표면 쪽으로 당겨 오므린다(HoleEdgeHugger).</param>
        public static int Cut(Renderer prop, IReadOnlyList<(Vector3 a, Vector3 b)> partEdges, Transform targetRoot, string meshName, HugSettings hug = null)
        {
            Mesh source = FitAnalysis.MeshOf(prop);
            if (source == null || partEdges.Count == 0) return 0;

            Vector3[] inRoot = RootFramePositions(prop, source, targetRoot);
            Bounds propBounds = EdgeGrid.BoundsOf(inRoot.Where(p => !float.IsInfinity(p.x)));
            // 길이가 거의 0인 변(크기 0으로 숨긴 꼬리 등)은 뚫을 수 없고 격자 칸을 극단적으로 작게 만들어 뺀다.
            List<(Vector3 a, Vector3 b)> nearEdges = partEdges
                .Where(e => (e.b - e.a).sqrMagnitude > EdgeGrid.MinEdgeLength * EdgeGrid.MinEdgeLength && EdgeGrid.SegmentBounds(e.a, e.b).Intersects(propBounds))
                .ToList();
            if (nearEdges.Count == 0) return 0;
            var grid = new EdgeGrid(nearEdges);
            if (hug != null)
            {
                Mesh contour = ContourHoleCutter.Cut(source, inRoot, partEdges, grid, hug.Margin, meshName, out int changed);
                if (contour != null)
                {
                    PenetrationFixer.ReplaceMesh(prop, contour);
                    return changed;
                }
            }

            Mesh cutMesh = Object.Instantiate(source);
            cutMesh.name = meshName;
            int removed = 0;
            for (int s = 0; s < source.subMeshCount; s++)
            {
                if (source.GetTopology(s) == MeshTopology.Triangles) removed += KeepUnpierced(cutMesh, s, source.GetTriangles(s), inRoot, grid);
            }
            if (removed == 0)
            {
                Object.DestroyImmediate(cutMesh);
                return 0;
            }
            bool canHug = hug != null && hug.Margin > 0f && hug.MaxPull > 0f;
            if (canHug) HugHoleEdges(prop, source, cutMesh, inRoot, nearEdges, targetRoot, hug);
            PenetrationFixer.ReplaceMesh(prop, cutMesh);
            return removed;
        }

        /// <summary>
        /// 새 구멍 가장자리를 부위 변 쪽으로 당긴 정점 위치를 cutMesh에 쓴다(메시 공간). 당긴 결과가 몸 표면에서 margin보다 가까워지는(몸 안으로 드는) 정점은
        /// 옮기지 않는다(꼬리 밑동 변은 엉덩이 안에 묻혀 있어 그쪽으로 당기면 옷이 몸을 뚫음).
        /// </summary>
        static void HugHoleEdges(Renderer prop, Mesh source, Mesh cutMesh, Vector3[] inRoot, List<(Vector3 a, Vector3 b)> partEdges, Transform root, HugSettings hug)
        {
            Vector3[] deltas = HoleEdgeHugger.Pull(inRoot, source.vertices, PenetrationFixer.TriangleIndices(source), PenetrationFixer.TriangleIndices(cutMesh),
                partEdges, hug.Margin, hug.MaxPull);
            Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(prop, source);
            Vector3[] vertices = cutMesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                bool canMove = deltas[i].sqrMagnitude > 0f && matrices[i].HasValue && Mathf.Abs(matrices[i].Value.determinant) >= SurfaceOffsetFitter.MinDeterminant;
                if (canMove && StaysOutside(hug, inRoot[i] + deltas[i])) vertices[i] += matrices[i].Value.inverse.MultiplyVector(root.rotation * deltas[i]);
            }
            cutMesh.vertices = vertices;
            cutMesh.RecalculateBounds();
        }

        /// <summary>점(루트 기준)이 몸 표면 바깥으로 margin 이상 떨어져 있는지. 몸 표면을 모르거나 근처에 몸이 없으면 참.</summary>
        static bool StaysOutside(HugSettings hug, Vector3 p)
        {
            if (hug.Body == null || !hug.Body.TryNearest(p, out Vector3 nearest, out Vector3 normal)) return true;
            return Vector3.Dot(p - nearest, normal) >= hug.Margin;
        }

        /// <summary>정점 위치(루트 기준 축). 위치를 구할 수 없는 정점은 무한대.</summary>
        static Vector3[] RootFramePositions(Renderer prop, Mesh mesh, Transform root)
        {
            Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(prop, mesh);
            Vector3[] vertices = mesh.vertices;
            var result = new Vector3[vertices.Length];
            var frame = new FitPlacement.RootFrame(root);
            for (int i = 0; i < vertices.Length; i++)
            {
                result[i] = matrices[i].HasValue ? frame.Of(matrices[i].Value.MultiplyPoint3x4(vertices[i])) : Vector3.positiveInfinity;
            }
            return result;
        }

        /// <summary>서브메시에서 부위가 통과하지 않은 삼각형만 남긴다. 지운 삼각형 수를 돌려준다.</summary>
        static int KeepUnpierced(Mesh mesh, int subMesh, int[] triangles, Vector3[] positions, EdgeGrid edges)
        {
            var kept = new List<int>(triangles.Length);
            int removed = 0;
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (edges.Pierces(positions[triangles[t]], positions[triangles[t + 1]], positions[triangles[t + 2]])) { removed++; continue; }
                kept.Add(triangles[t]); kept.Add(triangles[t + 1]); kept.Add(triangles[t + 2]);
            }
            mesh.SetTriangles(kept, subMesh);
            return removed;
        }

        /// <summary>선분 p-q가 삼각형 a-b-c를 지나는지(Möller–Trumbore 광선-삼각형 교차를 선분 범위 t∈[0,1]로 제한)</summary>
        public static bool SegmentHitsTriangle(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 dir = q - p, e1 = b - a, e2 = c - a;
            Vector3 h = Vector3.Cross(dir, e2);
            float det = Vector3.Dot(e1, h);
            // 선분이 삼각형 평면과 거의 나란하면(각도 사인 1e-5 미만) 반올림 오차로 잘못 지우지 않게 교차로 보지 않는다.
            bool isParallel = Mathf.Abs(det) < ParallelSine * dir.magnitude * e1.magnitude * e2.magnitude;
            if (isParallel) return false;

            float inv = 1f / det;
            Vector3 s = p - a;
            float u = inv * Vector3.Dot(s, h);
            if (u < 0f || u > 1f) return false;
            Vector3 k = Vector3.Cross(s, e1);
            float v = inv * Vector3.Dot(dir, k);
            if (v < 0f || u + v > 1f) return false;
            float t = inv * Vector3.Dot(e2, k);
            return t >= 0f && t <= 1f;
        }

        static IEnumerable<int[]> Triangles(Mesh mesh)
        {
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                int[] tri = mesh.GetTriangles(s);
                for (int t = 0; t + 2 < tri.Length; t += 3) yield return new[] { tri[t], tri[t + 1], tri[t + 2] };
            }
        }
    }
}
