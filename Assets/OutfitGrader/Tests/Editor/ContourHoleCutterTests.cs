using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace OutfitGrader.Tests
{
    /// <summary>꼬리 둘레 경계선 자르기: 구멍 테두리가 옷 격자가 아니라 꼬리 둘레를 따르고, 뚫리지 않은 곳·지나치게 넓은 구멍은 자르지 않는다</summary>
    public class ContourHoleCutterTests
    {
        const float Margin = 0.01f;
        static readonly Vector3 Axis = new Vector3(0.013f, 0f, 0.007f);

        /// <summary>y=0 평면 위 정사각 격자 옷(칸 간격 step, 가로세로 cells칸). 왼쪽 반은 본 0, 오른쪽 반은 본 1에 묶고 UV는 위치를 따른다.</summary>
        static Mesh Cloth(int cells, float step)
        {
            var vertices = new List<Vector3>();
            var weights = new List<BoneWeight>();
            var uv = new List<Vector2>();
            float half = cells * step * 0.5f;
            for (int z = 0; z <= cells; z++)
                for (int x = 0; x <= cells; x++)
                {
                    var p = new Vector3(x * step - half, 0f, z * step - half);
                    vertices.Add(p);
                    uv.Add(new Vector2(p.x, p.z));
                    weights.Add(new BoneWeight { boneIndex0 = p.x < 0f ? 0 : 1, weight0 = 1f });
                }
            var triangles = new List<int>();
            for (int z = 0; z < cells; z++)
                for (int x = 0; x < cells; x++)
                {
                    int a = z * (cells + 1) + x, b = a + 1, c = a + cells + 1, d = c + 1;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            var mesh = new Mesh();
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.boneWeights = weights.ToArray();
            return mesh;
        }

        /// <summary>Axis를 중심으로 세운 닫힌 12각 기둥(반지름 radius, 높이 bottom~top)의 변. 삼각형마다 (a,b),(b,c),(c,a) 순서.</summary>
        static List<(Vector3 a, Vector3 b)> Prism(float radius, float bottom, float top)
        {
            const int Sides = 12;
            Vector3 Ring(int i, float y)
            {
                float angle = i * Mathf.PI * 2f / Sides;
                return Axis + new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
            }
            var triangles = new List<(Vector3, Vector3, Vector3)>();
            Vector3 low = Axis + Vector3.up * bottom, high = Axis + Vector3.up * top;
            for (int i = 0; i < Sides; i++)
            {
                Vector3 a = Ring(i, bottom), b = Ring(i + 1, bottom), c = Ring(i, top), d = Ring(i + 1, top);
                triangles.Add((a, c, b));
                triangles.Add((b, c, d));
                triangles.Add((low, a, b));
                triangles.Add((high, d, c));
            }
            return triangles.SelectMany(t => new[] { (t.Item1, t.Item2), (t.Item2, t.Item3), (t.Item3, t.Item1) }).ToList();
        }

        static float Radial(Vector3 p) => new Vector2(p.x - Axis.x, p.z - Axis.z).magnitude;

        static Mesh Cut(Mesh cloth, List<(Vector3 a, Vector3 b)> tail, out int changed)
        {
            Vector3[] positions = cloth.vertices;
            return ContourHoleCutter.Cut(cloth, positions, tail, new EdgeGrid(tail), Margin, "cut", out changed);
        }

        [Test]
        public void 구멍_테두리가_꼬리_둘레를_따라_생긴다()
        {
            Mesh cloth = Cloth(cells: 8, step: 0.05f);
            const float radius = 0.06f;

            Mesh cut = Cut(cloth, Prism(radius, -0.2f, 0.2f), out int changed);

            Assert.IsNotNull(cut);
            Assert.Greater(changed, 0);
            Vector3[] v = cut.vertices;
            int[] tris = cut.triangles;
            // 12각 기둥의 면은 원보다 cos 15도만큼 안쪽이라 테두리 반지름은 0.068~0.07 사이에 든다.
            Assert.IsTrue(tris.All(i => Radial(v[i]) > radius * Mathf.Cos(Mathf.PI / 12f) + Margin - 1e-3f), "꼬리와 여유 간격 안쪽에 남은 옷이 없다");
            var added = Enumerable.Range(cloth.vertexCount, v.Length - cloth.vertexCount).Where(i => tris.Contains(i)).ToList();
            Assert.Greater(added.Count, 6, "경계선 위에 새 정점이 생긴다");
            Assert.IsTrue(added.All(i => Radial(v[i]) < radius + Margin + 1e-3f), "새 정점은 꼬리 둘레 경계선 위에 있다");
        }

        [Test]
        public void 새_정점의_본_가중치와_UV를_보간한다()
        {
            Mesh cloth = Cloth(cells: 8, step: 0.05f);

            Mesh cut = Cut(cloth, Prism(0.06f, -0.2f, 0.2f), out _);

            Assert.AreEqual(cut.vertexCount, cut.boneWeights.Length);
            var uv = new List<Vector2>();
            cut.GetUVs(0, uv);
            Assert.AreEqual(cut.vertexCount, uv.Count);
            Vector3[] v = cut.vertices;
            for (int i = cloth.vertexCount; i < cut.vertexCount; i++)
            {
                BoneWeight w = cut.boneWeights[i];
                Assert.AreEqual(1f, w.weight0 + w.weight1 + w.weight2 + w.weight3, 1e-4f, "가중치 합은 1");
                Assert.AreEqual(v[i].x, uv[i].x, 1e-4f, "UV는 위치와 같은 비율로 보간");
                Assert.AreEqual(v[i].z, uv[i].y, 1e-4f);
            }
        }

        [Test]
        public void UV_이음선_양쪽의_경계점이_같은_위치에_생긴다()
        {
            // x=0 세로줄 정점을 오른쪽 삼각형용으로 한 벌 더 만들어(같은 위치, 다른 번호) 구멍을 가로지르는 이음선을 만든다.
            Mesh cloth = Cloth(cells: 8, step: 0.05f);
            var vertices = cloth.vertices.ToList();
            var weights = cloth.boneWeights.ToList();
            var uv = new List<Vector2>();
            cloth.GetUVs(0, uv);
            var copyOf = new Dictionary<int, int>();
            for (int i = 0; i < cloth.vertexCount; i++)
            {
                if (Mathf.Abs(vertices[i].x) > 1e-6f) continue;
                copyOf[i] = vertices.Count;
                vertices.Add(vertices[i]);
                weights.Add(weights[i]);
                uv.Add(uv[i] + Vector2.right);
            }
            int[] tris = cloth.triangles;
            for (int t = 0; t < tris.Length; t += 3)
            {
                bool isRight = Enumerable.Range(0, 3).Average(k => cloth.vertices[tris[t + k]].x) > 0f;
                if (!isRight) continue;
                for (int k = 0; k < 3; k++) if (copyOf.TryGetValue(tris[t + k], out int copy)) tris[t + k] = copy;
            }
            var seamed = new Mesh();
            seamed.SetVertices(vertices);
            seamed.SetUVs(0, uv);
            seamed.SetTriangles(tris, 0);
            seamed.boneWeights = weights.ToArray();

            Mesh cut = Cut(seamed, Prism(0.06f, -0.2f, 0.2f), out _);

            Assert.IsNotNull(cut);
            Vector3[] v = cut.vertices;
            var onSeam = Enumerable.Range(seamed.vertexCount, cut.vertexCount - seamed.vertexCount).Where(i => Mathf.Abs(v[i].x) < 1e-4f).ToList();
            Assert.GreaterOrEqual(onSeam.Count, 2, "경계선이 이음선을 지나며 양쪽에 새 정점이 생긴다");
            foreach (int i in onSeam)
                Assert.IsTrue(onSeam.Any(j => j != i && (v[j] - v[i]).sqrMagnitude < 1e-12f), "이음선 반대쪽에 같은 위치의 새 정점이 있다(틈 없음)");
        }

        [Test]
        public void 꼬리가_뚫지_않고_옷_위에_얹혀만_있으면_자르지_않는다()
        {
            Mesh cloth = Cloth(cells: 8, step: 0.05f);

            // 밑면이 옷에서 여유 간격보다 가까이 떠 있는 꼬리: 옷 정점 일부가 경계선 안에 들지만 꼬리 변은 옷을 지나지 않는다.
            Mesh cut = Cut(cloth, Prism(0.06f, 0.005f, 0.2f), out int changed);

            Assert.IsNull(cut);
            Assert.AreEqual(0, changed);
        }

        [Test]
        public void 구멍이_기존보다_두_배_넘게_넓어지면_쓰지_않는다()
        {
            // inuinu처럼 꼬리 몸통이 옷 바로 아래(여유 간격 안)에 넓게 깔리고, 끝만 성긴 옷(칸 0.2)을 작게 뚫고 나온다.
            Mesh cloth = Cloth(cells: 6, step: 0.2f);
            List<(Vector3 a, Vector3 b)> tail = Prism(0.6f, -0.1f, -0.005f);
            Vector3 spikeA = new Vector3(0.05f, -0.1f, 0.03f), spikeB = new Vector3(0.05f, 0.1f, 0.03f), spikeC = new Vector3(0.07f, 0.1f, 0.03f);
            tail.AddRange(new[] { (spikeA, spikeB), (spikeB, spikeC), (spikeC, spikeA) });

            Mesh cut = Cut(cloth, tail, out _);

            Assert.IsNull(cut, "뚫린 삼각형 이웃이 경계선 안에 넓게 들어 구멍이 크게 벌어지므로 호출부가 기존 방식을 쓴다");
        }
    }
}
