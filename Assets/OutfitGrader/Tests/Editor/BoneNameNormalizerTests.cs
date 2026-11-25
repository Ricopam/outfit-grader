using NUnit.Framework;

namespace OutfitGrader.Tests
{
    public class BoneNameNormalizerTests
    {
        [TestCase("J_Bip_L_UpperArm")]
        [TestCase("mixamorig:LeftArm")]
        [TestCase("左腕")]
        [TestCase("UpperArm.L")]
        [TestCase("upperarm_l")]
        [TestCase("Left UpperArm")]
        public void 제작사별_왼쪽_위팔_이름이_같은_키가_된다(string name)
        {
            Assert.AreEqual("upperarm.l", BoneNameNormalizer.Normalize(name));
        }

        [TestCase("RightHand")]
        [TestCase("Hand.R")]
        [TestCase("右手首")]
        [TestCase("J_Bip_R_Hand")]
        public void 오른손_이름이_같은_키가_된다(string name)
        {
            Assert.AreEqual("hand.r", BoneNameNormalizer.Normalize(name));
        }

        [TestCase("J_Bip_C_Hips")]
        [TestCase("Hips")]
        [TestCase("pelvis")]
        [TestCase("センター")]
        public void 엉덩이_동의어가_같은_키가_된다(string name)
        {
            Assert.AreEqual("hips", BoneNameNormalizer.Normalize(name));
        }

        [TestCase("Head (1)")]
        [TestCase("Head.001")]
        [TestCase("Armature/Head")]
        public void 복제_번호와_경로를_무시한다(string name)
        {
            Assert.AreEqual("head", BoneNameNormalizer.Normalize(name));
        }

        [Test]
        public void 사슬_순번_꼬리는_지우지_않는다()
        {
            Assert.AreNotEqual(BoneNameNormalizer.Normalize("Skirt_1"), BoneNameNormalizer.Normalize("Skirt_2"));
        }

        [Test]
        public void VRoid_보조본_접두사에서도_좌우를_읽는다()
        {
            Assert.AreEqual("bust1.l", BoneNameNormalizer.Normalize("J_Sec_L_Bust1"));
        }

        [Test]
        public void 좌우가_다르면_다른_키가_된다()
        {
            Assert.AreNotEqual(BoneNameNormalizer.Normalize("LeftHand"), BoneNameNormalizer.Normalize("RightHand"));
        }

        [Test]
        public void 빈_이름은_빈_키()
        {
            Assert.AreEqual(string.Empty, BoneNameNormalizer.Normalize(null));
            Assert.AreEqual(string.Empty, BoneNameNormalizer.Normalize(""));
        }
    }
}
