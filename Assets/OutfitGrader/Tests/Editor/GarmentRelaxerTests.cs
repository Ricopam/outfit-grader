using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>풀어 주기: 접힘 펴기 거리, 고정 정점 유지, 쉼 거리로 당기기</summary>
    public class GarmentRelaxerTests
    {
        [Test]
        public void 접힌_두_삼각형_건너편_거리는_편_거리_쪽으로_비율만큼_옮긴다()
        {
            // 공유 변 a-b(x축), 건너편 c는 +y, d는 90도 접혀 +z. 지금 거리 sqrt(2), 편 거리 2.
            Vector3 a = Vector3.zero, b = Vector3.right, c = new Vector3(0.5f, 1f, 0f), d = new Vector3(0.5f, 0f, 1f);

            Assert.AreEqual(Mathf.Sqrt(2f), GarmentRelaxer.FlattenedDistance(a, b, c, d, 0f), 1e-4f, "0이면 그대로");
            Assert.AreEqual(2f, GarmentRelaxer.FlattenedDistance(a, b, c, d, 1f), 1e-4f, "1이면 완전히 편 거리");
            Assert.AreEqual((Mathf.Sqrt(2f) + 2f) * 0.5f, GarmentRelaxer.FlattenedDistance(a, b, c, d, 0.5f), 1e-4f);
        }

        [Test]
        public void 고정_정점은_움직이지_않고_자유_정점끼리만_쉼_거리로_당긴다()
        {
            // 삼각형 하나(0,1,2): 0은 고정, 1·2는 자유. 1-2 쉼 거리를 지금(2)보다 짧게(1) 주면 둘이 가까워지고 0은 그대로다.
            // 고정 정점과 만나는 변은 쉼 거리 대신 지금 길이를 쓴다(고정한 끈·후드와 이음매가 벌어지지 않게).
            var positions = new[] { Vector3.zero, new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
            Vector3[] original = (Vector3[])positions.Clone();
            var isFree = new[] { false, true, true };
            var region = new[] { BodyRegion.LeftLowerArm, BodyRegion.LeftLowerArm, BodyRegion.LeftLowerArm };
            float Rest(int x, int y) => (x == 1 && y == 2) || (x == 2 && y == 1) ? 1f : -1f;

            int moved = GarmentRelaxer.Relax(positions, original, new[] { 0, 1, 2 }, isFree, region, EmptyBody(), null, Rest);

            Assert.AreEqual(2, moved);
            Assert.AreEqual(original[0], positions[0]);
            Assert.Less((positions[2] - positions[1]).magnitude, 2f, "쉼 거리 쪽으로 당겨진다");
            Assert.Greater((positions[2] - positions[1]).magnitude, 1f, "원래 위치에 붙여 두는 힘 때문에 끝까지 가지는 않는다");
        }

        [Test]
        public void 쉼_거리가_유한하지_않으면_지금_길이를_써서_NaN이_퍼지지_않는다()
        {
            var positions = new[] { Vector3.zero, new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f) };
            Vector3[] original = (Vector3[])positions.Clone();
            var region = new[] { BodyRegion.LeftLowerArm, BodyRegion.LeftLowerArm, BodyRegion.LeftLowerArm };

            GarmentRelaxer.Relax(positions, original, new[] { 0, 1, 2 }, new[] { false, true, true }, region, EmptyBody(), null, (x, y) => float.PositiveInfinity);

            foreach (Vector3 p in positions) Assert.IsFalse(float.IsNaN(p.x) || float.IsInfinity(p.x));
            Assert.AreEqual(0f, (positions[2] - original[2]).magnitude, 1e-4f, "지금 길이를 지키므로 그대로");
        }

        [Test]
        public void 아랫단_한도를_0으로_바꾸면_허벅지를_잰_경우에만_적용된다()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Hem", NameStyle.Plain);
            try
            {
                BodyCylinders body = BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(avatar.Animator), new Transform[0], includeBodyChildren: true));
                Assert.IsTrue(body.SetHemReach(0f), "테스트 아바타는 허벅지 본이 있어 잴 수 있다");
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }

        [Test]
        public void 늘어뜨리기는_고정점과_이어진_자유_정점만_아래로_내리고_떨어진_조각은_둔다()
        {
            // 0·1 위쪽 고정, 2·3은 0·1에 이어진 아래 천(자유), 4·5·6은 어디에도 이어지지 않은 조각(단추 등, 자유로 표시).
            var positions = new[]
            {
                new Vector3(0f, 1f, 0f), new Vector3(0.1f, 1f, 0f), new Vector3(0f, 0.9f, 0.05f), new Vector3(0.1f, 0.9f, 0.05f),
                new Vector3(0.5f, 0.9f, 0f), new Vector3(0.55f, 0.9f, 0f), new Vector3(0.5f, 0.85f, 0f),
            };
            Vector3[] original = (Vector3[])positions.Clone();
            var isFree = new[] { false, false, true, true, true, true, true };
            int[] triangles = { 0, 1, 2, 1, 3, 2, 4, 5, 6 };

            int moved = GarmentDraper.Drape(positions, original, triangles, isFree, TorsoBody(), null, out float drop);

            Assert.AreEqual(2, moved, "이어진 아래 천 두 정점만 움직인다");
            Assert.Greater(drop, 0f, "중력으로 아래로 내려간다");
            Assert.Less(positions[2].y, original[2].y);
            Assert.AreEqual(original[0], positions[0]);
            Assert.AreEqual(original[4], positions[4], "고정점과 이어지지 않은 조각은 그대로");
            Assert.AreEqual((original[2] - original[0]).magnitude, (positions[2] - positions[0]).magnitude, 0.01f, "변 길이는 지킨다(천이 늘어나지 않음)");
        }

        /// <summary>몸통 반지름을 잴 수 있는 테스트 몸(중력·두께가 0이 되지 않게). 몸통은 원점 근처라 위 천 조각과 닿지 않는다.</summary>
        static BodyCylinders TorsoBody()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Torso", NameStyle.Plain);
            try
            {
                var points = new System.Collections.Generic.List<(Vector3, Transform)>();
                for (float y = 1.05f; y < 1.46f; y += 0.05f)
                for (int i = 0; i < 16; i++)
                    points.Add((avatar.Root.transform.position + new Vector3(Mathf.Cos(i * Mathf.PI / 8f) * 0.02f, y, Mathf.Sin(i * Mathf.PI / 8f) * 0.02f), avatar[HumanBodyBones.Chest]));
                TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Torso", points);
                return BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(avatar.Animator), new Transform[0], includeBodyChildren: true));
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }

        static BodyCylinders EmptyBody()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Empty", NameStyle.Plain);
            try
            {
                return BodyCylinders.Build(BodyPointCloud.Collect(AvatarRig.FromAnimator(avatar.Animator), new Transform[0], includeBodyChildren: true));
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }
    }
}
