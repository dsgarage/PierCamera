using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;

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

                // Animator Controller のレイヤー1のみから AnimationClip を収集
                const int targetLayerIndex = 1;
                var rac = animator.runtimeAnimatorController;
                var clips = new List<AnimationClip>();

                bool TryCollectLayerClips(RuntimeAnimatorController ctrl, int layerIndex, List<AnimationClip> destination)
                {
                    if (ctrl == null) return false;

                    var overrideMap = new Dictionary<AnimationClip, AnimationClip>();
                    AnimatorController baseController = null;

                    void Traverse(RuntimeAnimatorController current)
                    {
                        switch (current)
                        {
                            case AnimatorOverrideController aoc:
                                Traverse(aoc.runtimeAnimatorController);
                                var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
                                aoc.GetOverrides(overrides);
                                foreach (var pair in overrides)
                                {
                                    if (pair.Value != null)
                                        overrideMap[pair.Key] = pair.Value;
                                    else
                                        overrideMap.Remove(pair.Key);
                                }
                                break;
                            case AnimatorController ac:
                                baseController = ac;
                                break;
                        }
                    }

                    Traverse(ctrl);

                    if (baseController == null) return false;
                    if (layerIndex < 0 || layerIndex >= baseController.layers.Length) return false;

                    AnimationClip ResolveClip(AnimationClip original)
                    {
                        if (original == null) return null;
                        if (overrideMap.TryGetValue(original, out var overridden) && overridden != null)
                            return overridden;
                        return original;
                    }

                    void CollectFromMotion(Motion motion)
                    {
                        if (motion == null) return;
                        switch (motion)
                        {
                            case AnimationClip clip:
                                var resolved = ResolveClip(clip);
                                if (resolved != null)
                                    destination.Add(resolved);
                                break;
                            case BlendTree blendTree:
                                foreach (var child in blendTree.children)
                                    CollectFromMotion(child.motion);
                                break;
                        }
                    }

                    void CollectFromStateMachine(AnimatorStateMachine stateMachine)
                    {
                        foreach (var childState in stateMachine.states)
                            CollectFromMotion(childState.state.motion);

                        foreach (var childMachine in stateMachine.stateMachines)
                            CollectFromStateMachine(childMachine.stateMachine);
                    }

                    CollectFromStateMachine(baseController.layers[layerIndex].stateMachine);
                    return true;
                }

                if (!TryCollectLayerClips(rac, targetLayerIndex, clips))
                {
                    EditorUtility.DisplayDialog("Animator レイヤーが見つかりません",
                        $"Animator Controller にレイヤー {targetLayerIndex} が存在しません。FaceController の Animator を確認してください。", "OK");
                    return;
                }

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