#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor;
using UnityEditor.SceneManagement;
using UITK_Pier.Debug;

namespace UITK_Pier.Editor
{
    /// <summary>
    /// UIテストシーンを作成・管理するEditorスクリプト
    /// スクリプトから呼び出して使用する
    /// </summary>
    public static class UITestSceneCreator
    {
        public const string TestScenePath = "Assets/UITK_Pier/Scenes/UITestScene.unity";
        public const string UxmlPath = "Assets/UI/CameraCapture/CameraCaptureUI.uxml";
        public const string LightingUxmlPath = "Assets/UI/CameraCapture/LightingPanel.uxml";
        public const string PanelSettingsPath = "Assets/UI/CameraCapturePanelSetting.asset";

        /// <summary>
        /// UIテストシーンを作成
        /// </summary>
        public static void CreateUITestScene()
        {
            // Scenesフォルダが存在しない場合は作成
            var scenesFolder = System.IO.Path.GetDirectoryName(TestScenePath);
            if (!System.IO.Directory.Exists(scenesFolder))
            {
                System.IO.Directory.CreateDirectory(scenesFolder);
                AssetDatabase.Refresh();
            }

            // 新しいシーンを作成
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // メインカメラの設定
            var mainCamera = GameObject.Find("Main Camera");
            if (mainCamera != null)
            {
                mainCamera.transform.position = new Vector3(0, 1, -5);
                mainCamera.GetComponent<Camera>().backgroundColor = new Color(0.2f, 0.2f, 0.2f);
            }

            // UIDocument GameObjectを作成
            var uiDocumentGO = new GameObject("UIDocument_Test");

            // UIDocumentコンポーネントを追加
            var uiDocument = uiDocumentGO.AddComponent<UIDocument>();

            // アセットを読み込んで設定
            var visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            var panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);

            if (visualTreeAsset != null)
            {
                uiDocument.visualTreeAsset = visualTreeAsset;
                UnityEngine.Debug.Log($"Loaded VisualTreeAsset: {UxmlPath}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"VisualTreeAsset not found at: {UxmlPath}");
            }

            if (panelSettings != null)
            {
                uiDocument.panelSettings = panelSettings;
                UnityEngine.Debug.Log($"Loaded PanelSettings: {PanelSettingsPath}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"PanelSettings not found at: {PanelSettingsPath}");
            }

            // UIDebugCheckerを追加
            uiDocumentGO.AddComponent<UIDebugChecker>();

            // UITestSceneSetupを追加
            uiDocumentGO.AddComponent<UITestSceneSetup>();

            // テスト用のダミーオブジェクトを作成
            CreateDummyObjects();

            // シーンを保存
            EditorSceneManager.SaveScene(scene, TestScenePath);

            UnityEngine.Debug.Log($"UI Test Scene created at: {TestScenePath}");

            // Inspectorでフォーカス
            Selection.activeGameObject = uiDocumentGO;
        }

        /// <summary>
        /// UIテストシーンを開く
        /// </summary>
        public static void OpenUITestScene()
        {
            if (System.IO.File.Exists(TestScenePath))
            {
                EditorSceneManager.OpenScene(TestScenePath);
            }
            else
            {
                if (EditorUtility.DisplayDialog("UI Test Scene Not Found",
                    "UI Test Scene does not exist. Create it now?", "Create", "Cancel"))
                {
                    CreateUITestScene();
                }
            }
        }

        /// <summary>
        /// UIバリデーションを実行
        /// </summary>
        public static ValidationResult RunUIValidation()
        {
            var debugChecker = Object.FindFirstObjectByType<UIDebugChecker>();
            if (debugChecker != null)
            {
                debugChecker.RunAllChecks();
                return debugChecker.GetValidationResult();
            }
            else
            {
                UnityEngine.Debug.LogError("UIDebugChecker not found in scene. Open UI Test Scene first.");
                return default;
            }
        }

        private static void CreateDummyObjects()
        {
            // テスト用のダミーアバターオブジェクト
            var dummyAvatar = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            dummyAvatar.name = "DummyAvatar";
            dummyAvatar.transform.position = new Vector3(0, 1, 0);

            // ダミーライト
            var lightGO = new GameObject("DirectionalLight_Test");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50, -30, 0);

            // グラウンドプレーン
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(2, 1, 2);
        }
    }
}
#endif
