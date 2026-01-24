using System;
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
        /// <returns>キャッシュID（ファイルハッシュ）</returns>
        public async UniTask<string> CreateBinaryCacheAsync(GameObject avatar, string sourceFilePath)
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

            // キャッシュを作成
            await _cacheManager.CreateCacheAsync(avatar, sourceFilePath);

            Debug.Log($"[AvatarCacheIntegrator] Cache created: {cacheId}");
            return cacheId;
        }

        /// <summary>
        /// バイナリキャッシュからアバターをロード
        /// </summary>
        /// <param name="cacheId">キャッシュID</param>
        /// <param name="onProgress">進捗コールバック（0-100）</param>
        /// <returns>ロードしたアバターGameObject（キャッシュが存在しない場合はnull）</returns>
        public async UniTask<GameObject> LoadFromBinaryCacheAsync(string cacheId, Action<float> onProgress = null)
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
                var avatar = await _cacheManager.LoadFromCacheAsync(cacheId, onProgress);
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
