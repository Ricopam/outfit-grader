using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 의상 맞추기 창. 입력 -> 실행 전 진단 -> 본 연결 확인·직접 지정 -> 실행 -> 저장 순서로 구성한다.
    /// 여러 의상을 한 아바타에 한 번에 입히며, 계산은 FitAnalysis/OutfitFitter가 맡고 이 창은 상태를 그리기만 한다.
    /// </summary>
    public sealed class OutfitGraderWindow : EditorWindow
    {
        const float MinMultiplier = 0.5f, MaxMultiplier = 2f;

        // 창을 다시 열거나 스크립트가 다시 컴파일되어도 입력이 유지되도록 직렬화한다.
        [SerializeField] Animator source, target;
        [SerializeField] OutfitInputList outfits = new OutfitInputList();
        [SerializeField] float bodyMultiplier = 1f, headMultiplier = 1f;
        [SerializeField] bool fixPenetration = true;
        [SerializeField] bool preserveOffset = true;
        [SerializeField] FitStyle fitStyle = FitStyle.Oversized;
        [SerializeField] bool relax;
        bool showOnlyAttention;
        bool scaleFoldout;

        readonly Dictionary<Transform, Transform> manualTargets = new Dictionary<Transform, Transform>();
        /// <summary>의상마다 하나씩. 의상 칸 순서와 같다.</summary>
        List<FitAnalysis> analyses = new List<FitAnalysis>();
        List<FitResult> lastResults = new List<FitResult>();
        string statusMessage;
        MessageType statusType = MessageType.None;
        Vector2 scroll;

        [MenuItem("Tools/Outfit Grader")]
        public static void Open()
        {
            var window = GetWindow<OutfitGraderWindow>();
            window.titleContent = new GUIContent("Outfit Grader");
            window.minSize = new Vector2(420f, 360f);
        }

        void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            Reanalyze();
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        void OnUndoRedo()
        {
            Reanalyze();
            Repaint();
        }

        /// <summary>계층에서 본을 지우거나 옮기면 이전 분석이 파괴된 오브젝트를 가리키므로 다시 분석한다.</summary>
        void OnHierarchyChange()
        {
            Reanalyze();
            Repaint();
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawInputs();
            EditorGUILayout.Space();

            if (!HasAllInputs())
            {
                DrawEmptyState();
            }
            else
            {
                DrawScale();
                DrawOutfitSections();
            }

            EditorGUILayout.Space();
            DrawActions();
            DrawStatus();
            EditorGUILayout.EndScrollView();
        }

        // ---------- 입력 ----------

        void DrawInputs()
        {
            EditorGUILayout.LabelField("입력", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            source = (Animator)EditorGUILayout.ObjectField(
                new GUIContent("원본 아바타", "의상이 원래 대응하는 아바타 (씬에 배치된 Humanoid)"), source, typeof(Animator), true);
            bool outfitsChanged = outfits.Draw();
            target = (Animator)EditorGUILayout.ObjectField(
                new GUIContent("대상 아바타", "의상을 옮겨 입힐 아바타 (씬에 배치된 Humanoid)"), target, typeof(Animator), true);
            if (EditorGUI.EndChangeCheck() || outfitsChanged) RequestAfterGui(ResetForNewInputs);

            if (GUILayout.Button(new GUIContent("다시 분석", "의상이나 아바타를 고친 뒤 다시 잽니다. 직접 지정한 연결은 초기화됩니다.")))
            {
                RequestAfterGui(ResetForNewInputs);
            }
        }

        /// <summary>
        /// 그리는 도중 분석 결과(의상 수·표 줄 수)가 바뀌면 IMGUI의 배치 계산과 그리기 단계가 어긋나 예외가 나므로,
        /// 입력 변경에 따른 재분석은 이번 그리기가 끝난 뒤에 한다.
        /// </summary>
        void RequestAfterGui(EditorApplication.CallbackFunction action)
        {
            EditorApplication.delayCall += () =>
            {
                if (this == null) return; // 그 사이 창이 닫힌 경우
                action();
                Repaint();
            };
        }

        void ResetForNewInputs()
        {
            manualTargets.Clear();
            lastResults.Clear();
            statusMessage = null;
            Reanalyze();
        }

        bool HasAllInputs() => source != null && outfits.HasAny && target != null;

        static void DrawEmptyState()
        {
            EditorGUILayout.HelpBox(
                "사용 순서\n" +
                "1. 원본 아바타: 의상이 원래 대응하는 아바타를 씬에서 지정합니다.\n" +
                "2. 의상: 원본 아바타에 입힌 모자·상의·안경을 지정합니다. 여러 개를 한 번에 끌어다 놓을 수 있습니다.\n" +
                "3. 대상 아바타: 의상을 옮겨 입힐 아바타를 지정합니다.\n" +
                "칸을 채우면 의상마다 실행 전 진단과 본 연결 표가 나타납니다.",
                MessageType.None);
        }

        // ---------- 의상별 진단·본 연결 ----------

        void DrawOutfitSections()
        {
            showOnlyAttention = EditorGUILayout.ToggleLeft("확인이 필요한 본만 보기 (대체 연결, 소품 부착, 따라감)", showOnlyAttention);
            for (int i = 0; i < analyses.Count; i++)
            {
                FitAnalysis analysis = analyses[i];
                EditorGUILayout.Space();
                string name = analysis.Outfit != null ? analysis.Outfit.name : "(배치되지 않은 의상)";
                EditorGUILayout.LabelField($"의상 {i + 1}: {name}  {CountLabel(analysis)}", EditorStyles.boldLabel);
                DrawDiagnostics(analysis);
                MatchTableView.Draw(analysis, showOnlyAttention, SetManualTarget);
            }
        }

        static string CountLabel(FitAnalysis analysis)
        {
            int errors = analysis.Diagnostics.Count(d => d.Severity == Severity.Error);
            int warnings = analysis.Diagnostics.Count(d => d.Severity == Severity.Warning);
            return errors + warnings == 0 ? "(문제 없음)" : $"(오류 {errors} / 경고 {warnings})";
        }

        static void DrawDiagnostics(FitAnalysis analysis)
        {
            foreach (Diagnostic d in analysis.Diagnostics)
            {
                string text = $"{d.Problem}\n원인: {d.Cause}\n해결: {d.Fix}";
                EditorGUILayout.HelpBox(text, ToMessageType(d.Severity));
            }
        }

        static MessageType ToMessageType(Severity severity)
        {
            switch (severity)
            {
                case Severity.Error: return MessageType.Error;
                case Severity.Warning: return MessageType.Warning;
                default: return MessageType.Info;
            }
        }

        void SetManualTarget(Transform outfitBone, Transform picked)
        {
            bool isTargetBone = picked != null && target != null && picked.IsChildOf(target.transform);
            if (picked != null && !isTargetBone)
            {
                ShowStatus("대상 아바타 안의 본만 지정할 수 있습니다.", MessageType.Warning);
                return;
            }

            if (picked == null) manualTargets.Remove(outfitBone);
            else manualTargets[outfitBone] = picked;
            RequestAfterGui(Reanalyze);
        }

        // ---------- 크기 ----------

        void DrawScale()
        {
            FitAnalysis first = analyses.FirstOrDefault(a => a.CanFit);
            if (first == null) return;

            ScaleProfile m = first.Measured;
            scaleFoldout = EditorGUILayout.Foldout(scaleFoldout,
                $"크기 비율 (측정값) 몸 x{m.Body:0.00} / 머리 x{m.Head:0.00}", true);
            if (!scaleFoldout) return;

            EditorGUI.indentLevel++;
            EditorGUI.BeginChangeCheck();
            bodyMultiplier = EditorGUILayout.Slider("몸 배율 조정", bodyMultiplier, MinMultiplier, MaxMultiplier);
            headMultiplier = EditorGUILayout.Slider("머리 배율 조정", headMultiplier, MinMultiplier, MaxMultiplier);
            if (EditorGUI.EndChangeCheck()) RequestAfterGui(Reanalyze);
            if (GUILayout.Button("배율 초기화"))
            {
                bodyMultiplier = headMultiplier = 1f;
                RequestAfterGui(Reanalyze);
            }
            EditorGUI.indentLevel--;
        }

        // ---------- 실행·저장 ----------

        bool CanFitAll() => HasAllInputs() && analyses.Count > 0 && analyses.All(a => a.CanFit);

        bool HasResults() => lastResults.Count > 0 && lastResults.Any(r => r.OutfitRoot != null);

        /// <summary>맞춤 방식 선택. 체형 차이가 작은 아바타는 두 방식의 결과가 같다.</summary>
        static FitStyle DrawFitStyle(FitStyle current)
        {
            var label = new GUIContent("맞춤 방식",
                "팔·몸통이 짧고 굵은 아바타(치비 등)에서 옷 길이를 정하는 방식입니다. " +
                "정사이즈: 몸 길이에 가깝게 맞춥니다(옷이 조금 눌릴 수 있음). " +
                "오버핏: 옷 모양을 더 지키고 소매·옷자락이 손과 다리를 덮도록 길게 둡니다. 체형 차이가 작으면 두 방식 결과가 같습니다.");
            var options = new[] { new GUIContent("정사이즈"), new GUIContent("오버핏") };
            return (FitStyle)EditorGUILayout.Popup(label, (int)current, options);
        }

        void DrawActions()
        {
            fixPenetration = EditorGUILayout.ToggleLeft(new GUIContent("관통 밀어내기 (몸 안으로 들어간 옷을 밖으로)",
                "몸 안으로 들어간 의상 정점을 대상 몸 표면 바깥으로 밀어낸 새 메시로 교체합니다. 모자는 두상에 맞추고 귀가 지나가는 자리에, 옷은 꼬리가 지나가는 자리에 구멍을 내며, 옷에 가려지는 털 묶음은 숨깁니다. " +
                "원본 메시는 바뀌지 않으며, " +
                "새 메시는 프리팹 저장 때 " + ResultPrefabSaver.MeshFolder + "에 저장됩니다."), fixPenetration);
            using (new EditorGUI.DisabledScope(!fixPenetration))
            using (new EditorGUI.IndentLevelScope())
            {
                preserveOffset = EditorGUILayout.ToggleLeft(new GUIContent("표면 간격 보존 (상의를 체형에 맞게 다시 배치)",
                    "상의의 몸통·팔 부위를 원본 몸과 떨어져 있던 간격과 겹쳐 입은 층 순서를 지키며 대상 체형에 다시 놓고, " +
                    "몸·속옷이 뚫은 자리를 정리합니다. 결과가 이상하면 끄면 이전 방식(밀어내기)으로 맞춥니다. 후드 등 머리 주변은 이전 방식을 씁니다."),
                    preserveOffset);
                using (new EditorGUI.DisabledScope(!preserveOffset))
                {
                    fitStyle = DrawFitStyle(fitStyle);
                    relax = EditorGUILayout.ToggleLeft(new GUIContent("풀어 주기 (체형 차이가 큰 아바타용)",
                        "맞춘 뒤 몸통·소매를 짧게 풀어 길이로 크게 눌린 곳의 날카로운 접힘을 부드럽게 펴고, 아랫단은 엉덩이까지만 늘입니다. " +
                        "머리가 크고 팔·몸통이 짧은 치비 아바타에서 효과가 크며, 아바타당 약 1~2초 더 걸립니다."), relax);
                }
            }
            bool canFit = CanFitAll();
            string fitTooltip = canFit
                ? "모든 의상을 대상 아바타 안에 입힙니다. 원본 의상은 바뀌지 않고, Ctrl+Z 한 번으로 전부 되돌릴 수 있습니다."
                : "오류가 있는 의상을 고치거나 목록에서 빼 주세요.";
            using (new EditorGUI.DisabledScope(!canFit))
            {
                string label = analyses.Count > 1 ? $"맞추기 실행 (의상 {analyses.Count}개)" : "맞추기 실행";
                if (GUILayout.Button(new GUIContent(label, fitTooltip), GUILayout.Height(28f))) RequestAfterGui(RunFit);
            }

            bool hasResults = HasResults();
            using (new EditorGUI.DisabledScope(!hasResults))
            {
                string tooltip = "의상을 입힌 아바타를 " + ResultPrefabSaver.OutputFolder + "에 새 프리팹으로 저장합니다. 씬의 아바타는 그대로입니다.";
                if (GUILayout.Button(new GUIContent("결과를 프리팹으로 저장", tooltip))) SaveResult();
            }

            if (!hasResults) return;
            EditorGUILayout.LabelField("변경 보고서", EditorStyles.boldLabel);
            string report = Report();
            EditorGUILayout.TextArea(report, GUILayout.MinHeight(80f));
            if (GUILayout.Button("보고서 복사")) EditorGUIUtility.systemCopyBuffer = report;
        }

        string Report()
        {
            return string.Join("\n\n", lastResults.Where(r => r.OutfitRoot != null).Select(r => $"[{r.OutfitRoot.name}]\n{r.ToReport()}"));
        }

        void RunFit()
        {
            Reanalyze();
            if (!CanFitAll()) return;

            lastResults = OutfitFitter.FitAll(analyses, new FitOptions { FixPenetration = fixPenetration, PreserveOffset = preserveOffset, Style = fitStyle, Relax = relax });
            int fitGroup = Undo.GetCurrentGroup();
            GameObject[] roots = lastResults.Select(r => r.OutfitRoot).ToArray();
            Selection.objects = roots;
            // 선택 변경이 따로 Undo 한 칸을 차지하면 Ctrl+Z 첫 번째가 선택만 되돌리므로 맞추기와 같은 묶음으로 합친다.
            Undo.CollapseUndoOperations(fitGroup);
            EditorGUIUtility.PingObject(roots.Last());
            ShowStatus($"완료: {target.name}에 의상 {roots.Length}개를 입혔습니다. Ctrl+Z 한 번으로 전부 되돌릴 수 있습니다.", MessageType.Info);
        }

        void SaveResult()
        {
            FitResult[] results = lastResults.Where(r => r.OutfitRoot != null).ToArray();
            string outfitPart = results.Length == 1 ? results[0].OutfitRoot.name : $"의상{results.Length}개";
            string path = ResultPrefabSaver.Save(results[0].Avatar, $"{results[0].Avatar.name}_{outfitPart}", out int removed);
            if (path == null)
            {
                ShowStatus("프리팹 저장에 실패했습니다. Console 창의 오류를 확인해 주세요.", MessageType.Error);
                return;
            }

            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
            string removedNote = removed > 0 ? $" (비어 있는 스크립트 {removed}개는 저장본에서 제거)" : string.Empty;
            ShowStatus($"저장했습니다: {path}{removedNote}", MessageType.Info);
        }

        void ShowStatus(string message, MessageType type)
        {
            statusMessage = message;
            statusType = type;
        }

        void DrawStatus()
        {
            if (!string.IsNullOrEmpty(statusMessage)) EditorGUILayout.HelpBox(statusMessage, statusType);
        }

        // ---------- 분석 ----------

        void Reanalyze()
        {
            List<Transform> resolved = outfits.Resolve();
            analyses = resolved.Select(outfit => FitAnalysis.Run(new FitInput
            {
                Source = source,
                Outfit = outfit,
                Target = target,
                ManualTargets = manualTargets,
                OtherOutfits = resolved.Where(o => o != null && o != outfit).ToList(),
                BodyMultiplier = bodyMultiplier,
                HeadMultiplier = headMultiplier,
            })).ToList();
        }
    }
}
