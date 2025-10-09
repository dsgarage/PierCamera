using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

[CustomEditor(typeof(ExpressionGridLayout))]
public class ExpressionGridLayoutEditor : Editor
{
    SerializedProperty editorTargetController;
    SerializedProperty contentRect;
    SerializedProperty slotPrefab;
    SerializedProperty items;
    SerializedProperty columns;
    SerializedProperty spacing;
    SerializedProperty padding;
    SerializedProperty squareCell;
    SerializedProperty minCellWidth;
    SerializedProperty autoScrollToTop;

    void OnEnable()
    {
        editorTargetController  = serializedObject.FindProperty("editorTargetController");
        contentRect             = serializedObject.FindProperty("contentRect");
        slotPrefab              = serializedObject.FindProperty("slotPrefab");
        items                   = serializedObject.FindProperty("items");
        columns                 = serializedObject.FindProperty("columns");
        spacing                 = serializedObject.FindProperty("spacing");
        padding                 = serializedObject.FindProperty("padding");
        squareCell              = serializedObject.FindProperty("squareCell");
        minCellWidth            = serializedObject.FindProperty("minCellWidth");
        autoScrollToTop         = serializedObject.FindProperty("autoScrollToTop");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(editorTargetController);
        EditorGUILayout.PropertyField(contentRect);
        EditorGUILayout.PropertyField(slotPrefab);

        EditorGUILayout.Space(6);
        if (items != null)
        {
            EditorGUILayout.PropertyField(items, new GUIContent("Items (Clip + Thumbnail + Name)"), true);
        }
        else
        {
            EditorGUILayout.HelpBox("`items` フィールドが見つかりません。ExpressionGridLayout に List<ExpressionItem> items を追加してください。", MessageType.Error);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(columns, new GUIContent("Columns (横数)"));
        EditorGUILayout.PropertyField(spacing, new GUIContent("Spacing (間隔)"));
        EditorGUILayout.PropertyField(padding, new GUIContent("Padding (余白)"));
        EditorGUILayout.PropertyField(squareCell, new GUIContent("Square Cell"));
        EditorGUILayout.PropertyField(minCellWidth, new GUIContent("Min Cell Width"));
        EditorGUILayout.PropertyField(autoScrollToTop, new GUIContent("Auto Scroll To Top"));

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("AnimatorからItemsを自動生成"))
            {
                // ここまでの Inspector 入力をいったん適用
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);

                var grid = (ExpressionGridLayout)target;

                // FaceController の取得（明示指定 → 親から → シーンから）
                var faceCtl = grid.editorTargetController;
                if (!faceCtl)
                {
                    faceCtl = grid.GetComponentInParent<FaceController>();
                    if (!faceCtl) faceCtl = Object.FindFirstObjectByType<FaceController>(FindObjectsInactive.Include);
                }
                if (!faceCtl)
                {
                    EditorUtility.DisplayDialog("FaceController が見つかりません",
                        "ExpressionGridLayout.editorTargetController にアバターの FaceController を割り当ててください。", "OK");
                    return;
                }

                // Animator を探す（FaceController 直下優先→子孫）
                var animator = faceCtl.GetComponent<Animator>();
                if (!animator) animator = faceCtl.GetComponentInChildren<Animator>(true);
                if (!animator || animator.runtimeAnimatorController == null)
                {
                    EditorUtility.DisplayDialog("Animator が見つかりません",
                        "FaceController に Animator（RuntimeAnimatorController 設定済み）をアタッチしてください。", "OK");
                    return;
                }

                // RuntimeAnimatorController から全 AnimationClip を収集（OverrideController も展開）
                var rac = animator.runtimeAnimatorController;
                var clips = new List<AnimationClip>();

                void CollectClips(RuntimeAnimatorController ctrl)
                {
                    if (ctrl == null) return;
                    if (ctrl is AnimatorOverrideController aoc)
                    {
                        // 元コントローラを辿る
                        CollectClips(aoc.runtimeAnimatorController);
                        // 上書き後のClipも追加
                        clips.AddRange(aoc.animationClips.Where(c => c));
                    }
                    else
                    {
                        clips.AddRange(ctrl.animationClips.Where(c => c));
                    }
                }
                CollectClips(rac);

                // 重複除去（同一インスタンス）＆名前でソート
                var unique = clips
                    .Where(c => c != null)
                    .GroupBy(c => c.GetInstanceID())
                    .Select(g => g.First())
                    .OrderBy(c => c.name, System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

                // ExpressionItem リスト化
                var newItems = new List<ExpressionItem>(unique.Count);
                foreach (var c in unique)
                {
                    var item = new ExpressionItem
                    {
                        clip = c,
                        thumbnail = null,          // 必要なら後で自動生成ロジックを追加
                        displayNameOverride = ""   // clip.name をUI側で既定表示
                    };
                    newItems.Add(item);
                }

                // 反映（Undo対応）
                Undo.RecordObject(grid, "Generate Items from Animator");
                grid.SetItems(newItems, andRebuild: true);
                EditorUtility.SetDirty(grid);

                EditorUtility.DisplayDialog("完了",
                    $"Animator から {unique.Count} 個の AnimationClip を items に設定しました。", "OK");
            }

            if (GUILayout.Button("Update", GUILayout.Height(28)))
            {
                // まず Inspector 変更を反映
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);
            
                foreach (var t in targets)
                {
                    var grid = (ExpressionGridLayout)t;
                    Undo.RecordObject(grid, "Rebuild Expression Grid Layout");
            
                    // ★ ここがポイント：いったん全スロット削除してから再構築
                    grid.RebuildLayoutFromScratch();
            
                    EditorUtility.SetDirty(grid);
                }
            }

            if (GUILayout.Button("Ping Content", GUILayout.Height(28)))
            {
                var grid = (ExpressionGridLayout)target;
                if (grid != null)
                    EditorGUIUtility.PingObject(grid.gameObject);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }
}