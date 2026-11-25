using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>후드 두상 맞춤: 머리 본에 묶인 후드를 모양 그대로 머리에서 떼어 머리 관통을 줄이고, 몸 관통이 늘면 옮기지 않는다</summary>
    public class HoodHeadFitterTests
    {
        const float HeadWidth = 0.4f;
        TestAvatar avatar;
        AvatarRig rig;

        [SetUp]
        public void SetUp()
        {
            avatar = TestRigBuilder.CreateHumanoid("Hooded", NameStyle.Plain);
            rig = AvatarRig.FromAnimator(avatar.Animator);
        }

        [TearDown]
        public void TearDown() => TestRigBuilder.Destroy(avatar.Root);

        /// <summary>머리 뒤(-z)에 수직으로 선 후드 삼각형 하나(머리 본에 묶임). 머리 중심에서 hoodDepth만큼 뒤.</summary>
        SkinnedMeshRenderer Hood(float hoodDepth)
        {
            Transform head = avatar[HumanBodyBones.Head];
            Vector3 c = head.position + Vector3.back * hoodDepth;
            SkinnedMeshRenderer hood = TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Hood", new List<(Vector3, Transform)>
            {
                (c + new Vector3(-0.05f, -0.05f, 0f), head), (c + new Vector3(0f, 0.05f, 0f), head), (c + new Vector3(0.05f, -0.05f, 0f), head),
            });
            hood.sharedMesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            return hood;
        }

        Vector3 HeadCenter => FitPlacement.ToRootFrame(avatar.Root.transform, avatar[HumanBodyBones.Head].position);

        /// <summary>후드 삼각형 가운데를 앞뒤(z)로 뚫는 짧은 머리 변(후드 면 뒤 depth까지)</summary>
        EdgeGrid HeadPiercing(float hoodDepth, float reach)
        {
            Vector3 c = HeadCenter + Vector3.back * hoodDepth;
            return new EdgeGrid(new List<(Vector3, Vector3)> { (c + Vector3.forward * 0.01f, c + Vector3.back * reach) });
        }

        [Test]
        public void 머리가_뚫는_후드는_모양_그대로_머리에서_떼어_관통을_줄인다()
        {
            SkinnedMeshRenderer hood = Hood(0.1f);
            Vector3[] before = hood.sharedMesh.vertices;

            var (shift, headBefore, headAfter) = HoodHeadFitter.Fit(hood, rig, HeadPiercing(0.1f, 0.005f), null, HeadCenter, HeadWidth, "fitted");

            Assert.Greater(shift, 0f);
            Assert.AreEqual(1, headBefore);
            Assert.AreEqual(0, headAfter);
            Vector3[] after = hood.sharedMesh.vertices;
            Vector3 d0 = after[0] - before[0];
            Assert.IsTrue(Enumerable.Range(0, 3).All(i => (after[i] - before[i] - d0).sqrMagnitude < 1e-10f), "세 정점이 같은 만큼 옮겨져 모양이 그대로");
            Assert.LessOrEqual(shift, HeadWidth * HoodHeadFitter.MaxShiftToHeadWidth + 1e-5f, "최대 이동 거리 이내");
        }

        [Test]
        public void 떼어도_몸_관통이_늘면_옮기지_않는다()
        {
            SkinnedMeshRenderer hood = Hood(0.1f);
            Vector3[] before = hood.sharedMesh.vertices;
            // 후드 면 바로 뒤에서 시작해 뒤로 뻗은 몸 변: 후드가 뒤로 옮겨지면 뚫린다.
            Vector3 c = HeadCenter + Vector3.back * 0.103f;
            var body = new EdgeGrid(new List<(Vector3, Vector3)> { (c, c + Vector3.back * 0.1f) });

            var (shift, _, _) = HoodHeadFitter.Fit(hood, rig, HeadPiercing(0.1f, 0.005f), body, HeadCenter, HeadWidth, "fitted");

            Assert.AreEqual(0f, shift);
            CollectionAssert.AreEqual(before, hood.sharedMesh.vertices, "메시를 바꾸지 않는다");
        }

        [Test]
        public void UV_이음매로_갈라진_같은_위치_정점은_함께_옮긴다()
        {
            // 후드 삼각형(머리 본)과, 그 꼭짓점 하나를 같은 위치의 다른 정점(가슴 본)으로 쓰는 이웃 삼각형
            Transform head = avatar[HumanBodyBones.Head], chest = avatar[HumanBodyBones.Chest];
            Vector3 c = head.position + Vector3.back * 0.1f;
            Vector3 a = c + new Vector3(-0.05f, -0.05f, 0f), b = c + new Vector3(0f, 0.05f, 0f), d = c + new Vector3(0.05f, -0.05f, 0f);
            SkinnedMeshRenderer hood = TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, "Hood", new List<(Vector3, Transform)>
            {
                (a, head), (b, head), (d, head), (d, chest), (d + Vector3.down * 0.1f, chest), (a + Vector3.down * 0.1f, chest),
            });
            hood.sharedMesh.SetIndices(new[] { 0, 1, 2, 3, 4, 5 }, MeshTopology.Triangles, 0);
            Vector3[] before = hood.sharedMesh.vertices;

            var (shift, _, _) = HoodHeadFitter.Fit(hood, rig, HeadPiercing(0.1f, 0.005f), null, HeadCenter, HeadWidth, "fitted");

            Assert.Greater(shift, 0f);
            Vector3[] after = hood.sharedMesh.vertices;
            Assert.AreEqual((after[2] - before[2]).magnitude, (after[3] - before[3]).magnitude, 1e-6f, "이음매 양쪽 정점이 같은 거리만큼 움직인다");
        }

        [Test]
        public void 머리가_뚫지_않으면_아무것도_하지_않는다()
        {
            SkinnedMeshRenderer hood = Hood(0.3f);
            Mesh original = hood.sharedMesh;

            var (shift, headBefore, _) = HoodHeadFitter.Fit(hood, rig, HeadPiercing(0.1f, 0.005f), null, HeadCenter, HeadWidth, "fitted");

            Assert.AreEqual(0f, shift);
            Assert.AreEqual(0, headBefore);
            Assert.AreSame(original, hood.sharedMesh);
        }
    }
}
