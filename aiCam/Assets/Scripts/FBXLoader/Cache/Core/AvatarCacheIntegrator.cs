using System;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AICam.AvatarCache
{
    /// <summary>
    /// Issue #457: AvatarCacheManagerとAvatarMemoryCacheを統合するブリッジ
    /// バイナリキャッシュシステムと既存のスロットシステムを接続する
    /// </summary>
    public class AvatarCacheIntegrator
    {
        private const string ICON_FILENAME = "icon.png";

        private readonly string _cacheRootPath;
        private readonly AvatarCacheManager _cacheManager;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="cacheRootPath">キャッシュルートパス（nullの場合はデフォルトを使用）</param>
        public AvatarCacheIntegrator(string cacheRootPath = null)
        {
            _cacheRootPath = cacheRootPath ?? Application.persistentDataPath;
            _cacheManager = new AvatarCacheManager(_cacheRootPath);
        }

        /// <summary>
        /// バイナリキャッシュが存在するか確認
        /// </summary>
        /// <param name="cacheId">キャッシュID（ファイルハッシュ）</param>
        /// <returns>キャッシュが存在する場合はtrue</returns>
        public bool HasBinaryCache(string cacheId)
        {
            if (string.IsNullOrEmpty(cacheId))
                return false;

            return _cacheManager.CacheExists(cacheId);
        }

        /// <summary>
        /// バイナリキャッシュを作成
        /// </summary>
        /// <param name="avatar">キャッシュするアバターGameObject</param>
        /// <param name="sourceFilePath">元のVRM/FBXファイルパス</param>
        /// <param name="iconSourcePath">アイコンファイルパス（キャッシュに含める）</param>
        /// <returns>キャッシュID（ファイルハッシュ）</returns>
        public async UniTask<string> CreateBinaryCacheAsync(GameObject avatar, string sourceFilePath, string iconSourcePath = null)
        {
            if (avatar == null)
                throw new ArgumentNullException(nameof(avatar));

            if (string.IsNullOrEmpty(sourceFilePath))
                throw new ArgumentNullException(nameof(sourceFilePath));

            // ファイルハッシュをキャッシュIDとして使用
            var cacheId = AvatarCacheManager.CalculateFileHash(sourceFilePath);

            // 既にキャッシュが存在する場合はスキップ
            if (HasBinaryCache(cacheId))
            {
                Debug.Log($"[AvatarCacheIntegrator] Cache already exists: {cacheId}");
                return cacheId;
            }

            // キャッシュを作成（引数順序: vrmPath, avatar）
            await _cacheManager.CreateCacheAsync(sourceFilePath, avatar);

            // アイコンをキャッシュフォルダにコピー
            if (!string.IsNullOrEmpty(iconSourcePath) && File.Exists(iconSourcePath))
            {
                var cacheDir = _cacheManager.GetCacheDirectoryPath(cacheId);
                var iconDestPath = Path.Combine(cacheDir, ICON_FILENAME);
                try
                {
                    File.Copy(iconSourcePath, iconDestPath, overwrite: true);
                    Debug.Log($"[AvatarCacheIntegrator] Icon copied to cache: {iconDestPath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AvatarCacheIntegrator] Failed to copy icon to cache: {e.Message}");
                }
            }

            Debug.Log($"[AvatarCacheIntegrator] Cache created: {cacheId}");
            return cacheId;
        }

        /// <summary>
        /// バイナリキャッシュからアバターをロード
        /// </summary>
        /// <param name="cacheId">キャッシュID</param>
        /// <param name="onProgress">進捗コールバック（0-100）</param>
        /// <param name="slotIndex">スロットインデックス（0以上の場合、アイコンをキャッシュからスロットに復元する）</param>
        /// <returns>ロードしたアバターGameObject（キャッシュが存在しない場合はnull）</returns>
        public async UniTask<GameObject> LoadFromBinaryCacheAsync(string cacheId, Action<float> onProgress = null, int slotIndex = -1)
        {
            if (string.IsNullOrEmpty(cacheId))
                return null;

            if (!HasBinaryCache(cacheId))
            {
                Debug.LogWarning($"[AvatarCacheIntegrator] Cache not found: {cacheId}");
                return null;
            }

            try
            {
                onProgress?.Invoke(10f);
                var avatar = await _cacheManager.LoadFromCacheAsync(cacheId);
                onProgress?.Invoke(100f);

                // アイコンをキャッシュからスロットに復元
                if (slotIndex >= 0)
                {
                    RestoreIconFromCache(cacheId, slotIndex);
                }

                Debug.Log($"[AvatarCacheIntegrator] Loaded from cache: {cacheId}");
                return avatar;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarCacheIntegrator] Failed to load from cache: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// キャッシュ内のアイコンパスを取得
        /// </summary>
        /// <param name="cacheId">キャッシュID</param>
        /// <returns>アイコンパス（存在しない場合はnull）</returns>
        public string GetCachedIconPath(string cacheId)
        {
            if (string.IsNullOrEmpty(cacheId))
                return null;

            var cacheDir = _cacheManager.GetCacheDirectoryPath(cacheId);
            var iconPath = Path.Combine(cacheDir, ICON_FILENAME);
            return File.Exists(iconPath) ? iconPath : null;
        }

        /// <summary>
        /// キャッシュからスロットのアイコンを復元
        /// </summary>
        private void RestoreIconFromCache(string cacheId, int slotIndex)
        {
            var cachedIconPath = GetCachedIconPath(cacheId);
            if (cachedIconPath == null) return;

            var slotIconPath = AvatarSlotCache.GetIconPath(slotIndex);
            try
            {
                var iconsDir = Path.GetDirectoryName(slotIconPath);
                if (!string.IsNullOrEmpty(iconsDir) && !Directory.Exists(iconsDir))
                {
                    Directory.CreateDirectory(iconsDir);
                }

                File.Copy(cachedIconPath, slotIconPath, overwrite: true);
                Debug.Log($"[AvatarCacheIntegrator] Icon restored from cache to: {slotIconPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarCacheIntegrator] Failed to restore icon from cache: {e.Message}");
            }
        }

        /// <summary>
        /// バイナリキャッシュを削除
        /// </summary>
        /// <param name="cacheId">キャッシュID</param>
        public void DeleteBinaryCache(string cacheId)
        {
            if (string.IsNullOrEmpty(cacheId))
                return;

            _cacheManager.DeleteCache(cacheId);
            Debug.Log($"[AvatarCacheIntegrator] Cache deleted: {cacheId}");
        }
    }
}
