import { Composition, registerRoot, staticFile } from "remotion";

import { DemoVideo } from "./DemoVideo.jsx";
import { Explain } from "./Explain.jsx";
import { buildScenes, totalFrames as explainFrames } from "./explain.mjs";
import { FPS, SCENES, totalFrames, withDurations } from "./timeline.mjs";

// 툴 조작 녹화의 단계 기록(Assets/_Demo/Editor/DemoWindowRecorder.cs가 public/session에 남김)을 읽는다.
async function loadSummaryProps() {
  const run = await fetch(staticFile("session/s2_events.json")).then((response) => response.json());
  return { props: { scenes: withDurations(SCENES), events: run.events, eventFps: run.fps } };
}

// 기능 설명 영상의 실측값(Assets/_Demo/Editor/DemoExplainCapture.cs가 public/explain에 남김)으로 장면을 만든다.
async function loadExplainProps() {
  const metrics = await fetch(staticFile("explain/metrics.json")).then((response) => response.json());
  const scenes = buildScenes(metrics);
  return { props: { scenes }, durationInFrames: explainFrames(scenes) };
}

const Root = () => (
  <>
    <Composition id="Summary" component={DemoVideo} width={1920} height={1080} fps={FPS} durationInFrames={totalFrames(SCENES)}
      defaultProps={{ scenes: withDurations(SCENES), events: [], eventFps: 15 }}
      calculateMetadata={() => loadSummaryProps()} />
    <Composition id="Explain" component={Explain} width={1920} height={1080} fps={FPS} durationInFrames={explainFrames(buildScenes({}))}
      defaultProps={{ scenes: buildScenes({}) }}
      calculateMetadata={() => loadExplainProps()} />
  </>
);

registerRoot(Root);
