using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>창에서 받은 입력. 의상은 원본 아바타에 입힌(씬에 놓인) 오브젝트여야 한다.</summary>
    public sealed class FitInput
    {
        public Animator Source;
        public Transform Outfit;
        public Animator Target;
        public IReadOnlyDictionary<Transform, Transform> ManualTargets;
        /// <summary>같이 옮길 다른 의상들. 원본 몸 크기를 잴 때 이 의상들의 정점도 뺀다(겹쳐 입은 재킷이 팔 굵기에 섞이지 않게).</summary>
        public IReadOnlyList<Transform> OtherOutfits;
        public float BodyMultiplier = 1f, HeadMultiplier = 1f;
    }

    /// <summary>
    /// 실행 전 분석: 본 매칭, 크기 측정, 진단을 한 번에 계산한다.
    /// 창은 이 결과를 그대로 그리고, 맞추기는 이 결과를 그대로 적용한다.
    /// </summary>
    public sealed class FitAnalysis
    {
        public AvatarRig Source { get; private set; }
        public AvatarRig Target { get; private set; }
        public Transform Outfit { get; private set; }
        /// <summary>함께 옮기는 다른 의상. 원본 몸을 잴 때 모두 뺀다.</summary>
        public IReadOnlyList<Transform> OtherOutfits { get; private set; } = new Transform[0];
        public List<BoneMatch> Matches { get; private set; } = new List<BoneMatch>();
        public ScaleProfile Measured { get; private set; } = ScaleProfile.Identity;
        public ScaleProfile Scale { get; private set; } = ScaleProfile.Identity;
        public List<Diagnostic> Diagnostics { get; private set; } = new List<Diagnostic>();

        public bool CanFit => !FitDiagnostics.HasErrors(Diagnostics);

        public static FitAnalysis Run(FitInput input)
        {
            var analysis = new FitAnalysis { Outfit = input.Outfit, OtherOutfits = input.OtherOutfits ?? new Transform[0] };
            var facts = new FitFacts
            {
                HasSource = input.Source != null,
                HasOutfit = input.Outfit != null,
                HasTarget = input.Target != null,
            };

            if (facts.HasSource && facts.HasOutfit && facts.HasTarget)
            {
                analysis.Source = AvatarRig.FromAnimator(input.Source, input.Outfit);
                analysis.Target = AvatarRig.FromAnimator(input.Target);
                CollectInputFacts(input, facts, analysis);
            }

            bool inputsUsable = facts.HasSource && facts.HasOutfit && facts.HasTarget
                && analysis.Source != null && analysis.Target != null
                && facts.SourceInScene && facts.TargetInScene && !facts.SameAvatar && !facts.OutfitUnderTarget;
            if (inputsUsable)
            {
                analysis.Match(input, facts);
                analysis.MeasureScale(input, facts);
                CollectPoseFacts(analysis, facts);
            }

            analysis.Diagnostics = FitDiagnostics.Evaluate(facts);
            return analysis;
        }

        /// <summary>
        /// 의상의 본: 스킨 메시가 쓰는 본과 그 조상(Armature 등). 계층 순서(부모 먼저)를 유지한다.
        /// 정리용 빈 오브젝트는 본으로 치지 않아 그 아래 소품이 따로 부착될 수 있게 한다.
        /// </summary>
        public static List<Transform> CollectOutfitBones(Transform outfit)
        {
            HashSet<Transform> skinBones = SkinBonesWithAncestors(outfit);
            return outfit.GetComponentsInChildren<Transform>(true).Where(skinBones.Contains).ToList();
        }

        static HashSet<Transform> SkinBonesWithAncestors(Transform outfit)
        {
            var set = new HashSet<Transform>();
            foreach (SkinnedMeshRenderer renderer in outfit.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (Transform bone in renderer.bones)
                {
                    for (Transform t = bone; t != null && t != outfit && t.IsChildOf(outfit); t = t.parent)
                    {
                        if (!set.Add(t)) break;
                    }
                }
            }
            return set;
        }

        /// <summary>
        /// Armature 없이 자기 위치에 그려지는 단단한 소품(안경·모자 등). MeshRenderer와 본 없는 스킨 메시가 해당한다.
        /// 위쪽에 스킨 본이 있으면 그 본을 따라가므로 제외한다.
        /// </summary>
        public static List<Renderer> CollectRigidProps(Transform outfit)
        {
            HashSet<Transform> skinBones = SkinBonesWithAncestors(outfit);
            return outfit.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer skinned && IsUnskinned(skinned))
                .Where(r => !HasBoneAbove(r.transform, outfit, skinBones))
                .ToList();
        }

        /// <summary>
        /// 본 없이 자기 위치에 그려지는 스킨 메시. 블렌드쉐이프로 크기만 조정하는 모자처럼
        /// 스킨 없이 SkinnedMeshRenderer를 쓰는 소품이 흔하다(바인드포즈가 없으면 Unity가 오브젝트 위치에 그린다).
        /// </summary>
        public static bool IsUnskinned(SkinnedMeshRenderer renderer)
        {
            return renderer.bones.Length == 0 && renderer.sharedMesh != null && renderer.sharedMesh.bindposes.Length == 0;
        }

        static bool HasBoneAbove(Transform t, Transform outfit, HashSet<Transform> skinBones)
        {
            // 의상 최상위 자체가 소품이면 의상 밖(아바타 계층)은 보지 않는다.
            if (t == outfit) return false;
            for (Transform p = t.parent; p != null && p != outfit; p = p.parent)
            {
                if (skinBones.Contains(p)) return true;
            }
            return false;
        }

        /// <summary>메시 중심(월드). Renderer.bounds는 비활성 오브젝트에서 비어 있을 수 있어 메시 경계로 계산한다.</summary>
        static Vector3 MeshCenter(Renderer renderer)
        {
            Mesh mesh = MeshOf(renderer);
            return mesh != null ? renderer.transform.TransformPoint(mesh.bounds.center) : renderer.bounds.center;
        }

        /// <summary>렌더러가 그리는 메시(스킨 메시 또는 MeshFilter). 없으면 null.</summary>
        public static Mesh MeshOf(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = renderer.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        static void CollectInputFacts(FitInput input, FitFacts facts, FitAnalysis analysis)
        {
            facts.SourceHumanoid = analysis.Source != null;
            facts.TargetHumanoid = analysis.Target != null;
            facts.SourceInScene = !EditorUtility.IsPersistent(input.Source);
            facts.TargetInScene = !EditorUtility.IsPersistent(input.Target);
            facts.SameAvatar = input.Source == input.Target;
            facts.OutfitUnderTarget = input.Outfit.IsChildOf(input.Target.transform);
            // 본 없는 소품은 의상 최상위째 머리 등 아바타 본 아래로 옮겨지므로 아바타 계층 전체에서 같은 이름을 찾는다.
            facts.TargetAlreadyWearing = !facts.OutfitUnderTarget && input.Target.GetComponentsInChildren<Transform>(true)
                .Any(t => t != input.Target.transform && t.name == input.Outfit.name);

            Renderer[] renderers = input.Outfit.GetComponentsInChildren<Renderer>(true);
            facts.OutfitRendererCount = renderers.Length;
            facts.BrokenSkinnedCount = renderers.OfType<SkinnedMeshRenderer>().Count(IsBroken);
            facts.TargetMissingScripts = input.Target.GetComponentsInChildren<Transform>(true)
                .Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
        }

        /// <summary>
        /// 정점이 실제로 쓰는 본이 비어 있으면 깨진 것으로 본다.
        /// Modular Avatar 방식 의상은 본 목록에 쓰지 않는 빈 칸이 흔해, 빈 칸 자체는 문제로 보지 않는다.
        /// 본 없이 그리는 소품용 스킨 메시도 정상이다.
        /// </summary>
        public static bool IsBroken(SkinnedMeshRenderer renderer)
        {
            if (IsUnskinned(renderer)) return false;
            Mesh mesh = renderer.sharedMesh;
            Transform[] bones = renderer.bones;
            if (mesh == null || bones.Length == 0) return true;

            foreach (BoneWeight w in mesh.boneWeights)
            {
                if (IsMissing(bones, w.boneIndex0, w.weight0) || IsMissing(bones, w.boneIndex1, w.weight1)
                    || IsMissing(bones, w.boneIndex2, w.weight2) || IsMissing(bones, w.boneIndex3, w.weight3)) return true;
            }
            return false;
        }

        static bool IsMissing(Transform[] bones, int index, float weight)
        {
            return weight > 0f && (index >= bones.Length || bones[index] == null);
        }

        void Match(FitInput input, FitFacts facts)
        {
            List<Transform> bones = CollectOutfitBones(input.Outfit);
            Matches = BoneMatcher.Match(bones, Source, Target, input.ManualTargets);

            foreach (Renderer prop in CollectRigidProps(input.Outfit))
            {
                BoneMatch propMatch = BoneMatcher.MatchProp(prop.transform, MeshCenter(prop), input.Outfit.parent,
                    Source, Target, input.ManualTargets);
                Matches.Add(propMatch);
                facts.PropNotes.Add($"{prop.name}: {propMatch.Note}");
            }

            facts.OutfitBoneCount = bones.Count;
            facts.IdentifiedBySourceCount = Matches.Count(m => m.SourceAnchor != null);
            facts.UnmatchedCount = Matches.Count(m => m.Kind == MatchKind.Unmatched);

            List<BoneMatch> fallbacks = Matches.Where(m => m.Kind == MatchKind.HumanoidFallback).ToList();
            facts.FallbackBoneCount = fallbacks.Count;
            facts.FallbackNotes.AddRange(fallbacks
                .GroupBy(m => m.HumanBone)
                .Select(g => $"{g.Count()}개 -> {g.Key}"));
        }

        void MeasureScale(FitInput input, FitFacts facts)
        {
            // 아바타마다 몸 정점을 한 번만 읽어 머리 구역과 팔·몸통 마디 측정이 함께 쓴다.
            // 머리 상자: 원본을 입은 그대로 잰다(아무것도 빼지 않음). 같이 옮기는 의상이 모두 같은 머리 비율을 써야
            //   후드 위에 쓴 모자가 후드 안으로 들어가지 않는다. 착용물을 빼면 원본만 얼굴 크기가 되어(대상은 머리카락·머리띠 포함)
            //   비율이 과하게 커진다.
            // 팔·몸통 굵기: 옮기는 의상을 모두 뺀다. 겹쳐 입은 재킷이 원본 팔을 굵게 만들면 소매가 팔 안에 묻힌다.
            BodyPointCloud sourceCloud = BodyPointCloud.Collect(Source, null);
            BodyPointCloud sourceBodyOnly = sourceCloud.Excluding((input.OtherOutfits ?? new Transform[0]).Append(input.Outfit).ToList());
            BodyPointCloud targetCloud = BodyPointCloud.Collect(Target, null);
            Measured = RegionMeasurer.Measure(sourceCloud, targetCloud, out bool headMeasured);
            Measured = Measured.WithSegments(BodySegmentMeasurer.Measure(sourceBodyOnly, targetCloud, Measured.Body));
            Scale = Measured.Multiply(input.BodyMultiplier, input.HeadMultiplier);
            facts.HeadMeasured = headMeasured;
            CollectShrinkKeys(Source.Root, input.Outfit, facts.SourceShrinkKeys);
            CollectShrinkKeys(Target.Root, null, facts.TargetShrinkKeys);
        }

        static void CollectShrinkKeys(Transform root, Transform excluded, List<string> into)
        {
            foreach (SkinnedMeshRenderer renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                bool isExcluded = excluded != null && renderer.transform.IsChildOf(excluded);
                if (isExcluded || renderer.sharedMesh == null) continue;

                Mesh mesh = renderer.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    string name = mesh.GetBlendShapeName(i);
                    bool active = renderer.GetBlendShapeWeight(i) > 0f;
                    if (active && FitDiagnostics.IsShrinkKey(name) && !into.Contains(name)) into.Add(name);
                }
            }
        }

        static void CollectPoseFacts(FitAnalysis analysis, FitFacts facts)
        {
            facts.SourceArmDropDeg = MaxArmDrop(analysis.Source);
            facts.TargetArmDropDeg = MaxArmDrop(analysis.Target);
            facts.FacingDifferenceDeg = Vector3.Angle(BodyFacing(analysis.Source), BodyFacing(analysis.Target));
        }

        static float MaxArmDrop(AvatarRig rig)
        {
            float left = ArmDrop(rig, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftHand);
            float right = ArmDrop(rig, HumanBodyBones.RightUpperArm, HumanBodyBones.RightHand);
            if (float.IsNaN(left)) return right;
            if (float.IsNaN(right)) return left;
            return Mathf.Max(left, right);
        }

        static float ArmDrop(AvatarRig rig, HumanBodyBones upper, HumanBodyBones hand)
        {
            Transform a = rig.GetHuman(upper), b = rig.GetHuman(hand);
            return a == null || b == null ? float.NaN : FitDiagnostics.ArmDropDegrees(a.position, b.position, rig.Root.up);
        }

        /// <summary>루트 기준 몸의 정면 방향. 왼다리 -> 오른다리 방향과 위쪽의 외적.</summary>
        static Vector3 BodyFacing(AvatarRig rig)
        {
            Transform left = rig.GetHuman(HumanBodyBones.LeftUpperLeg), right = rig.GetHuman(HumanBodyBones.RightUpperLeg);
            if (left == null || right == null) return Vector3.forward;
            Vector3 world = Vector3.Cross(right.position - left.position, rig.Root.up);
            return rig.Root.InverseTransformDirection(world).normalized;
        }
    }
}
