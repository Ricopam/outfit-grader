using System.Linq;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>겹침 정리 미는 방향: 옷 법선이 몸 바깥과 크게 어긋나면 몸 바깥 법선으로 민다</summary>
    public class ClothPierceDirectionTests
    {
        const float MaxDistance = 1f;
        Transform root;

        [SetUp] public void CreateRoot() => root = new GameObject("root").transform;
        [TearDown] public void DestroyRoot() => Object.DestroyImmediate(root.gameObject);

        /// <summary>원점 근처에 바깥 법선이 -z(등쪽)인 몸 표면 점 하나. +z 쪽이 몸 안이다.</summary>
        static readonly Vector3 Inside = new Vector3(0f, 0f, 0.01f);
        static BodySurface BackSurface() => new BodySurface(new List<(Vector3, Vector3)> { (Vector3.zero, Vector3.back) }, 0.1f);

        [Test]
        public void 수평으로_접힌_옷은_몸_바깥_법선으로_민다()
        {
            Vector3[] result = ClothPierceResolver.TowardBodyOutside(new[] { Vector3.up }, new[] { Inside }, BackSurface(), root, MaxDistance);

            Assert.AreEqual(Vector3.back, result[0]);
        }

        [Test]
        public void 몸_바깥과_비슷한_옷_법선은_그대로_둔다()
        {
            Vector3 normal = new Vector3(0f, 0.5f, -1f).normalized;

            Vector3[] result = ClothPierceResolver.TowardBodyOutside(new[] { normal }, new[] { Inside }, BackSurface(), root, MaxDistance);

            Assert.AreEqual(normal, result[0]);
        }

        [Test]
        public void 어긋난_법선은_안쪽_성분이_있어도_몸_바깥으로_바꾼다()
        {
            // 거의 수평인 법선은 메시가 조금만 기울어도 안쪽 성분을 가진다. 부호를 따르면 몸 안으로 밀린다.
            Vector3 slightlyInward = new Vector3(0f, 1f, 0.2f).normalized;

            Vector3[] result = ClothPierceResolver.TowardBodyOutside(new[] { slightlyInward }, new[] { Inside }, BackSurface(), root, MaxDistance);

            Assert.AreEqual(Vector3.back, result[0]);
        }

        [Test]
        public void 몸_안쪽을_향한_뒷면_법선은_뒤집어_바깥으로_맞춘다()
        {
            Vector3 inward = new Vector3(0f, 0.3f, 1f).normalized;

            Vector3[] result = ClothPierceResolver.TowardBodyOutside(new[] { inward }, new[] { Inside }, BackSurface(), root, MaxDistance);

            Assert.AreEqual(-inward, result[0]);
        }

        [Test]
        public void 몸_표면_바깥에_있는_정점은_어긋나도_그대로_둔다()
        {
            // 겨드랑이·엉덩이 뒤 모서리처럼 이미 몸 밖에 있는 정점이 다른 부위 아랫면을 짚어 아래로 바뀌면 옷판이 미끄러진다.
            Vector3 outside = new Vector3(0f, 0f, -0.005f);

            Vector3[] result = ClothPierceResolver.TowardBodyOutside(new[] { Vector3.up }, new[] { outside }, BackSurface(), root, MaxDistance);

            Assert.AreEqual(Vector3.up, result[0]);
        }

        [Test]
        public void 몸_점이_한도보다_멀면_그대로_둔다()
        {
            Vector3[] result = ClothPierceResolver.TowardBodyOutside(new[] { Vector3.up }, new[] { new Vector3(0f, 0f, 0.05f) }, BackSurface(), root, maxDistance: 0.01f);

            Assert.AreEqual(Vector3.up, result[0]);
        }

        [Test]
        public void 남은_관통_정리_방향은_가까운_몸_표면_법선을_쓴다()
        {
            Vector3? outward = ClothPierceResolver.OutwardAt(Inside, BackSurface(), null, MaxDistance);

            Assert.AreEqual(Vector3.back, outward);
        }

        [Test]
        public void 근처에_몸_정점도_원통_표도_없으면_방향을_정하지_않는다()
        {
            Vector3? outward = ClothPierceResolver.OutwardAt(new Vector3(5f, 5f, 5f), BackSurface(), null, MaxDistance);

            Assert.IsNull(outward, "방향을 모르면 그 삼각형은 옮기지 않는다");
        }

        /// <summary>
        /// y=0 수평 삼각형 두 개(공유 변 x=0). 왼쪽 삼각형 t0은 세로 몸 변(x=-0.02)에 뚫려 있다.
        /// 정점은 루트 기준 그대로(단위 행렬)다.
        /// </summary>
        static (Vector3[] vertices, int[] triangles, Matrix4x4?[] matrices) Pair()
        {
            Vector3[] vertices =
            {
                new Vector3(-0.06f, 0f, -0.03f), new Vector3(0f, 0f, 0.03f), new Vector3(0f, 0f, -0.03f),
                new Vector3(0.06f, 0f, 0.03f),
            };
            int[] triangles = { 0, 1, 2, 2, 1, 3 };
            return (vertices, triangles, vertices.Select(_ => (Matrix4x4?)Matrix4x4.identity).ToArray());
        }

        [Test]
        public void 남은_관통_삼각형을_바깥으로_옮겨_풀고_메시_정점에_기록한다()
        {
            var (vertices, triangles, matrices) = Pair();
            Vector3[] original = (Vector3[])vertices.Clone();
            var body = new EdgeGrid(new List<(Vector3, Vector3)> { (new Vector3(-0.02f, -0.1f, -0.005f), new Vector3(-0.02f, 0.1f, -0.005f)) });

            int cleared = ClothPierceResolver.ClearRemaining(vertices, original, matrices, triangles, body, _ => Vector3.forward, root, 0.01f, null);

            Assert.AreEqual(1, cleared);
            Assert.IsFalse(body.Pierces(vertices[0], vertices[1], vertices[2]), "정리한 삼각형은 더 이상 뚫리지 않는다");
            Assert.Greater(vertices[0].z, original[0].z, "옮긴 위치가 메시 정점에 기록된다");
        }

        [Test]
        public void 옮기면_이웃이_새로_뚫리는_삼각형은_되돌린다()
        {
            var (vertices, triangles, matrices) = Pair();
            Vector3[] original = (Vector3[])vertices.Clone();
            // 왼쪽 삼각형을 뚫는 변과, 오른쪽 삼각형 바로 앞(+z로 옮기면 뚫릴 자리)의 변
            var body = new EdgeGrid(new List<(Vector3, Vector3)>
            {
                (new Vector3(-0.02f, -0.1f, -0.005f), new Vector3(-0.02f, 0.1f, -0.005f)),
                (new Vector3(0.015f, -0.1f, 0.039f), new Vector3(0.015f, 0.1f, 0.039f)),
            });

            int cleared = ClothPierceResolver.ClearRemaining(vertices, original, matrices, triangles, body, _ => Vector3.forward, root, 0.01f, null);

            Assert.AreEqual(0, cleared);
            CollectionAssert.AreEqual(original, vertices, "받아들이지 않은 이동은 메시에 남지 않는다");
        }

        [Test]
        public void 남은_겹침_부위는_가장_가까운_Humanoid_조상으로_정한다()
        {
            TestAvatar avatar = TestRigBuilder.CreateHumanoid("Parts", NameStyle.Plain);
            try
            {
                AvatarRig rig = AvatarRig.FromAnimator(avatar.Animator);
                var clothBone = new GameObject("Sleeve_Cloth").transform;
                clothBone.SetParent(avatar[HumanBodyBones.LeftLowerArm], false);

                Assert.AreEqual("어깨/팔", ClothPierceResolver.PartOf(rig, clothBone), "옷 본은 붙은 Humanoid 본의 부위");
                Assert.AreEqual("몸통", ClothPierceResolver.PartOf(rig, avatar[HumanBodyBones.Chest]));
                Assert.AreEqual("손", ClothPierceResolver.PartOf(rig, avatar[HumanBodyBones.LeftHand]));
                Assert.AreEqual("기타", ClothPierceResolver.PartOf(rig, null));
            }
            finally
            {
                TestRigBuilder.Destroy(avatar.Root);
            }
        }

        [Test]
        public void 위치를_모르거나_근처에_몸이_없으면_그대로_둔다()
        {
            Vector3[] normals = { Vector3.up, Vector3.up };
            Vector3[] positions = { Vector3.positiveInfinity, new Vector3(5f, 5f, 5f) };

            Vector3[] result = ClothPierceResolver.TowardBodyOutside(normals, positions, BackSurface(), root, MaxDistance);

            Assert.AreEqual(Vector3.up, result[0]);
            Assert.AreEqual(Vector3.up, result[1]);
        }
    }
}
