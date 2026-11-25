using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>수인·퍼리 아바타에 옷을 입힐 때: 옷에 가려지는 털 숨김과 꼬리 구멍용 꼬리 판정</summary>
    public class FurAndTailTests
    {
        TestAvatar avatar;
        AvatarRig rig;

        [SetUp]
        public void SetUp()
        {
            avatar = TestRigBuilder.CreateHumanoid("Kemono", NameStyle.Plain);
            rig = AvatarRig.FromAnimator(avatar.Animator);
        }

        [TearDown]
        public void TearDown()
        {
            TestRigBuilder.Destroy(avatar.Root);
        }

        /// <summary>부위 본 하나와 그 본에 묶인 삼각형 하나짜리 메시</summary>
        SkinnedMeshRenderer AddPart(Transform parent, string boneName, Vector3 localPosition)
        {
            var bone = new GameObject(boneName).transform;
            bone.SetParent(parent, false);
            bone.localPosition = localPosition;
            SkinnedMeshRenderer mesh = TestRigBuilder.AddSkinnedMesh(avatar.Root.transform, boneName + "_Mesh", new List<(Vector3, Transform)>
            {
                (bone.position, bone), (bone.position + Vector3.forward * 0.05f, bone), (bone.position + Vector3.right * 0.02f, bone),
            });
            mesh.sharedMesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);
            return mesh;
        }

        /// <summary>점 center를 바깥 법선 normal 방향으로 덮는 옷 조각(정점 3개, 모두 같은 법선)</summary>
        MeshRenderer AddGarment(Vector3 center, Vector3 normal)
        {
            var go = new GameObject("Garment");
            go.transform.SetParent(avatar.Root.transform, false);
            Vector3 side = Vector3.Cross(normal, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.Cross(normal, Vector3.up).normalized : Vector3.right;
            var mesh = new Mesh
            {
                vertices = new[] { center, center + side * 0.01f, center - side * 0.01f },
                normals = new[] { normal, normal, normal },
                triangles = new[] { 0, 1, 2 },
            };
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        [TestCase("Tail.001", true)]
        [TestCase("Tail_Root", true)]
        [TestCase("Shippo", true)]
        [TestCase("tale.001", true)]
        [TestCase("Retale", false)]
        [TestCase("Ponytail", false)]
        [TestCase("Tailor", false)]
        public void 엉덩이_바로_아래_꼬리_이름_본만_꼬리로_본다(string boneName, bool isTail)
        {
            AddPart(avatar[HumanBodyBones.Hips], boneName, Vector3.back * 0.1f);

            Assert.AreEqual(isTail ? 3 : 0, AppendageHoleCutter.TailEdges(rig, null).Count);
        }

        [Test]
        public void 머리_아래_꼬리_이름_본은_꼬리로_보지_않는다()
        {
            // 머리카락 묶음(Tail)은 가장 가까운 Humanoid 조상이 Head라 꼬리 구멍 대상이 아니다.
            AddPart(avatar[HumanBodyBones.Head], "Hair_Tail", Vector3.back * 0.1f);

            Assert.AreEqual(0, AppendageHoleCutter.TailEdges(rig, null).Count);
        }

        [Test]
        public void 뿌리가_옷_안쪽인_털_묶음만_지우고_원본은_그대로_둔다()
        {
            SkinnedMeshRenderer fur = AddPart(avatar[HumanBodyBones.Chest], "FurChest", Vector3.forward * 0.05f);
            Mesh original = fur.sharedMesh;
            Transform root = fur.bones[0];
            // 털 뿌리 1cm 앞에 옷이 바깥(앞)을 향해 있다: 뿌리는 옷 안쪽.
            MeshRenderer garment = AddGarment(root.position + Vector3.forward * 0.01f, Vector3.forward);

            List<(string renderer, int tufts, int triangles)> hidden = FurHider.Hide(rig, new Renderer[] { garment });

            Assert.AreEqual(1, hidden.Count);
            Assert.AreEqual((1, 1), (hidden[0].tufts, hidden[0].triangles));
            Assert.AreEqual(0, fur.sharedMesh.triangles.Length, "털 삼각형을 지운 새 메시로 교체");
            Assert.AreEqual(3, original.triangles.Length, "원본 메시는 그대로");
        }

        [Test]
        public void 뿌리가_옷_밖이거나_옷에서_먼_털은_남긴다()
        {
            SkinnedMeshRenderer outside = AddPart(avatar[HumanBodyBones.Chest], "FurChest", Vector3.forward * 0.05f);
            SkinnedMeshRenderer far = AddPart(avatar[HumanBodyBones.LeftLowerArm], "FurLowerArm.L", Vector3.zero);
            Mesh outsideMesh = outside.sharedMesh, farMesh = far.sharedMesh;
            // 옷은 털 뿌리 1cm 뒤에서 앞을 향한다: 뿌리는 옷 바깥. 팔 털은 옷에서 멀다.
            MeshRenderer garment = AddGarment(outside.bones[0].position + Vector3.back * 0.01f, Vector3.forward);

            List<(string renderer, int tufts, int triangles)> hidden = FurHider.Hide(rig, new Renderer[] { garment });

            Assert.AreEqual(0, hidden.Count);
            Assert.AreSame(outsideMesh, outside.sharedMesh);
            Assert.AreSame(farMesh, far.sharedMesh);
        }

        [Test]
        public void 옷이_몸에서_떨어져_있어도_몸통_굵기_안이면_옷_안쪽_털을_지운다()
        {
            // 간격 보존으로 옷이 가슴에서 떨어져 놓이면 털 뿌리와 옷 사이가 엉덩이 높이 기준 한도(1.0 x 8% = 0.08)를 넘는다.
            SkinnedMeshRenderer fur = AddPart(avatar[HumanBodyBones.Chest], "FurChest", Vector3.forward * 0.05f);
            MeshRenderer garment = AddGarment(fur.bones[0].position + Vector3.forward * 0.12f, Vector3.forward);

            Assert.AreEqual(0, FurHider.Hide(rig, new Renderer[] { garment }).Count, "몸통 굵기를 모르면 엉덩이 높이 기준 그대로");
            Assert.AreEqual(1, FurHider.Hide(rig, new Renderer[] { garment }, torsoRadius: 0.3f).Count,
                "몸통 반지름 0.3이면 한도 0.3 x 0.6 = 0.18 안이라 지운다");
        }

        [TestCase("FurChest", true)]
        [TestCase("Fur_01", true)]
        [TestCase("ChestFluff", true)]
        [TestCase("Further", false)]
        [TestCase("FluffyHair", false)]
        [TestCase("Hair_01", false)]
        public void 털_이름_본만_털로_본다(string boneName, bool isFur)
        {
            SkinnedMeshRenderer part = AddPart(avatar[HumanBodyBones.Chest], boneName, Vector3.forward * 0.05f);
            MeshRenderer garment = AddGarment(part.bones[0].position + Vector3.forward * 0.01f, Vector3.forward);

            Assert.AreEqual(isFur ? 1 : 0, FurHider.Hide(rig, new Renderer[] { garment }).Count);
        }
    }
}
