using System.Collections.Generic;
using UnityEngine;

namespace OutfitGrader
{
    public enum Severity
    {
        /// <summary>실행을 막는 문제</summary>
        Error,
        /// <summary>실행은 되지만 결과가 어긋날 수 있음</summary>
        Warning,
        /// <summary>알아두면 좋은 동작 안내</summary>
        Info,
    }

    /// <summary>사용자에게 보여줄 진단 한 건: 무엇이 문제인지, 왜 생겼는지, 어떻게 고치는지</summary>
    public sealed class Diagnostic
    {
        public Severity Severity;
        public string Problem;
        public string Cause;
        public string Fix;

        public Diagnostic(Severity severity, string problem, string cause, string fix)
        {
            Severity = severity;
            Problem = problem;
            Cause = cause;
            Fix = fix;
        }
    }

    /// <summary>진단 규칙이 판단에 쓰는 사실 모음. 엔진 객체 없이 채울 수 있어 규칙을 단독 테스트할 수 있다.</summary>
    public sealed class FitFacts
    {
        public bool HasSource, HasOutfit, HasTarget;
        public bool SourceHumanoid, TargetHumanoid;
        public bool SourceInScene, TargetInScene;
        public bool SameAvatar, OutfitUnderTarget;
        /// <summary>대상 아바타 안에 같은 이름의 의상이 이미 있음(이전에 입힌 결과)</summary>
        public bool TargetAlreadyWearing;

        public int OutfitRendererCount;
        public int BrokenSkinnedCount;
        public int OutfitBoneCount;
        public int IdentifiedBySourceCount;
        public int UnmatchedCount;
        public int FallbackBoneCount;
        /// <summary>본 없는 소품의 부착 위치 설명 (예: "glasses: 가장 가까운 Head에 부착")</summary>
        public readonly List<string> PropNotes = new List<string>();
        /// <summary>대체 연결 요약 (예: "LeftHand에 15개")</summary>
        public readonly List<string> FallbackNotes = new List<string>();

        /// <summary>팔이 수평보다 아래로 내려간 각도. 잴 수 없으면 NaN.</summary>
        public float SourceArmDropDeg = float.NaN, TargetArmDropDeg = float.NaN;
        public float FacingDifferenceDeg;
        public readonly List<string> SourceShrinkKeys = new List<string>();
        public readonly List<string> TargetShrinkKeys = new List<string>();
        public int TargetMissingScripts;
        public bool HeadMeasured = true;
    }

    /// <summary>
    /// 실행 전 진단 규칙. 키세타 등 기존 도구에서 사용자가 내부 오류 문구만 보고 막혔던 상황을
    /// 실행 전에 찾아 한국어로 원인과 해결 방법을 알려준다.
    /// </summary>
    public static class FitDiagnostics
    {
        public const float APoseThresholdDeg = 20f;
        public const float FacingThresholdDeg = 10f;

        static readonly string[] ShrinkKeywords = { "shrink", "縮", "隠", "hide", "貫通", "kill" };

        public static List<Diagnostic> Evaluate(FitFacts f)
        {
            var list = new List<Diagnostic>();
            if (!f.HasSource || !f.HasOutfit || !f.HasTarget)
            {
                list.Add(new Diagnostic(Severity.Error, "입력이 비어 있습니다",
                    "원본 아바타, 의상, 대상 아바타가 모두 필요합니다.",
                    "세 칸을 모두 지정해 주세요. 의상은 원본 아바타에 입힌 상태로 지정합니다."));
                return list;
            }

            AddInputErrors(f, list);
            if (list.Count > 0) return list;

            AddOutfitErrors(f, list);
            AddPoseWarnings(f, list);
            AddScaleWarnings(f, list);
            AddInfo(f, list);
            return list;
        }

        public static bool HasErrors(IEnumerable<Diagnostic> diagnostics)
        {
            foreach (Diagnostic d in diagnostics)
            {
                if (d.Severity == Severity.Error) return true;
            }
            return false;
        }

        static void AddInputErrors(FitFacts f, List<Diagnostic> list)
        {
            const string humanoidFix = "모델 FBX의 Import Settings > Rig에서 Animation Type을 Humanoid로 바꾸고 Configure에서 본 지정을 확인해 주세요.";
            if (!f.SourceHumanoid)
                list.Add(new Diagnostic(Severity.Error, "원본 아바타가 Humanoid가 아닙니다",
                    "Animator가 없거나 Humanoid Avatar가 설정되지 않아 본을 식별할 수 없습니다.", humanoidFix));
            if (!f.TargetHumanoid)
                list.Add(new Diagnostic(Severity.Error, "대상 아바타가 Humanoid가 아닙니다",
                    "Animator가 없거나 Humanoid Avatar가 설정되지 않아 본을 식별할 수 없습니다.", humanoidFix));
            if (!f.SourceInScene || !f.TargetInScene)
                list.Add(new Diagnostic(Severity.Error, "아바타가 씬에 배치되어 있지 않습니다",
                    "Project 창의 프리팹 에셋을 직접 지정하면 본 위치를 읽을 수 없습니다.",
                    "아바타를 씬에 놓은 뒤 Hierarchy에서 끌어와 지정해 주세요."));
            if (f.SameAvatar)
                list.Add(new Diagnostic(Severity.Error, "원본과 대상이 같은 아바타입니다",
                    "같은 아바타끼리는 옮길 필요가 없습니다.", "대상 칸에 의상을 입힐 다른 아바타를 지정해 주세요."));
            if (f.OutfitUnderTarget)
                list.Add(new Diagnostic(Severity.Error, "의상이 이미 대상 아바타 안에 있습니다",
                    "결과를 만들 때 대상 아바타를 복제하므로 의상이 두 번 들어갑니다.",
                    "의상을 원본 아바타 아래나 씬 최상위로 옮겨 주세요."));
        }

        static void AddOutfitErrors(FitFacts f, List<Diagnostic> list)
        {
            if (f.OutfitRendererCount == 0)
                list.Add(new Diagnostic(Severity.Error, "의상에 메시가 없습니다",
                    "SkinnedMeshRenderer나 MeshRenderer를 찾지 못했습니다.",
                    "의상 프리팹의 최상위 오브젝트를 지정했는지 확인해 주세요."));
            if (f.BrokenSkinnedCount > 0)
                list.Add(new Diagnostic(Severity.Error, $"의상 메시 {f.BrokenSkinnedCount}개의 본 연결이 비어 있습니다",
                    "Armature 없이 메시만 복사했거나 의상의 본이 삭제되었습니다.",
                    "의상 프리팹을 Armature가 포함된 원래 상태로 다시 배치해 주세요."));
            if (f.OutfitBoneCount > 0 && f.IdentifiedBySourceCount == 0)
                list.Add(new Diagnostic(Severity.Error, "의상이 원본 아바타용이 아닌 것 같습니다",
                    "의상 본 이름이 원본 아바타의 본과 하나도 맞지 않습니다.",
                    "의상 상품 페이지의 대응 아바타를 원본 칸에 지정했는지 확인해 주세요."));
        }

        static void AddPoseWarnings(FitFacts f, List<Diagnostic> list)
        {
            if (f.TargetAlreadyWearing)
                list.Add(new Diagnostic(Severity.Warning, "대상 아바타에 같은 이름의 의상이 이미 입혀져 있습니다",
                    "실행하면 의상이 하나 더 들어가 겹쳐 보입니다.",
                    "다시 맞추려면 Ctrl+Z로 이전 결과를 되돌리거나 대상 아바타 안의 기존 의상을 지운 뒤 실행해 주세요."));
            AddArmWarning("원본", f.SourceArmDropDeg, list);
            AddArmWarning("대상", f.TargetArmDropDeg, list);
            if (f.FacingDifferenceDeg > FacingThresholdDeg)
                list.Add(new Diagnostic(Severity.Warning, $"두 아바타가 보는 방향이 {f.FacingDifferenceDeg:0}도 다릅니다",
                    "루트 회전 차이만큼은 보정하지만, 몸이 루트와 다른 방향을 보고 있으면 어긋납니다.",
                    "두 아바타 루트의 Rotation을 같게 맞춰 주세요."));
        }

        static void AddArmWarning(string who, float dropDeg, List<Diagnostic> list)
        {
            if (float.IsNaN(dropDeg) || dropDeg <= APoseThresholdDeg) return;
            list.Add(new Diagnostic(Severity.Warning, $"{who} 아바타가 A 자세로 보입니다 (팔이 {dropDeg:0}도 내려감)",
                "이 도구는 두 아바타가 T 자세라는 전제로 위치를 옮깁니다. 상의 소매가 어긋날 수 있습니다.",
                "Rig > Configure > Pose > Enforce T-Pose로 T 자세를 맞춘 뒤 실행해 주세요."));
        }

        static void AddScaleWarnings(FitFacts f, List<Diagnostic> list)
        {
            AddShrinkWarning("원본", f.SourceShrinkKeys, list);
            AddShrinkWarning("대상", f.TargetShrinkKeys, list);
            if (f.FallbackBoneCount > 0)
                list.Add(new Diagnostic(Severity.Warning, $"대상 아바타에 없는 본 {f.FallbackBoneCount}개를 가까운 부모 본에 연결합니다",
                    "대상 아바타에 해당 본이 없습니다: " + string.Join(", ", f.FallbackNotes),
                    "결과가 어긋나면 본 연결 표에서 대상 본을 직접 지정해 주세요."));
            if (!f.HeadMeasured)
                list.Add(new Diagnostic(Severity.Warning, "머리 크기를 잴 수 없어 몸 비율을 대신 씁니다",
                    "머리 본에 묶인 몸 메시 정점을 찾지 못했습니다.",
                    "결과 크기가 맞지 않으면 배율 조정 값을 바꿔 주세요."));
        }

        static void AddShrinkWarning(string who, List<string> keys, List<Diagnostic> list)
        {
            if (keys.Count == 0) return;
            list.Add(new Diagnostic(Severity.Info, $"{who} 아바타에 체형 축소 블렌드쉐이프가 켜져 있습니다",
                $"{string.Join(", ", keys)} 값으로 몸이 줄어 보입니다. 크기 측정에는 영향이 없지만 결과를 눈으로 확인할 때 소품이 떠 보일 수 있습니다.",
                "결과를 확인할 때는 해당 블렌드쉐이프 값을 0으로 바꿔 비교해 주세요."));
        }

        static void AddInfo(FitFacts f, List<Diagnostic> list)
        {
            if (f.PropNotes.Count > 0)
                list.Add(new Diagnostic(Severity.Info, $"본이 없는 소품 {f.PropNotes.Count}개를 아바타 본에 붙입니다",
                    string.Join(", ", f.PropNotes),
                    "부착 위치가 다르면 본 연결 표에서 대상 본을 직접 지정해 주세요."));
            if (f.UnmatchedCount > 0)
                list.Add(new Diagnostic(Severity.Info, $"의상 전용 본 {f.UnmatchedCount}개는 부모 본을 따라 움직입니다",
                    "치마·리본·꼬리처럼 원본 아바타에 없는 본입니다.",
                    "따로 할 일은 없습니다. 위치가 이상하면 표에서 직접 지정할 수 있습니다."));
            if (f.TargetMissingScripts > 0)
                list.Add(new Diagnostic(Severity.Info, $"대상 아바타에 비어 있는 스크립트 {f.TargetMissingScripts}개가 있습니다",
                    "VRChat SDK 같은 패키지가 없는 프로젝트에서 연 아바타입니다.",
                    "맞추기에는 영향이 없습니다. 프리팹 저장 시 저장본에서만 제거합니다."));
        }

        /// <summary>위팔에서 손까지 방향이 수평보다 아래로 내려간 각도(도). 위로 올라가면 음수.</summary>
        public static float ArmDropDegrees(Vector3 upperArm, Vector3 hand, Vector3 up)
        {
            Vector3 dir = hand - upperArm;
            if (dir.sqrMagnitude < 1e-8f) return float.NaN;
            return Mathf.Asin(Mathf.Clamp(-Vector3.Dot(dir.normalized, up.normalized), -1f, 1f)) * Mathf.Rad2Deg;
        }

        public static bool IsShrinkKey(string blendShapeName)
        {
            if (string.IsNullOrEmpty(blendShapeName)) return false;
            string lower = blendShapeName.ToLowerInvariant();
            foreach (string keyword in ShrinkKeywords)
            {
                if (lower.Contains(keyword)) return true;
            }
            return false;
        }
    }
}
