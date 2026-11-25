namespace OutfitGrader
{
    /// <summary>
    /// 체형이 많이 다른 아바타(팔·몸통이 짧고 굵은 치비 등)에 옷을 입힐 때 옷 길이를 정하는 방식.
    /// 옷을 몸 길이에 그대로 맞추면 지퍼·끈·소매가 한쪽으로 크게 눌려 형태를 알아보기 어려워지므로,
    /// 한쪽 눌림(굵기 배율 / 길이 배율)이 상한을 넘지 않게 옷 길이를 남긴다. 두 방식은 몸통 상한만 다르다.
    /// </summary>
    public enum FitStyle
    {
        /// <summary>정사이즈: 몸통 길이에 가깝게 맞추고 눌림은 조금 더 허용한다(옷자락이 엉덩이 근처에서 끝남).</summary>
        Regular,
        /// <summary>
        /// 오버핏: 옷 비율을 더 지키고 옷자락을 길게 둔다. 단 아랫단은 허벅지 절반(BodyCylinders.HemReachToThigh)을 넘지 않아,
        /// 몸통이 아주 짧은 아바타는 두 방식이 같은 한도에 닿아 결과가 같을 수 있다(형태 보존 우선).
        /// </summary>
        Oversized,
    }

    public static class FitStyles
    {
        /// <summary>정사이즈 몸통에서 허용하는 한쪽 눌림 상한</summary>
        public const float RegularTorsoMaxSquash = 4f;
        /// <summary>오버핏 몸통에서 허용하는 한쪽 눌림 상한</summary>
        public const float OversizedTorsoMaxSquash = 2f;
        /// <summary>
        /// 소매(위팔·아래팔)의 한쪽 눌림 상한. 두 방식이 같다. 소맷부리가 원래 손을 덮을 만큼 길어(inuinu에서 아래팔의 약 2배)
        /// 소매를 더 늘이면 날개처럼 뻗어(오버핏 상한 2·3배 실험) 소매는 심하게 눌린 곳만 조금 늘인다.
        /// </summary>
        public const float ArmMaxSquash = 4f;

        /// <summary>부위별 한쪽 눌림 상한. 몸통·팔 외 부위는 0(늘이지 않음).</summary>
        public static float MaxSquash(FitStyle style, BodyRegion region)
        {
            switch (region)
            {
                case BodyRegion.Torso: return style == FitStyle.Oversized ? OversizedTorsoMaxSquash : RegularTorsoMaxSquash;
                case BodyRegion.LeftUpperArm:
                case BodyRegion.LeftLowerArm:
                case BodyRegion.RightUpperArm:
                case BodyRegion.RightLowerArm: return ArmMaxSquash;
                default: return 0f;
            }
        }
    }
}
