using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    public class FitPlacementTests
    {
        const float Tolerance = 1e-4f;

        static void AssertNear(Vector3 expected, Vector3 actual)
        {
            Assert.Less(Vector3.Distance(expected, actual), Tolerance, $"expected {expected} but was {actual}");
        }

        [Test]
        public void 원본_본에서_떨어진_거리를_비율만큼_늘려_대상_본에_붙인다()
        {
            var outfit = new BoneState(new Vector3(0f, 1.6f, 0.1f), Quaternion.identity, Vector3.one);

            BoneState placed = FitPlacement.Place(outfit, new Vector3(0f, 1.5f, 0f), new Vector3(0f, 0.8f, 0f), Quaternion.identity, 2f);

            AssertNear(new Vector3(0f, 1.0f, 0.2f), placed.Position);
            AssertNear(Vector3.one * 2f, placed.LossyScale);
        }

        [Test]
        public void 루트_방향_차이만큼_거리와_회전을_돌린다()
        {
            var outfit = new BoneState(new Vector3(0f, 0f, 1f), Quaternion.identity, Vector3.one);
            Quaternion delta = Quaternion.Euler(0f, 90f, 0f);

            BoneState placed = FitPlacement.Place(outfit, Vector3.zero, Vector3.zero, delta, 1f);

            AssertNear(new Vector3(1f, 0f, 0f), placed.Position);
            Assert.Less(Quaternion.Angle(delta, placed.Rotation), 0.01f);
        }

        [Test]
        public void 원본_기준_본이_없으면_대상_본_위치에_둔다()
        {
            var outfit = new BoneState(new Vector3(5f, 5f, 5f), Quaternion.identity, Vector3.one);

            BoneState placed = FitPlacement.Place(outfit, null, new Vector3(1f, 2f, 3f), Quaternion.identity, 3f);

            AssertNear(new Vector3(1f, 2f, 3f), placed.Position);
        }

        [Test]
        public void 부모_크기로_나눠_localScale을_구하고_0으로는_나누지_않는다()
        {
            AssertNear(new Vector3(1f, 2f, 3f), FitPlacement.LocalScaleFor(new Vector3(2f, 2f, 3f), new Vector3(2f, 1f, 0f)));
        }

        [Test]
        public void 원본_상자_안_상대_위치를_대상_상자의_같은_상대_위치로_옮긴다()
        {
            var source = new Bounds(new Vector3(0f, 1f, 0f), new Vector3(0.2f, 0.2f, 0.2f));
            var target = new Bounds(new Vector3(0f, 0.5f, 0.3f), new Vector3(0.4f, 0.4f, 0.8f));
            Vector3 frontEyeLevel = new Vector3(0f, 1.05f, 0.1f); // 원본 앞면, 높이 75%

            Vector3 mapped = FitPlacement.MapInBox(frontEyeLevel, source, target);

            AssertNear(new Vector3(0f, 0.6f, 0.7f), mapped);
        }

        [Test]
        public void 두께가_없는_축은_대상_상자_가운데로_둔다()
        {
            var flat = new Bounds(Vector3.zero, new Vector3(1f, 0f, 1f));
            var target = new Bounds(new Vector3(0f, 2f, 0f), Vector3.one);

            Assert.AreEqual(2f, FitPlacement.MapInBox(new Vector3(0f, 5f, 0f), flat, target).y, Tolerance);
        }

        [Test]
        public void 반경_안에서_가장_앞쪽_정점이_표면_깊이다()
        {
            Vector3[] face =
            {
                new Vector3(0f, 0f, 0.10f),   // 눈 앞 표면
                new Vector3(0.01f, 0f, 0.05f), // 같은 위치 안쪽
                new Vector3(0f, -0.5f, 0.30f), // 반경 밖 주둥이
            };

            Assert.AreEqual(0.10f, FitPlacement.FrontDepth(face, Vector2.zero, 0.1f).Value, Tolerance);
            Assert.IsNull(FitPlacement.FrontDepth(face, new Vector2(5f, 5f), 0.1f));
        }

        [Test]
        public void 표면과의_간격을_비율만큼_유지한다()
        {
            // 원본: 표면 0.10, 소품 0.12 (2cm 앞) -> 대상 표면 0.30, 비율 1.5 -> 0.33
            Assert.AreEqual(0.33f, FitPlacement.KeepSurfaceGap(0.12f, 0.10f, 0.30f, 1.5f), Tolerance);
        }

        [Test]
        public void 루트_기준_축_변환은_왕복하면_원래_좌표()
        {
            var root = new GameObject("Root").transform;
            try
            {
                root.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 90f, 0f));
                Vector3 world = new Vector3(4f, 5f, 6f);

                AssertNear(world, FitPlacement.FromRootFrame(root, FitPlacement.ToRootFrame(root, world)));
                AssertNear(new Vector3(0f, 0f, 1f), FitPlacement.ToRootFrame(root, root.position + root.forward));
            }
            finally
            {
                Object.DestroyImmediate(root.gameObject);
            }
        }

        [Test]
        public void 본_축은_월드_방향과_가장_나란한_로컬_축이다()
        {
            Assert.AreEqual(0, FitPlacement.DominantAxis(Quaternion.identity, Vector3.left));
            Assert.AreEqual(1, FitPlacement.DominantAxis(Quaternion.Euler(0f, 0f, 90f), Vector3.left), "z축 90도 회전이면 로컬 y가 월드 x 방향");
            Assert.AreEqual(2, FitPlacement.DominantAxis(Quaternion.identity, new Vector3(0.1f, 0.2f, -1f)));
        }

        [Test]
        public void 길이_축만_길이_비율_나머지는_굵기_비율()
        {
            AssertNear(new Vector3(0.5f, 2f, 2f), FitPlacement.AxisScale(Vector3.one, 0, 0.5f, 2f));
            AssertNear(new Vector3(4f, 1f, 4f), FitPlacement.AxisScale(new Vector3(2f, 2f, 2f), 1, 0.5f, 2f));
        }

        [Test]
        public void 격자에서_주변_칸의_가장_가까운_몸_정점을_찾는다()
        {
            var surface = new BodySurface(new List<(Vector3, Vector3)>
            {
                (new Vector3(0f, 0f, 0f), Vector3.up),
                (new Vector3(0.05f, 0f, 0f), Vector3.right),
                (new Vector3(5f, 5f, 5f), Vector3.up),
            }, 0.1f);

            Assert.IsTrue(surface.TryNearest(new Vector3(0.04f, 0.01f, 0f), out Vector3 point, out Vector3 normal));
            AssertNear(new Vector3(0.05f, 0f, 0f), point);
            AssertNear(Vector3.right, normal);
            Assert.IsFalse(surface.TryNearest(new Vector3(2f, 2f, 2f), out _, out _), "근처에 몸이 없으면 찾지 않는다");
        }

        [Test]
        public void 몸_안쪽_정점만_여유_간격까지_밀어낸다()
        {
            Vector3 inside = FitPlacement_PushOut(new Vector3(0f, -0.01f, 0f));
            Vector3 outside = FitPlacement_PushOut(new Vector3(0f, 0.05f, 0f));
            Vector3 tooDeep = FitPlacement_PushOut(new Vector3(0f, -0.5f, 0f));

            AssertNear(new Vector3(0f, 0.002f, 0f), inside);
            AssertNear(new Vector3(0f, 0.05f, 0f), outside);
            AssertNear(new Vector3(0f, -0.5f, 0f), tooDeep);
        }

        static Vector3 FitPlacement_PushOut(Vector3 v) => BodySurface.PushOut(v, Vector3.zero, Vector3.up, 0.002f, 0.1f);

        [Test]
        public void 밀어낸_정점의_이동량을_이웃_고리마다_이전_고리_대비_비율로_나눈다()
        {
            // 0-1-2-3 으로 이어진 띠: 0번만 밀었다.
            int[] triangles = { 0, 1, 1, 1, 2, 2, 2, 3, 3 };
            Vector3[] deltas = { Vector3.up, Vector3.zero, Vector3.zero, Vector3.zero };

            Vector3[] spread = PenetrationFixer.SpreadToNeighbors(deltas, triangles, new[] { 0.5f, 0.25f });

            AssertNear(Vector3.up, spread[0]);
            AssertNear(Vector3.up * 0.5f, spread[1]);
            AssertNear(Vector3.up * 0.125f, spread[2]);
            AssertNear(Vector3.zero, spread[3]);
        }

        [Test]
        public void 같은_위치에_겹친_이음새_정점은_함께_밀린다()
        {
            // 0과 1은 UV 이음새로 나뉜 같은 위치의 정점, 2는 이웃.
            Vector3[] positions = { Vector3.zero, Vector3.zero, Vector3.right };
            Vector3[] deltas = { Vector3.up, Vector3.zero, Vector3.zero };
            int[] triangles = { 1, 2, 2 };

            Vector3[] result = PenetrationFixer.SpreadWelded(deltas, positions, triangles);

            AssertNear(Vector3.up, result[0]);
            AssertNear(Vector3.up, result[1]);
            AssertNear(Vector3.up * 0.6f, result[2]);
        }

        [Test]
        public void 삭제된_본에_걸린_가중치는_빼고_정규화한다()
        {
            var bone = new GameObject("Bone").transform;
            try
            {
                bone.position = new Vector3(1f, 0f, 0f);
                Matrix4x4?[] skin = SkinningMath.SkinMatrices(new[] { bone, null }, new[] { Matrix4x4.identity, Matrix4x4.identity });
                var half = new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f };
                var none = new BoneWeight { boneIndex0 = 1, weight0 = 1f };

                Assert.IsTrue(SkinningMath.TryBlend(skin, half, out Matrix4x4 m));
                AssertNear(new Vector3(1f, 0f, 0f), m.MultiplyPoint3x4(Vector3.zero));
                Assert.IsFalse(SkinningMath.TryBlend(skin, none, out _), "유효한 본이 없으면 위치를 계산하지 않는다");
            }
            finally
            {
                Object.DestroyImmediate(bone.gameObject);
            }
        }
    }
}
