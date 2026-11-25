using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 몸(피부·대상이 원래 입던 속옷)의 변이 실제로 뚫고 지나간 옷 삼각형만 찾아, 그 정점을 옷 바깥 법선 방향으로 조금씩 밀고
    /// 뚫림이 더 줄지 않을 때까지 반복한다(표면 간격 보존 맞춤의 마무리 정리). 밀기 양은 PushField가 이웃과 완만하게 고른다.
    /// 정점 평면 판정은 정점 사이 넓은 면을 볼록한 속옷 컵이 뚫는 경우를 놓쳐 정확한 선분-삼각형 교차로 판정한다.
    /// 실험에서 Lumelana 속옷 관통을 201 -> 64개로 줄였다.
    /// </summary>
    public static class ClothPierceResolver
    {
        /// <summary>
        /// 한 번에 미는 거리: 대상 몸통 원통 반지름(칸 중앙값) 대비 비율(Lumelana 81.8mm에서 약 3mm).
        /// 엉덩이 높이 기준은 다리가 짧은 치비에서 너무 작아(inuinu 1.0mm) 성긴 몸 면을 넘지 못하고 멈췄다.
        /// 거리를 키워도 가시가 생기지 않는 것은 PushField가 이동량을 완만하게 고르기 때문이다.
        /// </summary>
        public const float StepToTorsoRadius = 0.037f;
        /// <summary>몸통 원통이 없을 때 쓰는 한 번에 미는 거리: 엉덩이 높이 대비 비율</summary>
        public const float StepToHipHeight = 0.0066f;
        /// <summary>
        /// 반복 상한. 뚫림이 늘지 않는 동안 계속한다. 한 번에 묶음마다 최대 한 번 미는 거리만 더하고 언덕 고르기는 최댓값을 넘지 않아,
        /// 누적 거리는 최대 12 x 3.7% = 몸통 반지름의 44%다(Lumelana 약 36mm, inuinu 약 54mm).
        /// </summary>
        public const int MaxIterations = 12;

        /// <summary>
        /// 옷 법선과 가장 가까운 몸 표면의 바깥 법선이 이 코사인(60도)보다 어긋나면 몸 바깥 법선으로 민다.
        /// inuinu 등판 위쪽은 옷이 수평으로 접혀 법선이 위(y 0.97)를 향했고, 그 방향으로는 46~64mm를 밀어야 몸을 빠져나와
        /// 누적 한도(약 43mm)를 넘어 몸이 옷을 뚫은 채 남았다.
        /// </summary>
        public const float AlignedCosine = 0.5f;

        /// <summary>
        /// 남은 관통 정리에서 삼각형 하나를 옮길 수 있는 최대 횟수(한 번 미는 거리 단위). 이웃에 나눠 주지 않고 그 삼각형만 옮기므로 길면 가시가 된다
        /// (inuinu 겨드랑이 한 곳이 43mm 옮겨져 소매 아래 가시). 등판 수평 접힘은 1번(약 4mm)으로 풀렸다.
        /// </summary>
        public const int MaxClearSteps = 3;

        /// <summary>한 번에 미는 거리(월드 단위). 몸통 원통이 있으면 그 굵기, 없으면 엉덩이 높이 기준.</summary>
        public static float StepOf(BodyCylinders target, float hipHeight)
        {
            float torsoRadius = target != null ? target.MedianRadius(BodyRegion.Torso) : 0f;
            return torsoRadius > 0f ? torsoRadius * StepToTorsoRadius : hipHeight * StepToHipHeight;
        }

        /// <summary>
        /// 대상 몸 메시의 변(루트 기준 축). 세 정점의 주 본이 모두 상의 부위(몸통·팔·허벅지, 가슴 흔들림 본 포함)인 삼각형만 쓴다.
        /// 머리카락·후드 주변·꼬리·털은 빼서(범위 밖이거나 따로 처리) 옷을 그쪽으로 밀지 않는다.
        /// </summary>
        public static List<(Vector3 a, Vector3 b)> BodyEdges(AvatarRig target, IReadOnlyCollection<Transform> excluded, SkinCache skins = null)
        {
            var edges = new List<(Vector3, Vector3)>();
            foreach (SkinnedMeshRenderer body in target.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                bool isExcluded = excluded.Any(e => e != null && body.transform.IsChildOf(e));
                bool isRigid = FitAnalysis.IsUnskinned(body);
                if (isExcluded || isRigid || !body.enabled || body.sharedMesh == null) continue;
                AddBodyEdges(target, body, edges, skins);
            }
            return edges.Where(e => (e.Item2 - e.Item1).sqrMagnitude > EdgeGrid.MinEdgeLength * EdgeGrid.MinEdgeLength).ToList();
        }

        static void AddBodyEdges(AvatarRig target, SkinnedMeshRenderer body, List<(Vector3, Vector3)> edges, SkinCache skins)
        {
            Mesh mesh = body.sharedMesh;
            bool[] isBodyBone = body.bones.Select(b => b != null && BodyCylinders.RegionOfBone(target, b) != BodyRegion.None).ToArray();
            Matrix4x4?[] matrices = SkinCache.Of(skins, body, mesh);
            var frame = new FitPlacement.RootFrame(target.Root);
            Vector3[] positions = mesh.vertices.Select((v, i) => RootPosition(v, matrices[i], frame)).ToArray();
            BoneWeight[] weights = mesh.boneWeights;
            bool IsBody(int v) => v < weights.Length && weights[v].boneIndex0 < isBodyBone.Length && isBodyBone[weights[v].boneIndex0] && !float.IsInfinity(positions[v].x);
            // 이웃 삼각형이 공유하는 변은 한 번만 담는다(판정 결과는 같고 격자·판정량이 절반 가까이 준다).
            var seen = new HashSet<(int, int)>();
            void AddEdge(int a, int b)
            {
                if (seen.Add((Mathf.Min(a, b), Mathf.Max(a, b)))) edges.Add((positions[a], positions[b]));
            }
            foreach (int[] tri in TrianglesOf(mesh))
            {
                if (!IsBody(tri[0]) || !IsBody(tri[1]) || !IsBody(tri[2])) continue;
                AddEdge(tri[0], tri[1]);
                AddEdge(tri[1], tri[2]);
                AddEdge(tri[2], tri[0]);
            }
        }

        /// <summary>옷 메시의 뚫린 삼각형을 밀어 정리한 새 메시로 교체한다. (처음 뚫린 삼각형 수, 정리 뒤 수)를 돌려준다.</summary>
        /// <param name="isLocked">움직이지 않을 정점(통째로 배치한 끈 등). null이면 없음. 정점마다 밀면 모양이 구겨진다.</param>
        /// <param name="surface">몸 표면(루트 기준). 있으면 옷 법선이 몸 바깥과 크게 어긋난 정점은 몸 바깥 법선으로 민다.</param>
        /// <param name="cylinders">대상 몸 원통 표. 근처에 몸 정점이 없을 때(성긴 저폴리 몸) 남은 관통을 정리할 바깥 방향으로 쓴다.</param>
        public static (int before, int after) Resolve(SkinnedMeshRenderer garment, EdgeGrid body, Transform root, float step, string meshName,
            bool[] isLocked = null, BodySurface surface = null, BodyCylinders cylinders = null)
        {
            Mesh mesh = garment.sharedMesh;
            if (mesh == null || mesh.normals.Length != mesh.vertexCount) return (0, 0);

            Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(garment, mesh);
            Vector3[] original = mesh.vertices, vertices = original;
            int[] triangles = TrianglesOf(mesh).SelectMany(t => t).ToArray();
            var frame = new FitPlacement.RootFrame(root);
            Vector3[] positions = vertices.Select((v, i) => RootPosition(v, matrices[i], frame)).ToArray();
            Vector3[] normals = WorldNormals(mesh.normals, matrices);
            if (surface != null) normals = TowardBodyOutside(normals, positions, surface, root, step * MaxIterations);
            var field = new PushField(original, triangles, normals);
            var isPierced = new bool[triangles.Length / 3];
            int before = Recount(positions, triangles, body, isPierced, null);
            Vector3[] best = vertices;
            int after = before, current = before;
            // 정점이 성긴 몸은 한 번 밀어서는 줄지 않을 수 있어, 늘어나지 않는 동안 계속 밀고 가장 적었던 결과를 남긴다.
            for (int iteration = 0; iteration < MaxIterations && current > 0; iteration++)
            {
                bool[] isMoved = field.Push(isPierced, step);
                if (isLocked != null)
                    for (int i = 0; i < isMoved.Length && i < isLocked.Length; i++) isMoved[i] &= !isLocked[i];
                vertices = vertices.Select((x, i) => isMoved[i] ? Moved(original[i], matrices[i], field.WorldDelta(i)) : x).ToArray();
                for (int i = 0; i < vertices.Length; i++)
                    if (isMoved[i]) positions[i] = RootPosition(vertices[i], matrices[i], frame);
                int next = Recount(positions, triangles, body, isPierced, isMoved);
                bool isWorse = next > current;
                if (isWorse) break;
                current = next;
                if (current < after) { after = current; best = vertices; }
            }
            if ((surface != null || cylinders != null) && after > 0)
            {
                // best는 원본 정점 배열과 같을 수 있어 복사해 고친다.
                best = (Vector3[])best.Clone();
                after -= ClearRemaining(best, original, matrices, triangles, body, p => OutwardAt(p, surface, cylinders, step * MaxIterations), root, step, isLocked);
            }
            if (after < before) Replace(garment, mesh, best, meshName);
            return (before, after);
        }

        /// <summary>
        /// 몸 변이 아직 뚫고 있는 옷 삼각형 수를 부위별로 센 보고 문구("몸통 3, 손 2"). 없으면 빈 문자열(PRD G5: 자동으로 처리하지 못한 부분을 숨기지 않음).
        /// 삼각형의 부위는 첫 정점의 주 본에서 가장 가까운 Humanoid 조상으로 정한다.
        /// </summary>
        public static string RemainingByPart(SkinnedMeshRenderer garment, EdgeGrid body, AvatarRig target)
        {
            Mesh mesh = garment.sharedMesh;
            if (mesh == null || body == null) return "";
            Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(garment, mesh);
            var frame = new FitPlacement.RootFrame(target.Root);
            Vector3[] positions = mesh.vertices.Select((v, i) => RootPosition(v, matrices[i], frame)).ToArray();
            BoneWeight[] weights = mesh.boneWeights;
            int[] triangles = TrianglesOf(mesh).SelectMany(t => t).ToArray();
            var counts = new Dictionary<string, int>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                if (!body.Pierces(positions[triangles[t]], positions[triangles[t + 1]], positions[triangles[t + 2]])) continue;
                int boneIndex = triangles[t] < weights.Length ? weights[triangles[t]].boneIndex0 : -1;
                Transform bone = boneIndex >= 0 && boneIndex < garment.bones.Length ? garment.bones[boneIndex] : null;
                string part = PartOf(target, bone);
                counts[part] = counts.TryGetValue(part, out int c) ? c + 1 : 1;
            }
            return string.Join(", ", counts.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value}"));
        }

        /// <summary>본에서 가장 가까운 Humanoid 조상으로 정한 부위 이름(몸통·목/머리·어깨/팔·손·다리, 못 찾으면 기타)</summary>
        public static string PartOf(AvatarRig target, Transform bone)
        {
            for (Transform t = bone; t != null; t = t.parent)
            {
                if (!target.TryGetHuman(t, out HumanBodyBones human)) continue;
                switch (human)
                {
                    case HumanBodyBones.Hips: case HumanBodyBones.Spine: case HumanBodyBones.Chest: case HumanBodyBones.UpperChest: return "몸통";
                    case HumanBodyBones.Neck: case HumanBodyBones.Head: return "목/머리";
                    case HumanBodyBones.LeftHand: case HumanBodyBones.RightHand: return "손";
                    case HumanBodyBones.LeftShoulder: case HumanBodyBones.RightShoulder: case HumanBodyBones.LeftUpperArm: case HumanBodyBones.RightUpperArm:
                    case HumanBodyBones.LeftLowerArm: case HumanBodyBones.RightLowerArm: return "어깨/팔";
                    case HumanBodyBones.LeftUpperLeg: case HumanBodyBones.RightUpperLeg: case HumanBodyBones.LeftLowerLeg: case HumanBodyBones.RightLowerLeg:
                    case HumanBodyBones.LeftFoot: case HumanBodyBones.RightFoot: return "다리";
                    default: return human.ToString().Contains("Hand") || human.ToString().Contains("Thumb") || human.ToString().Contains("Index")
                        || human.ToString().Contains("Middle") || human.ToString().Contains("Ring") || human.ToString().Contains("Little") ? "손" : "기타";
                }
            }
            return "기타";
        }

        /// <summary>정점마다 월드 바깥 법선. 위치를 못 구하거나 역행렬이 없는 정점은 영벡터(밀지 않음).</summary>
        static Vector3[] WorldNormals(Vector3[] normals, Matrix4x4?[] matrices)
        {
            return normals.Select((n, i) => IsInvertible(matrices[i]) ? matrices[i].Value.MultiplyVector(n).normalized : Vector3.zero).ToArray();
        }

        /// <summary>
        /// 옷 법선(월드)을 가장 가까운 몸 표면의 바깥 쪽으로 맞춘다. 몸 바깥 법선과 AlignedCosine보다 어긋나면 몸 바깥 법선(월드)으로 바꾸고,
        /// 어긋나지 않은 뒷면 법선(몸 안쪽을 향함)은 뒤집는다. 양면 옷 앞·뒷면과 바꾼 정점이 한 묶음에서 모두 바깥을 향해야
        /// PushField가 묶음 첫 정점 순서와 상관없이 바깥으로 민다. 몸 점이 maxDistance보다 멀면(겨드랑이 등에서 다른 부위를 짚음) 그대로 둔다.
        /// 몸 표면 바깥에 있는 정점도 그대로 둔다. 바깥 정점까지 바꾸면 겨드랑이·엉덩이 뒤 모서리에서 팔·엉덩이 아랫면을 짚어 아래로 바뀌고,
        /// 밀기가 이웃으로 퍼질 때 옷판이 아래로 미끄러졌다(inuinu 풀어 주기 켬 관통 204 -> 215, 안쪽만 바꾸면 205).
        /// </summary>
        public static Vector3[] TowardBodyOutside(Vector3[] worldNormals, Vector3[] positions, BodySurface surface, Transform root, float maxDistance)
        {
            var result = (Vector3[])worldNormals.Clone();
            for (int i = 0; i < result.Length; i++)
            {
                bool canCheck = result[i] != Vector3.zero && !float.IsInfinity(positions[i].x);
                if (!canCheck || !surface.TryNearest(positions[i], out Vector3 point, out Vector3 outward)) continue;
                if ((point - positions[i]).sqrMagnitude > maxDistance * maxDistance) continue;
                bool isInsideBody = Vector3.Dot(positions[i] - point, outward) < 0f;
                if (!isInsideBody) continue;
                Vector3 outwardWorld = (root.rotation * outward).normalized;
                float alignment = Vector3.Dot(result[i], outwardWorld);
                bool isMisaligned = Mathf.Abs(alignment) < AlignedCosine;
                if (isMisaligned) result[i] = outwardWorld;
                else if (alignment < 0f) result[i] = -result[i];
            }
            return result;
        }

        /// <summary>
        /// 점(루트 기준)의 몸 바깥 방향(루트 기준). 가장 가까운 몸 표면 법선을 쓰고, 근처에 몸 정점이 없으면(inuinu 등판은 7~8mm 안에 몸 정점이 없음)
        /// 몸통 원통 축에서 바깥쪽 방향을 쓴다. 몸 정점이 maxDistance보다 멀면(겨드랑이에서 팔 아랫면을 짚는 등) 원통 방향을 쓴다. 둘 다 없으면 null.
        /// </summary>
        public static Vector3? OutwardAt(Vector3 point, BodySurface surface, BodyCylinders cylinders, float maxDistance)
        {
            Vector3 nearest = default, normal = default;
            bool hasNear = surface != null && surface.TryNearest(point, out nearest, out normal) && (nearest - point).sqrMagnitude <= maxDistance * maxDistance;
            if (hasNear) return normal;
            if (cylinders != null && cylinders.TryOutward(BodyRegion.Torso, point, out Vector3 radial)) return radial;
            return null;
        }

        /// <summary>
        /// 전체 밀기가 끝난 뒤에도 뚫린 삼각형을 하나씩 정리한다. 그 삼각형 정점 묶음을 몸 바깥 방향(OutwardAt)으로 step씩(최대 MaxClearSteps번) 옮겨,
        /// 그 삼각형이 풀리고 정점이 닿은 삼각형 중 새로 뚫리는 것이 없을 때만 받아들인다. 전체 밀기는 옷 전체 관통 수가 늘면 멈춰
        /// (inuinu는 첫 번 밀기 뒤 멈춤, 약 3.6mm) 수평으로 접힌 등판처럼 더 밀어야 하는 곳이 남았다(허리 위 가는 선). 정리한 삼각형 수를 돌려준다.
        /// vertices(메시 공간)를 고쳐 쓴다.
        /// </summary>
        public static int ClearRemaining(Vector3[] vertices, Vector3[] original, Matrix4x4?[] matrices, int[] triangles, EdgeGrid body,
            System.Func<Vector3, Vector3?> outwardAt, Transform root, float step, bool[] isLocked)
        {
            Vector3[] positions = vertices.Select((v, i) => RootPosition(v, matrices[i], root)).ToArray();
            int[] groupOf = PenetrationFixer.WeldGroups(original, out int groupCount);
            var membersOf = new List<int>[groupCount];
            for (int v = 0; v < groupOf.Length; v++) (membersOf[groupOf[v]] ??= new List<int>()).Add(v);
            var trianglesOf = new List<int>[groupCount];
            for (int t = 0; t < triangles.Length / 3; t++)
                for (int k = 0; k < 3; k++) (trianglesOf[groupOf[triangles[3 * t + k]]] ??= new List<int>()).Add(t);
            bool Pierces(int t) => body.Pierces(positions[triangles[3 * t]], positions[triangles[3 * t + 1]], positions[triangles[3 * t + 2]]);

            // 묶음마다 이미 옮긴 거리. 여러 삼각형이 공유하는 묶음도 합쳐서 step x MaxClearSteps를 넘지 않게 한다(넘으면 가시).
            var movedOf = new float[groupCount];
            float maxMove = step * MaxClearSteps;
            int cleared = 0;
            for (int t = 0; t < triangles.Length / 3; t++)
            {
                if (!Pierces(t)) continue;
                int[] groups = Enumerable.Range(0, 3).Select(k => groupOf[triangles[3 * t + k]]).Distinct().ToArray();
                int[] members = groups.SelectMany(g => membersOf[g]).ToArray();
                bool isMovable = members.All(v => IsInvertible(matrices[v]) && !float.IsInfinity(positions[v].x) && (isLocked == null || v >= isLocked.Length || !isLocked[v]));
                if (!isMovable) continue;
                // 묶음마다 한 방향(첫 정점 기준 몸 바깥 법선, 루트 기준)
                var direction = new Dictionary<int, Vector3>();
                foreach (int g in groups)
                    if (outwardAt(positions[membersOf[g][0]]) is Vector3 outward) direction[g] = outward;
                if (direction.Count < groups.Length) continue;
                int[] touched = groups.SelectMany(g => trianglesOf[g]).Distinct().Where(n => n != t).ToArray();
                var wasPierced = new HashSet<int>(touched.Where(Pierces));
                Vector3[] start = members.Select(v => positions[v]).ToArray();
                bool isAccepted = false;
                for (int k = 1; k <= MaxClearSteps && !isAccepted; k++)
                {
                    float distance = step * k;
                    bool isWithinLimit = groups.All(g => movedOf[g] + distance <= maxMove + 1e-6f);
                    if (!isWithinLimit) break;
                    for (int m = 0; m < members.Length; m++) positions[members[m]] = start[m] + direction[groupOf[members[m]]] * distance;
                    isAccepted = !Pierces(t) && touched.All(n => wasPierced.Contains(n) || !Pierces(n));
                    if (!isAccepted) continue;
                    foreach (int g in groups) movedOf[g] += distance;
                    foreach (int v in members)
                        vertices[v] = original[v] + matrices[v].Value.inverse.MultiplyVector(root.rotation * (positions[v] - RootPosition(original[v], matrices[v], root)));
                    cleared++;
                }
                // 받아들이지 않았으면 판정용 위치를 반드시 되돌린다(마지막 시도 위치가 남으면 실제 메시와 어긋남).
                if (!isAccepted)
                    for (int m = 0; m < members.Length; m++) positions[members[m]] = start[m];
            }
            return cleared;
        }

        /// <summary>원래 정점을 월드 이동량만큼 옮긴 메시 공간 위치</summary>
        static Vector3 Moved(Vector3 original, Matrix4x4? matrix, Vector3 worldDelta)
        {
            return IsInvertible(matrix) ? original + matrix.Value.inverse.MultiplyVector(worldDelta) : original;
        }

        static bool IsInvertible(Matrix4x4? matrix) => matrix.HasValue && Mathf.Abs(matrix.Value.determinant) >= SurfaceOffsetFitter.MinDeterminant;

        /// <summary>
        /// 삼각형별 뚫림을 다시 판정하고 전체 뚫린 수를 돌려준다. isMoved가 있으면 움직인 정점이 닿은 삼각형만 다시 판정한다
        /// (나머지는 위치가 그대로라 결과도 같다. 매번 전체를 판정하면 옷 하나에 1초 넘게 걸렸다).
        /// </summary>
        static int Recount(Vector3[] positions, int[] triangles, EdgeGrid body, bool[] isPierced, bool[] isMoved)
        {
            int count = 0;
            for (int t = 0; t < isPierced.Length; t++)
            {
                int a = triangles[t * 3], b = triangles[t * 3 + 1], c = triangles[t * 3 + 2];
                bool isChanged = isMoved == null || isMoved[a] || isMoved[b] || isMoved[c];
                if (isChanged) isPierced[t] = body.Pierces(positions[a], positions[b], positions[c]);
                if (isPierced[t]) count++;
            }
            return count;
        }

        static Vector3 RootPosition(Vector3 vertex, Matrix4x4? matrix, FitPlacement.RootFrame frame)
        {
            return matrix.HasValue ? frame.Of(matrix.Value.MultiplyPoint3x4(vertex)) : Vector3.positiveInfinity;
        }

        static Vector3 RootPosition(Vector3 vertex, Matrix4x4? matrix, Transform root)
        {
            return matrix.HasValue ? FitPlacement.ToRootFrame(root, matrix.Value.MultiplyPoint3x4(vertex)) : Vector3.positiveInfinity;
        }

        static void Replace(SkinnedMeshRenderer garment, Mesh mesh, Vector3[] vertices, string meshName)
        {
            Mesh resolved = Object.Instantiate(mesh);
            resolved.name = meshName;
            resolved.vertices = vertices;
            resolved.RecalculateBounds();
            PenetrationFixer.ReplaceMesh(garment, resolved);
        }

        static IEnumerable<int[]> TrianglesOf(Mesh mesh)
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
