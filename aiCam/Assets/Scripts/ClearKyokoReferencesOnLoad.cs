using UnityEngine;

namespace AICam
{
    /// <summary>
    /// 起動時にKyoko関連の参照をクリアする
    /// </summary>
    public static class ClearKyokoReferencesOnLoad
    {
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void OnEditorLoad()
        {
            // Editorモードで起動時に実行
            UnityEditor.EditorApplication.delayCall += ClearReferences;
        }
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void OnRuntimeLoad()
        {
            // Runtimeで起動時に実行
            ClearReferences();
        }

        static void ClearReferences()
        {
            // PlaceAvatarOnPlaneOnlyのavatarPrefabをクリア
            var placer = Object.FindFirstObjectByType<PlaceAvatarOnPlaneOnly>();
            if (placer != null)
            {
#if UNITY_EDITOR
                var placerSO = new UnityEditor.SerializedObject(placer);
                var avatarPrefabProp = placerSO.FindProperty("avatarPrefab");
                if (avatarPrefabProp != null && avatarPrefabProp.objectReferenceValue != null)
                {
                    var prefabName = avatarPrefabProp.objectReferenceValue.name;
                    if (prefabName.ToLower().Contains("kyoko"))
                    {
                        Debug.Log($"[ClearKyokoReferences] Clearing avatarPrefab: {prefabName}");
                        avatarPrefabProp.objectReferenceValue = null;
                        placerSO.ApplyModifiedProperties();
                        UnityEditor.EditorUtility.SetDirty(placer.gameObject);
                    }
                }
#endif
            }

            // FaceUIManagerのeditorTargetControllerをクリア
            var faceUIManager = Object.FindFirstObjectByType<FaceUIManager>();
            if (faceUIManager != null)
            {
#if UNITY_EDITOR
                var faceUISO = new UnityEditor.SerializedObject(faceUIManager);
                var editorTargetProp = faceUISO.FindProperty("editorTargetController");
                if (editorTargetProp != null && editorTargetProp.objectReferenceValue != null)
                {
                    var controllerName = editorTargetProp.objectReferenceValue.name;
                    if (controllerName.ToLower().Contains("kyoko"))
                    {
                        Debug.Log($"[ClearKyokoReferences] Clearing editorTargetController: {controllerName}");
                        editorTargetProp.objectReferenceValue = null;
                        faceUISO.ApplyModifiedProperties();
                        UnityEditor.EditorUtility.SetDirty(faceUIManager.gameObject);
                    }
                }
#endif
            }

            // シーン内のKyoko GameObjectを検索
            var allObjects = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
            foreach (var t in allObjects)
            {
                if (t.gameObject.name.ToLower().Contains("kyoko"))
                {
                    Debug.Log($"[ClearKyokoReferences] Found Kyoko GameObject: {GetGameObjectPath(t.gameObject)}");
                    // Runtimeでは削除、Editorでは警告のみ
#if UNITY_EDITOR
                    if (Application.isPlaying)
                    {
                        Object.Destroy(t.gameObject);
                    }
                    else
                    {
                        Debug.LogWarning($"[ClearKyokoReferences] Kyoko GameObject found but not destroyed in Edit mode: {GetGameObjectPath(t.gameObject)}");
                    }
#else
                    Object.Destroy(t.gameObject);
#endif
                }
            }
        }

        static string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
