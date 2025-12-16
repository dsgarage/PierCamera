using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using AICam.FBXLoader;

namespace AICam.FBXLoader.Editor
{
    /// <summary>
    /// AvatarSlotManagerをシーンに自動追加するEditorスクリプト
    /// Issue #416: Play Mode再開時のアバター自動ロードに必須
    /// </summary>
    public static class AvatarSlotManagerSetup
    {
        private const string MENU_PATH = "Tools/AICam/Setup AvatarSlotManager in Scene";

        [MenuItem(MENU_PATH)]
        public static void SetupAvatarSlotManager()
        {
            // 既存のAvatarSlotManagerを検索
            var existing = Object.FindFirstObjectByType<AvatarSlotManager>();
            if (existing != null)
            {
                Debug.Log($"[AvatarSlotManagerSetup] AvatarSlotManager already exists on '{existing.gameObject.name}'");
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                return;
            }

            // AppMgrを探す（推奨される親オブジェクト）
            GameObject parent = GameObject.Find("AppMgr");

            if (parent != null)
            {
                // AppMgrに追加
                var manager = parent.AddComponent<AvatarSlotManager>();
                Debug.Log($"[AvatarSlotManagerSetup] ✅ Added AvatarSlotManager to 'AppMgr'");
                EditorUtility.SetDirty(parent);
            }
            else
            {
                // 新しいGameObjectを作成
                var go = new GameObject("AvatarSlotManager");
                var manager = go.AddComponent<AvatarSlotManager>();
                Debug.Log($"[AvatarSlotManagerSetup] ✅ Created new GameObject with AvatarSlotManager");
                EditorUtility.SetDirty(go);
            }

            // シーンを保存するか確認
            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                if (EditorUtility.DisplayDialog(
                    "Save Scene?",
                    "AvatarSlotManager has been added. Save the scene now?",
                    "Save", "Later"))
                {
                    EditorSceneManager.SaveOpenScenes();
                    Debug.Log("[AvatarSlotManagerSetup] Scene saved");
                }
            }
        }

        [MenuItem(MENU_PATH, true)]
        public static bool ValidateSetupAvatarSlotManager()
        {
            // Play Mode中は無効
            return !Application.isPlaying;
        }

        /// <summary>
        /// シーンロード時に自動チェック
        /// </summary>
        [InitializeOnLoadMethod]
        private static void CheckOnSceneLoad()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            // ARCamera_originシーンでのみチェック
            if (!scene.name.Contains("ARCamera")) return;

            var existing = Object.FindFirstObjectByType<AvatarSlotManager>();
            if (existing == null)
            {
                Debug.LogWarning($"[AvatarSlotManagerSetup] ⚠️ AvatarSlotManager not found in scene '{scene.name}'!");
                Debug.LogWarning("[AvatarSlotManagerSetup] Use menu: Tools > AICam > Setup AvatarSlotManager in Scene");
            }
        }
    }
}
