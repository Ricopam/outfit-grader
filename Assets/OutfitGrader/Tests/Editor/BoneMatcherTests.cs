using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    public class BoneMatcherTests
    {
        TestAvatar source, target;
        GameObject outfit;

        [SetUp]
        public void SetUp()
        {
            source = TestRigBuilder.CreateHumanoid("Source", NameStyle.Plain);
            target = TestRigBuilder.CreateHumanoid("Target", NameStyle.VRoid, withFingers: false);
            outfit = TestRigBuilder.CreateGlassesOutfit(source, NameStyle.Plain, new Vector3(0f, 0.05f, 0.1f));
        }

        [TearDown]
        public void TearDown() => TestRigBuilder.Destroy(source.Root, target.Root, outfit);

        AvatarRig SourceRig => AvatarRig.FromAnimator(source.Animator, outfit.transform);
        AvatarRig TargetRig => AvatarRig.FromAnimator(target.Animator);

        BoneMatch MatchSingle(Transform bone, IReadOnlyDictionary<Transform, Transform> manual = null)
        {
            return BoneMatcher.Match(new[] { bone }, SourceRig, TargetRig, manual).Single();
        }

        [Test]
        public void 이름_규칙이_달라도_Humanoid_본끼리_연결된다()
        {
            Transform outfitHead = outfit.GetComponentsInChildren<Transform>().First(t => t.name == "Head");

            BoneMatch match = MatchSingle(outfitHead);

            Assert.AreEqual(MatchKind.Humanoid, match.Kind);
            Assert.AreSame(target[HumanBodyBones.Head], match.TargetBone);
            Assert.AreSame(source[HumanBodyBones.Head], match.SourceAnchor, "의상 안의 같은 이름 본이 아니라 아바타 본을 기준으로 삼아야 한다");
        }

        [Test]
        public void 대상에_손가락이_없으면_손_본에_연결하고_기준도_손으로_바꾼다()
        {
            var ring = new GameObject("LeftIndexProximal").transform;
            ring.SetParent(outfit.transform, false);

            BoneMatch match = MatchSingle(ring);

            Assert.AreEqual(MatchKind.HumanoidFallback, match.Kind);
            Assert.AreSame(target[HumanBodyBones.LeftHand], match.TargetBone);
            Assert.AreSame(source[HumanBodyBones.LeftHand], match.SourceAnchor);
            Assert.AreEqual(HumanBodyBones.LeftHand, match.HumanBone);
        }

        [Test]
        public void 의상_전용_본은_연결하지_않고_부모를_따라가게_둔다()
        {
            Transform glassesBone = outfit.GetComponentsInChildren<Transform>().First(t => t.name == "Glasses_Bone");

            BoneMatch match = MatchSingle(glassesBone);

            Assert.AreEqual(MatchKind.Unmatched, match.Kind);
            Assert.IsFalse(match.IsConnected);
        }

        [Test]
        public void 원본에서_식별된_Humanoid가_아닌_본은_대상에서_이름으로_찾는다()
        {
            new GameObject("Hair_Front").transform.SetParent(source[HumanBodyBones.Head], false);
            new GameObject("Hair_Front").transform.SetParent(target[HumanBodyBones.Head], false);
            var outfitHair = new GameObject("Hair_Front").transform;
            outfitHair.SetParent(outfit.transform, false);

            BoneMatch match = MatchSingle(outfitHair);

            Assert.AreEqual(MatchKind.Name, match.Kind);
            Assert.AreEqual("Hair_Front", match.TargetBone.name);
        }

        [Test]
        public void 원본에_없는_의상_전용_본은_대상에_같은_이름이_있어도_연결하지_않는다()
        {
            // 대상의 기존 의상 본(Skirt_1)에 새 의상이 묶이면 안 된다.
            new GameObject("Skirt_1").transform.SetParent(target[HumanBodyBones.Hips], false);
            var skirt = new GameObject("Skirt_1").transform;
            skirt.SetParent(outfit.transform, false);

            Assert.AreEqual(MatchKind.Unmatched, MatchSingle(skirt).Kind);
        }

        [Test]
        public void 파츠_Armature가_메인보다_먼저_있어도_Humanoid_본으로_식별한다()
        {
            // Modular Avatar 방식 파츠가 계층상 메인 Armature보다 앞에 있는 경우
            var part = new GameObject("Gloves").transform;
            part.SetParent(source.Root.transform, false);
            part.SetSiblingIndex(0);
            new GameObject("Head").transform.SetParent(part, false);
            Transform outfitHead = outfit.GetComponentsInChildren<Transform>().First(t => t.name == "Head");

            BoneMatch match = MatchSingle(outfitHead);

            Assert.AreEqual(MatchKind.Humanoid, match.Kind);
            Assert.AreSame(source[HumanBodyBones.Head], match.SourceAnchor);
        }

        [Test]
        public void 직접_지정이_자동_연결보다_우선한다()
        {
            Transform outfitHead = outfit.GetComponentsInChildren<Transform>().First(t => t.name == "Head");
            var manual = new Dictionary<Transform, Transform> { { outfitHead, target[HumanBodyBones.Neck] } };

            BoneMatch match = MatchSingle(outfitHead, manual);

            Assert.AreEqual(MatchKind.Manual, match.Kind);
            Assert.AreSame(target[HumanBodyBones.Neck], match.TargetBone);
        }
    }
}
