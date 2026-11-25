// 장면 순서·길이와 툴 조작 단계 계산 (순수 로직, React·Remotion 의존 없음).
export const FPS = 30;

// 원본 소스 길이(프레임, 30fps)와 재생 속도로 장면 길이를 정한다. 장면 길이는 소스가 끝나 빈 화면이 나오지 않게 맞춘다.
export const SCENES = [
  { id: "title", part: null, frames: 90 },
  { id: "original", part: "문제", src: "s1_original.mp4", srcFrames: 90, rate: 1 },
  { id: "naive", part: "문제", src: "s1_naive.mp4", srcFrames: 90, rate: 0.75 },
  { id: "window", part: "조작", src: "s2_window.mp4", srcFrames: 496, rate: 1 },
  { id: "semi", part: "결과", src: "s3_semi.mp4", srcFrames: 180, rate: 0.5 },
  { id: "chibi", part: "결과", src: "s3_chibi.mp4", srcFrames: 180, rate: 0.5 },
  { id: "compare", part: "비교", frames: 360 },
  { id: "end", part: null, frames: 120 },
];

/** 소스 길이 / 재생 속도로 장면 길이를 채운 목록 */
export function withDurations(scenes) {
  return scenes.map((s) => ({ ...s, frames: s.frames ?? Math.floor(s.srcFrames / s.rate) }));
}

export function totalFrames(scenes) {
  return withDurations(scenes).reduce((sum, s) => sum + s.frames, 0);
}

// 툴 조작 단계. key는 녹화기(DemoWindowRecorder) 단계 이름과 같다.
export const STEPS = [
  { key: "source", text: "원본 아바타 지정" },
  { key: "outfits", text: "옮길 의상 3개 지정 (모자·후드티·안경)" },
  { key: "target", text: "대상 아바타 지정" },
  { key: "scroll", text: "의상마다 진단(문제·원인·해결)과 본 연결 표 확인" },
  { key: "relax", text: "치비 체형용 \"풀어 주기\" 켜기" },
  { key: "run", text: "맞추기 실행 -> Ctrl+Z 한 번으로 전부 되돌리기 가능" },
];

/**
 * 녹화 프레임 기준 단계 기록(events, srcFps)에서 지금 장면 프레임의 진행 단계 번호를 구한다.
 * 아직 첫 단계 전이면 -1.
 */
export function activeStep(events, srcFps, frame, fps = FPS) {
  const srcFrame = (frame * srcFps) / fps;
  let active = -1;
  STEPS.forEach((step, index) => {
    const event = events.find((e) => e.step === step.key);
    if (event && srcFrame >= event.frame) active = index;
  });
  return active;
}
