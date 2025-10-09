using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PoseGridLayout))]
public class PoseGridLayoutEditor : Editor
{
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

    void OnEnable()
    {
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
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

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
        EditorGUILayout.LabelField("Rebuild Policy", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(fullRebuildOnUpdate, new GUIContent("Full Rebuild on Update"));

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
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
}