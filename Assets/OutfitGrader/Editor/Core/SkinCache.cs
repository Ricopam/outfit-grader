using System.Collections.Generic;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 대상 아바타 하나를 준비하는 동안 메시마다 정점 스키닝 행렬을 한 번만 계산해 나눠 쓴다.
    /// 몸 정점·몸 변·귀·꼬리 변·머리 변을 만들 때 같은 몸 메시를 다섯 번 스키닝해 준비가 약 280ms 걸렸다(PRD AC8).
    /// 본 자세가 바뀌면 다시 만들어야 하므로 준비 단계(BuildContext) 안에서만 쓰고 버린다.
    /// </summary>
    public sealed class SkinCache
    {
        readonly Dictionary<(Renderer, Mesh), Matrix4x4?[]> matrices = new Dictionary<(Renderer, Mesh), Matrix4x4?[]>();

        /// <summary>정점마다 메시 공간 -> 월드 행렬(PenetrationFixer.VertexMatrices와 같음). 같은 렌더러·메시는 한 번만 계산한다.</summary>
        public Matrix4x4?[] Matrices(Renderer renderer, Mesh mesh)
        {
            var key = (renderer, mesh);
            if (!matrices.TryGetValue(key, out Matrix4x4?[] result)) matrices[key] = result = PenetrationFixer.VertexMatrices(renderer, mesh);
            return result;
        }

        /// <summary>캐시가 없으면(null) 매번 계산한다.</summary>
        public static Matrix4x4?[] Of(SkinCache cache, Renderer renderer, Mesh mesh) =>
            cache != null ? cache.Matrices(renderer, mesh) : PenetrationFixer.VertexMatrices(renderer, mesh);
    }
}
