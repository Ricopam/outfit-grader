using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    public class FitDiagnosticsTests
    {
        /// <summary>모든 조건이 정상인 사실 모음</summary>
        static FitFacts Healthy()
        {
            return new FitFacts
            {
                HasSource = true, HasOutfit = true, HasTarget = true,
                SourceHumanoid = true, TargetHumanoid = true,
                SourceInScene = true, TargetInScene = true,
                OutfitRendererCount = 1, OutfitBoneCount = 6, IdentifiedBySourceCount = 5,
                SourceArmDropDeg = 0f, TargetArmDropDeg = 0f,
            };
        }

        static List<Diagnostic> Of(Severity severity, FitFacts facts)
        {
            return FitDiagnostics.Evaluate(facts).Where(d => d.Severity == severity).ToList();
        }

        [Test]
        public void 정상_입력은_오류도_경고도_없다()
        {
            List<Diagnostic> result = FitDiagnostics.Evaluate(Healthy());
            Assert.IsFalse(FitDiagnostics.HasErrors(result));
            Assert.IsEmpty(result.Where(d => d.Severity == Severity.Warning));
        }

        [Test]
        public void 입력이_비면_오류_하나만_보여준다()
        {
            FitFacts facts = Healthy();
            facts.HasTarget = false;

            List<Diagnostic> result = FitDiagnostics.Evaluate(facts);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(Severity.Error, result[0].Severity);
        }

        [Test]
        public void Humanoid가_아니면_실행을_막고_해결_방법을_알려준다()
        {
            FitFacts facts = Healthy();
            facts.TargetHumanoid = false;

            Diagnostic error = Of(Severity.Error, facts).Single();

            StringAssert.Contains("Humanoid", error.Problem);
            StringAssert.Contains("Animation Type", error.Fix);
        }

        [Test]
        public void 의상_본이_원본과_하나도_안_맞으면_다른_아바타용으로_판단한다()
        {
            FitFacts facts = Healthy();
            facts.IdentifiedBySourceCount = 0;

            StringAssert.Contains("원본 아바타용이 아닌", Of(Severity.Error, facts).Single().Problem);
        }

        [Test]
        public void 본이_비어_있는_의상_메시는_실행을_막는다()
        {
            FitFacts facts = Healthy();
            facts.BrokenSkinnedCount = 2;

            StringAssert.Contains("2개", Of(Severity.Error, facts).Single().Problem);
        }

        [TestCase(30f, 1)]
        [TestCase(10f, 0)]
        public void A_자세는_경고한다(float dropDeg, int expectedWarnings)
        {
            FitFacts facts = Healthy();
            facts.TargetArmDropDeg = dropDeg;

            Assert.AreEqual(expectedWarnings, Of(Severity.Warning, facts).Count);
        }

        [Test]
        public void 손가락_대체_연결은_경고하되_실행은_허용한다()
        {
            FitFacts facts = Healthy();
            facts.FallbackBoneCount = 15;
            facts.FallbackNotes.Add("15개 -> LeftHand");

            List<Diagnostic> result = FitDiagnostics.Evaluate(facts);

            Assert.IsFalse(FitDiagnostics.HasErrors(result));
            StringAssert.Contains("15개", result.Single(d => d.Severity == Severity.Warning).Problem);
        }

        [Test]
        public void 켜진_축소_블렌드쉐이프는_이름과_함께_안내한다()
        {
            FitFacts facts = Healthy();
            facts.TargetShrinkKeys.Add("Shrink_Upper");

            StringAssert.Contains("Shrink_Upper", Of(Severity.Info, facts).Single().Cause);
        }

        [Test]
        public void 팔_각도는_수평이면_0도_45도로_내리면_45도()
        {
            Assert.AreEqual(0f, FitDiagnostics.ArmDropDegrees(Vector3.zero, Vector3.left, Vector3.up), 1e-3f);
            Assert.AreEqual(45f, FitDiagnostics.ArmDropDegrees(Vector3.zero, new Vector3(-1f, -1f, 0f), Vector3.up), 1e-3f);
            Assert.IsNaN(FitDiagnostics.ArmDropDegrees(Vector3.zero, Vector3.zero, Vector3.up));
        }

        [TestCase("Shrink_Body", true)]
        [TestCase("胸_縮小", true)]
        [TestCase("Hide_Leg", true)]
        [TestCase("Breast_Big", false)]
        [TestCase("", false)]
        public void 축소용_블렌드쉐이프_이름을_알아본다(string name, bool expected)
        {
            Assert.AreEqual(expected, FitDiagnostics.IsShrinkKey(name));
        }
    }
}
