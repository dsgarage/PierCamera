using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Animations;

[CustomEditor(typeof(PoseGridLayout))]
public class PoseGridLayoutEditor : Editor
{
    SerializedProperty editorTargetAnimator;
    SerializedProperty contentRect;
    SerializedProperty slotPrefab;
    SerializedProperty items;
    SerializedProperty columns;
    SerializedProperty spacing;
    SerializedProperty padding;
    SerializedProperty squareCell;
    SerializedProperty minCellWidth;
    SerializedProperty autoScrollToTop;
    SerializedProperty fullRebuildOnUpdate;
    SerializedProperty targetLayerIndex;
    SerializedProperty crossFadeTime;

    void OnEnable()
    {
        editorTargetAnimator = serializedObject.FindProperty("editorTargetAnimator");
        contentRect         = serializedObject.FindProperty("contentRect");
        slotPrefab          = serializedObject.FindProperty("slotPrefab");
        items               = serializedObject.FindProperty("items");
        columns             = serializedObject.FindProperty("columns");
        spacing             = serializedObject.FindProperty("spacing");
        padding             = serializedObject.FindProperty("padding");
        squareCell          = serializedObject.FindProperty("squareCell");
        minCellWidth        = serializedObject.FindProperty("minCellWidth");
        autoScrollToTop     = serializedObject.FindProperty("autoScrollToTop");
        fullRebuildOnUpdate = serializedObject.FindProperty("fullRebuildOnUpdate");
        targetLayerIndex    = serializedObject.FindProperty("targetLayerIndex");
        crossFadeTime       = serializedObject.FindProperty("crossFadeTime");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.PropertyField(editorTargetAnimator, new GUIContent("Editor Target Animator"));
        EditorGUILayout.PropertyField(contentRect);
        EditorGUILayout.PropertyField(slotPrefab);

        EditorGUILayout.Space(6);
        if (items != null)
        {
            EditorGUILayout.PropertyField(items, new GUIContent("Items (Clip + Thumbnail + Name)"), true);
        }
        else
        {
            EditorGUILayout.HelpBox("`items` フィールドが見つかりません。PoseGridLayout に List<PoseItem> items を追加してください。", MessageType.Error);
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(columns,      new GUIContent("Columns (横数)"));
        EditorGUILayout.PropertyField(spacing,      new GUIContent("Spacing (間隔)"));
        EditorGUILayout.PropertyField(padding,      new GUIContent("Padding (余白)"));
        EditorGUILayout.PropertyField(squareCell,   new GUIContent("Square Cell"));
        EditorGUILayout.PropertyField(minCellWidth, new GUIContent("Min Cell Width"));
        EditorGUILayout.PropertyField(autoScrollToTop, new GUIContent("Auto Scroll To Top"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Animator Settings", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(targetLayerIndex, new GUIContent("Target Layer Index"));
        EditorGUILayout.PropertyField(crossFadeTime, new GUIContent("Cross Fade Time"));

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Rebuild Policy", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(fullRebuildOnUpdate, new GUIContent("Full Rebuild on Update"));

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("AnimatorからItemsを自動生成", GUILayout.Height(28)))
            {
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);

                var grid = (PoseGridLayout)target;
                var animator = grid.editorTargetAnimator;
                if (!animator)
                {
                    animator = grid.GetComponentInParent<Animator>();
                    if (!animator)
                        animator = Object.FindFirstObjectByType<Animator>(FindObjectsInactive.Include);
                }

                if (!animator || animator.runtimeAnimatorController == null)
                {
                    EditorUtility.DisplayDialog("Animator が見つかりません",
                        "PoseGridLayout.editorTargetAnimator に RuntimeAnimatorController が設定された Animator を割り当ててください。",
                        "OK");
                    return;
                }

                var rac = animator.runtimeAnimatorController;
                var entries = new List<(AnimationClip clip, string statePath)>();
                if (!TryCollectLayerClips(rac, grid.TargetLayerIndex, entries))
                {
                    EditorUtility.DisplayDialog("Animator レイヤーが見つかりません",
                        $"Animator Controller にレイヤー {grid.TargetLayerIndex} が存在しません。Animator を確認してください。",
                        "OK");
                    return;
                }

                var unique = entries
                    .Where(e => e.clip != null)
                    .GroupBy(e => e.clip.GetInstanceID())
                    .Select(g => g.First())
                    .OrderBy(e => e.clip.name, System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var newItems = new List<PoseItem>(unique.Count);
                foreach (var entry in unique)
                {
                    var item = new PoseItem
                    {
                        clip = entry.clip,
                        thumbnail = null,
                        displayNameOverride = string.Empty,
                        statePath = entry.statePath
                    };
                    newItems.Add(item);
                }

                Undo.RecordObject(grid, "Generate Pose Items from Animator");
                grid.editorTargetAnimator = animator;
                grid.SetItems(newItems, andRebuild: true);
                EditorUtility.SetDirty(grid);

                EditorUtility.DisplayDialog("完了",
                    $"Animator から {unique.Count} 個の AnimationClip を items に設定しました。",
                    "OK");

                serializedObject.Update();
            }

            if (GUILayout.Button("Update", GUILayout.Height(28)))
            {
                // これを最初に！ ここまでの Inspector 入力を実体へ反映
                serializedObject.ApplyModifiedProperties();
                GUI.FocusControl(null);

                foreach (var t in targets)
                {
                    var grid = (PoseGridLayout)t;
                    Undo.RecordObject(grid, "Update Pose Grid Layout");
                    grid.UpdateLayoutNow();          // ★ 内部で「全削除→新規生成→配置」を実行
                    EditorUtility.SetDirty(grid);
                }
            }

            if (GUILayout.Button("Ping Content", GUILayout.Height(28)))
            {
                var grid = (PoseGridLayout)target;
                if (grid != null)
                    EditorGUIUtility.PingObject(grid.gameObject);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private bool TryCollectLayerClips(RuntimeAnimatorController ctrl, int layerIndex, List<(AnimationClip clip, string statePath)> destination)
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

        void CollectFromMotion(Motion motion, string statePath)
        {
            if (motion == null) return;
            switch (motion)
            {
                case AnimationClip clip:
                    destination.Add((ResolveClip(clip), statePath));
                    break;
                case BlendTree blendTree:
                    foreach (var child in blendTree.children)
                        CollectFromMotion(child.motion, statePath);
                    break;
            }
        }

        void CollectFromStateMachine(AnimatorStateMachine stateMachine, string pathPrefix)
        {
            foreach (var childState in stateMachine.states)
            {
                string statePath = string.IsNullOrEmpty(pathPrefix)
                    ? childState.state.name
                    : $"{pathPrefix}.{childState.state.name}";
                CollectFromMotion(childState.state.motion, statePath);
            }

            foreach (var childMachine in stateMachine.stateMachines)
            {
                string childPath = string.IsNullOrEmpty(pathPrefix)
                    ? childMachine.stateMachine.name
                    : $"{pathPrefix}.{childMachine.stateMachine.name}";
                CollectFromStateMachine(childMachine.stateMachine, childPath);
            }
        }

        CollectFromStateMachine(baseController.layers[layerIndex].stateMachine, string.Empty);
        return true;
    }
}
