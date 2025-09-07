#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // TMP用

[CustomEditor(typeof(FaceUIManager))]
public class FaceUIManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var ui = (FaceUIManager)target;

        EditorGUILayout.Space(12);
        EditorGUILayout.LabelField("Expression UI Generator", EditorStyles.boldLabel);

        if (GUILayout.Button("faceClips から表情UIを生成"))
        {
            BuildUI(ui);
        }
    }

    private void BuildUI(FaceUIManager ui)
    {
        if (!ui.content || !ui.buttonPrefab)
        {
            EditorUtility.DisplayDialog("HUDの配線不足",
                "FaceUIManager の content と buttonPrefab を割り当ててください。", "OK");
            return;
        }

        // FaceController を取得
        var fc = ui.editorTargetController;
        if (!fc)
        {
            fc = ui.GetComponentInParent<FaceController>();
            if (!fc) fc = Object.FindFirstObjectByType<FaceController>(FindObjectsInactive.Include);
        }
        if (!fc)
        {
            EditorUtility.DisplayDialog("FaceController が見つかりません",
                "FaceUIManager.editorTargetController にアバターの FaceController を割り当ててください。", "OK");
            return;
        }

        // 既存ボタン削除
        Undo.RegisterFullObjectHierarchyUndo(ui.content.gameObject, "Clear Expression Buttons");
        ui.ClearButtons();

        // faceClips を列挙
        int made = 0;
        var clipsField = typeof(FaceController).GetField("faceClips",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var clips = clipsField?.GetValue(fc) as AnimationClip[] ?? new AnimationClip[0];

        foreach (var clip in clips)
        {
            if (!clip) continue;
            CreateButton(ui, fc, clip.name);
            made++;
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(ui.content);
        EditorUtility.SetDirty(ui.content);

        if (ui.hudGroup)
            FaceUIManager.SetVisible(ui.hudGroup, made > 0);

        if (made == 0)
        {
            EditorUtility.DisplayDialog("生成対象なし",
                "FaceController の faceClips が未設定です。Clip を登録してください。", "OK");
        }
        else
        {
            Debug.Log($"[FaceUIManagerEditor] 表情ボタンを {made} 個生成しました。");
        }
    }

    private void CreateButton(FaceUIManager ui, FaceController fc, string faceName)
    {
        var btn = (Button)PrefabUtility.InstantiatePrefab(ui.buttonPrefab, ui.content);
        Undo.RegisterCreatedObjectUndo(btn.gameObject, "Create Expression Button");
        btn.name = $"Btn_{faceName}";

        // --- ラベル設定（フォントはPrefab側をそのまま使う） ---
        var ugui = btn.GetComponentInChildren<Text>(true);
        if (ugui)
        {
            ugui.text = faceName;
            ugui.fontStyle = FontStyle.Bold; // 常に太字
        }

        var tmp = btn.GetComponentInChildren<TMP_Text>(true);
        if (tmp)
        {
            tmp.text = faceName;
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.ForceMeshUpdate();
        }

        // --- ボタン動作設定 ---
        var act = btn.GetComponent<ButtonFaceAction>() ?? Undo.AddComponent<ButtonFaceAction>(btn.gameObject);
        act.controller = fc;
        act.faceName   = faceName;
    }
}
#endif