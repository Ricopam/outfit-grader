using System.Collections.Generic;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 아바타 한 개의 뼈대 조회 창구. Humanoid 매핑과 이름 키 조회를 한곳에서 제공한다.
    /// Animator 없이도 만들 수 있어 테스트에서 직접 매핑을 넣을 수 있다.
    /// </summary>
    public sealed class AvatarRig
    {
        readonly Dictionary<HumanBodyBones, Transform> humanBones;
        readonly Dictionary<Transform, HumanBodyBones> humanByTransform = new Dictionary<Transform, HumanBodyBones>();
        readonly Dictionary<string, Transform> bonesByExactName = new Dictionary<string, Transform>();
        readonly Dictionary<string, Transform> bonesByKey = new Dictionary<string, Transform>();

        public Transform Root { get; }

        /// <param name="excluded">이름 조회에서 뺄 하위 계층. 아바타 아래에 놓인 의상을 아바타 본으로 오인하지 않게 한다.</param>
        public AvatarRig(Transform root, IDictionary<HumanBodyBones, Transform> humanMapping, Transform excluded = null)
        {
            Root = root;
            humanBones = new Dictionary<HumanBodyBones, Transform>(humanMapping);
            foreach (KeyValuePair<HumanBodyBones, Transform> pair in humanBones)
            {
                if (pair.Value != null) humanByTransform[pair.Value] = pair.Key;
            }

            // 같은 이름이 여러 개면 Humanoid 본을 먼저 등록하고, 나머지는 계층상 먼저 나온 본을 쓴다.
            // 파츠마다 같은 이름의 Armature를 가진 아바타(Modular Avatar 방식)에서 파츠 본이 메인 본을 가리지 않게 하기 위함.
            foreach (Transform bone in humanByTransform.Keys) Register(bone);
            foreach (Transform bone in root.GetComponentsInChildren<Transform>(true))
            {
                bool isExcluded = excluded != null && bone.IsChildOf(excluded);
                if (bone == root || isExcluded) continue;
                Register(bone);
            }
        }

        void Register(Transform bone)
        {
            if (!bonesByExactName.ContainsKey(bone.name)) bonesByExactName[bone.name] = bone;
            string key = BoneNameNormalizer.Normalize(bone.name);
            if (key.Length > 0 && !bonesByKey.ContainsKey(key)) bonesByKey[key] = bone;
        }

        /// <summary>Humanoid Animator에서 매핑을 읽어 만든다. Humanoid가 아니면 null.</summary>
        public static AvatarRig FromAnimator(Animator animator, Transform excluded = null)
        {
            if (!IsHumanoid(animator)) return null;

            var mapping = new Dictionary<HumanBodyBones, Transform>();
            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                Transform bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) mapping[(HumanBodyBones)i] = bone;
            }
            return new AvatarRig(animator.transform, mapping, excluded);
        }

        public static bool IsHumanoid(Animator animator)
        {
            return animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman;
        }

        public Transform GetHuman(HumanBodyBones bone)
        {
            return humanBones.TryGetValue(bone, out Transform t) ? t : null;
        }

        public bool TryGetHuman(Transform bone, out HumanBodyBones human)
        {
            return humanByTransform.TryGetValue(bone, out human);
        }

        /// <summary>정확한 이름 우선, 없으면 정규화 키로 찾는다.</summary>
        public Transform FindByName(string boneName)
        {
            if (bonesByExactName.TryGetValue(boneName, out Transform exact)) return exact;
            return bonesByKey.TryGetValue(BoneNameNormalizer.Normalize(boneName), out Transform byKey) ? byKey : null;
        }

        /// <summary>
        /// 원하는 Humanoid 본이 없으면 부모 Humanoid 본으로 올라가며 찾는다.
        /// 예: 손가락 본이 없는 아바타는 손 본으로 대체.
        /// </summary>
        public Transform GetHumanOrParent(HumanBodyBones wanted, out HumanBodyBones found)
        {
            HumanBodyBones current = wanted;
            while (true)
            {
                Transform bone = GetHuman(current);
                if (bone != null) { found = current; return bone; }

                int parent = HumanTrait.GetParentBone((int)current);
                if (parent < 0) { found = current; return null; }
                current = (HumanBodyBones)parent;
            }
        }

        public bool IsDescendantOf(Transform bone, HumanBodyBones ancestor)
        {
            Transform anchor = GetHuman(ancestor);
            return anchor != null && bone != null && bone.IsChildOf(anchor);
        }
    }
}
