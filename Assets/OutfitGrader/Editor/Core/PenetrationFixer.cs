using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>의상 메시 하나의 관통 보정 결과</summary>
    public readonly struct PenetrationResult
    {
        public readonly int PushedVertices;
        public readonly float MaxPush;

        public PenetrationResult(int pushedVertices, float maxPush)
        {
            PushedVertices = pushedVertices;
            MaxPush = maxPush;
        }
    }

    /// <summary>
    /// 대상 아바타의 관통 판정용 표면. 옷은 몸 전체(엉덩이 기준 칸), 모자는 머리만(머리 폭 기준 칸) 쓴다.
    /// 치비는 엉덩이가 낮아 칸이 작은데 머리 정점은 성겨, 모자만 큰 칸이 필요하다. 옷까지 큰 칸을 쓰면 몸이 없던
    /// 치마 밑단 같은 곳까지 밀려 결과가 바뀌므로 나눈다.
    /// </summary>
    public sealed class TargetSurface
    {
        public BodySurface Body;
        public BodySurface Head;
        public HeadShape HeadShape;
    }

    /// <summary>대상 머리 크기: 좌우 폭과 정수리 위치(루트 기준 축). 잴 수 없으면 Width 0.</summary>
    public readonly struct HeadShape
    {
        public readonly float Width;
        public readonly Vector3 Crown;

        public HeadShape(float width, Vector3 crown)
        {
            Width = width;
            Crown = crown;
        }
    }

    /// <summary>
    /// 마디 굵기 맞춤 뒤에도 몸 안으로 들어간 의상 정점을 대상 몸 표면 바깥으로 밀어낸 새 메시로 교체한다.
    /// 본에 묶인 옷뿐 아니라 본 없이 붙은 소품(모자 등)도 오브젝트 행렬로 펼쳐 같은 방식으로 두상에 맞춘다.
    /// 원본 메시는 바꾸지 않는다. 새 메시는 씬에만 두고 프리팹 저장 때 에셋으로 저장한다
    /// (맞추기를 Ctrl+Z로 되돌렸을 때 참조 없는 메시 에셋이 쌓이지 않게).
    /// </summary>
    public static class PenetrationFixer
    {
        /// <summary>몸과 옷 사이 여유 간격: 엉덩이 높이 대비 비율</summary>
        public const float MarginToHipHeight = 0.006f;
        /// <summary>한 정점을 미는 최대 거리: 엉덩이 높이 대비 비율(넘으면 법선 오판으로 보고 밀지 않음)</summary>
        public const float MaxPushToHipHeight = 0.08f;
        /// <summary>
        /// 머리 소품의 최대 밀기 거리: 머리 폭 대비 비율. 치비는 엉덩이가 낮아 엉덩이 기준으로는 1cm 남짓밖에 못 밀고,
        /// 머리가 큰 아바타일수록 모자가 깊이 파고든다.
        /// </summary>
        public const float MaxPushToHeadWidth = 0.15f;
        /// <summary>
        /// 모자와 머리 사이 여유 간격: 머리 폭 대비 비율. 머리 메시가 성긴(면이 넓은) 아바타는 정점 사이의 평평한 면이
        /// 모자 밖으로 비치므로, 엉덩이 기준(치비에서 약 1mm)보다 넉넉히 둔다.
        /// </summary>
        public const float MarginToHeadWidth = 0.02f;
        /// <summary>몸 표면 격자 칸 크기: 엉덩이 높이 대비 비율</summary>
        public const float CellToHipHeight = 0.04f;
        /// <summary>
        /// 모자용 머리 격자 칸 최소 크기: 머리 폭 대비 비율. 치비는 엉덩이가 낮아 칸이 작은데 머리는 크고 정점이 성겨,
        /// 엉덩이 기준 칸으로는 모자 정점 근처에서 머리 정점을 못 찾는다(inuinu 칸 6mm, 머리 정점 간격 수 cm).
        /// </summary>
        public const float CellToHeadWidth = 0.1f;
        /// <summary>밀어낸 정점에서 이웃 고리로 갈 때마다 이전 고리 이동량에 곱하는 비율(실제 60%, 30%)</summary>
        static readonly float[] NeighborFalloff = { 0.6f, 0.5f };

        /// <summary>
        /// 대상 아바타 표면. 보이는 메시만 쓰고, 이번에 입힌 의상(excluded)은 몸으로 치지 않는다.
        /// 몸·머리 표면과 머리 크기를 같은 정점으로 만든다(정점을 다시 읽지 않게).
        /// </summary>
        public static TargetSurface BuildSurface(AvatarRig target, IReadOnlyCollection<Transform> excluded) =>
            BuildSurface(target, BodyPointCloud.Collect(target, excluded));

        /// <summary>이미 모은 몸 정점(Humanoid 본에 직접 묶인 정점만)으로 몸·머리 표면을 만든다.</summary>
        public static TargetSurface BuildSurface(AvatarRig target, BodyPointCloud cloud)
        {
            List<Vector3> headPoints = cloud.InRegion(HumanBodyBones.Head);
            HeadShape head = headPoints.Count == 0 ? default : MeasureHead(headPoints);
            float bodyCell = RegionMeasurer.HipHeight(target) * CellToHipHeight;
            return new TargetSurface
            {
                Body = new BodySurface(cloud.SurfacePoints(), bodyCell),
                Head = new BodySurface(cloud.SurfacePoints(HumanBodyBones.Head), Mathf.Max(bodyCell, head.Width * CellToHeadWidth)),
                HeadShape = head,
            };
        }

        static HeadShape MeasureHead(List<Vector3> points)
        {
            float minX = points.Min(p => p.x), maxX = points.Max(p => p.x);
            float minZ = points.Min(p => p.z), maxZ = points.Max(p => p.z);
            var crown = new Vector3((minX + maxX) * 0.5f, points.Max(p => p.y), (minZ + maxZ) * 0.5f);
            return new HeadShape(maxX - minX, crown);
        }

        /// <summary>의상 메시를 지금 위치 기준으로 펼쳐 관통 정점을 밀어내고, 바뀐 정점이 있으면 새 메시로 교체한다.</summary>
        /// <param name="margin">몸 표면과 남길 여유 간격(월드 단위)</param>
        /// <param name="maxPush">한 정점을 미는 최대 거리(월드 단위). 넘으면 법선 오판으로 보고 밀지 않는다.</param>
        /// <param name="isKept">true인 정점은 직접 밀지 않는다(표면 간격 보존으로 이미 다시 배치한 정점). null이면 모두 판정.</param>
        public static PenetrationResult Fix(Renderer clothing, BodySurface surface, AvatarRig target, string meshName, float margin, float maxPush,
            bool[] isKept = null)
        {
            Mesh source = FitAnalysis.MeshOf(clothing);
            if (source == null || surface.Count == 0) return default;

            Vector3[] vertices = source.vertices;
            Vector3[] deltas = PushDeltas(VertexMatrices(clothing, source), vertices, surface, target.Root, margin, maxPush, isKept,
                out int pushed, out float pushedMax);
            if (pushed == 0) return default;

            deltas = SpreadWelded(deltas, vertices, TriangleIndices(source));
            for (int i = 0; i < vertices.Length; i++) vertices[i] += deltas[i];

            Mesh fixedMesh = Object.Instantiate(source);
            fixedMesh.name = meshName;
            fixedMesh.vertices = vertices;
            fixedMesh.RecalculateBounds();
            ReplaceMesh(clothing, fixedMesh);
            return new PenetrationResult(pushed, pushedMax);
        }

        /// <summary>
        /// 정점마다 메시 공간 -> 월드 행렬. 본에 묶인 메시는 본 섞음, 본 없는 소품은 오브젝트 행렬 하나.
        /// 위치를 구할 수 없는 정점(유효한 본이 없음)은 null.
        /// </summary>
        public static Matrix4x4?[] VertexMatrices(Renderer renderer, Mesh mesh)
        {
            var result = new Matrix4x4?[mesh.vertexCount];
            bool isSkinned = renderer is SkinnedMeshRenderer skinned && !FitAnalysis.IsUnskinned(skinned);
            if (!isSkinned)
            {
                Matrix4x4 rigid = renderer.transform.localToWorldMatrix;
                for (int i = 0; i < result.Length; i++) result[i] = rigid;
                return result;
            }

            var smr = (SkinnedMeshRenderer)renderer;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4?[] skin = SkinningMath.SkinMatrices(smr.bones, mesh.bindposes);
            for (int i = 0; i < result.Length && i < weights.Length; i++)
            {
                if (SkinningMath.TryBlend(skin, weights[i], out Matrix4x4 m)) result[i] = m;
            }
            return result;
        }

        /// <summary>렌더러의 메시를 Undo 가능하게 바꾼다(스킨 메시 또는 MeshFilter).</summary>
        public static void ReplaceMesh(Renderer renderer, Mesh mesh)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                Undo.RecordObject(skinned, OutfitFitter.UndoName);
                skinned.sharedMesh = mesh;
                return;
            }
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null) return;
            Undo.RecordObject(filter, OutfitFitter.UndoName);
            filter.sharedMesh = mesh;
        }

        /// <summary>정점마다 밀어낼 이동량(메시 공간). 밀 필요가 없으면 영벡터.</summary>
        static Vector3[] PushDeltas(Matrix4x4?[] matrices, Vector3[] vertices, BodySurface surface, Transform targetRoot,
            float margin, float limit, bool[] isKept, out int pushed, out float maxPush)
        {
            var deltas = new Vector3[vertices.Length];
            pushed = 0;
            maxPush = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                bool isSkipped = !matrices[i].HasValue || (isKept != null && i < isKept.Length && isKept[i]);
                if (isSkipped) continue;
                Matrix4x4 m = matrices[i].Value;
                Vector3 inRoot = FitPlacement.ToRootFrame(targetRoot, m.MultiplyPoint3x4(vertices[i]));
                if (!surface.TryNearest(inRoot, out Vector3 point, out Vector3 normal)) continue;

                Vector3 delta = BodySurface.PushOut(inRoot, point, normal, margin, limit) - inRoot;
                if (delta == Vector3.zero) continue;

                // 루트 기준 축의 이동량을 월드로, 다시 메시(바인드) 공간으로 되돌려 정점에 반영한다.
                deltas[i] = m.inverse.MultiplyVector(targetRoot.rotation * delta);
                maxPush = Mathf.Max(maxPush, delta.magnitude);
                pushed++;
            }
            return deltas;
        }

        /// <summary>
        /// UV·하드엣지 이음새처럼 같은 위치에 겹친 정점을 하나로 묶어 같이 움직인 뒤 이웃에 나눈다.
        /// 묶지 않으면 이음새 한쪽만 밀려 옷이 이음새를 따라 갈라진다.
        /// </summary>
        public static Vector3[] SpreadWelded(Vector3[] deltas, Vector3[] positions, int[] triangles)
        {
            int[] groupOf = WeldGroups(positions, out int groupCount);
            var groupDeltas = new Vector3[groupCount];
            for (int i = 0; i < deltas.Length; i++)
            {
                // 묶인 정점 중 가장 많이 밀린 값을 묶음 전체에 쓴다(덜 밀린 쪽이 몸 안에 남지 않게).
                if (deltas[i].sqrMagnitude > groupDeltas[groupOf[i]].sqrMagnitude) groupDeltas[groupOf[i]] = deltas[i];
            }
            int[] groupTriangles = triangles.Select(t => groupOf[t]).ToArray();
            Vector3[] spread = SpreadToNeighbors(groupDeltas, groupTriangles, NeighborFalloff);
            return groupOf.Select(g => spread[g]).ToArray();
        }

        internal static int[] WeldGroups(Vector3[] positions, out int groupCount)
        {
            var groupByPosition = new Dictionary<Vector3, int>();
            var groupOf = new int[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                // -0과 +0은 같은 위치지만 해시가 달라 묶이지 않을 수 있어 +0으로 맞춘다(x + 0 = +0).
                Vector3 key = positions[i] + Vector3.zero;
                if (!groupByPosition.TryGetValue(key, out int g)) groupByPosition[key] = g = groupByPosition.Count;
                groupOf[i] = g;
            }
            groupCount = groupByPosition.Count;
            return groupOf;
        }

        /// <summary>
        /// 밀어낸 정점은 그대로 두고, 움직이지 않은 이웃 정점에 고리마다 이전 고리 이동량 평균 * 비율을 준다.
        /// 몇몇 정점만 튀어나오면 옷이 얼룩처럼 찢어져 보이므로 경계를 부드럽게 하기 위함.
        /// </summary>
        public static Vector3[] SpreadToNeighbors(Vector3[] deltas, int[] triangles, IReadOnlyList<float> ringFalloff)
        {
            List<int>[] neighbors = Neighbors(deltas.Length, triangles);
            var result = (Vector3[])deltas.Clone();
            var source = deltas.Select(d => d != Vector3.zero).ToArray();
            foreach (float factor in ringFalloff)
            {
                var ring = new List<(int index, Vector3 delta)>();
                for (int v = 0; v < result.Length; v++)
                {
                    if (result[v] != Vector3.zero) continue;
                    Vector3[] moved = neighbors[v].Where(n => source[n]).Select(n => result[n]).ToArray();
                    if (moved.Length > 0) ring.Add((v, moved.Aggregate(Vector3.zero, (a, b) => a + b) / moved.Length * factor));
                }
                foreach (var (index, delta) in ring) { result[index] = delta; source[index] = true; }
            }
            return result;
        }

        /// <summary>삼각형 서브메시의 인덱스만 모은다. 점·선 서브메시에 mesh.triangles를 쓰면 Unity가 오류를 낸다.</summary>
        internal static int[] TriangleIndices(Mesh mesh)
        {
            var indices = new List<int>();
            for (int i = 0; i < mesh.subMeshCount; i++)
            {
                if (mesh.GetTopology(i) == MeshTopology.Triangles) indices.AddRange(mesh.GetTriangles(i));
            }
            return indices.ToArray();
        }

        internal static List<int>[] Neighbors(int vertexCount, int[] triangles)
        {
            var neighbors = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++) neighbors[i] = new List<int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                Link(neighbors, triangles[t], triangles[t + 1]);
                Link(neighbors, triangles[t + 1], triangles[t + 2]);
                Link(neighbors, triangles[t + 2], triangles[t]);
            }
            return neighbors;
        }

        static void Link(List<int>[] neighbors, int a, int b)
        {
            if (a == b) return;
            if (!neighbors[a].Contains(b)) neighbors[a].Add(b);
            if (!neighbors[b].Contains(a)) neighbors[b].Add(a);
        }
    }
}
