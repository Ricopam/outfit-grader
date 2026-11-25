namespace OutfitGrader
{
    /// <summary>표면 간격 보존 배치(SurfaceOffsetFitter.Fit) 결과 보고용 요약</summary>
    public sealed class PlacementReport
    {
        /// <summary>형태 검사로 눌림 상한을 낮춘 부위. 낮추지 않았으면 빈 문자열.</summary>
        public string FormNote = string.Empty;
        /// <summary>모양을 그대로 두고 크기만 맞춘 끈 수</summary>
        public int Strips;
        /// <summary>후드(머리·목 본에만 묶인 덩어리)를 목둘레에 붙이려고 옮긴 가장 큰 거리(월드 단위)</summary>
        public float HoodShift;
        /// <summary>소맷부리(손 본에만 묶인 덩어리)를 팔 방향으로 줄인 가장 작은 배율(1이면 줄이지 않음)과 둘레 배율 중 가장 큰 값(0이면 붙이지 않음)</summary>
        public float CuffScale = 1f, CuffGirth;
        /// <summary>정점마다 끈 여부. 뒤따르는 겹침 정리에서 끈을 정점마다 밀면 구겨지므로 움직이지 않는다.</summary>
        public bool[] IsStrip;
        /// <summary>
        /// 풀어 주기 옵션을 켰을 때 정점마다 풀 부위(몸통·팔). 고정할 정점(끈·후드·소맷부리·부위 밖)은 None. 옵션을 끄면 null.
        /// 겹침 정리까지 마친 뒤 GarmentRelaxer.RelaxMesh에 넘긴다.
        /// </summary>
        public BodyRegion[] RelaxRegion;
        /// <summary>
        /// 소매 정점 두 개(메시 정점 번호) 사이의 쉼 거리: 원본 변 벡터를 각 정점 팔 마디의 축 방향은 길이 배율, 둘레는 굵기 배율로 바꾼 두 벡터의 평균 길이.
        /// 둘 중 하나라도 소매가 아니면 음수. 풀어 주기는 소매끼리의 변·접힘을 이 거리로 당긴다. 옵션을 끄면 null.
        /// </summary>
        public System.Func<int, int, float> RelaxRestLength;
        /// <summary>몸통이 길이로 많이 눌려 남는 천이 층으로 겹칠 만큼인지(늘어뜨리기 대상, GarmentDraper.MinTorsoSquash 이상)</summary>
        public bool NeedsDrape;
    }
}
