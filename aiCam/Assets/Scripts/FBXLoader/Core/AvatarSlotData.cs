using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバターファイルタイプ
    /// </summary>
    public enum AvatarFileType
    {
        Unknown,
        VRM,
        FBX,
        UnityPackage
    }

    /// <summary>
    /// 永続化可能な位置情報
    /// </summary>
    [Serializable]
    public class SerializableTransform
    {
        public float posX, posY, posZ;
        public float rotX, rotY, rotZ, rotW;
        public float scaleX, scaleY, scaleZ;
        public bool hasData;

        public SerializableTransform()
        {
            posX = posY = posZ = 0f;
            rotX = rotY = rotZ = 0f;
            rotW = 1f;
            scaleX = scaleY = scaleZ = 1f;
            hasData = false;
        }

        public void SetFromWorldTransform(UnityEngine.Transform t)
        {
            if (t == null) return;
            posX = t.position.x;
            posY = t.position.y;
            posZ = t.position.z;
            rotX = t.rotation.x;
            rotY = t.rotation.y;
            rotZ = t.rotation.z;
            rotW = t.rotation.w;
            scaleX = t.localScale.x;
            scaleY = t.localScale.y;
            scaleZ = t.localScale.z;
            hasData = true;
        }

        public UnityEngine.Vector3 GetPosition() => new UnityEngine.Vector3(posX, posY, posZ);
        public UnityEngine.Quaternion GetRotation() => new UnityEngine.Quaternion(rotX, rotY, rotZ, rotW);
        public UnityEngine.Vector3 GetScale() => new UnityEngine.Vector3(scaleX, scaleY, scaleZ);

        public void ApplyToTransform(UnityEngine.Transform t)
        {
            if (t == null || !hasData) return;
            t.position = GetPosition();
            t.rotation = GetRotation();
            t.localScale = GetScale();
        }
    }

    /// <summary>
    /// アバタースロットの個別データ
    /// </summary>
    [Serializable]
    public class AvatarSlotData
    {
        public int slotIndex;
        public string avatarName;
        public string modelFilePath;        // VRM/FBXファイルパス
        public string manifestFilePath;     // マニフェストファイルパス
        public string iconFilePath;         // アイコン画像パス（512x512）
        public string extractedFolderPath;  // unitypackage展開先
        public AvatarFileType fileType;
        public string vrmVersion;           // VRM 0.x / 1.0
        public string lastLoadedAt;
        public bool isValid;

        // 永続化される位置情報（ワールド座標）
        public SerializableTransform lastTransform = new SerializableTransform();

        // ポーズ管理用（PoseSlotController統合）
        public string poseManifestPath;           // pose_manifest.json パス
        public string poseIconFolderPath;         // ポーズアイコンフォルダ
        public List<string> registeredOverrideNames = new List<string>(); // 登録済みOverrideController名

        // Issue #457: バイナリキャッシュ統合
        public string binaryCacheId;              // AvatarCacheManagerのキャッシュID

        public AvatarSlotData()
        {
            slotIndex = -1;
            avatarName = string.Empty;
            modelFilePath = string.Empty;
            manifestFilePath = string.Empty;
            iconFilePath = string.Empty;
            extractedFolderPath = string.Empty;
            fileType = AvatarFileType.Unknown;
            vrmVersion = string.Empty;
            lastLoadedAt = string.Empty;
            isValid = false;
            lastTransform = new SerializableTransform();
            poseManifestPath = string.Empty;
            poseIconFolderPath = string.Empty;
            registeredOverrideNames = new List<string>();
            binaryCacheId = string.Empty;
        }

        public AvatarSlotData(int index) : this()
        {
            slotIndex = index;
        }

        /// <summary>
        /// スロットが設定済みかどうか
        /// </summary>
        public bool IsConfigured => !string.IsNullOrEmpty(modelFilePath) && isValid;

        /// <summary>
        /// アイコン画像が存在するか
        /// </summary>
        public bool HasIcon => !string.IsNullOrEmpty(iconFilePath) && File.Exists(iconFilePath);

        /// <summary>
        /// モデルファイルが存在するか
        /// </summary>
        public bool ModelFileExists => !string.IsNullOrEmpty(modelFilePath) && File.Exists(modelFilePath);

        /// <summary>
        /// Issue #457: バイナリキャッシュが存在するか
        /// </summary>
        public bool HasBinaryCache => !string.IsNullOrEmpty(binaryCacheId);

        /// <summary>
        /// Issue #457: バイナリキャッシュIDを設定
        /// </summary>
        public void SetBinaryCacheId(string cacheId)
        {
            binaryCacheId = cacheId;
        }

        /// <summary>
        /// Issue #457: バイナリキャッシュIDをクリア
        /// </summary>
        public void ClearBinaryCache()
        {
            binaryCacheId = null;
        }

        /// <summary>
        /// ファイルパスからファイルタイプを判定
        /// </summary>
        public static AvatarFileType DetectFileType(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return AvatarFileType.Unknown;

            string ext = Path.GetExtension(filePath).ToLower();
            return ext switch
            {
                ".vrm" => AvatarFileType.VRM,
                ".fbx" => AvatarFileType.FBX,
                ".unitypackage" => AvatarFileType.UnityPackage,
                _ => AvatarFileType.Unknown
            };
        }

        /// <summary>
        /// データをクリア
        /// </summary>
        public void Clear()
        {
            avatarName = string.Empty;
            modelFilePath = string.Empty;
            manifestFilePath = string.Empty;
            iconFilePath = string.Empty;
            extractedFolderPath = string.Empty;
            fileType = AvatarFileType.Unknown;
            vrmVersion = string.Empty;
            lastLoadedAt = string.Empty;
            isValid = false;
            lastTransform = new SerializableTransform();
            poseManifestPath = string.Empty;
            poseIconFolderPath = string.Empty;
            registeredOverrideNames = new List<string>();
            binaryCacheId = string.Empty;
        }

        /// <summary>
        /// ポーズマニフェストパスを取得（自動生成）
        /// </summary>
        public string GetPoseManifestPath()
        {
            if (!string.IsNullOrEmpty(poseManifestPath))
            {
                return poseManifestPath;
            }

            if (string.IsNullOrEmpty(avatarName))
            {
                return string.Empty;
            }

            return Path.Combine(Application.persistentDataPath, "PoseSlots", SanitizeFileName(avatarName), "pose_manifest.json");
        }

        /// <summary>
        /// ポーズアイコンフォルダパスを取得（自動生成）
        /// </summary>
        public string GetPoseIconFolderPath()
        {
            if (!string.IsNullOrEmpty(poseIconFolderPath))
            {
                return poseIconFolderPath;
            }

            if (string.IsNullOrEmpty(avatarName))
            {
                return string.Empty;
            }

            return Path.Combine(Application.persistentDataPath, "PoseSlots", SanitizeFileName(avatarName), "icons");
        }

        /// <summary>
        /// ファイル名をサニタイズ
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }

        /// <summary>
        /// 位置情報を保存
        /// </summary>
        public void SaveTransform(UnityEngine.Transform t)
        {
            if (lastTransform == null)
            {
                lastTransform = new SerializableTransform();
            }
            lastTransform.SetFromWorldTransform(t);
            UnityEngine.Debug.Log($"[AvatarSlotData] SaveTransform slot {slotIndex}: pos=({lastTransform.posX}, {lastTransform.posY}, {lastTransform.posZ})");
        }

        /// <summary>
        /// 保存された位置情報があるか
        /// </summary>
        public bool HasSavedTransform => lastTransform != null && lastTransform.hasData;

        /// <summary>
        /// 位置情報を適用
        /// </summary>
        public void ApplyTransform(UnityEngine.Transform t)
        {
            if (lastTransform != null && lastTransform.hasData)
            {
                lastTransform.ApplyToTransform(t);
                UnityEngine.Debug.Log($"[AvatarSlotData] ApplyTransform slot {slotIndex}: pos=({lastTransform.posX}, {lastTransform.posY}, {lastTransform.posZ})");
            }
        }

        /// <summary>
        /// 最終ロード日時を更新
        /// </summary>
        public void UpdateLastLoadedAt()
        {
            lastLoadedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }

    /// <summary>
    /// アバタースロットキャッシュ（全スロットデータの永続化用）
    /// </summary>
    [Serializable]
    public class AvatarSlotCache
    {
        public const string CACHE_FILE_NAME = "avatar_slot_cache.json";
        public const string ICONS_FOLDER_NAME = "icons";
        public const int CURRENT_VERSION = 2;  // v2: Added SerializableTransform for position persistence

        public List<AvatarSlotData> slots = new List<AvatarSlotData>();
        public int maxSlots = 6;
        public int version = CURRENT_VERSION;
        public string lastModified;
        public int lastActiveSlotIndex = -1;  // Issue #416: 最後にアクティブだったスロット

        /// <summary>
        /// キャッシュディレクトリのパスを取得
        /// </summary>
        public static string GetCacheDirectory()
        {
            return Path.Combine(Application.persistentDataPath, "AvatarSlots");
        }

        /// <summary>
        /// キャッシュファイルのパスを取得
        /// </summary>
        public static string GetCacheFilePath()
        {
            return Path.Combine(GetCacheDirectory(), CACHE_FILE_NAME);
        }

        /// <summary>
        /// アイコンフォルダのパスを取得
        /// </summary>
        public static string GetIconsDirectory()
        {
            return Path.Combine(GetCacheDirectory(), ICONS_FOLDER_NAME);
        }

        /// <summary>
        /// スロットのアイコンパスを取得
        /// </summary>
        public static string GetIconPath(int slotIndex)
        {
            return Path.Combine(GetIconsDirectory(), $"slot_{slotIndex}.png");
        }

        /// <summary>
        /// キャッシュを初期化
        /// </summary>
        public void Initialize(int numSlots)
        {
            maxSlots = numSlots;
            slots.Clear();

            for (int i = 0; i < numSlots; i++)
            {
                slots.Add(new AvatarSlotData(i));
            }

            lastModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// スロットデータを取得
        /// </summary>
        public AvatarSlotData GetSlot(int index)
        {
            if (index < 0 || index >= slots.Count)
            {
                Debug.LogWarning($"[AvatarSlotCache] Invalid slot index: {index}");
                return null;
            }
            return slots[index];
        }

        /// <summary>
        /// スロットデータを更新
        /// </summary>
        public void UpdateSlot(int index, AvatarSlotData data)
        {
            if (index < 0 || index >= slots.Count)
            {
                Debug.LogWarning($"[AvatarSlotCache] Invalid slot index: {index}");
                return;
            }

            data.slotIndex = index;
            slots[index] = data;
            lastModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// スロットをクリア
        /// </summary>
        public void ClearSlot(int index)
        {
            if (index < 0 || index >= slots.Count)
            {
                Debug.LogWarning($"[AvatarSlotCache] Invalid slot index: {index}");
                return;
            }

            // アイコンファイルを削除
            string iconPath = GetIconPath(index);
            if (File.Exists(iconPath))
            {
                try
                {
                    File.Delete(iconPath);
                    Debug.Log($"[AvatarSlotCache] Deleted icon: {iconPath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AvatarSlotCache] Failed to delete icon: {e.Message}");
                }
            }

            slots[index].Clear();
            lastModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// キャッシュをファイルに保存
        /// </summary>
        public void SaveToFile()
        {
            try
            {
                string directory = GetCacheDirectory();
                string iconsDirectory = GetIconsDirectory();

                // ディレクトリ作成
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    Debug.Log($"[💾 SAVE] Created directory: {directory}");
                }
                if (!Directory.Exists(iconsDirectory))
                {
                    Directory.CreateDirectory(iconsDirectory);
                }

                lastModified = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                string json = JsonUtility.ToJson(this, true);
                string filePath = GetCacheFilePath();
                File.WriteAllText(filePath, json);

                // 保存完了の詳細ログ
                var fileInfo = new FileInfo(filePath);
                int configuredCount = 0;
                int validCount = 0;
                foreach (var slot in slots)
                {
                    if (!string.IsNullOrEmpty(slot.modelFilePath)) configuredCount++;
                    if (slot.isValid) validCount++;
                }

                Debug.Log($"[💾 SAVE ✅] Cache saved successfully!");
                Debug.Log($"[💾 SAVE] Path: {filePath}");
                Debug.Log($"[💾 SAVE] Size: {fileInfo.Length} bytes");
                Debug.Log($"[💾 SAVE] Slots: {slots.Count} total, {configuredCount} configured, {validCount} valid");
                Debug.Log($"[💾 SAVE] LastActiveSlot: {lastActiveSlotIndex}");
                Debug.Log($"[💾 SAVE] LastModified: {lastModified}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[💾 SAVE ❌] Failed to save cache: {e.Message}");
                Debug.LogError($"[💾 SAVE ❌] Stack: {e.StackTrace}");
            }
        }

        /// <summary>
        /// キャッシュをファイルから読み込み（同期版）
        /// Issue #416: デバッグログ強化
        /// </summary>
        public static AvatarSlotCache LoadFromFile()
        {
            string filePath = GetCacheFilePath();
            Debug.Log($"[📂 LOAD] LoadFromFile started");
            Debug.Log($"[📂 LOAD] Path: {filePath}");

            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogWarning($"[📂 LOAD ⚠️] Cache file not found - creating new cache");
                    var newCache = new AvatarSlotCache();
                    newCache.Initialize(6);
                    return newCache;
                }

                var fileInfo = new FileInfo(filePath);
                Debug.Log($"[📂 LOAD] File found: {fileInfo.Length} bytes, modified: {fileInfo.LastWriteTime}");

                string json = File.ReadAllText(filePath);
                Debug.Log($"[📂 LOAD] JSON loaded: {json.Length} chars");

                return ParseAndMigrateCache(json, filePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[📂 LOAD ❌] Failed: {e.GetType().Name}: {e.Message}");
                var newCache = new AvatarSlotCache();
                newCache.Initialize(6);
                return newCache;
            }
        }

        /// <summary>
        /// キャッシュをファイルから非同期で読み込み
        /// Issue #416: デバッグログ強化
        /// </summary>
        public static async Cysharp.Threading.Tasks.UniTask<AvatarSlotCache> LoadFromFileAsync()
        {
            string filePath = GetCacheFilePath();
            Debug.Log($"[📂 LOAD ASYNC] ========== Loading Cache ==========");
            Debug.Log($"[📂 LOAD ASYNC] Path: {filePath}");

            try
            {
                // ディレクトリの存在確認
                string directory = GetCacheDirectory();
                if (!Directory.Exists(directory))
                {
                    Debug.LogWarning($"[📂 LOAD ASYNC ⚠️] Directory not found: {directory}");
                    Debug.Log($"[📂 LOAD ASYNC] Creating new empty cache");
                    var newCache = new AvatarSlotCache();
                    newCache.Initialize(6);
                    return newCache;
                }

                // ファイルの存在確認
                if (!File.Exists(filePath))
                {
                    Debug.LogWarning($"[📂 LOAD ASYNC ⚠️] Cache file not found");
                    Debug.Log($"[📂 LOAD ASYNC] Creating new empty cache");
                    var newCache = new AvatarSlotCache();
                    newCache.Initialize(6);
                    return newCache;
                }

                // ファイルサイズ確認
                var fileInfo = new FileInfo(filePath);
                Debug.Log($"[📂 LOAD ASYNC] File exists: {fileInfo.Length} bytes");
                Debug.Log($"[📂 LOAD ASYNC] Last modified: {fileInfo.LastWriteTime}");

                if (fileInfo.Length == 0)
                {
                    Debug.LogWarning($"[📂 LOAD ASYNC ⚠️] Cache file is empty");
                    var newCache = new AvatarSlotCache();
                    newCache.Initialize(6);
                    return newCache;
                }

                // 非同期でファイル読み込み
                string json = await File.ReadAllTextAsync(filePath);
                Debug.Log($"[📂 LOAD ASYNC] JSON loaded: {json.Length} chars");

                if (string.IsNullOrWhiteSpace(json))
                {
                    Debug.LogWarning($"[📂 LOAD ASYNC ⚠️] JSON is empty or whitespace");
                    var newCache = new AvatarSlotCache();
                    newCache.Initialize(6);
                    return newCache;
                }

                return ParseAndMigrateCache(json, filePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarSlotCache] Failed to load cache async: {e.GetType().Name}: {e.Message}");
                Debug.LogError($"[AvatarSlotCache] Stack trace: {e.StackTrace}");

                // 破損したキャッシュファイルをバックアップ
                try
                {
                    if (File.Exists(filePath))
                    {
                        string backupPath = filePath + $".corrupted.{DateTime.Now:yyyyMMddHHmmss}";
                        File.Copy(filePath, backupPath);
                        Debug.Log($"[AvatarSlotCache] Corrupted cache backed up to: {backupPath}");
                    }
                }
                catch (Exception backupEx)
                {
                    Debug.LogWarning($"[AvatarSlotCache] Failed to backup corrupted cache: {backupEx.Message}");
                }

                var newCache = new AvatarSlotCache();
                newCache.Initialize(6);
                return newCache;
            }
        }

        /// <summary>
        /// JSONをパースしてマイグレーション処理を行う（共通処理）
        /// Issue #416: デバッグログ強化
        /// </summary>
        private static AvatarSlotCache ParseAndMigrateCache(string json, string sourceFilePath = null)
        {
            AvatarSlotCache cache = null;

            try
            {
                cache = JsonUtility.FromJson<AvatarSlotCache>(json);
            }
            catch (Exception parseEx)
            {
                Debug.LogError($"[📂 PARSE ❌] JSON parse failed: {parseEx.Message}");
                Debug.LogError($"[📂 PARSE ❌] JSON preview: {json.Substring(0, Math.Min(500, json.Length))}");

                var newCache = new AvatarSlotCache();
                newCache.Initialize(6);
                return newCache;
            }

            if (cache == null)
            {
                Debug.LogWarning("[📂 PARSE ⚠️] Parse result is null");
                Debug.LogWarning($"[📂 PARSE ⚠️] JSON preview: {json.Substring(0, Math.Min(500, json.Length))}");
                var newCache = new AvatarSlotCache();
                newCache.Initialize(6);
                return newCache;
            }

            Debug.Log($"[📂 PARSE ✅] JSON parsed successfully");
            Debug.Log($"[📂 PARSE] Version: {cache.version}");
            Debug.Log($"[📂 PARSE] MaxSlots: {cache.maxSlots}");
            Debug.Log($"[📂 PARSE] Slots count: {cache.slots?.Count ?? 0}");
            Debug.Log($"[📂 PARSE] LastActiveSlot: {cache.lastActiveSlotIndex}");
            Debug.Log($"[📂 PARSE] LastModified: {cache.lastModified}");

            // slotsがnullの場合の対策
            if (cache.slots == null)
            {
                Debug.LogWarning("[📂 PARSE ⚠️] slots list is null, initializing...");
                cache.slots = new List<AvatarSlotData>();
            }

            // バージョンチェック（マイグレーション処理）
            if (cache.version < CURRENT_VERSION)
            {
                Debug.Log($"[📂 MIGRATE] Migrating v{cache.version} -> v{CURRENT_VERSION}");

                // v1 -> v2: SerializableTransform追加
                if (cache.version < 2)
                {
                    foreach (var slot in cache.slots)
                    {
                        if (slot.lastTransform == null)
                        {
                            slot.lastTransform = new SerializableTransform();
                        }
                    }
                    Debug.Log("[📂 MIGRATE] Added SerializableTransform to all slots");
                }

                cache.version = CURRENT_VERSION;
            }

            // スロット数の調整
            while (cache.slots.Count < cache.maxSlots)
            {
                cache.slots.Add(new AvatarSlotData(cache.slots.Count));
            }

            // 各スロットの有効性を検証
            int configuredCount = 0;
            int validCount = 0;
            Debug.Log($"[📂 VALIDATE] Checking {cache.slots.Count} slots...");
            for (int i = 0; i < cache.slots.Count; i++)
            {
                var slot = cache.slots[i];
                slot.slotIndex = i;

                if (!string.IsNullOrEmpty(slot.modelFilePath))
                {
                    configuredCount++;
                    // モデルファイルが存在しない場合は無効化
                    if (!File.Exists(slot.modelFilePath))
                    {
                        Debug.LogWarning($"[📂 VALIDATE ⚠️] Slot {i}: Model file missing - {slot.modelFilePath}");
                        slot.isValid = false;
                    }
                    else
                    {
                        validCount++;
                        Debug.Log($"[📂 VALIDATE ✅] Slot {i}: {slot.avatarName} ({slot.fileType})");
                    }
                }
            }

            Debug.Log($"[📂 LOAD COMPLETE ✅] {cache.slots.Count} total, {configuredCount} configured, {validCount} valid");
            Debug.Log($"[📂 LOAD ASYNC] ========================================");
            return cache;
        }

        /// <summary>
        /// 設定済みスロットの数を取得
        /// </summary>
        public int GetConfiguredSlotCount()
        {
            int count = 0;
            foreach (var slot in slots)
            {
                if (slot.IsConfigured) count++;
            }
            return count;
        }

        /// <summary>
        /// Issue #416: 最後にアクティブだったスロットを設定
        /// </summary>
        public void SetLastActiveSlot(int slotIndex)
        {
            if (slotIndex >= -1 && slotIndex < maxSlots)
            {
                lastActiveSlotIndex = slotIndex;
                Debug.Log($"[AvatarSlotCache] Set lastActiveSlotIndex to {slotIndex}");
            }
        }

        /// <summary>
        /// Issue #416: 復元すべきスロットインデックスを取得
        /// 最後にアクティブだったスロットが有効であればそれを、
        /// そうでなければ最初の設定済みスロットを返す
        /// </summary>
        public int GetSlotToRestore()
        {
            // 最後にアクティブだったスロットが有効かチェック
            if (lastActiveSlotIndex >= 0 && lastActiveSlotIndex < slots.Count)
            {
                var slot = slots[lastActiveSlotIndex];
                if (slot != null && slot.IsConfigured && slot.ModelFileExists)
                {
                    Debug.Log($"[AvatarSlotCache] Will restore last active slot: {lastActiveSlotIndex}");
                    return lastActiveSlotIndex;
                }
            }

            // 最初の有効なスロットを探す
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                if (slot != null && slot.IsConfigured && slot.ModelFileExists)
                {
                    Debug.Log($"[AvatarSlotCache] Will restore first valid slot: {i}");
                    return i;
                }
            }

            Debug.Log("[AvatarSlotCache] No valid slot to restore");
            return -1;
        }
    }
}
