using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 수인·퍼리 아바타의 메시 털(본마다 따로 붙은 털 묶음)이 새로 입힌 옷을 뚫고 나오지 않게, 뿌리가 옷 안쪽에 묻힌
    /// 털 묶음의 삼각형을 대상 아바타의 새 메시에서 지운다. Modular Avatar의 Shape Changer(Delete)·Mesh Cutter(By Bone)가
    /// "옷에 가려지는 본체 부분을 지운다"는 것과 같은 일. 원본 메시는 바꾸지 않고, Undo로 되돌린다.
    /// 옷을 털 위로 부풀리면 털 길이(수 cm)만큼 옷이 뜨고, 털 본 크기를 0으로 줄이면 PhysBone이 깨져서 지우는 쪽을 쓴다.
    /// ponytail: 이름 규칙을 따르지 않는 털·셰이더로 그리는 털(lilToon Fur)은 다루지 않는다.
    /// </summary>
    public static class FurHider
    {
        /// <summary>털 묶음 뿌리와 옷 사이 최대 거리: 엉덩이 높이 대비 비율. 이보다 멀면 옷에 가려진 것으로 보지 않는다.</summary>
        public const float RootReachToHipHeight = 0.08f;
        /// <summary>
        /// 털 묶음 뿌리와 옷 사이 최대 거리: 대상 몸통 원통 반지름(칸 중앙값) 대비 비율. 표면 간격 보존은 원본에서 옷이 몸과 떨어져 있던
        /// 간격을 살려 옷이 털 뿌리에서 멀어질 수 있어(Lumelana 가슴 털 38mm, 엉덩이 기준 한도 37mm), 몸 굵기 기준 한도도 함께 쓴다.
        /// 뿌리가 옷 면 뒤쪽인지 함께 보므로 옷 밖으로 나온 털은 지우지 않는다.
        /// </summary>
        public const float RootReachToTorsoRadius = 0.6f;

        /// <summary>털 본 이름: FurChest, FurLowerArm.001.L, Fur_01, ChestFluff, もふ. 한 단어일 때만(Further·Furniture·FluffyHair 제외).</summary>
        internal static readonly Regex FurBoneName = new Regex(
            @"(?:(?<![A-Za-z])([Ff][Uu][Rr]|[Ff][Ll][Uu][Ff][Ff])|(?<=[a-z])(F[Uu][Rr]|F[Ll][Uu][Ff][Ff]))(?![a-z])|もふ|モフ");

        /// <summary>
        /// 새 옷(garments) 안쪽에 뿌리가 묻힌 털 묶음을 지운다. 렌더러마다 (이름, 지운 털 묶음 수, 지운 삼각형 수)를 돌려준다.
        /// </summary>
        /// <param name="torsoRadius">대상 몸통 원통 반지름(월드 단위). 0이면 엉덩이 높이 기준 한도만 쓴다.</param>
        public static List<(string renderer, int tufts, int triangles)> Hide(AvatarRig target, IReadOnlyList<Renderer> garments, float torsoRadius = 0f)
        {
            var hidden = new List<(string, int, int)>();
            float reach = Mathf.Max(RegionMeasurer.HipHeight(target) * RootReachToHipHeight, torsoRadius * RootReachToTorsoRadius);
            BodySurface garmentSurface = GarmentSurface(garments, target.Root, reach);
            if (garmentSurface.Count == 0) return hidden;

            Transform[] garmentTransforms = garments.Where(g => g != null).Select(g => g.transform).ToArray();
            foreach (SkinnedMeshRenderer renderer in target.Root.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                bool isGarment = garmentTransforms.Any(g => renderer.transform.IsChildOf(g));
                if (isGarment || !renderer.enabled || renderer.sharedMesh == null) continue;

                (int tufts, int triangles) = HideCoveredTufts(target, renderer, garmentSurface, reach);
                if (triangles > 0) hidden.Add((renderer.name, tufts, triangles));
            }
            return hidden;
        }

        /// <summary>옷 정점과 바깥 법선(루트 기준 축). 털 뿌리가 옷 안쪽인지 판정하는 데 쓴다.</summary>
        static BodySurface GarmentSurface(IReadOnlyList<Renderer> garments, Transform root, float cell)
        {
            var surface = new List<(Vector3, Vector3)>();
            Quaternion toRoot = Quaternion.Inverse(root.rotation);
            foreach (Renderer garment in garments.Where(g => g != null))
            {
                Mesh mesh = FitAnalysis.MeshOf(garment);
                if (mesh == null || mesh.normals.Length != mesh.vertexCount) continue;

                Matrix4x4?[] matrices = PenetrationFixer.VertexMatrices(garment, mesh);
                Vector3[] vertices = mesh.vertices, normals = mesh.normals;
                for (int i = 0; i < vertices.Length; i++)
                {
                    if (!matrices[i].HasValue) continue;
                    Matrix4x4 m = matrices[i].Value;
                    surface.Add((FitPlacement.ToRootFrame(root, m.MultiplyPoint3x4(vertices[i])), (toRoot * m.MultiplyVector(normals[i])).normalized));
                }
            }
            return new BodySurface(surface, cell);
        }

        /// <summary>렌더러 하나에서 뿌리가 옷 안쪽인 털 묶음의 삼각형(세 정점 모두 그 묶음)을 지운다.</summary>
        static (int tufts, int triangles) HideCoveredTufts(AvatarRig target, SkinnedMeshRenderer renderer, BodySurface garmentSurface, float reach)
        {
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            BoneWeight[] weights = mesh.boneWeights;
            var weightedBones = new HashSet<Transform>(weights.Select(w => w.boneIndex0).Where(i => i < bones.Length && bones[i] != null).Select(i => bones[i]));
            Transform[] tuftRoots = bones.Select(b => TuftRoot(target, b, weightedBones)).ToArray();
            var covered = new HashSet<Transform>(tuftRoots.Where(r => r != null).Distinct()
                .Where(r => IsInsideGarment(FitPlacement.ToRootFrame(target.Root, r.position), garmentSurface, reach)));
            if (covered.Count == 0) return (0, 0);

            bool[] isHidden = weights.Select(w => w.boneIndex0 < tuftRoots.Length && tuftRoots[w.boneIndex0] != null && covered.Contains(tuftRoots[w.boneIndex0])).ToArray();
            Mesh trimmed = Object.Instantiate(mesh);
            trimmed.name = $"{target.Root.name}_{renderer.name}_FurHidden";
            int removed = 0;
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                int[] tri = mesh.GetTriangles(s);
                var kept = new List<int>(tri.Length);
                for (int t = 0; t + 2 < tri.Length; t += 3)
                {
                    bool isTuftTriangle = isHidden[tri[t]] && isHidden[tri[t + 1]] && isHidden[tri[t + 2]];
                    if (isTuftTriangle) { removed++; continue; }
                    kept.Add(tri[t]); kept.Add(tri[t + 1]); kept.Add(tri[t + 2]);
                }
                trimmed.SetTriangles(kept, s);
            }
            if (removed == 0)
            {
                // 털 묶음 정점이 몸 본과 섞여 지울 삼각형이 없으면 메시를 바꾸지 않는다(불필요한 메시 에셋 방지).
                Object.DestroyImmediate(trimmed);
                return (0, 0);
            }
            PenetrationFixer.ReplaceMesh(renderer, trimmed);
            return (covered.Count, removed);
        }

        /// <summary>
        /// 털 본이 속한 털 묶음의 뿌리: Humanoid가 아닌 조상을 따라 가장 위쪽 털 이름 본까지 올라가며, 정점이 묶인 가장 위쪽 본.
        /// 정점이 없는 정리용 본(FurSupport 등)은 여러 묶음을 하나로 합치므로 뿌리로 쓰지 않는다.
        /// 털 이름 본보다 위(가슴 흔들림 본 등)로는 올라가지 않아 몸 피부가 털로 묶이지 않는다. 털이 아니면 null.
        /// </summary>
        static Transform TuftRoot(AvatarRig target, Transform bone, HashSet<Transform> weightedBones)
        {
            if (bone == null || target.TryGetHuman(bone, out _)) return null;
            var chain = new List<Transform>();
            for (Transform t = bone; t != null && !target.TryGetHuman(t, out _); t = t.parent) chain.Add(t);
            int topFur = chain.FindLastIndex(t => FurBoneName.IsMatch(t.name));
            if (topFur < 0) return null;
            return chain.Take(topFur + 1).LastOrDefault(weightedBones.Contains);
        }

        /// <summary>가장 가까운 옷 정점의 법선 평면 뒤쪽(옷 안쪽)이고, 옷과 reach 이내인지</summary>
        static bool IsInsideGarment(Vector3 point, BodySurface garmentSurface, float reach)
        {
            if (!garmentSurface.TryNearest(point, out Vector3 nearest, out Vector3 normal)) return false;
            bool isNear = (point - nearest).sqrMagnitude <= reach * reach;
            return isNear && Vector3.Dot(point - nearest, normal) < 0f;
        }
    }
}
