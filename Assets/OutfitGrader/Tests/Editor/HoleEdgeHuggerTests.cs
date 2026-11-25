using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>구멍 가장자리 붙이기: 새로 생긴 구멍 가장자리만 부위 쪽으로 당기고, 원래 테두리와 멀리 있는 정점은 두지 않는다</summary>
    public class HoleEdgeHuggerTests
    {
        /// <summary>xz 평면 위 3x3 칸 격자(정점 4x4, 간격 0.1). 가운데 칸 두 삼각형을 지우면 구멍이 생긴다.</summary>
        static (Vector3[] positions, int[] before, int[] after) Grid()
        {
            var positions = new List<Vector3>();
            for (int z = 0; z < 4; z++)
                for (int x = 0; x < 4; x++)
                    positions.Add(new Vector3(x * 0.1f, 0f, z * 0.1f));
            var before = new List<int>();
            var after = new List<int>();
            for (int z = 0; z < 3; z++)
                for (int x = 0; x < 3; x++)
                {
                    int a = z * 4 + x, b = a + 1, c = a + 4, d = a + 5;
                    int[] cell = { a, c, b, b, c, d };
                    before.AddRange(cell);
                    bool isCenter = x == 1 && z == 1;
                    if (!isCenter) after.AddRange(cell);
                }
            return (positions.ToArray(), before.ToArray(), after.ToArray());
        }

        [Test]
        public void 새_구멍_가장자리만_부위_쪽으로_당기고_원래_테두리는_조금만_따라온다()
        {
            var (positions, before, after) = Grid();
            // 구멍 가운데(0.15, 0.15)를 세로로 지나는 가는 꼬리.
            var tail = new List<(Vector3, Vector3)> { (new Vector3(0.15f, -0.1f, 0.15f), new Vector3(0.15f, 0.1f, 0.15f)) };

            Vector3[] deltas = HoleEdgeHugger.Pull(positions, positions, before, after, tail, margin: 0.01f, maxPull: 1f);

            int holeCorner = 1 * 4 + 1;
            float beforeGap = (new Vector3(positions[holeCorner].x, 0f, positions[holeCorner].z) - new Vector3(0.15f, 0f, 0.15f)).magnitude;
            Vector3 moved = positions[holeCorner] + deltas[holeCorner];
            float afterGap = (new Vector3(moved.x, 0f, moved.z) - new Vector3(0.15f, 0f, 0.15f)).magnitude;
            Assert.Less(afterGap, beforeGap * 0.5f, "구멍 모서리가 꼬리 쪽으로 오므려진다");
            Assert.Less(deltas[0].magnitude, deltas[holeCorner].magnitude * 0.5f, "원래 경계는 당기지 않고 이웃 나누기만큼만 조금 따라온다");
        }

        [Test]
        public void 위치를_구할_수_없는_정점이_섞여도_먼_정점은_움직이지_않는다()
        {
            var (positions, before, after) = Grid();
            Vector3[] keys = (Vector3[])positions.Clone();
            positions[15] = Vector3.positiveInfinity;
            positions[12] = Vector3.positiveInfinity;
            var tail = new List<(Vector3, Vector3)> { (new Vector3(0.15f, -0.1f, 0.15f), new Vector3(0.15f, 0.1f, 0.15f)) };

            Vector3[] deltas = HoleEdgeHugger.Pull(positions, keys, before, after, tail, margin: 0.01f, maxPull: 1f);

            Assert.IsTrue(deltas.All(d => !float.IsNaN(d.x) && !float.IsInfinity(d.x)));
            int holeCorner = 1 * 4 + 1;
            Assert.Greater(deltas[holeCorner].magnitude, 0f, "구멍 가장자리는 그대로 당긴다");
            Assert.Less(deltas[3].magnitude, deltas[holeCorner].magnitude * 0.5f, "위치를 모르는 정점끼리 한 묶음이 되어 먼 정점이 끌려가지 않는다");
        }

        [Test]
        public void 가장자리_고리를_다듬으면_꼬리에서_떨어진_거리가_고르게_된다()
        {
            // 구멍 가장자리 네 정점(5, 6, 9, 10)이 꼬리 축(0.15, *, 0.15)에서 같은 거리가 되도록 다듬어진다.
            var (positions, before, after) = Grid();
            var tail = new List<(Vector3, Vector3)> { (new Vector3(0.15f, -0.1f, 0.15f), new Vector3(0.15f, 0.1f, 0.15f)) };

            Vector3[] deltas = HoleEdgeHugger.Pull(positions, positions, before, after, tail, margin: 0.01f, maxPull: 1f);

            float Gap(int v) { Vector3 p = positions[v] + deltas[v]; return new Vector2(p.x - 0.15f, p.z - 0.15f).magnitude; }
            float[] gaps = { Gap(5), Gap(6), Gap(9), Gap(10) };
            Assert.Less(gaps.Max() - gaps.Min(), 0.005f, "고르게 꼬리를 감싼다");
        }

        [Test]
        public void 당길_거리가_한도보다_멀면_한도까지만_부위_쪽으로_당긴다()
        {
            var (positions, before, after) = Grid();
            var tail = new List<(Vector3, Vector3)> { (new Vector3(0.15f, -0.1f, 0.15f), new Vector3(0.15f, 0.1f, 0.15f)) };
            const float maxPull = 0.001f;

            Vector3[] deltas = HoleEdgeHugger.Pull(positions, positions, before, after, tail, margin: 0.01f, maxPull: maxPull);

            int holeCorner = 1 * 4 + 1;
            Assert.AreEqual(maxPull, deltas[holeCorner].magnitude, 1e-5f, "구멍 모서리는 한도만큼 움직인다(버리면 구멍이 부위보다 넓게 남음)");
            Vector3 towardTail = new Vector3(0.15f, 0f, 0.15f) - positions[holeCorner];
            Assert.Greater(Vector3.Dot(deltas[holeCorner], towardTail), 0f, "부위 쪽으로 움직인다");
            Assert.IsTrue(deltas.All(d => d.magnitude <= maxPull + 1e-6f), "어느 정점도 한도보다 멀리 움직이지 않는다");
        }
    }
}
