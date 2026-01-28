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

        // Phase 01: サービス
        private AlertService alertService;
        private SlotProgressService slotProgressService;

        // Phase 02: コントローラー
        private IconPreviewController iconPreviewController;
        private MediaViewerController mediaViewerController;
        private AspectRatioController aspectRatioController;

        // Phase 03: コントローラー
        private CaptureController captureController;
        private SettingsPanelUIController settingsPanelUIController;
        private ARFeatureController arFeatureController;

        // Phase 04: コントローラー
        private ExpressionUIController expressionUIController;
        private PoseUIController poseUIController;

        // パネル要素
        private VisualElement topPanel;
        private VisualElement bottomPanel;
        private VisualElement bottomButtonContainer;
        private Button bottomButtonAdd;
        private int bottomButtonCount = 1; // UXML has only bottomButton1

        // サイドパネル要素（sideButton1/3 は ARFeatureController が管理）
        private VisualElement sidePanel;
        private Button sideButton2;
        private Button sideButtonBugReport; // Issue #413: バグレポート

        // Issue #451: 撮影設定バー（topButton1-4用、貫通防止）
        private VisualElement captureSettingBar;

        // Issue #74: Light Estimation状態
        private bool isLightEstimationEnabled = true;
        private ARLightEstimationController cachedLightEstimationController;

        // Issue #75: Shadow状態
        private bool isShadowEnabled = true;
        private Light cachedMainLight;
        private ARPlaneShadowReceiver cachedPlaneShadowReceiver;

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

        // スロットデータ管理
        private Dictionary<Button, SlotData> slotDataMap = new Dictionary<Button, SlotData>();
        private Button currentSelectedSlot;

        // スロットロード中フラグ（重複ロード防止）
        private bool isSlotLoading = false;
        private Button currentLoadingSlot = null;

        // Issue #458: スロットダブルタップ検出用
        private const float DOUBLE_TAP_THRESHOLD = 0.3f;
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


        void OnEnable()
        {
            if (enableDebugLogging) Debug.Log("🔧 CameraCaptureController OnEnable called");

            // Phase 03: photoControllerのイベント登録はCaptureControllerが管理

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

            // Phase 01: サービス初期化
            alertService = new AlertService(root);
            slotProgressService = new SlotProgressService(root);
            new VersionInfoService(root);

            // Phase 02: コントローラー初期化
            iconPreviewController = new IconPreviewController(root);
            mediaViewerController = new MediaViewerController(root);

            // Phase 03: コントローラー初期化
            captureController = new CaptureController(root, photoController,
                (photo, isVideo) => mediaViewerController?.OpenViewer(photo, isVideo));
            settingsPanelUIController = new SettingsPanelUIController(root, enableDebugLogging,
                (code, msg) => ShowWarning(code, msg),
                (code, msg) => ShowError(code, msg));
            arFeatureController = new ARFeatureController(root, enableDebugLogging,
                (code, msg) => ShowWarning(code, msg));

            // Phase 04: 表情・ポーズコントローラー初期化
            expressionUIController = new ExpressionUIController(root, expressionSetup, enableDebugLogging);
            poseUIController = new PoseUIController(root, poseOverrideControllers, poseSlotController,
                fbxLoaderBridge, enableDebugLogging,
                (code, msg, duration) => ShowInfo(code, msg, duration));

            topPanel = root.Q<VisualElement>("topPanel");
            bottomPanel = root.Q<VisualElement>("bottomPanel");
            bottomButtonContainer = root.Q<VisualElement>("bottomButtonContainer");
            bottomButtonAdd = root.Q<Button>("bottomButtonAdd");

            // サイドパネル要素の取得（sideButton1/3 は ARFeatureController が自己取得）
            sidePanel = root.Q<VisualElement>("sidePanel");
            sideButton2 = root.Q<Button>("sideButton2");
            sideButtonBugReport = root.Q<Button>("sideButtonBugReport"); // Issue #413

            // Issue #451: 撮影設定バー（貫通防止用）
            captureSettingBar = root.Q<VisualElement>("captureSettingBar");

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

            // sideButton1/3 のイベントは ARFeatureController が管理
            // sideButton2 のイベントは AspectRatioController が管理

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

            // topButton1/2 は SettingsPanelUIController、topButton3/4 は Expression/PoseUIController、topButton5 は ARFeatureController が管理

            if (enableDebugLogging)
            {
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
            // Phase 03: CaptureController のイベント解除（photoController含む）
            captureController?.Dispose();

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


        void Update()
        {
            // Phase 03: 撮影ボタンの状態更新
            captureController?.Tick(Time.deltaTime);

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

        /// <summary>
        /// ARPhotoControllerを設定（外部から呼び出し可能）
        /// </summary>
        public void SetPhotoController(ARPhotoController controller)
        {
            photoController = controller;
            captureController?.SetPhotoController(controller);
            aspectRatioController?.SetPhotoController(controller);
        }

        /// <summary>
        /// 録画中かどうかを取得
        /// </summary>
        public bool IsRecording => captureController != null && captureController.IsRecording;

        /// <summary>
        /// 最後にキャプチャした写真のサムネイルを更新
        /// </summary>
        public void UpdateLastCapturedPhoto(Texture2D photo)
            => captureController?.UpdateLastCapturedPhoto(photo);

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
                poseUIController?.ApplyDefaultAOC(avatar);
                expressionUIController?.SetupExpressionSystem(avatar, slotIndex);
                expressionUIController?.TriggerExpressionIconGeneration(avatar, slotIndex);

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
                    poseUIController?.SetCachedAvatar(avatar);
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
            if (IsPointOverElement(captureController?.CaptureButton, panelPosition, "captureButton")) return true;
            if (IsPointOverElement(captureController?.GalleryThumbnail, panelPosition, "galleryThumbnail")) return true;

            // Phase 03: 設定パネル表示中は全画面ブロック
            if (settingsPanelUIController != null && settingsPanelUIController.IsSettingsVisible)
            {
                Debug.Log($"[#71] Touch over settingsPanel (visible)");
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
                poseUIController?.ApplyDefaultAOC(avatar);

                // Issue #145/#411: 表情システムをセットアップ
                expressionUIController?.SetupExpressionSystem(avatar, GetSlotIndexFromButton(targetButton));
                expressionUIController?.TriggerExpressionIconGeneration(avatar, GetSlotIndexFromButton(targetButton));

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
                poseUIController?.ApplyDefaultAOC(loadedModel);

                // Issue #145/#411: 表情システムをセットアップ
                expressionUIController?.SetupExpressionSystem(loadedModel, GetSlotIndexFromButton(targetButton));
                expressionUIController?.TriggerExpressionIconGeneration(loadedModel, GetSlotIndexFromButton(targetButton));

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
                    expressionUIController?.SaveExpressionDataToCache(avatar, cacheId);
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
                    expressionUIController?.SaveExpressionDataToCache(avatar, cacheId);
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
                    expressionUIController?.SaveExpressionDataToCache(currentModel, cacheId);
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
                poseUIController?.ApplyDefaultAOC(avatar);

                // Issue #145/#411: 表情システムをセットアップ
                expressionUIController?.SetupExpressionSystem(avatar, slotIndex);
                expressionUIController?.TriggerExpressionIconGeneration(avatar, slotIndex);

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

                // Phase 04: キャッシュを更新
                poseUIController?.SetCachedAvatar(avatar);

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

            // 選択したスロットのアバターを表示・配置
            if (slotData?.loadedAvatar != null)
            {
                // カメラ前方に配置（PlaceAvatarOnPlaneOnly の内部 avatar も更新される）
                PlaceAvatarAheadOfCamera(slotData.loadedAvatar);

                // Phase 04: ポーズ・表情キャッシュを更新
                poseUIController?.SetCachedAvatar(slotData.loadedAvatar);
                Debug.Log($"Updated cachedCurrentAvatar: {slotData.loadedAvatar.name}");
            }

            // Phase 04: 表情状態を復元（BlendshapeController リセット含む）
            expressionUIController?.OnSlotActivated(slotData?.loadedAvatar);
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
        /// Issue #407: アバタースロットロード完了時のハンドラ
        /// </summary>
        void OnAvatarSlotLoadComplete(int slotIndex, bool success)
        {
            Debug.Log($"🎭 OnAvatarSlotLoadComplete called: slotIndex={slotIndex}, success={success}");
            if (success)
            {
                poseUIController?.OnSlotLoadComplete(slotIndex);
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
                // Phase 04: ポーズ・表情のセットアップを委譲
                poseUIController?.OnLoadHandlerComplete(result.Avatar);
                expressionUIController?.SetupExpressionSystem(result.Avatar, result.SlotIndex);
                expressionUIController?.TriggerExpressionIconGeneration(result.Avatar, result.SlotIndex);

                Debug.Log($"🎭 Avatar setup complete from AvatarLoadHandler: {result.Avatar.name}");
            }
        }

        /// <summary>
        /// Issue #407: アバタースロットクリア時のハンドラ
        /// </summary>
        void OnAvatarSlotCleared(int slotIndex)
        {
            poseUIController?.OnSlotCleared(slotIndex);
        }

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
        /// public: RuntimeFBXLoaderBridge からも呼び出せるように公開
        /// </summary>
        public void ReapplyLightingSettings()
            => settingsPanelUIController?.ReapplyLightingSettings();
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
        /// Issue #407: 情報アラートを表示（水色）
        /// </summary>
        public void ShowInfo(string code, string message, float autoDismissSeconds = 3f)
            => alertService?.ShowInfo(code, message, autoDismissSeconds);

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