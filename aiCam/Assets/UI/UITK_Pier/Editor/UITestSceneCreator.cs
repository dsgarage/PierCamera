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
    /// </summary>
    public static class UITestSceneCreator
    {
        private const string TestScenePath = "Assets/UI/UITK_Pier/Scenes/UITestScene.unity";
        private const string UxmlPath = "Assets/UI/CameraCapture/CameraCaptureUI.uxml";
        private const string LightingUxmlPath = "Assets/UI/CameraCapture/LightingPanel.uxml";
        private const string PanelSettingsPath = "Assets/UI/CameraCapturePanelSetting.asset";

        [MenuItem("UITK_Pier/UI Test/Create UI Test Scene", false, 100)]
        public static void CreateUITestScene()
        {
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
                Debug.Log($"Loaded VisualTreeAsset: {UxmlPath}");
            }
            else
            {
                Debug.LogWarning($"VisualTreeAsset not found at: {UxmlPath}");
            }

            if (panelSettings != null)
            {
                uiDocument.panelSettings = panelSettings;
                Debug.Log($"Loaded PanelSettings: {PanelSettingsPath}");
            }
            else
            {
                Debug.LogWarning($"PanelSettings not found at: {PanelSettingsPath}");
            }

            // UIDebugCheckerを追加
            var debugChecker = uiDocumentGO.AddComponent<UIDebugChecker>();

            // UITestSceneSetupを追加
            var testSetup = uiDocumentGO.AddComponent<UITestSceneSetup>();

            // テスト用のダミーオブジェクトを作成
            CreateDummyObjects();

            // シーンを保存
            EditorSceneManager.SaveScene(scene, TestScenePath);

            Debug.Log($"UI Test Scene created at: {TestScenePath}");

            // Inspectorでフォーカス
            Selection.activeGameObject = uiDocumentGO;
        }

        [MenuItem("UITK_Pier/UI Test/Open UI Test Scene", false, 101)]
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

        [MenuItem("UITK_Pier/UI Test/Run UI Validation", false, 200)]
        public static void RunUIValidation()
        {
            var debugChecker = Object.FindFirstObjectByType<UIDebugChecker>();
            if (debugChecker != null)
            {
                debugChecker.RunAllChecks();
            }
            else
            {
                Debug.LogError("UIDebugChecker not found in scene. Open UI Test Scene first.");
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

    /// <summary>
    /// UIテスト用のEditorウィンドウ
    /// </summary>
    public class UITestWindow : EditorWindow
    {
        private UIDebugChecker debugChecker;
        private UITestSceneSetup testSetup;
        private Vector2 scrollPosition;
        private ValidationResult lastResult;

        [MenuItem("UITK_Pier/UI Test/Open Test Window", false, 102)]
        public static void ShowWindow()
        {
            var window = GetWindow<UITestWindow>("UI Test");
            window.minSize = new Vector2(300, 400);
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("UI Test Tools", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // シーン操作
            EditorGUILayout.LabelField("Scene", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Create Test Scene"))
            {
                UITestSceneCreator.CreateUITestScene();
            }
            if (GUILayout.Button("Open Test Scene"))
            {
                UITestSceneCreator.OpenUITestScene();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space();

            // コンポーネント検索
            FindComponents();

            // デバッグチェック
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            GUI.enabled = debugChecker != null;
            if (GUILayout.Button("Run All Checks"))
            {
                if (debugChecker != null)
                {
                    debugChecker.RunAllChecks();
                    lastResult = debugChecker.GetValidationResult();
                }
            }
            GUI.enabled = true;

            // 結果表示
            if (lastResult.TotalElements > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last Result", EditorStyles.boldLabel);

                var style = lastResult.IsValid ? EditorStyles.helpBox : EditorStyles.helpBox;
                var color = lastResult.IsValid ? Color.green : Color.red;

                var prevColor = GUI.backgroundColor;
                GUI.backgroundColor = color;
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUI.backgroundColor = prevColor;

                EditorGUILayout.LabelField($"Total: {lastResult.TotalElements}");
                EditorGUILayout.LabelField($"Found: {lastResult.FoundElements}");
                EditorGUILayout.LabelField($"Missing: {lastResult.MissingElements}");
                EditorGUILayout.LabelField($"Type Errors: {lastResult.TypeErrors?.Length ?? 0}");
                EditorGUILayout.LabelField($"Valid: {lastResult.IsValid}");

                EditorGUILayout.EndVertical();

                // 欠落要素の詳細
                if (lastResult.MissingElementIds != null && lastResult.MissingElementIds.Length > 0)
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.LabelField("Missing Elements:", EditorStyles.boldLabel);
                    foreach (var id in lastResult.MissingElementIds)
                    {
                        EditorGUILayout.LabelField($"  - {id}", EditorStyles.miniLabel);
                    }
                }
            }

            EditorGUILayout.Space();

            // 状態シミュレーション
            EditorGUILayout.LabelField("State Simulation", EditorStyles.boldLabel);
            GUI.enabled = testSetup != null;

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Default"))
            {
                testSetup?.ResetToDefault();
            }
            if (GUILayout.Button("Recording"))
            {
                testSetup?.SimulateRecording();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Show All Panels"))
            {
                testSetup?.ShowAllPanels();
            }
            if (GUILayout.Button("Cycle Aspect"))
            {
                testSetup?.CycleAspectRatio();
            }
            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;

            EditorGUILayout.Space();

            // ステータス
            EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"UIDebugChecker: {(debugChecker != null ? "Found" : "Not Found")}");
            EditorGUILayout.LabelField($"UITestSceneSetup: {(testSetup != null ? "Found" : "Not Found")}");

            EditorGUILayout.EndScrollView();
        }

        private void FindComponents()
        {
            if (debugChecker == null)
            {
                debugChecker = Object.FindFirstObjectByType<UIDebugChecker>();
            }
            if (testSetup == null)
            {
                testSetup = Object.FindFirstObjectByType<UITestSceneSetup>();
            }
        }

        private void OnFocus()
        {
            FindComponents();
        }
    }
}
#endif
