using System;
using System.Collections.Generic;
using UnityEngine;
using Cysharp.Threading.Tasks;
using AICam.AvatarCache;

namespace AICam.FBXLoader
{
    /// <summary>
    /// ロード済みアバターのメモリキャッシュ
    /// アプリ実行中はメモリ上にキャッシュし、再ロードを回避
    /// アプリ終了時に自動破棄
    /// </summary>
    public class AvatarMemoryCache : MonoBehaviour
    {
        public static AvatarMemoryCache Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private int maxCachedAvatars = 6;
        [SerializeField] private bool deactivateCachedAvatars = true;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        // キャッシュエントリ
        [Serializable]
        public class CacheEntry
        {
            public int slotIndex;
            public string modelPath;
            public GameObject avatarObject;
            public DateTime loadedAt;
            public DateTime lastUsedAt;
            public bool isActive;

            // 最終位置情報（リスポーン用）
            public Vector3 lastLocalPosition;
            public Vector3 lastWorldPosition;  // ワールド座標も保存
            public Quaternion lastLocalRotation;
            public Quaternion lastWorldRotation;  // ワールド回転も保存
            public Vector3 lastScale;
            public Transform lastParent;
            public bool hasLastTransform;

            public CacheEntry(int slot, string path, GameObject obj)
            {
                slotIndex = slot;
                modelPath = path;
                avatarObject = obj;
                loadedAt = DateTime.Now;
                lastUsedAt = DateTime.Now;
                isActive = true;
                hasLastTransform = false;
            }

            /// <summary>
            /// 現在のトランスフォームを保存
            /// </summary>
            public void SaveTransform()
            {
                if (avatarObject != null)
                {
                    lastLocalPosition = avatarObject.transform.localPosition;
                    lastWorldPosition = avatarObject.transform.position;
                    lastLocalRotation = avatarObject.transform.localRotation;
                    lastWorldRotation = avatarObject.transform.rotation;
                    lastScale = avatarObject.transform.localScale;
                    lastParent = avatarObject.transform.parent;
                    hasLastTransform = true;

                    Debug.Log($"[CacheEntry] SaveTransform slot {slotIndex}: parent={lastParent?.name ?? "null"}, localPos={lastLocalPosition}, worldPos={lastWorldPosition}");
                }
                else
                {
                    Debug.LogWarning($"[CacheEntry] SaveTransform failed: avatarObject is null");
                }
            }

            /// <summary>
            /// 保存したトランスフォームを復元（ワールド座標ベース）
            /// </summary>
            public void RestoreTransform(Transform defaultParent)
            {
                if (avatarObject != null && hasLastTransform)
                {
                    // Unity の fake null 対策: bool変換で確実にチェック
                    Transform parent = (lastParent && lastParent != null) ? lastParent : defaultParent;

                    Debug.Log($"[CacheEntry] RestoreTransform: target worldPos={lastWorldPosition}, worldRot={lastWorldRotation.eulerAngles}, scale={lastScale}");
                    Debug.Log($"[CacheEntry] RestoreTransform: parent={parent?.name ?? "null"}, lastParent valid={(lastParent && lastParent != null)}");

                    // まず親を設定
                    avatarObject.transform.SetParent(parent, false);

                    // ワールド座標で位置と回転を設定（親の状態に関係なく正確に復元）
                    avatarObject.transform.position = lastWorldPosition;
                    avatarObject.transform.rotation = lastWorldRotation;
                    avatarObject.transform.localScale = lastScale;

                    Debug.Log($"[CacheEntry] After restore: worldPos={avatarObject.transform.position}, localPos={avatarObject.transform.localPosition}");
                }
                else
                {
                    Debug.LogWarning($"[CacheEntry] RestoreTransform failed: avatarObject={avatarObject != null}, hasLastTransform={hasLastTransform}");
                }
            }
        }

        // スロットインデックスをキーとしたキャッシュ
        private Dictionary<int, CacheEntry> cacheBySlot = new Dictionary<int, CacheEntry>();

        // モデルパスをキーとしたキャッシュ（同じモデルの重複ロード防止）
        private Dictionary<string, CacheEntry> cacheByPath = new Dictionary<string, CacheEntry>();

        // 現在アクティブなアバターのスロットインデックス
        private int activeSlotIndex = -1;

        // ローダー参照（依存注入 or SerializeField）
        [Header("Loader")]
        [SerializeField] private RuntimeFBXLoaderBridge avatarLoader;

        // デフォルト配置用の親Transform
        [SerializeField] private Transform defaultParent;

        // Issue #457: バイナリキャッシュ統合
        private AvatarCacheIntegrator _cacheIntegrator;

        // キャッシュ統計
        public int CachedCount => cacheBySlot.Count;
        public int ActiveSlotIndex => activeSlotIndex;

        /// <summary>
        /// ローダーを設定
        /// </summary>
        public void SetLoader(IAvatarLoader loader)
        {
            avatarLoader = loader as RuntimeFBXLoaderBridge;
        }

        /// <summary>
        /// デフォルト親を設定
        /// </summary>
        public void SetDefaultParent(Transform parent)
        {
            defaultParent = parent;
        }

        /// <summary>
        /// Issue #457: バイナリキャッシュインテグレーターを設定
        /// </summary>
        public void SetCacheIntegrator(AvatarCacheIntegrator integrator)
        {
            _cacheIntegrator = integrator;
        }

        // イベント
        public event Action<int, GameObject> OnAvatarCached;
        public event Action<int> OnAvatarEvicted;
        public event Action<int, GameObject> OnAvatarActivated;
        public event Action<int, SlotSwitchResult> OnSlotSwitched;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[AvatarMemoryCache] Duplicate instance, destroying...");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        /// <summary>
        /// アバターをキャッシュに追加
        /// </summary>
        /// <param name="slotIndex">スロットインデックス</param>
        /// <param name="modelPath">モデルファイルパス</param>
        /// <param name="avatarObject">アバターGameObject</param>
        /// <param name="keepActive">trueの場合、キャッシュ登録後もアクティブのまま維持（初回ロード時用）</param>
        public void CacheAvatar(int slotIndex, string modelPath, GameObject avatarObject, bool keepActive = false)
        {
            if (avatarObject == null)
            {
                Debug.LogWarning($"[AvatarMemoryCache] Cannot cache null avatar for slot {slotIndex}");
                return;
            }

            // 同じGameObjectが既にキャッシュされていないか確認（重複防止）
            foreach (var kvp in cacheBySlot)
            {
                if (kvp.Value.avatarObject == avatarObject && kvp.Key != slotIndex)
                {
                    Debug.LogWarning($"[AvatarMemoryCache] Avatar already cached in slot {kvp.Key}, skipping cache for slot {slotIndex}");
                    return;
                }
            }

            // 既存のキャッシュがあれば削除（同じスロットに別のモデルを入れる場合）
            if (cacheBySlot.TryGetValue(slotIndex, out var existingEntry))
            {
                if (existingEntry.avatarObject != avatarObject)
                {
                    EvictEntry(existingEntry);
                }
                else
                {
                    // 同じオブジェクトなので更新だけ
                    existingEntry.lastUsedAt = DateTime.Now;
                    if (showDebugInfo)
                    {
                        Debug.Log($"[AvatarMemoryCache] Avatar already cached for slot {slotIndex}, updated timestamp");
                    }
                    return;
                }
            }

            // 同じパスのキャッシュがあれば削除（別スロットで同じモデルを使う場合）
            if (!string.IsNullOrEmpty(modelPath) && cacheByPath.TryGetValue(modelPath, out var pathEntry))
            {
                if (pathEntry.slotIndex != slotIndex)
                {
                    EvictEntry(pathEntry);
                }
            }

            // キャッシュが最大数に達している場合、最も古いものを削除
            if (cacheBySlot.Count >= maxCachedAvatars)
            {
                EvictOldest();
            }

            // 新しいエントリを作成
            var entry = new CacheEntry(slotIndex, modelPath, avatarObject);
            cacheBySlot[slotIndex] = entry;

            if (!string.IsNullOrEmpty(modelPath))
            {
                cacheByPath[modelPath] = entry;
            }

            // 現在の位置を保存
            entry.SaveTransform();

            if (keepActive)
            {
                // 初回ロード時はアクティブのまま維持（位置も変更しない）
                entry.isActive = true;
                activeSlotIndex = slotIndex;

                if (showDebugInfo)
                {
                    Debug.Log($"[AvatarMemoryCache] Cached avatar for slot {slotIndex} (kept active): {avatarObject.name}");
                    Debug.Log($"[AvatarMemoryCache] Saved position: {entry.lastWorldPosition}");
                }
            }
            else
            {
                // キャッシュ用の親オブジェクトに移動し、非アクティブ化
                avatarObject.transform.SetParent(transform);
                avatarObject.SetActive(false);
                entry.isActive = false;

                if (showDebugInfo)
                {
                    Debug.Log($"[AvatarMemoryCache] Cached avatar for slot {slotIndex}: {avatarObject.name}");
                }
            }

            if (showDebugInfo)
            {
                Debug.Log($"[AvatarMemoryCache] Cache count: {cacheBySlot.Count}/{maxCachedAvatars}");
            }

            OnAvatarCached?.Invoke(slotIndex, avatarObject);
        }

        /// <summary>
        /// キャッシュからアバターを取得
        /// </summary>
        public GameObject GetCachedAvatar(int slotIndex)
        {
            if (cacheBySlot.TryGetValue(slotIndex, out var entry))
            {
                if (entry.avatarObject != null)
                {
                    entry.lastUsedAt = DateTime.Now;

                    if (showDebugInfo)
                    {
                        Debug.Log($"[AvatarMemoryCache] Cache HIT for slot {slotIndex}");
                    }

                    return entry.avatarObject;
                }
                else
                {
                    // オブジェクトが破棄されている場合はキャッシュから削除
                    RemoveFromCache(slotIndex);
                }
            }

            if (showDebugInfo)
            {
                Debug.Log($"[AvatarMemoryCache] Cache MISS for slot {slotIndex}");
            }

            return null;
        }

        /// <summary>
        /// パスからキャッシュを取得
        /// </summary>
        public GameObject GetCachedAvatarByPath(string modelPath)
        {
            if (string.IsNullOrEmpty(modelPath)) return null;

            if (cacheByPath.TryGetValue(modelPath, out var entry))
            {
                if (entry.avatarObject != null)
                {
                    entry.lastUsedAt = DateTime.Now;
                    return entry.avatarObject;
                }
                else
                {
                    RemoveFromCache(entry.slotIndex);
                }
            }

            return null;
        }

        /// <summary>
        /// キャッシュにアバターが存在するか確認
        /// </summary>
        public bool HasCachedAvatar(int slotIndex)
        {
            if (cacheBySlot.TryGetValue(slotIndex, out var entry))
            {
                return entry.avatarObject != null;
            }
            return false;
        }

        /// <summary>
        /// アバターをアクティブ化（表示）
        /// 注: 他アバターの非アクティブ化はAvatarSlotManager.SaveCurrentAvatarPosition()で行われるため、ここでは行わない
        /// </summary>
        public GameObject ActivateAvatar(int slotIndex, Transform parent = null)
        {
            Debug.Log($"[AvatarMemoryCache] ActivateAvatar called for slot {slotIndex}");

            var avatar = GetCachedAvatar(slotIndex);
            if (avatar != null)
            {
                if (parent != null)
                {
                    avatar.transform.SetParent(parent);
                }

                avatar.SetActive(true);
                activeSlotIndex = slotIndex;

                if (cacheBySlot.TryGetValue(slotIndex, out var entry))
                {
                    entry.isActive = true;
                    entry.lastUsedAt = DateTime.Now;
                }

                if (showDebugInfo)
                {
                    Debug.Log($"[AvatarMemoryCache] Activated avatar for slot {slotIndex}: {avatar.name}");
                }

                OnAvatarActivated?.Invoke(slotIndex, avatar);
                return avatar;
            }

            Debug.LogWarning($"[AvatarMemoryCache] No cached avatar found for slot {slotIndex}");
            return null;
        }

        #region SwitchToSlotAsync - 統合スロット切り替えAPI

        /// <summary>
        /// スロットを切り替える統合API
        /// 1. 現在のアバターを保存・非アクティブ化
        /// 2. キャッシュチェック
        /// 3. キャッシュヒット → アクティブ化＆位置復元
        /// 4. キャッシュミス → ローダー経由でロード → キャッシュ追加
        /// </summary>
        /// <param name="targetSlotIndex">切り替え先スロット</param>
        /// <param name="slotData">永続キャッシュデータ（位置保存用）</param>
        /// <param name="currentSlotData">現在のスロットデータ（位置保存用）</param>
        /// <param name="onProgress">進捗コールバック</param>
        /// <returns>切り替え結果</returns>
        public async UniTask<SlotSwitchResult> SwitchToSlotAsync(
            int targetSlotIndex,
            AvatarSlotData slotData,
            AvatarSlotData currentSlotData = null,
            Action<float> onProgress = null)
        {
            if (slotData == null)
            {
                return SlotSwitchResult.Failed(targetSlotIndex, "SlotData is null");
            }

            if (string.IsNullOrEmpty(slotData.modelFilePath))
            {
                return SlotSwitchResult.Failed(targetSlotIndex, "Model file path is empty");
            }

            Debug.Log($"[AvatarMemoryCache] === SwitchToSlotAsync: {activeSlotIndex} -> {targetSlotIndex} ===");

            // UIの応答性を維持するためにYield（Issue #426）
            await UniTask.Yield();

            // 1. 現在のアバターを保存・非アクティブ化
            if (activeSlotIndex >= 0 && activeSlotIndex != targetSlotIndex)
            {
                DeactivateAvatar(activeSlotIndex, currentSlotData);
                Debug.Log($"[AvatarMemoryCache] Deactivated previous slot {activeSlotIndex}");
            }

            // 2. キャッシュチェック
            bool isCacheHit = HasCachedAvatar(targetSlotIndex);
            GameObject avatar = null;

            if (isCacheHit)
            {
                // 3. キャッシュヒット → アクティブ化＆位置復元
                Debug.Log($"[AvatarMemoryCache] CACHE HIT for slot {targetSlotIndex}");
                onProgress?.Invoke(50f);

                // 親を指定しない（RestoreTransformで保存された親を使用）
                avatar = ActivateAvatar(targetSlotIndex, null);

                if (avatar != null)
                {
                    // 位置復元
                    var entry = GetCacheEntry(targetSlotIndex);
                    if (entry != null && entry.hasLastTransform)
                    {
                        // 保存された親と位置を復元（nullの場合はavatarLoaderのmodelParentを使う）
                        Transform restoreParent = entry.lastParent;
                        if (restoreParent == null && avatarLoader != null)
                        {
                            restoreParent = avatarLoader.transform.parent; // loaderのmodelParentに近い位置
                        }
                        entry.RestoreTransform(restoreParent);
                        Debug.Log($"[AvatarMemoryCache] Restored position: {entry.lastWorldPosition}");
                    }
                    else if (slotData.HasSavedTransform)
                    {
                        // メモリキャッシュにない場合は永続キャッシュから復元
                        slotData.ApplyTransform(avatar.transform);
                        Debug.Log($"[AvatarMemoryCache] Restored position from persistent cache");
                    }

                    onProgress?.Invoke(100f);

                    var result = SlotSwitchResult.Succeeded(targetSlotIndex, avatar, wasCacheHit: true);
                    OnSlotSwitched?.Invoke(targetSlotIndex, result);
                    return result;
                }
                else
                {
                    // キャッシュヒットしたが復元失敗 - フォールスルーしてロード
                    Debug.LogWarning($"[AvatarMemoryCache] Cache hit but ActivateAvatar failed, falling back to load");
                    isCacheHit = false;
                }
            }

            // 4. キャッシュミス → ローダー経由でロード
            Debug.Log($"[AvatarMemoryCache] CACHE MISS for slot {targetSlotIndex}, loading from file");

            // ロード前にYield（Issue #426）
            await UniTask.Yield();

            if (avatarLoader == null)
            {
                // ローダーが未設定の場合はエラー（SetLoader()またはInspectorで設定必須）
                Debug.LogError("[AvatarMemoryCache] avatarLoader is null. Use SetLoader() or assign in Inspector.");
                return SlotSwitchResult.Failed(targetSlotIndex, "Avatar loader not configured. Call SetLoader() or assign in Inspector.");
            }

            onProgress?.Invoke(10f);

            // ローダー側のデフォルト親を使用（nullを渡すとLoaderのmodelParentが使われる）
            var loadResult = await avatarLoader.LoadAsync(
                slotData.modelFilePath,
                null,
                progress => onProgress?.Invoke(10f + progress * 0.8f) // 10-90%
            );

            if (!loadResult.Success)
            {
                return SlotSwitchResult.Failed(targetSlotIndex, loadResult.ErrorMessage);
            }

            avatar = loadResult.Avatar;
            onProgress?.Invoke(95f);

            // キャッシュに追加
            CacheAvatar(targetSlotIndex, slotData.modelFilePath, avatar, keepActive: true);

            // 永続キャッシュから位置を復元（初回ロード時）
            if (slotData.HasSavedTransform)
            {
                slotData.ApplyTransform(avatar.transform);
                Debug.Log($"[AvatarMemoryCache] Applied saved transform from persistent cache");
            }

            onProgress?.Invoke(100f);

            var switchResult = SlotSwitchResult.Succeeded(targetSlotIndex, avatar, wasCacheHit: false);
            OnSlotSwitched?.Invoke(targetSlotIndex, switchResult);

            Debug.Log($"[AvatarMemoryCache] Switch complete: slot {targetSlotIndex}, cacheHit={isCacheHit}");
            return switchResult;
        }

        #endregion

        /// <summary>
        /// アバターを非アクティブ化（非表示、キャッシュには残す）
        /// 最終位置を保存してリスポーン時に復元できるようにする
        /// </summary>
        /// <param name="slotIndex">スロットインデックス</param>
        /// <param name="slotData">永続キャッシュに保存する場合はAvatarSlotDataを渡す</param>
        public void DeactivateAvatar(int slotIndex, AvatarSlotData slotData = null)
        {
            if (cacheBySlot.TryGetValue(slotIndex, out var entry))
            {
                if (entry.avatarObject != null && deactivateCachedAvatars)
                {
                    // アバターがアクティブな場合のみ位置を保存（既に非アクティブなら保存済み）
                    if (entry.avatarObject.activeInHierarchy)
                    {
                        Debug.Log($"[AvatarMemoryCache] DeactivateAvatar slot {slotIndex}: avatar is ACTIVE, saving current position");
                        Debug.Log($"[AvatarMemoryCache] Current worldPos: {entry.avatarObject.transform.position}, localPos: {entry.avatarObject.transform.localPosition}");

                        // メモリキャッシュに保存
                        entry.SaveTransform();

                        // 永続キャッシュにも保存
                        if (slotData != null)
                        {
                            slotData.SaveTransform(entry.avatarObject.transform);
                            Debug.Log($"[AvatarMemoryCache] Also saved to persistent cache for slot {slotIndex}");
                        }
                    }
                    else
                    {
                        Debug.Log($"[AvatarMemoryCache] DeactivateAvatar slot {slotIndex}: avatar already INACTIVE, keeping saved position: {entry.lastWorldPosition}");
                    }

                    entry.avatarObject.SetActive(false);
                    entry.avatarObject.transform.SetParent(transform);
                    entry.isActive = false;

                    if (showDebugInfo)
                    {
                        Debug.Log($"[AvatarMemoryCache] Deactivated avatar for slot {slotIndex}, final saved position: {entry.lastWorldPosition}");
                    }
                }
            }
            else
            {
                // キャッシュにないスロットは無視
                // Debug.Log($"[AvatarMemoryCache] DeactivateAvatar slot {slotIndex}: not in cache, skipping");
            }

            if (activeSlotIndex == slotIndex)
            {
                activeSlotIndex = -1;
            }
        }

        /// <summary>
        /// キャッシュエントリを取得（位置情報復元用）
        /// </summary>
        public CacheEntry GetCacheEntry(int slotIndex)
        {
            if (cacheBySlot.TryGetValue(slotIndex, out var entry))
            {
                return entry;
            }
            return null;
        }

        /// <summary>
        /// スロットのキャッシュを削除
        /// </summary>
        public void RemoveFromCache(int slotIndex)
        {
            if (cacheBySlot.TryGetValue(slotIndex, out var entry))
            {
                EvictEntry(entry);
            }
        }

        /// <summary>
        /// 全てのキャッシュをクリア
        /// </summary>
        public void ClearAll()
        {
            var slotIndices = new List<int>(cacheBySlot.Keys);
            foreach (var slotIndex in slotIndices)
            {
                RemoveFromCache(slotIndex);
            }

            cacheBySlot.Clear();
            cacheByPath.Clear();
            activeSlotIndex = -1;

            if (showDebugInfo)
            {
                Debug.Log("[AvatarMemoryCache] Cleared all cache");
            }
        }

        /// <summary>
        /// 最も古いキャッシュを削除
        /// </summary>
        private void EvictOldest()
        {
            CacheEntry oldest = null;
            int oldestSlot = -1;

            foreach (var kvp in cacheBySlot)
            {
                // アクティブなアバターは削除しない
                if (kvp.Value.isActive) continue;

                if (oldest == null || kvp.Value.lastUsedAt < oldest.lastUsedAt)
                {
                    oldest = kvp.Value;
                    oldestSlot = kvp.Key;
                }
            }

            if (oldest != null)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"[AvatarMemoryCache] Evicting oldest cache: slot {oldestSlot}");
                }
                EvictEntry(oldest);
            }
        }

        /// <summary>
        /// キャッシュエントリを削除
        /// </summary>
        private void EvictEntry(CacheEntry entry)
        {
            if (entry == null) return;

            // Dictionaryから削除
            cacheBySlot.Remove(entry.slotIndex);
            if (!string.IsNullOrEmpty(entry.modelPath))
            {
                cacheByPath.Remove(entry.modelPath);
            }

            // GameObjectを破棄
            if (entry.avatarObject != null)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"[AvatarMemoryCache] Destroying cached avatar for slot {entry.slotIndex}");
                }
                Destroy(entry.avatarObject);
            }

            if (activeSlotIndex == entry.slotIndex)
            {
                activeSlotIndex = -1;
            }

            OnAvatarEvicted?.Invoke(entry.slotIndex);
        }

        /// <summary>
        /// キャッシュ情報を取得（デバッグ用）
        /// </summary>
        public string GetCacheInfo()
        {
            var info = $"Avatar Memory Cache\n";
            info += $"Cached: {cacheBySlot.Count}/{maxCachedAvatars}\n";
            info += $"Active Slot: {activeSlotIndex}\n\n";

            foreach (var kvp in cacheBySlot)
            {
                var entry = kvp.Value;
                info += $"Slot {kvp.Key}: {entry.avatarObject?.name ?? "NULL"}\n";
                info += $"  Path: {entry.modelPath}\n";
                info += $"  Active: {entry.isActive}\n";
                info += $"  Loaded: {entry.loadedAt:HH:mm:ss}\n";
                info += $"  LastUsed: {entry.lastUsedAt:HH:mm:ss}\n\n";
            }

            return info;
        }

        private void OnDestroy()
        {
            // アプリ終了時に全キャッシュをクリア
            ClearAll();

            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            // アプリ終了時に全キャッシュをクリア
            ClearAll();
        }

        /// <summary>
        /// Issue #346: バックグラウンド復帰時のキャッシュ整合性チェック
        /// iOSではバックグラウンド時にメモリが解放されGameObjectが破棄される可能性がある
        /// </summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus)
            {
                // バックグラウンドに入る前: 現在の状態を保存
                Debug.Log("[AvatarMemoryCache] App pausing, saving cache state...");
                SaveAllTransforms();
            }
            else
            {
                // バックグラウンドから復帰: キャッシュの整合性をチェック
                Debug.Log("[AvatarMemoryCache] App resuming, validating cache...");
                ValidateAndCleanupCache();
            }
        }

        /// <summary>
        /// 全キャッシュエントリのトランスフォームを保存
        /// </summary>
        private void SaveAllTransforms()
        {
            foreach (var kvp in cacheBySlot)
            {
                var entry = kvp.Value;
                if (entry.avatarObject != null && entry.avatarObject.activeInHierarchy)
                {
                    entry.SaveTransform();
                    Debug.Log($"[AvatarMemoryCache] Saved transform for slot {kvp.Key}");
                }
            }
        }

        /// <summary>
        /// Issue #346: キャッシュの整合性を検証し、無効なエントリをクリーンアップ
        /// バックグラウンド復帰時にGameObjectが破棄されている可能性があるため
        /// </summary>
        private void ValidateAndCleanupCache()
        {
            var invalidSlots = new List<int>();
            int validCount = 0;
            int invalidCount = 0;

            foreach (var kvp in cacheBySlot)
            {
                var entry = kvp.Value;

                // Unity の fake null チェック: GameObjectが破棄されているか確認
                // ReferenceEquals で null チェックしても破棄されたオブジェクトは検出できないため
                // bool変換で確実にチェック
                bool isValid = entry.avatarObject != null && entry.avatarObject;

                if (!isValid)
                {
                    Debug.LogWarning($"[AvatarMemoryCache] Cache entry for slot {kvp.Key} is invalid (GameObject destroyed during background)");
                    invalidSlots.Add(kvp.Key);
                    invalidCount++;
                }
                else
                {
                    validCount++;
                    Debug.Log($"[AvatarMemoryCache] Cache entry for slot {kvp.Key} is valid: {entry.avatarObject.name}");
                }
            }

            // 無効なエントリを削除
            foreach (var slotIndex in invalidSlots)
            {
                if (cacheBySlot.TryGetValue(slotIndex, out var entry))
                {
                    // パスキャッシュからも削除
                    if (!string.IsNullOrEmpty(entry.modelPath))
                    {
                        cacheByPath.Remove(entry.modelPath);
                    }
                    cacheBySlot.Remove(slotIndex);

                    Debug.Log($"[AvatarMemoryCache] Removed invalid cache entry for slot {slotIndex}");
                }
            }

            // アクティブスロットの整合性チェック
            if (activeSlotIndex >= 0 && !cacheBySlot.ContainsKey(activeSlotIndex))
            {
                Debug.LogWarning($"[AvatarMemoryCache] Active slot {activeSlotIndex} was invalidated, resetting to -1");
                activeSlotIndex = -1;
            }

            Debug.Log($"[AvatarMemoryCache] Cache validation complete: {validCount} valid, {invalidCount} invalid entries removed");

            // キャッシュが全て無効になった場合の通知
            if (invalidCount > 0 && cacheBySlot.Count == 0)
            {
                Debug.LogWarning("[AvatarMemoryCache] All cache entries were invalidated. Avatars need to be reloaded.");
            }
        }

        /// <summary>
        /// 指定スロットのキャッシュが有効かどうかをチェック
        /// バックグラウンド復帰後に再ロードが必要かどうかを判定するために使用
        /// </summary>
        public bool IsCacheValid(int slotIndex)
        {
            if (cacheBySlot.TryGetValue(slotIndex, out var entry))
            {
                // Unity の fake null チェック
                return entry.avatarObject != null && entry.avatarObject;
            }
            return false;
        }
    }
}
