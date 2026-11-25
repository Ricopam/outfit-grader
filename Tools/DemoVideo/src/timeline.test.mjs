import assert from "node:assert/strict";
import { test } from "node:test";

import { activeStep, SCENES, STEPS, totalFrames, withDurations, FPS } from "./timeline.mjs";

test("전체 길이는 PRD 기준 60~90초 안", () => {
  const seconds = totalFrames(SCENES) / FPS;
  assert.ok(seconds >= 60 && seconds <= 90, `${seconds}초`);
});

test("문제 장면이 첫 10초 안에 시작", () => {
  const scenes = withDurations(SCENES);
  const start = scenes.slice(0, scenes.findIndex((s) => s.part === "문제")).reduce((sum, s) => sum + s.frames, 0);
  assert.ok(start / FPS < 10);
});

test("소스 영상 장면은 소스보다 길지 않음 (빈 화면 방지)", () => {
  for (const s of withDurations(SCENES).filter((s) => s.src)) {
    assert.ok(s.frames * s.rate <= s.srcFrames, s.id);
  }
});

test("모든 장면이 3초 이상 머무름 (자막 기준)", () => {
  for (const s of withDurations(SCENES)) assert.ok(s.frames >= 3 * FPS, s.id);
});

test("단계 기록으로 진행 단계를 구함 (15fps 녹화 -> 30fps 영상)", () => {
  const events = [{ step: "source", frame: 24 }, { step: "outfits", frame: 48 }, { step: "run", frame: 181 }];
  assert.equal(activeStep(events, 15, 0), -1);
  assert.equal(activeStep(events, 15, 48), 0);
  assert.equal(activeStep(events, 15, 95), 0);
  assert.equal(activeStep(events, 15, 96), 1);
  assert.equal(activeStep(events, 15, 400), STEPS.findIndex((s) => s.key === "run"));
});
