import { AbsoluteFill, Img, interpolate, OffthreadVideo, Series, staticFile, useCurrentFrame } from "remotion";

import { beatAt, fadeOutOpacity, WIPE_FRAMES, WIPE_FROM } from "./explain.mjs";

// 기능 설명 영상. 장마다 같은 카메라의 정지 화면 4장(적용 전, 작동 표시, 적용 후 표시, 적용 후)을 겹쳐 전환한다.
const FONT = "'Malgun Gothic', 'Apple SD Gothic Neo', sans-serif";
const COLORS = { bg: "#0f1220", panel: "rgba(15, 18, 32, 0.84)", accent: "#7E57C2", text: "#f4f5fb", sub: "#aeb3c7", red: "#ff4050" };
const FADE = 10;
const shot = (id, name) => staticFile(`explain/${id}_${name}.png`);
const session = (file) => staticFile(`session/${file}`);

export const Explain = ({ scenes }) => (
  <AbsoluteFill style={{ background: COLORS.bg, fontFamily: FONT, color: COLORS.text, wordBreak: "keep-all" }}>
    <Series>
      {scenes.map((scene, i) => (
        <Series.Sequence key={i} durationInFrames={scene.frames}>
          <FadeIn><Scene scene={scene} /></FadeIn>
        </Series.Sequence>
      ))}
    </Series>
  </AbsoluteFill>
);

const FadeIn = ({ children }) => {
  const opacity = interpolate(useCurrentFrame(), [0, FADE], [0, 1], { extrapolateRight: "clamp" });
  return <AbsoluteFill style={{ opacity }}>{children}</AbsoluteFill>;
};

const Scene = ({ scene }) => {
  switch (scene.type) {
    case "title": return <Title />;
    case "window": return <WindowClip scene={scene} caption={["옷·아바타 상태를 실행 전에 검사", "문제마다 문제 / 원인 / 해결을 보여 주고, 오류가 있으면 실행을 잠금"]} />;
    case "windowEnd": return <WindowClip scene={{ ...scene, title: "실행" }} caption={["맞추기 실행 -> 대상 아바타 안에 의상 3개", "Ctrl+Z 한 번으로 전부 되돌릴 수 있음"]} />;
    case "chapter": return <Chapter scene={scene} />;
    default: return <Turn scene={scene} />;
  }
};

const ChapterHeader = ({ number, title }) => (
  <div style={{ position: "absolute", left: 56, top: 44, display: "flex", alignItems: "center", gap: 18 }}>
    {number ? <div style={{ width: 64, height: 64, borderRadius: 32, background: COLORS.accent, display: "flex", alignItems: "center", justifyContent: "center", fontSize: 34, fontWeight: 700 }}>{number}</div> : null}
    <div style={{ fontSize: 46, fontWeight: 700, textShadow: "0 2px 12px rgba(0,0,0,0.6)" }}>{title}</div>
  </div>
);

const Chip = ({ text }) => (
  <div style={{ position: "absolute", right: 56, top: 52, padding: "10px 24px", borderRadius: 10, border: `2px solid ${COLORS.accent}`, background: COLORS.panel, fontSize: 30, fontWeight: 700 }}>{text}</div>
);

// 자막은 2줄 이하. 첫 줄은 핵심, 둘째 줄은 보충.
const Caption = ({ lines }) => (
  <div style={{ position: "absolute", left: 0, right: 0, bottom: 52, display: "flex", justifyContent: "center" }}>
    <div style={{ background: COLORS.panel, padding: "18px 40px", borderRadius: 14, textAlign: "center", maxWidth: 1600 }}>
      {lines.slice(0, 2).map((line, i) => (
        <div key={i} style={{ fontSize: i === 0 ? 44 : 32, fontWeight: i === 0 ? 700 : 400, color: i === 0 ? COLORS.text : COLORS.sub, lineHeight: 1.45 }}>{line}</div>
      ))}
    </div>
  </div>
);

const Layer = ({ src, opacity = 1, clip }) => (
  <AbsoluteFill style={{ opacity, clipPath: clip }}>
    <Img src={src} style={{ width: 1920, height: 1080 }} />
  </AbsoluteFill>
);

/**
 * 장 구성: 적용 전 -> (교차 페이드) 작동 표시 -> (왼쪽에서 밀어 넘기기) 적용 후 표시 -> (교차 페이드) 적용 후.
 * 같은 카메라 구도라 바뀐 부분만 눈에 띈다.
 */
const Chapter = ({ scene }) => {
  const f = useCurrentFrame();
  const beat = beatAt(f);
  const vizIn = interpolate(f, [90, 120], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const wipe = interpolate(f, [WIPE_FROM, WIPE_FROM + WIPE_FRAMES], [0, 100], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const cleanIn = interpolate(f, [360, 390], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp" });
  const caption = scene[beat.key];
  return (
    <AbsoluteFill>
      <Layer src={shot(scene.id, "before")} />
      <Layer src={shot(scene.id, "before_viz")} opacity={vizIn} />
      <Layer src={shot(scene.id, "after_viz")} clip={`inset(0 ${100 - wipe}% 0 0)`} />
      <Layer src={shot(scene.id, "after")} opacity={cleanIn} />
      {wipe > 0 && wipe < 100 ? <div style={{ position: "absolute", top: 0, bottom: 0, left: `${wipe}%`, width: 4, background: COLORS.text }} /> : null}
      <ChapterHeader number={scene.number} title={scene.title} />
      <Chip text={beat.chip} />
      <Caption lines={caption} />
    </AbsoluteFill>
  );
};

// 창 녹화(600x882)를 확대해 글자를 읽을 수 있게 한다. 위쪽(입력·진단) 또는 아래쪽(실행·완료 메시지) 영역만 보여 준다.
const WINDOW = { w: 600, h: 882, zoom: 1.9 };
// 하단 자막(위쪽 끝 약 y=880)과 겹치지 않는 높이
const VIEW = { w: 1240, h: 750, left: 340, top: 112 };

const WindowClip = ({ scene, caption }) => {
  const shownHeight = VIEW.h / WINDOW.zoom;
  const offsetY = scene.focus === "bottom" ? WINDOW.h - shownHeight : 0;
  return (
  <AbsoluteFill>
    <div style={{ position: "absolute", left: VIEW.left, top: VIEW.top, width: VIEW.w, height: VIEW.h, borderRadius: 10, overflow: "hidden",
      boxShadow: "0 20px 60px rgba(0,0,0,0.55)", background: "#383838" }}>
      <OffthreadVideo src={session("s2_window.mp4")} trimBefore={scene.from} muted
        style={{ position: "absolute", left: (VIEW.w - WINDOW.w * WINDOW.zoom) / 2, top: -offsetY * WINDOW.zoom, width: WINDOW.w * WINDOW.zoom, height: WINDOW.h * WINDOW.zoom }} />
    </div>
    <ChapterHeader number={scene.number} title={scene.title} />
    <Caption lines={caption} />
  </AbsoluteFill>
  );
};

const Turn = ({ scene }) => {
  const opacity = fadeOutOpacity(useCurrentFrame(), scene.frames, scene.fadeOut);
  return (
    <AbsoluteFill style={{ opacity }}>
      <OffthreadVideo src={session(scene.src)} muted />
      <ChapterHeader title="결과" />
      <Caption lines={scene.caption} />
      {scene.credits ? <div style={{ position: "absolute", left: 0, right: 0, bottom: 14, textAlign: "center", fontSize: 20, color: COLORS.sub }}>시연 모델: {CREDITS}</div> : null}
    </AbsoluteFill>
  );
};

const Title = () => (
  <AbsoluteFill style={{ justifyContent: "center", alignItems: "center" }}>
    <div style={{ fontSize: 100, fontWeight: 700 }}>Outfit Grader</div>
    <div style={{ fontSize: 40, color: COLORS.sub, marginTop: 26 }}>기능별 작동 원리 - 처리 순서대로</div>
  </AbsoluteFill>
);

const CREDITS = "フェリス (リュクらぼ) · Lumelana (Murixir workshop) · inuinu いぬいぬ (zuchicorn) · ニット帽 (TRIM) · フェリスちゃん用メガネ (MT-Shop)";
