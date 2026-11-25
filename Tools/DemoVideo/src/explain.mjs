// 기능 설명 영상(Explain) 장 구성과 박자 계산 (순수 로직). 장마다 같은 4박자 틀을 반복한다.
export const FPS = 30;

// 장 안의 박자(프레임). 문제 -> 작동 표시 -> 적용 후(표시) -> 적용 후(깨끗한 화면).
export const BEATS = [
  { key: "problem", from: 0, to: 90, chip: "적용 전" },
  { key: "work", from: 90, to: 210, chip: "작동" },
  { key: "after", from: 210, to: 480, chip: "적용 후" },
];
export const CHAPTER_FRAMES = 480;
export const WIPE_FROM = 210;
export const WIPE_FRAMES = 30;

/** 장 안 프레임의 박자. 범위를 넘으면 마지막 박자. */
export function beatAt(frame) {
  return BEATS.find((b) => frame >= b.from && frame < b.to) ?? BEATS[BEATS.length - 1];
}

const n = (v) => (typeof v === "number" ? v.toLocaleString("en-US") : "?");

/**
 * 장 목록. 정지 화면 이름은 Unity 렌더(DemoExplainCapture)와 같다. 수치는 metrics.json 실측값으로 채운다.
 * 자막은 2줄 이하, 한 줄은 짧게.
 */
export function buildChapters(m) {
  const seg = m.ch3_segments ?? {};
  const forearm = seg.LeftLowerArm ?? [];
  const counts = m.ch2_counts ?? {};
  return [
    {
      id: "ch2", title: "본 연결",
      problem: ["옷의 본은 원본 아바타의 본에 묶여 있음"],
      work: ["같은 색 = 서로 연결된 부위", "보라 머리·소품 · 초록 몸통 · 주황 왼팔 · 파랑 오른팔 · 청록 다리"],
      after: ["원본은 그대로 두고, 대상 안에 옷 복제본을 입힘",
        `Humanoid ${n(counts.Humanoid)}개 · 소품 ${n(counts.Attachment)}개 연결, 나머지 ${n(counts.Unmatched)}개는 부모 본을 따라감`],
    },
    {
      id: "ch3", title: "크기 맞춤",
      problem: [`몸 비율 하나(x${seg.Body?.[0] ?? "?"})로만 줄이면 옷이 몸에 묻힘`],
      work: ["빨간 점: 몸 안에 묻힌 후드티 정점 (목 아래)", "팔·몸통 마디마다 몸 단면 굵기와 길이를 잼"],
      after: [`묻힌 정점 ${n(m.ch3_inside_before)} -> ${n(m.ch3_inside_after)}`, `마디마다 따로 맞춤 (예: 아래팔 굵기 x${forearm[0] ?? "?"}, 길이 x${forearm[1] ?? "?"})`],
    },
    {
      id: "ch4", title: "소품 부착",
      problem: ["그냥 옮기면 안경·모자·후드티가 원본 크기·높이 그대로 떠 있음"],
      work: ["보라 상자: 원본 머리 구역(위)과 대상 머리 구역(아래)을 따로 잼", "몸 x0.22 / 머리 x1.68 (치비 마스코트형)"],
      after: ["머리 본에 붙이고 머리 비율로 크기를 맞춤", "얼굴 앞면과의 간격도 원본처럼 유지"],
    },
    {
      id: "ch5", title: "관통 밀어내기",
      problem: ["크기를 맞춰도 몸이 옷을 뚫는 자리가 남음"],
      work: ["빨간 점: 몸 표면 안쪽에 있는 옷 정점 (목 아래)", "찾은 정점을 몸 바깥으로 밀어낸 새 메시로 교체"],
      after: [`묻힌 정점 ${n(m.ch5_inside_before)} -> ${n(m.ch5_inside_after)}`, `치비 모자도 머리 안쪽 정점 ${n(m.ch5_hat_before)} -> ${n(m.ch5_hat_after)}, 귀 자리에 구멍`],
    },
    {
      id: "ch6", title: "표면 간격 보존",
      problem: ["정점을 하나씩 밀면 소매가 울퉁불퉁하고 속옷이 옷을 뚫음"],
      work: ["원본 몸과 떨어져 있던 간격을 그대로 기억", "대상 몸 표면 기준으로 다시 놓아 겹친 층 순서를 지킴"],
      after: [`묻힌 정점 ${n(m.ch6_inside_before)} -> ${n(m.ch6_inside_after)}`, "소매가 매끈해지고 속옷이 뚫던 자리가 사라짐"],
    },
    {
      id: "ch7", title: "풀어 주기 (치비용)",
      problem: ["팔·몸통이 짧은 치비 체형에서는 옷이 크게 눌려 접힘"],
      work: ["몸통·소매를 조금씩 풀어 날카로운 접힘을 폄", "아랫단은 엉덩이까지만 늘임"],
      after: [`묻힌 정점 ${n(m.ch7_inside_before)} -> ${n(m.ch7_inside_after)}`, "소매의 톱니가 펴지고 앞발을 덮는 매끈한 통이 됨"],
    },
  ];
}

// 전체 장면 순서. 장 번호는 화면 표시용(1장은 진단 창 녹화).
export function buildScenes(m) {
  const chapters = buildChapters(m).map((c, i) => ({ type: "chapter", frames: CHAPTER_FRAMES, number: i + 2, ...c }));
  return [
    { type: "title", frames: 90 },
    { type: "window", frames: 300, number: 1, title: "실행 전 진단", from: 0, focus: "top" },
    ...chapters,
    { type: "windowEnd", frames: 134, from: 362, focus: "bottom" },
    { type: "turn", frames: 180, src: "s3_semi.mp4", caption: ["세미케모형 결과", "몸 x0.70 / 머리 x0.96"] },
    // 마지막 장면은 제목 화면 없이 서서히 어두워지며 끝난다. 시연 모델 크레딧은 이 장면에 작게 넣는다.
    { type: "turn", frames: 180, src: "s3_chibi.mp4", caption: ["치비 마스코트형 결과 (풀어 주기 켬)", "몸 x0.22 / 머리 x1.68"], fadeOut: FADE_OUT_FRAMES, credits: true },
  ];
}

export const FADE_OUT_FRAMES = 45;

/** 끝에서 fadeFrames 동안 1 -> 0으로 줄어드는 불투명도 */
export function fadeOutOpacity(frame, frames, fadeFrames) {
  if (!fadeFrames) return 1;
  return Math.min(1, Math.max(0, (frames - frame) / fadeFrames));
}

export function totalFrames(scenes) {
  return scenes.reduce((sum, s) => sum + s.frames, 0);
}
