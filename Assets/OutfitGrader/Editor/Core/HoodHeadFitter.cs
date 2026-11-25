using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 후드 두상 맞춤(PRD 11장 결정: 후드 범위 포함). 후드처럼 머리·목 본에 묶인 옷 정점을 머리 중심에서 멀어지는 쪽으로
    /// 모양 그대로 조금씩 옮겨, 머리 피부가 후드를 뚫는 삼각형을 줄인다. 이동은 이웃 두 고리에 나눠 목둘레 이음매가 벌어지지 않게 하고,
    /// 머리 관통이 줄고 몸 관통이 늘지 않는 거리만 받아들인다. 정점마다 머리 표면 밖으로 밀면 수는 줄지 않고 자기 교차만 늘었다(실측).
    /// </summary>
    public static class HoodHeadFitter
    {
        /// <summary>한 번에 옮기는 거리: 머리 폭 대비 비율(최대 거리까지 1%씩 시험)</summary>
        public const float StepToHeadWidth = 0.01f;
        /// <summary>최대 이동 거리: 머리 폭 대비 비율. 3%(inuinu 13mm)면 허리 위 등판 이음 부분에 목 피부가 점처럼, 5% 이상이면 띠처럼 드러났다(실측).</summary>
        public const float MaxShiftToHeadWidth = 0.02f;
        /// <summary>이동을 이웃 고리에 나누는 비율(관통 밀어내기와 같음)</summary>
        static readonly float[] NeighborFalloff = { 0.6f, 0.3f };

        /// <summary>
        /// 후드 정점을 옮긴 새 메시로 교체하고 (옮긴 거리, 머리 관통 전, 후)를 돌려준다. 옮기지 않으면 거리 0.
        /// head는 머리 피부 변 격자, body는 몸(몸통·팔) 변 격자(null이면 몸 관통 검사 생략), headCenter는 머리 본 위치(루트 기준).
        /// </summary>
        public static (float shift, int before, int after) Fit(SkinnedMeshRenderer garment, AvatarRig target, EdgeGrid head, EdgeGrid body,
            Vector3 headCenter, float headWidth, string meshName)
        {
            Mesh mesh = garment.sharedMesh;
            if (mesh == null || head == null || headWidth <= 0f) return (0f, 0, 0);
            Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(garment, mesh);
            Vector3[] vertices = mesh.vertices;
            Vector3[] positions = vertices.Select((v, i) => matrices[i].HasValue ? FitPlacement.ToRootFrame(target.Root, matrices[i].Value.MultiplyPoint3x4(v)) : Vector3.positiveInfinity).ToArray();
            BoneWeight[] weights = mesh.boneWeights;
            int[] triangles = PenetrationFixer.TriangleIndices(mesh);
            Transform[] bones = garment.bones;
            var partOfBone = new Dictionary<int, bool>();
            bool IsHoodBone(int bone) => partOfBone.TryGetValue(bone, out bool isHoodBone) ? isHoodBone
                : partOfBone[bone] = ClothPierceResolver.PartOf(target, bone >= 0 && bone < bones.Length ? bones[bone] : null) == "목/머리";
            bool[] isHood = Enumerable.Range(0, vertices.Length).Select(i => i < weights.Length && IsHoodBone(weights[i].boneIndex0) && matrices[i].HasValue).ToArray();
            int[] hoodTriangles = Enumerable.Range(0, triangles.Length / 3).Where(t => isHood[triangles[3 * t]] || isHood[triangles[3 * t + 1]] || isHood[triangles[3 * t + 2]]).ToArray();
            if (hoodTriangles.Length == 0) return (0f, 0, 0);

            Vector3 hoodCenter = Enumerable.Range(0, vertices.Length).Where(i => isHood[i]).Aggregate(Vector3.zero, (s, i) => s + positions[i]) / isHood.Count(x => x);
            Vector3 away = hoodCenter - headCenter;
            if (away.sqrMagnitude < 1e-12f) return (0f, 0, 0);
            away.Normalize();

            int Count(EdgeGrid grid, Vector3[] p) => grid == null ? 0 : hoodTriangles.Count(t => grid.Pierces(p[triangles[3 * t]], p[triangles[3 * t + 1]], p[triangles[3 * t + 2]]));
            int headBefore = Count(head, positions), bodyBefore = Count(body, positions);
            if (headBefore == 0) return (0f, 0, 0);

            // 같은 위치에 겹친 이음매 정점은 묶음 하나로 함께 옮긴다(따로 나누면 UV 이음매를 따라 옷이 갈라짐).
            int[] groupOf = PenetrationFixer.WeldGroups(vertices, out int groupCount);
            var groupUnit = new Vector3[groupCount];
            for (int i = 0; i < vertices.Length; i++) if (isHood[i]) groupUnit[groupOf[i]] = away;
            Vector3[] groupSpread = PenetrationFixer.SpreadToNeighbors(groupUnit, triangles.Select(v => groupOf[v]).ToArray(), NeighborFalloff);
            // 옮길 수 없는 정점(역행렬 없음)은 평가에서도 그대로 둔다(보고 수치와 결과가 같게).
            Vector3[] spread = Enumerable.Range(0, vertices.Length)
                .Select(i => matrices[i].HasValue && Mathf.Abs(matrices[i].Value.determinant) >= SurfaceOffsetFitter.MinDeterminant ? groupSpread[groupOf[i]] : Vector3.zero).ToArray();
            float step = headWidth * StepToHeadWidth, maxShift = headWidth * MaxShiftToHeadWidth;
            float bestShift = 0f;
            int bestHead = headBefore;
            for (float shift = step; shift <= maxShift + 1e-6f; shift += step)
            {
                Vector3[] moved = positions.Select((p, i) => p + spread[i] * shift).ToArray();
                int headNow = Count(head, moved), bodyNow = Count(body, moved);
                bool isBetter = headNow < bestHead && bodyNow <= bodyBefore;
                if (!isBetter) continue;
                bestHead = headNow;
                bestShift = shift;
            }
            if (bestShift <= 0f) return (0f, headBefore, headBefore);

            Quaternion toWorld = target.Root.rotation;
            for (int i = 0; i < vertices.Length; i++)
            {
                if (spread[i] != Vector3.zero) vertices[i] += matrices[i].Value.inverse.MultiplyVector(toWorld * (spread[i] * bestShift));
            }
            Mesh fitted = Object.Instantiate(mesh);
            fitted.name = meshName;
            fitted.vertices = vertices;
            fitted.RecalculateBounds();
            PenetrationFixer.ReplaceMesh(garment, fitted);
            return (bestShift, headBefore, bestHead);
        }

        /// <summary>
        /// 머리·목 피부 변(루트 기준). 머리와 몸통 정점을 함께 가진 몸 메시에서, 세 정점의 주 본이 모두 Humanoid 머리·목 본 자신인 삼각형만 쓴다.
        /// 머리카락 메시는 머리 본에 직접 묶여 있어도 몸통 정점이 없어 빠진다(넣으면 후드를 목 피부 쪽으로 밀어 Lumelana 관통 86 -> 179).
        /// 그런 메시가 여럿이면(이미 입은 바디수트 등) 머리 정점이 가장 많은 메시 하나를 몸으로 본다. 이번에 입힌 의상은 뺀다.
        /// </summary>
        public static List<(Vector3 a, Vector3 b)> HeadEdges(AvatarRig target, IReadOnlyCollection<Transform> excluded, SkinCache skins = null)
        {
            var edges = new List<(Vector3, Vector3)>();
            Transform head = target.GetHuman(HumanBodyBones.Head), neck = target.GetHuman(HumanBodyBones.Neck);
            if (head == null) return edges;
            var torso = new HashSet<Transform>(new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest }.Select(target.GetHuman).Where(t => t != null));
            SkinnedMeshRenderer skin = null;
            int skinHeadCount = 0;
            foreach (SkinnedMeshRenderer body in target.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                bool isExcluded = excluded.Any(e => e != null && body.transform.IsChildOf(e));
                if (isExcluded || !body.enabled || body.sharedMesh == null || FitAnalysis.IsUnskinned(body)) continue;
                Mesh mesh = body.sharedMesh;
                BoneWeight[] weights = mesh.boneWeights;
                // renderer.bones는 읽을 때마다 배열을 새로 만들어 한 번만 읽는다.
                Transform[] bones = body.bones;
                Transform MainBone(int v) => v < weights.Length && weights[v].boneIndex0 < bones.Length ? bones[weights[v].boneIndex0] : null;
                if (!bones.Contains(head) || !bones.Any(torso.Contains)) continue;
                int headCount = 0;
                bool hasTorso = false;
                for (int v = 0; v < weights.Length; v++)
                {
                    Transform main = MainBone(v);
                    if (main == head) headCount++;
                    else if (torso.Contains(main)) hasTorso = true;
                }
                if (!hasTorso || headCount <= skinHeadCount) continue;
                skin = body;
                skinHeadCount = headCount;
            }
            if (skin == null) return edges;
            {
                SkinnedMeshRenderer body = skin;
                Mesh mesh = body.sharedMesh;
                BoneWeight[] weights = mesh.boneWeights;
                Transform[] bones = body.bones;
                Transform MainBone(int v) => v < weights.Length && weights[v].boneIndex0 < bones.Length ? bones[weights[v].boneIndex0] : null;
                Matrix4x4?[] matrices = SkinCache.Of(skins, body, mesh);
                Vector3[] vertices = mesh.vertices;
                bool IsHead(int v) => (MainBone(v) == head || (neck != null && MainBone(v) == neck)) && matrices[v].HasValue;
                Vector3 P(int v) => FitPlacement.ToRootFrame(target.Root, matrices[v].Value.MultiplyPoint3x4(vertices[v]));
                int[] tris = PenetrationFixer.TriangleIndices(mesh);
                for (int t = 0; t + 2 < tris.Length; t += 3)
                {
                    int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                    if (!IsHead(a) || !IsHead(b) || !IsHead(c)) continue;
                    edges.Add((P(a), P(b)));
                    edges.Add((P(b), P(c)));
                    edges.Add((P(c), P(a)));
                }
            }
            return edges.Where(e => (e.Item2 - e.Item1).sqrMagnitude > EdgeGrid.MinEdgeLength * EdgeGrid.MinEdgeLength).ToList();
        }
    }
}
