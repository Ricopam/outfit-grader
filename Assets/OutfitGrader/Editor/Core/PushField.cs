using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 겹침 정리에서 정점 묶음(같은 위치에 겹친 이음새 정점)마다 쌓이는 바깥 방향 밀기 거리.
    /// 뚫린 자리만 밀면 그 자리가 가시처럼 솟아(inuinu, 미는 거리 3배) 이웃 평균이 자기 값보다 크면 평균으로 올려
    /// 완만한 언덕으로 만든다. 값을 깎지는 않아 이미 몸 밖으로 나온 곳이 다시 들어가지 않는다.
    /// </summary>
    public sealed class PushField
    {
        /// <summary>한 번 더 밀 때마다 언덕을 고르는 횟수. 한 번에 한 고리씩 넓어진다.</summary>
        public const int SmoothPasses = 3;
        /// <summary>
        /// 마지막으로 반영한 뒤 거리 변화가 한 번 미는 거리의 이 비율보다 작으면 아직 움직이지 않은 것으로 본다.
        /// 평균으로 올리기는 매번 한 고리씩 아주 작은 값을 퍼뜨려, 모두 반영하면 다시 판정할 삼각형이 반복마다 늘어난다.
        /// </summary>
        public const float MinChangeToStep = 0.01f;

        readonly int[] triangles;
        readonly int[] groupOf;
        readonly List<int>[] neighbors;
        readonly Vector3[] direction;
        readonly float[] offset, applied;

        /// <param name="worldNormals">정점마다 월드 바깥 법선(구할 수 없으면 영벡터). 묶음 안에서 평균해 이음새가 갈라지지 않게 한다.</param>
        public PushField(Vector3[] vertices, int[] triangles, Vector3[] worldNormals)
        {
            this.triangles = triangles;
            groupOf = PenetrationFixer.WeldGroups(vertices, out int count);
            neighbors = PenetrationFixer.Neighbors(count, triangles.Select(t => groupOf[t]).ToArray());
            offset = new float[count];
            applied = new float[count];
            direction = GroupDirections(worldNormals, count);
        }

        /// <summary>
        /// 묶음마다 법선 평균. 양면 옷은 앞·뒷면 정점이 같은 위치에 반대 법선이라 그냥 더하면 0이 되므로,
        /// 묶음의 첫 법선과 반대인 법선은 뒤집어 더한다(이전처럼 묶음 전체가 한 방향으로 움직임).
        /// </summary>
        Vector3[] GroupDirections(Vector3[] worldNormals, int count)
        {
            var first = new Vector3[count];
            var sum = new Vector3[count];
            for (int i = 0; i < worldNormals.Length; i++)
            {
                int g = groupOf[i];
                if (first[g] == Vector3.zero) first[g] = worldNormals[i];
                sum[g] += Vector3.Dot(worldNormals[i], first[g]) < 0f ? -worldNormals[i] : worldNormals[i];
            }
            return sum.Select(s => s.normalized).ToArray();
        }

        /// <summary>뚫린 삼각형의 정점 묶음을 step만큼 더 밀고 언덕을 고른다. 이번에 반영할 만큼 거리가 바뀐 정점 표시를 돌려준다.</summary>
        public bool[] Push(bool[] isPierced, float step)
        {
            var raised = new HashSet<int>();
            for (int t = 0; t < isPierced.Length; t++)
            {
                if (!isPierced[t]) continue;
                for (int k = 0; k < 3; k++) raised.Add(groupOf[triangles[t * 3 + k]]);
            }
            foreach (int g in raised) offset[g] += step;
            for (int pass = 0; pass < SmoothPasses; pass++) Smooth();

            var isChanged = new bool[offset.Length];
            for (int g = 0; g < offset.Length; g++)
            {
                isChanged[g] = offset[g] - applied[g] >= step * MinChangeToStep;
                if (isChanged[g]) applied[g] = offset[g];
            }
            return groupOf.Select(g => isChanged[g]).ToArray();
        }

        /// <summary>정점의 월드 이동량(바깥 법선 x 반영된 거리)</summary>
        public Vector3 WorldDelta(int vertex) => direction[groupOf[vertex]] * applied[groupOf[vertex]];

        /// <summary>
        /// 이웃 평균이 자기 값보다 크면 평균으로 올린다. 거리가 0인 묶음은 이웃도 모두 0이면 바뀌지 않으므로, 거리가 있는 묶음과
        /// 그 이웃만 계산한다(옷 전체 약 9,500개 묶음을 매번 훑어 겹침 정리 반복이 길었음, PRD AC8). 결과는 전체를 훑은 것과 같다.
        /// </summary>
        void Smooth()
        {
            var candidates = new HashSet<int>();
            for (int g = 0; g < offset.Length; g++)
            {
                if (offset[g] <= 0f) continue;
                candidates.Add(g);
                foreach (int n in neighbors[g]) candidates.Add(n);
            }
            var next = new List<(int g, float value)>(candidates.Count);
            foreach (int g in candidates)
            {
                List<int> around = neighbors[g];
                if (around.Count == 0) continue;
                // Enumerable.Average와 같은 결과가 나오게 double로 더한다.
                double sum = 0d;
                for (int k = 0; k < around.Count; k++) sum += offset[around[k]];
                float average = (float)(sum / around.Count);
                if (average > offset[g]) next.Add((g, average));
            }
            foreach (var (g, value) in next) offset[g] = value;
        }
    }
}
