using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace UITK_Pier.Debug
{
    /// <summary>
    /// UIToolkit要素の検証とデバッグログ出力を行うクラス
    /// テストシーンで使用し、UI設定の正確性を確認する
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UIDebugChecker : MonoBehaviour
    {
        [Header("Check Settings")]
        [SerializeField] private bool checkOnStart = true;
        [SerializeField] private bool logToConsole = true;
        [SerializeField] private bool showOverlayPanel = true;

        [Header("Debug Panel")]
        [SerializeField] private string debugPanelId = "debugPanel";
        [SerializeField] private string debugLogId = "debugLog";

        private UIDocument uiDocument;
        private VisualElement root;
        private Label debugLogLabel;
        private StringBuilder logBuilder = new StringBuilder();

        // 検証結果
        private int totalElements = 0;
        private int foundElements = 0;
        private int missingElements = 0;
        private List<string> missingElementIds = new List<string>();
        private List<string> typeErrors = new List<string>();

        /// <summary>
        /// 必須UI要素のID一覧と期待される型
        /// </summary>
        private static readonly Dictionary<string, Type> RequiredElements = new Dictionary<string, Type>
        {
            // Capture Elements
            { "captureButton", typeof(VisualElement) },
            { "innerCircle", typeof(VisualElement) },
            { "progressRing", typeof(VisualElement) },
            { "progressArc", typeof(VisualElement) },
            { "flashOverlay", typeof(VisualElement) },
            { "galleryThumbnail", typeof(VisualElement) },

            // Panels
            { "topPanel", typeof(VisualElement) },
            { "sidePanel", typeof(VisualElement) },
            { "bottomPanel", typeof(VisualElement) },
            { "bottomButtonContainer", typeof(VisualElement) },

            // Top Buttons
            { "topButton1", typeof(Button) },
            { "topButton2", typeof(Button) },
            { "topButton3", typeof(Button) },
            { "topButton4", typeof(Button) },
            { "topButton5", typeof(Button) },

            // Side Buttons
            { "sideButton1", typeof(Button) },
            { "sideButton2", typeof(Button) },
            { "sideButton3", typeof(Button) },
            { "sideButtonBugReport", typeof(Button) },

            // Bottom Buttons
            { "bottomButtonAdd", typeof(Button) },

            // Alert Bar
            { "alertBar", typeof(VisualElement) },
            { "alertMessage", typeof(Label) },
            { "alertClose", typeof(Button) },

            // Viewer Overlay
            { "viewerOverlay", typeof(VisualElement) },
            { "viewerImage", typeof(Image) },

            // Icon Preview Panel
            { "iconPreviewPanel", typeof(VisualElement) },
            { "iconPreviewImage", typeof(VisualElement) },
            { "iconPreviewRetake", typeof(Button) },
            { "iconPreviewConfirm", typeof(Button) },

            // Aspect Masks
            { "topMask", typeof(VisualElement) },
            { "bottomMask", typeof(VisualElement) },
            { "leftMask", typeof(VisualElement) },
            { "rightMask", typeof(VisualElement) },

            // Lighting Panel
            { "lightingPanelOverlay", typeof(VisualElement) },
            { "lightingPanel", typeof(VisualElement) },
            { "lightingPanelClose", typeof(Button) },
            { "presetAuto", typeof(Button) },
            { "presetSunny", typeof(Button) },
            { "presetCloudy", typeof(Button) },
            { "presetIndoor", typeof(Button) },
            { "presetWarm", typeof(Button) },
            { "presetSunset", typeof(Button) },
            { "colorTempSlider", typeof(Slider) },
            { "colorTempValue", typeof(Label) },
            { "brightnessSlider", typeof(Slider) },
            { "brightnessValue", typeof(Label) },
            { "elevationSlider", typeof(Slider) },
            { "elevationValue", typeof(Label) },
            { "lightDirectionBackground", typeof(VisualElement) },
            { "lightDirectionKnob", typeof(VisualElement) },
            { "arSyncToggle", typeof(Toggle) },

            // Shadow Panel
            { "shadowPanelOverlay", typeof(VisualElement) },
            { "shadowPanel", typeof(VisualElement) },
            { "shadowPanelClose", typeof(Button) },
            { "shadowToggle", typeof(Toggle) },
            { "shadowIntensitySlider", typeof(Slider) },
            { "shadowIntensityValue", typeof(Label) },
            { "softHard", typeof(Button) },
            { "softMedium", typeof(Button) },
            { "softSoft", typeof(Button) },
        };

        /// <summary>
        /// 状態CSSクラスの一覧
        /// </summary>
        private static readonly string[] StateCssClasses = new string[]
        {
            "visible",
            "hidden",
            "recording",
            "active",
            "selected",
            "disabled",
            "preset-selected",
            "softness-selected",
            "loading",
            "error",
            "warning",
            "info"
        };

        private void Start()
        {
            uiDocument = GetComponent<UIDocument>();

            if (checkOnStart)
            {
                // UIDocumentの初期化を待つ
                StartCoroutine(WaitAndCheck());
            }
        }

        private System.Collections.IEnumerator WaitAndCheck()
        {
            // 1フレーム待ってUIの初期化を確実にする
            yield return null;

            RunAllChecks();
        }

        /// <summary>
        /// 全てのチェックを実行
        /// </summary>
        [ContextMenu("Run All Checks")]
        public void RunAllChecks()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            root = uiDocument.rootVisualElement;

            if (root == null)
            {
                LogError("Root VisualElement is null. UIDocument may not be properly configured.");
                return;
            }

            // 結果をリセット
            logBuilder.Clear();
            totalElements = 0;
            foundElements = 0;
            missingElements = 0;
            missingElementIds.Clear();
            typeErrors.Clear();

            LogHeader("UI Debug Checker - Validation Report");
            LogInfo($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            LogInfo($"UIDocument: {uiDocument.name}");
            LogInfo($"Visual Tree Asset: {(uiDocument.visualTreeAsset != null ? uiDocument.visualTreeAsset.name : "NULL")}");
            LogInfo($"Panel Settings: {(uiDocument.panelSettings != null ? uiDocument.panelSettings.name : "NULL")}");
            LogSeparator();

            // 1. 必須要素チェック
            CheckRequiredElements();

            // 2. 要素型チェック
            CheckElementTypes();

            // 3. PanelSettings チェック
            CheckPanelSettings();

            // 4. CSSクラス対応チェック
            CheckCssClassSupport();

            // 5. サマリー出力
            OutputSummary();

            // デバッグパネルに出力
            if (showOverlayPanel)
            {
                UpdateDebugPanel();
            }
        }

        private void CheckRequiredElements()
        {
            LogSection("Required Elements Check");

            foreach (var kvp in RequiredElements)
            {
                totalElements++;
                var element = root.Q(kvp.Key);

                if (element != null)
                {
                    foundElements++;
                    LogSuccess($"[OK] {kvp.Key}");
                }
                else
                {
                    missingElements++;
                    missingElementIds.Add(kvp.Key);
                    LogError($"[MISSING] {kvp.Key}");
                }
            }
        }

        private void CheckElementTypes()
        {
            LogSection("Element Type Check");

            foreach (var kvp in RequiredElements)
            {
                var element = root.Q(kvp.Key);
                if (element == null) continue;

                var expectedType = kvp.Value;
                var actualType = element.GetType();

                // 型が一致するか、派生クラスかをチェック
                if (expectedType.IsAssignableFrom(actualType))
                {
                    LogSuccess($"[OK] {kvp.Key}: {actualType.Name}");
                }
                else
                {
                    typeErrors.Add($"{kvp.Key}: Expected {expectedType.Name}, got {actualType.Name}");
                    LogWarning($"[TYPE MISMATCH] {kvp.Key}: Expected {expectedType.Name}, got {actualType.Name}");
                }
            }
        }

        private void CheckPanelSettings()
        {
            LogSection("PanelSettings Check");

            var panelSettings = uiDocument.panelSettings;

            if (panelSettings == null)
            {
                LogError("[MISSING] PanelSettings is not assigned");
                return;
            }

            LogSuccess($"[OK] PanelSettings: {panelSettings.name}");

            // 参照解像度チェック
            var refRes = panelSettings.referenceResolution;
            LogInfo($"  Reference Resolution: {refRes.x}x{refRes.y}");

            // スケールモードチェック
            LogInfo($"  Scale Mode: {panelSettings.scaleMode}");

            // スクリーンマッチモードチェック
            LogInfo($"  Screen Match Mode: {panelSettings.screenMatchMode}");

            // 期待値との比較
            if (refRes.x == 1920 && refRes.y == 1080)
            {
                LogSuccess("  [OK] Reference resolution matches expected (1920x1080)");
            }
            else if (refRes.x == 1200 && refRes.y == 800)
            {
                LogInfo("  [INFO] Using legacy resolution (1200x800)");
            }
            else
            {
                LogWarning($"  [WARN] Unexpected reference resolution: {refRes.x}x{refRes.y}");
            }
        }

        private void CheckCssClassSupport()
        {
            LogSection("CSS State Classes Check");

            // 各状態クラスがUSSで定義されているかを確認するには
            // 実際に要素に適用して確認する必要があるため、
            // ここでは存在確認のみを記録

            LogInfo("Expected state classes:");
            foreach (var cssClass in StateCssClasses)
            {
                LogInfo($"  .{cssClass}");
            }

            // サンプル要素で.visibleクラスのテスト
            var testElement = root.Q("lightingPanelOverlay");
            if (testElement != null)
            {
                var hadVisible = testElement.ClassListContains("visible");
                testElement.AddToClassList("visible");
                var hasVisible = testElement.ClassListContains("visible");

                if (!hadVisible)
                {
                    testElement.RemoveFromClassList("visible");
                }

                if (hasVisible)
                {
                    LogSuccess("[OK] CSS class toggle works correctly");
                }
                else
                {
                    LogError("[ERROR] CSS class toggle failed");
                }
            }
        }

        private void OutputSummary()
        {
            LogSeparator();
            LogHeader("Summary");

            float percentage = totalElements > 0 ? (float)foundElements / totalElements * 100f : 0f;

            LogInfo($"Total Required Elements: {totalElements}");
            LogInfo($"Found: {foundElements}");
            LogInfo($"Missing: {missingElements}");
            LogInfo($"Coverage: {percentage:F1}%");

            if (missingElementIds.Count > 0)
            {
                LogSection("Missing Element IDs");
                foreach (var id in missingElementIds)
                {
                    LogError($"  - {id}");
                }
            }

            if (typeErrors.Count > 0)
            {
                LogSection("Type Errors");
                foreach (var error in typeErrors)
                {
                    LogWarning($"  - {error}");
                }
            }

            LogSeparator();

            if (missingElements == 0 && typeErrors.Count == 0)
            {
                LogSuccess("All UI elements are correctly configured!");
            }
            else
            {
                LogError($"UI validation failed: {missingElements} missing, {typeErrors.Count} type errors");
            }
        }

        private void UpdateDebugPanel()
        {
            debugLogLabel = root.Q<Label>(debugLogId);
            if (debugLogLabel != null)
            {
                debugLogLabel.text = logBuilder.ToString();
            }
        }

        #region Logging Methods

        private void LogHeader(string message)
        {
            var line = $"{'='.ToString().PadRight(50, '=')}";
            Log(line);
            Log($"  {message}");
            Log(line);
        }

        private void LogSection(string message)
        {
            Log("");
            Log($"--- {message} ---");
        }

        private void LogSeparator()
        {
            Log(new string('-', 50));
        }

        private void LogSuccess(string message)
        {
            Log($"[OK] {message}");
            if (logToConsole) UnityEngine.Debug.Log($"<color=green>{message}</color>");
        }

        private void LogError(string message)
        {
            Log($"[ERROR] {message}");
            if (logToConsole) UnityEngine.Debug.LogError(message);
        }

        private void LogWarning(string message)
        {
            Log($"[WARN] {message}");
            if (logToConsole) UnityEngine.Debug.LogWarning(message);
        }

        private void LogInfo(string message)
        {
            Log(message);
            if (logToConsole) UnityEngine.Debug.Log(message);
        }

        private void Log(string message)
        {
            logBuilder.AppendLine(message);
        }

        #endregion

        #region Public API

        /// <summary>
        /// 検証結果を取得
        /// </summary>
        public ValidationResult GetValidationResult()
        {
            return new ValidationResult
            {
                TotalElements = totalElements,
                FoundElements = foundElements,
                MissingElements = missingElements,
                MissingElementIds = missingElementIds.ToArray(),
                TypeErrors = typeErrors.ToArray(),
                IsValid = missingElements == 0 && typeErrors.Count == 0
            };
        }

        /// <summary>
        /// 特定要素の存在確認
        /// </summary>
        public bool HasElement(string elementId)
        {
            if (root == null)
            {
                root = uiDocument?.rootVisualElement;
            }
            return root?.Q(elementId) != null;
        }

        /// <summary>
        /// 特定要素を型指定で取得
        /// </summary>
        public T GetElement<T>(string elementId) where T : VisualElement
        {
            if (root == null)
            {
                root = uiDocument?.rootVisualElement;
            }
            return root?.Q<T>(elementId);
        }

        #endregion
    }

    /// <summary>
    /// 検証結果を保持する構造体
    /// </summary>
    [Serializable]
    public struct ValidationResult
    {
        public int TotalElements;
        public int FoundElements;
        public int MissingElements;
        public string[] MissingElementIds;
        public string[] TypeErrors;
        public bool IsValid;

        public override string ToString()
        {
            return $"ValidationResult: {FoundElements}/{TotalElements} elements found, Valid={IsValid}";
        }
    }
}
