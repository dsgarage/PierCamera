using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using AICam.Analytics;
using AICam.AvatarCache;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Issue #419: 統一アバターロードワークフロー
    /// 全てのアバターロード操作の単一エントリポイント
    /// </summary>
    public class AvatarLoadHandler : MonoBehaviour
    {
        #region Singleton

        private static AvatarLoadHandler _instance;
        public static AvatarLoadHandler Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindObjectOfType<AvatarLoadHandler>();
                    if (_instance == null)
                    {
                        Debug.LogError("[AvatarLoadHandler] Instance not found in scene!");
                    }
                }
                return _instance;
            }
        }

        #endregion

        #region Serialized Fields

        [Header("Loaders")]
        [SerializeField] private RuntimeFBXLoaderBridge fbxLoaderBridge;
        // Note: VRM loading is handled through RuntimeFBXLoaderBridge to avoid cyclic assembly dependencies

        [Header("Cache")]
        [SerializeField] private AvatarMemoryCache memoryCache;
        [SerializeField] private AvatarSlotManager slotManager;

        [Header("Icon")]
        [SerializeField] private AvatarIconCapture iconCapture;

        #endregion

        #region Events

        /// <summary>
        /// アバターロード完了時に発火
        /// </summary>
        public event Action<LoadResult> OnLoadComplete;

        /// <summary>
        /// ロード進捗更新時に発火
        /// </summary>
        public event Action<float> OnLoadProgress;

        #endregion

        #region Private Fields

        private bool isLoading = false;
        private CancellationTokenSource currentLoadCts;

        #endregion

        #region Unity Lifecycle

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;

            ValidateDependencies();
        }

        void OnDestroy()
        {
            currentLoadCts?.Cancel();
            currentLoadCts?.Dispose();

            if (_instance == this)
            {
                _instance = null;
            }
        }

        #endregion

        #region Dependency Validation

        private void ValidateDependencies()
        {
            // 自動検索
            if (fbxLoaderBridge == null)
                fbxLoaderBridge = FindObjectOfType<RuntimeFBXLoaderBridge>();

            if (memoryCache == null)
                memoryCache = AvatarMemoryCache.Instance;

            if (slotManager == null)
                slotManager = AvatarSlotManager.Instance;

            if (iconCapture == null)
                iconCapture = AvatarIconCapture.Instance;

            // 検証ログ
            Debug.Log($"[AvatarLoadHandler] Dependencies validated:");
            Debug.Log($"  - fbxLoaderBridge: {(fbxLoaderBridge != null ? "✅" : "❌")}");
            Debug.Log($"  - memoryCache: {(memoryCache != null ? "✅" : "❌")}");
            Debug.Log($"  - slotManager: {(slotManager != null ? "✅" : "❌")}");
            Debug.Log($"  - iconCapture: {(iconCapture != null ? "✅" : "❌")}");
        }

        #endregion

        #region Public API - Main Entry Points

        /// <summary>
        /// 統一ロードエントリポイント
        /// </summary>
        public async UniTask<LoadResult> LoadAsync(LoadRequest request)
        {
            if (request == null)
            {
                return LoadResult.Failed(-1, "LoadRequest is null");
            }

            Debug.Log($"[📦 LOAD] ==========================================");
            Debug.Log($"[📦 LOAD] Source: {request.Source}");
            Debug.Log($"[📦 LOAD] FilePath: {request.FilePath ?? "null"}");
            Debug.Log($"[📦 LOAD] TargetSlot: {request.TargetSlotIndex}");
            Debug.Log($"[📦 LOAD] FileType: {request.FileType}");

            if (isLoading)
            {
                Debug.LogWarning("[📦 LOAD] Already loading, cancelling previous load");
                currentLoadCts?.Cancel();
            }

            isLoading = true;
            currentLoadCts = new CancellationTokenSource();

            try
            {
                LoadResult result;

                switch (request.Source)
                {
                    case LoadSource.FromFilePicker:
                        result = await LoadFromFilePickerInternalAsync(request, currentLoadCts.Token);
                        break;

                    case LoadSource.FromSlot:
                        result = await LoadFromSlotInternalAsync(request, currentLoadCts.Token);
                        break;

                    case LoadSource.FromRestore:
                        result = await LoadFromRestoreInternalAsync(request, currentLoadCts.Token);
                        break;

                    case LoadSource.FromUnityPackage:
                        result = await LoadFromUnityPackageInternalAsync(request, currentLoadCts.Token);
                        break;

                    default:
                        result = LoadResult.Failed(request.TargetSlotIndex, $"Unknown LoadSource: {request.Source}");
                        break;
                }

                Debug.Log($"[📦 LOAD] Result: {(result.Success ? "✅ SUCCESS" : "❌ FAILED")}");
                if (!result.Success)
                {
                    Debug.Log($"[📦 LOAD] Error: {result.ErrorMessage}");
                }
                Debug.Log($"[📦 LOAD] ==========================================");

                OnLoadComplete?.Invoke(result);
                return result;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[📦 LOAD] Load cancelled");
                return LoadResult.Failed(request.TargetSlotIndex, "Load cancelled");
            }
            catch (Exception e)
            {
                Debug.LogError($"[📦 LOAD ❌] Exception: {e.Message}");
                Debug.LogException(e);

                // Crashlytics にエラー情報を送信
                CrashlyticsHelper.LogAvatarLoadError(request.FilePath, e.Message);

                return LoadResult.Failed(request.TargetSlotIndex, e.Message);
            }
            finally
            {
                isLoading = false;
            }
        }

        /// <summary>
        /// ファイルピッカーからロード（新規ファイル選択時）
        /// </summary>
        public async UniTask<LoadResult> LoadFromFilePickerAsync(string filePath, int targetSlot)
        {
            var request = new LoadRequest
            {
                Source = LoadSource.FromFilePicker,
                FilePath = filePath,
                TargetSlotIndex = targetSlot,
                FileType = AvatarSlotData.DetectFileType(filePath)
            };

            return await LoadAsync(request);
        }

        /// <summary>
        /// スロットからロード（スロットタップ時）
        /// </summary>
        public async UniTask<LoadResult> LoadFromSlotAsync(int slotIndex)
        {
            var cache = slotManager?.Cache;
            var slotData = cache?.GetSlot(slotIndex);

            var request = new LoadRequest
            {
                Source = LoadSource.FromSlot,
                FilePath = slotData?.modelFilePath,
                TargetSlotIndex = slotIndex,
                FileType = slotData?.fileType ?? AvatarFileType.Unknown
            };

            return await LoadAsync(request);
        }

        /// <summary>
        /// アプリ復帰時に最後にアクティブだったスロットを復元
        /// </summary>
        public async UniTask<LoadResult> RestoreLastActiveAsync()
        {
            var cache = slotManager?.Cache;
            if (cache == null)
            {
                return LoadResult.Failed(-1, "Cache not available");
            }

            int slotToRestore = cache.GetSlotToRestore();
            if (slotToRestore < 0)
            {
                Debug.Log("[📦 RESTORE] No valid slot to restore");
                return LoadResult.Failed(-1, "No valid slot to restore");
            }

            var slotData = cache.GetSlot(slotToRestore);

            var request = new LoadRequest
            {
                Source = LoadSource.FromRestore,
                FilePath = slotData?.modelFilePath,
                TargetSlotIndex = slotToRestore,
                FileType = slotData?.fileType ?? AvatarFileType.Unknown
            };

            return await LoadAsync(request);
        }

        #endregion

        #region Internal Load Implementations

        /// <summary>
        /// ファイルピッカーからのロード実装
        /// </summary>
        private async UniTask<LoadResult> LoadFromFilePickerInternalAsync(LoadRequest request, CancellationToken ct)
        {
            // 1. ファイル存在確認
            if (string.IsNullOrEmpty(request.FilePath) || !File.Exists(request.FilePath))
            {
                return LoadResult.Failed(request.TargetSlotIndex, $"File not found: {request.FilePath}");
            }

            OnLoadProgress?.Invoke(0.1f);

            // 2. ファイルタイプに応じてロード
            GameObject avatar = await LoadAvatarByTypeAsync(request, ct);
            if (avatar == null)
            {
                return LoadResult.Failed(request.TargetSlotIndex, "Failed to load avatar");
            }

            OnLoadProgress?.Invoke(0.6f);

            // 3. AnimatorController設定
            SetupAnimatorController(avatar);
            OnLoadProgress?.Invoke(0.7f);

            // 4. アイコン生成・保存
            string iconPath = await CaptureAndSaveIconAsync(avatar, request.TargetSlotIndex);
            OnLoadProgress?.Invoke(0.85f);

            // 5. キャッシュ更新（メモリ）
            if (memoryCache != null)
            {
                memoryCache.CacheAvatar(request.TargetSlotIndex, request.FilePath, avatar, keepActive: true);
            }

            // 6. キャッシュ更新（永続）
            UpdatePersistentCache(request, avatar.name, iconPath);
            OnLoadProgress?.Invoke(0.95f);

            // 7. スロット選択
            slotManager?.SelectSlot(request.TargetSlotIndex);
            OnLoadProgress?.Invoke(1.0f);

            // 8. Crashlytics にアバター情報を送信
            CrashlyticsHelper.SetAvatarInfo(request.FilePath);
            CrashlyticsHelper.SetSlotInfo(request.TargetSlotIndex);

            return LoadResult.Succeeded(request.TargetSlotIndex, avatar);
        }

        /// <summary>
        /// スロットからのロード実装
        /// </summary>
        private async UniTask<LoadResult> LoadFromSlotInternalAsync(LoadRequest request, CancellationToken ct)
        {
            var cache = slotManager?.Cache;
            var slotData = cache?.GetSlot(request.TargetSlotIndex);

            if (slotData == null || !slotData.IsConfigured)
            {
                return LoadResult.Failed(request.TargetSlotIndex, "Slot is not configured");
            }

            if (!slotData.ModelFileExists)
            {
                return LoadResult.Failed(request.TargetSlotIndex, $"Model file not found: {slotData.modelFilePath}");
            }

            OnLoadProgress?.Invoke(0.1f);

            // メモリキャッシュを確認
            if (memoryCache != null && memoryCache.HasCachedAvatar(request.TargetSlotIndex))
            {
                if (memoryCache.IsCacheValid(request.TargetSlotIndex))
                {
                    Debug.Log($"[📦 LOAD] Cache hit for slot {request.TargetSlotIndex}");
                    var cachedAvatar = memoryCache.ActivateAvatar(request.TargetSlotIndex, null);
                    if (cachedAvatar != null)
                    {
                        SetupAnimatorController(cachedAvatar);
                        slotManager?.SelectSlot(request.TargetSlotIndex);
                        OnLoadProgress?.Invoke(1.0f);

                        // Crashlytics にアバター情報を送信（キャッシュヒット時も）
                        CrashlyticsHelper.SetAvatarInfo(slotData?.modelFilePath);
                        CrashlyticsHelper.SetSlotInfo(request.TargetSlotIndex);

                        return LoadResult.Succeeded(request.TargetSlotIndex, cachedAvatar, wasCacheHit: true);
                    }
                }
                else
                {
                    Debug.Log($"[📦 LOAD] Invalid cache for slot {request.TargetSlotIndex}, reloading");
                    memoryCache.RemoveFromCache(request.TargetSlotIndex);
                }
            }

            // ファイルからロード
            request.FilePath = slotData.modelFilePath;
            request.FileType = slotData.fileType;

            GameObject avatar = await LoadAvatarByTypeAsync(request, ct);
            if (avatar == null)
            {
                return LoadResult.Failed(request.TargetSlotIndex, "Failed to load avatar from slot");
            }

            OnLoadProgress?.Invoke(0.7f);

            // AnimatorController設定
            SetupAnimatorController(avatar);

            // 位置復元
            if (slotData.HasSavedTransform)
            {
                slotData.ApplyTransform(avatar.transform);
                Debug.Log($"[📦 LOAD] Restored transform from cache");
            }

            // メモリキャッシュに追加
            if (memoryCache != null)
            {
                memoryCache.CacheAvatar(request.TargetSlotIndex, request.FilePath, avatar, keepActive: true);
            }

            // スロット選択
            slotManager?.SelectSlot(request.TargetSlotIndex);
            OnLoadProgress?.Invoke(1.0f);

            // Crashlytics にアバター情報を送信
            CrashlyticsHelper.SetAvatarInfo(request.FilePath);
            CrashlyticsHelper.SetSlotInfo(request.TargetSlotIndex);

            return LoadResult.Succeeded(request.TargetSlotIndex, avatar);
        }

        /// <summary>
        /// アプリ復帰時のロード実装
        /// </summary>
        private async UniTask<LoadResult> LoadFromRestoreInternalAsync(LoadRequest request, CancellationToken ct)
        {
            Debug.Log($"[📦 RESTORE] Restoring slot {request.TargetSlotIndex}...");

            // スロットからのロードと同じ処理
            return await LoadFromSlotInternalAsync(request, ct);
        }

        /// <summary>
        /// UnityPackageからのロード実装（将来拡張用）
        /// </summary>
        private async UniTask<LoadResult> LoadFromUnityPackageInternalAsync(LoadRequest request, CancellationToken ct)
        {
            // TODO: unitypackage解凍・ロード実装
            await UniTask.Yield(ct);
            return LoadResult.Failed(request.TargetSlotIndex, "UnityPackage loading not yet implemented");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// ファイルタイプに応じたアバターロード
        /// All formats (VRM, FBX, GLB, GLTF) are loaded through RuntimeFBXLoaderBridge
        /// </summary>
        private async UniTask<GameObject> LoadAvatarByTypeAsync(LoadRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (fbxLoaderBridge == null)
            {
                Debug.LogError("[📦 LOAD] RuntimeFBXLoaderBridge is not available");
                return null;
            }

            switch (request.FileType)
            {
                case AvatarFileType.VRM:
                case AvatarFileType.FBX:
                    Debug.Log($"[📦 LOAD] Loading {request.FileType}: {request.FilePath}");
                    var result = await fbxLoaderBridge.LoadAsync(request.FilePath, null, p => OnLoadProgress?.Invoke(0.1f + p * 0.5f));
                    return result.Success ? result.Avatar : null;

                case AvatarFileType.UnityPackage:
                    Debug.LogWarning($"[📦 LOAD] UnityPackage loading not yet implemented");
                    return null;

                default:
                    Debug.LogWarning($"[📦 LOAD] Unknown file type: {request.FileType}");
                    return null;
            }
        }

        /// <summary>
        /// AnimatorController設定
        /// </summary>
        private void SetupAnimatorController(GameObject avatar)
        {
            if (avatar == null) return;

            var animator = avatar.GetComponent<Animator>();
            if (animator == null)
            {
                animator = avatar.AddComponent<Animator>();
                Debug.Log($"[🎭 ANIMATOR] Added Animator component to {avatar.name}");
            }

            // RuntimeFBXLoaderBridgeからAnimatorControllerを取得
            var controller = fbxLoaderBridge?.GetAnimatorController();
            if (controller != null)
            {
                animator.runtimeAnimatorController = controller;
                Debug.Log($"[🎭 ANIMATOR ✅] Set AnimatorController '{controller.name}' to {avatar.name}");
            }
            else
            {
                Debug.LogWarning($"[🎭 ANIMATOR ⚠️] No AnimatorController available for {avatar.name}");
            }
        }

        /// <summary>
        /// アイコンキャプチャ・保存
        /// </summary>
        private async UniTask<string> CaptureAndSaveIconAsync(GameObject avatar, int slotIndex)
        {
            if (iconCapture == null || avatar == null)
            {
                return null;
            }

            try
            {
                string iconPath = AvatarSlotCache.GetIconPath(slotIndex);

                // ディレクトリ作成
                string directory = Path.GetDirectoryName(iconPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // キャプチャ
                var texture = await iconCapture.CaptureAsTextureAsync(avatar);
                if (texture != null)
                {
                    byte[] pngData = texture.EncodeToPNG();
                    File.WriteAllBytes(iconPath, pngData);
                    Debug.Log($"[💾 ICON ✅] Saved icon: {iconPath} ({pngData.Length} bytes)");
                    return iconPath;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[💾 ICON ❌] Failed to capture icon: {e.Message}");
            }

            return null;
        }

        /// <summary>
        /// 永続キャッシュ更新
        /// </summary>
        private void UpdatePersistentCache(LoadRequest request, string avatarName, string iconPath)
        {
            var cache = slotManager?.Cache;
            if (cache == null) return;

            var slotData = cache.GetSlot(request.TargetSlotIndex);
            if (slotData == null) return;

            slotData.modelFilePath = request.FilePath;
            slotData.fileType = request.FileType;
            slotData.avatarName = avatarName ?? "Avatar";
            slotData.isValid = true;

            if (!string.IsNullOrEmpty(iconPath))
            {
                slotData.iconFilePath = iconPath;
            }

            cache.SetLastActiveSlot(request.TargetSlotIndex);
            cache.SaveToFile();

            Debug.Log($"[💾 CACHE ✅] Updated persistent cache: slot={request.TargetSlotIndex}, path={request.FilePath}");
        }

        #endregion

        #region Public Utility Methods

        /// <summary>
        /// ロード中かどうか
        /// </summary>
        public bool IsLoading => isLoading;

        /// <summary>
        /// 現在のロードをキャンセル
        /// </summary>
        public void CancelCurrentLoad()
        {
            currentLoadCts?.Cancel();
        }

        #endregion
    }

    #region Supporting Classes

    /// <summary>
    /// ロードソース（どこからロードするか）
    /// </summary>
    public enum LoadSource
    {
        /// <summary>ファイルピッカーから新規選択</summary>
        FromFilePicker,

        /// <summary>スロットタップ</summary>
        FromSlot,

        /// <summary>アプリ復帰/再起動時の復元</summary>
        FromRestore,

        /// <summary>UnityPackage解凍後</summary>
        FromUnityPackage
    }

    /// <summary>
    /// ロードリクエスト
    /// </summary>
    public class LoadRequest
    {
        public LoadSource Source { get; set; }
        public string FilePath { get; set; }
        public int TargetSlotIndex { get; set; }
        public AvatarFileType FileType { get; set; }

        public override string ToString()
        {
            return $"LoadRequest(Source={Source}, FilePath={FilePath}, Slot={TargetSlotIndex}, Type={FileType})";
        }
    }

    /// <summary>
    /// ロード結果
    /// </summary>
    public class LoadResult
    {
        public bool Success { get; private set; }
        public GameObject Avatar { get; private set; }
        public int SlotIndex { get; private set; }
        public string ErrorMessage { get; private set; }
        public bool WasCacheHit { get; private set; }

        private LoadResult() { }

        public static LoadResult Succeeded(int slotIndex, GameObject avatar, bool wasCacheHit = false)
        {
            return new LoadResult
            {
                Success = true,
                SlotIndex = slotIndex,
                Avatar = avatar,
                WasCacheHit = wasCacheHit,
                ErrorMessage = null
            };
        }

        public static LoadResult Failed(int slotIndex, string errorMessage)
        {
            return new LoadResult
            {
                Success = false,
                SlotIndex = slotIndex,
                Avatar = null,
                WasCacheHit = false,
                ErrorMessage = errorMessage
            };
        }

        public override string ToString()
        {
            if (Success)
            {
                return $"LoadResult(Success, Slot={SlotIndex}, Avatar={Avatar?.name ?? "null"}, CacheHit={WasCacheHit})";
            }
            return $"LoadResult(Failed, Slot={SlotIndex}, Error={ErrorMessage})";
        }
    }

    #endregion
}
