using UnityEngine;
using UnityEngine.UIElements;

namespace ARCamera.UI.Debug
{
    /// <summary>
    /// UIテストシーンのセットアップと状態再現を行うクラス
    /// Inspectorから各種状態をシミュレートできる
    /// </summary>
    public class UITestSceneSetup : MonoBehaviour
    {
        [Header("Required References")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PanelSettings panelSettings;
        [SerializeField] private VisualTreeAsset mainUxml;
        [SerializeField] private VisualTreeAsset lightingPanelUxml;

        [Header("Test State Simulation")]
        [SerializeField] private TestState currentState = TestState.Default;

        [Header("Panel Visibility")]
        [SerializeField] private bool showLightingPanel = false;
        [SerializeField] private bool showShadowPanel = false;
        [SerializeField] private bool showViewerOverlay = false;
        [SerializeField] private bool showIconPreview = false;
        [SerializeField] private bool showAlertBar = false;

        [Header("Capture State")]
        [SerializeField] private bool isRecording = false;
        [SerializeField] [Range(0f, 1f)] private float recordingProgress = 0f;

        [Header("Aspect Ratio")]
        [SerializeField] private AspectRatioMode aspectRatio = AspectRatioMode.Full;

        [Header("Alert Settings")]
        [SerializeField] private AlertType alertType = AlertType.Info;
        [SerializeField] private string alertMessage = "Test alert message";

        [Header("Debug")]
        [SerializeField] private UIDebugChecker debugChecker;
        [SerializeField] private bool autoValidateOnChange = true;

        private VisualElement root;

        public enum TestState
        {
            Default,
            Recording,
            Capturing,
            PreviewingPhoto,
            PreviewingIcon,
            LightingAdjustment,
            ShadowAdjustment,
            Alert
        }

        public enum AspectRatioMode
        {
            Full,
            Ratio16_9,
            Ratio3_2,
            Ratio1_1
        }

        public enum AlertType
        {
            Info,
            Warning,
            Error
        }

        private void Start()
        {
            InitializeScene();
        }

        private void OnValidate()
        {
            if (Application.isPlaying && root != null && autoValidateOnChange)
            {
                ApplyCurrentState();
            }
        }

        /// <summary>
        /// シーンを初期化
        /// </summary>
        [ContextMenu("Initialize Scene")]
        public void InitializeScene()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            if (uiDocument == null)
            {
                UnityEngine.Debug.LogError("UIDocument not found!");
                return;
            }

            root = uiDocument.rootVisualElement;

            if (root == null)
            {
                UnityEngine.Debug.LogError("Root VisualElement is null!");
                return;
            }

            // デフォルト状態を適用
            ApplyCurrentState();

            UnityEngine.Debug.Log("UITestSceneSetup initialized");
        }

        /// <summary>
        /// 現在の設定状態を適用
        /// </summary>
        [ContextMenu("Apply Current State")]
        public void ApplyCurrentState()
        {
            if (root == null) return;

            // パネル表示状態
            SetPanelVisibility("lightingPanelOverlay", showLightingPanel);
            SetPanelVisibility("shadowPanelOverlay", showShadowPanel);
            SetPanelVisibility("viewerOverlay", showViewerOverlay);
            SetPanelVisibility("iconPreviewPanel", showIconPreview);
            SetPanelVisibility("alertBar", showAlertBar);

            // 録画状態
            ApplyRecordingState();

            // アスペクト比
            ApplyAspectRatio();

            // アラート設定
            if (showAlertBar)
            {
                ApplyAlertSettings();
            }

            // テスト状態に応じた追加設定
            ApplyTestState();

            UnityEngine.Debug.Log($"Applied state: {currentState}");
        }

        private void SetPanelVisibility(string elementId, bool visible)
        {
            var element = root.Q(elementId);
            if (element == null) return;

            if (visible)
            {
                element.AddToClassList("visible");
                element.style.display = DisplayStyle.Flex;
            }
            else
            {
                element.RemoveFromClassList("visible");
                element.style.display = DisplayStyle.None;
            }
        }

        private void ApplyRecordingState()
        {
            var innerCircle = root.Q("innerCircle");
            var progressRing = root.Q("progressRing");
            var progressArc = root.Q("progressArc");

            if (innerCircle != null)
            {
                if (isRecording)
                {
                    innerCircle.AddToClassList("recording");
                }
                else
                {
                    innerCircle.RemoveFromClassList("recording");
                }
            }

            if (progressRing != null)
            {
                if (isRecording)
                {
                    progressRing.AddToClassList("active");
                    progressRing.style.display = DisplayStyle.Flex;
                }
                else
                {
                    progressRing.RemoveFromClassList("active");
                    progressRing.style.display = DisplayStyle.None;
                }
            }

            // プログレスバーの更新（簡易版）
            if (progressArc != null && isRecording)
            {
                // 実際のプログレス表示はCircularProgressElementで行う
                // ここでは opacity で簡易表現
                progressArc.style.opacity = recordingProgress;
            }
        }

        private void ApplyAspectRatio()
        {
            var topMask = root.Q("topMask");
            var bottomMask = root.Q("bottomMask");
            var leftMask = root.Q("leftMask");
            var rightMask = root.Q("rightMask");

            // 全マスクを非表示にリセット
            HideMask(topMask);
            HideMask(bottomMask);
            HideMask(leftMask);
            HideMask(rightMask);

            switch (aspectRatio)
            {
                case AspectRatioMode.Full:
                    // マスクなし
                    break;

                case AspectRatioMode.Ratio16_9:
                    ShowMask(topMask, "10%");
                    ShowMask(bottomMask, "10%");
                    break;

                case AspectRatioMode.Ratio3_2:
                    ShowMask(topMask, "8%");
                    ShowMask(bottomMask, "8%");
                    break;

                case AspectRatioMode.Ratio1_1:
                    ShowMask(leftMask, "15%");
                    ShowMask(rightMask, "15%");
                    break;
            }
        }

        private void HideMask(VisualElement mask)
        {
            if (mask == null) return;
            mask.style.display = DisplayStyle.None;
        }

        private void ShowMask(VisualElement mask, string size)
        {
            if (mask == null) return;
            mask.style.display = DisplayStyle.Flex;
        }

        private void ApplyAlertSettings()
        {
            var alertBar = root.Q("alertBar");
            var alertMessageLabel = root.Q<Label>("alertMessage");

            if (alertBar == null) return;

            // 既存のタイプクラスを削除
            alertBar.RemoveFromClassList("info");
            alertBar.RemoveFromClassList("warning");
            alertBar.RemoveFromClassList("error");

            // 新しいタイプクラスを追加
            switch (alertType)
            {
                case AlertType.Info:
                    alertBar.AddToClassList("info");
                    break;
                case AlertType.Warning:
                    alertBar.AddToClassList("warning");
                    break;
                case AlertType.Error:
                    alertBar.AddToClassList("error");
                    break;
            }

            // メッセージを設定
            if (alertMessageLabel != null)
            {
                alertMessageLabel.text = alertMessage;
            }
        }

        private void ApplyTestState()
        {
            // 全状態をリセット
            ResetAllStates();

            switch (currentState)
            {
                case TestState.Default:
                    // デフォルト状態（何もしない）
                    break;

                case TestState.Recording:
                    isRecording = true;
                    ApplyRecordingState();
                    break;

                case TestState.Capturing:
                    // フラッシュ表示
                    var flash = root.Q("flashOverlay");
                    if (flash != null)
                    {
                        flash.style.display = DisplayStyle.Flex;
                        flash.style.opacity = 1f;
                    }
                    break;

                case TestState.PreviewingPhoto:
                    showViewerOverlay = true;
                    SetPanelVisibility("viewerOverlay", true);
                    break;

                case TestState.PreviewingIcon:
                    showIconPreview = true;
                    SetPanelVisibility("iconPreviewPanel", true);
                    break;

                case TestState.LightingAdjustment:
                    showLightingPanel = true;
                    SetPanelVisibility("lightingPanelOverlay", true);
                    break;

                case TestState.ShadowAdjustment:
                    showShadowPanel = true;
                    SetPanelVisibility("shadowPanelOverlay", true);
                    break;

                case TestState.Alert:
                    showAlertBar = true;
                    SetPanelVisibility("alertBar", true);
                    ApplyAlertSettings();
                    break;
            }
        }

        private void ResetAllStates()
        {
            isRecording = false;

            var flash = root.Q("flashOverlay");
            if (flash != null)
            {
                flash.style.display = DisplayStyle.None;
                flash.style.opacity = 0f;
            }
        }

        #region Test Methods

        /// <summary>
        /// デフォルト状態に戻す
        /// </summary>
        [ContextMenu("Reset to Default")]
        public void ResetToDefault()
        {
            currentState = TestState.Default;
            showLightingPanel = false;
            showShadowPanel = false;
            showViewerOverlay = false;
            showIconPreview = false;
            showAlertBar = false;
            isRecording = false;
            recordingProgress = 0f;
            aspectRatio = AspectRatioMode.Full;

            ApplyCurrentState();
        }

        /// <summary>
        /// 全パネルを表示（レイアウト確認用）
        /// </summary>
        [ContextMenu("Show All Panels")]
        public void ShowAllPanels()
        {
            showLightingPanel = true;
            showShadowPanel = true;
            showViewerOverlay = true;
            showIconPreview = true;
            showAlertBar = true;

            ApplyCurrentState();
        }

        /// <summary>
        /// 録画状態をシミュレート
        /// </summary>
        [ContextMenu("Simulate Recording")]
        public void SimulateRecording()
        {
            currentState = TestState.Recording;
            isRecording = true;
            recordingProgress = 0.5f;

            ApplyCurrentState();
        }

        /// <summary>
        /// 全アスペクト比をサイクル
        /// </summary>
        [ContextMenu("Cycle Aspect Ratio")]
        public void CycleAspectRatio()
        {
            aspectRatio = (AspectRatioMode)(((int)aspectRatio + 1) % 4);
            ApplyAspectRatio();

            UnityEngine.Debug.Log($"Aspect Ratio: {aspectRatio}");
        }

        /// <summary>
        /// UIデバッグチェックを実行
        /// </summary>
        [ContextMenu("Run Debug Check")]
        public void RunDebugCheck()
        {
            if (debugChecker == null)
            {
                debugChecker = GetComponent<UIDebugChecker>();
            }

            if (debugChecker != null)
            {
                debugChecker.RunAllChecks();
            }
            else
            {
                UnityEngine.Debug.LogWarning("UIDebugChecker not found on this GameObject");
            }
        }

        #endregion

        #region Validation

        /// <summary>
        /// 現在の状態が期待通りかを検証
        /// </summary>
        public bool ValidateCurrentState()
        {
            if (root == null) return false;

            bool isValid = true;

            // パネル表示状態の検証
            isValid &= ValidatePanelState("lightingPanelOverlay", showLightingPanel);
            isValid &= ValidatePanelState("shadowPanelOverlay", showShadowPanel);
            isValid &= ValidatePanelState("viewerOverlay", showViewerOverlay);
            isValid &= ValidatePanelState("iconPreviewPanel", showIconPreview);
            isValid &= ValidatePanelState("alertBar", showAlertBar);

            // 録画状態の検証
            var innerCircle = root.Q("innerCircle");
            if (innerCircle != null)
            {
                bool hasRecordingClass = innerCircle.ClassListContains("recording");
                if (hasRecordingClass != isRecording)
                {
                    UnityEngine.Debug.LogError($"Recording state mismatch: expected {isRecording}, got {hasRecordingClass}");
                    isValid = false;
                }
            }

            if (isValid)
            {
                UnityEngine.Debug.Log("All state validations passed!");
            }

            return isValid;
        }

        private bool ValidatePanelState(string elementId, bool expectedVisible)
        {
            var element = root.Q(elementId);
            if (element == null)
            {
                UnityEngine.Debug.LogError($"Element not found: {elementId}");
                return false;
            }

            bool hasVisibleClass = element.ClassListContains("visible");
            bool isDisplayed = element.resolvedStyle.display == DisplayStyle.Flex;

            if (expectedVisible && (!hasVisibleClass || !isDisplayed))
            {
                UnityEngine.Debug.LogError($"{elementId}: Expected visible but is hidden");
                return false;
            }

            if (!expectedVisible && (hasVisibleClass && isDisplayed))
            {
                UnityEngine.Debug.LogError($"{elementId}: Expected hidden but is visible");
                return false;
            }

            return true;
        }

        #endregion
    }
}
