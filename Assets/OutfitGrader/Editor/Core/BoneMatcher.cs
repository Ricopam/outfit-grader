using System.Collections.Generic;
using UnityEngine;

namespace OutfitGrader
{
    public enum MatchKind
    {
        /// <summary>같은 Humanoid 본에 연결</summary>
        Humanoid,
        /// <summary>대상에 해당 본이 없어 부모 Humanoid 본에 연결 (예: 손가락 -> 손)</summary>
        HumanoidFallback,
        /// <summary>Humanoid가 아닌 본을 이름으로 연결 (예: 머리카락 본)</summary>
        Name,
        /// <summary>사용자가 표에서 직접 지정</summary>
        Manual,
        /// <summary>본이 없는 단단한 소품(안경 등)을 원래 붙어 있던 본이나 가장 가까운 본에 부착</summary>
        Attachment,
        /// <summary>연결 대상 없음. 부모 본을 따라 그대로 이동</summary>
        Unmatched,
    }

    /// <summary>의상 본 하나의 연결 결과</summary>
    public sealed class BoneMatch
    {
        public Transform OutfitBone;
        /// <summary>원본 아바타 쪽 기준 본. 위치 차이(오프셋) 계산에 쓴다.</summary>
        public Transform SourceAnchor;
        public Transform TargetBone;
        /// <summary>소품의 메시 중심(월드). 있으면 구역 상자 안 상대 위치로 옮길 때 기준점으로 쓴다.</summary>
        public Vector3? SourcePoint;
        public MatchKind Kind;
        /// <summary>크기 보정 구역을 정하는 Humanoid 본 (Humanoid 계열 연결일 때만 의미 있음)</summary>
        public HumanBodyBones HumanBone = HumanBodyBones.LastBone;
        public string Note = string.Empty;

        public bool IsConnected => Kind != MatchKind.Unmatched && TargetBone != null;
    }

    /// <summary>
    /// 의상 본을 원본 아바타 본으로 식별한 뒤 대상 아바타 본에 짝짓는다.
    /// 1) 원본에서 Humanoid 본이면 대상의 같은 Humanoid 본 (없으면 부모 Humanoid 본)
    /// 2) 아니면 대상에서 이름 키로 검색
    /// 3) 둘 다 실패하면 연결하지 않고 부모를 따라가게 둔다 (치마·꼬리 같은 흔들림 본 보존)
    /// </summary>
    public static class BoneMatcher
    {
        public static List<BoneMatch> Match(
            IReadOnlyList<Transform> outfitBones,
            AvatarRig source,
            AvatarRig target,
            IReadOnlyDictionary<Transform, Transform> manualTargets = null)
        {
            var results = new List<BoneMatch>(outfitBones.Count);
            foreach (Transform outfitBone in outfitBones)
            {
                results.Add(MatchOne(outfitBone, source, target, manualTargets));
            }
            return results;
        }

        // 소품을 붙일 수 있는 큰 본. 눈·턱·손가락처럼 작게 움직이는 본에 잘못 붙지 않게 제한한다.
        static readonly HumanBodyBones[] AttachableBones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
            HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
        };

        /// <summary>
        /// 본이 없는 단단한 소품 하나를 부착할 본을 정한다.
        /// 의상이 원본 아바타의 Humanoid 본 아래에 놓여 있으면 그 본, 아니면 메시 중심에서 가장 가까운 본.
        /// </summary>
        /// <param name="placedUnder">의상 최상위 오브젝트의 부모 (아바타 본 아래에 직접 놓았는지 판단)</param>
        public static BoneMatch MatchProp(Transform prop, Vector3 meshCenter, Transform placedUnder, AvatarRig source, AvatarRig target,
            IReadOnlyDictionary<Transform, Transform> manualTargets = null)
        {
            var match = new BoneMatch { OutfitBone = prop, Kind = MatchKind.Attachment, SourcePoint = meshCenter };
            HumanBodyBones anchor = HumanBodyBones.Hips;
            bool placedOnSourceBone = placedUnder != null && source.TryGetHuman(placedUnder, out anchor);
            if (!placedOnSourceBone) anchor = Nearest(source, meshCenter);
            match.SourceAnchor = source.GetHuman(anchor);

            if (manualTargets != null && manualTargets.TryGetValue(prop, out Transform manual) && manual != null)
            {
                match.TargetBone = manual;
                match.Kind = MatchKind.Manual;
                match.Note = "직접 지정";
                return match;
            }

            match.TargetBone = target.GetHumanOrParent(anchor, out HumanBodyBones found);
            match.HumanBone = found;
            match.Note = placedOnSourceBone ? $"원래 붙어 있던 {anchor}에 부착" : $"메시에서 가장 가까운 {anchor}에 부착 (다르면 직접 지정)";
            if (match.TargetBone == null) match.Kind = MatchKind.Unmatched;
            return match;
        }

        static HumanBodyBones Nearest(AvatarRig source, Vector3 point)
        {
            HumanBodyBones best = HumanBodyBones.Hips;
            float bestDistance = float.MaxValue;
            foreach (HumanBodyBones candidate in AttachableBones)
            {
                Transform bone = source.GetHuman(candidate);
                if (bone == null) continue;
                float distance = (bone.position - point).sqrMagnitude;
                if (distance < bestDistance) { bestDistance = distance; best = candidate; }
            }
            return best;
        }

        static BoneMatch MatchOne(Transform outfitBone, AvatarRig source, AvatarRig target,
            IReadOnlyDictionary<Transform, Transform> manualTargets)
        {
            Transform sourceBone = source.FindByName(outfitBone.name);
            var match = new BoneMatch { OutfitBone = outfitBone, SourceAnchor = sourceBone };

            if (manualTargets != null && manualTargets.TryGetValue(outfitBone, out Transform manual) && manual != null)
            {
                match.TargetBone = manual;
                match.Kind = MatchKind.Manual;
                match.Note = "직접 지정";
                return match;
            }

            if (sourceBone != null && source.TryGetHuman(sourceBone, out HumanBodyBones human))
            {
                Transform targetBone = target.GetHumanOrParent(human, out HumanBodyBones found);
                if (targetBone != null)
                {
                    match.TargetBone = targetBone;
                    match.HumanBone = found;
                    match.Kind = found == human ? MatchKind.Humanoid : MatchKind.HumanoidFallback;
                    if (match.Kind == MatchKind.HumanoidFallback)
                    {
                        // 오프셋은 대체된 본 기준으로 계산해야 원래 위치 관계가 유지된다.
                        match.SourceAnchor = source.GetHuman(found) ?? sourceBone;
                        match.Note = $"대상에 {human} 본이 없어 {found}에 연결";
                    }
                    return match;
                }
            }

            // 이름 연결은 원본 아바타에서 식별된 본(머리카락 본 등)에만 쓴다.
            // 의상 전용 본까지 대상 이름으로 붙이면 대상의 기존 의상 본에 새 의상이 묶일 수 있다.
            Transform byName = sourceBone != null ? target.FindByName(outfitBone.name) : null;
            if (byName != null)
            {
                match.TargetBone = byName;
                match.Kind = MatchKind.Name;
                match.Note = "이름으로 연결";
                return match;
            }

            match.Kind = MatchKind.Unmatched;
            match.Note = sourceBone == null ? "원본 아바타에 없는 의상 전용 본 (부모를 따라감)" : "대상 아바타에 같은 본 없음 (부모를 따라감)";
            return match;
        }
    }
}
