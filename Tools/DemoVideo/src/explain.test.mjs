import assert from "node:assert/strict";
import { test } from "node:test";

import { beatAt, BEATS, buildChapters, buildScenes, CHAPTER_FRAMES, fadeOutOpacity, FPS, totalFrames } from "./explain.mjs";

const metrics = {
  ch2_counts: { Humanoid: 15, Attachment: 2, Name: 1, Unmatched: 14 },
  ch3_segments: { LeftLowerArm: [1.19, 0.6], Body: [0.7, 0.7] },
  ch3_inside_before: 2979, ch3_inside_after: 1076,
};

test("전체 길이 2~3분", () => {
  const seconds = totalFrames(buildScenes(metrics)) / FPS;
  assert.ok(seconds >= 120 && seconds <= 180, `${seconds}초`);
});

test("박자는 빈틈없이 장 길이를 덮고 각 3초 이상", () => {
  assert.equal(BEATS[0].from, 0);
  assert.equal(BEATS[BEATS.length - 1].to, CHAPTER_FRAMES);
  BEATS.forEach((b, i) => {
    if (i > 0) assert.equal(b.from, BEATS[i - 1].to);
    assert.ok(b.to - b.from >= 3 * FPS, b.key);
  });
});

test("박자 찾기", () => {
  assert.equal(beatAt(0).key, "problem");
  assert.equal(beatAt(90).key, "work");
  assert.equal(beatAt(479).key, "after");
  assert.equal(beatAt(999).key, "after");
});

test("자막은 2줄 이하, 실측값이 들어감", () => {
  const chapters = buildChapters(metrics);
  for (const c of chapters) for (const key of ["problem", "work", "after"]) assert.ok(c[key].length <= 2, `${c.id} ${key}`);
  assert.equal(chapters.find((c) => c.id === "ch3").after[0], "묻힌 정점 2,979 -> 1,076");
});

test("마지막 장면은 페이드 아웃으로 끝남 (제목 화면 없음)", () => {
  const scenes = buildScenes(metrics);
  const last = scenes[scenes.length - 1];
  assert.ok(last.fadeOut > 0);
  assert.ok(!scenes.some((s) => s.type === "end"));
  assert.equal(fadeOutOpacity(0, last.frames, last.fadeOut), 1);
  assert.equal(fadeOutOpacity(last.frames, last.frames, last.fadeOut), 0);
  assert.ok(fadeOutOpacity(last.frames - 1, last.frames, last.fadeOut) < 0.1);
});

test("값이 없으면 ?로 표시하고 멈추지 않음", () => {
  const c = buildChapters({}).find((x) => x.id === "ch5");
  assert.equal(c.after[0], "묻힌 정점 ? -> ?");
});
