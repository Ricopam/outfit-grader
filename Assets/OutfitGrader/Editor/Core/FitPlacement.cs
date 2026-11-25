using UnityEngine;

namespace OutfitGrader
{
    /// <summary>본 하나의 월드 상태 (위치·회전·전체 크기)</summary>
    public readonly struct BoneState
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 LossyScale;

        public BoneState(Vector3 position, Quaternion rotation, Vector3 lossyScale)
        {
            Position = position;
            Rotation = rotation;
            LossyScale = lossyScale;
        }

        public static BoneState Of(Transform t) => new BoneState(t.position, t.rotation, t.lossyScale);
    }

    /// <summary>
    /// 의상 본이 원본 본에서 떨어져 있던 거리를 대상 본 기준으로 옮기고 크기 비율만큼 늘이거나 줄인다.
    /// 두 아바타 모두 T 자세라는 전제에서 회전은 루트 방향 차이만 보정한다.
    /// </summary>
    public static class FitPlacement
    {
        /// <param name="sourceAnchor">원본 기준 본 위치. 원본에 대응 본이 없으면 null(대상 본 위치에 그대로 둠).</param>
        /// <param name="rootDelta">대상 루트 회전 * 원본 루트 회전의 역</param>
        public static BoneState Place(BoneState outfit, Vector3? sourceAnchor, Vector3 targetAnchor, Quaternion rootDelta, float ratio)
        {
            Vector3 offset = sourceAnchor.HasValue ? outfit.Position - sourceAnchor.Value : Vector3.zero;
            Vector3 position = targetAnchor + rootDelta * (offset * ratio);
            Quaternion rotation = rootDelta * outfit.Rotation;
            return new BoneState(position, rotation, outfit.LossyScale * ratio);
        }

        /// <summary>
        /// 원본 구역 상자 안의 상대 위치(0~1)를 대상 구역 상자의 같은 상대 위치로 옮긴다.
        /// 예: 원본 머리 상자 앞면·눈높이의 안경 중심 -> 대상 머리 상자 앞면·눈높이.
        /// 머리 모양이 크게 다른 아바타(주둥이가 튀어나온 동물 등)에서 피벗 기준 확대보다 정확하다.
        /// </summary>
        public static Vector3 MapInBox(Vector3 point, Bounds source, Bounds target)
        {
            return new Vector3(
                MapAxis(point.x, source.min.x, source.size.x, target.min.x, target.size.x),
                MapAxis(point.y, source.min.y, source.size.y, target.min.y, target.size.y),
                MapAxis(point.z, source.min.z, source.size.z, target.min.z, target.size.z));
        }

        static float MapAxis(float value, float sourceMin, float sourceSize, float targetMin, float targetSize)
        {
            // 두께가 없는 축은 상대 위치를 알 수 없으므로 가운데로 둔다.
            float t = sourceSize > 1e-6f ? (value - sourceMin) / sourceSize : 0.5f;
            return targetMin + t * targetSize;
        }

        /// <summary>
        /// 앞(+Z)에서 봤을 때 (x, y) 근처 반경 안 정점 중 가장 앞쪽 깊이. 그 위치의 얼굴 표면.
        /// 반경 안에 정점이 없으면 null.
        /// </summary>
        public static float? FrontDepth(Vector3[] points, Vector2 xy, float radius)
        {
            float best = float.MinValue;
            float radiusSq = radius * radius;
            bool any = false;
            foreach (Vector3 p in points)
            {
                float dx = p.x - xy.x, dy = p.y - xy.y;
                if (dx * dx + dy * dy > radiusSq || p.z <= best) continue;
                best = p.z;
                any = true;
            }
            return any ? best : (float?)null;
        }

        /// <summary>
        /// 원본에서 표면과 떨어져 있던 깊이 간격을 대상 표면 기준으로 유지한다.
        /// 상자 비율만 쓰면 볼·주둥이처럼 굴곡이 다른 얼굴에서 소품이 파묻히거나 뜬다.
        /// </summary>
        public static float KeepSurfaceGap(float sourceDepth, float sourceSurface, float targetSurface, float ratio)
        {
            return targetSurface + (sourceDepth - sourceSurface) * ratio;
        }

        /// <summary>월드 좌표를 아바타 루트 기준 축(월드 단위, 루트 크기 무시)으로 바꾼다.</summary>
        public static Vector3 ToRootFrame(Transform root, Vector3 world)
        {
            return Quaternion.Inverse(root.rotation) * (world - root.position);
        }

        /// <summary>
        /// 루트 기준 축 변환을 미리 읽어 둔 것(ToRootFrame과 같은 식). 정점마다 root.rotation·position을 엔진에서 다시 읽지 않게
        /// 정점 수만큼 도는 반복에서 쓴다(몸 정점 수만 개, PRD AC8).
        /// </summary>
        public readonly struct RootFrame
        {
            readonly Quaternion inverse;
            readonly Vector3 origin;

            public RootFrame(Transform root)
            {
                inverse = Quaternion.Inverse(root.rotation);
                origin = root.position;
            }

            public Vector3 Of(Vector3 world) => inverse * (world - origin);
        }

        public static Vector3 FromRootFrame(Transform root, Vector3 local)
        {
            return root.position + root.rotation * local;
        }

        /// <summary>회전된 본의 로컬 축(0=x, 1=y, 2=z) 중 주어진 월드 방향과 가장 나란한 축</summary>
        public static int DominantAxis(Quaternion rotation, Vector3 worldDirection)
        {
            Vector3 local = Quaternion.Inverse(rotation) * worldDirection;
            float x = Mathf.Abs(local.x), y = Mathf.Abs(local.y), z = Mathf.Abs(local.z);
            if (x >= y && x >= z) return 0;
            return y >= z ? 1 : 2;
        }

        /// <summary>한 축은 길이 비율, 나머지 두 축은 굵기 비율을 곱한 크기</summary>
        public static Vector3 AxisScale(Vector3 baseScale, int lengthAxis, float lengthRatio, float thicknessRatio)
        {
            Vector3 scaled = baseScale * thicknessRatio;
            scaled[lengthAxis] = baseScale[lengthAxis] * lengthRatio;
            return scaled;
        }

        /// <summary>원하는 전체 크기를 부모 크기로 나눠 localScale을 구한다.</summary>
        public static Vector3 LocalScaleFor(Vector3 desiredLossy, Vector3 parentLossy)
        {
            return new Vector3(
                SafeDivide(desiredLossy.x, parentLossy.x),
                SafeDivide(desiredLossy.y, parentLossy.y),
                SafeDivide(desiredLossy.z, parentLossy.z));
        }

        static float SafeDivide(float value, float divisor)
        {
            return Mathf.Abs(divisor) < 1e-6f ? value : value / divisor;
        }
    }
}
