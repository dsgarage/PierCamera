using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバタースロットを管理するマネージャー
    /// Issue #72: AvatarOperationQueue によるキュー制御を追加
    /// </summary>
    public class AvatarSlotManager : MonoBehaviour
    {
        public static AvatarSlotManager Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private int maxSlots = 6;

        [Header("References")]
        [SerializeField] private FileBrowserController fileBrowserController;
        [SerializeField] private RuntimeFBXLoaderBridge loaderBridge;
        [SerializeField] private IconPreviewPanel iconPreviewPanel;
        [SerializeField] private List<AvatarSlot> avatarSlots = new List<AvatarSlot>();
        [SerializeField] private AvatarMemoryCache memoryCache;
        [SerializeField] private AvatarOperationQueue operationQueue;

        // スロットキャッシュ
        private AvatarSlotCache cache;
        private int currentSlotIndex = -1;
        private bool isProcessing;
        private bool isInitialized;
        private bool isInitializing;

        /// <summary>
        /// 操作キュー（外部からアクセス用）
        /// </summary>
        public AvatarOperationQueue OperationQueue => operationQueue;

        // イベント
        public event Action<int, bool> OnSlotLoadComplete;
        public event Action<int> OnSlotCleared;
        public event Action<int> OnSlotSelected;

        public int CurrentSlotIndex => currentSlotIndex;
        public int MaxSlots => maxSlots;
        public AvatarSlotCache Cache => cache;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[AvatarSlotManager] Duplicate instance detected, destroying...");
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // 非同期初期化を開始
            InitializeAsync().Forget();
        }

        /// <summary>
        /// 非同期初期化
        /// キャッシュの読み込みと依存関係の設定を非同期で行う
        /// </summary>
        private async UniTask InitializeAsync()
        {
            if (isInitialized || isInitializing) return;
            isInitializing = true;

            try
            {
                // キャッシュを非同期で読み込み
                cache = await AvatarSlotCache.LoadFromFileAsync();

                if (cache.maxSlots != maxSlots)
                {
                    cache.Initialize(maxSlots);
                    cache.SaveToFile();
                }

                Debug.Log($"[AvatarSlotManager] Cache loaded async with {cache.GetConfiguredSlotCount()} configured slots");

                // 次のフレームまで待機してUIが準備完了するのを待つ
                await UniTask.Yield();

                // スロットUIを初期化
                InitializeSlots();

                // 依存関係の検証（SerializeFieldで設定されていない場合は遅延初期化）
                ValidateDependencies();

                isInitialized = true;
                Debug.Log("[AvatarSlotManager] Async initialization complete");

                // Issue #416: 最後にアクティブだったスロットを自動復元
                await RestoreLastActiveSlotAsync();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarSlotManager] Async initialization failed: {e.Message}");
                Debug.LogException(e);

                // フォールバック: 同期読み込み
                LoadCacheFallback();
                InitializeSlots();
                ValidateDependencies();
                isInitialized = true;
            }
            finally
            {
                isInitializing = false;
            }
        }

        /// <summary>
        /// 初期化完了を待機
        /// </summary>
        public async UniTask WaitForInitializationAsync()
        {
            while (!isInitialized)
            {
                await UniTask.Yield();
            }
        }

        /// <summary>
        /// 初期化完了かどうか
        /// </summary>
        public bool IsInitialized => isInitialized;

        private void Start()
        {
            // 非同期初期化に移行したため、Startでは何もしない
            // ただし、まだ初期化が完了していない場合のフォールバック
            if (!isInitialized && !isInitializing)
            {
                Debug.LogWarning("[AvatarSlotManager] Fallback sync initialization in Start");
                LoadCacheFallback();
                InitializeSlots();
                ValidateDependencies();
                isInitialized = true;
            }
        }

        /// <summary>
        /// 依存関係を検証し、未設定の場合は遅延初期化用のフラグを設定
        /// FindFirstObjectByType は初回使用時まで遅延させる
        /// 本番ビルドでは SerializeField で設定することを推奨
        /// </summary>
        private void ValidateDependencies()
        {
            // SerializeFieldで設定されているものは即座に使用可能
            // 未設定のものは初回アクセス時に遅延初期化される

            // loaderBridge を memoryCache に設定（依存注入）
            // 両方設定されている場合のみ
            if (memoryCache != null && loaderBridge != null)
            {
                memoryCache.SetLoader(loaderBridge);
            }

            // 操作キューの設定（設定されている場合）
            SetupOperationQueue();
        }

        /// <summary>
        /// FileBrowserControllerを遅延取得
        /// </summary>
        private FileBrowserController GetFileBrowserController()
        {
            if (fileBrowserController == null)
            {
                Debug.LogWarning("[AvatarSlotManager] FileBrowserController not assigned in Inspector, using FindFirstObjectByType (lazy)");
                fileBrowserController = FindFirstObjectByType<FileBrowserController>();
            }
            return fileBrowserController;
        }

        /// <summary>
        /// RuntimeFBXLoaderBridgeを遅延取得
        /// </summary>
        private RuntimeFBXLoaderBridge GetLoaderBridge()
        {
            if (loaderBridge == null)
            {
                Debug.LogWarning("[AvatarSlotManager] RuntimeFBXLoaderBridge not assigned in Inspector, using FindFirstObjectByType (lazy)");
                loaderBridge = FindFirstObjectByType<RuntimeFBXLoaderBridge>();
            }
            return loaderBridge;
        }

        /// <summary>
        /// IconPreviewPanelを遅延取得
        /// </summary>
        private IconPreviewPanel GetIconPreviewPanel()
        {
            if (iconPreviewPanel == null)
            {
                Debug.LogWarning("[AvatarSlotManager] IconPreviewPanel not assigned in Inspector, using FindFirstObjectByType (lazy)");
                iconPreviewPanel = FindFirstObjectByType<IconPreviewPanel>();
            }
            return iconPreviewPanel;
        }

        /// <summary>
        /// AvatarMemoryCacheを遅延取得
        /// </summary>
        private AvatarMemoryCache GetMemoryCache()
        {
            if (memoryCache == null)
            {
                Debug.LogWarning("[AvatarSlotManager] AvatarMemoryCache not assigned in Inspector, using FindFirstObjectByType (lazy)");
                memoryCache = FindFirstObjectByType<AvatarMemoryCache>();
                if (memoryCache == null)
                {
                    var cacheObj = new GameObject("AvatarMemoryCache");
                    memoryCache = cacheObj.AddComponent<AvatarMemoryCache>();
                    Debug.Log("[AvatarSlotManager] Created AvatarMemoryCache");
                }

                // loaderBridge を設定
                var bridge = GetLoaderBridge();
                if (bridge != null)
                {
                    memoryCache.SetLoader(bridge);
                }
            }
            return memoryCache;
        }

        /// <summary>
        /// AvatarOperationQueueを遅延取得
        /// </summary>
        private AvatarOperationQueue GetOperationQueue()
        {
            if (operationQueue == null)
            {
                Debug.LogWarning("[AvatarSlotManager] AvatarOperationQueue not assigned in Inspector, using FindFirstObjectByType (lazy)");
                operationQueue = FindFirstObjectByType<AvatarOperationQueue>();
                if (operationQueue == null)
                {
                    var queueObj = new GameObject("AvatarOperationQueue");
                    operationQueue = queueObj.AddComponent<AvatarOperationQueue>();
                    Debug.Log("[AvatarSlotManager] Created AvatarOperationQueue");
                }
                SetupOperationQueue();
            }
            return operationQueue;
        }

        /// <summary>
        /// 操作キューの設定
        /// </summary>
        private void SetupOperationQueue()
        {
            if (operationQueue != null)
            {
                operationQueue.SetExecutor(ExecuteOperation);

                // Issue #73: プログレスイベント購読
                operationQueue.OnOperationStarted += OnQueueOperationStarted;
                operationQueue.OnProgressUpdated += OnQueueProgressUpdated;
                operationQueue.OnOperationCompleted += OnQueueOperationCompleted;
                operationQueue.OnOperationCancelled += OnQueueOperationCancelled;

                Debug.Log("[AvatarSlotManager] Operation queue executor and progress events configured");
            }
        }

        /// <summary>
        /// キャッシュを読み込み（フォールバック用同期版）
        /// </summary>
        private void LoadCacheFallback()
        {
            cache = AvatarSlotCache.LoadFromFile();

            if (cache.maxSlots != maxSlots)
            {
                cache.Initialize(maxSlots);
                cache.SaveToFile();
            }

            Debug.Log($"[AvatarSlotManager] Cache loaded (sync fallback) with {cache.GetConfiguredSlotCount()} configured slots");
        }

        /// <summary>
        /// スロットUIを初期化
        /// </summary>
        private void InitializeSlots()
        {
            for (int i = 0; i < avatarSlots.Count && i < cache.slots.Count; i++)
            {
                var slot = avatarSlots[i];
                var slotData = cache.GetSlot(i);

                slot.Initialize(i, slotData);
                slot.OnSlotClicked += OnSlotClickedHandler;
                slot.OnSlotLongPressed += OnSlotLongPressedHandler;
            }

            Debug.Log($"[AvatarSlotManager] Initialized {avatarSlots.Count} slot UIs");
        }

        /// <summary>
        /// Issue #416: 最後にアクティブだったスロットを自動復元
        /// アプリ再起動時に以前使用していたアバターを自動的にロードする
        /// </summary>
        private async UniTask RestoreLastActiveSlotAsync()
        {
            try
            {
                int slotToRestore = cache.GetSlotToRestore();

                if (slotToRestore < 0)
                {
                    Debug.Log("[AvatarSlotManager] No slot to restore on startup");
                    return;
                }

                Debug.Log($"[AvatarSlotManager] Restoring slot {slotToRestore} on startup...");

                // 少し待機してUIの準備完了を確実にする
                await UniTask.Delay(100);

                // スロットを復元
                if (operationQueue != null)
                {
                    var result = await operationQueue.EnqueueLoad(slotToRestore, AvatarOperationQueue.OperationPriority.Normal);
                    if (result.Success)
                    {
                        Debug.Log($"[AvatarSlotManager] Successfully restored slot {slotToRestore} on startup");
                    }
                    else
                    {
                        Debug.LogWarning($"[AvatarSlotManager] Failed to restore slot {slotToRestore}: {result.ErrorMessage}");
                    }
                }
                else
                {
                    // キューがない場合は直接実行
                    await LoadAvatarFromSlotInternal(slotToRestore, CancellationToken.None);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarSlotManager] Error restoring slot on startup: {e.Message}");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// スロットクリックハンドラー
        /// Issue #72: キュー経由で操作を実行
        /// </summary>
        private async void OnSlotClickedHandler(int slotIndex)
        {
            try
            {
                Debug.Log($"[AvatarSlotManager] Slot {slotIndex} clicked");

                var slotData = cache.GetSlot(slotIndex);

                if (slotData == null || !slotData.IsConfigured)
                {
                    // 未設定スロット - FilePickerを開く（キュー外で処理）
                    if (isProcessing)
                    {
                        Debug.LogWarning("[AvatarSlotManager] Already processing, ignoring file picker request");
                        return;
                    }
                    await OpenFilePickerForSlot(slotIndex);
                }
                else
                {
                    // 設定済みスロット - キュー経由でロード
                    // 既にロード中の場合、High優先度で現在の操作をキャンセルして新しいスロットをロード
                    var priority = operationQueue?.IsProcessing == true
                        ? AvatarOperationQueue.OperationPriority.High
                        : AvatarOperationQueue.OperationPriority.Normal;

                    if (operationQueue != null)
                    {
                        var result = await operationQueue.EnqueueLoad(slotIndex, priority);
                        if (!result.Success && !result.WasCancelled)
                        {
                            Debug.LogError($"[AvatarSlotManager] Load failed: {result.ErrorMessage}");
                        }
                    }
                    else
                    {
                        // キューがない場合は直接実行（フォールバック）
                        await LoadAvatarFromSlotInternal(slotIndex, CancellationToken.None);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AvatarSlotManager] Error handling slot click: {e.Message}");
                Debug.LogException(e);
                isProcessing = false;
            }
        }

        /// <summary>
        /// スロット長押しハンドラー
        /// </summary>
        private void OnSlotLongPressedHandler(int slotIndex)
        {
            Debug.Log($"[AvatarSlotManager] Slot {slotIndex} long pressed");

            var slotData = cache.GetSlot(slotIndex);

            if (slotData != null && slotData.IsConfigured)
            {
                // TODO: スロット設定メニューを表示（変更/削除）
                ShowSlotContextMenu(slotIndex);
            }
            else
            {
                // 未設定の場合はFilePickerを開く
                _ = OpenFilePickerForSlot(slotIndex);
            }
        }

        /// <summary>
        /// スロット設定メニューを表示
        /// </summary>
        private void ShowSlotContextMenu(int slotIndex)
        {
            // TODO: UIでコンテキストメニューを表示
            Debug.Log($"[AvatarSlotManager] Show context menu for slot {slotIndex}");

            // 仮実装: 長押しでスロットをクリア
            // ClearSlot(slotIndex);
        }

        /// <summary>
        /// 指定スロット用にFilePickerを開く
        /// </summary>
        public async UniTask OpenFilePickerForSlot(int slotIndex)
        {
            var browser = GetFileBrowserController();
            if (browser == null)
            {
                Debug.LogError("[AvatarSlotManager] FileBrowserController not found!");
                AlertBarController.ErrorFileNotFound("FileBrowserControllerが見つかりません");
                return;
            }

            isProcessing = true;
            Debug.Log($"[AvatarSlotManager] Opening file picker for slot {slotIndex}");

            try
            {
                var tcs = new UniTaskCompletionSource<(bool success, string path)>();
                bool callbackInvoked = false;

                browser.OpenFilePicker((success, path) =>
                {
                    Debug.Log($"[AvatarSlotManager] FilePicker callback received: success={success}, path={path}");
                    callbackInvoked = true;
                    tcs.TrySetResult((success, path));
                });

                // タイムアウト付きで待機（5分）
                // ユーザーがファイル選択をキャンセルした場合やコールバックが呼ばれなかった場合に備える
                var cts = new System.Threading.CancellationTokenSource();
                cts.CancelAfter(System.TimeSpan.FromMinutes(5));

                (bool success, string path) result;
                try
                {
                    result = await tcs.Task.AttachExternalCancellation(cts.Token);
                }
                catch (System.OperationCanceledException)
                {
                    // タイムアウト
                    Debug.LogWarning("[AvatarSlotManager] File picker timed out");
                    if (!callbackInvoked)
                    {
                        AlertBarController.WarnManifestNotFound("ファイル選択がタイムアウトしました");
                    }
                    return;
                }
                finally
                {
                    cts.Dispose();
                }

                if (result.success && !string.IsNullOrEmpty(result.path))
                {
                    Debug.Log($"[AvatarSlotManager] File selected: {result.path}");
                    await RegisterAvatarToSlot(slotIndex, result.path);
                }
                else
                {
                    Debug.Log("[AvatarSlotManager] File selection cancelled");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AvatarSlotManager] Error in file picker: {e.Message}");
                Debug.LogException(e);
            }
            finally
            {
                isProcessing = false;
            }
        }

        /// <summary>
        /// アバターをスロットに登録
        /// </summary>
        public async UniTask RegisterAvatarToSlot(int slotIndex, string filePath)
        {
            if (slotIndex < 0 || slotIndex >= maxSlots)
            {
                Debug.LogError($"[AvatarSlotManager] Invalid slot index: {slotIndex}");
                return;
            }

            isProcessing = true;
            Debug.Log($"[AvatarSlotManager] Registering avatar to slot {slotIndex}: {filePath}");

            try
            {
                // === 現在アクティブなアバターの位置を永続化 ===
                SaveCurrentAvatarPosition();

                // ファイルタイプを判定
                AvatarFileType fileType = AvatarSlotData.DetectFileType(filePath);

                if (fileType == AvatarFileType.Unknown)
                {
                    Debug.LogError($"[AvatarSlotManager] Unknown file type: {filePath}");
                    AlertBarController.ErrorFileFormatInvalid(Path.GetExtension(filePath));
                    return;
                }

                // マニフェストをチェック/生成
                string manifestPath = await EnsureManifestExists(filePath, fileType);

                if (string.IsNullOrEmpty(manifestPath))
                {
                    Debug.LogError("[AvatarSlotManager] Failed to create/load manifest");
                    AlertBarController.WarnManifestNotFound("マニフェストの生成に失敗しました。unitypackageからやり直してください。");
                    return;
                }

                // マニフェストを読み込んで検証
                var manifest = AvatarManifest.LoadFromFile(manifestPath);
                if (manifest == null || manifest.IsEmpty())
                {
                    Debug.LogError("[AvatarSlotManager] Manifest is empty or invalid");
                    AlertBarController.WarnManifestNotFound("マニフェストが空です。unitypackageからやり直してください。");
                    return;
                }

                // スロットデータを作成
                var slotData = new AvatarSlotData(slotIndex)
                {
                    avatarName = manifest.avatarName,
                    modelFilePath = filePath,
                    manifestFilePath = manifestPath,
                    fileType = fileType,
                    vrmVersion = manifest.vrmVersion,
                    isValid = true
                };

                // アバターを読み込み
                bool loadSuccess = await LoadAvatarAndCaptureIcon(slotData);

                if (loadSuccess)
                {
                    // キャッシュを更新
                    cache.UpdateSlot(slotIndex, slotData);
                    cache.SaveToFile();

                    // UIを更新
                    UpdateSlotUI(slotIndex);

                    // 選択状態を更新
                    SelectSlot(slotIndex);

                    OnSlotLoadComplete?.Invoke(slotIndex, true);
                    Debug.Log($"[AvatarSlotManager] Successfully registered avatar to slot {slotIndex}");
                }
                else
                {
                    OnSlotLoadComplete?.Invoke(slotIndex, false);
                    Debug.LogError($"[AvatarSlotManager] Failed to load avatar for slot {slotIndex}");
                }
            }
            finally
            {
                isProcessing = false;
            }
        }

        /// <summary>
        /// マニフェストファイルの存在を確認し、なければ生成
        /// </summary>
        private async UniTask<string> EnsureManifestExists(string modelFilePath, AvatarFileType fileType)
        {
            string manifestPath = AvatarManifest.GetManifestPath(modelFilePath);

            // 既存のマニフェストがあれば使用
            if (File.Exists(manifestPath))
            {
                Debug.Log($"[AvatarSlotManager] Found existing manifest: {manifestPath}");
                return manifestPath;
            }

            Debug.Log($"[AvatarSlotManager] Manifest not found, generating...");

            // マニフェストを生成するにはモデルを一時的に読み込む必要がある
            // RuntimeFBXLoaderBridgeでの読み込み後にマニフェストを生成する
            // ここでは仮のマニフェストパスを返す

            // 基本的なマニフェストを作成
            var manifest = new AvatarManifest
            {
                avatarName = Path.GetFileNameWithoutExtension(modelFilePath),
                modelFileName = Path.GetFileName(modelFilePath),
                fileType = fileType.ToString()
            };

            // VRMの場合はバージョンを検出
            if (fileType == AvatarFileType.VRM)
            {
                manifest.vrmVersion = await DetectVrmVersionAsync(modelFilePath);
            }

            // 一時的なマニフェストを保存
            // 実際のHumanoid情報は読み込み後に更新される
            manifest.SaveToFile(manifestPath);

            return manifestPath;
        }

        /// <summary>
        /// VRMバージョンを非同期で検出
        /// </summary>
        private async UniTask<string> DetectVrmVersionAsync(string vrmFilePath)
        {
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(vrmFilePath);

                if (bytes.Length < 20) return "Unknown";

                int chunkLength = BitConverter.ToInt32(bytes, 12);
                if (bytes.Length < 20 + chunkLength) return "Unknown";

                string json = System.Text.Encoding.UTF8.GetString(bytes, 20, chunkLength);

                if (json.Contains("\"VRMC_vrm\"")) return "VRM 1.0";
                if (json.Contains("\"VRM\"")) return "VRM 0.x";

                return "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        /// <summary>
        /// アバターを読み込んでアイコンを撮影
        /// </summary>
        private async UniTask<bool> LoadAvatarAndCaptureIcon(AvatarSlotData slotData)
        {
            var bridge = GetLoaderBridge();
            if (bridge == null)
            {
                Debug.LogError("[AvatarSlotManager] RuntimeFBXLoaderBridge not found!");
                return false;
            }

            var tcs = new UniTaskCompletionSource<bool>();

            // アイコン保存先
            string iconPath = AvatarSlotCache.GetIconPath(slotData.slotIndex);

            // 読み込み（パス直接指定・アイコン撮影付き）
            bridge.StartRuntimeLoadFromPath(
                slotData.modelFilePath,
                slotData.slotIndex,
                iconPath,
                (progress) =>
                {
                    Debug.Log($"[AvatarSlotManager] Loading progress: {progress}%");
                },
                async (success) =>
                {
                    if (success)
                    {
                        // アイコンパスを設定
                        slotData.iconFilePath = iconPath;
                        slotData.UpdateLastLoadedAt();

                        // マニフェストを更新（Humanoid情報など）
                        await UpdateManifestAfterLoad(slotData, bridge.CurrentModel);

                        // プレビュー表示（アイコンが保存されている場合）
                        var preview = GetIconPreviewPanel();
                        if (preview != null && System.IO.File.Exists(iconPath))
                        {
                            await ShowIconPreview(iconPath, slotData.slotIndex);
                        }

                        // メモリキャッシュに追加（アクティブのまま維持）
                        var cache = GetMemoryCache();
                        if (cache != null && bridge.CurrentModel != null)
                        {
                            cache.CacheAvatar(slotData.slotIndex, slotData.modelFilePath, bridge.CurrentModel, keepActive: true);
                            Debug.Log($"[AvatarSlotManager] Cached new avatar in memory for slot {slotData.slotIndex}");
                        }
                    }

                    tcs.TrySetResult(success);
                }
            );

            return await tcs.Task;
        }

        /// <summary>
        /// アイコンプレビューを表示
        /// </summary>
        private async UniTask ShowIconPreview(string iconPath, int slotIndex)
        {
            var preview = GetIconPreviewPanel();
            if (preview == null)
            {
                Debug.LogWarning("[AvatarSlotManager] IconPreviewPanel not found, skipping preview");
                return;
            }

            var previewTcs = new UniTaskCompletionSource<bool>();
            bool needsRetake = false;

            await preview.ShowPreviewFromFile(
                iconPath,
                onConfirmCallback: () =>
                {
                    Debug.Log($"[AvatarSlotManager] Icon preview confirmed for slot {slotIndex}");
                    previewTcs.TrySetResult(true);
                },
                onRetakeCallback: () =>
                {
                    Debug.Log($"[AvatarSlotManager] Icon retake requested for slot {slotIndex}");
                    needsRetake = true;
                    previewTcs.TrySetResult(false);
                }
            );

            await previewTcs.Task;

            // 再撮影が要求された場合
            if (needsRetake)
            {
                await RetakeIconAndShowPreview(iconPath, slotIndex);
            }
        }

        /// <summary>
        /// Issue #68: アイコンを再撮影してプレビュー表示
        /// </summary>
        private async UniTask RetakeIconAndShowPreview(string iconPath, int slotIndex)
        {
            // 現在ロード中のアバターを取得
            GameObject currentAvatar = null;

            // loaderBridge から現在のモデルを取得
            if (loaderBridge != null && loaderBridge.CurrentModel != null)
            {
                currentAvatar = loaderBridge.CurrentModel;
            }
            else
            {
                // メモリキャッシュから取得を試みる
                var memCache = GetMemoryCache();
                if (memCache != null)
                {
                    currentAvatar = memCache.GetCachedAvatar(slotIndex);
                }
            }

            if (currentAvatar == null)
            {
                Debug.LogWarning($"[AvatarSlotManager] Cannot retake icon - no avatar found for slot {slotIndex}");
                return;
            }

            Debug.Log($"[AvatarSlotManager] Retaking icon for avatar: {currentAvatar.name}");

            // アイコンを再撮影
            var iconCapture = AvatarIconCapture.Instance;
            Texture2D newIcon = await iconCapture.CaptureAsTextureAsync(currentAvatar);

            if (newIcon == null)
            {
                Debug.LogError("[AvatarSlotManager] Failed to retake icon");
                return;
            }

            // アイコンをファイルに保存
            try
            {
                byte[] pngData = newIcon.EncodeToPNG();
                if (pngData != null && pngData.Length > 0)
                {
                    System.IO.File.WriteAllBytes(iconPath, pngData);
                    Debug.Log($"[AvatarSlotManager] Retaken icon saved to: {iconPath}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AvatarSlotManager] Failed to save retaken icon: {e.Message}");
                UnityEngine.Object.Destroy(newIcon);
                return;
            }

            // キャッシュを更新
            cache.SaveToFile();

            // 新しいアイコンでプレビューを再表示
            await ShowIconPreview(iconPath, slotIndex);

            // 一時テクスチャを解放
            UnityEngine.Object.Destroy(newIcon);
        }

        /// <summary>
        /// 読み込み後にマニフェストを更新
        /// </summary>
        private async UniTask UpdateManifestAfterLoad(AvatarSlotData slotData, GameObject loadedModel)
        {
            if (string.IsNullOrEmpty(slotData.manifestFilePath)) return;
            if (loadedModel == null) return;

            var manifest = AvatarManifest.LoadFromFile(slotData.manifestFilePath);
            if (manifest == null)
            {
                // マニフェストがない場合は新規作成
                if (slotData.fileType == AvatarFileType.VRM)
                {
                    string vrmVersion = loaderBridge?.LoadedVrmVersion.ToString() ?? "Unknown";
                    manifest = AvatarManifest.CreateFromVRM(slotData.modelFilePath, loadedModel, vrmVersion);
                }
                else if (slotData.fileType == AvatarFileType.FBX)
                {
                    manifest = AvatarManifest.CreateFromFBX(slotData.modelFilePath, loadedModel);
                }
            }

            if (manifest != null)
            {
                // Humanoid情報を更新
                var animator = loadedModel.GetComponent<Animator>();
                if (animator != null && animator.avatar != null)
                {
                    manifest.humanoidBones = new AvatarManifest.HumanoidBoneInfo
                    {
                        isValid = animator.avatar.isValid && animator.avatar.isHuman,
                        boneCount = 0
                    };

                    // ボーン数をカウント
                    var humanBones = (HumanBodyBones[])Enum.GetValues(typeof(HumanBodyBones));
                    foreach (var bone in humanBones)
                    {
                        if (bone == HumanBodyBones.LastBone) continue;
                        if (animator.GetBoneTransform(bone) != null)
                        {
                            manifest.humanoidBones.boneCount++;
                            manifest.humanoidBones.mappedBones.Add(bone.ToString());
                        }
                    }
                }

                manifest.SaveToFile(slotData.manifestFilePath);
                Debug.Log($"[AvatarSlotManager] Updated manifest: {slotData.manifestFilePath}");
            }

            await UniTask.CompletedTask;
        }

        /// <summary>
        /// スロットからアバターを読み込む（公開API）
        /// Issue #72: キュー経由で実行
        /// </summary>
        public async UniTask LoadAvatarFromSlot(int slotIndex)
        {
            if (operationQueue != null)
            {
                await operationQueue.EnqueueLoad(slotIndex);
            }
            else
            {
                await LoadAvatarFromSlotInternal(slotIndex, CancellationToken.None);
            }
        }

        /// <summary>
        /// Issue #72: 操作キューから呼び出される実行デリゲート
        /// </summary>
        private async UniTask<AvatarOperationQueue.OperationResult> ExecuteOperation(AvatarOperationQueue.Operation operation)
        {
            Debug.Log($"[AvatarSlotManager] ExecuteOperation: {operation}");

            try
            {
                switch (operation.Type)
                {
                    case AvatarOperationQueue.OperationType.Load:
                        return await LoadAvatarFromSlotInternal(operation.SlotIndex, operation.CancellationToken);

                    case AvatarOperationQueue.OperationType.Respawn:
                        return await RespawnAvatarInternal(operation.SlotIndex, operation.CancellationToken);

                    case AvatarOperationQueue.OperationType.Unload:
                        return await UnloadAvatarInternal(operation.SlotIndex, operation.CancellationToken);

                    case AvatarOperationQueue.OperationType.Clear:
                        return await ClearAllAvatarsInternal(operation.CancellationToken);

                    default:
                        return AvatarOperationQueue.OperationResult.Failed(operation.SlotIndex, $"Unknown operation type: {operation.Type}");
                }
            }
            catch (OperationCanceledException)
            {
                Debug.Log($"[AvatarSlotManager] Operation cancelled: {operation}");
                return AvatarOperationQueue.OperationResult.Cancelled(operation.SlotIndex);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarSlotManager] Operation failed: {e.Message}");
                return AvatarOperationQueue.OperationResult.Failed(operation.SlotIndex, e.Message);
            }
        }

        /// <summary>
        /// スロットからアバターを読み込む（内部実装）
        /// Issue #72: CancellationToken対応
        /// Issue #346: バックグラウンド復帰時のキャッシュ整合性チェック
        /// </summary>
        private async UniTask<AvatarOperationQueue.OperationResult> LoadAvatarFromSlotInternal(int slotIndex, CancellationToken cancellationToken)
        {
            var slotData = cache.GetSlot(slotIndex);

            if (slotData == null || !slotData.IsConfigured)
            {
                Debug.LogWarning($"[AvatarSlotManager] Slot {slotIndex} is not configured");
                return AvatarOperationQueue.OperationResult.Failed(slotIndex, "Slot is not configured");
            }

            if (!slotData.ModelFileExists)
            {
                Debug.LogError($"[AvatarSlotManager] Model file not found: {slotData.modelFilePath}");
                AlertBarController.ErrorFileNotFound(slotData.modelFilePath);

                // スロットを無効化
                slotData.isValid = false;
                cache.SaveToFile();
                UpdateSlotUI(slotIndex);
                return AvatarOperationQueue.OperationResult.Failed(slotIndex, "Model file not found");
            }

            var memoryCacheRef = GetMemoryCache();
            if (memoryCacheRef == null)
            {
                Debug.LogError("[AvatarSlotManager] AvatarMemoryCache not found!");
                return AvatarOperationQueue.OperationResult.Failed(slotIndex, "Memory cache not found");
            }

            // Issue #346: バックグラウンド復帰後のキャッシュ整合性チェック
            // キャッシュに存在するが無効（GameObjectが破棄済み）の場合は事前にクリア
            if (memoryCacheRef.HasCachedAvatar(slotIndex) && !memoryCacheRef.IsCacheValid(slotIndex))
            {
                Debug.LogWarning($"[AvatarSlotManager] Slot {slotIndex} has invalid cache (GameObject destroyed), forcing reload");
                memoryCacheRef.RemoveFromCache(slotIndex);
            }

            isProcessing = true;

            try
            {
                Debug.Log($"[AvatarSlotManager] LoadAvatarFromSlotInternal: slot {slotIndex}");

                // キャンセルチェック
                cancellationToken.ThrowIfCancellationRequested();

                // 現在のスロットデータを取得（位置保存用）
                var currentSlotData = currentSlotIndex >= 0 ? cache.GetSlot(currentSlotIndex) : null;

                // SwitchToSlotAsync で一元管理
                // TODO: SwitchToSlotAsync に CancellationToken を渡す
                var opQueue = GetOperationQueue();
                var result = await memoryCacheRef.SwitchToSlotAsync(
                    slotIndex,
                    slotData,
                    currentSlotData,
                    progress =>
                    {
                        opQueue?.ReportProgress(progress);
                        Debug.Log($"[AvatarSlotManager] Loading progress: {progress}%");
                    }
                );

                // キャンセルチェック
                cancellationToken.ThrowIfCancellationRequested();

                if (result.Success)
                {
                    // loaderBridge の現在モデルを更新
                    var bridge = GetLoaderBridge();
                    if (bridge != null && result.Avatar != null)
                    {
                        bridge.SetCurrentModel(result.Avatar, slotIndex);
                    }

                    slotData.UpdateLastLoadedAt();
                    cache.SaveToFile();

                    SelectSlot(slotIndex);

                    Debug.Log($"[AvatarSlotManager] Switch complete: slot {slotIndex}, cacheHit={result.WasCacheHit}");
                    OnSlotLoadComplete?.Invoke(slotIndex, true);

                    return AvatarOperationQueue.OperationResult.Succeeded(slotIndex, result.Avatar);
                }
                else
                {
                    Debug.LogError($"[AvatarSlotManager] Switch failed: {result.ErrorMessage}");
                    AlertBarController.ErrorVrmLoadFailed(result.ErrorMessage);
                    OnSlotLoadComplete?.Invoke(slotIndex, false);

                    return AvatarOperationQueue.OperationResult.Failed(slotIndex, result.ErrorMessage);
                }
            }
            finally
            {
                isProcessing = false;
            }
        }

        /// <summary>
        /// Issue #72: リスポーン操作（内部実装）
        /// </summary>
        private async UniTask<AvatarOperationQueue.OperationResult> RespawnAvatarInternal(int slotIndex, CancellationToken cancellationToken)
        {
            // TODO: リスポーン実装
            Debug.Log($"[AvatarSlotManager] RespawnAvatarInternal: slot {slotIndex}");
            await UniTask.Yield(cancellationToken);
            return AvatarOperationQueue.OperationResult.Succeeded(slotIndex);
        }

        /// <summary>
        /// Issue #72: アンロード操作（内部実装）
        /// </summary>
        private async UniTask<AvatarOperationQueue.OperationResult> UnloadAvatarInternal(int slotIndex, CancellationToken cancellationToken)
        {
            Debug.Log($"[AvatarSlotManager] UnloadAvatarInternal: slot {slotIndex}");

            var cacheRef = GetMemoryCache();
            if (cacheRef != null)
            {
                cacheRef.RemoveFromCache(slotIndex);
            }

            await UniTask.Yield(cancellationToken);
            return AvatarOperationQueue.OperationResult.Succeeded(slotIndex);
        }

        /// <summary>
        /// Issue #72: 全クリア操作（内部実装）
        /// </summary>
        private async UniTask<AvatarOperationQueue.OperationResult> ClearAllAvatarsInternal(CancellationToken cancellationToken)
        {
            Debug.Log("[AvatarSlotManager] ClearAllAvatarsInternal");

            var cacheRef = GetMemoryCache();
            if (cacheRef != null)
            {
                cacheRef.ClearAll();
            }

            currentSlotIndex = -1;

            await UniTask.Yield(cancellationToken);
            return AvatarOperationQueue.OperationResult.Succeeded(-1);
        }

        /// <summary>
        /// 現在アクティブなアバターの位置を保存して非アクティブ化
        /// メモリキャッシュと永続キャッシュの両方に保存
        /// </summary>
        private void SaveCurrentAvatarPosition()
        {
            if (currentSlotIndex < 0) return;

            var currentSlotData = cache.GetSlot(currentSlotIndex);
            if (currentSlotData == null || !currentSlotData.IsConfigured) return;

            // loaderBridgeから現在のモデルを取得
            var bridge = GetLoaderBridge();
            if (bridge != null && bridge.CurrentModel != null)
            {
                if (bridge.CurrentModel.activeInHierarchy)
                {
                    Debug.Log($"[AvatarSlotManager] Saving position for slot {currentSlotIndex}");

                    // 永続キャッシュに保存
                    currentSlotData.SaveTransform(bridge.CurrentModel.transform);
                    cache.SaveToFile();

                    // メモリキャッシュにも保存して非アクティブ化
                    var cacheRef = GetMemoryCache();
                    if (cacheRef != null)
                    {
                        cacheRef.DeactivateAvatar(currentSlotIndex, currentSlotData);
                    }

                    Debug.Log($"[AvatarSlotManager] Saved and deactivated avatar for slot {currentSlotIndex}");
                }
            }
        }

        /// <summary>
        /// スロットUIを更新
        /// </summary>
        private void UpdateSlotUI(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= avatarSlots.Count) return;

            var slot = avatarSlots[slotIndex];
            var slotData = cache.GetSlot(slotIndex);

            slot.SetSlotData(slotData);
        }

        /// <summary>
        /// スロットを選択
        /// </summary>
        public void SelectSlot(int slotIndex)
        {
            // 以前の選択を解除
            if (currentSlotIndex >= 0 && currentSlotIndex < avatarSlots.Count)
            {
                avatarSlots[currentSlotIndex].SetSelected(false);
            }

            currentSlotIndex = slotIndex;

            // 新しいスロットを選択
            if (slotIndex >= 0 && slotIndex < avatarSlots.Count)
            {
                avatarSlots[slotIndex].SetSelected(true);
            }

            // Issue #416: 最後にアクティブだったスロットを記録
            if (cache != null && slotIndex >= 0)
            {
                cache.SetLastActiveSlot(slotIndex);
                cache.SaveToFile();
            }

            OnSlotSelected?.Invoke(slotIndex);
        }

        /// <summary>
        /// スロットをクリア
        /// </summary>
        public void ClearSlot(int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= maxSlots) return;

            Debug.Log($"[AvatarSlotManager] Clearing slot {slotIndex}...");

            // RuntimeFBXLoaderBridgeに通知（currentModel参照のクリア）
            var bridge = GetLoaderBridge();
            if (bridge != null)
            {
                bridge.OnSlotCleared(slotIndex);
            }

            // メモリキャッシュからも削除（GameObjectの破棄）
            var cacheRef = GetMemoryCache();
            if (cacheRef != null)
            {
                cacheRef.RemoveFromCache(slotIndex);
            }

            cache.ClearSlot(slotIndex);
            cache.SaveToFile();

            UpdateSlotUI(slotIndex);

            if (currentSlotIndex == slotIndex)
            {
                currentSlotIndex = -1;
            }

            OnSlotCleared?.Invoke(slotIndex);
            Debug.Log($"[AvatarSlotManager] Cleared slot {slotIndex}");
        }

        /// <summary>
        /// スロットUIを登録
        /// </summary>
        public void RegisterSlotUI(AvatarSlot slot)
        {
            if (!avatarSlots.Contains(slot))
            {
                avatarSlots.Add(slot);

                int index = avatarSlots.Count - 1;
                var slotData = cache.GetSlot(index);

                slot.Initialize(index, slotData);
                slot.OnSlotClicked += OnSlotClickedHandler;
                slot.OnSlotLongPressed += OnSlotLongPressedHandler;
            }
        }

        #region Issue #73: Progress Event Handlers

        /// <summary>
        /// 操作開始時 - プログレス表示開始
        /// </summary>
        private void OnQueueOperationStarted(AvatarOperationQueue.Operation op)
        {
            if (op.Type == AvatarOperationQueue.OperationType.Load &&
                op.SlotIndex >= 0 && op.SlotIndex < avatarSlots.Count)
            {
                avatarSlots[op.SlotIndex].StartLoading();
                Debug.Log($"[AvatarSlotManager] Progress started for slot {op.SlotIndex}");
            }
        }

        /// <summary>
        /// 進捗更新時 - プログレスリング更新
        /// </summary>
        private void OnQueueProgressUpdated(AvatarOperationQueue.Operation op, float progress)
        {
            if (op.SlotIndex >= 0 && op.SlotIndex < avatarSlots.Count)
            {
                // 0-100 → 0-1 に変換
                avatarSlots[op.SlotIndex].SetProgress(progress / 100f);
            }
        }

        /// <summary>
        /// 操作完了時 - プログレス表示完了
        /// </summary>
        private void OnQueueOperationCompleted(AvatarOperationQueue.Operation op, AvatarOperationQueue.OperationResult result)
        {
            if (op.SlotIndex >= 0 && op.SlotIndex < avatarSlots.Count)
            {
                if (result.Success)
                {
                    avatarSlots[op.SlotIndex].CompleteLoading();
                }
                else
                {
                    avatarSlots[op.SlotIndex].CancelLoading();
                }
                Debug.Log($"[AvatarSlotManager] Progress completed for slot {op.SlotIndex}, success={result.Success}");
            }
        }

        /// <summary>
        /// 操作キャンセル時 - プログレス表示キャンセル
        /// </summary>
        private void OnQueueOperationCancelled(AvatarOperationQueue.Operation op)
        {
            if (op.SlotIndex >= 0 && op.SlotIndex < avatarSlots.Count)
            {
                avatarSlots[op.SlotIndex].CancelLoading();
                Debug.Log($"[AvatarSlotManager] Progress cancelled for slot {op.SlotIndex}");
            }
        }

        #endregion

        private void OnDestroy()
        {
            // イベント登録解除
            foreach (var slot in avatarSlots)
            {
                if (slot != null)
                {
                    slot.OnSlotClicked -= OnSlotClickedHandler;
                    slot.OnSlotLongPressed -= OnSlotLongPressedHandler;
                }
            }

            // Issue #73: キューイベント登録解除
            if (operationQueue != null)
            {
                operationQueue.OnOperationStarted -= OnQueueOperationStarted;
                operationQueue.OnProgressUpdated -= OnQueueProgressUpdated;
                operationQueue.OnOperationCompleted -= OnQueueOperationCompleted;
                operationQueue.OnOperationCancelled -= OnQueueOperationCancelled;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
