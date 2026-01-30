using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using AICam.Core;
using DSGarage.PoseSlot;

namespace AICam.UI
{
    /// <summary>
    /// UIToolkit版のカメラ撮影コントローラー
    /// タップで写真撮影、長押しで動画撮影を行う
    ///
    /// ## v0.8.0 変更履歴
    /// - Issue #476: パネルクローズ後の入力ブロック機能を追加
    ///   - panelClosedTime でクローズ時刻を記録
    ///   - PANEL_CLOSE_COOLDOWN (0.2秒) 間はタッチをブロック
    ///   - NotifyPanelClosed() を各コントローラーから呼び出し
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [RequireComponent(typeof(UIToolkitInputBlocker))]
    public class CameraCaptureController : MonoBehaviour, IUIBlockingProvider, ILightingSettingsProvider, ISlotPersistenceHost
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

        // Phase 05: キャッシュ・永続化
        private BinaryCacheService binaryCacheService;
        private AvatarCacheSyncService avatarCacheSyncService;
        private SlotPersistenceController slotPersistenceController;

        // Phase 06: スロットUI・ロード
        private AvatarSlotUIController avatarSlotUIController;
        private SlotDeleteController slotDeleteController;
        private AvatarLoadOrchestrator avatarLoadOrchestrator;

        // パネル要素（IsPointOverUIPanel用）
        private VisualElement topPanel;
        private VisualElement bottomPanel;

        // サイドパネル要素（sideButton1/3 は ARFeatureController が管理）
        private VisualElement sidePanel;
        private Button sideButton2;
        private Button sideButtonBugReport; // Issue #413: バグレポート

        // Issue #451: 撮影設定バー（topButton1-4用、貫通防止）
        private VisualElement captureSettingBar;

        // Issue #476: パネルクローズ後の入力ブロック
        private float panelClosedTime = -1f;
        private const float PANEL_CLOSE_COOLDOWN = 0.2f;  // パネル閉鎖後のクールダウン（秒）

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

            InitializeSubControllers();
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
            InitializeSubControllers();
        }

        /// <summary>
        /// サブコントローラーの初期化（OnEnableまたは遅延初期化から呼ばれる）
        /// </summary>
        private void InitializeSubControllers()
        {
            if (enableDebugLogging) Debug.Log($"✅ Root element found: {root.name}");

            // Phase 00: SerializeField の自動取得（null の場合）
            if (expressionSetup == null)
            {
                expressionSetup = GetComponent<AICam.Expression.VrmExpressionSetup>();
                if (enableDebugLogging) Debug.Log($"🔧 Auto-resolved expressionSetup: {(expressionSetup != null ? "✅" : "❌")}");
            }
            if (poseSlotController == null)
            {
                poseSlotController = FindAnyObjectByType<PoseSlotController>();
                if (enableDebugLogging) Debug.Log($"🔧 Auto-resolved poseSlotController: {(poseSlotController != null ? "✅" : "❌")}");
            }

            // Phase 01: サービス初期化
            alertService = new AlertService(root);
            slotProgressService = new SlotProgressService(root);
            new VersionInfoService(root);

            // Phase 02: コントローラー初期化
            iconPreviewController = new IconPreviewController(root, () => NotifyPanelClosed());  // Issue #476
            mediaViewerController = new MediaViewerController(root, () => NotifyPanelClosed());  // Issue #476

            // Phase 03: コントローラー初期化
            captureController = new CaptureController(root, photoController,
                (photo, isVideo) => mediaViewerController?.OpenViewer(photo, isVideo));
            settingsPanelUIController = new SettingsPanelUIController(root, enableDebugLogging,
                (code, msg) => ShowWarning(code, msg),
                (code, msg) => ShowError(code, msg),
                () => NotifyPanelClosed());  // Issue #476
            arFeatureController = new ARFeatureController(root, enableDebugLogging,
                (code, msg) => ShowWarning(code, msg));

            // Phase 04: 表情・ポーズコントローラー初期化
            expressionUIController = new ExpressionUIController(root, expressionSetup, enableDebugLogging);
            poseUIController = new PoseUIController(root, poseOverrideControllers, poseSlotController,
                fbxLoaderBridge, enableDebugLogging,
                (code, msg, duration) => ShowInfo(code, msg, duration));

            // Phase 05: キャッシュ・永続化サービス初期化
            binaryCacheService = new BinaryCacheService(
                expressionUIController, fbxLoaderBridge,
                (code, msg, duration) => ShowInfo(code, msg, duration),
                (code, msg, duration) => ShowWarning(code, msg, duration),
                (code, msg) => ShowError(code, msg),
                (slotIndex, avatarSlotData) => avatarSlotUIController?.ShowExportPopupDirect(slotIndex, avatarSlotData));
            avatarCacheSyncService = new AvatarCacheSyncService(binaryCacheService, fbxLoaderBridge);
            slotPersistenceController = new SlotPersistenceController(
                this, slotProgressService, poseUIController, expressionUIController, enableDebugLogging);

            // Phase 06: スロットUI・ロード初期化
            avatarSlotUIController = new AvatarSlotUIController(root,
                slotPersistenceController, binaryCacheService, poseUIController, expressionUIController,
                avatar => PlaceAvatarAheadOfCamera(avatar),
                (code, msg, duration) => ShowInfo(code, msg, duration),
                enableDebugLogging);
            slotDeleteController = new SlotDeleteController(root, avatarSlotUIController,
                slotPersistenceController,
                (code, msg, duration) => ShowInfo(code, msg, duration),
                enableDebugLogging);
            avatarLoadOrchestrator = new AvatarLoadOrchestrator(
                avatarLoader, fbxLoaderBridge, avatarCacheSyncService,
                slotProgressService, poseUIController, expressionUIController,
                avatarSlotUIController,
                () => FindAvatarPlacer(),
                avatar => PlaceAvatarAheadOfCamera(avatar),
                () => ReapplyLightingSettings(),
                obj => Destroy(obj),
                enableDebugLogging);
            avatarSlotUIController.SetLoadOrchestrator(avatarLoadOrchestrator);
            avatarSlotUIController.SetDeleteController(slotDeleteController);

            // パネル要素の取得（IsPointOverUIPanel用）
            topPanel = root.Q<VisualElement>("topPanel");
            bottomPanel = root.Q<VisualElement>("bottomPanel");

            // サイドパネル要素の取得（sideButton1/3 は ARFeatureController が自己取得）
            sidePanel = root.Q<VisualElement>("sidePanel");
            sideButton2 = root.Q<Button>("sideButton2");
            sideButtonBugReport = root.Q<Button>("sideButtonBugReport"); // Issue #413

            // Issue #451: 撮影設定バー（貫通防止用）
            captureSettingBar = root.Q<VisualElement>("captureSettingBar");

            // Issue #413: バグレポートボタンのイベント登録
            if (sideButtonBugReport != null)
            {
                sideButtonBugReport.RegisterCallback<ClickEvent>(evt => OnBugReportButtonClicked());
                if (enableDebugLogging) Debug.Log("✅ Bug report button events registered");

                var bugReportIcon = Resources.Load<Texture2D>("Sprite/PictIcon/SideBear/04_BugReport");
                if (bugReportIcon != null)
                {
                    sideButtonBugReport.style.backgroundImage = new StyleBackground(bugReportIcon);
                }
            }

            // Issue #416: 永続化されたスロットデータを読み込み（非同期でキャッシュ準備を待つ）
            if (enableDebugLogging) Debug.Log("🔧 Loading persisted slot data...");
            slotPersistenceController?.LoadPersistedSlotDataAsync().Forget();

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

            // Phase 06: スロット長押し検出
            avatarSlotUIController?.Tick(Time.deltaTime);
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
        /// Check if screen position is over UI Toolkit panel (top, side, or bottom)
        /// Issue #71: Unity Screen座標とUIToolkit座標の変換
        /// - Unity Screen: Y=0が画面下部、上に向かって増加
        /// - UIToolkit worldBound: Y=0が画面上部、下に向かって増加
        /// - PanelSettingsのScaleWithScreenSizeを考慮
        /// </summary>
        public bool IsPointOverUIPanel(Vector2 screenPosition)
        {
            if (root == null) return false;

            // Issue #476: パネルクローズ直後はタッチをブロック
            if (panelClosedTime > 0 && Time.time - panelClosedTime < PANEL_CLOSE_COOLDOWN)
            {
                if (enableDebugLogging)
                    Debug.Log($"[#476] Touch blocked - panel close cooldown ({Time.time - panelClosedTime:F2}s < {PANEL_CLOSE_COOLDOWN}s)");
                return true;
            }

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
        /// Issue #476: パネルが閉じられたことを通知
        /// クールダウン期間中はタッチをブロックする
        /// </summary>
        public void NotifyPanelClosed()
        {
            panelClosedTime = Time.time;
            if (enableDebugLogging)
                Debug.Log($"[#476] Panel closed - cooldown started");
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

        #region IconPreviewPanel Methods (Phase 02: delegated to IconPreviewController)

        public void ShowIconPreview(Texture2D texture, System.Action onConfirm, System.Action onRetake = null)
            => iconPreviewController?.Show(texture, onConfirm, onRetake);

        public void HideIconPreview()
            => iconPreviewController?.Hide();

        public bool IsIconPreviewShowing => iconPreviewController != null && iconPreviewController.IsShowing;

        #endregion

        #region ISlotPersistenceHost (Phase 05, delegates to Phase 06 AvatarSlotUIController)

        VisualElement ISlotPersistenceHost.BottomButtonContainer => avatarSlotUIController?.BottomButtonContainer;
        Button ISlotPersistenceHost.BottomButtonAdd => avatarSlotUIController?.BottomButtonAdd;
        int ISlotPersistenceHost.BottomButtonCount
        {
            get => avatarSlotUIController?.BottomButtonCount ?? 0;
            set { if (avatarSlotUIController != null) avatarSlotUIController.BottomButtonCount = value; }
        }

        SlotData ISlotPersistenceHost.EnsureSlotData(Button button)
            => avatarSlotUIController?.EnsureSlotData(button);

        SlotData ISlotPersistenceHost.GetSlotData(Button button)
            => avatarSlotUIController?.GetSlotData(button);

        void ISlotPersistenceHost.AddBottomPanelButtonForSlot(int slotIndex)
            => avatarSlotUIController?.AddBottomPanelButtonForSlot(slotIndex);

        int ISlotPersistenceHost.GetSlotIndexFromButton(Button button)
            => avatarSlotUIController?.GetSlotIndexFromButton(button) ?? -1;

        void ISlotPersistenceHost.PlaceAvatarAheadOfCamera(GameObject avatar)
            => PlaceAvatarAheadOfCamera(avatar);

        void ISlotPersistenceHost.UpdateSlotSelection(Button button)
            => avatarSlotUIController?.UpdateSlotSelection(button);

        void ISlotPersistenceHost.UpdateButtonIcon(Button button, Texture2D texture)
            => avatarSlotUIController?.UpdateButtonIcon(button, texture);

        string ISlotPersistenceHost.SaveThumbnailToFile(Button button, Texture2D texture)
            => avatarSlotUIController?.SaveThumbnailToFile(button, texture);

        #endregion

    }
}