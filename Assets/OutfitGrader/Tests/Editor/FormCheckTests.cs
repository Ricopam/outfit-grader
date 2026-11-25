using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>형태 검사: 삼각형 눌림 계산, 부위별 무너진 면 비율, 무너진 부위의 눌림 상한 낮추기</summary>
    public class FormCheckTests
    {
        static readonly Vector3 A = Vector3.zero, B = Vector3.right, C = Vector3.up;

        [Test]
        public void 한쪽만_줄어든_삼각형의_눌림은_두_방향_늘음_비율의_비다()
        {
            Assert.AreEqual(1f, FormCheck.Squash(A, B, C, A * 2f, B * 2f, C * 2f), 1e-4f, "균등하게 커지면 모양은 그대로");
            Assert.AreEqual(3f, FormCheck.Squash(A, B, C, A, B * 1.5f, C * 0.5f), 1e-4f, "가로 x1.5, 세로 x0.5면 3배 눌림");
            Assert.AreEqual(1f, FormCheck.Squash(A, B, A, A, B, A), 1e-4f, "넓이가 없는 삼각형은 판정하지 않는다(1)");
            Assert.GreaterOrEqual(FormCheck.Squash(A, B, C, A, B, A), FormCheck.CollapseSquash, "선으로 납작해진 면은 가장 심하게 무너진 면으로 센다");
            Assert.GreaterOrEqual(FormCheck.Squash(A, B, C, A, B, C * 1e-5f), FormCheck.CollapseSquash, "거의 납작한 면도 무너진 면");
        }

        [Test]
        public void 부위별로_3배_이상_눌린_면이_기준_비율을_넘은_부위만_무너졌다고_본다()
        {
            // 몸통 삼각형 4개 중 1개(25%)가 4배 눌림, 팔 삼각형 4개는 모두 그대로.
            var before = new List<Vector3>();
            var after = new List<Vector3>();
            var triangles = new List<int>();
            var regions = new List<BodyRegion>();
            void Add(BodyRegion region, float squashY)
            {
                int i = before.Count;
                before.AddRange(new[] { A, B, C });
                after.AddRange(new[] { A, B, C * (1f / squashY) });
                triangles.AddRange(new[] { i, i + 1, i + 2 });
                regions.Add(region);
            }
            Add(BodyRegion.Torso, 4f);
            for (int k = 0; k < 3; k++) Add(BodyRegion.Torso, 1f);
            for (int k = 0; k < 4; k++) Add(BodyRegion.LeftLowerArm, 1f);

            Dictionary<BodyRegion, float> shares = FormCheck.CollapsedShares(before.ToArray(), after.ToArray(), triangles.ToArray(), regions.ToArray());

            Assert.AreEqual(0.25f, shares[BodyRegion.Torso], 1e-4f);
            Assert.AreEqual(0f, shares[BodyRegion.LeftLowerArm], 1e-4f);
            CollectionAssert.AreEqual(new[] { BodyRegion.Torso }, FormCheck.Collapsed(shares));
        }

        [Test]
        public void 무너진_부위만_눌림_상한을_낮추고_하한_아래로는_내리지_않는다()
        {
            SquashCaps caps = SquashCaps.For(FitStyle.Regular);
            float torso = caps.Of(BodyRegion.Torso), arm = caps.Of(BodyRegion.LeftLowerArm);

            SquashCaps lowered = caps.Lowered(new[] { BodyRegion.Torso, BodyRegion.LeftUpperLeg });

            Assert.AreEqual(torso * SquashCaps.LowerFactor, lowered.Of(BodyRegion.Torso), 1e-4f);
            Assert.AreEqual(arm, lowered.Of(BodyRegion.LeftLowerArm), 1e-4f, "무너지지 않은 부위는 그대로");
            Assert.AreEqual(0f, lowered.Of(BodyRegion.LeftUpperLeg), "늘이지 않는 부위(다리)는 낮출 상한이 없다");
            SquashCaps floor = lowered;
            for (int k = 0; k < 20; k++) floor = floor.Lowered(new[] { BodyRegion.Torso });
            Assert.AreEqual(SquashCaps.MinCap, floor.Of(BodyRegion.Torso), 1e-4f);
            Assert.IsFalse(floor.CanLower(BodyRegion.Torso), "하한에 닿으면 더 낮추지 않는다");
        }
    }
}
