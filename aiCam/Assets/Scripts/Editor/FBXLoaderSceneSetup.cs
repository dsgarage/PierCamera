using UnityEngine;
using UnityEditor;
using UnityEngine.UIElements;

namespace AICam.Editor
{
    /// <summary>
    /// FBXLoaderシーンのセットアップを自動化するEditor拡張
    /// </summary>
    public static class FBXLoaderSceneSetup
    {
        [MenuItem("AICam/Setup/Setup FBXLoader Scene")]
        public static void SetupFBXLoaderScene()
        {
            Debug.Log("[FBXLoaderSceneSetup] Starting scene setup...");

            // 1. UI_Document作成
            GameObject uiDocObj = SetupUIDocument();

            // 2. RuntimeManager作成
            GameObject runtimeMgrObj = SetupRuntimeManager();

            // 3. 参照を設定
            SetupReferences(uiDocObj, runtimeMgrObj);

            // シーンをダーティマーク
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            Debug.Log("[FBXLoaderSceneSetup] ✅ Scene setup complete!");
            EditorUtility.DisplayDialog("Setup Complete",
                "FBXLoader scene has been set up successfully!\n\nPress Play to test the UI.",
                "OK");
        }

        private static GameObject SetupUIDocument()
        {
            // 既存のUI_Documentを検索
            GameObject uiDocObj = GameObject.Find("UI_Document");

            if (uiDocObj == null)
            {
                uiDocObj = new GameObject("UI_Document");
                Debug.Log("[FBXLoaderSceneSetup] Created UI_Document GameObject");
            }
            else
            {
                Debug.Log("[FBXLoaderSceneSetup] UI_Document already exists");
            }

            // UIDocumentコンポーネント追加
            UIDocument uiDoc = uiDocObj.GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                uiDoc = uiDocObj.AddComponent<UIDocument>();
                Debug.Log("[FBXLoaderSceneSetup] Added UIDocument component");
            }

            // UXMLをロード
            string uxmlPath = "Assets/UI/RuntimeFBXLoaderWithFileBrowser/RuntimeFBXLoaderWithFileBrowser.uxml";
            VisualTreeAsset uxmlAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);

            if (uxmlAsset != null)
            {
                uiDoc.visualTreeAsset = uxmlAsset;
                Debug.Log($"[FBXLoaderSceneSetup] Set UXML: {uxmlPath}");
            }
            else
            {
                Debug.LogError($"[FBXLoaderSceneSetup] UXML not found: {uxmlPath}");
            }

            // PanelSettingsをロードまたは作成
            string panelSettingsPath = "Assets/UI/PanelSettings.asset";
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(panelSettingsPath);

            if (panelSettings == null)
            {
                // PanelSettingsを新規作成
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                panelSettings.referenceResolution = new Vector2Int(1920, 1080);
                panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
                panelSettings.match = 0.5f;

                AssetDatabase.CreateAsset(panelSettings, panelSettingsPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"[FBXLoaderSceneSetup] Created PanelSettings: {panelSettingsPath}");
            }

            uiDoc.panelSettings = panelSettings;

            // FileBrowserUIController追加
            var uiController = uiDocObj.GetComponent<AICam.FBXLoader.FileBrowserUIController>();
            if (uiController == null)
            {
                uiController = uiDocObj.AddComponent<AICam.FBXLoader.FileBrowserUIController>();
                Debug.Log("[FBXLoaderSceneSetup] Added FileBrowserUIController");
            }

            // UIDocumentへの参照を設定（SerializedObjectを使用）
            SerializedObject so = new SerializedObject(uiController);
            SerializedProperty uiDocProp = so.FindProperty("uiDocument");
            if (uiDocProp != null)
            {
                uiDocProp.objectReferenceValue = uiDoc;
                so.ApplyModifiedProperties();
                Debug.Log("[FBXLoaderSceneSetup] Set UIDocument reference in FileBrowserUIController");
            }

            EditorUtility.SetDirty(uiDocObj);
            return uiDocObj;
        }

        private static GameObject SetupRuntimeManager()
        {
            // 既存のRuntimeManagerを検索
            GameObject runtimeMgrObj = GameObject.Find("RuntimeManager");

            if (runtimeMgrObj == null)
            {
                runtimeMgrObj = new GameObject("RuntimeManager");
                Debug.Log("[FBXLoaderSceneSetup] Created RuntimeManager GameObject");
            }
            else
            {
                Debug.Log("[FBXLoaderSceneSetup] RuntimeManager already exists");
            }

            // FileBrowserController追加
            var fileBrowser = runtimeMgrObj.GetComponent<AICam.FBXLoader.FileBrowserController>();
            if (fileBrowser == null)
            {
                fileBrowser = runtimeMgrObj.AddComponent<AICam.FBXLoader.FileBrowserController>();
                Debug.Log("[FBXLoaderSceneSetup] Added FileBrowserController");
            }

            // RuntimeFBXLoaderBridge追加
            var loaderBridge = runtimeMgrObj.GetComponent<AICam.FBXLoader.RuntimeFBXLoaderBridge>();
            if (loaderBridge == null)
            {
                loaderBridge = runtimeMgrObj.AddComponent<AICam.FBXLoader.RuntimeFBXLoaderBridge>();
                Debug.Log("[FBXLoaderSceneSetup] Added RuntimeFBXLoaderBridge");
            }

            // RuntimeFBXLoaderBridgeの参照を設定
            SerializedObject so = new SerializedObject(loaderBridge);
            SerializedProperty browserProp = so.FindProperty("browser");
            if (browserProp != null)
            {
                browserProp.objectReferenceValue = fileBrowser;
                so.ApplyModifiedProperties();
                Debug.Log("[FBXLoaderSceneSetup] Set FileBrowserController reference in RuntimeFBXLoaderBridge");
            }

            EditorUtility.SetDirty(runtimeMgrObj);
            return runtimeMgrObj;
        }

        private static void SetupReferences(GameObject uiDocObj, GameObject runtimeMgrObj)
        {
            // すでに個別のセットアップ関数内で参照設定済み
            Debug.Log("[FBXLoaderSceneSetup] All references have been set");
        }

        [MenuItem("AICam/Setup/Setup FBXLoader Scene", true)]
        private static bool ValidateSetupFBXLoaderScene()
        {
            // FBXLoaderシーンが開かれているかチェック
            var activeScene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            return activeScene.name == "FBXLoader";
        }
    }
}
