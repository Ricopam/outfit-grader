using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 아바타 몸 메시 정점을 한 번만 읽어, 정점마다 가장 크게 묶인 Humanoid 본과 함께 보관한다.
    /// 머리 구역과 팔·몸통 마디를 재는 측정이 모두 같은 정점을 쓰므로 메시를 반복해서 읽지 않기 위함.
    /// </summary>
    public sealed class BodyPointCloud
    {
        /// <summary>
        /// 정점마다 (주 Humanoid 본, 위치, 정점이 속한 메시 오브젝트, 바깥 방향 법선, Humanoid 아래 몸 본으로 넣은 정점인지). 법선이 없는 메시는 영벡터.
        /// </summary>
        readonly List<(Transform human, Vector3 point, Transform renderer, Vector3 normal, bool isBodyChild)> points;

        BodyPointCloud(List<(Transform human, Vector3 point, Transform renderer, Vector3 normal, bool isBodyChild)> points)
        {
            this.points = points;
        }

        public AvatarRig Rig { get; private set; }

        /// <summary>
        /// 정점 위치는 아바타 루트 기준 축(월드 단위). 주 본이 Humanoid가 아닌 정점(귀·머리카락·후드)은 버려
        /// 몸 자체 크기를 잰다(동물형 아바타의 큰 귀가 머리 상자를 키워 눈높이가 틀어지는 문제 방지).
        /// 꺼져 있거나 숨긴 메시(벗겨 둔 기본 옷, 체형 변형용 예비 메시)는 보이는 몸이 아니므로 뺀다.
        /// 위치는 의상 관통 보정과 같은 4본 섞음 스키닝으로 구한다(블렌드쉐이프는 반영하지 않음).
        /// </summary>
        /// <param name="excluded">측정에서 뺄 계층(옮길 의상들). null이면 없음.</param>
        /// <param name="includeBodyChildren">
        /// true면 가슴 흔들림 본처럼 Humanoid 아래에 달린 몸 본의 정점도 가장 가까운 Humanoid 본으로 넣는다(귀·꼬리·털 제외).
        /// 상의를 체형(가슴 크기 등)에 맞출 때 쓰며, 머리·손 크기 측정은 귀·머리카락이 섞이지 않게 false로 쓴다.
        /// </param>
        /// <param name="skins">같은 준비 단계에서 스키닝 행렬을 나눠 쓸 묶음. null이면 이 메서드 안에서 계산한다.</param>
        public static BodyPointCloud Collect(AvatarRig rig, IReadOnlyCollection<Transform> excluded, bool includeBodyChildren = false, SkinCache skins = null)
        {
            var points = new List<(Transform, Vector3, Transform, Vector3, bool)>();
            var humanOf = new Dictionary<Transform, Transform>();
            // Humanoid가 아닌 본의 몸 조상(BodyAncestor)은 정규식으로 조상을 거슬러 올라가 정점마다 구하면 느려 본마다 한 번만 구한다(PRD AC8).
            var bodyAncestorOf = new Dictionary<Transform, Transform>();
            foreach (SkinnedMeshRenderer renderer in rig.Root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool isVisible = renderer.enabled && renderer.gameObject.activeInHierarchy;
                if (!isVisible || renderer.sharedMesh == null || IsExcluded(renderer.transform, excluded)) continue;
                AddRendererPoints(rig, renderer, humanOf, bodyAncestorOf, points, includeBodyChildren, skins);
            }
            return new BodyPointCloud(points) { Rig = rig };
        }

        static void AddRendererPoints(AvatarRig rig, SkinnedMeshRenderer renderer, Dictionary<Transform, Transform> humanOf, Dictionary<Transform, Transform> bodyAncestorOf,
            List<(Transform, Vector3, Transform, Vector3, bool)> points, bool includeBodyChildren, SkinCache skins)
        {
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4?[] skin = SkinningMath.SkinMatrices(bones, mesh.bindposes);
            // 본에 묶인 메시는 나눠 쓰는 스키닝 행렬(같은 4본 섞음)을 쓴다. 본 없는 메시는 기존처럼 직접 섞는다.
            Matrix4x4?[] shared = skins != null && !FitAnalysis.IsUnskinned(renderer) ? skins.Matrices(renderer, mesh) : null;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            bool hasNormals = normals.Length == vertices.Length;
            Quaternion toRoot = Quaternion.Inverse(rig.Root.rotation);
            var frame = new FitPlacement.RootFrame(rig.Root);
            Transform rendererTransform = renderer.transform;
            int count = Mathf.Min(vertices.Length, weights.Length);
            for (int i = 0; i < count; i++)
            {
                int boneIndex = weights[i].boneIndex0;
                bool hasMainBone = boneIndex < bones.Length && bones[boneIndex] != null;
                Transform human = hasMainBone ? HumanOf(rig, bones[boneIndex], humanOf) : null;
                bool isBodyChild = human == null && hasMainBone && includeBodyChildren;
                if (isBodyChild)
                {
                    Transform bone = bones[boneIndex];
                    if (!bodyAncestorOf.TryGetValue(bone, out human)) bodyAncestorOf[bone] = human = BodyCylinders.BodyAncestor(rig, bone);
                }
                if (human == null) continue;
                Matrix4x4 m;
                if (shared != null) { if (!shared[i].HasValue) continue; m = shared[i].Value; }
                else if (!SkinningMath.TryBlend(skin, weights[i], out m)) continue;

                Vector3 normal = hasNormals ? toRoot * m.MultiplyVector(normals[i]).normalized : Vector3.zero;
                points.Add((human, frame.Of(m.MultiplyPoint3x4(vertices[i])), rendererTransform, normal, isBodyChild));
            }
        }

        static bool IsExcluded(Transform t, IReadOnlyCollection<Transform> excluded)
        {
            return excluded != null && excluded.Any(e => e != null && t.IsChildOf(e));
        }

        /// <summary>
        /// 정점의 주 본을 Humanoid 본으로 바꾼다. Humanoid가 아니면 null.
        /// 파츠마다 Armature가 따로 있는 아바타(Modular Avatar 방식)는 같은 이름의 메인 본으로 바꾼다.
        /// </summary>
        static Transform HumanOf(AvatarRig rig, Transform bone, Dictionary<Transform, Transform> cache)
        {
            if (cache.TryGetValue(bone, out Transform cached)) return cached;

            Transform candidate = rig.TryGetHuman(bone, out _) ? bone : rig.FindByName(bone.name);
            Transform human = candidate != null && rig.TryGetHuman(candidate, out _) ? candidate : null;
            cache[bone] = human;
            return human;
        }

        /// <summary>
        /// Humanoid 아래 몸 본(includeBodyChildren)으로 넣은 정점을 뺀 묶음. includeBodyChildren=false로 다시 모은 것과 같은 정점이며,
        /// 같은 대상의 몸 표면과 몸 원통을 만들 때 메시를 두 번 스키닝하지 않게 한다(PRD AC8).
        /// </summary>
        public BodyPointCloud WithoutBodyChildren()
        {
            return new BodyPointCloud(points.Where(p => !p.isBodyChild).ToList()) { Rig = Rig };
        }

        /// <summary>메시를 다시 읽지 않고, 주어진 계층(같이 옮길 다른 의상 등)에 속한 정점을 뺀 묶음을 만든다.</summary>
        public BodyPointCloud Excluding(IReadOnlyCollection<Transform> excluded)
        {
            if (excluded == null || excluded.Count == 0) return this;
            var kept = points.Where(p => !IsExcluded(p.renderer, excluded)).ToList();
            return new BodyPointCloud(kept) { Rig = Rig };
        }

        /// <summary>정점마다 (주 Humanoid 본, 위치). 마디 원통 표를 만들 때 쓴다.</summary>
        public IEnumerable<(Transform human, Vector3 point)> HumanPoints => points.Select(p => (p.human, p.point));

        /// <summary>법선이 있는 정점(위치, 바깥 방향 법선). 의상 관통 판정에 쓰는 몸 표면.</summary>
        public List<(Vector3 point, Vector3 normal)> SurfacePoints()
        {
            return points.Where(p => p.normal != Vector3.zero).Select(p => (p.point, p.normal)).ToList();
        }

        /// <summary>구역(본 자신과 그 아래 Humanoid 본)의 법선 있는 정점. 모자를 머리 표면에 맞출 때 쓴다.</summary>
        public List<(Vector3 point, Vector3 normal)> SurfacePoints(HumanBodyBones region)
        {
            Transform root = Rig.GetHuman(region);
            if (root == null) return new List<(Vector3, Vector3)>();
            return points.Where(p => p.normal != Vector3.zero && p.human.IsChildOf(root)).Select(p => (p.point, p.normal)).ToList();
        }

        /// <summary>구역 본 자신과 그 아래 Humanoid 본(눈·손가락 등)에 묶인 정점</summary>
        public List<Vector3> InRegion(HumanBodyBones region)
        {
            Transform root = Rig.GetHuman(region);
            return root == null ? new List<Vector3>() : points.Where(p => p.human.IsChildOf(root)).Select(p => p.point).ToList();
        }

        /// <summary>Humanoid 본 하나에만 묶인 정점(하위 본 제외). 팔·몸통 단면 굵기 측정용.</summary>
        public List<Vector3> OfBone(HumanBodyBones bone)
        {
            Transform target = Rig.GetHuman(bone);
            return target == null ? new List<Vector3>() : points.Where(p => p.human == target).Select(p => p.point).ToList();
        }
    }
}
