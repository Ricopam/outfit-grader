# 시연 영상 편집(Remotion)

Outfit Grader 시연 요약본(`Summary`, 약 66.5초, 1920x1080 30fps, 소리 없음)을 편집한다.
흐름: 제목 -> 문제(원본, 그냥 옮긴 경우) -> 툴 조작 -> 결과 회전(세미케모형, 치비 마스코트형) -> 보정 전후 비교 -> 마무리.

## 사용법

```powershell
cd Tools/DemoVideo
npm install
npm test                  # 장면 길이·단계 계산 규칙 검증
npm run studio            # 브라우저에서 미리보기
npm run render:summary    # out/outfit-grader-summary.mp4
npm run render:explain    # out/outfit-grader-explain.mp4 (기능 설명 시안, 약 2분 5초)
```

기능 설명 영상(`Explain`)은 처리 순서대로 장마다 "적용 전 -> 작동 표시 -> 적용 후" 틀을 반복한다. 장별 정지 화면과 실측값은
`DemoExplainCapture.RenderAll`(배치 모드)이 `public/explain/`에 만든다. 단계 상태는 도구 옵션으로 만들고, "몸 비율 하나만" 상태는 분석 결과의 마디 굵기 정보를 비워 만든다.

```powershell
& $unity -batchmode -quit -projectPath . -executeMethod OutfitGrader.Demo.DemoExplainCapture.RenderAll
```

## 녹화 원본 만들기

원본은 VRC_P를 Unity에서 닫은 상태로 아래 두 명령으로 만든다. 스크립트는 로컬 시연 폴더 `Assets/_Demo/Editor/`(저장소 제외)에 있다.

```powershell
$unity = "C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe"
& $unity -batchmode -quit -projectPath . -executeMethod OutfitGrader.Demo.DemoVideoCapture.RenderAll
& $unity -projectPath . -executeMethod OutfitGrader.Demo.DemoWindowRecorder.Record
```

- `DemoVideoCapture.RenderAll`(배치 모드): 원본·그냥 옮긴 경우·결과 회전·보정 전후 정지 화면을 카메라로 직접 렌더하고 측정값을 `metrics.txt`에 남긴다. Unity Recorder는 Play 모드가 필요해 배치 모드에서는 쓰지 않는다.
- `DemoWindowRecorder.Record`(일반 모드): 창 입력을 정해진 시각에 채우고 맞추기를 실행하며 창 화면을 15fps로 저장한 뒤 Unity를 닫는다. 단계 시각은 `s2_events.json`에 남는다.
- 프레임 연번은 ffmpeg로 mp4로 바꾼다. 창 프레임은 위아래가 뒤집혀 저장되므로 `vflip`을 건다.

```powershell
cd Tools/DemoVideo/public/session
foreach ($n in "s1_original","s1_naive","s3_semi","s3_chibi") { ffmpeg -y -framerate 30 -i "$n/%04d.jpg" -c:v libx264 -crf 18 -pix_fmt yuv420p "$n.mp4" }
ffmpeg -y -framerate 15 -i "s2_window/%04d.png" -vf "vflip,pad=600:882:0:0,fps=30" -c:v libx264 -crf 16 -pix_fmt yuv420p s2_window.mp4
```

## 구성

- `src/timeline.mjs`: 장면 순서·길이, 툴 조작 단계 계산(순수 로직)
- `src/DemoVideo.jsx`: 장면 그리기(단계 이름표, 자막 2줄 이하, 조작 단계 목록, 좌우 비교)
- `src/index.jsx`: 구성 등록, 조작 단계 기록 읽기

## 메모

- Unity가 가져오지 않도록 `Assets/` 밖에 둔다.
- `public/`, `out/`, `node_modules/`, README 외 md는 저장소에 올리지 않는다(.gitignore).
- 자막은 장르 용어(케모미미형·세미케모형·치비 마스코트형)를 쓰고, 모델 이름은 마무리 크레딧에만 쓴다.
