using System.Collections.Generic;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 대상 몸 표면(정점 위치 + 바깥 법선)을 격자로 나눠, 의상 정점 근처의 가장 가까운 몸 정점을 빠르게 찾는다.
    /// 정점 수만 개끼리 전부 비교하면 수억 번이 되므로 주변 격자 칸(3x3x3)만 본다.
    /// 좌표는 아바타 루트 기준 축(월드 단위).
    /// </summary>
    public sealed class BodySurface
    {
        readonly float cellSize;
        readonly Dictionary<Vector3Int, List<int>> cells = new Dictionary<Vector3Int, List<int>>();
        readonly List<(Vector3 point, Vector3 normal)> surface;

        public BodySurface(List<(Vector3 point, Vector3 normal)> surface, float cellSize)
        {
            this.surface = surface;
            this.cellSize = Mathf.Max(cellSize, 1e-4f);
            for (int i = 0; i < surface.Count; i++)
            {
                Vector3Int key = CellOf(surface[i].point);
                if (!cells.TryGetValue(key, out List<int> list)) cells[key] = list = new List<int>();
                list.Add(i);
            }
        }

        public int Count => surface.Count;

        Vector3Int CellOf(Vector3 p)
        {
            return new Vector3Int(Mathf.FloorToInt(p.x / cellSize), Mathf.FloorToInt(p.y / cellSize), Mathf.FloorToInt(p.z / cellSize));
        }

        /// <summary>주변 격자 칸에서 가장 가까운 몸 정점. 근처에 몸이 없으면(소매 끝·치마 밑단 등) false.</summary>
        public bool TryNearest(Vector3 p, out Vector3 point, out Vector3 normal)
        {
            point = normal = Vector3.zero;
            float best = float.MaxValue;
            Vector3Int center = CellOf(p);
            for (int x = -1; x <= 1; x++)
            for (int y = -1; y <= 1; y++)
            for (int z = -1; z <= 1; z++)
            {
                if (!cells.TryGetValue(center + new Vector3Int(x, y, z), out List<int> list)) continue;
                foreach (int i in list)
                {
                    float d = (surface[i].point - p).sqrMagnitude;
                    if (d >= best) continue;
                    best = d;
                    point = surface[i].point;
                    normal = surface[i].normal;
                }
            }
            return best < float.MaxValue;
        }

        /// <summary>
        /// 몸 표면 평면(가장 가까운 몸 정점과 그 법선) 기준으로 의상 정점이 여유 간격보다 안쪽이면 바깥으로 민 위치.
        /// 밀 필요가 없거나, 밀어야 할 거리가 최대치를 넘으면(법선이 엉뚱한 곳을 가리키는 경우) 원래 위치.
        /// </summary>
        public static Vector3 PushOut(Vector3 vertex, Vector3 surfacePoint, Vector3 surfaceNormal, float margin, float maxPush)
        {
            float signedDistance = Vector3.Dot(vertex - surfacePoint, surfaceNormal);
            float push = margin - signedDistance;
            bool isInside = push > 0f;
            return isInside && push <= maxPush ? vertex + surfaceNormal * push : vertex;
        }
    }
}
