using UnityEngine;
using UnityEngine.UIElements;
using NativeFilePickerNamespace;
using Cysharp.Threading.Tasks;
using System.IO;
using System;
using System.Collections.Generic;
using AICam.AR;
using DSGarage.PoseSlot;

namespace AICam.UI
{
    /// <summary>
    /// UIToolkit版のカメラ撮影コントローラー
    /// タップで写真撮影、長押しで動画撮影を行う
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(UIToolkitInputBlocker))]
    public class CameraCaptureController : MonoBehaviour
    {
        [Header("Capture Settings")]
        [SerializeField] private ARPhotoController photoController;

        [Header("Avatar Loader")]
        [SerializeField] private AICam.VRM.RuntimeAvatarLoader avatarLoader;
        [SerializeField] private AICam.FBXLoader.RuntimeFBXLoaderBridge fbxLoaderBridge;

        [Header("Pose Animation (Issue #407)")]
        [SerializeField] private AnimatorOverrideController[] poseOverrideControllers;
        [SerializeField] private PoseSlotController poseSlotController;

        [Header("Expression System (Issue #145/#411)")]
        [SerializeField] private AICam.Expression.VrmExpressionSetup expressionSetup;

        [Header("Debug (Issue #427)")]
        [Tooltip("起動時のデバッグログを有効化（プロダクションビルドではOFFにして起動時間を短縮）")]
        [SerializeField] private bool enableDebugLogging = false;

        private VisualElement root;
        private VisualElement captureButton;
        private VisualElement innerCircle;
        private VisualElement progressRing;
        private VisualElement progressArc;
        private VisualElement flashOverlay;
        private VisualElement galleryThumbnail;

        // ビューア要素
        private VisualElement viewerOverlay;
        private Image viewerImage;

        // アラートバー要素
        private VisualElement alertBar;
        private Label alertMessage;
        private Button alertClose;

        // バージョン表示ラベル
        private Label versionLabel;

        // アイコンプレビューパネル要素
        private VisualElement iconPreviewPanel;
        private VisualElement iconPreviewImage;
        private Button iconPreviewRetake;
        private Button iconPreviewConfirm;
        private System.Action onIconPreviewConfirm;
        private System.Action onIconPreviewRetake;

        // パネル要素
        private VisualElement topPanel;
        private VisualElement bottomPanel;
        private VisualElement bottomButtonContainer;
        private Button bottomButtonAdd;
        private int bottomButtonCount = 3;

        // サイドパネル要素
        private VisualElement sidePanel;
        private Button sideButton1;
        private Button sideButton2;
        private Button sideButton3;
        private Button sideButtonBugReport; // Issue #413: バグレポート

        // Issue #74/#75: トップパネルボタン要素
        private Button topButton1; // Light Estimation ON/OFF
        private Button topButton2; // Shadow ON/OFF
        private Button topButton3; // Issue #33/#405: Expression切り替え
        private Button topButton4; // Issue #407: Pose切り替え
        private Button topButton5; // Issue #345: Plane Visibility ON/OFF

        // Issue #407: ポーズ切り替え
        private int currentPoseIndex = 0;
        private int currentOverrideIndex = 0;  // 現在のOverrideControllerインデックス
        private GameObject cachedCurrentAvatar;  // 現在のアバター参照
        private System.Collections.Generic.List<string> cachedStateNames;  // キャッシュされたState名

        // Issue #407: ダブルタップ検出用（ポーズ）
        private const float DOUBLE_TAP_THRESHOLD = 0.3f;  // 300ms以内でダブルタップ判定
        private int tapCount = 0;
        private System.Threading.CancellationTokenSource tapCts;

        // Issue #33/#405: ダブルタップ検出用（表情）
        private int expressionTapCount = 0;
        private System.Threading.CancellationTokenSource expressionTapCts;

        // Issue #345: 平面表示状態
        private bool isPlaneVisible = true;
        private ARPlaneVisibilityController cachedPlaneVisibilityController;

        // Issue #74: Light Estimation状態
        private bool isLightEstimationEnabled = true;
        private ARLightEstimationController cachedLightEstimationController;

        // Issue #75: Shadow状態
        private bool isShadowEnabled = true;
        private Light cachedMainLight;
        private ARPlaneShadowReceiver cachedPlaneShadowReceiver;

        // Issue #120: ライティング/シャドウパネル
        private LightingPanelController lightingPanelController;
        private VisualElement settingsPanelBackdrop;
        private VisualElement lightingPanelOverlay;
        private VisualElement shadowPanelOverlay;
        // Issue #74/#75 修正: 長押し関連変数は不要になったため削除

        // アスペクト比トグル用のステート（02_01 → 02_02 → 02_03 → 02_01）
        private int aspectRatioState = 0;
        private readonly string[] aspectRatioIcons = new string[]
        {
            "Sprite/PictIcon/SideBear/02_01_Full",
            "Sprite/PictIcon/SideBear/02_02_169",
            "Sprite/PictIcon/SideBear/02_03_32",
            "Sprite/PictIcon/SideBear/02_04_11"  // 1:1 (正方形)
        };

        // アスペクト比の定義（幅/高さ）
        private readonly float[] aspectRatios = new float[]
        {
            0f,      // Full (0 = カメラの最大画角)
            16f/9f,  // 16:9
            3f/2f,   // 3:2
            1f       // 1:1 (正方形)
        };

        // アスペクト比マスク要素
        private VisualElement topMask;
        private VisualElement bottomMask;
        private VisualElement leftMask;
        private VisualElement rightMask;

        // 削除ポップアップ関連
        private VisualElement deletePopup;
        private Button deleteButton;
        private Button cancelButton;
        private Button currentLongPressButton;
        private float longPressTime = 0f;
        private const float longPressThresholdForDelete = 0.5f;
        private bool isLongPressing = false;
        private bool suppressNextClick = false; // 長押し後のクリックを抑制するフラグ

        private bool isPressed = false;
        private bool isRecording = false;
        private float pressTime = 0f;
        private const float longPressThreshold = 0.5f;
        private const float maxRecordTime = 5f;
        private bool lastMediaIsVideo = false;

        private Texture2D lastCapturedPhoto;
        private string lastCapturedVideoPath;

        // スロットデータ管理
        private Dictionary<Button, SlotData> slotDataMap = new Dictionary<Button, SlotData>();
        private Button currentSelectedSlot;

        // Issue #73: スロット別プログレス要素の管理
        private Dictionary<Button, CircularProgressElement> slotProgressMap = new Dictionary<Button, CircularProgressElement>();

        /// <summary>
        /// スロットのファイルタイプ
        /// </summary>
        private enum SlotFileType
        {
            None,
            VRM,
            FBX
        }

        /// <summary>
        /// スロットデータ（ファイルパス、サムネイル、ロード済みアバターを管理）
        /// </summary>
        private class SlotData
        {
            public string filePath;
            public SlotFileType fileType;
            public Texture2D thumbnail;
            public GameObject loadedAvatar;
            public bool IsConfigured => !string.IsNullOrEmpty(filePath);
        }

        #region Lazy Initialization

        /// <summary>
        /// LightingPanelControllerを遅延取得・初期化
        /// パネルを開くときに呼び出す
        /// </summary>
        private LightingPanelController GetLightingPanelController()
        {
            if (lightingPanelController == null)
            {
                lightingPanelController = FindFirstObjectByType<LightingPanelController>();
                if (lightingPanelController == null)
                {
                    Debug.Log("💡 LightingPanelController not found - creating automatically (lazy)");
                    var lightingObj = new GameObject("LightingPanelController");
                    lightingPanelController = lightingObj.AddComponent<LightingPanelController>();
                }

                if (lightingPanelController != null)
                {
                    lightingPanelController.Initialize();
                    lightingPanelController.OnWarning += (code, message) => ShowWarning(code, message);
                    lightingPanelController.OnError += (code, message) => ShowError(code, message);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log("[Init] LightingPanelController initialized (lazy)");
#endif
                }
            }
            return lightingPanelController;
        }

        #endregion

        void OnEnable()
        {
            if (enableDebugLogging) Debug.Log("🔧 CameraCaptureController OnEnable called");

            // ARPhotoControllerのイベント登録
            if (photoController != null)
            {
                photoController.OnPhotoCaptured += OnPhotoCapturedHandler;
            }

            var uiDoc = GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                Debug.LogError("❌ UIDocument component not found!");
                return;
            }

            root = uiDoc.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("⏳ Root VisualElement is null - waiting for UIDocument to initialize...");
                StartCoroutine(WaitForUIDocumentAndInitialize(uiDoc));
                return;
            }

            InitializeUIElements();
        }

        /// <summary>
        /// UIDocumentのrootVisualElementが利用可能になるまで待機してから初期化
        /// </summary>
        private System.Collections.IEnumerator WaitForUIDocumentAndInitialize(UIDocument uiDoc)
        {
            int maxRetries = 10;
            int retryCount = 0;

            while (root == null && retryCount < maxRetries)
            {
                yield return null; // 1フレーム待機
                root = uiDoc.rootVisualElement;
                retryCount++;
                Debug.Log($"⏳ Waiting for UIDocument... attempt {retryCount}/{maxRetries}");
            }

            if (root == null)
            {
                Debug.LogError("❌ Root VisualElement is still null after waiting!");
                yield break;
            }

            Debug.Log($"✅ Root element found after {retryCount} frame(s): {root.name}");
            InitializeUIElements();
        }

        /// <summary>
        /// UI要素の初期化（OnEnableまたは遅延初期化から呼ばれる）
        /// </summary>
        private void InitializeUIElements()
        {
            if (enableDebugLogging) Debug.Log($"✅ Root element found: {root.name}");

            captureButton = root.Q<VisualElement>("captureButton");
            innerCircle = root.Q<VisualElement>("innerCircle");
            progressRing = root.Q<VisualElement>("progressRing");
            progressArc = root.Q<VisualElement>("progressArc");
            flashOverlay = root.Q<VisualElement>("flashOverlay");
            galleryThumbnail = root.Q<VisualElement>("galleryThumbnail");

            viewerOverlay = root.Q<VisualElement>("viewerOverlay");
            viewerImage = root.Q<Image>("viewerImage");

            // アラートバー要素の取得
            alertBar = root.Q<VisualElement>("alertBar");
            alertMessage = root.Q<Label>("alertMessage");
            alertClose = root.Q<Button>("alertClose");

            // バージョン表示ラベルの取得と設定
            versionLabel = root.Q<Label>("versionLabel");
            if (versionLabel != null)
            {
                string version = Application.version;
                string buildNumber = GetBuildNumber();
                versionLabel.text = $"v{version} ({buildNumber})";
                if (enableDebugLogging) Debug.Log($"✅ Version label set: v{version} ({buildNumber})");
            }

            // アイコンプレビューパネル要素の取得
            iconPreviewPanel = root.Q<VisualElement>("iconPreviewPanel");
            iconPreviewImage = root.Q<VisualElement>("iconPreviewImage");
            iconPreviewRetake = root.Q<Button>("iconPreviewRetake");
            iconPreviewConfirm = root.Q<Button>("iconPreviewConfirm");

            topPanel = root.Q<VisualElement>("topPanel");
            bottomPanel = root.Q<VisualElement>("bottomPanel");
            bottomButtonContainer = root.Q<VisualElement>("bottomButtonContainer");
            bottomButtonAdd = root.Q<Button>("bottomButtonAdd");

            // サイドパネル要素の取得
            sidePanel = root.Q<VisualElement>("sidePanel");
            sideButton1 = root.Q<Button>("sideButton1");
            sideButton2 = root.Q<Button>("sideButton2");
            sideButton3 = root.Q<Button>("sideButton3");
            sideButtonBugReport = root.Q<Button>("sideButtonBugReport"); // Issue #413

            // Issue #74/#75: トップパネルボタンの取得
            topButton1 = root.Q<Button>("topButton1");
            topButton2 = root.Q<Button>("topButton2");
            topButton3 = root.Q<Button>("topButton3"); // Issue #33/#405: Expression
            topButton4 = root.Q<Button>("topButton4"); // Issue #407: Pose
            topButton5 = root.Q<Button>("topButton5"); // Issue #345: Plane Visibility

            // アスペクト比マスク要素の取得
            topMask = root.Q<VisualElement>("topMask");
            bottomMask = root.Q<VisualElement>("bottomMask");
            leftMask = root.Q<VisualElement>("leftMask");
            rightMask = root.Q<VisualElement>("rightMask");

            // ScrollViewの設定（物理スクロール対応）
            var bottomScrollView = root.Q<ScrollView>("bottomScrollView");
            if (bottomScrollView != null)
            {
                bottomScrollView.mode = ScrollViewMode.Horizontal;

                // 実機用：スクロールバー非表示、エディタ用：Auto表示
#if UNITY_EDITOR
                bottomScrollView.horizontalScrollerVisibility = ScrollerVisibility.Auto;
#else
                bottomScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
#endif
                bottomScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;

                // 物理スクロール設定
                bottomScrollView.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
                bottomScrollView.elasticity = 0.1f;
                bottomScrollView.scrollDecelerationRate = 0.135f;

                // 横スクロールのみ有効化
                bottomScrollView.horizontalPageSize = 0;
                bottomScrollView.verticalPageSize = 0;
                bottomScrollView.nestedInteractionKind = ScrollView.NestedInteractionKind.Default;

                // ContentContainerのflex設定を強制
                bottomScrollView.contentContainer.style.flexDirection = FlexDirection.Row;
                bottomScrollView.contentContainer.style.flexWrap = Wrap.NoWrap;

                // マウスホイールスクロール（エディタ用）
                bottomScrollView.mouseWheelScrollSize = 30f;

                if (enableDebugLogging) Debug.Log($"✅ ScrollView configured: mode={bottomScrollView.mode}, touchBehavior={bottomScrollView.touchScrollBehavior}");
            }

            // Issue #427: デバッグログを条件付きに変更（起動時間短縮）
            if (enableDebugLogging)
            {
                Debug.Log($"captureButton: {(captureButton != null ? "✅" : "❌")}");
                Debug.Log($"innerCircle: {(innerCircle != null ? "✅" : "❌")}");
                Debug.Log($"progressRing: {(progressRing != null ? "✅" : "❌")}");
                Debug.Log($"progressArc: {(progressArc != null ? "✅" : "❌")}");
                Debug.Log($"flashOverlay: {(flashOverlay != null ? "✅" : "❌")}");
                Debug.Log($"galleryThumbnail: {(galleryThumbnail != null ? "✅" : "❌")}");
                Debug.Log($"viewerOverlay: {(viewerOverlay != null ? "✅" : "❌")}");
                Debug.Log($"viewerImage: {(viewerImage != null ? "✅" : "❌")}");
            }

            if (captureButton != null)
            {
                captureButton.RegisterCallback<PointerDownEvent>(OnPointerDown);
                captureButton.RegisterCallback<PointerUpEvent>(OnPointerUp);
                if (enableDebugLogging) captureButton.RegisterCallback<ClickEvent>(evt => Debug.Log("🖱 Capture button clicked!"));
                if (enableDebugLogging) Debug.Log("✅ Capture button events registered");
            }
            else
            {
                Debug.LogError("❌ captureButton is null - cannot register events");
            }

            if (galleryThumbnail != null)
            {
                galleryThumbnail.RegisterCallback<ClickEvent>(evt => OpenViewer());
                if (enableDebugLogging) Debug.Log("✅ Gallery thumbnail events registered");
            }

            if (viewerOverlay != null)
            {
                viewerOverlay.RegisterCallback<ClickEvent>(evt => CloseViewer());
                if (enableDebugLogging) Debug.Log("✅ Viewer overlay events registered");
            }

            // アラートバーのイベント登録
            if (alertClose != null)
            {
                alertClose.RegisterCallback<ClickEvent>(evt => HideAlert());
                if (enableDebugLogging) Debug.Log("✅ Alert close button events registered");
            }

            // アイコンプレビューパネルのイベント登録
            if (iconPreviewConfirm != null)
            {
                iconPreviewConfirm.RegisterCallback<ClickEvent>(evt => OnIconPreviewConfirmClicked());
                if (enableDebugLogging) Debug.Log("✅ Icon preview confirm button events registered");
            }

            if (iconPreviewRetake != null)
            {
                iconPreviewRetake.RegisterCallback<ClickEvent>(evt => OnIconPreviewRetakeClicked());
                if (enableDebugLogging) Debug.Log("✅ Icon preview retake button events registered");
            }

            if (bottomButtonAdd != null)
            {
                bottomButtonAdd.RegisterCallback<ClickEvent>(evt => AddBottomPanelButton());
                if (enableDebugLogging) Debug.Log("✅ Add button events registered");
            }

            // サイドパネルボタンのイベント登録
            if (sideButton1 != null)
            {
                sideButton1.RegisterCallback<ClickEvent>(evt => OnSideButton1Clicked());
                if (enableDebugLogging) Debug.Log("✅ Side button 1 events registered");
            }

            if (sideButton2 != null)
            {
                sideButton2.RegisterCallback<ClickEvent>(evt => OnSideButton2Clicked());
                if (enableDebugLogging) Debug.Log("✅ Side button 2 events registered");
            }

            if (sideButton3 != null)
            {
                sideButton3.RegisterCallback<ClickEvent>(evt => OnSideButton3Clicked());
                if (enableDebugLogging) Debug.Log("✅ Side button 3 events registered");
            }

            // Issue #413: バグレポートボタンのイベント登録
            if (sideButtonBugReport != null)
            {
                sideButtonBugReport.RegisterCallback<ClickEvent>(evt => OnBugReportButtonClicked());
                if (enableDebugLogging) Debug.Log("✅ Bug report button events registered");

                // バグレポートアイコンを設定
                var bugReportIcon = Resources.Load<Texture2D>("Sprite/PictIcon/SideBear/04_BugReport");
                if (bugReportIcon != null)
                {
                    sideButtonBugReport.style.backgroundImage = new StyleBackground(bugReportIcon);
                }
            }

            // Issue #74/#75: トップパネルボタンのイベント登録
            if (enableDebugLogging)
            {
                Debug.Log($"🔘 topButton1: {(topButton1 != null ? "✅ found" : "❌ NOT FOUND")}");
                Debug.Log($"🔘 topButton2: {(topButton2 != null ? "✅ found" : "❌ NOT FOUND")}");
            }

            if (topButton1 != null)
            {
                // Issue #74 修正: 短押しでライティングパネル表示（パネル内にON/OFFトグルあり）
                topButton1.RegisterCallback<ClickEvent>(evt => OnTopButton1Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 1 (Light Estimation) click event registered");
            }

            if (topButton2 != null)
            {
                // Issue #75 修正: 短押しでシャドウパネル表示（パネル内にON/OFFトグルあり）
                topButton2.RegisterCallback<ClickEvent>(evt => OnTopButton2Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 2 (Shadow) click event registered");
            }

            // Issue #33/#405: 表情切り替えボタンのイベント登録
            if (enableDebugLogging) Debug.Log($"🔘 topButton3: {(topButton3 != null ? "✅ found" : "❌ NOT FOUND")}");
            if (topButton3 != null)
            {
                topButton3.RegisterCallback<ClickEvent>(evt => OnTopButton3Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 3 (Expression) click event registered");
            }

            // Issue #407: ポーズ切り替えボタンのイベント登録
            if (enableDebugLogging) Debug.Log($"🔘 topButton4: {(topButton4 != null ? "✅ found" : "❌ NOT FOUND")}");
            if (topButton4 != null)
            {
                topButton4.RegisterCallback<ClickEvent>(evt => OnTopButton4Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 4 (Pose) click event registered");
            }

            // Issue #345: 平面表示切り替えボタンのイベント登録
            if (enableDebugLogging) Debug.Log($"🔘 topButton5: {(topButton5 != null ? "✅ found" : "❌ NOT FOUND")}");
            if (topButton5 != null)
            {
                topButton5.RegisterCallback<ClickEvent>(evt => OnTopButton5Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 5 (Plane Visibility) click event registered");

                // 初期アイコンを設定
                UpdatePlaneVisibilityIcon();
            }

            // Issue #120: パネル要素を直接取得
            settingsPanelBackdrop = root.Q<VisualElement>("settingsPanelBackdrop");
            lightingPanelOverlay = root.Q<VisualElement>("lightingPanelOverlay");
            shadowPanelOverlay = root.Q<VisualElement>("shadowPanelOverlay");

            if (settingsPanelBackdrop != null)
            {
                // バックドロップ自体がクリックされた場合のみパネルを閉じる（子要素のクリックは無視）
                settingsPanelBackdrop.RegisterCallback<PointerDownEvent>(evt =>
                {
                    // クリックがバックドロップ自体の場合のみ閉じる
                    if (evt.target == settingsPanelBackdrop)
                    {
                        if (enableDebugLogging) Debug.Log("🔲 Backdrop clicked directly - closing panels");
                        HideAllPanels();
                        evt.StopPropagation();
                    }
                });
                if (enableDebugLogging) Debug.Log("✅ Settings panel backdrop events registered");
            }

            var lightingCloseButton = root.Q<Button>("lightingPanelClose");
            if (lightingCloseButton != null)
            {
                lightingCloseButton.RegisterCallback<ClickEvent>(evt => HideAllPanels());
                if (enableDebugLogging) Debug.Log("✅ Lighting panel close button events registered");
            }

            var shadowCloseButton = root.Q<Button>("shadowPanelClose");
            if (shadowCloseButton != null)
            {
                shadowCloseButton.RegisterCallback<ClickEvent>(evt => HideAllPanels());
                if (enableDebugLogging) Debug.Log("✅ Shadow panel close button events registered");
            }

            // LightingPanelControllerは初回使用時に遅延初期化
            // 起動時間短縮のためFindFirstObjectByTypeをここでは呼ばない
            // InitializeLightingPanelControllerAsync().Forget() は必要時に呼び出される
            if (enableDebugLogging)
            {
                Debug.Log($"💡 SettingsPanelBackdrop: {(settingsPanelBackdrop != null ? "✅" : "❌")}");
                Debug.Log($"💡 LightingPanelOverlay: {(lightingPanelOverlay != null ? "✅" : "❌")}");
                Debug.Log($"🌑 ShadowPanelOverlay: {(shadowPanelOverlay != null ? "✅" : "❌")}");

                // 削除ポップアップを作成（初期状態では非表示）
                Debug.Log("🔧 Creating delete popup...");
            }
            CreateDeletePopup(root);
            if (enableDebugLogging) Debug.Log($"🔧 Delete popup created: {(deletePopup != null ? "✅" : "❌")}");

            // 既存のボタンに長押しイベントを登録
            if (enableDebugLogging) Debug.Log("🔧 Registering long press for existing buttons...");
            RegisterLongPressForExistingButtons();

            // GeometryChangedEventでアスペクト比マスクを更新（レイアウト確定後）
            if (enableDebugLogging) Debug.Log("🔧 Registering GeometryChangedEvent for aspect mask...");
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            // ARPhotoControllerに初期アスペクト比を設定
            if (photoController != null)
            {
                float targetAspect = aspectRatios[aspectRatioState];
                photoController.SetAspectRatio(targetAspect);
                if (enableDebugLogging) Debug.Log($"✅ Initial aspect ratio set to: {targetAspect:F3}");
            }

            // Issue #407: AvatarSlotManagerのイベント購読
            if (AICam.FBXLoader.AvatarSlotManager.Instance != null)
            {
                AICam.FBXLoader.AvatarSlotManager.Instance.OnSlotLoadComplete += OnAvatarSlotLoadComplete;
                AICam.FBXLoader.AvatarSlotManager.Instance.OnSlotCleared += OnAvatarSlotCleared;
                if (enableDebugLogging) Debug.Log("🎭 Subscribed to AvatarSlotManager events");
            }
            else
            {
                Debug.LogWarning("🎭 AvatarSlotManager.Instance is null - cannot subscribe to events");
            }
        }

        void OnDisable()
        {
            // ARPhotoControllerのイベント解除
            if (photoController != null)
            {
                photoController.OnPhotoCaptured -= OnPhotoCapturedHandler;
            }

            // GeometryChangedEventの解除
            if (root != null)
            {
                root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }

            // Issue #407: AvatarSlotManagerのイベント解除
            if (AICam.FBXLoader.AvatarSlotManager.Instance != null)
            {
                AICam.FBXLoader.AvatarSlotManager.Instance.OnSlotLoadComplete -= OnAvatarSlotLoadComplete;
                AICam.FBXLoader.AvatarSlotManager.Instance.OnSlotCleared -= OnAvatarSlotCleared;
            }
        }

        void OnPhotoCapturedHandler(Texture2D thumbnail)
        {
            Debug.Log("📸 Photo captured, updating thumbnail");
            lastCapturedPhoto = thumbnail;
            lastMediaIsVideo = false;

            if (galleryThumbnail != null)
            {
                galleryThumbnail.style.backgroundImage = new StyleBackground(thumbnail);
            }
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            Debug.Log("👇 OnPointerDown triggered");
            isPressed = true;
            pressTime = 0f;

            // Light impact for button press
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            Debug.Log($"👆 OnPointerUp triggered (pressTime: {pressTime}s, isRecording: {isRecording})");
            isPressed = false;

            if (isRecording)
            {
                // 録画中だった場合は停止
                StopRecording();
            }
            else if (pressTime < longPressThreshold)
            {
                // 短押しの場合は写真撮影
                TakePhoto();
            }

            pressTime = 0f;
        }

        void Update()
        {
            // Issue #74/#75 修正: 長押し検出は不要になったため削除
            // 短押しでパネル表示、パネル内にON/OFFトグルあり

            if (isPressed)
            {
                pressTime += Time.deltaTime;

                // 長押し判定: 0.5秒経過したら録画開始
                if (!isRecording && pressTime >= longPressThreshold)
                {
                    StartRecording();
                }

                // 録画中の処理
                if (isRecording)
                {
                    float recordTime = pressTime - longPressThreshold;
                    float progress = Mathf.Clamp01(recordTime / maxRecordTime);

                    UpdateProgressRing(progress);

                    // 最大録画時間に達したら自動停止
                    if (progress >= 1f)
                    {
                        isPressed = false;
                        StopRecording();
                    }
                }
            }

            // アバタースロットボタンの長押し検出
            if (isLongPressing && currentLongPressButton != null)
            {
                longPressTime += Time.deltaTime;

                // デバッグログ（0.1秒ごと）
                if (Mathf.FloorToInt(longPressTime * 10) != Mathf.FloorToInt((longPressTime - Time.deltaTime) * 10))
                {
                    Debug.Log($"⏱ Long press time: {longPressTime:F2}s / {longPressThresholdForDelete}s");
                }

                if (longPressTime >= longPressThresholdForDelete)
                {
                    Debug.Log($"✅ Long press threshold reached! Showing popup for {currentLongPressButton.name}");
                    ShowDeletePopup(currentLongPressButton);
                    isLongPressing = false;
                    longPressTime = 0f;
                }
            }
        }

        void StartRecording()
        {
            isRecording = true;
            Debug.Log("🎬 録画開始");

            // UIの状態変更
            innerCircle?.AddToClassList("recording");
            progressRing?.AddToClassList("active");

            // Heavy impact for recording start
            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        void TakePhoto()
        {
            Debug.Log("📸 写真撮影");
            FlashEffect();

            if (photoController != null)
            {
                photoController.Capture();
                // サムネイルはOnPhotoCapturedHandlerで更新される
            }
            else
            {
                Debug.LogWarning("ARPhotoController is not assigned");
            }

            ResetButtonState();

            // Medium impact for photo capture
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        void StopRecording()
        {
            if (!isRecording) return;

            Debug.Log("🎥 動画撮影終了");
            FlashEffect();

            lastMediaIsVideo = true;
            lastCapturedVideoPath = Application.persistentDataPath + "/lastVideo.mp4";

            // 仮の赤サムネイル
            Texture2D dummyFrame = new Texture2D(64, 64);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.red;
            }
            dummyFrame.SetPixels(pixels);
            dummyFrame.Apply();

            if (galleryThumbnail != null)
            {
                galleryThumbnail.style.backgroundImage = new StyleBackground(dummyFrame);
            }

            ResetButtonState();
            isRecording = false;
        }

        void FlashEffect()
        {
            if (flashOverlay == null) return;

            flashOverlay.style.opacity = 1;
            flashOverlay.schedule.Execute(() =>
            {
                flashOverlay.style.opacity = 0;
            }).StartingIn(100);
        }

        void UpdateProgressRing(float progress)
        {
            if (progressArc == null) return;

            // 連続的な円形プログレス表示（12時位置から時計回り）
            // rotate: -90degにより、12時位置スタート
            // 進行度を360度の角度に変換し、各辺の表示を細かく制御

            Color red = new Color(1f, 0f, 0f, 1f);
            Color transparent = new Color(0f, 0f, 0f, 0f);

            // 進行度を角度に変換（0-360度）
            float angle = progress * 360f;

            // 各辺は90度ずつ担当
            // rotate: -90deg により: top=12時~3時, right=3時~6時, bottom=6時~9時, left=9時~12時

            // 上辺 (0-90度 = 12時~3時)
            Color topColor;
            if (angle < 90f)
            {
                // 0-90度の範囲で線形補間
                topColor = Color.Lerp(transparent, red, angle / 90f);
            }
            else
            {
                topColor = red;
            }

            // 右辺 (90-180度 = 3時~6時)
            Color rightColor;
            if (angle < 90f)
            {
                rightColor = transparent;
            }
            else if (angle < 180f)
            {
                topColor = red;
                rightColor = Color.Lerp(transparent, red, (angle - 90f) / 90f);
            }
            else
            {
                topColor = red;
                rightColor = red;
            }

            // 下辺 (180-270度 = 6時~9時)
            Color bottomColor;
            if (angle < 180f)
            {
                bottomColor = transparent;
            }
            else if (angle < 270f)
            {
                topColor = red;
                rightColor = red;
                bottomColor = Color.Lerp(transparent, red, (angle - 180f) / 90f);
            }
            else
            {
                topColor = red;
                rightColor = red;
                bottomColor = red;
            }

            // 左辺 (270-360度 = 9時~12時)
            Color leftColor;
            if (angle < 270f)
            {
                leftColor = transparent;
            }
            else
            {
                topColor = red;
                rightColor = red;
                bottomColor = red;
                leftColor = Color.Lerp(transparent, red, (angle - 270f) / 90f);
            }

            progressArc.style.borderTopColor = topColor;
            progressArc.style.borderRightColor = rightColor;
            progressArc.style.borderBottomColor = bottomColor;
            progressArc.style.borderLeftColor = leftColor;
        }

        void ResetButtonState()
        {
            innerCircle?.RemoveFromClassList("recording");
            progressRing?.RemoveFromClassList("active");

            if (progressArc != null)
            {
                Color transparent = new Color(0f, 0f, 0f, 0f);
                progressArc.style.borderTopColor = transparent;
                progressArc.style.borderRightColor = transparent;
                progressArc.style.borderBottomColor = transparent;
                progressArc.style.borderLeftColor = transparent;
            }
        }

        void OpenViewer()
        {
            Debug.Log("🖼 OpenViewer called");

            if (viewerOverlay == null)
            {
                Debug.LogWarning("⚠️ viewerOverlay is null");
                return;
            }

            viewerOverlay.style.display = DisplayStyle.Flex;
            Debug.Log("✅ Viewer opened");

            if (lastMediaIsVideo)
            {
                // 動画の場合は今後実装
                Debug.Log("📹 Video mode (not implemented)");
                if (viewerImage != null)
                {
                    viewerImage.style.display = DisplayStyle.None;
                }
            }
            else
            {
                if (viewerImage != null && lastCapturedPhoto != null)
                {
                    viewerImage.style.display = DisplayStyle.Flex;
                    viewerImage.image = lastCapturedPhoto;
                    Debug.Log("✅ Photo displayed in viewer");
                }
                else
                {
                    Debug.LogWarning("⚠️ No photo to display");
                }
            }
        }

        void CloseViewer()
        {
            Debug.Log("✋ CloseViewer called");

            if (viewerOverlay != null)
            {
                viewerOverlay.style.display = DisplayStyle.None;
                Debug.Log("✅ Viewer closed");
            }
            else
            {
                Debug.LogWarning("⚠️ viewerOverlay is null");
            }
        }

        /// <summary>
        /// ARPhotoControllerを設定（外部から呼び出し可能）
        /// </summary>
        public void SetPhotoController(ARPhotoController controller)
        {
            photoController = controller;
        }

        /// <summary>
        /// 録画中かどうかを取得
        /// </summary>
        public bool IsRecording => isRecording;

        /// <summary>
        /// 最後にキャプチャした写真のサムネイルを更新
        /// </summary>
        public void UpdateLastCapturedPhoto(Texture2D photo)
        {
            lastCapturedPhoto = photo;
            lastMediaIsVideo = false;

            if (galleryThumbnail != null && photo != null)
            {
                galleryThumbnail.style.backgroundImage = new StyleBackground(photo);
            }
        }

        /// <summary>
        /// 下部パネルにボタンを追加
        /// </summary>
        void AddBottomPanelButton()
        {
            if (bottomButtonContainer == null)
            {
                Debug.LogWarning("⚠️ bottomButtonContainer is null");
                return;
            }

            bottomButtonCount++;
            Debug.Log($"➕ Adding bottom panel button #{bottomButtonCount}");

            // 新しいボタンを作成
            var newButton = new Button();
            newButton.name = $"bottomButton{bottomButtonCount}";
            newButton.AddToClassList("bottom-panel-button");

            // +ボタンの直前に挿入
            int addButtonIndex = bottomButtonContainer.IndexOf(bottomButtonAdd);
            bottomButtonContainer.Insert(addButtonIndex, newButton);

            // 長押しイベントを登録（ClickEventより先に登録）
            RegisterLongPressForButton(newButton);

            // ボタンのクリックイベントを登録
            newButton.RegisterCallback<ClickEvent>(evt =>
            {
                // 長押し後のクリックは抑制
                if (suppressNextClick)
                {
                    Debug.Log($"🚫 Click suppressed after long press on {newButton.name}");
                    suppressNextClick = false;
                    return;
                }

                Debug.Log($"🔘 Bottom button #{newButton.name} clicked");
                TapticEngine.Selection();

                // スロットの状態に応じて処理を分岐
                OnSlotClicked(newButton);
            });

            // Light impact for button addition
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        /// <summary>
        /// 削除ポップアップを作成
        /// </summary>
        void CreateDeletePopup(VisualElement root)
        {
            deletePopup = new VisualElement();
            deletePopup.name = "deletePopup";
            deletePopup.AddToClassList("delete-popup");

            // 絶対配置を有効化
            deletePopup.style.position = Position.Absolute;
            deletePopup.pickingMode = PickingMode.Position;

            // 削除ボタン
            deleteButton = new Button();
            deleteButton.text = "削除";
            deleteButton.AddToClassList("delete-popup-button");
            deleteButton.AddToClassList("delete");
            deleteButton.RegisterCallback<ClickEvent>(evt => OnDeleteButtonClicked());

            // キャンセルボタン
            cancelButton = new Button();
            cancelButton.text = "キャンセル";
            cancelButton.AddToClassList("delete-popup-button");
            cancelButton.RegisterCallback<ClickEvent>(evt => HideDeletePopup());

            deletePopup.Add(deleteButton);
            deletePopup.Add(cancelButton);
            root.Add(deletePopup);

            Debug.Log("✅ Delete popup created");
        }

        /// <summary>
        /// 既存のアバタースロットボタンに長押しイベントとクリックイベントを登録
        /// </summary>
        void RegisterLongPressForExistingButtons()
        {
            if (bottomButtonContainer == null) return;

            var buttons = bottomButtonContainer.Query<Button>().ToList();
            foreach (var button in buttons)
            {
                // +ボタンは除外
                if (button == bottomButtonAdd) continue;

                RegisterLongPressForButton(button);

                // クリックイベントも登録
                button.RegisterCallback<ClickEvent>(evt =>
                {
                    // 長押し後のクリックは抑制
                    if (suppressNextClick)
                    {
                        Debug.Log($"🚫 Click suppressed after long press on {button.name}");
                        suppressNextClick = false;
                        return;
                    }

                    Debug.Log($"🔘 Bottom button #{button.name} clicked");
                    TapticEngine.Selection();

                    // スロットの状態に応じて処理を分岐
                    OnSlotClicked(button);
                });
            }

            Debug.Log($"✅ Long press and click registered for {buttons.Count - 1} buttons");
        }

        /// <summary>
        /// ボタンに長押しイベントを登録
        /// </summary>
        void RegisterLongPressForButton(Button button)
        {
            // PointerDownEventで長押し開始を検出（TrickleDownフェーズで優先キャプチャ）
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                isLongPressing = true;
                currentLongPressButton = button;
                longPressTime = 0f;
                Debug.Log($"👇 Long press started on {button.name}");
            }, TrickleDown.TrickleDown);

            // PointerUpEventで長押しをキャンセル
            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                Debug.Log($"👆 Long press released on {button.name} (time: {longPressTime:F2}s, isLongPressing: {isLongPressing})");

                // 短押しの場合はClickEventに任せる
                if (longPressTime < longPressThresholdForDelete)
                {
                    Debug.Log($"📌 Short press detected, allowing click event");
                }
                else
                {
                    Debug.Log($"⏱ Long press detected, suppressing click event");
                    evt.StopPropagation();
                    suppressNextClick = true; // 次のクリックイベントを抑制
                }

                isLongPressing = false;
                longPressTime = 0f;
            }, TrickleDown.TrickleDown);

            // PointerLeaveEventで長押しをキャンセル（ボタンから離れた場合）
            button.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                if (isLongPressing)
                {
                    Debug.Log($"↖️ Pointer left {button.name}, cancelling long press");
                    isLongPressing = false;
                    longPressTime = 0f;
                }
            });
        }

        /// <summary>
        /// 削除ポップアップを表示
        /// </summary>
        void ShowDeletePopup(Button targetButton)
        {
            if (deletePopup == null)
            {
                Debug.LogError("❌ deletePopup is null!");
                return;
            }

            if (targetButton == null)
            {
                Debug.LogError("❌ targetButton is null!");
                return;
            }

            // ポップアップをボタンの上部に配置
            var buttonBounds = targetButton.worldBound;
            Debug.Log($"📍 Button bounds: x={buttonBounds.x}, y={buttonBounds.y}, width={buttonBounds.width}, height={buttonBounds.height}");

            // ポップアップサイズ: 120px x 80px (USSで定義)
            float popupWidth = 120f;
            float popupHeight = 90f; // 少し余裕を持たせる

            // ボタンの中央にポップアップを配置（水平方向）
            float popupLeft = buttonBounds.x + (buttonBounds.width / 2) - (popupWidth / 2);

            // ボタンの上に配置（垂直方向） - 10pxの余白
            float popupTop = buttonBounds.y - popupHeight - 10;

            Debug.Log($"📍 Popup position: left={popupLeft}, top={popupTop}");

            deletePopup.style.left = popupLeft;
            deletePopup.style.top = popupTop;
            deletePopup.style.display = DisplayStyle.Flex;

            Debug.Log($"📋 Delete popup shown for {targetButton.name}");
            Debug.Log($"📋 Popup display style: {deletePopup.style.display}");
            Debug.Log($"📋 Popup position type: {deletePopup.style.position}");

            // Heavy impact for popup appearance
            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        /// <summary>
        /// 削除ポップアップを非表示
        /// </summary>
        void HideDeletePopup()
        {
            if (deletePopup == null) return;

            deletePopup.style.display = DisplayStyle.None;
            currentLongPressButton = null;
            Debug.Log("❌ Delete popup hidden");

            // Light impact for cancel
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        /// <summary>
        /// 削除ボタンがクリックされた時の処理
        /// </summary>
        void OnDeleteButtonClicked()
        {
            if (currentLongPressButton == null || bottomButtonContainer == null)
            {
                HideDeletePopup();
                return;
            }

            Debug.Log($"🗑 Deleting button: {currentLongPressButton.name}");

            // ボタンを削除
            bottomButtonContainer.Remove(currentLongPressButton);
            HideDeletePopup();

            // Medium impact for deletion
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// Check if screen position is over UI Toolkit panel (top, side, or bottom)
        /// Issue #71: Unity Screen座標とUIToolkit座標の変換
        /// - Unity Screen: Y=0が画面下部、上に向かって増加
        /// - UIToolkit worldBound: Y=0が画面上部、下に向かって増加
        /// - PanelSettingsのScaleWithScreenSizeを考慮
        /// </summary>
        public bool IsPointOverUIPanel(Vector2 screenPosition)
        {
            if (root == null) return false;

            // RuntimePanelUtilsを使用してスクリーン座標をパネル座標に変換
            // これによりPanelSettingsのスケーリングが自動的に考慮される
            var uiDoc = GetComponent<UIDocument>();
            if (uiDoc == null || uiDoc.rootVisualElement == null) return false;

            // スクリーン座標をパネル座標に変換
            // UIToolkit: Y軸が上から下（画面上部が0）
            // Unity Screen: Y軸が下から上（画面下部が0）
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
                uiDoc.rootVisualElement.panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y)
            );

            // 汎用的なUIToolkit要素ヒット判定
            if (IsPointOverElement(topPanel, panelPosition, "topPanel")) return true;
            if (IsPointOverElement(sidePanel, panelPosition, "sidePanel")) return true;
            if (IsPointOverElement(bottomPanel, panelPosition, "bottomPanel")) return true;
            if (IsPointOverElement(captureButton, panelPosition, "captureButton")) return true;
            if (IsPointOverElement(galleryThumbnail, panelPosition, "galleryThumbnail")) return true;

            // Issue #120: ライティング/シャドウパネルのチェックを追加
            // バックドロップが表示中の場合、全画面をブロック
            if (settingsPanelBackdrop != null && settingsPanelBackdrop.ClassListContains("visible"))
            {
                Debug.Log($"[#71] Touch over settingsPanelBackdrop (visible)");
                return true;
            }

            // ライティングパネルオーバーレイ
            if (lightingPanelOverlay != null && lightingPanelOverlay.ClassListContains("visible"))
            {
                Debug.Log($"[#71] Touch over lightingPanelOverlay (visible)");
                return true;
            }

            // シャドウパネルオーバーレイ
            if (shadowPanelOverlay != null && shadowPanelOverlay.ClassListContains("visible"))
            {
                Debug.Log($"[#71] Touch over shadowPanelOverlay (visible)");
                return true;
            }

            // アラートバーのチェック
            if (alertBar != null && alertBar.ClassListContains("visible") && alertBar.worldBound.Contains(panelPosition))
            {
                Debug.Log($"[#71] Touch over alertBar");
                return true;
            }

            // アイコンプレビューパネルのチェック
            if (iconPreviewPanel != null && iconPreviewPanel.ClassListContains("visible"))
            {
                Debug.Log($"[#71] Touch over iconPreviewPanel (visible)");
                return true;
            }

            // ビューアオーバーレイのチェック
            if (viewerOverlay != null && viewerOverlay.resolvedStyle.display == DisplayStyle.Flex)
            {
                Debug.Log($"[#71] Touch over viewerOverlay (visible)");
                return true;
            }

            return false;
        }

        /// <summary>
        /// 指定された要素がタッチ位置に重なっているかチェック
        /// </summary>
        private bool IsPointOverElement(VisualElement element, Vector2 panelPosition, string debugName)
        {
            if (element == null) return false;

            // 要素の境界をチェック（worldBoundはパネル座標系）
            if (element.worldBound.Contains(panelPosition))
            {
                Debug.Log($"[#71] Touch over {debugName}: bounds={element.worldBound}, pos={panelPosition}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// ファイルピッカーを開く（複数形式対応）
        /// VRMFilePickerLoader.csのパターンに従った実装
        /// </summary>
        async void OpenFilePicker(Button targetButton)
        {
            Debug.Log($"📂 Opening file picker for button: {targetButton.name}");

            try
            {
#if UNITY_EDITOR
                // Unity Editor: VRMFilePickerLoader.csと同じパターンを使用
                Debug.Log($"💻 Opening Unity Editor file panel for VRM");

                // VRMファイルのみを選択（VRMFilePickerLoader.csと同じ実装）
                string path = UnityEditor.EditorUtility.OpenFilePanel("Select VRM File", "", "vrm");

                if (string.IsNullOrEmpty(path))
                {
                    Debug.Log("❌ File picker cancelled");
                    return;
                }

                Debug.Log($"✅ File selected: {path}");
                TapticEngine.Impact(TapticEngine.ImpactStyle.Light);

                // ファイルを非同期でロード
                await LoadFileAsync(path, targetButton);
#elif UNITY_IOS || UNITY_ANDROID
                // モバイル: VRMFilePickerLoader.csと同じパターンを使用
                Debug.Log($"📱 Opening NativeFilePicker...");

                var tcs = new System.Threading.Tasks.TaskCompletionSource<string>();

                string[] allowedFileTypes;

#if UNITY_IOS
                // iOS: UTI形式（VRMFilePickerLoader.csと同じ）
                allowedFileTypes = new string[] { "public.data", "public.content", "public.item" };
                Debug.Log("[FilePicker] iOS: Using UTI types for file picker");
#elif UNITY_ANDROID
                // Android: MIMEタイプ形式（VRMFilePickerLoader.csと同じ）
                allowedFileTypes = new string[] { "*/*" };
                Debug.Log("[FilePicker] Android: Using MIME type for file picker");
#endif

                Debug.Log($"🔍 Calling NativeFilePicker.PickFile...");

                NativeFilePicker.PickFile((path) =>
                {
                    Debug.Log($"[FilePicker] File picker callback: {path}");
                    tcs.SetResult(path);
                }, allowedFileTypes);

                Debug.Log("[FilePicker] Waiting for file selection...");
                string selectedPath = await tcs.Task;

                if (string.IsNullOrEmpty(selectedPath))
                {
                    Debug.Log("❌ File selection cancelled");
                    return;
                }

                Debug.Log($"✅ File selected: {selectedPath}");

                // VRMファイルかどうかを確認
                if (!selectedPath.ToLower().EndsWith(".vrm"))
                {
                    Debug.LogWarning($"⚠️ Selected file may not be a VRM file: {selectedPath}");
                    Debug.LogWarning("[FilePicker] Attempting to load anyway...");
                }

                TapticEngine.Impact(TapticEngine.ImpactStyle.Light);

                // ファイルを非同期でロード
                await LoadFileAsync(selectedPath, targetButton);
#endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"❌ Error opening file picker: {e.Message}");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 拡張子に基づいてファイルをロード
        /// </summary>
        async UniTask LoadFileAsync(string filePath, Button targetButton)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("❌ File path is null or empty");
                return;
            }

            // ファイルの存在確認
            if (!File.Exists(filePath))
            {
                Debug.LogError($"❌ File not found: {filePath}");
                return;
            }

            // 拡張子を取得して小文字に変換
            string extension = Path.GetExtension(filePath).ToLower();
            Debug.Log($"📄 File extension: {extension}");

            try
            {
                switch (extension)
                {
                    case ".vrm":
                    case ".glb":
                        // VRMとGLBは同じローダーで処理（VRMはGLBの拡張）
                        await LoadVRMFileAsync(filePath, targetButton);
                        break;

                    case ".fbx":
                        await LoadFBXFileAsync(filePath, targetButton);
                        break;

                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".gif":
                        Debug.LogWarning("⚠️ Image format is not yet supported");
                        // TODO: 将来的に実装
                        // await LoadImageFileAsync(filePath, targetButton);
                        break;

                    default:
                        Debug.LogError($"❌ Unsupported file format: {extension}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Failed to load file: {e.Message}");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// VRMファイルをロード
        /// </summary>
        async UniTask LoadVRMFileAsync(string filePath, Button targetButton)
        {
            if (avatarLoader == null)
            {
                Debug.LogError("❌ RuntimeAvatarLoader is not assigned!");
                return;
            }

            Debug.Log($"🎭 Loading VRM file: {filePath}");

            // Issue #73: プログレス表示開始
            StartSlotLoading(targetButton);
            UpdateSlotProgress(targetButton, 0.1f); // 10%: 開始

            try
            {
                // 既存のアバターをクリアしてから新しいVRMをロード
                Debug.Log("🗑️ Clearing existing avatar before loading new VRM...");
                avatarLoader.ClearCurrentAvatar();

                // PlaceAvatarOnPlaneOnlyのavatarもクリア
                var placer = FindFirstObjectByType<PlaceAvatarOnPlaneOnly>();
                if (placer != null)
                {
                    // Reflectionを使ってprivateフィールドにアクセス
                    var avatarField = typeof(PlaceAvatarOnPlaneOnly).GetField("avatar",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                    if (avatarField != null)
                    {
                        var existingAvatar = avatarField.GetValue(placer) as GameObject;
                        if (existingAvatar != null)
                        {
                            Debug.Log($"🗑️ Destroying existing avatar in PlaceAvatarOnPlaneOnly: {existingAvatar.name}");
                            Destroy(existingAvatar);
                            avatarField.SetValue(placer, null);
                        }
                    }
                }

                // Issue #73: プログレス更新
                UpdateSlotProgress(targetButton, 0.3f); // 30%: バイト読込開始

                // VRMをロード
                var avatar = await avatarLoader.LoadVRMFromPathAsync(filePath);

                if (avatar == null)
                {
                    Debug.LogError("❌ Failed to load VRM avatar");
                    CancelSlotLoading(targetButton); // Issue #73: キャンセル
                    return;
                }

                Debug.Log($"✅ VRM avatar loaded successfully: {avatar.name}");

                // Issue #407: デフォルトのAOCを適用
                ApplyDefaultAOC(avatar);

                // Issue #145/#411: 表情システムをセットアップ
                SetupExpressionSystem(avatar);

                // Issue #425: アバターをカメラの1m前方に配置
                PlaceAvatarAheadOfCamera(avatar);

                // Issue #442: ライティング・シャドウ設定を再適用
                ReapplyLightingSettings();

                // Issue #73: プログレス更新
                UpdateSlotProgress(targetButton, 0.7f); // 70%: VRM生成完了

                // レンダリングが安定するまで待機
                await UniTask.DelayFrame(3);

                // Issue #73: プログレス更新
                UpdateSlotProgress(targetButton, 0.85f); // 85%: 配置完了

                // サムネイルを生成（AvatarIconCaptureを使用）
                Debug.Log($"🖼 Starting thumbnail capture for: {avatar.name}");
                var thumbnail = await AICam.FBXLoader.AvatarIconCapture.Instance.CaptureAsTextureAsync(avatar);
                Debug.Log($"🖼 Thumbnail capture result: {(thumbnail != null ? $"{thumbnail.width}x{thumbnail.height}" : "NULL")}");

                // Issue #73: プログレス完了
                CompleteSlotLoading(targetButton);

                // スロットデータを保存
                if (!slotDataMap.ContainsKey(targetButton))
                {
                    slotDataMap[targetButton] = new SlotData();
                }
                var slotData = slotDataMap[targetButton];
                slotData.filePath = filePath;
                slotData.fileType = SlotFileType.VRM;
                slotData.thumbnail = thumbnail;
                slotData.loadedAvatar = avatar;

                Debug.Log($"💾 Slot data saved for {targetButton.name}: {filePath} (VRM)");

                if (thumbnail != null)
                {
                    // ボタンアイコンを更新
                    UpdateButtonIcon(targetButton, thumbnail);
                    Debug.Log($"🖼 Thumbnail generated and applied to button: {targetButton.name}");
                }

                // 選択状態を更新
                UpdateSlotSelection(targetButton);

                // Heavy impact for successful load
                TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error loading VRM: {e.Message}");
                Debug.LogException(e);
                CancelSlotLoading(targetButton); // Issue #73: エラー時はキャンセル
            }
        }

        /// <summary>
        /// FBXファイルをロード
        /// </summary>
        async UniTask LoadFBXFileAsync(string filePath, Button targetButton)
        {
            if (fbxLoaderBridge == null)
            {
                // RuntimeFBXLoaderBridgeを探す
                fbxLoaderBridge = FindFirstObjectByType<AICam.FBXLoader.RuntimeFBXLoaderBridge>();

                if (fbxLoaderBridge == null)
                {
                    Debug.LogError("❌ RuntimeFBXLoaderBridge is not found!");
                    return;
                }
            }

            Debug.Log($"📦 Loading FBX file: {filePath}");

            // Issue #73: プログレス表示開始
            StartSlotLoading(targetButton);
            UpdateSlotProgress(targetButton, 0.1f); // 10%: 開始

            bool loadSuccess = false;
            var tcs = new UniTaskCompletionSource();

            try
            {
                // RuntimeFBXLoaderBridgeを使用してFBXをロード
                fbxLoaderBridge.StartRuntimeLoadFromPath(
                    filePath,
                    -1,  // スロットインデックスは使わない
                    null, // アイコンパスは自前で処理
                    progress =>
                    {
                        Debug.Log($"📦 FBX load progress: {progress}%");
                        // Issue #73: FBXローダーの進捗をUIに反映（0-100を0.1-0.9にマップ）
                        UpdateSlotProgress(targetButton, 0.1f + (progress / 100f) * 0.8f);
                    },
                    success =>
                    {
                        loadSuccess = success;
                        tcs.TrySetResult();
                    }
                );

                await tcs.Task;

                if (!loadSuccess)
                {
                    Debug.LogError("❌ Failed to load FBX");
                    CancelSlotLoading(targetButton); // Issue #73: キャンセル
                    return;
                }

                var loadedModel = fbxLoaderBridge.CurrentModel;
                if (loadedModel == null)
                {
                    Debug.LogError("❌ FBX model is null after loading");
                    CancelSlotLoading(targetButton); // Issue #73: キャンセル
                    return;
                }

                Debug.Log($"✅ FBX loaded successfully: {loadedModel.name}");

                // Issue #407: デフォルトのAOCを適用
                ApplyDefaultAOC(loadedModel);

                // Issue #145/#411: 表情システムをセットアップ
                SetupExpressionSystem(loadedModel);

                // Issue #425: アバターをカメラの1m前方に配置
                PlaceAvatarAheadOfCamera(loadedModel);

                // Issue #442: ライティング・シャドウ設定を再適用
                // Note: RuntimeFBXLoaderBridgeでも呼ばれるが、全セットアップ完了後にも再適用
                ReapplyLightingSettings();

                // Issue #73: プログレス更新
                UpdateSlotProgress(targetButton, 0.9f); // 90%: FBX生成完了

                // レンダリングが安定するまで待機
                await UniTask.DelayFrame(3);

                // サムネイルを生成（AvatarIconCaptureを使用）
                Debug.Log($"🖼 Starting thumbnail capture for: {loadedModel.name}");
                Texture2D thumbnail = await AICam.FBXLoader.AvatarIconCapture.Instance.CaptureAsTextureAsync(loadedModel);
                Debug.Log($"🖼 Thumbnail capture result: {(thumbnail != null ? $"{thumbnail.width}x{thumbnail.height}" : "NULL")}");

                // Issue #73: プログレス完了
                CompleteSlotLoading(targetButton);

                // スロットデータを保存
                if (!slotDataMap.ContainsKey(targetButton))
                {
                    slotDataMap[targetButton] = new SlotData();
                }
                var slotData = slotDataMap[targetButton];
                slotData.filePath = filePath;
                slotData.fileType = SlotFileType.FBX;
                slotData.thumbnail = thumbnail;
                slotData.loadedAvatar = loadedModel;

                Debug.Log($"💾 Slot data saved for {targetButton.name}: {filePath} (FBX)");

                if (thumbnail != null)
                {
                    UpdateButtonIcon(targetButton, thumbnail);
                    Debug.Log($"🖼 Thumbnail generated and applied to button: {targetButton.name}");
                }

                // 選択状態を更新
                UpdateSlotSelection(targetButton);

                // Heavy impact for successful load
                TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error loading FBX: {e.Message}");
                Debug.LogException(e);
                CancelSlotLoading(targetButton); // Issue #73: エラー時はキャンセル
            }
        }

        /// <summary>
        /// スロットクリック時の処理
        /// </summary>
        void OnSlotClicked(Button button)
        {
            // スロットデータを取得
            if (!slotDataMap.TryGetValue(button, out var slotData))
            {
                slotData = null;
            }

            if (slotData != null && slotData.IsConfigured)
            {
                // 設定済みスロット → アバターを切り替え
                Debug.Log($"🔄 Switching to avatar in slot: {button.name}");
                SwitchToSlotAvatar(button, slotData);
            }
            else
            {
                // 空のスロット → ファイルピッカーを開く
                Debug.Log($"📂 Empty slot, opening file picker: {button.name}");
                OpenFilePicker(button);
            }
        }

        /// <summary>
        /// スロットのアバターに切り替え
        /// </summary>
        async void SwitchToSlotAvatar(Button button, SlotData slotData)
        {
            if (slotData == null || !slotData.IsConfigured) return;

            // 既に選択中のスロットなら何もしない
            if (currentSelectedSlot == button)
            {
                Debug.Log($"🔄 Already selected slot: {button.name}");
                return;
            }

            // 選択状態を更新
            UpdateSlotSelection(button);

            // アバターがまだロードされていない場合はロード
            if (slotData.loadedAvatar == null)
            {
                Debug.Log($"🔄 Avatar not loaded, loading from: {slotData.filePath}");
                if (slotData.fileType == SlotFileType.VRM)
                {
                    await LoadVRMFileAsync(slotData.filePath, button);
                }
                else if (slotData.fileType == SlotFileType.FBX)
                {
                    await LoadFBXFileAsync(slotData.filePath, button);
                }
            }
            else
            {
                // 既存のアバターを非表示にして、このスロットのアバターを表示
                Debug.Log($"🔄 Activating avatar: {slotData.loadedAvatar.name}");
                ActivateSlotAvatar(slotData);
            }

            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// スロットの選択状態を更新
        /// </summary>
        void UpdateSlotSelection(Button selectedButton)
        {
            // 前の選択を解除
            if (currentSelectedSlot != null)
            {
                currentSelectedSlot.RemoveFromClassList("selected");
            }

            // 新しい選択を設定
            currentSelectedSlot = selectedButton;
            if (currentSelectedSlot != null)
            {
                currentSelectedSlot.AddToClassList("selected");
            }
        }

        /// <summary>
        /// スロットのアバターをアクティブにする
        /// </summary>
        void ActivateSlotAvatar(SlotData slotData)
        {
            // 全スロットのアバターを非表示
            foreach (var kvp in slotDataMap)
            {
                if (kvp.Value?.loadedAvatar != null)
                {
                    kvp.Value.loadedAvatar.SetActive(false);
                }
            }

            // 選択したスロットのアバターを表示
            if (slotData?.loadedAvatar != null)
            {
                slotData.loadedAvatar.SetActive(true);

                // Issue #407: キャッシュを更新してポーズ切り替えが動作するようにする
                cachedCurrentAvatar = slotData.loadedAvatar;
                Debug.Log($"🎭 Updated cachedCurrentAvatar: {cachedCurrentAvatar.name}");
            }
        }

        /// <summary>
        /// ボタンのアイコンを更新（背景画像として直接設定）
        /// </summary>
        void UpdateButtonIcon(Button button, Texture2D texture)
        {
            if (button == null)
            {
                Debug.LogWarning("⚠️ UpdateButtonIcon: button is null");
                return;
            }

            if (texture == null)
            {
                Debug.LogWarning($"⚠️ UpdateButtonIcon: texture is null for {button.name}");
                return;
            }

            Debug.Log($"🖼 UpdateButtonIcon: Setting texture {texture.width}x{texture.height} to {button.name}");

            // ボタン自体の背景画像としてサムネイルを設定
            button.style.backgroundImage = new StyleBackground(texture);

            // has-iconクラスを追加してUSSスタイルを適用
            button.AddToClassList("has-icon");

            Debug.Log($"✅ Button icon updated for {button.name}");
        }

        /// <summary>
        /// サイドバーボタン1（Preference）クリック時の処理
        /// </summary>
        void OnSideButton1Clicked()
        {
            Debug.Log("⚙️ Side button 1 (Preference) clicked");
            TapticEngine.Selection();

            // ここに設定画面を開く処理を追加
        }

        /// <summary>
        /// サイドバーボタン2（アスペクト比）クリック時の処理
        /// Full → 16:9 → 3:2 → Full のようにトグル
        /// </summary>
        void OnSideButton2Clicked()
        {
            Debug.Log("📐 Side button 2 (Aspect Ratio) clicked");
            TapticEngine.Selection();

            // アスペクト比ステートをトグル
            aspectRatioState = (aspectRatioState + 1) % aspectRatioIcons.Length;

            // アイコンを更新
            if (sideButton2 != null)
            {
                var iconPath = aspectRatioIcons[aspectRatioState];
                var icon = Resources.Load<Texture2D>(iconPath);

                if (icon != null)
                {
                    sideButton2.style.backgroundImage = new StyleBackground(icon);
                    Debug.Log($"✅ Aspect ratio changed to: {iconPath}");
                }
                else
                {
                    Debug.LogWarning($"⚠️ Icon not found: {iconPath}");
                }
            }

            // アスペクト比マスクを更新
            UpdateAspectMask();

            // ARPhotoControllerにアスペクト比を設定
            if (photoController != null)
            {
                float targetAspect = aspectRatios[aspectRatioState];
                photoController.SetAspectRatio(targetAspect);
            }
        }

        /// <summary>
        /// サイドバーボタン3（Flash）クリック時の処理
        /// </summary>
        void OnSideButton3Clicked()
        {
            Debug.Log("⚡ Side button 3 (Flash) clicked");
            TapticEngine.Selection();

            // ここにフラッシュ切り替え処理を追加
        }

        /// <summary>
        /// Issue #413: バグレポートボタンクリック時の処理
        /// </summary>
        void OnBugReportButtonClicked()
        {
            Debug.Log("🐛 Bug report button clicked");
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);

            // BugReportManagerを使用してバグレポートを開始
            var bugReportManager = AICam.BugReport.BugReportManager.Instance;
            if (bugReportManager == null)
            {
                // Findで探す
                bugReportManager = FindFirstObjectByType<AICam.BugReport.BugReportManager>();
            }

            if (bugReportManager == null)
            {
                // 存在しない場合は自動生成
                Debug.Log("🐛 BugReportManager not found, creating one...");
                var go = new GameObject("BugReportManager");
                bugReportManager = go.AddComponent<AICam.BugReport.BugReportManager>();
            }

            bugReportManager.StartBugReport();
        }

        /// <summary>
        /// Issue #74 修正: トップボタン1 クリック時の処理
        /// ライティングパネルを表示（パネル内にLight Estimation ON/OFFトグルあり）
        /// </summary>
        void OnTopButton1Click()
        {
            Debug.Log("💡 Top button 1 clicked: Opening Lighting Panel");
            TapticEngine.Selection();
            ShowLightingPanel();
        }

        /// <summary>
        /// Issue #75 修正: トップボタン2 クリック時の処理
        /// シャドウパネルを表示（パネル内にShadow ON/OFFトグルあり）
        /// </summary>
        void OnTopButton2Click()
        {
            Debug.Log("🌑 Top button 2 clicked: Opening Shadow Panel");
            TapticEngine.Selection();
            ShowShadowPanel();
        }

        /// <summary>
        /// Issue #345: トップボタン5 クリック時の処理
        /// 平面表示/非表示を切り替え
        /// </summary>
        void OnTopButton5Click()
        {
            Debug.Log($"🔲 Top button 5 clicked: Toggle Plane Visibility (current: {isPlaneVisible})");
            TapticEngine.Selection();
            TogglePlaneVisibility();
        }

        /// <summary>
        /// Issue #345: 平面表示/非表示を切り替え
        /// </summary>
        void TogglePlaneVisibility()
        {
            isPlaneVisible = !isPlaneVisible;
            Debug.Log($"🔲 Plane visibility toggled to: {isPlaneVisible}");

            // ARPlaneVisibilityControllerを取得して表示を切り替え
            if (cachedPlaneVisibilityController == null)
            {
                cachedPlaneVisibilityController = FindFirstObjectByType<ARPlaneVisibilityController>();
            }

            if (cachedPlaneVisibilityController != null)
            {
                cachedPlaneVisibilityController.SetPlanesVisible(isPlaneVisible);
                Debug.Log($"✅ Plane visibility set to: {isPlaneVisible}");
            }
            else
            {
                Debug.LogWarning("⚠️ ARPlaneVisibilityController not found in scene");
            }

            // アイコンを更新
            UpdatePlaneVisibilityIcon();
        }

        /// <summary>
        /// Issue #345: 平面表示ボタンのアイコンを更新
        /// </summary>
        void UpdatePlaneVisibilityIcon()
        {
            if (topButton5 == null) return;

            // 表示状態に応じてスタイルを切り替え
            if (isPlaneVisible)
            {
                topButton5.RemoveFromClassList("plane-hidden");
                topButton5.AddToClassList("plane-visible");
            }
            else
            {
                topButton5.RemoveFromClassList("plane-visible");
                topButton5.AddToClassList("plane-hidden");
            }

            Debug.Log($"🔲 Plane button icon updated: {(isPlaneVisible ? "visible" : "hidden")}");
        }

        /// <summary>
        /// Issue #407: アバタースロットロード完了時のハンドラ
        /// </summary>
        void OnAvatarSlotLoadComplete(int slotIndex, bool success)
        {
            Debug.Log($"🎭 OnAvatarSlotLoadComplete called: slotIndex={slotIndex}, success={success}");
            if (success)
            {
                // AvatarMemoryCacheから現在のアバターを取得してキャッシュ
                var memoryCache = AICam.FBXLoader.AvatarMemoryCache.Instance;
                if (memoryCache != null)
                {
                    cachedCurrentAvatar = memoryCache.GetCachedAvatar(slotIndex);
                    currentPoseIndex = 0;  // ポーズインデックスをリセット
                    cachedStateNames = null;  // State名キャッシュをリセット
                    Debug.Log($"🎭 Avatar cached from slot {slotIndex}: {(cachedCurrentAvatar != null ? cachedCurrentAvatar.name : "null")}");

                    // Issue #407: PoseAnimatorController/OverrideControllerを設定
                    if (cachedCurrentAvatar != null)
                    {
                        AssignPoseAnimatorController(cachedCurrentAvatar);
                    }
                }
            }
        }

        /// <summary>
        /// Issue #407: アバターにAnimatorOverrideControllerを設定
        /// </summary>
        void AssignPoseAnimatorController(GameObject avatar)
        {
            // ApplyDefaultAOCに委譲
            ApplyDefaultAOC(avatar);
        }

        /// <summary>
        /// Issue #407: アバタースロットクリア時のハンドラ
        /// </summary>
        void OnAvatarSlotCleared(int slotIndex)
        {
            cachedCurrentAvatar = null;
            cachedStateNames = null;
            currentPoseIndex = 0;
            Debug.Log($"🎭 Avatar cache cleared (slot {slotIndex} was cleared)");
        }

        /// <summary>
        /// Issue #33/#405: topButton3クリック時の表情切り替え
        /// ダブルタップで表情リセット、シングルタップで次の表情に切り替え
        /// </summary>
        void OnTopButton3Click()
        {
            expressionTapCount++;
            Debug.Log($"🔘 topButton3 clicked - expressionTapCount: {expressionTapCount}");

            if (expressionTapCount == 1)
            {
                // 1回目のタップ - 遅延処理を開始
                expressionTapCts?.Cancel();
                expressionTapCts = new System.Threading.CancellationTokenSource();
                HandleExpressionTapAsync(expressionTapCts.Token).Forget();
            }
            // 2回目以降のタップはexpressionTapCountが増えるだけ（HandleExpressionTapAsyncで処理）
        }

        async UniTaskVoid HandleExpressionTapAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                // ダブルタップ待機
                await UniTask.Delay((int)(DOUBLE_TAP_THRESHOLD * 1000), cancellationToken: ct);

                // 待機完了後、タップ数に応じて処理
                int finalTapCount = expressionTapCount;
                expressionTapCount = 0;  // リセット

                if (finalTapCount >= 2)
                {
                    // ダブルタップ → 表情リセット（Neutral）
                    Debug.Log("🔘 Double tap detected! Resetting expression to neutral...");
                    TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
                    ResetExpression();
                }
                else
                {
                    // シングルタップ → 次の表情
                    Debug.Log("🔘 Single tap confirmed - Switching expression...");
                    TapticEngine.Selection();
                    SwitchToNextExpression();
                }
            }
            catch (System.OperationCanceledException)
            {
                // キャンセルされた場合は何もしない
            }
        }

        /// <summary>
        /// Issue #33/#405: 次の表情に切り替え
        /// VRM 1.0とVRM 0.xの両方に対応
        /// </summary>
        void SwitchToNextExpression()
        {
            Debug.Log("😊 SwitchToNextExpression called");

            // VRM 0.xを優先チェック（より一般的）
            if (vrm0ExpressionController != null)
            {
                int indexBefore = vrm0ExpressionController.CurrentExpressionIndex;
                vrm0ExpressionController.NextExpression();
                int indexAfter = vrm0ExpressionController.CurrentExpressionIndex;
                Debug.Log($"😊 VRM 0.x Expression switched: {indexBefore} → {indexAfter}, Name: {vrm0ExpressionController.CurrentExpressionName}");
                return;
            }

            // VRM 1.0をチェック
            if (expressionSetup == null)
            {
                expressionSetup = FindFirstObjectByType<AICam.Expression.VrmExpressionSetup>();
            }

            if (expressionSetup != null)
            {
                var controller = expressionSetup.CurrentExpressionController;
                if (controller != null)
                {
                    int indexBefore = controller.CurrentExpressionIndex;
                    expressionSetup.NextExpression();
                    int indexAfter = controller.CurrentExpressionIndex;
                    Debug.Log($"😊 VRM 1.0 Expression switched: {indexBefore} → {indexAfter}, Name: {controller.CurrentExpressionName}");
                    return;
                }
            }

            Debug.LogWarning("😊 No expression controller available - load a VRM avatar first");
        }

        /// <summary>
        /// Issue #33/#405: 表情をリセット（Neutral）
        /// VRM 1.0とVRM 0.xの両方に対応
        /// </summary>
        void ResetExpression()
        {
            Debug.Log("😊 ResetExpression called");

            // VRM 0.xを優先チェック
            if (vrm0ExpressionController != null)
            {
                vrm0ExpressionController.ResetToNeutral();
                Debug.Log("😊 VRM 0.x Expression reset to neutral");
                return;
            }

            // VRM 1.0をチェック
            if (expressionSetup == null)
            {
                expressionSetup = FindFirstObjectByType<AICam.Expression.VrmExpressionSetup>();
            }

            if (expressionSetup != null)
            {
                var controller = expressionSetup.CurrentExpressionController;
                if (controller != null)
                {
                    expressionSetup.ResetExpression();
                    Debug.Log("😊 VRM 1.0 Expression reset to neutral");
                    return;
                }
            }

            Debug.LogWarning("😊 No expression controller available - load a VRM avatar first");
        }

        /// <summary>
        /// Issue #407: topButton4クリック時のポーズ切り替え
        /// ダブルタップでOverrideController切り替え、シングルタップでポーズ切り替え
        /// </summary>
        void OnTopButton4Click()
        {
            tapCount++;
            Debug.Log($"🔘 topButton4 clicked - tapCount: {tapCount}");

            if (tapCount == 1)
            {
                // 1回目のタップ - 遅延処理を開始
                tapCts?.Cancel();
                tapCts = new System.Threading.CancellationTokenSource();
                HandleTapAsync(tapCts.Token).Forget();
            }
            // 2回目以降のタップはtapCountが増えるだけ（HandleTapAsyncで処理）
        }

        async UniTaskVoid HandleTapAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                // ダブルタップ待機
                await UniTask.Delay((int)(DOUBLE_TAP_THRESHOLD * 1000), cancellationToken: ct);

                // 待機完了後、タップ数に応じて処理
                int finalTapCount = tapCount;
                tapCount = 0;  // リセット

                if (finalTapCount >= 2)
                {
                    // ダブルタップ
                    Debug.Log("🔘 Double tap detected! Switching OverrideController...");
                    TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
                    SwitchToNextOverrideController();
                }
                else
                {
                    // シングルタップ
                    Debug.Log("🔘 Single tap confirmed - Switching pose...");
                    TapticEngine.Selection();
                    SwitchToNextPose();
                }
            }
            catch (System.OperationCanceledException)
            {
                // キャンセルされた場合は何もしない
            }
        }

        /// <summary>
        /// Issue #407: 次のポーズに切り替え
        /// </summary>
        void SwitchToNextPose()
        {
            Debug.Log("🎭 SwitchToNextPose called");

            GameObject avatar = null;

            // 方法0: キャッシュされたアバターを使用（最優先）
            if (cachedCurrentAvatar != null && cachedCurrentAvatar.activeInHierarchy)
            {
                avatar = cachedCurrentAvatar;
                Debug.Log($"🎭 Using cached avatar: {avatar.name}");
            }

            // 方法1: AvatarSlotManager + AvatarMemoryCacheから取得
            if (avatar == null)
            {
                var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                var memoryCache = AICam.FBXLoader.AvatarMemoryCache.Instance;

                if (slotManager != null && memoryCache != null)
                {
                    int currentSlot = slotManager.CurrentSlotIndex;
                    Debug.Log($"🎭 CurrentSlotIndex: {currentSlot}");

                    if (currentSlot >= 0)
                    {
                        avatar = memoryCache.GetCachedAvatar(currentSlot);
                        if (avatar != null)
                        {
                            cachedCurrentAvatar = avatar;  // キャッシュを更新
                        }
                        Debug.Log($"🎭 From MemoryCache: {(avatar != null ? avatar.name : "null")}");
                    }
                }
            }

            // 方法2: RuntimeFBXLoaderBridgeから取得（フォールバック）
            if (avatar == null)
            {
                if (fbxLoaderBridge == null)
                {
                    fbxLoaderBridge = FindFirstObjectByType<AICam.FBXLoader.RuntimeFBXLoaderBridge>();
                }
                if (fbxLoaderBridge != null)
                {
                    avatar = fbxLoaderBridge.CurrentModel;
                    if (avatar != null)
                    {
                        cachedCurrentAvatar = avatar;  // キャッシュを更新
                    }
                    Debug.Log($"🎭 From RuntimeFBXLoaderBridge: {(avatar != null ? avatar.name : "null")}");
                }
            }

            // 方法3: シーン内のAnimatorを持つアクティブなアバターを検索（最終フォールバック）
            if (avatar == null)
            {
                var animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
                foreach (var anim in animators)
                {
                    // Humanoidアバターを探す
                    if (anim.avatar != null && anim.avatar.isHuman && anim.gameObject.activeInHierarchy)
                    {
                        avatar = anim.gameObject;
                        cachedCurrentAvatar = avatar;  // キャッシュを更新
                        Debug.Log($"🎭 Found Humanoid avatar in scene: {avatar.name}");
                        break;
                    }
                }
            }

            Animator animator = null;
            if (avatar != null)
            {
                animator = avatar.GetComponent<Animator>();
            }
            Debug.Log($"🎭 avatar: {(avatar != null ? avatar.name : "null")}, animator: {animator != null}");

            if (avatar == null)
            {
                Debug.LogWarning("⚠️ No avatar placed");
                return;
            }

            if (animator == null)
            {
                animator = avatar.GetComponent<Animator>();
                if (animator == null)
                {
                    Debug.LogWarning("⚠️ Avatar has no Animator component");
                    return;
                }
            }

            // AnimatorControllerのClip一覧を取得
            var controller = animator.runtimeAnimatorController;
            Debug.Log($"🎭 runtimeAnimatorController: {(controller != null ? controller.name : "null")}");

            // OverrideControllerが設定されていない場合、b010を自動設定
            if (poseOverrideControllers != null && poseOverrideControllers.Length > 0 && poseOverrideControllers[0] != null)
            {
                bool isOverrideController = controller is AnimatorOverrideController;
                if (!isOverrideController)
                {
                    animator.runtimeAnimatorController = poseOverrideControllers[0];
                    controller = animator.runtimeAnimatorController;
                    currentOverrideIndex = 0;
                    currentPoseIndex = 0;
                    Debug.Log($"🎭 Auto-assigned OverrideController: {poseOverrideControllers[0].name}");
                }
            }

            if (controller == null)
            {
                Debug.LogWarning("⚠️ Animator has no RuntimeAnimatorController");
                return;
            }

            // PoseAnimatorControllerのState名は固定（Pose00〜Pose11）
            // ランタイムではAnimatorControllerのState名を直接取得できないため、固定配列を使用
            const int POSE_COUNT = 12;

            // 次のポーズインデックスに進む
            int previousIndex = currentPoseIndex;
            currentPoseIndex = (currentPoseIndex + 1) % POSE_COUNT;
            var targetState = $"Pose{currentPoseIndex:D2}";

            Debug.Log($"🎭 Pose: {targetState} ({currentPoseIndex + 1}/{POSE_COUNT})");

            // Pose11からPose00に戻った場合はアラートバーを表示
            if (previousIndex == POSE_COUNT - 1 && currentPoseIndex == 0)
            {
                ShowInfo("Pose", "Loop - Back to Pose00", 1.5f);
            }

            // State名で再生
            animator.Play(targetState, 0, 0f);
        }

        /// <summary>
        /// Issue #407: 次のOverrideControllerに切り替え（ダブルタップ時）
        /// PoseSlotControllerを使用して切り替えを行う
        /// </summary>
        void SwitchToNextOverrideController()
        {
            Debug.Log($"🎭 SwitchToNextOverrideController called");

            // PoseSlotControllerを使用
            if (poseSlotController != null)
            {
                // アバターが変わっている場合、PoseSlotControllerを更新
                EnsurePoseSlotControllerSetup();

                poseSlotController.NextOverride();
                currentOverrideIndex = poseSlotController.CurrentOverrideIndex;
                currentPoseIndex = 0;  // ポーズインデックスをリセット

                Debug.Log($"🎭 PoseSlotController.NextOverride() - index: {currentOverrideIndex}, name: {poseSlotController.CurrentOverrideName}");
                ShowInfo("Change", poseSlotController.CurrentOverrideName, 2f);
                return;
            }

            // フォールバック: PoseSlotControllerがない場合は従来の実装
            Debug.Log($"🎭 Fallback: poseOverrideControllers: {(poseOverrideControllers != null ? poseOverrideControllers.Length.ToString() : "null")}");

            if (poseOverrideControllers == null || poseOverrideControllers.Length == 0)
            {
                Debug.LogWarning("⚠️ No OverrideControllers configured - please set poseOverrideControllers in Inspector");
                return;
            }

            // アバター取得
            GameObject avatar = cachedCurrentAvatar;
            if (avatar == null || !avatar.activeInHierarchy)
            {
                // アバターを検索
                var animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
                foreach (var anim in animators)
                {
                    if (anim.avatar != null && anim.avatar.isHuman && anim.gameObject.activeInHierarchy)
                    {
                        avatar = anim.gameObject;
                        cachedCurrentAvatar = avatar;
                        break;
                    }
                }
            }

            if (avatar == null)
            {
                Debug.LogWarning("⚠️ No avatar found for OverrideController switch");
                return;
            }

            var animator = avatar.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning("⚠️ Avatar has no Animator component");
                return;
            }

            // 次のOverrideControllerに進む
            currentOverrideIndex = (currentOverrideIndex + 1) % poseOverrideControllers.Length;
            var nextOverride = poseOverrideControllers[currentOverrideIndex];

            if (nextOverride == null)
            {
                Debug.LogWarning($"⚠️ OverrideController at index {currentOverrideIndex} is null");
                return;
            }

            // OverrideControllerを適用
            var previousController = animator.runtimeAnimatorController;
            Debug.Log($"🎭 Before switch - current controller: {(previousController != null ? previousController.name : "null")}");

            animator.runtimeAnimatorController = nextOverride;

            Debug.Log($"🎭 After switch - new controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "null")}");

            // ポーズインデックスをリセットしてPose00を再生
            currentPoseIndex = 0;
            animator.Play("Pose00", 0, 0f);

            // State名キャッシュをクリア（新しいコントローラー用に再取得）
            cachedStateNames = null;

            Debug.Log($"🎭 Switched to OverrideController: {nextOverride.name} ({currentOverrideIndex + 1}/{poseOverrideControllers.Length})");

            // 水色のアラートバーで表示
            ShowInfo("Change", nextOverride.name, 2f);
        }

        /// <summary>
        /// PoseSlotControllerのセットアップを確認・更新
        /// アバター変更時にTargetAnimatorを更新する
        /// </summary>
        void EnsurePoseSlotControllerSetup()
        {
            if (poseSlotController == null) return;

            // 現在のアバターを取得
            GameObject avatar = GetCurrentAvatar();
            if (avatar == null) return;

            var animator = avatar.GetComponent<Animator>();
            if (animator == null) return;

            // TargetAnimatorが異なる場合は更新
            if (poseSlotController.TargetAnimator != animator)
            {
                Debug.Log($"🎭 Updating PoseSlotController.TargetAnimator to: {avatar.name}");
                poseSlotController.TargetAnimator = animator;

                // OverrideControllersを設定（未設定の場合）
                if (poseSlotController.OverrideCount == 0 && poseOverrideControllers != null)
                {
                    poseSlotController.SetOverrideControllers(poseOverrideControllers);
                    Debug.Log($"🎭 Set {poseOverrideControllers.Length} override controllers to PoseSlotController");
                }
            }
        }

        /// <summary>
        /// 現在のアバターを取得するヘルパーメソッド
        /// </summary>
        GameObject GetCurrentAvatar()
        {
            // キャッシュされたアバターを優先
            if (cachedCurrentAvatar != null && cachedCurrentAvatar.activeInHierarchy)
            {
                return cachedCurrentAvatar;
            }

            // AvatarSlotManager + AvatarMemoryCacheから取得
            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            var memoryCache = AICam.FBXLoader.AvatarMemoryCache.Instance;

            if (slotManager != null && memoryCache != null)
            {
                int currentSlot = slotManager.CurrentSlotIndex;
                if (currentSlot >= 0)
                {
                    var avatar = memoryCache.GetCachedAvatar(currentSlot);
                    if (avatar != null)
                    {
                        cachedCurrentAvatar = avatar;
                        return avatar;
                    }
                }
            }

            // RuntimeFBXLoaderBridgeから取得
            if (fbxLoaderBridge != null && fbxLoaderBridge.CurrentModel != null)
            {
                cachedCurrentAvatar = fbxLoaderBridge.CurrentModel;
                return cachedCurrentAvatar;
            }

            // シーン内検索
            var animators = FindObjectsByType<Animator>(FindObjectsSortMode.None);
            foreach (var anim in animators)
            {
                if (anim.avatar != null && anim.avatar.isHuman && anim.gameObject.activeInHierarchy)
                {
                    cachedCurrentAvatar = anim.gameObject;
                    return cachedCurrentAvatar;
                }
            }

            return null;
        }

        /// <summary>
        /// Issue #425: アバターをカメラの1m前方に配置
        /// PlaceAvatarOnPlaneOnlyを使用して平面優先で配置
        /// </summary>
        void PlaceAvatarAheadOfCamera(GameObject avatar)
        {
            if (avatar == null) return;

            var placer = FindFirstObjectByType<PlaceAvatarOnPlaneOnly>();
            if (placer != null)
            {
                bool success = placer.PlaceAvatarAhead(avatar, 1.0f);
                Debug.Log($"📍 Issue #425: Avatar placement result: {(success ? "success" : "failed")}");
            }
            else
            {
                Debug.LogWarning("⚠️ PlaceAvatarOnPlaneOnly not found - avatar position unchanged");
            }
        }

        /// <summary>
        /// Issue #442: ライティング・シャドウ設定を再適用
        /// アバターロード後に呼び出して、新しいマテリアルに設定を適用する
        ///
        /// 修正: FindFirstObjectByType ではなく GetLightingPanelController() を使用
        /// LightingPanelController は遅延初期化されるため、直接検索すると null になる
        ///
        /// public: RuntimeFBXLoaderBridge からも呼び出せるように公開
        /// </summary>
        public void ReapplyLightingSettings()
        {
            var lightingPanel = GetLightingPanelController();
            if (lightingPanel != null)
            {
                lightingPanel.ReapplyAllSettings();
                Debug.Log("💡 Issue #442: Reapplied lighting and shadow settings");
            }
            else
            {
                Debug.LogWarning("⚠️ LightingPanelController could not be initialized");
            }
        }

        /// <summary>
        /// Issue #407: アバターロード時にデフォルトのAOCを適用
        /// </summary>
        void ApplyDefaultAOC(GameObject avatar)
        {
            if (avatar == null)
            {
                Debug.LogWarning("⚠️ ApplyDefaultAOC: avatar is null");
                return;
            }

            if (poseOverrideControllers == null || poseOverrideControllers.Length == 0)
            {
                Debug.LogWarning("⚠️ ApplyDefaultAOC: No OverrideControllers configured");
                return;
            }

            var animator = avatar.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning($"⚠️ ApplyDefaultAOC: Avatar {avatar.name} has no Animator component");
                return;
            }

            // 最初のAOC（p012 = デフォルト）を適用
            var defaultAOC = poseOverrideControllers[0];
            if (defaultAOC == null)
            {
                Debug.LogWarning("⚠️ ApplyDefaultAOC: First OverrideController is null");
                return;
            }

            animator.runtimeAnimatorController = defaultAOC;
            currentOverrideIndex = 0;
            currentPoseIndex = 0;
            cachedCurrentAvatar = avatar;

            // 初期ポーズを再生
            animator.Play("Pose00", 0, 0f);

            Debug.Log($"🎭 ApplyDefaultAOC: Applied {defaultAOC.name} to {avatar.name}");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Issue #439: デバッグビルドのみAOC適用メッセージを表示
            ShowInfo("AOC", defaultAOC.name, 1.5f);
#endif
        }

        // Issue #145/#411: VRM 0.x用の表情コントローラー
        private AICam.Expression.Vrm0ExpressionController vrm0ExpressionController;

        /// <summary>
        /// Issue #145/#411: VRM表情システムをセットアップ
        /// VRM 1.0とVRM 0.xの両方に対応
        /// </summary>
        void SetupExpressionSystem(GameObject avatar)
        {
            if (avatar == null) return;

            Debug.Log($"🎭 SetupExpressionSystem: Starting setup for {avatar.name}");

            // VRM 1.0を確認
            var vrm10Instance = avatar.GetComponent<UniVRM10.Vrm10Instance>();
            if (vrm10Instance != null)
            {
                Debug.Log($"🎭 SetupExpressionSystem: VRM 1.0 detected");
                SetupVrm10ExpressionSystem(avatar, vrm10Instance);
                return;
            }

            // VRM 0.xを確認
            var blendShapeProxy = avatar.GetComponent<global::VRM.VRMBlendShapeProxy>();
            if (blendShapeProxy != null)
            {
                Debug.Log($"🎭 SetupExpressionSystem: VRM 0.x detected");
                SetupVrm0ExpressionSystem(avatar, blendShapeProxy);
                return;
            }

            Debug.LogWarning($"🎭 SetupExpressionSystem: {avatar.name} is not a VRM avatar - no VRM expression support");
        }

        /// <summary>
        /// VRM 1.0用の表情システムセットアップ
        /// </summary>
        void SetupVrm10ExpressionSystem(GameObject avatar, UniVRM10.Vrm10Instance vrmInstance)
        {
            // VrmExpressionSetupを検索、なければ作成
            if (expressionSetup == null)
            {
                expressionSetup = FindFirstObjectByType<AICam.Expression.VrmExpressionSetup>();

                if (expressionSetup == null)
                {
                    var setupObj = new GameObject("VrmExpressionSetup");
                    expressionSetup = setupObj.AddComponent<AICam.Expression.VrmExpressionSetup>();
                    Debug.Log($"🎭 SetupExpressionSystem: Created new VrmExpressionSetup for VRM 1.0");
                }
            }

            if (expressionSetup != null)
            {
                expressionSetup.OnVrmLoaded(avatar);

                var controller = expressionSetup.CurrentExpressionController;
                if (controller != null)
                {
                    Debug.Log($"🎭 SetupExpressionSystem: VRM 1.0 expression system ready, Available: {controller.AvailableExpressions.Count}");
                }
                else
                {
                    Debug.LogWarning($"🎭 SetupExpressionSystem: VRM 1.0 CurrentExpressionController is null");
                }
            }

            // VRM 0.xコントローラーをクリア
            vrm0ExpressionController = null;
        }

        /// <summary>
        /// VRM 0.x用の表情システムセットアップ
        /// </summary>
        void SetupVrm0ExpressionSystem(GameObject avatar, global::VRM.VRMBlendShapeProxy blendShapeProxy)
        {
            // 既存のコントローラーを取得または追加
            vrm0ExpressionController = avatar.GetComponent<AICam.Expression.Vrm0ExpressionController>();
            if (vrm0ExpressionController == null)
            {
                vrm0ExpressionController = avatar.AddComponent<AICam.Expression.Vrm0ExpressionController>();
            }

            vrm0ExpressionController.SetBlendShapeProxy(blendShapeProxy);

            Debug.Log($"🎭 SetupExpressionSystem: VRM 0.x expression system ready, Available: {vrm0ExpressionController.AvailableExpressions.Count}");

            // VRM 1.0セットアップをクリア
            expressionSetup = null;
        }

        /// <summary>
        /// Issue #407: 情報アラートを表示（水色）
        /// </summary>
        public void ShowInfo(string code, string message, float autoDismissSeconds = 3f)
        {
            if (alertBar == null || alertMessage == null)
            {
                Debug.LogWarning("⚠️ AlertBar elements not found");
                return;
            }

            // メッセージを設定
            alertMessage.text = $"{code}:{message}";

            // スタイルを設定（info = 水色）
            alertBar.RemoveFromClassList("warning");
            alertBar.RemoveFromClassList("error");
            alertBar.RemoveFromClassList("info");
            alertBar.AddToClassList("info");

            // フェードイン表示
            alertBar.style.display = DisplayStyle.Flex;
            alertBar.style.opacity = 0;

            // 次のフレームでopacity:1に変更してCSSトランジションを発火
            alertBar.schedule.Execute(() =>
            {
                alertBar.AddToClassList("visible");
                alertBar.style.opacity = 1;
            }).StartingIn(10);

            Debug.Log($"ℹ️ Info shown: {code}:{message}");

            // 自動非表示
            if (autoDismissSeconds > 0)
            {
                alertBar.schedule.Execute(() => HideAlert()).StartingIn((long)(autoDismissSeconds * 1000));
            }
        }

        /// <summary>
        /// Issue #120: ライティングパネルを表示
        /// </summary>
        void ShowLightingPanel()
        {
            Debug.Log($"📋 ShowLightingPanel called");
            Debug.Log($"📋 settingsPanelBackdrop is null: {settingsPanelBackdrop == null}");
            Debug.Log($"📋 lightingPanelOverlay is null: {lightingPanelOverlay == null}");
            HideAllPanels(); // 他のパネルを閉じる

            // パネル表示時にLightingPanelControllerを遅延初期化
            GetLightingPanelController();

            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.pickingMode = PickingMode.Position; // 表示時はクリック受付
                settingsPanelBackdrop.AddToClassList("visible");
                Debug.Log($"📋 settingsPanelBackdrop classes after: {string.Join(", ", settingsPanelBackdrop.GetClasses())}");
                Debug.Log($"📋 settingsPanelBackdrop display: {settingsPanelBackdrop.resolvedStyle.display}");
            }
            else
            {
                Debug.LogWarning("⚠️ settingsPanelBackdrop is NULL - cannot show backdrop");
            }
            if (lightingPanelOverlay != null)
            {
                lightingPanelOverlay.AddToClassList("visible");
                Debug.Log($"📋 lightingPanelOverlay classes after: {string.Join(", ", lightingPanelOverlay.GetClasses())}");
                Debug.Log($"📋 lightingPanelOverlay display: {lightingPanelOverlay.resolvedStyle.display}");
                Debug.Log("💡 Lighting panel shown");
            }
            else
            {
                Debug.LogWarning("⚠️ lightingPanelOverlay is NULL - cannot show panel");
            }
        }

        /// <summary>
        /// Issue #120: シャドウパネルを表示
        /// </summary>
        void ShowShadowPanel()
        {
            Debug.Log($"📋 ShowShadowPanel called");
            Debug.Log($"📋 settingsPanelBackdrop is null: {settingsPanelBackdrop == null}");
            Debug.Log($"📋 shadowPanelOverlay is null: {shadowPanelOverlay == null}");
            HideAllPanels(); // 他のパネルを閉じる

            // パネル表示時にLightingPanelControllerを遅延初期化
            GetLightingPanelController();

            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.pickingMode = PickingMode.Position; // 表示時はクリック受付
                settingsPanelBackdrop.AddToClassList("visible");
                Debug.Log($"📋 settingsPanelBackdrop classes after: {string.Join(", ", settingsPanelBackdrop.GetClasses())}");
                Debug.Log($"📋 settingsPanelBackdrop display: {settingsPanelBackdrop.resolvedStyle.display}");
            }
            else
            {
                Debug.LogWarning("⚠️ settingsPanelBackdrop is NULL - cannot show backdrop");
            }
            if (shadowPanelOverlay != null)
            {
                shadowPanelOverlay.AddToClassList("visible");
                Debug.Log($"📋 shadowPanelOverlay classes after: {string.Join(", ", shadowPanelOverlay.GetClasses())}");
                Debug.Log($"📋 shadowPanelOverlay display: {shadowPanelOverlay.resolvedStyle.display}");
                Debug.Log("🌑 Shadow panel shown");
            }
            else
            {
                Debug.LogWarning("⚠️ shadowPanelOverlay is NULL - cannot show panel");
            }
        }

        /// <summary>
        /// Issue #120: すべてのパネルを非表示
        /// </summary>
        void HideAllPanels()
        {
            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.pickingMode = PickingMode.Ignore; // 非表示時はクリック無視
                settingsPanelBackdrop.RemoveFromClassList("visible");
            }
            if (lightingPanelOverlay != null)
            {
                lightingPanelOverlay.RemoveFromClassList("visible");
            }
            if (shadowPanelOverlay != null)
            {
                shadowPanelOverlay.RemoveFromClassList("visible");
            }
            Debug.Log("📋 All panels hidden");
        }

        /// <summary>
        /// トップボタンの透明度を更新
        /// ONのとき不透明、OFFのとき半透明
        /// </summary>
        void UpdateTopButtonOpacity(Button button, bool isEnabled)
        {
            if (button == null) return;
            button.style.opacity = isEnabled ? 1.0f : 0.4f;
        }

        /// <summary>
        /// Issue #74: Light Estimation設定を適用
        /// </summary>
        void ApplyLightEstimationSetting()
        {
            // キャッシュがない場合のみ検索（lazy initialization）
            if (cachedLightEstimationController == null)
            {
                cachedLightEstimationController = FindFirstObjectByType<ARLightEstimationController>();
            }

            if (cachedLightEstimationController != null)
            {
                cachedLightEstimationController.enabled = isLightEstimationEnabled;
                Debug.Log($"💡 ARLightEstimationController.enabled = {isLightEstimationEnabled}");
            }
            else
            {
                Debug.LogWarning("⚠️ ARLightEstimationController not found in scene");
            }
        }

        /// <summary>
        /// Issue #75: Shadow設定を適用
        /// </summary>
        void ApplyShadowSetting()
        {
            // キャッシュがない場合のみ検索（lazy initialization）
            if (cachedMainLight == null)
            {
                cachedMainLight = FindMainDirectionalLight();
            }

            if (cachedMainLight != null)
            {
                cachedMainLight.shadows = isShadowEnabled ? LightShadows.Soft : LightShadows.None;
                Debug.Log($"🌑 Main light shadows = {cachedMainLight.shadows}");
            }
            else
            {
                Debug.LogWarning("⚠️ Main Directional Light not found in scene");
            }

            // AR平面の落ち影レシーバーも制御
            if (cachedPlaneShadowReceiver == null)
            {
                cachedPlaneShadowReceiver = FindFirstObjectByType<ARPlaneShadowReceiver>();
            }

            if (cachedPlaneShadowReceiver != null)
            {
                cachedPlaneShadowReceiver.SetShadowEnabled(isShadowEnabled);
                Debug.Log($"🌑 AR Plane shadow receiver = {isShadowEnabled}");
            }
        }

        /// <summary>
        /// メインのDirectional Lightを検索
        /// </summary>
        Light FindMainDirectionalLight()
        {
            var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (var light in lights)
            {
                if (light.type == LightType.Directional)
                {
                    return light;
                }
            }
            return null;
        }

        /// <summary>
        /// UI要素のジオメトリ変更時のコールバック
        /// レイアウト確定後にアスペクト比マスクを更新
        /// </summary>
        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            Debug.Log($"📐 GeometryChangedEvent: {root.resolvedStyle.width}x{root.resolvedStyle.height}");
            UpdateAspectMask();
        }

        /// <summary>
        /// アスペクト比マスクを更新
        /// </summary>
        void UpdateAspectMask()
        {
            if (topMask == null || bottomMask == null)
            {
                Debug.LogWarning("⚠️ topMask or bottomMask is null");
                return;
            }

            Debug.Log($"📐 UpdateAspectMask called: state={aspectRatioState}");

            float targetAspect = aspectRatios[aspectRatioState];

            // Full (0) の場合はマスクを非表示
            if (targetAspect == 0f)
            {
                topMask.style.display = DisplayStyle.None;
                bottomMask.style.display = DisplayStyle.None;
                if (leftMask != null) leftMask.style.display = DisplayStyle.None;
                if (rightMask != null) rightMask.style.display = DisplayStyle.None;
                Debug.Log("📐 Aspect masks hidden (Full mode)");
                return;
            }

            // UI要素の実際のレンダリングサイズを取得
            float screenWidth = root.resolvedStyle.width;
            float screenHeight = root.resolvedStyle.height;

            // resolvedStyleが未確定の場合は処理をスキップ
            if (float.IsNaN(screenWidth) || float.IsNaN(screenHeight) || screenWidth <= 0 || screenHeight <= 0)
            {
                Debug.LogWarning($"⚠️ resolvedStyle not ready: {screenWidth}x{screenHeight}");
                return;
            }

            // モバイルは縦長画面なので、targetAspectを「高さ/幅」として扱う
            float targetHeightWidthRatio = targetAspect;  // 高さ/幅
            float screenHeightWidthRatio = screenHeight / screenWidth;

            Debug.Log($"📐 UI Size (resolvedStyle): {screenWidth}x{screenHeight}, screen H/W ratio: {screenHeightWidthRatio:F3}, target H/W ratio: {targetHeightWidthRatio:F3}");

            // カメラの最大画角の中心から指定アスペクト比でクロップ
            float maskWidth = 0f;
            float maskHeight = 0f;
            bool isVerticalCrop = screenHeightWidthRatio > targetHeightWidthRatio;

            if (screenHeightWidthRatio > targetHeightWidthRatio)
            {
                // 画面が目標より縦長 → 上下にマスク
                float targetHeight = screenWidth * targetHeightWidthRatio;
                maskHeight = (screenHeight - targetHeight) / 2f;
                Debug.Log($"📐 Vertical crop: target height={targetHeight}px, mask height={maskHeight}px");
            }
            else
            {
                // 画面が目標より横長 → 左右にマスク
                float targetWidth = screenHeight / targetHeightWidthRatio;
                maskWidth = (screenWidth - targetWidth) / 2f;
                Debug.Log($"📐 Horizontal crop: target width={targetWidth}px, mask width={maskWidth}px");
            }

            if (isVerticalCrop)
            {
                // 上下にマスク
                if (leftMask != null) leftMask.style.display = DisplayStyle.None;
                if (rightMask != null) rightMask.style.display = DisplayStyle.None;

                // 上マスク（画面上端から配置）
                topMask.style.display = DisplayStyle.Flex;
                topMask.style.position = Position.Absolute;
                topMask.style.left = 0;
                topMask.style.right = 0;
                topMask.style.top = 0;
                topMask.style.width = screenWidth;
                topMask.style.height = maskHeight;
                topMask.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                topMask.style.opacity = 1f;
                topMask.pickingMode = PickingMode.Ignore;
                Debug.Log($"📐 Top mask SET: {screenWidth}x{maskHeight}px");

                // 下マスク（画面下端から配置）
                bottomMask.style.display = DisplayStyle.Flex;
                bottomMask.style.position = Position.Absolute;
                bottomMask.style.left = 0;
                bottomMask.style.right = 0;
                bottomMask.style.bottom = 0;
                bottomMask.style.width = screenWidth;
                bottomMask.style.height = maskHeight;
                bottomMask.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                bottomMask.style.opacity = 1f;
                bottomMask.pickingMode = PickingMode.Ignore;
                Debug.Log($"📐 Bottom mask SET: {screenWidth}x{maskHeight}px");
            }
            else
            {
                // 左右にマスク
                topMask.style.display = DisplayStyle.None;
                bottomMask.style.display = DisplayStyle.None;

                if (leftMask != null)
                {
                    leftMask.style.display = DisplayStyle.Flex;
                    leftMask.style.position = Position.Absolute;
                    leftMask.style.left = 0;
                    leftMask.style.top = 0;
                    leftMask.style.bottom = 0;
                    leftMask.style.width = maskWidth;
                    leftMask.style.height = screenHeight;
                    leftMask.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                    leftMask.style.opacity = 1f;
                    leftMask.pickingMode = PickingMode.Ignore;
                    Debug.Log($"📐 Left mask SET: {maskWidth}x{screenHeight}px");
                }

                if (rightMask != null)
                {
                    rightMask.style.display = DisplayStyle.Flex;
                    rightMask.style.position = Position.Absolute;
                    rightMask.style.right = 0;
                    rightMask.style.top = 0;
                    rightMask.style.bottom = 0;
                    rightMask.style.width = maskWidth;
                    rightMask.style.height = screenHeight;
                    rightMask.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                    rightMask.style.opacity = 1f;
                    rightMask.pickingMode = PickingMode.Ignore;
                    Debug.Log($"📐 Right mask SET: {maskWidth}x{screenHeight}px");
                }
            }
        }

        #region AlertBar Methods

        /// <summary>
        /// 警告アラートを表示（フェードイン）
        /// </summary>
        /// <param name="code">警告コード（例: W001）</param>
        /// <param name="message">警告メッセージ</param>
        /// <param name="autoDismissSeconds">自動非表示までの秒数（0の場合は自動非表示しない）</param>
        public void ShowWarning(string code, string message, float autoDismissSeconds = 5f)
        {
            ShowAlertInternal(code, message, false, autoDismissSeconds);
        }

        /// <summary>
        /// エラーアラートを表示（フェードイン）
        /// </summary>
        /// <param name="code">エラーコード（例: E001）</param>
        /// <param name="message">エラーメッセージ</param>
        /// <param name="autoDismissSeconds">自動非表示までの秒数（0の場合は自動非表示しない）</param>
        public void ShowError(string code, string message, float autoDismissSeconds = 0f)
        {
            ShowAlertInternal(code, message, true, autoDismissSeconds);
        }

        private void ShowAlertInternal(string code, string message, bool isError, float autoDismissSeconds)
        {
            if (alertBar == null || alertMessage == null)
            {
                Debug.LogWarning("⚠️ AlertBar elements not found");
                return;
            }

            // メッセージを設定
            alertMessage.text = $"[{code}] {message}";

            // スタイルを設定（warning/error）
            alertBar.RemoveFromClassList("warning");
            alertBar.RemoveFromClassList("error");
            alertBar.AddToClassList(isError ? "error" : "warning");

            // フェードイン表示
            alertBar.style.display = DisplayStyle.Flex;
            alertBar.style.opacity = 0;

            // 次のフレームでopacity:1に変更してCSSトランジションを発火
            alertBar.schedule.Execute(() =>
            {
                alertBar.AddToClassList("visible");
                alertBar.style.opacity = 1;
            }).StartingIn(10);

            Debug.Log($"⚠️ Alert shown: [{code}] {message} (isError: {isError})");

            // Haptic feedback
            TapticEngine.Impact(isError ? TapticEngine.ImpactStyle.Heavy : TapticEngine.ImpactStyle.Medium);

            // 自動非表示
            if (autoDismissSeconds > 0)
            {
                alertBar.schedule.Execute(() => HideAlert()).StartingIn((long)(autoDismissSeconds * 1000));
            }
        }

        /// <summary>
        /// アラートを非表示（フェードアウト）
        /// </summary>
        public void HideAlert()
        {
            if (alertBar == null) return;

            alertBar.RemoveFromClassList("visible");
            alertBar.style.opacity = 0;

            // フェードアウト完了後に非表示
            alertBar.schedule.Execute(() =>
            {
                alertBar.style.display = DisplayStyle.None;
            }).StartingIn(300);

            Debug.Log("✅ Alert hidden");
        }

        #endregion

        #region Issue #73: Circular Progress Methods

        /// <summary>
        /// スロットボタンにプログレス要素を作成
        /// </summary>
        private CircularProgressElement CreateProgressForSlot(Button slotButton)
        {
            if (slotButton == null || root == null) return null;

            // 既存のプログレス要素があれば返す
            if (slotProgressMap.TryGetValue(slotButton, out var existingProgress))
            {
                return existingProgress;
            }

            // 新しいプログレス要素を作成
            var progress = new CircularProgressElement();
            progress.name = $"progress_{slotButton.name}";
            progress.RingWidth = 3f;
            progress.ProgressColor = new Color(0.3f, 0.7f, 1f, 1f); // Light blue
            progress.ShowBackground = false; // 背景なし、プログレス弧のみ

            // スタイル設定
            progress.style.position = Position.Absolute;
            progress.style.display = DisplayStyle.None; // 初期状態は非表示

            // rootに追加（最前面に配置）
            root.Add(progress);

            // マップに登録
            slotProgressMap[slotButton] = progress;

            Debug.Log($"[#73] Created progress element for {slotButton.name}");

            return progress;
        }

        /// <summary>
        /// スロットのプログレス要素を位置更新
        /// </summary>
        private void UpdateProgressPosition(Button slotButton)
        {
            if (!slotProgressMap.TryGetValue(slotButton, out var progress)) return;

            var bounds = slotButton.worldBound;
            float padding = 4f;
            float size = Mathf.Max(bounds.width, bounds.height) + padding * 2;

            progress.style.width = size;
            progress.style.height = size;
            progress.style.left = bounds.x - padding;
            progress.style.top = bounds.y - padding;
        }

        /// <summary>
        /// スロットのロード開始（プログレス表示開始）
        /// </summary>
        private void StartSlotLoading(Button slotButton)
        {
            var progress = CreateProgressForSlot(slotButton);
            if (progress == null) return;

            // 位置を更新
            UpdateProgressPosition(slotButton);

            // 表示開始
            progress.Progress = 0.01f; // 0より大きい値で開始
            progress.style.display = DisplayStyle.Flex;

            Debug.Log($"[#73] Loading started for {slotButton.name}");
        }

        /// <summary>
        /// スロットのロード進捗を更新
        /// </summary>
        private void UpdateSlotProgress(Button slotButton, float progress01)
        {
            if (!slotProgressMap.TryGetValue(slotButton, out var progress)) return;

            progress.Progress = progress01;

            Debug.Log($"[#73] Progress updated for {slotButton.name}: {progress01 * 100:F0}%");
        }

        /// <summary>
        /// スロットのロード完了（プログレス非表示）
        /// </summary>
        private void CompleteSlotLoading(Button slotButton)
        {
            if (!slotProgressMap.TryGetValue(slotButton, out var progress)) return;

            progress.Progress = 1f;

            // 少し遅延してから非表示（完了を視覚的に確認できるように）
            progress.schedule.Execute(() =>
            {
                progress.style.display = DisplayStyle.None;
                progress.Progress = 0f;
            }).StartingIn(300);

            Debug.Log($"[#73] Loading completed for {slotButton.name}");
        }

        /// <summary>
        /// スロットのロードキャンセル（プログレス非表示）
        /// </summary>
        private void CancelSlotLoading(Button slotButton)
        {
            if (!slotProgressMap.TryGetValue(slotButton, out var progress)) return;

            progress.style.display = DisplayStyle.None;
            progress.Progress = 0f;

            Debug.Log($"[#73] Loading cancelled for {slotButton.name}");
        }

        #endregion

        #region IconPreviewPanel Methods

        /// <summary>
        /// アイコンプレビューパネルを表示（フェードイン）
        /// </summary>
        /// <param name="texture">プレビューするテクスチャ</param>
        /// <param name="onConfirm">確定ボタン押下時のコールバック</param>
        /// <param name="onRetake">撮り直すボタン押下時のコールバック（nullの場合はボタン非表示）</param>
        public void ShowIconPreview(Texture2D texture, System.Action onConfirm, System.Action onRetake = null)
        {
            if (iconPreviewPanel == null || iconPreviewImage == null)
            {
                Debug.LogWarning("⚠️ IconPreviewPanel elements not found");
                return;
            }

            // コールバックを保存
            onIconPreviewConfirm = onConfirm;
            onIconPreviewRetake = onRetake;

            // 画像を設定
            iconPreviewImage.style.backgroundImage = new StyleBackground(texture);

            // 撮り直しボタンの表示/非表示
            if (iconPreviewRetake != null)
            {
                iconPreviewRetake.style.display = onRetake != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // フェードイン表示
            iconPreviewPanel.style.display = DisplayStyle.Flex;
            iconPreviewPanel.style.opacity = 0;

            // 次のフレームでopacity:1に変更してCSSトランジションを発火
            iconPreviewPanel.schedule.Execute(() =>
            {
                iconPreviewPanel.AddToClassList("visible");
                iconPreviewPanel.style.opacity = 1;
            }).StartingIn(10);

            Debug.Log($"🖼 IconPreview shown: {texture.width}x{texture.height}");
        }

        /// <summary>
        /// アイコンプレビューパネルを非表示（フェードアウト）
        /// </summary>
        public void HideIconPreview()
        {
            if (iconPreviewPanel == null) return;

            iconPreviewPanel.RemoveFromClassList("visible");
            iconPreviewPanel.style.opacity = 0;

            // フェードアウト完了後に非表示
            iconPreviewPanel.schedule.Execute(() =>
            {
                iconPreviewPanel.style.display = DisplayStyle.None;
                iconPreviewImage.style.backgroundImage = null;
            }).StartingIn(300);

            onIconPreviewConfirm = null;
            onIconPreviewRetake = null;

            Debug.Log("✅ IconPreview hidden");
        }

        private void OnIconPreviewConfirmClicked()
        {
            Debug.Log("✅ IconPreview confirm clicked");
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);

            var callback = onIconPreviewConfirm;
            HideIconPreview();
            callback?.Invoke();
        }

        private void OnIconPreviewRetakeClicked()
        {
            Debug.Log("🔄 IconPreview retake clicked");
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);

            var callback = onIconPreviewRetake;
            HideIconPreview();
            callback?.Invoke();
        }

        /// <summary>
        /// アイコンプレビューが表示中かどうか
        /// </summary>
        public bool IsIconPreviewShowing => iconPreviewPanel != null &&
            iconPreviewPanel.resolvedStyle.display == DisplayStyle.Flex;

        #endregion

        #region Version Info

        /// <summary>
        /// ビルド番号を取得
        /// 実機: PlayerSettingsのビルド番号（iOS: CFBundleVersion, Android: bundleVersionCode）
        /// Editor: 日付ベースの識別子
        /// </summary>
        private string GetBuildNumber()
        {
#if UNITY_EDITOR
            // エディタでは日付ベースのビルド番号
            return System.DateTime.Now.ToString("yyMMdd");
#elif UNITY_ANDROID
            // AndroidではpackageInfoからversionCodeを取得
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var packageInfo = packageManager.Call<AndroidJavaObject>("getPackageInfo",
                    activity.Call<string>("getPackageName"), 0))
                {
                    var versionCode = packageInfo.Get<int>("versionCode");
                    return versionCode.ToString();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VersionInfo] Failed to get Android versionCode: {e.Message}");
                return "1";
            }
#elif UNITY_IOS
            // iOSではビルド時に設定されたバージョンを使用
            // PlayerSettings.iOS.buildNumberはビルド時にInfo.plistに埋め込まれる
            // ランタイムでは直接アクセスできないため、ビルドGUIDの一部を使用
            string buildGuid = Application.buildGUID;
            if (!string.IsNullOrEmpty(buildGuid) && buildGuid.Length >= 8)
            {
                return buildGuid.Substring(0, 8);
            }
            return "1";
#else
            return "1";
#endif
        }

        #endregion
    }
}