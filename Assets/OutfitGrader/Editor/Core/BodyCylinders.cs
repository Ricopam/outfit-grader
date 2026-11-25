using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>상의를 표면 간격 보존으로 다시 배치하는 몸 부위. 머리·손·다리 아래쪽은 범위 밖(None).</summary>
    public enum BodyRegion
    {
        None,
        Torso,
        LeftUpperArm,
        LeftLowerArm,
        RightUpperArm,
        RightLowerArm,
        LeftUpperLeg,
        RightUpperLeg,
    }

    /// <summary>
    /// 아바타 하나의 부위별 원통(축 + 바깥 반지름 표). 좌표는 아바타 루트 기준 축(월드 단위).
    /// 둘레 각도의 0 방향은 루트 앞쪽(축과 거의 나란하면 루트 위쪽)이라 T 자세 두 아바타에서 같은 쪽을 가리킨다.
    /// 허벅지는 다리 쪽 본에 묶인 옷을 옮기며, 허벅지 위쪽은 몸통 규칙과 이어서 섞는다(TryMap 참고).
    /// </summary>
    public sealed class BodyCylinders
    {
        readonly struct Frame
        {
            public readonly Vector3 Origin, Axis, Forward, Side;
            public readonly float Length;

            /// <summary>축이 루트 앞쪽과 이보다 나란하면(코사인) 둘레 0도 기준을 루트 위쪽으로 바꾼다.</summary>
            const float ParallelCosine = 0.9f;

            public Frame(Vector3 start, Vector3 end)
            {
                Origin = start;
                Length = Vector3.Distance(start, end);
                Axis = (end - start).normalized;
                Vector3 reference = Mathf.Abs(Vector3.Dot(Axis, Vector3.forward)) < ParallelCosine ? Vector3.forward : Vector3.up;
                Forward = Vector3.ProjectOnPlane(reference, Axis).normalized;
                Side = Vector3.Cross(Axis, Forward);
            }
        }

        /// <summary>부위마다 축 시작·끝 Humanoid 본</summary>
        static readonly Dictionary<BodyRegion, (HumanBodyBones start, HumanBodyBones end)> Axes = new Dictionary<BodyRegion, (HumanBodyBones, HumanBodyBones)>
        {
            { BodyRegion.Torso, (HumanBodyBones.Hips, HumanBodyBones.Neck) },
            { BodyRegion.LeftUpperArm, (HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm) },
            { BodyRegion.LeftLowerArm, (HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand) },
            { BodyRegion.RightUpperArm, (HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm) },
            { BodyRegion.RightLowerArm, (HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand) },
            { BodyRegion.LeftUpperLeg, (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg) },
            { BodyRegion.RightUpperLeg, (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg) },
        };

        /// <summary>원통 반지름이 이보다 작으면 그 위치에 몸이 없는 것으로 보고 다시 배치하지 않는다(월드 단위).</summary>
        const float MinSurfaceRadius = 1e-3f;
        /// <summary>축 본 사이가 이보다 가까우면 축 방향을 믿을 수 없어 그 부위를 만들지 않는다(월드 단위).</summary>
        const float MinAxisLength = 1e-3f;

        readonly Dictionary<BodyRegion, (Frame frame, CylinderTable table)> regions = new Dictionary<BodyRegion, (Frame, CylinderTable)>();
        /// <summary>
        /// 부위를 늘일 때 고정 반대쪽 끝이 몸을 넘어 뻗을 수 있는 최대 거리(루트 기준 월드 단위). 옷이 팔보다 지나치게 길어
        /// 날개처럼 뻗거나(형태 검사가 소매 상한을 낮춘 inuinu 실험) 아랫단이 다리를 덮는 것을 막는다(PRD 0장 형태 보존).
        /// </summary>
        readonly Dictionary<BodyRegion, float> reach = new Dictionary<BodyRegion, float>();
        /// <summary>아랫단이 엉덩이 아래로 내려갈 수 있는 거리: 허벅지 길이 대비 비율(허벅지 중간까지)</summary>
        public const float HemReachToThigh = 0.5f;
        float thighLength;
        /// <summary>소매 끝이 손목 너머로 나갈 수 있는 거리: 손 길이 대비 비율. 위팔·아래팔이 반씩 나눠 쓴다.</summary>
        public const float CuffReachToHand = 1f;
        /// <summary>손가락 본이 없을 때 손 길이를 아래팔 길이의 이 비율로 본다.</summary>
        public const float HandToForearmFallback = 0.4f;
        /// <summary>위팔·아래팔이 소매 늘임 한도를 나눠 쓰는 비율(반씩)</summary>
        public const float ArmReachShare = 0.5f;
        /// <summary>손목 -> 가운데 손가락 첫 마디 거리에 곱하면 손 길이</summary>
        const float FingerBaseToHandLength = 2f;

        /// <summary>Humanoid 본이 속한 부위. 몸통은 Hips·Spine·Chest·UpperChest·어깨.</summary>
        public static BodyRegion RegionOf(HumanBodyBones bone)
        {
            switch (bone)
            {
                case HumanBodyBones.Hips:
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.RightShoulder: return BodyRegion.Torso;
                case HumanBodyBones.LeftUpperArm: return BodyRegion.LeftUpperArm;
                case HumanBodyBones.LeftLowerArm: return BodyRegion.LeftLowerArm;
                case HumanBodyBones.RightUpperArm: return BodyRegion.RightUpperArm;
                case HumanBodyBones.RightLowerArm: return BodyRegion.RightLowerArm;
                case HumanBodyBones.LeftUpperLeg: return BodyRegion.LeftUpperLeg;
                case HumanBodyBones.RightUpperLeg: return BodyRegion.RightUpperLeg;
                default: return BodyRegion.None;
            }
        }

        /// <summary>
        /// 본이 속한 부위: 본 자신이나 가장 가까운 Humanoid 조상의 부위. 가슴 흔들림 본처럼 Humanoid 아래에 달린 몸 본도 부위에 넣어
        /// 가슴 크기 같은 체형 차이를 반영한다. 귀·꼬리·털 이름 사슬은 따로 처리하므로(구멍·숨김) 몸으로 치지 않는다.
        /// </summary>
        public static BodyRegion RegionOfBone(AvatarRig rig, Transform bone)
        {
            Transform human = BodyAncestor(rig, bone);
            return human != null && rig.TryGetHuman(human, out HumanBodyBones h) ? RegionOf(h) : BodyRegion.None;
        }

        /// <summary>본 자신이나 가장 가까운 Humanoid 조상. 그 사이에 귀·꼬리·털 이름 본이 있으면 null.</summary>
        public static Transform BodyAncestor(AvatarRig rig, Transform bone)
        {
            for (Transform t = bone; t != null; t = t.parent)
            {
                if (rig.TryGetHuman(t, out _)) return t;
                bool isAppendage = AppendageHoleCutter.EarBoneName.IsMatch(t.name) || AppendageHoleCutter.TailBoneName.IsMatch(t.name)
                    || FurHider.FurBoneName.IsMatch(t.name);
                if (isAppendage) return null;
            }
            return null;
        }

        /// <summary>몸 정점 묶음에서 부위별 원통을 만든다. 축 본이 없거나 정점이 없는 부위는 빠진다.</summary>
        public static BodyCylinders Build(BodyPointCloud cloud)
        {
            AvatarRig rig = cloud.Rig;
            var body = new BodyCylinders();
            var frames = new Dictionary<BodyRegion, Frame>();
            foreach (var pair in Axes)
            {
                Transform start = rig.GetHuman(pair.Value.start), end = rig.GetHuman(pair.Value.end);
                if (start == null || end == null) continue;
                var frame = new Frame(FitPlacement.ToRootFrame(rig.Root, start.position), FitPlacement.ToRootFrame(rig.Root, end.position));
                if (frame.Length > MinAxisLength) frames[pair.Key] = frame;
            }
            foreach (var group in cloud.HumanPoints.GroupBy(p => RegionOf(rig, p.human)).Where(g => frames.ContainsKey(g.Key)))
            {
                Frame frame = frames[group.Key];
                CylinderTable table = CylinderTable.Build(group.Select(p => ToCylinder(frame, p.point)), frame.Length);
                if (table.HasData) body.regions[group.Key] = (frame, table);
            }
            body.MeasureReach(rig);
            body.MeasureHands(cloud);
            return body;
        }

        /// <summary>손 크기 대표값: 손·손가락 몸 정점이 아래팔 축에서 떨어진 거리의 이 분위수(굵은 앞발까지 덮도록 큰 쪽).</summary>
        public const float HandRadiusQuantile = 0.9f;
        /// <summary>손 정점이 이보다 적으면 손 크기를 재지 않는다(손을 지운 몸 메시 등에서 반지름이 작게 잡혀 배율이 폭주하지 않게).</summary>
        public const int MinHandPoints = 8;
        readonly Dictionary<bool, float> handRadius = new Dictionary<bool, float>();

        /// <summary>왼손·오른손마다 손 반지름(아래팔 축에서 손·손가락 몸 정점까지 거리의 HandRadiusQuantile 분위수)을 잰다.</summary>
        void MeasureHands(BodyPointCloud cloud)
        {
            foreach (bool isLeft in new[] { true, false })
            {
                Transform hand = cloud.Rig.GetHuman(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                if (hand == null || !regions.TryGetValue(isLeft ? BodyRegion.LeftLowerArm : BodyRegion.RightLowerArm, out var forearm)) continue;
                float[] distances = cloud.HumanPoints.Where(p => p.human.IsChildOf(hand))
                    .Select(p => ToCylinder(forearm.frame, p.point).radius).OrderBy(r => r).ToArray();
                if (distances.Length >= MinHandPoints) handRadius[isLeft] = distances[(int)((distances.Length - 1) * HandRadiusQuantile)];
            }
        }

        /// <summary>대상 손 반지름 / 원본 손 반지름. 어느 한쪽을 잴 수 없으면 0.</summary>
        public static float HandScale(BodyCylinders source, BodyCylinders target, bool isLeft)
        {
            if (!source.handRadius.TryGetValue(isLeft, out float from) || !target.handRadius.TryGetValue(isLeft, out float to)) return 0f;
            return from > 0f ? to / from : 0f;
        }

        /// <summary>
        /// 아랫단 늘임 한도를 허벅지 길이 대비 비율로 바꾼다(0이면 엉덩이까지). 풀어 주기 옵션은 날카로운 접힘을 펴 주므로
        /// 옷을 덜 늘여도 톱니가 서지 않아 짧은 후드티 모양을 쓴다(데모: inuinu 뒤 아랫단 톱니가 사라짐). 허벅지를 잴 수 없으면 그대로.
        /// </summary>
        /// <returns>허벅지를 재서 한도를 바꿨으면 true</returns>
        public bool SetHemReach(float thighShare)
        {
            if (thighLength <= 0f) return false;
            reach[BodyRegion.Torso] = thighLength * thighShare;
            return true;
        }

        /// <summary>부위 축 길이(월드 단위). 부위가 없으면 0.</summary>
        public float Length(BodyRegion region) => regions.TryGetValue(region, out var r) ? r.frame.Length : 0f;

        /// <summary>부위 축 방향(루트 기준 단위 벡터). 부위가 없으면 false.</summary>
        public bool TryAxis(BodyRegion region, out Vector3 axis)
        {
            axis = regions.TryGetValue(region, out var r) ? r.frame.Axis : Vector3.zero;
            return regions.ContainsKey(region);
        }

        /// <summary>
        /// 부위별 늘임 한도: 몸통은 허벅지 절반, 위팔·아래팔은 손 길이의 반씩.
        /// 기준 본 길이를 잴 수 없으면 한도를 두지 않는다(상한만 적용, 0으로 두면 늘임 자체가 막힘).
        /// </summary>
        void MeasureReach(AvatarRig rig)
        {
            float thigh = Mathf.Max(BoneLength(rig, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg),
                BoneLength(rig, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg));
            thighLength = thigh;
            if (thigh > 0f) reach[BodyRegion.Torso] = thigh * HemReachToThigh;
            foreach (bool isLeft in new[] { true, false })
            {
                float hand = HandLength(rig, isLeft) * CuffReachToHand * ArmReachShare;
                if (hand <= 0f) continue;
                reach[isLeft ? BodyRegion.LeftUpperArm : BodyRegion.RightUpperArm] = hand;
                reach[isLeft ? BodyRegion.LeftLowerArm : BodyRegion.RightLowerArm] = hand;
            }
        }

        /// <summary>손 길이: 손목 -> 가운데 손가락 첫 마디 거리의 2배. 손가락 본이 없으면 아래팔 길이 비율.</summary>
        static float HandLength(AvatarRig rig, bool isLeft)
        {
            HumanBodyBones hand = isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand;
            float toFinger = BoneLength(rig, hand, isLeft ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            if (toFinger > 0f) return toFinger * FingerBaseToHandLength;
            return BoneLength(rig, isLeft ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, hand) * HandToForearmFallback;
        }

        static float BoneLength(AvatarRig rig, HumanBodyBones a, HumanBodyBones b)
        {
            Transform ta = rig.GetHuman(a), tb = rig.GetHuman(b);
            if (ta == null || tb == null) return 0f;
            return Vector3.Distance(FitPlacement.ToRootFrame(rig.Root, ta.position), FitPlacement.ToRootFrame(rig.Root, tb.position));
        }

        /// <summary>부위를 늘일 수 있는 최대 배율(1 이상): 늘어난 길이가 한도를 넘지 않게.</summary>
        float MaxStretch(BodyRegion region, float length) => reach.TryGetValue(region, out float r) && length > 0f ? 1f + r / length : float.MaxValue;

        /// <summary>
        /// 상한 cap에서 부위가 실제로 늘어나는 배율(1 이상): 눌림 상한에 따른 늘임을 몸 기준 한도로 자른 값.
        /// 형태 검사가 상한을 낮춰도 이 값이 그대로면(한도에 막힘, 눌리지 않음) 결과가 바뀌지 않는다.
        /// </summary>
        public static float RegionStretch(BodyCylinders source, BodyCylinders target, BodyRegion region, float cap)
        {
            if (!source.regions.TryGetValue(region, out var from) || !target.regions.TryGetValue(region, out var to)) return 1f;
            return Mathf.Min(Stretch(from, to, cap), target.MaxStretch(region, to.frame.Length));
        }

        /// <summary>
        /// 부위의 길이 배율(늘임 포함)과 굵기 배율의 기하평균. 끈처럼 매달린 부품을 한쪽으로 눌리지 않게 균등하게 줄이거나 키운다.
        /// 부위를 잴 수 없으면 1.
        /// </summary>
        public static float UniformScale(BodyCylinders source, BodyCylinders target, BodyRegion region, float cap)
        {
            return TryAxisScales(source, target, region, cap, out float length, out float radial) ? Mathf.Sqrt(length * radial) : 1f;
        }

        /// <summary>부위의 축 방향 배율(길이 배율 x 늘임)과 둘레 방향 배율(굵기 배율). 부위를 잴 수 없으면 false(둘 다 1).</summary>
        public static bool TryAxisScales(BodyCylinders source, BodyCylinders target, BodyRegion region, float cap, out float length, out float radial)
        {
            length = radial = 1f;
            if (!source.regions.TryGetValue(region, out var from) || !target.regions.TryGetValue(region, out var to)) return false;
            float sourceRadius = from.table.MedianRadius();
            if (sourceRadius <= 0f || from.frame.Length <= 0f) return false;
            length = to.frame.Length / from.frame.Length * RegionStretch(source, target, region, cap);
            radial = to.table.MedianRadius() / sourceRadius;
            return true;
        }

        public bool Has(BodyRegion region) => regions.ContainsKey(region);

        /// <summary>부위 원통 반지름 표의 중앙값(월드 단위). 부위가 없으면 0.</summary>
        public float MedianRadius(BodyRegion region) => regions.TryGetValue(region, out var r) ? r.table.MedianRadius() : 0f;

        /// <summary>
        /// 점(루트 기준)이 부위 몸 표면에서 바깥으로 떨어진 거리(축 거리 - 표면 반지름, 안쪽이면 음수).
        /// 부위가 없거나 점이 마디 축 범위(t 0~1) 밖이면 false(끝 단면을 무한히 늘인 원통으로 판정하지 않음).
        /// </summary>
        public bool TryClearance(BodyRegion region, Vector3 rootPoint, out float clearance)
        {
            clearance = 0f;
            if (!regions.TryGetValue(region, out var r)) return false;
            var (t, angle, rho) = ToCylinder(r.frame, rootPoint);
            bool isWithinSegment = t >= 0f && t <= 1f;
            if (!isWithinSegment) return false;
            clearance = rho - r.table.Sample(t, angle);
            return true;
        }

        /// <summary>점(루트 기준)에서 부위 축에 수직인 바깥 방향(루트 기준 단위 벡터). 부위가 없거나 축 위의 점이면 false.</summary>
        public bool TryOutward(BodyRegion region, Vector3 rootPoint, out Vector3 outward)
        {
            outward = Vector3.zero;
            if (!regions.TryGetValue(region, out var r)) return false;
            Vector3 relative = rootPoint - r.frame.Origin;
            Vector3 radial = relative - r.frame.Axis * Vector3.Dot(relative, r.frame.Axis);
            if (radial.sqrMagnitude < MinSurfaceRadius * MinSurfaceRadius) return false;
            outward = radial.normalized;
            return true;
        }

        /// <summary>
        /// 원본 원통 좌표의 점을 대상 원통의 같은 위치로 옮긴다(축 방향 위치는 길이 비율, 축과의 거리는 MapRadius).
        /// 두 원통 중 하나에 부위가 없거나 그 위치에 몸이 없으면 false.
        /// </summary>
        /// <param name="style">맞춤 방식. 부위별 한쪽 눌림 상한(FitStyles.MaxSquash)을 넘으면 옷 길이를 남긴다.</param>
        public static bool TryMap(BodyCylinders source, BodyCylinders target, BodyRegion region, Vector3 sourcePoint, float margin, FitStyle style,
            out Vector3 targetPoint)
        {
            return TryMap(source, target, region, sourcePoint, margin, SquashCaps.For(style), out targetPoint);
        }

        /// <param name="caps">부위별 한쪽 눌림 상한. 넘으면 옷 길이를 남긴다(형태 검사로 낮춘 상한 포함).</param>
        /// <remarks>
        /// 허벅지 가중치 점은 원본 허벅지 위쪽(HipRuleUntil 깊이까지)이면 몸통 규칙, 아래쪽(LegRuleFrom부터)이면 허벅지 규칙으로 옮기고
        /// 그 사이는 깊이에 따라 부드럽게 섞는다. 같은 원본 점을 두 규칙으로 옮긴 위치는 몸통 반지름의 0.3~0.5배(중앙값, inuinu·Lumelana)씩 벌어져,
        /// Hips·허벅지 가중치가 섞인 상의 아랫단이 정점마다 두 위치를 오가며 톱니처럼 들쭉날쭉했다(inuinu 뒤 아랫단).
        /// 위치로만 정하므로 이웃 정점이 같은 비율을 받고, 바지 다리처럼 허벅지 깊은 곳은 허벅지를 그대로 따른다.
        /// </remarks>
        public static bool TryMap(BodyCylinders source, BodyCylinders target, BodyRegion region, Vector3 sourcePoint, float margin, SquashCaps caps,
            out Vector3 targetPoint)
        {
            bool canFollowHips = IsLeg(region) && source.Has(BodyRegion.Torso) && target.Has(BodyRegion.Torso) && source.Has(region);
            if (!canFollowHips) return TryMapInRegion(source, target, region, sourcePoint, margin, caps, out targetPoint);
            float legShare = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(HipRuleUntil, LegRuleFrom, source.Depth(region, sourcePoint)));
            bool hasHips = TryMapInRegion(source, target, BodyRegion.Torso, sourcePoint, margin, caps, out Vector3 byHips);
            bool hasLeg = TryMapInRegion(source, target, region, sourcePoint, margin, caps, out Vector3 byLeg);
            targetPoint = hasHips && hasLeg ? Vector3.Lerp(byHips, byLeg, legShare) : hasHips ? byHips : byLeg;
            return hasHips || hasLeg;
        }

        /// <summary>허벅지 축 깊이(엉덩이 0 ~ 무릎 1)가 이 값까지는 몸통 규칙만 쓴다.</summary>
        public const float HipRuleUntil = 0.3f;
        /// <summary>허벅지 축 깊이가 이 값부터는 허벅지 규칙만 쓴다(바지 다리 등).</summary>
        public const float LegRuleFrom = 0.7f;

        /// <summary>점(루트 기준)의 부위 축 방향 위치(0 = 축 시작, 1 = 끝). 부위가 없으면 0.</summary>
        public float Depth(BodyRegion region, Vector3 rootPoint) => regions.TryGetValue(region, out var r) ? ToCylinder(r.frame, rootPoint).t : 0f;

        static bool IsLeg(BodyRegion region) => region == BodyRegion.LeftUpperLeg || region == BodyRegion.RightUpperLeg;

        static bool TryMapInRegion(BodyCylinders source, BodyCylinders target, BodyRegion region, Vector3 sourcePoint, float margin, SquashCaps caps,
            out Vector3 targetPoint)
        {
            targetPoint = sourcePoint;
            if (!source.regions.TryGetValue(region, out var from) || !target.regions.TryGetValue(region, out var to)) return false;

            var (t, angle, rho) = ToCylinder(from.frame, sourcePoint);
            float targetT = StretchedT(region, t, RegionStretch(source, target, region, caps.Of(region)));
            float sourceSurface = from.table.Sample(t, angle), targetSurface = to.table.Sample(targetT, angle);
            bool hasBody = sourceSurface > MinSurfaceRadius && targetSurface > MinSurfaceRadius;
            if (!hasBody) return false;

            float mapped = CylinderTable.MapRadius(rho, sourceSurface, targetSurface, margin);
            targetPoint = FromCylinder(to.frame, targetT, angle, mapped) + ChainShift(source, target, region, caps);
            return true;
        }

        /// <summary>
        /// 길이 방향 추가 배율(1 이상). 길이 배율이 굵기 배율 / maxSquash보다 작으면 그만큼 옷을 길게 둔다.
        /// Lumelana(몸통 길이 x0.92, 굵기 x0.98)처럼 차이가 작으면 1이라 바뀌지 않는다.
        /// </summary>
        static float Stretch((Frame frame, CylinderTable table) from, (Frame frame, CylinderTable table) to, float maxSquash)
        {
            float sourceRadius = from.table.MedianRadius();
            if (sourceRadius <= 0f || maxSquash <= 0f) return 1f;
            float lengthRatio = to.frame.Length / from.frame.Length;
            float radialRatio = to.table.MedianRadius() / sourceRadius;
            return Mathf.Max(1f, radialRatio / maxSquash / lengthRatio);
        }

        /// <summary>
        /// 늘인 길이 위치. 몸통은 목(t = 1)을, 팔은 뿌리(t = 0, 어깨·팔꿈치)를 고정하고 반대쪽으로 늘인다. 다리는 그대로.
        /// 팔에서 관절보다 몸 안쪽(t &lt; 0, 어깨 덮개 등)은 늘이지 않는다(늘이면 몸통 안으로 더 들어간다).
        /// </summary>
        static float StretchedT(BodyRegion region, float t, float stretch)
        {
            if (region == BodyRegion.Torso) return 1f - (1f - t) * stretch;
            bool isArmBeyondJoint = IsArm(region) && t > 0f;
            return isArmBeyondJoint ? t * stretch : t;
        }

        static bool IsArm(BodyRegion region) => region == BodyRegion.LeftUpperArm || region == BodyRegion.LeftLowerArm
            || region == BodyRegion.RightUpperArm || region == BodyRegion.RightLowerArm;

        /// <summary>
        /// 앞 마디가 늘어난 만큼 뒤 마디 결과를 함께 옮긴다. 허벅지는 몸통 아랫단이 늘어난 만큼 내리고(몸통 규칙과 섞일 때 위아래가 어긋나지 않게),
        /// 아래팔은 위팔 소매가 늘어난 만큼 손 쪽으로 민다(위팔 소매와 겹치지 않게).
        /// </summary>
        static Vector3 ChainShift(BodyCylinders source, BodyCylinders target, BodyRegion region, SquashCaps caps)
        {
            switch (region)
            {
                case BodyRegion.LeftUpperLeg:
                case BodyRegion.RightUpperLeg: return Extension(source, target, BodyRegion.Torso, caps);
                case BodyRegion.LeftLowerArm: return Extension(source, target, BodyRegion.LeftUpperArm, caps);
                case BodyRegion.RightLowerArm: return Extension(source, target, BodyRegion.RightUpperArm, caps);
                default: return Vector3.zero;
            }
        }

        /// <summary>
        /// 손에 매달린 옷(소맷부리)을 소매 끝이 늘어난 만큼 옮길 거리(대상 루트 기준 축). 위팔·아래팔이 늘어난 만큼의 합.
        /// </summary>
        public static Vector3 HandShift(BodyCylinders source, BodyCylinders target, bool isLeft, FitStyle style)
        {
            return HandShift(source, target, isLeft, SquashCaps.For(style));
        }

        public static Vector3 HandShift(BodyCylinders source, BodyCylinders target, bool isLeft, SquashCaps caps)
        {
            BodyRegion upper = isLeft ? BodyRegion.LeftUpperArm : BodyRegion.RightUpperArm;
            BodyRegion lower = isLeft ? BodyRegion.LeftLowerArm : BodyRegion.RightLowerArm;
            // 아래팔 소매가 다시 배치되지 않으면(어느 한쪽 몸에 아래팔 부위가 없음) 소맷부리만 앞서 나가지 않게 옮기지 않는다.
            bool hasForearm = source.Has(lower) && target.Has(lower);
            if (!hasForearm) return Vector3.zero;
            return Extension(source, target, upper, caps) + Extension(source, target, lower, caps);
        }

        /// <summary>부위가 늘어나며 고정 반대쪽 끝이 옮겨진 거리(대상 루트 기준 축). 몸통은 아래로, 팔은 손 쪽으로.</summary>
        static Vector3 Extension(BodyCylinders source, BodyCylinders target, BodyRegion region, SquashCaps caps)
        {
            if (!source.regions.TryGetValue(region, out var from) || !target.regions.TryGetValue(region, out var to)) return Vector3.zero;
            float stretch = RegionStretch(source, target, region, caps.Of(region));
            float extra = (stretch - 1f) * to.frame.Length;
            return to.frame.Axis * (region == BodyRegion.Torso ? -extra : extra);
        }

        static BodyRegion RegionOf(AvatarRig rig, Transform human)
        {
            return rig.TryGetHuman(human, out HumanBodyBones bone) ? RegionOf(bone) : BodyRegion.None;
        }

        static (float t, float angle, float radius) ToCylinder(Frame frame, Vector3 point)
        {
            Vector3 relative = point - frame.Origin;
            float along = Vector3.Dot(relative, frame.Axis);
            Vector3 radial = relative - frame.Axis * along;
            float angle = Mathf.Atan2(Vector3.Dot(radial, frame.Side), Vector3.Dot(radial, frame.Forward));
            return (along / frame.Length, angle, radial.magnitude);
        }

        static Vector3 FromCylinder(Frame frame, float t, float angle, float radius)
        {
            Vector3 direction = frame.Forward * Mathf.Cos(angle) + frame.Side * Mathf.Sin(angle);
            return frame.Origin + frame.Axis * (t * frame.Length) + direction * radius;
        }
    }
}
