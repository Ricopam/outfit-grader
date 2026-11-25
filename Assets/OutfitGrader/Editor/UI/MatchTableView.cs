using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>의상 하나의 본 연결 표. 대상 본을 바꾸면 직접 지정으로 넘긴다.</summary>
    public static class MatchTableView
    {
        const float BoneColumnWidth = 150f, KindColumnWidth = 70f;

        /// <param name="onPick">(의상 본, 사용자가 고른 대상 본). 비우면 null.</param>
        public static void Draw(FitAnalysis analysis, bool showOnlyAttention, Action<Transform, Transform> onPick)
        {
            if (analysis == null || analysis.Matches.Count == 0) return;

            EditorGUILayout.LabelField(Summary(analysis.Matches), EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("의상 본", GUILayout.Width(BoneColumnWidth));
                GUILayout.Label("결과", GUILayout.Width(KindColumnWidth));
                GUILayout.Label("대상 본 (바꾸면 직접 지정)");
            }

            foreach (BoneMatch match in analysis.Matches)
            {
                if (match.OutfitBone == null) continue; // 분석 뒤 삭제된 본
                bool needsAttention = match.Kind == MatchKind.HumanoidFallback || match.Kind == MatchKind.Unmatched
                    || match.Kind == MatchKind.Attachment;
                if (showOnlyAttention && !needsAttention) continue;
                DrawRow(match, onPick);
            }
        }

        static void DrawRow(BoneMatch match, Action<Transform, Transform> onPick)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(match.OutfitBone.name, match.Note), GUILayout.Width(BoneColumnWidth));
                Color previous = GUI.color;
                GUI.color = KindColor(match.Kind);
                EditorGUILayout.LabelField(KindLabel(match.Kind), GUILayout.Width(KindColumnWidth));
                GUI.color = previous;

                EditorGUI.BeginChangeCheck();
                var picked = (Transform)EditorGUILayout.ObjectField(match.TargetBone, typeof(Transform), true);
                if (EditorGUI.EndChangeCheck()) onPick(match.OutfitBone, picked);
            }
        }

        static string Summary(List<BoneMatch> matches)
        {
            int Count(MatchKind kind) => matches.Count(m => m.Kind == kind);
            return $"Humanoid {Count(MatchKind.Humanoid)} / 대체 {Count(MatchKind.HumanoidFallback)} / 이름 {Count(MatchKind.Name)}" +
                   $" / 소품 {Count(MatchKind.Attachment)} / 직접 {Count(MatchKind.Manual)} / 따라감 {Count(MatchKind.Unmatched)}";
        }

        static string KindLabel(MatchKind kind)
        {
            switch (kind)
            {
                case MatchKind.Humanoid: return "Humanoid";
                case MatchKind.HumanoidFallback: return "대체 연결";
                case MatchKind.Name: return "이름";
                case MatchKind.Manual: return "직접 지정";
                case MatchKind.Attachment: return "소품 부착";
                default: return "따라감";
            }
        }

        static Color KindColor(MatchKind kind)
        {
            switch (kind)
            {
                case MatchKind.HumanoidFallback: return new Color(1f, 0.8f, 0.3f);
                case MatchKind.Unmatched: return new Color(0.7f, 0.7f, 0.7f);
                case MatchKind.Manual: return new Color(0.5f, 0.8f, 1f);
                case MatchKind.Attachment: return new Color(0.6f, 1f, 0.6f);
                default: return Color.white;
            }
        }
    }
}
