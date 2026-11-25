using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>모자 착용 보정: 두상 밖으로 밀어내기와 귀 구멍</summary>
    public class HatFittingTests
    {
        TestAvatar chibi;
        AvatarRig rig;
        Transform head;

        [SetUp]
        public void SetUp()
        {
            // 머리 정육면체 폭 0.8 * 0.5 = 0.4. 꼭짓점 법선은 머리 중심에서 바깥 방향.
            chibi = TestRigBuilder.CreateHumanoid("Chibi", NameStyle.Plain, scale: 0.5f, headWidth: 0.8f);
            rig = AvatarRig.FromAnimator(chibi.Animator);
            head = chibi[HumanBodyBones.Head];
        }

        [TearDown]
        public void TearDown()
        {
            TestRigBuilder.Destroy(chibi.Root);
        }

        /// <summary>MeshRenderer 소품. 정점은 월드 좌표로 받아 오브젝트 공간으로 바꿔 넣는다.</summary>
        static MeshRenderer CreateProp(Transform parent, string name, Vector3[] worldVertices, int[] triangles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0.05f, 0.1f, 0f);
            go.transform.localScale = Vector3.one * 2f;
            var mesh = new Mesh { vertices = System.Array.ConvertAll(worldVertices, go.transform.InverseTransformPoint) };
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            return go.AddComponent<MeshRenderer>();
        }

        [Test]
        public void 본_없는_모자의_머리_안쪽_정점을_두상_밖으로_민다()
        {
            Vector3 corner = head.position + new Vector3(0.2f, 0.4f, 0.2f);
            Vector3 normal = Vector3.one.normalized;
            MeshRenderer hat = CreateProp(head, "Hat",
                new[] { corner - normal * 0.01f, corner + Vector3.up * 0.3f, corner + Vector3.up * 0.3f + Vector3.right * 0.1f },
                new[] { 0, 1, 2 });
            Mesh original = hat.GetComponent<MeshFilter>().sharedMesh;

            TargetSurface surface = PenetrationFixer.BuildSurface(rig, new[] { hat.transform });
            HeadShape headShape = surface.HeadShape;
            PenetrationResult result = PenetrationFixer.Fix(hat, surface.Head, rig, "Hat_fitted",
                headShape.Width * PenetrationFixer.MarginToHeadWidth, headShape.Width * PenetrationFixer.MaxPushToHeadWidth);

            Assert.AreEqual(0.4f, headShape.Width, 1e-4f);
            Assert.AreEqual(head.position.y + 0.4f, headShape.Crown.y, 1e-4f, "정수리는 머리 정육면체 윗면");
            Assert.AreEqual(1, result.PushedVertices);
            MeshFilter filter = hat.GetComponent<MeshFilter>();
            Assert.AreNotSame(original, filter.sharedMesh, "원본 모자 메시는 그대로 두고 새 메시로 교체");
            Vector3 fitted = hat.transform.TransformPoint(filter.sharedMesh.vertices[0]);
            Assert.AreEqual(headShape.Width * PenetrationFixer.MarginToHeadWidth, Vector3.Dot(fitted - corner, normal), 1e-4f,
                "머리 표면 바깥, 여유 간격만큼 떨어진 자리");
        }

        [Test]
        public void 이미_다시_배치한_정점으로_표시된_정점은_밀어내기에서_건너뛴다()
        {
            // 표면 간격 보존으로 다시 배치한 정점은 밀지 않고, 범위 밖(후드 등) 정점만 미는 경로
            Vector3 corner = head.position + new Vector3(0.2f, 0.4f, 0.2f);
            Vector3 normal = Vector3.one.normalized;
            MeshRenderer hat = CreateProp(head, "Hat",
                new[] { corner - normal * 0.01f, corner + Vector3.up * 0.3f, corner + Vector3.up * 0.3f + Vector3.right * 0.1f },
                new[] { 0, 1, 2 });
            Mesh original = hat.GetComponent<MeshFilter>().sharedMesh;
            TargetSurface surface = PenetrationFixer.BuildSurface(rig, new[] { hat.transform });
            HeadShape headShape = surface.HeadShape;

            PenetrationResult result = PenetrationFixer.Fix(hat, surface.Head, rig, "Hat_fitted",
                headShape.Width * PenetrationFixer.MarginToHeadWidth, headShape.Width * PenetrationFixer.MaxPushToHeadWidth,
                isKept: new[] { true, false, false });

            Assert.AreEqual(0, result.PushedVertices, "머리 안쪽 정점이라도 표시된 정점은 밀지 않는다");
            Assert.AreSame(original, hat.GetComponent<MeshFilter>().sharedMesh, "밀 정점이 없으면 메시를 바꾸지 않는다");
        }

        [Test]
        public void 귀가_뚫고_지나가는_모자_삼각형만_지우고_원본은_그대로_둔다()
        {
            var root = new GameObject("Root").transform;
            try
            {
                // 수평으로 누운 모자 삼각형 둘. 귀 변(세로 선분)은 첫째만 뚫고, 둘째 옆을 스치는 변은 닿지 않는다.
                MeshRenderer hat = CreateProp(root, "Hat",
                    new[] { Vector3.zero, new Vector3(0.02f, 0f, 0f), new Vector3(0f, 0f, 0.02f),
                            new Vector3(1f, 0f, 0f), new Vector3(1.02f, 0f, 0f), new Vector3(1f, 0f, 0.02f) },
                    new[] { 0, 1, 2, 3, 4, 5 });
                Mesh original = hat.GetComponent<MeshFilter>().sharedMesh;
                var ear = new List<(Vector3, Vector3)>
                {
                    (new Vector3(0.005f, -0.05f, 0.005f), new Vector3(0.005f, 0.05f, 0.005f)),
                    (new Vector3(1.03f, -0.05f, 0.03f), new Vector3(1.03f, 0.05f, 0.03f)),
                };

                int removed = AppendageHoleCutter.Cut(hat, ear, root, "Hat_cut");

                Assert.AreEqual(1, removed);
                CollectionAssert.AreEqual(new[] { 3, 4, 5 }, hat.GetComponent<MeshFilter>().sharedMesh.triangles, "귀가 지나지 않은 삼각형은 남는다");
                Assert.AreEqual(6, original.triangles.Length, "원본 메시는 그대로");
                Assert.AreEqual(0, AppendageHoleCutter.Cut(hat, new List<(Vector3, Vector3)>(), root, "Hat_cut"), "귀가 없으면 지우지 않는다");
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [TestCase("Ear.001.L", true)]
        [TestCase("ear1.L", true)]
        [TestCase("PB_Ear.L", true)]
        [TestCase("Kemomimi_L", true)]
        [TestCase("耳.L", true)]
        [TestCase("CatEar", true)]
        [TestCase("Earring.L", false)]
        [TestCase("Ear_Ring.L", false)]
        [TestCase("Earphone_L", false)]
        [TestCase("Year", false)]
        [TestCase("Hair_Bear", false)]
        public void 머리_아래_귀_이름_본의_정점만_귀로_본다(string boneName, bool isEar)
        {
            var bone = new GameObject(boneName).transform;
            bone.SetParent(head, false);
            bone.localPosition = Vector3.up * 0.5f;
            SkinnedMeshRenderer ears = TestRigBuilder.AddSkinnedMesh(chibi.Root.transform, "Ears", new List<(Vector3, Transform)>
            {
                (bone.position, bone), (bone.position + Vector3.up * 0.1f, bone), (bone.position + Vector3.right * 0.05f, bone),
            });
            ears.sharedMesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0);

            List<(Vector3, Vector3)> edges = AppendageHoleCutter.EarEdges(rig, null);

            Assert.AreEqual(isEar ? 3 : 0, edges.Count, "귀 삼각형 하나의 변 3개");
        }
    }
}
