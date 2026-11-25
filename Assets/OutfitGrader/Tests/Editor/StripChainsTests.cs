using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>끈 판별: 매달린 사슬의 길쭉함으로 끈(가늘고 긴 부품)과 고리(소맷부리)를 구분한다.</summary>
    public class StripChainsTests
    {
        /// <summary>y축을 따라 길이 length, 반지름 radius인 원통 표면 점</summary>
        static List<Vector3> Tube(float length, float radius)
        {
            return Enumerable.Range(0, 11).SelectMany(k => Enumerable.Range(0, 8).Select(a =>
                new Vector3(Mathf.Cos(a * Mathf.PI / 4f) * radius, length * k / 10f, Mathf.Sin(a * Mathf.PI / 4f) * radius))).ToList();
        }

        [Test]
        public void 가늘고_긴_사슬은_끈이고_굵은_고리는_끈이_아니다()
        {
            Assert.GreaterOrEqual(StripChains.Elongation(Tube(0.3f, 0.01f)), StripChains.MinElongation, "길이 30cm, 굵기 2cm 끈");
            Assert.Less(StripChains.Elongation(Tube(0.09f, 0.045f)), StripChains.MinElongation, "길이 9cm, 굵기 9cm 소맷부리 고리");
        }

        [Test]
        public void 길쭉해도_부위에_비해_넓은_치마_판자_가닥은_끈이_아니다()
        {
            const float hipsRadius = 0.13f;
            Assert.IsTrue(StripChains.IsStrip(Tube(0.23f, 0.01f), hipsRadius), "굵기 2cm 후드 끈");
            // 폭 12cm, 길이 35cm 판자: 길쭉함은 기준을 넘지만 굵기가 엉덩이 반지름의 35%를 넘는다.
            var panel = Enumerable.Range(0, 11).SelectMany(k => Enumerable.Range(0, 7).Select(a => new Vector3(-0.06f + a * 0.02f, -0.35f * k / 10f, 0f))).ToList();
            Assert.GreaterOrEqual(StripChains.Elongation(panel), StripChains.MinElongation);
            Assert.IsFalse(StripChains.IsStrip(panel, hipsRadius));
            Assert.IsFalse(StripChains.IsStrip(Tube(0.23f, 0.01f), 0f), "부위 반지름을 모르면 판별하지 않는다");
        }

        [Test]
        public void 점이_모자라면_끈으로_보지_않는다()
        {
            Assert.AreEqual(0f, StripChains.Elongation(new[] { Vector3.zero }));
        }
    }
}
