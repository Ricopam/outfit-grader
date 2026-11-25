using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 정점의 현재 월드 위치를 구하는 선형 블렌드 스키닝(Unity와 같은 방식).
    /// 몸 측정과 관통 보정이 같은 계산을 써야 옷과 몸의 위치가 같은 기준으로 비교된다.
    /// BakeMesh는 FBX 단위 스케일(예: Armature 100배)을 일관되게 반영하지 않아 본 행렬 * 바인드포즈로 직접 계산한다.
    /// </summary>
    public static class SkinningMath
    {
        const float MinValidWeight = 1e-4f;

        /// <summary>본마다 본 행렬 * 바인드포즈. 본이 없거나 바인드포즈가 모자라면 null 칸.</summary>
        public static Matrix4x4?[] SkinMatrices(Transform[] bones, Matrix4x4[] bindposes)
        {
            var result = new Matrix4x4?[bones.Length];
            for (int i = 0; i < bones.Length && i < bindposes.Length; i++)
            {
                if (bones[i] != null) result[i] = bones[i].localToWorldMatrix * bindposes[i];
            }
            return result;
        }

        /// <summary>
        /// 유효한 본의 가중치만 섞고 그 합으로 나눠 정규화한 스키닝 행렬.
        /// 삭제된 본에 걸린 가중치가 영행렬로 섞여 정점이 원점 쪽으로 끌리는 것을 막는다. 유효 가중치가 거의 없으면 false.
        /// </summary>
        public static bool TryBlend(Matrix4x4?[] skin, BoneWeight w, out Matrix4x4 blended)
        {
            // 본 하나에만 걸린 정점(대부분의 몸 정점)은 섞을 필요가 없다: 행렬 16칸 누적을 건너뛴다.
            bool isSingleBone = w.weight1 <= 0f && w.weight2 <= 0f && w.weight3 <= 0f && w.weight0 >= MinValidWeight && w.boneIndex0 >= 0 && w.boneIndex0 < skin.Length && skin[w.boneIndex0].HasValue;
            if (isSingleBone)
            {
                blended = skin[w.boneIndex0].Value;
                return Mathf.Abs(blended.determinant) > 1e-12f;
            }

            blended = Matrix4x4.zero;
            float total = 0f;
            total += Accumulate(skin, w.boneIndex0, w.weight0, ref blended);
            total += Accumulate(skin, w.boneIndex1, w.weight1, ref blended);
            total += Accumulate(skin, w.boneIndex2, w.weight2, ref blended);
            total += Accumulate(skin, w.boneIndex3, w.weight3, ref blended);
            if (total < MinValidWeight) return false;

            blended = Scaled(blended, 1f / total);
            return Mathf.Abs(blended.determinant) > 1e-12f;
        }

        static float Accumulate(Matrix4x4?[] skin, int index, float weight, ref Matrix4x4 sum)
        {
            bool isValid = weight > 0f && index >= 0 && index < skin.Length && skin[index].HasValue;
            if (!isValid) return 0f;

            Matrix4x4 m = Scaled(skin[index].Value, weight);
            // 정점마다 불리므로 인덱서(범위 검사 분기) 대신 필드를 직접 더한다. 마지막 행은 항상 (0,0,0,1) 비율이라 함께 누적.
            sum.m00 += m.m00; sum.m01 += m.m01; sum.m02 += m.m02; sum.m03 += m.m03;
            sum.m10 += m.m10; sum.m11 += m.m11; sum.m12 += m.m12; sum.m13 += m.m13;
            sum.m20 += m.m20; sum.m21 += m.m21; sum.m22 += m.m22; sum.m23 += m.m23;
            sum.m30 += m.m30; sum.m31 += m.m31; sum.m32 += m.m32; sum.m33 += m.m33;
            return weight;
        }

        static Matrix4x4 Scaled(Matrix4x4 m, float s)
        {
            m.m00 *= s; m.m01 *= s; m.m02 *= s; m.m03 *= s;
            m.m10 *= s; m.m11 *= s; m.m12 *= s; m.m13 *= s;
            m.m20 *= s; m.m21 *= s; m.m22 *= s; m.m23 *= s;
            m.m30 *= s; m.m31 *= s; m.m32 *= s; m.m33 *= s;
            return m;
        }
    }
}
