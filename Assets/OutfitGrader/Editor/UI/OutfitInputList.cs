using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OutfitGrader
{
    /// <summary>
    /// 창의 "의상" 입력 목록. 원본 아바타에 입힌(씬에 놓인) 의상만 여러 개 받는다.
    /// 프리팹 에셋은 놓인 위치가 없어 원본 몸 기준을 잡을 수 없으므로 받지 않는다.
    /// </summary>
    [Serializable]
    public sealed class OutfitInputList
    {
        // 창을 다시 열거나 스크립트가 다시 컴파일되어도 입력이 유지되도록 직렬화한다.
        [SerializeField] List<GameObject> inputs = new List<GameObject>();
        /// <summary>방금 프리팹 에셋을 넣으려 해서 거절했는지(안내 표시용)</summary>
        bool rejectedAsset;

        public bool HasAny => inputs.Any(o => o != null);

        /// <summary>목록을 그린다. 항목이 바뀌면 true.</summary>
        public bool Draw()
        {
            // 예전 버전에서 저장된 프리팹 에셋 항목은 위치 기준이 없어 결과가 조용히 어긋나므로 빼고 안내한다.
            bool hadAssets = inputs.RemoveAll(IsAsset) > 0;
            if (hadAssets) rejectedAsset = true;
            if (inputs.Count == 0) inputs.Add(null); // 빈 상태에서도 칸 하나는 보이게

            bool changed = hadAssets | DrawRows();
            if (GUILayout.Button(new GUIContent("+ 의상 칸 추가", "원본 아바타에 입힌(씬에 놓인) 의상")))
            {
                inputs.Add(null);
                changed = true;
            }
            changed |= DrawDropArea();
            if (rejectedAsset)
                EditorGUILayout.HelpBox("프리팹 에셋은 받지 않습니다. 의상 프리팹을 원본 아바타 안에 놓은 뒤 씬의 오브젝트를 지정해 주세요.", MessageType.Warning);
            return changed;
        }

        bool DrawRows()
        {
            bool changed = false;
            int removeIndex = -1;
            for (int i = 0; i < inputs.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    var picked = (GameObject)EditorGUILayout.ObjectField(new GUIContent($"의상 {i + 1}"), inputs[i], typeof(GameObject), true);
                    if (EditorGUI.EndChangeCheck()) changed |= TrySet(i, picked);
                    if (GUILayout.Button(new GUIContent("-", "이 의상 빼기"), GUILayout.Width(24f))) removeIndex = i;
                }
            }
            // 그리는 도중 목록을 바꾸면 IMGUI 배치 계산이 어긋나므로 반복이 끝난 뒤 지운다.
            if (removeIndex < 0) return changed;
            inputs.RemoveAt(removeIndex);
            rejectedAsset = false;
            return true;
        }

        /// <summary>씬 오브젝트(또는 빈 칸)만 받는다. 프리팹 에셋이면 값을 되돌리고 안내를 켠다.</summary>
        bool TrySet(int index, GameObject picked)
        {
            rejectedAsset = IsAsset(picked);
            if (rejectedAsset)
            {
                // 입력이 그대로이므로 창이 직접 지정한 연결을 초기화하지 않게 변경 표시를 되돌린다.
                GUI.changed = false;
                return false;
            }
            inputs[index] = picked;
            return true;
        }

        static bool IsAsset(GameObject o) => o != null && EditorUtility.IsPersistent(o);

        /// <summary>Hierarchy에서 여러 의상을 한 번에 끌어다 놓는 영역. 실제로 추가된 의상이 있을 때만 true.</summary>
        bool DrawDropArea()
        {
            Rect area = GUILayoutUtility.GetRect(0f, 32f, GUILayout.ExpandWidth(true));
            GUI.Box(area, "여기에 의상 여러 개를 한 번에 끌어다 놓기", EditorStyles.helpBox);
            Event e = Event.current;
            bool isDragOverArea = (e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && area.Contains(e.mousePosition);
            if (!isDragOverArea) return false;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type != EventType.DragPerform) return false;

            DragAndDrop.AcceptDrag();
            e.Use();
            GameObject[] dropped = DragAndDrop.objectReferences.OfType<GameObject>().ToArray();
            rejectedAsset = dropped.Any(IsAsset);
            GameObject[] added = dropped.Where(o => !IsAsset(o) && !inputs.Contains(o)).ToArray();
            if (added.Length == 0) return false;

            inputs.AddRange(added);
            inputs.RemoveAll(o => o == null);
            return true;
        }

        /// <summary>
        /// 분석에 넘길 의상 목록(빈 칸 제외).
        /// 같은 의상이 두 칸에 있거나 다른 칸 의상의 일부(자식)가 들어 있으면 빼서 두 번 입혀지지 않게 한다.
        /// </summary>
        public List<Transform> Resolve()
        {
            return WithoutOverlaps(inputs.Where(o => o != null && !IsAsset(o)).Select(o => o.transform).ToList());
        }

        /// <summary>같은 의상은 하나만, 다른 의상의 자식은 빼고 남긴다(null 항목은 그대로 둔다).</summary>
        public static List<Transform> WithoutOverlaps(IReadOnlyList<Transform> outfits)
        {
            var unique = new List<Transform>();
            foreach (Transform t in outfits)
            {
                bool isPartOfAnother = t != null && outfits.Any(other => other != null && other != t && t.IsChildOf(other));
                bool isRepeated = t != null && unique.Contains(t);
                if (isPartOfAnother || isRepeated) continue;
                unique.Add(t);
            }
            return unique;
        }
    }
}
