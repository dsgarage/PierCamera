using System;
using System.IO;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AICam.AvatarCache
{
    /// <summary>
    /// アバターキャッシュマネージャー
    /// キャッシュの作成・ロード・管理を担当
    /// </summary>
    public class AvatarCacheManager
    {
        public const int CURRENT_CACHE_FORMAT_VERSION = 1;

        private readonly string _cacheRootPath;

        public AvatarCacheManager(string cacheRootPath)
        {
            _cacheRootPath = cacheRootPath;
        }

        /// <summary>
        /// ファイルのSHA256ハッシュを計算
        /// </summary>
        public static string CalculateFileHash(string filePath)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// キャッシュディレクトリパスを取得
        /// </summary>
        public string GetCacheDirectoryPath(string cacheId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// キャッシュが存在するかチェック
        /// </summary>
        public bool CacheExists(string cacheId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// キャッシュが有効かチェック
        /// </summary>
        public bool IsCacheValid(string cacheId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// VRMからキャッシュを作成
        /// </summary>
        public UniTask CreateCacheAsync(string vrmPath, GameObject avatar)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// キャッシュからアバターをロード
        /// </summary>
        public UniTask<GameObject> LoadFromCacheAsync(string cacheId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// キャッシュを削除
        /// </summary>
        public void DeleteCache(string cacheId)
        {
            throw new NotImplementedException();
        }
    }
}
