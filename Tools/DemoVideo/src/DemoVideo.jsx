import { AbsoluteFill, Img, interpolate, OffthreadVideo, Series, staticFile, useCurrentFrame } from "remotion";

import { activeStep, STEPS } from "./timeline.mjs";

// 화면 배치 상수 - 1920x1080 기준. 소리 없음, 설명은 자막과 단계 이름표로만 한다.
const FONT = "'Malgun Gothic', 'Apple SD Gothic Neo', sans-serif";
const COLORS = { bg: "#0f1220", panel: "rgba(15, 18, 32, 0.82)", accent: "#7E57C2", text: "#f4f5fb", sub: "#aeb3c7", ok: "#3ecf8e", off: "#ff7a7a" };
const FADE_FRAMES = 8;
const session = (file) => staticFile(`session/${file}`);

export const DemoVideo = ({ scenes, events, eventFps }) => (
  <AbsoluteFill style={{ background: COLORS.bg, fontFamily: FONT, color: COLORS.text, wordBreak: "keep-all" }}>
    <Series>
      {scenes.map((scene) => (
        <Series.Sequence key={scene.id} durationInFrames={scene.frames}>
          <Fade>
            <Scene scene={scene} events={events} eventFps={eventFps} />
          </Fade>
        </Series.Sequence>
      ))}
    </Series>
  </AbsoluteFill>
);

const Fade = ({ children }) => {
  const frame = useCurrentFrame();
  const opacity = interpolate(frame, [0, FADE_FRAMES], [0, 1], { extrapolateRight: "clamp" });
  return <AbsoluteFill style={{ opacity }}>{children}</AbsoluteFill>;
};

const Scene = ({ scene, events, eventFps }) => {
  switch (scene.id) {
    case "title": return <TitleCard />;
    case "original": return <Clip scene={scene} caption="원본: 케모미미형 아바타에 입힌 모자·후드티·안경" />;
    case "naive": return <Clip scene={scene} caption={["그냥 옮기면 옷이 원본 크기·위치 그대로 떠 있음", "대상은 몸이 작고 머리가 큰 치비 마스코트형"]} />;
    case "window": return <WindowScene scene={scene} events={events} eventFps={eventFps} />;
    case "semi": return <Clip scene={scene} caption={["세미케모형에 3개를 한 번에", "몸 x0.70 / 머리 x0.96"]} />;
    case "chibi": return <Clip scene={scene} caption={["치비 마스코트형에 3개를 한 번에 (풀어 주기 켬)", "몸 x0.22 / 머리 x1.68"]} />;
    case "compare": return <CompareScene />;
    default: return <EndCard />;
  }
};

const PartLabel = ({ text }) => (
  <div style={{ position: "absolute", left: 60, top: 48, padding: "10px 22px", borderRadius: 10, background: COLORS.accent, fontSize: 30, fontWeight: 700 }}>
    {text}
  </div>
);

// 자막은 2줄 이하.
const Caption = ({ lines }) => (
  <div style={{ position: "absolute", left: 0, right: 0, bottom: 56, display: "flex", justifyContent: "center" }}>
    <div style={{ background: COLORS.panel, padding: "18px 36px", borderRadius: 14, textAlign: "center", maxWidth: 1500 }}>
      {[].concat(lines).slice(0, 2).map((line, i) => (
        <div key={i} style={{ fontSize: i === 0 ? 44 : 32, fontWeight: i === 0 ? 700 : 400, color: i === 0 ? COLORS.text : COLORS.sub, lineHeight: 1.4 }}>{line}</div>
      ))}
    </div>
  </div>
);

const Clip = ({ scene, caption }) => (
  <AbsoluteFill>
    <OffthreadVideo src={session(scene.src)} playbackRate={scene.rate} muted />
    <PartLabel text={scene.part} />
    <Caption lines={caption} />
  </AbsoluteFill>
);

const WINDOW_SCALE = 1.08;

const WindowScene = ({ scene, events, eventFps }) => {
  const frame = useCurrentFrame();
  const active = activeStep(events, eventFps, frame);
  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", left: 110, top: 60, width: 600 * WINDOW_SCALE, height: 882 * WINDOW_SCALE,
        borderRadius: 8, overflow: "hidden", boxShadow: "0 20px 60px rgba(0,0,0,0.55)" }}>
        <OffthreadVideo src={session(scene.src)} playbackRate={scene.rate} muted style={{ width: "100%", height: "100%" }} />
      </div>
      <div style={{ position: "absolute", left: 850, top: 150, width: 980 }}>
        <div style={{ fontSize: 30, color: COLORS.sub, marginBottom: 12 }}>조작</div>
        <div style={{ fontSize: 52, fontWeight: 700, marginBottom: 44 }}>Tools &gt; Outfit Grader</div>
        {STEPS.map((step, index) => <StepRow key={step.key} index={index} text={step.text} state={index < active ? "done" : index === active ? "now" : "todo"} />)}
        <div style={{ fontSize: 24, color: COLORS.sub, marginTop: 36 }}>맞추기 계산(실제 약 3초)은 영상에서 생략</div>
      </div>
    </AbsoluteFill>
  );
};

const StepRow = ({ index, text, state }) => {
  const color = state === "now" ? COLORS.text : state === "done" ? COLORS.sub : "#5b6178";
  return (
    <div style={{ display: "flex", alignItems: "center", gap: 22, marginBottom: 26, fontSize: 34, color, fontWeight: state === "now" ? 700 : 400 }}>
      <div style={{ width: 52, height: 52, borderRadius: 26, flexShrink: 0, display: "flex", alignItems: "center", justifyContent: "center", fontSize: 26,
        background: state === "now" ? COLORS.accent : "transparent", border: `2px solid ${state === "todo" ? "#5b6178" : COLORS.accent}` }}>
        {state === "done" ? "v" : index + 1}
      </div>
      {text}
    </div>
  );
};

const CompareScene = () => {
  const frame = useCurrentFrame();
  const zoom = interpolate(frame, [0, 360], [1, 1.06]);
  return (
    <AbsoluteFill>
      {[["s4_body_before.jpg", "관통 보정 끔", COLORS.off], ["s4_body_after.jpg", "관통 보정 켬 (기본)", COLORS.ok]].map(([file, label, color], i) => (
        <div key={file} style={{ position: "absolute", left: i * 960, top: 0, width: 960, height: 1080, overflow: "hidden" }}>
          <Img src={session(file)} style={{ width: 960, height: 1080, transform: `scale(${zoom})` }} />
          <div style={{ position: "absolute", top: 44, left: 0, right: 0, textAlign: "center" }}>
            <span style={{ background: COLORS.panel, border: `3px solid ${color}`, borderRadius: 12, padding: "10px 26px", fontSize: 36, fontWeight: 700 }}>{label}</span>
          </div>
        </div>
      ))}
      <div style={{ position: "absolute", left: 958, top: 0, width: 4, height: 1080, background: COLORS.bg }} />
      <PartLabel text="비교" />
      <Caption lines={["세미케모형: 몸 안에 묻힌 후드티 정점 1,076 -> 159", "치비 마스코트형: 머리 안에 묻힌 모자 정점 217 -> 4"]} />
    </AbsoluteFill>
  );
};

const TitleCard = () => (
  <AbsoluteFill style={{ justifyContent: "center", alignItems: "center" }}>
    <div style={{ fontSize: 104, fontWeight: 700 }}>Outfit Grader</div>
    <div style={{ fontSize: 42, color: COLORS.sub, marginTop: 28 }}>체형이 전혀 다른 아바타에 의상을 옮겨 입히는 Unity 에디터 도구</div>
  </AbsoluteFill>
);

const CREDITS = "フェリス (リュクらぼ) · Lumelana (Murixir workshop) · inuinu いぬいぬ (zuchicorn) · ニット帽 (TRIM) · フェリスちゃん用メガネ (MT-Shop)";

const EndCard = () => (
  <AbsoluteFill style={{ justifyContent: "center", alignItems: "center" }}>
    <div style={{ fontSize: 88, fontWeight: 700 }}>Outfit Grader</div>
    <div style={{ fontSize: 36, color: COLORS.sub, marginTop: 24 }}>Unity 2022.3 · 외부 패키지 없음 · 비파괴(Ctrl+Z 한 번)</div>
    <div style={{ position: "absolute", bottom: 60, fontSize: 22, color: COLORS.sub }}>시연 모델: {CREDITS}</div>
  </AbsoluteFill>
);
