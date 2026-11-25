using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>후드 붙이기: 머리·목 본에만 묶인 정점 찾기, 목둘레 이음매, 통째로 옮길 거리</summary>
    public class PartAttachTests
    {
        static BoneWeight Weight(int bone0, float w0, int bone1 = 0, float w1 = 0f)
            => new BoneWeight { boneIndex0 = bone0, weight0 = w0, boneIndex1 = bone1, weight1 = w1 };

        [Test]
        public void 머리_목_본에만_묶인_정점만_후드로_본다()
        {
            // 본 0 = 머리, 1 = 목, 2 = 가슴
            bool IsHeadBone(int bone) => bone == 0 || bone == 1;
            var weights = new[] { Weight(0, 1f), Weight(1, 0.6f, 0, 0.4f), Weight(0, 0.7f, 2, 0.3f), Weight(2, 1f) };

            CollectionAssert.AreEqual(new[] { 0, 1 }, PartAttach.Members(weights, IsHeadBone));
        }

        [Test]
        public void 후드와_몸판이_만나는_변만_이음매로_잡는다()
        {
            // 삼각형 (0,1,2)에서 0·1은 후드, 2는 몸판. 삼각형 (2,3,4)는 몸판끼리.
            var seams = PartAttach.Seams(new[] { 0, 1 }, new[] { 0, 1, 2, 2, 3, 4 });

            CollectionAssert.AreEquivalent(new[] { (0, 2), (1, 2) }, seams);
        }

        [Test]
        public void 이어지지_않은_덩어리는_따로_나눈다()
        {
            // 0-1-2는 한 삼각형(후드), 5-6-7은 떨어진 삼각형(목걸이), 3·4는 몸판.
            var parts = PartAttach.Parts(new[] { 0, 1, 2, 5, 6, 7 }, new[] { 0, 1, 2, 2, 3, 4, 5, 6, 7 });

            Assert.AreEqual(2, parts.Count);
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, parts.First(p => p.Contains(0)));
            Assert.AreEqual(0, PartAttach.Seams(parts.First(p => p.Contains(5)), new[] { 0, 1, 2, 2, 3, 4, 5, 6, 7 }).Length, "목걸이는 이음매가 없다");
        }

        [Test]
        public void 원본_간격을_뺀_벌어짐의_축별_중앙값만큼_옮기고_튀는_쌍에_끌려가지_않는다()
        {
            // 쌍 3개: 두 쌍은 원본보다 0.04 아래로 벌어졌고, 한 쌍(앞섶 끝)만 0.5 앞으로 튀었다.
            var placed = new[]
            {
                new Vector3(0f, 1f, 0f), new Vector3(0f, 0.95f, 0f),
                new Vector3(1f, 1f, 0f), new Vector3(1f, 0.95f, 0f),
                new Vector3(2f, 1f, 0f), new Vector3(2f, 0.95f, 0.5f),
            };
            var seams = new[] { (0, 1), (2, 3), (4, 5) };

            Assert.IsTrue(PartAttach.TryShift(seams, placed, (hood, body) => new Vector3(0f, -0.01f, 0f), out Vector3 shift));

            Assert.AreEqual(0f, (shift - new Vector3(0f, -0.04f, 0f)).magnitude, 1e-5f);
            Assert.IsFalse(PartAttach.TryShift(seams.Take(PartAttach.MinSeamPairs - 1).ToArray(), placed, (h, b) => Vector3.zero, out _), "쌍이 모자라면 옮기지 않는다");
        }

        [Test]
        public void 축_방향과_둘레_방향을_따로_바꾸고_대상_축으로_돌린다()
        {
            // 원본 팔은 +x 방향, 대상 팔은 아래로 45도 기울었다(A 자세). 팔 방향 0.5배, 둘레 2배.
            Vector3 fromAxis = Vector3.right, toAxis = new Vector3(1f, -1f, 0f).normalized;
            Vector3 result = PartAttach.AxisStretch(new Vector3(0.1f, 0.02f, 0f), fromAxis, toAxis, along: 0.5f, across: 2f);

            Vector3 expected = Quaternion.FromToRotation(fromAxis, toAxis) * new Vector3(0.05f, 0.04f, 0f);
            Assert.AreEqual(0f, (result - expected).magnitude, 1e-5f);
            Assert.AreEqual(0.05f, Vector3.Dot(result, toAxis), 1e-5f, "팔 방향 성분은 대상 축을 따라 0.5배");
        }
    }
}
