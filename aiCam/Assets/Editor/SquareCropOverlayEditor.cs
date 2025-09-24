#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SquareCropOverlay))]
[CanEditMultipleObjects]
public class SquareCropOverlayEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(8);
        EditorGUILayout.HelpBox(
            "このオーバーレイは『手動更新のみ』です。レイアウトがずれたら下のボタンを押して成形してください。\n撮影時は ARPhotoController.uiToHide により自動で非表示になり、写真には写りません。",
            MessageType.Info
        );

        if (GUILayout.Button("Crop UIを更新（成形）", GUILayout.Height(28)))
        {
            foreach (var t in targets)
            {
                var ov = t as SquareCropOverlay;
                if (!ov) continue;

                // Undo 対応（マスク/枠の RectTransform 変更を巻き戻せるように）
                var list = new System.Collections.Generic.List<Object>();
                list.Add(ov.transform);
                if (ov.topMask)    list.Add(ov.topMask);
                if (ov.bottomMask) list.Add(ov.bottomMask);
                if (ov.leftMask)   list.Add(ov.leftMask);
                if (ov.rightMask)  list.Add(ov.rightMask);
                if (ov.frame)      list.Add(ov.frame);
                Undo.RecordObjects(list.ToArray(), "SquareCropOverlay Apply");

                ov.RefreshNow();
            }
        }

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("photo を自動割り当て", GUILayout.Height(22)))
            {
                foreach (var t in targets)
                {
                    var ov = t as SquareCropOverlay;
                    if (!ov || ov.photo) continue;

                    // シーン内の ARPhotoController を検索して最初の1つを割り当て
                    var photos = Object.FindObjectsByType<ARPhotoController>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (photos != null && photos.Length > 0)
                    {
                        Undo.RecordObject(ov, "Assign ARPhotoController");
                        ov.photo = photos[0];
                        EditorUtility.SetDirty(ov);
                    }
                }
            }

            if (GUILayout.Button("マスクの Raycast OFF", GUILayout.Height(22)))
            {
                foreach (var t in targets)
                {
                    var ov = t as SquareCropOverlay;
                    if (!ov) continue;

                    void OffRT(RectTransform rt)
                    {
                        if (!rt) return;
                        var img = rt.GetComponent<UnityEngine.UI.Image>();
                        if (img && img.raycastTarget)
                        {
                            Undo.RecordObject(img, "Disable Raycast Target");
                            img.raycastTarget = false;
                            EditorUtility.SetDirty(img);
                        }
                    }
                    OffRT(ov.topMask);
                    OffRT(ov.bottomMask);
                    OffRT(ov.leftMask);
                    OffRT(ov.rightMask);
                    OffRT(ov.frame);
                }
            }
        }
    }

    // 便利コマンド：現在開いているシーン内のすべてのオーバーレイを一括更新
    [MenuItem("Tools/Square Crop/Refresh All Overlays in Scene")]
    private static void RefreshAllInScene()
    {
        var overlays = Object.FindObjectsByType<SquareCropOverlay>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (overlays == null || overlays.Length == 0)
        {
            EditorUtility.DisplayDialog("Square Crop", "オーバーレイが見つかりませんでした。", "OK");
            return;
        }
        Undo.IncrementCurrentGroup();
        foreach (var ov in overlays)
        {
            if (!ov) continue;
            Undo.RecordObject(ov.transform, "SquareCropOverlay Apply (All)");
            ov.RefreshNow();
        }
        EditorUtility.DisplayDialog("Square Crop", $"{overlays.Length} 個を更新しました。", "OK");
    }
}
#endif