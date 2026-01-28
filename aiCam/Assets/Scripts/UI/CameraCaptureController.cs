using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using NativeFilePickerNamespace;
using Cysharp.Threading.Tasks;
using System.IO;
using System;
using System.Collections.Generic;
using AICam.AR;
using AICam.AvatarCache;
using AICam.Core;
using DSGarage.PoseSlot;
#if BLENDSHAPE_CONTROLLER
using DSGarage.BlendShape;
#endif

namespace AICam.UI
{
    /// <summary>
    /// UIToolkit版のカメラ撮影コントローラー
    /// タップで写真撮影、長押しで動画撮影を行う
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(UIToolkitInputBlocker))]
    public class CameraCaptureController : MonoBehaviour, IUIBlockingProvider, ILightingSettingsProvider
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

        // IAvatarPlacer（PlaceAvatarOnPlaneOnlyの抽象化）
        private IAvatarPlacer cachedAvatarPlacer;

        private VisualElement root;
        private VisualElement captureButton;
        private VisualElement innerCircle;
        private VisualElement progressRing;
        private VisualElement progressArc;
        private VisualElement flashOverlay;
        private VisualElement galleryThumbnail;

        // Phase 01: サービス
        private AlertService alertService;
        private SlotProgressService slotProgressService;

        // Phase 02: コントローラー
        private IconPreviewController iconPreviewController;
        private MediaViewerController mediaViewerController;
        private AspectRatioController aspectRatioController;

        // パネル要素
        private VisualElement topPanel;
        private VisualElement bottomPanel;
        private VisualElement bottomButtonContainer;
        private Button bottomButtonAdd;
        private int bottomButtonCount = 1; // UXML has only bottomButton1

        // サイドパネル要素
        private VisualElement sidePanel;
        private Button sideButton1;
        private Button sideButton2;
        private Button sideButton3;
        private Button sideButtonBugReport; // Issue #413: バグレポート

        // Issue #451: 撮影設定バー（topButton1-4用、貫通防止）
        private VisualElement captureSettingBar;

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

        // Issue #452: トーチ（背面ライト）状態
        private bool isTorchEnabled = false;
        private ARCameraManager cachedARCameraManager;

        // Issue #120: ライティング/シャドウパネル
        private LightingPanelController lightingPanelController;
        private VisualElement settingsPanelBackdrop;
        private VisualElement lightingPanelOverlay;
        private VisualElement shadowPanelOverlay;

        // Issue #450: Lighting Panel タブ切り替え用
        private VisualElement tabMood;
        private VisualElement tabDirection;
        private VisualElement lightingPanelMood;
        private VisualElement lightingPanelDirection;

        // Issue #74/#75 修正: 長押し関連変数は不要になったため削除

        // 削除ポップアップ関連
        private VisualElement deletePopup;
        private Button deleteButton;
        private Button cancelButton;
        private Button currentLongPressButton;
        private float longPressTime = 0f;
        private const float longPressThresholdForDelete = 0.5f;
        private bool isLongPressing = false;
        private bool suppressNextClick = false; // 長押し後のクリックを抑制するフラグ

        // Issue #459: キャッシュクリアポップアップ関連
        private VisualElement clearCachePopup;
        private Button clearCacheButton;
        private Button clearCacheCancelButton;

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

        // スロットロード中フラグ（重複ロード防止）
        private bool isSlotLoading = false;
        private Button currentLoadingSlot = null;

        // Issue #458: スロットダブルタップ検出用
        private Button lastClickedSlotButton;
        private float lastSlotClickTime;

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

            // Phase 01: サービス初期化
            alertService = new AlertService(root);
            slotProgressService = new SlotProgressService(root);
            new VersionInfoService(root);

            // Phase 02: コントローラー初期化
            iconPreviewController = new IconPreviewController(root);
            mediaViewerController = new MediaViewerController(root);

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

            // Issue #451: 撮影設定バー（貫通防止用）
            captureSettingBar = root.Q<VisualElement>("captureSettingBar");

            // Issue #74/#75: トップパネルボタンの取得
            topButton1 = root.Q<Button>("topButton1");
            topButton2 = root.Q<Button>("topButton2");
            topButton3 = root.Q<Button>("topButton3"); // Issue #33/#405: Expression
            topButton4 = root.Q<Button>("topButton4"); // Issue #407: Pose
            topButton5 = root.Q<Button>("topButton5"); // Issue #345: Plane Visibility

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
                galleryThumbnail.RegisterCallback<ClickEvent>(evt =>
                    mediaViewerController?.OpenViewer(lastCapturedPhoto, lastMediaIsVideo));
                if (enableDebugLogging) Debug.Log("✅ Gallery thumbnail events registered");
            }

            if (bottomButtonAdd != null)
            {
                bottomButtonAdd.RegisterCallback<ClickEvent>(evt =>
                {
                    // 長押し後のクリックは抑制
                    if (suppressNextClick)
                    {
                        Debug.Log($"Click suppressed after long press on {bottomButtonAdd.name}");
                        suppressNextClick = false;
                        return;
                    }
                    AddBottomPanelButton();
                });
                // +ボタンにも長押しイベントを登録（キャッシュクリア用）
                RegisterLongPressForButton(bottomButtonAdd);
                if (enableDebugLogging) Debug.Log("✅ Add button events registered (including long press)");
            }

            // サイドパネルボタンのイベント登録
            if (sideButton1 != null)
            {
                sideButton1.RegisterCallback<ClickEvent>(evt => OnSideButton1Clicked());
                if (enableDebugLogging) Debug.Log("✅ Side button 1 events registered");
            }

            // sideButton2 のイベントは AspectRatioController が管理

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

            // Issue #450: Lighting Panel タブ要素を取得
            tabMood = root.Q<VisualElement>("tabMood");
            tabDirection = root.Q<VisualElement>("tabDirection");
            lightingPanelMood = root.Q<VisualElement>("lightingPanelMood");
            lightingPanelDirection = root.Q<VisualElement>("lightingPanelDirection");

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

            // Issue #450: Lighting Panel タブ切り替えイベント登録
            tabMood?.RegisterCallback<ClickEvent>(_ => ShowLightingMood());
            tabDirection?.RegisterCallback<ClickEvent>(_ => ShowLightingDirection());
            if (enableDebugLogging)
            {
                Debug.Log($"🔄 TabMood: {(tabMood != null ? "✅" : "❌")}");
                Debug.Log($"🔄 TabDirection: {(tabDirection != null ? "✅" : "❌")}");
                Debug.Log($"🔄 LightingPanelMood: {(lightingPanelMood != null ? "✅" : "❌")}");
                Debug.Log($"🔄 LightingPanelDirection: {(lightingPanelDirection != null ? "✅" : "❌")}");
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

            // Issue #459: キャッシュクリアポップアップを作成
            CreateClearCachePopup(root);
            if (enableDebugLogging) Debug.Log($"🔧 Clear cache popup created: {(clearCachePopup != null ? "✅" : "❌")}");

            // 既存のボタンに長押しイベントを登録
            if (enableDebugLogging) Debug.Log("🔧 Registering long press for existing buttons...");
            RegisterLongPressForExistingButtons();

            // Issue #416: 永続化されたスロットデータを読み込み（非同期でキャッシュ準備を待つ）
            if (enableDebugLogging) Debug.Log("🔧 Loading persisted slot data...");
            LoadPersistedSlotDataAsync().Forget();

            // Phase 02: AspectRatioController 初期化（GeometryChanged登録・初期アスペクト比設定含む）
            aspectRatioController = new AspectRatioController(root, sideButton2, photoController);

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

            // Issue #416: AvatarLoadHandlerのイベント購読（リストア時のAOC適用用）
            if (AICam.FBXLoader.AvatarLoadHandler.HasInstance)
            {
                AICam.FBXLoader.AvatarLoadHandler.Instance.OnLoadComplete += OnAvatarLoadHandlerComplete;
                if (enableDebugLogging) Debug.Log("🎭 Subscribed to AvatarLoadHandler events");
            }
            else
            {
                // 遅延購読（AvatarLoadHandlerが後から初期化される場合）
                SubscribeToAvatarLoadHandlerDelayed().Forget();
            }
        }

        /// <summary>
        /// Issue #416: AvatarLoadHandlerへの遅延購読
        /// </summary>
        private async Cysharp.Threading.Tasks.UniTaskVoid SubscribeToAvatarLoadHandlerDelayed()
        {
            int maxWait = 20; // 最大2秒待機
            while (!AICam.FBXLoader.AvatarLoadHandler.HasInstance && maxWait > 0)
            {
                await Cysharp.Threading.Tasks.UniTask.Delay(100);
                maxWait--;
            }

            if (AICam.FBXLoader.AvatarLoadHandler.HasInstance)
            {
                AICam.FBXLoader.AvatarLoadHandler.Instance.OnLoadComplete += OnAvatarLoadHandlerComplete;
                Debug.Log("🎭 Subscribed to AvatarLoadHandler events (delayed)");
            }
        }

        void OnDisable()
        {
            // ARPhotoControllerのイベント解除
            if (photoController != null)
            {
                photoController.OnPhotoCaptured -= OnPhotoCapturedHandler;
            }

            // Phase 02: AspectRatioController のイベント解除
            aspectRatioController?.Dispose();

            // Issue #407: AvatarSlotManagerのイベント解除
            if (AICam.FBXLoader.AvatarSlotManager.Instance != null)
            {
                AICam.FBXLoader.AvatarSlotManager.Instance.OnSlotLoadComplete -= OnAvatarSlotLoadComplete;
                AICam.FBXLoader.AvatarSlotManager.Instance.OnSlotCleared -= OnAvatarSlotCleared;
            }

            // Issue #416: AvatarLoadHandlerのイベント解除
            if (AICam.FBXLoader.AvatarLoadHandler.HasInstance)
            {
                AICam.FBXLoader.AvatarLoadHandler.Instance.OnLoadComplete -= OnAvatarLoadHandlerComplete;
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

                    // +ボタンの場合はキャッシュクリアポップアップを表示
                    if (currentLongPressButton == bottomButtonAdd)
                    {
                        ShowClearCachePopup();
                    }
                    else
                    {
                        ShowDeletePopup(currentLongPressButton);
                    }

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

        /// <summary>
        /// ARPhotoControllerを設定（外部から呼び出し可能）
        /// </summary>
        public void SetPhotoController(ARPhotoController controller)
        {
            photoController = controller;
            aspectRatioController?.SetPhotoController(controller);
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
        void AddBottomPanelButton(bool persistSlotCount = true)
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

            // Issue #462: スロットボタン数をキャッシュに保存
            if (persistSlotCount)
            {
                SaveSlotCountToCache();
            }

            // Light impact for button addition
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        /// <summary>
        /// Issue #462: 特定のスロットインデックス用のボタンを作成
        /// 起動時に設定済みスロットのみ復元する際に使用
        /// </summary>
        Button AddBottomPanelButtonForSlot(int slotIndex)
        {
            if (bottomButtonContainer == null)
            {
                Debug.LogWarning("⚠️ bottomButtonContainer is null");
                return null;
            }

            int buttonNumber = slotIndex + 1; // slot 0 → bottomButton1

            var newButton = new Button();
            newButton.name = $"bottomButton{buttonNumber}";
            newButton.AddToClassList("bottom-panel-button");

            // +ボタンの直前に挿入
            int addButtonIndex = bottomButtonContainer.IndexOf(bottomButtonAdd);
            bottomButtonContainer.Insert(addButtonIndex, newButton);

            // 長押しイベントを登録（ClickEventより先に登録）
            RegisterLongPressForButton(newButton);

            // ボタンのクリックイベントを登録
            newButton.RegisterCallback<ClickEvent>(evt =>
            {
                if (suppressNextClick)
                {
                    Debug.Log($"🚫 Click suppressed after long press on {newButton.name}");
                    suppressNextClick = false;
                    return;
                }

                Debug.Log($"🔘 Bottom button #{newButton.name} clicked");
                TapticEngine.Selection();
                OnSlotClicked(newButton);
            });

            // bottomButtonCountを最大値に合わせる（次の+ボタン用）
            if (buttonNumber > bottomButtonCount)
                bottomButtonCount = buttonNumber;

            Debug.Log($"➕ Added button for slot {slotIndex}: {newButton.name}");
            return newButton;
        }

        /// <summary>
        /// Issue #462: 現在のスロットボタン数をキャッシュに保存
        /// </summary>
        void SaveSlotCountToCache()
        {
            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            if (slotManager?.Cache != null)
            {
                slotManager.Cache.lastCreatedSlotCount = bottomButtonCount;
                slotManager.Cache.SaveToFile();
                Debug.Log($"[📦 PERSIST] Saved lastCreatedSlotCount={bottomButtonCount}");
            }
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
        /// Issue #459: キャッシュクリアポップアップを作成
        /// </summary>
        void CreateClearCachePopup(VisualElement root)
        {
            clearCachePopup = new VisualElement();
            clearCachePopup.name = "clearCachePopup";
            clearCachePopup.AddToClassList("delete-popup");

            // 絶対配置を有効化
            clearCachePopup.style.position = Position.Absolute;
            clearCachePopup.pickingMode = PickingMode.Position;

            // キャッシュクリアボタン
            clearCacheButton = new Button();
            clearCacheButton.text = "キャッシュクリア";
            clearCacheButton.AddToClassList("delete-popup-button");
            clearCacheButton.AddToClassList("delete"); // 赤い色を使用
            clearCacheButton.RegisterCallback<ClickEvent>(evt => OnClearCacheButtonClicked());

            // キャンセルボタン
            clearCacheCancelButton = new Button();
            clearCacheCancelButton.text = "キャンセル";
            clearCacheCancelButton.AddToClassList("delete-popup-button");
            clearCacheCancelButton.RegisterCallback<ClickEvent>(evt => HideClearCachePopup());

            clearCachePopup.Add(clearCacheButton);
            clearCachePopup.Add(clearCacheCancelButton);
            root.Add(clearCachePopup);

            Debug.Log("✅ Clear cache popup created");
        }

        /// <summary>
        /// Issue #416: 永続化されたスロットデータを読み込み（非同期版）
        /// AvatarSlotManagerの初期化完了を待ってからアイコンを読み込み
        /// </summary>
        async Cysharp.Threading.Tasks.UniTaskVoid LoadPersistedSlotDataAsync()
        {
            Debug.Log("[📦 PERSIST] LoadPersistedSlotDataAsync called - waiting for AvatarSlotManager...");

            // AvatarSlotManagerの初期化完了を待機（最大3秒）
            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            int maxWait = 30;
            while ((slotManager == null || !slotManager.IsInitialized) && maxWait > 0)
            {
                await Cysharp.Threading.Tasks.UniTask.Delay(100);
                slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                maxWait--;
            }

            if (slotManager == null)
            {
                Debug.LogWarning("[📦 PERSIST] AvatarSlotManager.Instance is null after waiting");
                return;
            }
            if (!slotManager.IsInitialized)
            {
                Debug.LogWarning("[📦 PERSIST] AvatarSlotManager not initialized after waiting");
                return;
            }
            if (slotManager.Cache == null)
            {
                Debug.LogWarning("[📦 PERSIST] AvatarSlotManager.Cache is null");
                return;
            }

            Debug.Log("[📦 PERSIST] AvatarSlotManager ready, loading persisted data...");

            var cache = slotManager.Cache;
            Debug.Log($"[📦 PERSIST] Cache available: {cache.GetConfiguredSlotCount()} configured slots, lastActive={cache.lastActiveSlotIndex}");

            if (bottomButtonContainer == null)
            {
                Debug.LogWarning("[📦 PERSIST] bottomButtonContainer is null");
                return;
            }

            // Issue #462: 既存ボタン数を正確にカウントし、bottomButtonCountを補正
            var existingButtons = bottomButtonContainer.Query<Button>().ToList();
            int existingSlotCount = 0;
            foreach (var btn in existingButtons)
            {
                if (btn != bottomButtonAdd) existingSlotCount++;
            }
            bottomButtonCount = existingSlotCount;
            Debug.Log($"[📦 PERSIST] Existing slot buttons: {existingSlotCount}, corrected bottomButtonCount={bottomButtonCount}");

            // 孤立スロットデータのクリーンアップ
            // lastCreatedSlotCount 以上のインデックスに残っている設定済みデータは
            // ボタン削除時にクリアされなかった孤立データなので起動時に削除する
            if (cache.lastCreatedSlotCount >= 0)
            {
                bool cleaned = false;
                for (int i = cache.lastCreatedSlotCount; i < cache.slots.Count; i++)
                {
                    var orphan = cache.GetSlot(i);
                    if (orphan != null && orphan.IsConfigured)
                    {
                        Debug.Log($"[📦 PERSIST] Cleaning orphaned slot {i}: {orphan.avatarName}");
                        cache.ClearSlot(i);
                        cleaned = true;
                    }
                }
                if (cleaned)
                {
                    cache.SaveToFile();
                    Debug.Log("[📦 PERSIST] Orphaned slot data cleaned up");
                }
            }

            // Issue #462: 設定済みスロットの最大インデックスを特定
            int maxConfiguredIndex = -1;
            var configuredSlotIndices = new System.Collections.Generic.List<int>();
            for (int i = 0; i < cache.slots.Count; i++)
            {
                var slot = cache.GetSlot(i);
                if (slot != null && slot.IsConfigured)
                {
                    configuredSlotIndices.Add(i);
                    maxConfiguredIndex = i;
                }
            }

            if (configuredSlotIndices.Count == 0)
            {
                Debug.Log("[📦 PERSIST] No configured slots found");
                return;
            }

            Debug.Log($"[📦 PERSIST] Configured slots: [{string.Join(", ", configuredSlotIndices)}], maxIndex={maxConfiguredIndex}");

            // Issue #462: 設定済みスロットのみボタン生成（空スロットは復元しない）
            Debug.Log($"[📦 PERSIST] Creating buttons for {configuredSlotIndices.Count} configured slots");
            foreach (int slotIdx in configuredSlotIndices)
            {
                // slot 0 → bottomButton1 は UXML に既存
                if (slotIdx == 0) continue;
                AddBottomPanelButtonForSlot(slotIdx);
            }

            // Phase 1: 全設定済みスロットのメタデータ・アイコンを復元
            var allButtons = bottomButtonContainer.Query<Button>().ToList();
            Debug.Log($"[📦 PERSIST] Total buttons after creation: {allButtons.Count}");
            int loadedCount = 0;
            Button lastActiveButton = null;
            var configuredButtons = new System.Collections.Generic.List<Button>();

            foreach (var button in allButtons)
            {
                if (button == bottomButtonAdd) continue;

                int slotIndex = GetSlotIndexFromButton(button);
                if (slotIndex < 0) continue;

                var avatarSlotData = cache.GetSlot(slotIndex);
                if (avatarSlotData == null || !avatarSlotData.IsConfigured) continue;

                Debug.Log($"[📦 PERSIST] Processing slot {slotIndex}: {avatarSlotData.avatarName}, hasIcon={avatarSlotData.HasIcon}");

                // slotDataMapを更新
                if (!slotDataMap.ContainsKey(button))
                {
                    slotDataMap[button] = new SlotData();
                }

                var slotData = slotDataMap[button];
                slotData.filePath = avatarSlotData.modelFilePath;
                slotData.fileType = avatarSlotData.fileType == AICam.AvatarCache.AvatarFileType.VRM
                    ? SlotFileType.VRM
                    : SlotFileType.FBX;

                // アイコンを読み込んでUIを更新
                if (avatarSlotData.HasIcon && System.IO.File.Exists(avatarSlotData.iconFilePath))
                {
                    try
                    {
                        byte[] iconData = System.IO.File.ReadAllBytes(avatarSlotData.iconFilePath);
                        var texture = new Texture2D(2, 2);
                        if (texture.LoadImage(iconData))
                        {
                            slotData.thumbnail = texture;
                            UpdateButtonIcon(button, texture);
                            Debug.Log($"[📦 PERSIST] ✅ Icon loaded for slot {slotIndex}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[📦 PERSIST] Failed to load icon for slot {slotIndex}: {e.Message}");
                    }
                }

                // lastActiveSlotIndexのボタンを記録
                if (slotIndex == cache.lastActiveSlotIndex)
                {
                    lastActiveButton = button;
                }

                configuredButtons.Add(button);
                loadedCount++;
            }

            Debug.Log($"[📦 PERSIST] Restored metadata for {loadedCount} slots");

            // Phase 2: Issue #462 バイナリキャッシュからアバターを自動ロード
            // lastActiveSlotを最初にロード（表示用）、その後他のスロットをロード（非表示）
            // lastActiveSlotが見つからない場合は最初の設定済みスロットをアクティブにする
            if (lastActiveButton == null && configuredButtons.Count > 0)
            {
                lastActiveButton = configuredButtons[0];
                Debug.Log($"[📦 PERSIST] lastActiveSlot not found, using first configured: {lastActiveButton.name}");
            }

            if (lastActiveButton != null)
            {
                Debug.Log($"[📦 PERSIST] Auto-loading lastActive slot: {lastActiveButton.name}");
                await AutoLoadSlotFromCacheAsync(lastActiveButton, slotManager, isActiveSlot: true);
            }

            // 他の設定済みスロットをロード
            foreach (var button in configuredButtons)
            {
                if (button == lastActiveButton) continue;
                Debug.Log($"[📦 PERSIST] Auto-loading additional slot: {button.name}");
                await AutoLoadSlotFromCacheAsync(button, slotManager, isActiveSlot: false);
            }

            Debug.Log($"[📦 PERSIST] ✅ All {loadedCount} persisted slots loaded");
        }

        /// <summary>
        /// Issue #462: 起動時にバイナリキャッシュからアバターを自動ロード
        /// TryLoadFromBinaryCacheAsyncと異なり、既存アバターを破棄しない
        /// </summary>
        async UniTask AutoLoadSlotFromCacheAsync(Button button, AICam.FBXLoader.AvatarSlotManager slotManager, bool isActiveSlot)
        {
            if (!slotDataMap.TryGetValue(button, out var slotData) || !slotData.IsConfigured)
                return;

            int slotIndex = GetSlotIndexFromButton(button);
            if (slotIndex < 0) return;

            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            if (avatarSlotData == null || string.IsNullOrEmpty(avatarSlotData.binaryCacheId))
            {
                Debug.Log($"[📦 AUTO-LOAD] No binaryCacheId for slot {slotIndex}, skipping");
                return;
            }

            string cacheId = avatarSlotData.binaryCacheId;

            try
            {
                var cacheIntegrator = new AICam.AvatarCache.AvatarCacheIntegrator(Application.persistentDataPath);

                if (!cacheIntegrator.HasBinaryCache(cacheId))
                {
                    Debug.Log($"[📦 AUTO-LOAD] Cache not found for slot {slotIndex}: {cacheId}");
                    return;
                }

                // プログレス表示
                StartSlotLoading(button);
                UpdateSlotProgress(button, 0.1f);

                var avatar = await cacheIntegrator.LoadFromBinaryCacheAsync(cacheId, progress =>
                {
                    UpdateSlotProgress(button, 0.1f + (progress / 100f) * 0.6f);
                }, slotIndex);

                if (avatar == null)
                {
                    Debug.LogWarning($"[📦 AUTO-LOAD] Failed to load avatar for slot {slotIndex}");
                    CancelSlotLoading(button);
                    return;
                }

                Debug.Log($"[📦 AUTO-LOAD] Avatar loaded for slot {slotIndex}: {avatar.name}");

                UpdateSlotProgress(button, 0.7f);

                // AOC・表情セットアップ
                ApplyDefaultAOC(avatar);
                SetupExpressionSystem(avatar, slotIndex);
                TriggerExpressionIconGeneration(avatar, slotIndex);

                if (isActiveSlot)
                {
                    // アクティブスロット: カメラ前方に配置して表示
                    PlaceAvatarAheadOfCamera(avatar);
                    ReapplyLightingSettings();

                    UpdateSlotProgress(button, 0.85f);
                    await UniTask.DelayFrame(3);
                    CompleteSlotLoading(button);

                    slotData.loadedAvatar = avatar;
                    UpdateSlotSelection(button);
                    cachedCurrentAvatar = avatar;
                    avatar.SetActive(true);
                    Debug.Log($"[📦 AUTO-LOAD] ✅ Active slot {slotIndex} loaded and visible");
                }
                else
                {
                    // 非アクティブスロット: 配置せずロードだけして非表示
                    // PlaceAvatarAheadOfCamera を呼ばない（PlaceAvatarOnPlaneOnly の
                    // 内部 avatar フィールドを上書きしないため）
                    avatar.SetActive(false);

                    UpdateSlotProgress(button, 0.85f);
                    await UniTask.DelayFrame(3);
                    CompleteSlotLoading(button);

                    slotData.loadedAvatar = avatar;
                    Debug.Log($"[📦 AUTO-LOAD] ✅ Slot {slotIndex} loaded (hidden)");
                }

                // アイコン復元（キャッシュから復元されていない場合）
                string iconPath = AICam.AvatarCache.AvatarSlotCache.GetIconPath(slotIndex);
                if (!System.IO.File.Exists(iconPath) && avatar != null)
                {
                    await UniTask.DelayFrame(3);
                    var thumbnail = await AICam.FBXLoader.AvatarIconCapture.Instance.CaptureAsTextureAsync(avatar);
                    if (thumbnail != null)
                    {
                        slotData.thumbnail = thumbnail;
                        UpdateButtonIcon(button, thumbnail);
                        SaveThumbnailToFile(button, thumbnail);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[📦 AUTO-LOAD] Error loading slot {slotIndex}: {e.Message}");
                CancelSlotLoading(button);
            }
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

                    // Issue #458: ダブルタップ検出（設定済みスロットのみ）
                    float currentTime = Time.time;
                    bool isConfiguredSlot = slotDataMap.TryGetValue(button, out var slotData) && slotData != null && slotData.IsConfigured;

                    if (isConfiguredSlot && lastClickedSlotButton == button && (currentTime - lastSlotClickTime) <= DOUBLE_TAP_THRESHOLD)
                    {
                        // ダブルタップ検出
                        Debug.Log($"👆👆 Double tap detected on slot: {button.name}");
                        lastClickedSlotButton = null;
                        lastSlotClickTime = 0f;
                        OnSlotDoubleTapped(button, slotData);
                        return;
                    }

                    // シングルクリック（設定済みスロットはダブルタップを待機）
                    lastClickedSlotButton = isConfiguredSlot ? button : null;
                    lastSlotClickTime = currentTime;

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
        /// キャッシュクリアポップアップを表示
        /// Issue #459: +ボタン長押しでキャッシュクリアポップアップ
        /// </summary>
        void ShowClearCachePopup()
        {
            if (clearCachePopup == null)
            {
                Debug.LogError("❌ clearCachePopup is null!");
                return;
            }

            if (bottomButtonAdd == null)
            {
                Debug.LogError("❌ bottomButtonAdd is null!");
                return;
            }

            // ポップアップをボタンの上部に配置
            var buttonBounds = bottomButtonAdd.worldBound;
            Debug.Log($"📍 Add button bounds: x={buttonBounds.x}, y={buttonBounds.y}, width={buttonBounds.width}, height={buttonBounds.height}");

            // ポップアップサイズ
            float popupWidth = 140f;
            float popupHeight = 90f;

            // ボタンの中央にポップアップを配置（水平方向）
            float popupLeft = buttonBounds.x + (buttonBounds.width / 2) - (popupWidth / 2);

            // ボタンの上に配置（垂直方向） - 10pxの余白
            float popupTop = buttonBounds.y - popupHeight - 10;

            Debug.Log($"📍 Clear cache popup position: left={popupLeft}, top={popupTop}");

            clearCachePopup.style.left = popupLeft;
            clearCachePopup.style.top = popupTop;
            clearCachePopup.style.display = DisplayStyle.Flex;

            Debug.Log("🗑 Clear cache popup shown");

            // Heavy impact for popup appearance
            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        /// <summary>
        /// キャッシュクリアポップアップを非表示
        /// </summary>
        void HideClearCachePopup()
        {
            if (clearCachePopup == null) return;

            clearCachePopup.style.display = DisplayStyle.None;
            Debug.Log("❌ Clear cache popup hidden");

            // Light impact for cancel
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        /// <summary>
        /// キャッシュクリアボタンがクリックされた時の処理
        /// Issue #459
        /// </summary>
        void OnClearCacheButtonClicked()
        {
            Debug.Log("🗑 Clear cache button clicked");

            // キャッシュクリア実行
            ClearAllAvatarCache();

            // ポップアップを閉じる
            HideClearCachePopup();

            // Medium impact for action
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// 全アバターキャッシュをクリア
        /// Issue #459
        /// </summary>
        void ClearAllAvatarCache()
        {
            Debug.Log("🗑 Clearing all avatar cache...");

            // AvatarSlotManagerのキャッシュをクリア
            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            if (slotManager != null)
            {
                var cache = slotManager.Cache;
                if (cache != null)
                {
                    for (int i = 0; i < cache.maxSlots; i++)
                    {
                        slotManager.ClearSlot(i);
                    }
                }
            }

            // メモリキャッシュをクリア
            var memoryCache = AICam.AvatarCache.AvatarMemoryCache.Instance;
            if (memoryCache != null)
            {
                memoryCache.ClearAll();
            }

            // UIのスロットアイコンをリフレッシュ
            RefreshAllSlotIcons();

            // 通知
            ShowInfo("Cache", "キャッシュをクリアしました", 2f);
            Debug.Log("✅ All avatar cache cleared");
        }

        /// <summary>
        /// 全スロットのアイコンをリフレッシュ
        /// </summary>
        void RefreshAllSlotIcons()
        {
            if (bottomButtonContainer == null) return;

            var buttons = bottomButtonContainer.Query<Button>().ToList();
            foreach (var button in buttons)
            {
                if (button == bottomButtonAdd) continue;

                // アイコンをクリア
                button.style.backgroundImage = StyleKeyword.None;

                // slotDataMapもクリア
                if (slotDataMap.ContainsKey(button))
                {
                    slotDataMap[button] = new SlotData();
                }
            }

            Debug.Log("✅ All slot icons refreshed");
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

            // スロットデータをクリア（永続化キャッシュ・メモリキャッシュ・バイナリキャッシュ）
            int slotIndex = GetSlotIndexFromButton(currentLongPressButton);
            if (slotIndex >= 0)
            {
                var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                if (slotManager != null)
                {
                    slotManager.ClearSlot(slotIndex);
                    Debug.Log($"🗑 Cleared slot data for index {slotIndex}");
                }
            }

            // slotDataMap からも削除
            if (slotDataMap.ContainsKey(currentLongPressButton))
            {
                slotDataMap.Remove(currentLongPressButton);
            }

            // ボタンを削除
            bottomButtonContainer.Remove(currentLongPressButton);
            HideDeletePopup();

            // Issue #462: スロットボタン数を更新して保存
            bottomButtonCount = 0;
            foreach (var child in bottomButtonContainer.Children())
            {
                if (child is Button btn && btn != bottomButtonAdd)
                    bottomButtonCount++;
            }
            SaveSlotCountToCache();

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
            if (IsPointOverElement(captureSettingBar, panelPosition, "captureSettingBar")) return true; // Issue #451
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
            if (alertService != null && alertService.IsAlertVisible && alertService.AlertWorldBound.Contains(panelPosition))
            {
                return true;
            }

            // Phase 02: コントローラー経由のチェック
            if (iconPreviewController != null && iconPreviewController.IsVisible)
            {
                Debug.Log($"[#71] Touch over iconPreviewPanel (visible)");
                return true;
            }

            if (mediaViewerController != null && mediaViewerController.IsViewerVisible)
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

                // IAvatarPlacerのavatarもクリア
                var placer = FindAvatarPlacer();
                if (placer != null)
                {
                    var existingAvatar = placer.PlacedAvatar;
                    if (existingAvatar != null)
                    {
                        Debug.Log($"🗑️ Destroying existing avatar in IAvatarPlacer: {existingAvatar.name}");
                        Destroy(existingAvatar);
                        placer.PlacedAvatar = null;
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
                SetupExpressionSystem(avatar, GetSlotIndexFromButton(targetButton));
                TriggerExpressionIconGeneration(avatar, GetSlotIndexFromButton(targetButton));

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

                // アイコンをファイルに保存
                string iconPath = SaveThumbnailToFile(targetButton, thumbnail);

                // Issue #458: AvatarSlotManagerのキャッシュと同期（VRM用）
                SyncWithAvatarSlotManagerForVRM(targetButton, filePath, avatar, iconPath);

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
                SetupExpressionSystem(loadedModel, GetSlotIndexFromButton(targetButton));
                TriggerExpressionIconGeneration(loadedModel, GetSlotIndexFromButton(targetButton));

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

                // アイコンをファイルに保存
                string iconPath = SaveThumbnailToFile(targetButton, thumbnail);

                // Issue #458: AvatarSlotManagerのキャッシュと同期
                SyncWithAvatarSlotManager(targetButton, filePath, loadedModel.name, iconPath);

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
            // ロード中なら無視（同じスロットの連打防止）
            if (isSlotLoading)
            {
                Debug.Log($"🔄 A slot is already loading (current: {currentLoadingSlot?.name}), ignoring click on {button.name}");
                return;
            }

            // スロットデータを取得
            if (!slotDataMap.TryGetValue(button, out var slotData))
            {
                slotData = null;
            }

            if (slotData != null && slotData.IsConfigured)
            {
                // 設定済みスロット → アバターを切り替え
                Debug.Log($"🔄 Switching to avatar in slot: {button.name}");

                // ★ 即座にロード中フラグを設定（連打防止のためSwitchToSlotAvatarより前に設定）
                isSlotLoading = true;
                currentLoadingSlot = button;

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
        /// スロットダブルタップ時の処理
        /// Issue #458: エクスポートポップアップを表示
        /// </summary>
        void OnSlotDoubleTapped(Button button, SlotData slotData)
        {
            if (slotData == null || !slotData.IsConfigured)
            {
                Debug.LogWarning($"[CameraCaptureController] Cannot export unconfigured slot: {button.name}");
                return;
            }

            Debug.Log($"📤 Double tap on configured slot, showing export popup: {button.name}");

            int slotIndex = GetSlotIndexFromButton(button);

            // AvatarSlotDataを取得または作成
            AICam.AvatarCache.AvatarSlotData avatarSlotData = null;
            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;

            if (slotManager?.Cache != null)
            {
                avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            }

            // キャッシュにない場合は、CameraCaptureControllerのslotDataから作成
            if (avatarSlotData == null || !avatarSlotData.IsConfigured)
            {
                Debug.Log($"[CameraCaptureController] Creating AvatarSlotData from local slotData for slot {slotIndex}");
                avatarSlotData = new AICam.AvatarCache.AvatarSlotData(slotIndex)
                {
                    modelFilePath = slotData.filePath,
                    avatarName = slotData.loadedAvatar != null ? slotData.loadedAvatar.name : System.IO.Path.GetFileNameWithoutExtension(slotData.filePath)
                };

                // バイナリキャッシュを作成（現在のアバターから）
                if (slotData.loadedAvatar != null)
                {
                    CreateBinaryCacheAndShowPopup(slotIndex, avatarSlotData, slotData.loadedAvatar);
                }
                else
                {
                    Debug.LogWarning($"[CameraCaptureController] No loaded avatar for slot {slotIndex}");
                    ShowInfo("Error", "アバターがロードされていません", 2f);
                }
            }
            else
            {
                // 既存のキャッシュデータでポップアップを表示
                ShowExportPopupDirect(slotIndex, avatarSlotData);
            }

            // ハプティックフィードバック
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// バイナリキャッシュを作成してからポップアップを表示
        /// </summary>
        async void CreateBinaryCacheAndShowPopup(int slotIndex, AICam.AvatarCache.AvatarSlotData avatarSlotData, GameObject avatar)
        {
            try
            {
                ShowInfo("Cache", "キャッシュを作成中...", 3f);

                // AvatarCacheIntegratorを使用してバイナリキャッシュを作成
                var cacheIntegrator = new AICam.AvatarCache.AvatarCacheIntegrator(Application.persistentDataPath);
                string cacheId = await cacheIntegrator.CreateBinaryCacheAsync(avatar, avatarSlotData.modelFilePath);

                if (!string.IsNullOrEmpty(cacheId))
                {
                    avatarSlotData.binaryCacheId = cacheId;

                    // AvatarSlotManagerのキャッシュを更新
                    var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                    if (slotManager?.Cache != null)
                    {
                        slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
                        slotManager.Cache.SaveToFile();
                    }

                    Debug.Log($"[CameraCaptureController] Binary cache created: {cacheId}");

#if BLENDSHAPE_CONTROLLER
                    SaveExpressionDataToCache(avatar, cacheId);
#endif

                    // ポップアップを表示
                    ShowExportPopupDirect(slotIndex, avatarSlotData);
                }
                else
                {
                    Debug.LogWarning($"[CameraCaptureController] Failed to create binary cache for slot {slotIndex}");
                    ShowWarning("CACHE_ERROR", "キャッシュの作成に失敗しました");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CameraCaptureController] Error creating binary cache: {e.Message}");
                ShowError("CACHE_ERROR", $"キャッシュエラー: {e.Message}");
            }
        }

        /// <summary>
        /// エクスポートポップアップを直接表示
        /// </summary>
        void ShowExportPopupDirect(int slotIndex, AICam.AvatarCache.AvatarSlotData avatarSlotData)
        {
            var popup = AICam.FBXLoader.ExportPopup.Instance;
            if (popup == null)
            {
                Debug.Log("[CameraCaptureController] Creating ExportPopup instance");
                var popupObj = new GameObject("ExportPopup");
                popup = popupObj.AddComponent<AICam.FBXLoader.ExportPopup>();
            }

            Debug.Log($"[CameraCaptureController] Showing export popup for slot {slotIndex}, binaryCacheId: {avatarSlotData.binaryCacheId}");
            // rootVisualElementを渡してUI要素を正しく初期化
            popup.Show(slotIndex, avatarSlotData, root, (success, path) =>
            {
                if (success)
                {
                    Debug.Log($"📤 Export completed: {path}");
                    ShowInfo("Export", "エクスポート完了", 2f);
                }
            });
        }

        /// <summary>
        /// ボタンからスロットインデックスを取得
        /// </summary>
        int GetSlotIndexFromButton(Button button)
        {
            if (button == null) return -1;

            // ボタン名から番号を抽出（bottomButton1 → 0, bottomButton2 → 1, ...）
            string name = button.name;
            if (name.StartsWith("bottomButton"))
            {
                string numStr = name.Replace("bottomButton", "");
                if (int.TryParse(numStr, out int num))
                {
                    return num - 1; // 1-indexed → 0-indexed
                }
            }
            return -1;
        }

        /// <summary>
        /// AvatarSlotManagerのキャッシュと同期
        /// Issue #458: エクスポート機能のために必要
        /// </summary>
        void SyncWithAvatarSlotManager(Button button, string filePath, string avatarName, string iconFilePath = null)
        {
            int slotIndex = GetSlotIndexFromButton(button);
            if (slotIndex < 0)
            {
                Debug.LogWarning($"[CameraCaptureController] Cannot sync - invalid slot index for {button.name}");
                return;
            }

            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            if (slotManager == null || slotManager.Cache == null)
            {
                Debug.LogWarning("[CameraCaptureController] AvatarSlotManager not available for sync");
                return;
            }

            // スロットデータを取得または作成
            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            if (avatarSlotData == null)
            {
                avatarSlotData = new AICam.AvatarCache.AvatarSlotData(slotIndex);
            }

            // ファイルパスとアバター名を設定
            avatarSlotData.modelFilePath = filePath;
            avatarSlotData.avatarName = avatarName;
            avatarSlotData.isValid = true;
            avatarSlotData.fileType = AICam.AvatarCache.AvatarFileType.FBX;

            // アイコンパスを設定
            if (!string.IsNullOrEmpty(iconFilePath))
            {
                avatarSlotData.iconFilePath = iconFilePath;
            }

            // キャッシュを更新
            slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
            slotManager.Cache.SetLastActiveSlot(slotIndex);  // Issue #416: lastActiveSlotIndexを設定
            slotManager.Cache.SaveToFile();

            Debug.Log($"[CameraCaptureController] Synced slot {slotIndex} with AvatarSlotManager: {avatarName}, icon={iconFilePath ?? "none"}, lastActive={slotIndex}");

            // バイナリキャッシュを作成（AvatarMemoryCacheが利用可能な場合）
            var memoryCache = FindFirstObjectByType<AICam.AvatarCache.AvatarMemoryCache>();
            if (memoryCache != null && fbxLoaderBridge != null && fbxLoaderBridge.CurrentModel != null)
            {
                // 非同期でバイナリキャッシュを作成
                CreateBinaryCacheAsync(slotIndex, avatarSlotData, memoryCache).Forget();
            }
        }

        /// <summary>
        /// AvatarSlotManagerのキャッシュと同期（VRM用）
        /// Issue #458: エクスポート機能のために必要
        /// </summary>
        void SyncWithAvatarSlotManagerForVRM(Button button, string filePath, GameObject avatar, string iconFilePath = null)
        {
            int slotIndex = GetSlotIndexFromButton(button);
            if (slotIndex < 0)
            {
                Debug.LogWarning($"[CameraCaptureController] Cannot sync VRM - invalid slot index for {button.name}");
                return;
            }

            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            if (slotManager == null || slotManager.Cache == null)
            {
                Debug.LogWarning("[CameraCaptureController] AvatarSlotManager not available for VRM sync");
                return;
            }

            // スロットデータを取得または作成
            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            if (avatarSlotData == null)
            {
                avatarSlotData = new AICam.AvatarCache.AvatarSlotData(slotIndex);
            }

            // ファイルパスとアバター名を設定
            avatarSlotData.modelFilePath = filePath;
            avatarSlotData.avatarName = avatar != null ? avatar.name : Path.GetFileNameWithoutExtension(filePath);
            avatarSlotData.isValid = true;
            avatarSlotData.fileType = AICam.AvatarCache.AvatarFileType.VRM;

            // アイコンパスを設定
            if (!string.IsNullOrEmpty(iconFilePath))
            {
                avatarSlotData.iconFilePath = iconFilePath;
            }

            // キャッシュを更新
            slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
            slotManager.Cache.SetLastActiveSlot(slotIndex);  // Issue #416: lastActiveSlotIndexを設定
            slotManager.Cache.SaveToFile();

            Debug.Log($"[CameraCaptureController] Synced VRM slot {slotIndex} with AvatarSlotManager: {avatarSlotData.avatarName}, icon={iconFilePath ?? "none"}, lastActive={slotIndex}");

            // VRM用バイナリキャッシュを作成
            if (avatar != null)
            {
                CreateBinaryCacheForVRMAsync(slotIndex, avatarSlotData, avatar, iconFilePath).Forget();
            }
        }

        /// <summary>
        /// VRM用バイナリキャッシュを非同期で作成
        /// </summary>
        async UniTaskVoid CreateBinaryCacheForVRMAsync(int slotIndex, AICam.AvatarCache.AvatarSlotData avatarSlotData, GameObject avatar, string iconSourcePath = null)
        {
            try
            {
                // AvatarCacheIntegratorを取得
                var cacheIntegrator = new AICam.AvatarCache.AvatarCacheIntegrator(Application.persistentDataPath);

                // バイナリキャッシュを作成（アイコンも含む）
                string cacheId = await cacheIntegrator.CreateBinaryCacheAsync(avatar, avatarSlotData.modelFilePath, iconSourcePath);

                if (!string.IsNullOrEmpty(cacheId))
                {
                    avatarSlotData.binaryCacheId = cacheId;
                    var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                    if (slotManager?.Cache != null)
                    {
                        slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
                        slotManager.Cache.SaveToFile();
                    }
                    Debug.Log($"[CameraCaptureController] VRM binary cache created for slot {slotIndex}: {cacheId}");

#if BLENDSHAPE_CONTROLLER
                    SaveExpressionDataToCache(avatar, cacheId);
#endif
                }
                else
                {
                    Debug.LogWarning($"[CameraCaptureController] Failed to create VRM binary cache for slot {slotIndex}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CameraCaptureController] Error creating VRM binary cache: {e.Message}");
            }
        }

        /// <summary>
        /// バイナリキャッシュを非同期で作成
        /// </summary>
        async UniTaskVoid CreateBinaryCacheAsync(int slotIndex, AICam.AvatarCache.AvatarSlotData avatarSlotData, AICam.AvatarCache.AvatarMemoryCache memoryCache)
        {
            try
            {
                // 現在のモデルを取得
                GameObject currentModel = fbxLoaderBridge?.CurrentModel;
                if (currentModel == null)
                {
                    Debug.LogWarning($"[CameraCaptureController] No current model available for binary cache");
                    return;
                }

                // AvatarCacheIntegratorを取得
                var cacheIntegrator = new AICam.AvatarCache.AvatarCacheIntegrator(Application.persistentDataPath);

                // バイナリキャッシュを作成（キャッシュIDはファイルハッシュから自動生成される）
                string cacheId = await cacheIntegrator.CreateBinaryCacheAsync(currentModel, avatarSlotData.modelFilePath);

                if (!string.IsNullOrEmpty(cacheId))
                {
                    avatarSlotData.binaryCacheId = cacheId;
                    var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                    if (slotManager?.Cache != null)
                    {
                        slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
                        slotManager.Cache.SaveToFile();
                    }
                    Debug.Log($"[CameraCaptureController] Binary cache created for slot {slotIndex}: {cacheId}");

#if BLENDSHAPE_CONTROLLER
                    SaveExpressionDataToCache(currentModel, cacheId);
#endif
                }
                else
                {
                    Debug.LogWarning($"[CameraCaptureController] Failed to create binary cache for slot {slotIndex}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[CameraCaptureController] Error creating binary cache: {e.Message}");
            }
        }

        /// <summary>
        /// スロットのアバターに切り替え
        /// </summary>
        async void SwitchToSlotAvatar(Button button, SlotData slotData)
        {
            // ★ 注意: isSlotLoadingフラグはOnSlotClickedで既に設定されている
            // 全ての早期リターンでフラグをリセットする必要がある

            if (slotData == null || !slotData.IsConfigured)
            {
                // フラグをリセット
                isSlotLoading = false;
                currentLoadingSlot = null;
                return;
            }

            // 既に選択中のスロットなら何もしない
            if (currentSelectedSlot == button)
            {
                Debug.Log($"🔄 Already selected slot: {button.name}");
                // フラグをリセット
                isSlotLoading = false;
                currentLoadingSlot = null;
                return;
            }

            // 選択状態を更新
            UpdateSlotSelection(button);

            // アバターがまだロードされていない場合はロード
            if (slotData.loadedAvatar == null)
            {
                Debug.Log($"🔄 Avatar not loaded, loading from: {slotData.filePath}");

                // ★ isSlotLoadingはOnSlotClickedで既に設定済み

                try
                {
                    // まずバイナリキャッシュからの復元を試みる
                    bool loadedFromCache = await TryLoadFromBinaryCacheAsync(button, slotData);

                    if (!loadedFromCache)
                    {
                        // キャッシュからの復元に失敗した場合は、元のファイルからロード
                        Debug.Log($"🔄 Binary cache not available, loading from original file: {slotData.filePath}");

                        if (slotData.fileType == SlotFileType.VRM)
                        {
                            await LoadVRMFileAsync(slotData.filePath, button);
                        }
                        else if (slotData.fileType == SlotFileType.FBX)
                        {
                            await LoadFBXFileAsync(slotData.filePath, button);
                        }
                    }
                }
                finally
                {
                    // ロード完了
                    isSlotLoading = false;
                    currentLoadingSlot = null;
                }
            }
            else
            {
                // 既存のアバターを非表示にして、このスロットのアバターを表示
                Debug.Log($"🔄 Activating avatar: {slotData.loadedAvatar.name}");
                ActivateSlotAvatar(slotData);

                // フラグをリセット（既にロード済みのアバターを表示するだけなので即座にリセット）
                isSlotLoading = false;
                currentLoadingSlot = null;
            }

            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// バイナリキャッシュからアバターを復元する
        /// </summary>
        /// <param name="button">対象のスロットボタン</param>
        /// <param name="slotData">スロットデータ</param>
        /// <returns>復元に成功した場合はtrue</returns>
        async UniTask<bool> TryLoadFromBinaryCacheAsync(Button button, SlotData slotData)
        {
            int slotIndex = GetSlotIndexFromButton(button);
            if (slotIndex < 0)
            {
                Debug.Log($"[BinaryCache] Invalid slot index for {button.name}");
                return false;
            }

            // AvatarSlotManagerからbinaryCacheIdを取得
            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            if (slotManager?.Cache == null)
            {
                Debug.Log("[BinaryCache] AvatarSlotManager not available");
                return false;
            }

            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            if (avatarSlotData == null || string.IsNullOrEmpty(avatarSlotData.binaryCacheId))
            {
                Debug.Log($"[BinaryCache] No binaryCacheId for slot {slotIndex}");
                return false;
            }

            string cacheId = avatarSlotData.binaryCacheId;
            Debug.Log($"🚀 Attempting to load from binary cache: {cacheId}");

            // Issue #73: プログレス表示開始
            StartSlotLoading(button);
            UpdateSlotProgress(button, 0.1f); // 10%: 開始

            try
            {
                var cacheIntegrator = new AICam.AvatarCache.AvatarCacheIntegrator(Application.persistentDataPath);

                // キャッシュが存在するか確認
                if (!cacheIntegrator.HasBinaryCache(cacheId))
                {
                    Debug.Log($"[BinaryCache] Cache not found: {cacheId}");
                    CancelSlotLoading(button);
                    return false;
                }

                UpdateSlotProgress(button, 0.3f); // 30%: キャッシュ確認完了

                // バイナリキャッシュからロード（slotIndexを渡してアイコンも復元）
                var avatar = await cacheIntegrator.LoadFromBinaryCacheAsync(cacheId, progress =>
                {
                    // 30-70%: ロード中
                    UpdateSlotProgress(button, 0.3f + (progress / 100f) * 0.4f);
                }, slotIndex);

                if (avatar == null)
                {
                    Debug.LogWarning($"[BinaryCache] Failed to load from cache: {cacheId}");
                    CancelSlotLoading(button);
                    return false;
                }

                Debug.Log($"✅ Avatar loaded from binary cache: {avatar.name}");

                UpdateSlotProgress(button, 0.7f); // 70%: アバター生成完了

                // 既存のアバターをクリア
                if (avatarLoader != null)
                {
                    avatarLoader.ClearCurrentAvatar();
                }

                // IAvatarPlacerのavatarもクリア
                var placer2 = FindAvatarPlacer();
                if (placer2 != null)
                {
                    var existingAvatar = placer2.PlacedAvatar;
                    if (existingAvatar != null)
                    {
                        Debug.Log($"🗑️ Destroying existing avatar in IAvatarPlacer: {existingAvatar.name}");
                        Destroy(existingAvatar);
                        placer2.PlacedAvatar = null;
                    }
                }

                // Issue #407: デフォルトのAOCを適用
                ApplyDefaultAOC(avatar);

                // Issue #145/#411: 表情システムをセットアップ
                SetupExpressionSystem(avatar, slotIndex);
                TriggerExpressionIconGeneration(avatar, slotIndex);

                // Issue #425: アバターをカメラの1m前方に配置
                PlaceAvatarAheadOfCamera(avatar);

                // Issue #442: ライティング・シャドウ設定を再適用
                ReapplyLightingSettings();

                UpdateSlotProgress(button, 0.85f); // 85%: 配置完了

                // レンダリングが安定するまで待機
                await UniTask.DelayFrame(3);

                // Issue #73: プログレス完了
                CompleteSlotLoading(button);

                // スロットデータを更新
                slotData.loadedAvatar = avatar;
                Debug.Log($"💾 Slot data updated from binary cache for {button.name}");

                // アイコンを復元または新規キャプチャ
                string iconPath = AvatarSlotCache.GetIconPath(slotIndex);
                bool iconRestored = false;

                // 1. まずキャッシュから復元されたアイコンファイルを確認
                if (File.Exists(iconPath))
                {
                    try
                    {
                        byte[] iconData = File.ReadAllBytes(iconPath);
                        var texture = new Texture2D(2, 2);
                        if (texture.LoadImage(iconData))
                        {
                            slotData.thumbnail = texture;
                            UpdateButtonIcon(button, texture);
                            iconRestored = true;
                            Debug.Log($"[BinaryCache] Icon restored from cache for slot {slotIndex}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[BinaryCache] Failed to load restored icon: {ex.Message}");
                    }
                }

                // 2. アイコンがなければ新規キャプチャ
                if (!iconRestored && avatar != null)
                {
                    await UniTask.DelayFrame(3);
                    var thumbnail = await AICam.FBXLoader.AvatarIconCapture.Instance.CaptureAsTextureAsync(avatar);
                    if (thumbnail != null)
                    {
                        slotData.thumbnail = thumbnail;
                        UpdateButtonIcon(button, thumbnail);
                        iconPath = SaveThumbnailToFile(button, thumbnail);
                        Debug.Log($"[BinaryCache] New icon captured for slot {slotIndex}");
                    }
                }

                // 3. iconFilePathを永続キャッシュに更新
                if (File.Exists(iconPath) && avatarSlotData.iconFilePath != iconPath)
                {
                    avatarSlotData.iconFilePath = iconPath;
                    slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
                    slotManager.Cache.SaveToFile();
                }

                // 選択状態を更新
                UpdateSlotSelection(button);

                // cachedCurrentAvatarを更新
                cachedCurrentAvatar = avatar;

                TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BinaryCache] Error loading from cache: {e.Message}");
                CancelSlotLoading(button);
                return false;
            }
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

            // Issue #471: スロット切り替え時に BlendshapeController をリセット
#if BLENDSHAPE_CONTROLLER
            blendShapeExpressionManager = null;
#endif

            // 選択したスロットのアバターを表示・配置
            if (slotData?.loadedAvatar != null)
            {
                // カメラ前方に配置（PlaceAvatarOnPlaneOnly の内部 avatar も更新される）
                PlaceAvatarAheadOfCamera(slotData.loadedAvatar);

                // Issue #407: キャッシュを更新してポーズ切り替えが動作するようにする
                cachedCurrentAvatar = slotData.loadedAvatar;
                Debug.Log($"Updated cachedCurrentAvatar: {cachedCurrentAvatar.name}");

                // Issue #471: 切り替え先アバターの ExpressionSetManager を復元
#if BLENDSHAPE_CONTROLLER
                var manager = slotData.loadedAvatar.GetComponent<ExpressionSetManager>();
                if (manager != null)
                {
                    blendShapeExpressionManager = manager;
                    expressionSetup = null;
                    vrm0ExpressionController = null;
                    Debug.Log($"[Expression] Restored BlendShape expression manager: {manager.Collection?.CurrentSet?.Count ?? 0} expressions");
                }
#endif
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
        /// サムネイルをファイルに保存し、パスを返す
        /// </summary>
        string SaveThumbnailToFile(Button button, Texture2D thumbnail)
        {
            if (thumbnail == null) return null;

            int slotIndex = GetSlotIndexFromButton(button);
            if (slotIndex < 0) return null;

            try
            {
                string iconPath = AvatarSlotCache.GetIconPath(slotIndex);
                string iconDir = Path.GetDirectoryName(iconPath);
                if (!Directory.Exists(iconDir))
                    Directory.CreateDirectory(iconDir);

                byte[] pngData = thumbnail.EncodeToPNG();
                File.WriteAllBytes(iconPath, pngData);
                Debug.Log($"[ICON] Saved icon to {iconPath} ({pngData.Length} bytes)");
                return iconPath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ICON] Failed to save icon for slot {slotIndex}: {e.Message}");
                return null;
            }
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
        /// Issue #452: サイドバーボタン3（Flash/Torch）クリック時の処理
        /// デバイスの背面ライト（トーチ）をON/OFFする
        /// </summary>
        void OnSideButton3Clicked()
        {
            Debug.Log("⚡ Side button 3 (Flash) clicked");
            TapticEngine.Selection();

            // ARCameraManagerを取得
            if (cachedARCameraManager == null)
            {
                cachedARCameraManager = FindFirstObjectByType<ARCameraManager>();
            }

            if (cachedARCameraManager == null)
            {
                Debug.LogWarning("[Torch] ARCameraManager not found");
                ShowWarning("W452", "カメラが見つかりません");
                return;
            }

            // トーチの状態をトグル
            isTorchEnabled = !isTorchEnabled;

            // AR Foundation のトーチモードを設定
            cachedARCameraManager.requestedCameraTorchMode = isTorchEnabled
                ? UnityEngine.XR.ARSubsystems.XRCameraTorchMode.On
                : UnityEngine.XR.ARSubsystems.XRCameraTorchMode.Off;

            Debug.Log($"[Torch] Torch mode set to: {(isTorchEnabled ? "ON" : "OFF")}");

            // アイコンを更新
            UpdateTorchIcon();
        }

        /// <summary>
        /// Issue #452: トーチアイコンを状態に応じて更新
        /// CSSクラスで切り替え: torch-on / torch-off
        /// </summary>
        void UpdateTorchIcon()
        {
            if (sideButton3 == null) return;

            if (isTorchEnabled)
            {
                sideButton3.RemoveFromClassList("torch-off");
                sideButton3.AddToClassList("torch-on");
            }
            else
            {
                sideButton3.RemoveFromClassList("torch-on");
                sideButton3.AddToClassList("torch-off");
            }
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
                var memoryCache = AvatarMemoryCache.Instance;
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
        /// Issue #416: AvatarLoadHandler経由のロード完了時のハンドラ
        /// リストア時やAvatarLoadHandler.LoadAsync経由のロード時にAOCを適用
        /// </summary>
        void OnAvatarLoadHandlerComplete(AICam.FBXLoader.LoadResult result)
        {
            Debug.Log($"🎭 OnAvatarLoadHandlerComplete called: slotIndex={result.SlotIndex}, success={result.Success}, avatar={result.Avatar?.name ?? "null"}");
            if (result.Success && result.Avatar != null)
            {
                cachedCurrentAvatar = result.Avatar;
                currentPoseIndex = 0;  // ポーズインデックスをリセット
                cachedStateNames = null;  // State名キャッシュをリセット

                // AOCを適用
                ApplyDefaultAOC(result.Avatar);

                // 表情システムをセットアップ
                SetupExpressionSystem(result.Avatar, result.SlotIndex);
                TriggerExpressionIconGeneration(result.Avatar, result.SlotIndex);

                Debug.Log($"🎭 Avatar setup complete from AvatarLoadHandler: {result.Avatar.name}");
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

            // Issue #471: BlendshapeController SDK フォールバック
#if BLENDSHAPE_CONTROLLER
            if (blendShapeExpressionManager != null)
            {
                int indexBefore = blendShapeExpressionManager.CurrentExpressionIndex;
                blendShapeExpressionManager.NextExpression();
                int indexAfter = blendShapeExpressionManager.CurrentExpressionIndex;
                var current = blendShapeExpressionManager.CurrentExpression;
                Debug.Log($"BlendShape Expression switched: {indexBefore} -> {indexAfter}, Name: {current?.name}");
                return;
            }
#endif

            Debug.LogWarning("No expression controller available - load a VRM avatar first");
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

            // Issue #471: BlendshapeController SDK フォールバック
#if BLENDSHAPE_CONTROLLER
            if (blendShapeExpressionManager != null)
            {
                blendShapeExpressionManager.ResetAllBlendShapes();
                Debug.Log("BlendShape Expression reset to neutral");
                return;
            }
#endif

            Debug.LogWarning("No expression controller available - load a VRM avatar first");
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
                var memoryCache = AvatarMemoryCache.Instance;

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
            var memoryCache = AvatarMemoryCache.Instance;

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
        /// IAvatarPlacerの検索（キャッシュ付き）
        /// </summary>
        private IAvatarPlacer FindAvatarPlacer()
        {
            if (cachedAvatarPlacer != null && cachedAvatarPlacer is MonoBehaviour mb && mb != null)
                return cachedAvatarPlacer;

            foreach (var obj in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (obj is IAvatarPlacer placer)
                {
                    cachedAvatarPlacer = placer;
                    return placer;
                }
            }
            return null;
        }

        /// <summary>
        /// Issue #425: アバターをカメラの1m前方に配置
        /// IAvatarPlacerを使用して平面優先で配置
        /// </summary>
        void PlaceAvatarAheadOfCamera(GameObject avatar)
        {
            if (avatar == null) return;

            var placer = FindAvatarPlacer();
            if (placer != null)
            {
                bool success = placer.PlaceAvatarAhead(avatar, 1.5f);
                Debug.Log($"📍 Issue #425: Avatar placement result: {(success ? "success" : "failed")}");
            }
            else
            {
                Debug.LogWarning("⚠️ IAvatarPlacer not found - avatar position unchanged");
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

#if BLENDSHAPE_CONTROLLER
        // Issue #471: キャッシュロード時のフォールバック表情コントローラー
        private ExpressionSetManager blendShapeExpressionManager;
#endif

        /// <summary>
        /// Issue #145/#411: VRM表情システムをセットアップ
        /// VRM 1.0とVRM 0.xの両方に対応
        /// </summary>
        void SetupExpressionSystem(GameObject avatar, int slotIndex = -1)
        {
            if (avatar == null) return;

            Debug.Log($"SetupExpressionSystem: Starting setup for {avatar.name}, slotIndex={slotIndex}");

            // VRM 1.0を確認
            var vrm10Instance = avatar.GetComponent<UniVRM10.Vrm10Instance>();
            if (vrm10Instance != null)
            {
                Debug.Log($"SetupExpressionSystem: VRM 1.0 detected");
                SetupVrm10ExpressionSystem(avatar, vrm10Instance);
                return;
            }

            // VRM 0.xを確認
            var blendShapeProxy = avatar.GetComponent<global::VRM.VRMBlendShapeProxy>();
            if (blendShapeProxy != null)
            {
                Debug.Log($"SetupExpressionSystem: VRM 0.x detected");
                SetupVrm0ExpressionSystem(avatar, blendShapeProxy);
                return;
            }

            // Issue #471: キャッシュロード時のフォールバック（BlendshapeController SDK）
#if BLENDSHAPE_CONTROLLER
            if (TrySetupBlendShapeExpressionSystem(avatar, slotIndex))
            {
                return;
            }
#endif

            Debug.LogWarning($"SetupExpressionSystem: {avatar.name} - no expression support");
        }

#if BLENDSHAPE_CONTROLLER
        /// <summary>
        /// Issue #471: キャッシュロードされたアバター用の BlendshapeController SDK フォールバック
        /// 1. expressions.json があればそれを使用
        /// 2. なければアバターのブレンドシェイプから直接構築（VRoidStudio アバターの場合）
        /// </summary>
        bool TrySetupBlendShapeExpressionSystem(GameObject avatar, int slotIndex)
        {
            // expressions.json からの読み込みを試行
            string jsonPath = null;
            string cacheDir = null;
            if (slotIndex >= 0)
            {
                var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                if (slotManager?.Cache != null)
                {
                    var slotData = slotManager.Cache.GetSlot(slotIndex);
                    if (slotData != null && !string.IsNullOrEmpty(slotData.binaryCacheId))
                    {
                        cacheDir = Path.Combine(Application.persistentDataPath, "AvatarCache", slotData.binaryCacheId);
                        jsonPath = Path.Combine(cacheDir, "expressions.json");
                    }
                }
            }

            // パス1: expressions.json が存在する場合
            if (jsonPath != null && File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    if (SetupBlendShapeManager(avatar, json))
                    {
                        Debug.Log($"[Expression] BlendShape setup from expressions.json: {blendShapeExpressionManager.Collection?.CurrentSet?.Count ?? 0} expressions");
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Expression] Failed to load expressions.json: {e.Message}");
                }
            }

            // パス2: expressions.json がない場合、アバターから直接構築
            Debug.Log("[Expression] No expressions.json found, attempting direct blendshape scan...");
            ExpressionSet expressionSet = null;

            if (AICam.VRM.VrmExpressionBridge.IsVRoidStudioAvatar(avatar))
            {
                expressionSet = AICam.VRM.VrmExpressionBridge.GetStandardExpressionSet();
                Debug.Log("[Expression] VRoidStudio avatar detected, using standard expression set");
            }

            if (expressionSet == null || expressionSet.Count == 0)
            {
                Debug.Log("[Expression] Cannot build expression set from avatar blendshapes");
                return false;
            }

            // ExpressionSetCollection を構築
            var collection = new ExpressionSetCollection
            {
                collectionName = "VRM Expressions",
                avatarName = avatar.name
            };
            collection.AddSet(expressionSet);

            // JSON にシリアライズしてマネージャーにロード
            string collectionJson = ExpressionSetSerializer.ToJson(collection);
            if (!SetupBlendShapeManager(avatar, collectionJson))
            {
                return false;
            }

            // 次回のために expressions.json を保存
            if (cacheDir != null)
            {
                try
                {
                    string savePath = Path.Combine(cacheDir, "expressions.json");
                    ExpressionSetSerializer.SaveCollection(collection, savePath);
                    Debug.Log($"[Expression] Saved generated expressions.json to cache: {savePath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Expression] Failed to save expressions.json: {e.Message}");
                }
            }

            Debug.Log($"[Expression] BlendShape setup from avatar scan: {expressionSet.Count} expressions");
            return true;
        }

        /// <summary>
        /// ExpressionSetManager をアバターにアタッチして JSON からロード
        /// </summary>
        bool SetupBlendShapeManager(GameObject avatar, string json)
        {
            try
            {
                var manager = avatar.GetComponent<ExpressionSetManager>();
                if (manager == null)
                {
                    manager = avatar.AddComponent<ExpressionSetManager>();
                }

                manager.SetTargetAvatar(avatar);
                manager.LoadCollectionFromJson(json);

                if (manager.Collection != null && manager.Collection.SetCount > 0)
                {
                    manager.SwitchSet(0);
                }

                blendShapeExpressionManager = manager;
                expressionSetup = null;
                vrm0ExpressionController = null;

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Expression] Failed to setup BlendShape manager: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Issue #471: VRM 表情メタデータをキャッシュに保存
        /// </summary>
        void SaveExpressionDataToCache(GameObject avatar, string cacheId)
        {
            try
            {
                var vrm10 = avatar.GetComponent<UniVRM10.Vrm10Instance>();
                if (vrm10 == null) return;

                ExpressionSet expressionSet = null;
                if (AICam.VRM.VrmExpressionBridge.IsVRoidStudioAvatar(avatar))
                {
                    expressionSet = AICam.VRM.VrmExpressionBridge.GetStandardExpressionSet();
                }
                else
                {
                    expressionSet = AICam.VRM.VrmExpressionBridge.CreateExpressionSetFromVrm10(vrm10, avatar);
                }

                if (expressionSet == null || expressionSet.Count == 0) return;

                var collection = new ExpressionSetCollection
                {
                    collectionName = "VRM Expressions",
                    avatarName = avatar.name
                };
                collection.AddSet(expressionSet);

                string cacheDir = Path.Combine(Application.persistentDataPath, "AvatarCache", cacheId);
                string jsonPath = Path.Combine(cacheDir, "expressions.json");
                ExpressionSetSerializer.SaveCollection(collection, jsonPath);

                Debug.Log($"[Expression] Saved expression data to cache: {jsonPath} ({expressionSet.Count} expressions)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Expression] Failed to save expression data: {e.Message}");
            }
        }
#endif

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
        /// Issue #467: 表情アイコン生成をトリガー（Fire-and-forget）
        /// SetupExpressionSystem の後に呼び出す
        /// </summary>
        void TriggerExpressionIconGeneration(GameObject avatar, int slotIndex)
        {
            if (avatar == null || slotIndex < 0) return;

            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            if (slotManager?.Cache == null) return;

            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            string avatarName = avatarSlotData?.avatarName ?? avatar.name;

            // 既にアイコンがある場合はスキップ
            if (avatarSlotData != null && avatarSlotData.HasExpressionIcons) return;

            Debug.Log($"🎨 TriggerExpressionIconGeneration: Starting for slot {slotIndex}, avatar={avatarName}");

            AICam.VRM.ExpressionIconService.Instance.GenerateForSlot(
                avatar,
                slotIndex,
                avatarName,
                onComplete: (folderPath) =>
                {
                    Debug.Log($"🎨 Expression icons generated for slot {slotIndex}: {folderPath}");

                    // AvatarSlotData を更新・永続化
                    var mgr = AICam.FBXLoader.AvatarSlotManager.Instance;
                    if (mgr?.Cache != null)
                    {
                        var slot = mgr.Cache.GetSlot(slotIndex);
                        if (slot != null)
                        {
                            slot.expressionIconFolderPath = folderPath;
                            mgr.Cache.UpdateSlot(slotIndex, slot);
                            mgr.Cache.SaveToFile();
                            Debug.Log($"🎨 Persisted expressionIconFolderPath for slot {slotIndex}");
                        }
                    }
                },
                onError: (error) =>
                {
                    Debug.LogWarning($"🎨 Expression icon generation failed for slot {slotIndex}: {error}");
                }
            );
        }

        /// <summary>
        /// Issue #407: 情報アラートを表示（水色）
        /// </summary>
        public void ShowInfo(string code, string message, float autoDismissSeconds = 3f)
            => alertService?.ShowInfo(code, message, autoDismissSeconds);

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
        /// Issue #450: Lighting Panel の Mood タブを表示
        /// </summary>
        void ShowLightingMood()
        {
            tabMood?.AddToClassList("is-selected");
            tabDirection?.RemoveFromClassList("is-selected");
            lightingPanelMood?.AddToClassList("is-active");
            lightingPanelDirection?.RemoveFromClassList("is-active");
            if (enableDebugLogging) Debug.Log("🔄 Switched to Mood tab");
        }

        /// <summary>
        /// Issue #450: Lighting Panel の Direction タブを表示
        /// </summary>
        void ShowLightingDirection()
        {
            tabDirection?.AddToClassList("is-selected");
            tabMood?.RemoveFromClassList("is-selected");
            lightingPanelDirection?.AddToClassList("is-active");
            lightingPanelMood?.RemoveFromClassList("is-active");
            if (enableDebugLogging) Debug.Log("🔄 Switched to Direction tab");
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

        #region AlertBar Methods

        /// <summary>
        /// 警告アラートを表示（フェードイン）
        /// </summary>
        /// <param name="code">警告コード（例: W001）</param>
        /// <param name="message">警告メッセージ</param>
        /// <param name="autoDismissSeconds">自動非表示までの秒数（0の場合は自動非表示しない）</param>
        public void ShowWarning(string code, string message, float autoDismissSeconds = 5f)
            => alertService?.ShowWarning(code, message, autoDismissSeconds);

        public void ShowError(string code, string message, float autoDismissSeconds = 0f)
            => alertService?.ShowError(code, message, autoDismissSeconds);

        public void HideAlert()
            => alertService?.HideAlert();

        #endregion

        #region Issue #73: Circular Progress Methods

        private void StartSlotLoading(Button slotButton)
            => slotProgressService?.StartSlotLoading(slotButton);

        private void UpdateSlotProgress(Button slotButton, float progress01)
            => slotProgressService?.UpdateSlotProgress(slotButton, progress01);

        private void CompleteSlotLoading(Button slotButton)
            => slotProgressService?.CompleteSlotLoading(slotButton);

        private void CancelSlotLoading(Button slotButton)
            => slotProgressService?.CancelSlotLoading(slotButton);

        #endregion

        #region IconPreviewPanel Methods (Phase 02: delegated to IconPreviewController)

        public void ShowIconPreview(Texture2D texture, System.Action onConfirm, System.Action onRetake = null)
            => iconPreviewController?.Show(texture, onConfirm, onRetake);

        public void HideIconPreview()
            => iconPreviewController?.Hide();

        public bool IsIconPreviewShowing => iconPreviewController != null && iconPreviewController.IsShowing;

        #endregion

    }
}