using System;
using Cysharp.Threading.Tasks;

namespace AICam.AvatarCache.IO
{
    /// <summary>
    /// アバターキャッシュのインポーター
    /// .avatarcache形式（ZIP）からインポート
    /// </summary>
    public class AvatarCacheImporter
    {
        /// <summary>
        /// .avatarcache形式からインポート
        /// </summary>
        public static UniTask<string> ImportAsync(string importPath, string cacheRootPath)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// インポートファイルの互換性チェック
        /// </summary>
        public static ImportCompatibility CheckCompatibility(string importPath)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// インポート互換性情報
    /// </summary>
    public class ImportCompatibility
    {
        public bool isCompatible;
        public int cacheFormatVersion;
        public string unityVersion;
        public string platform;
        public bool needsTextureRecompression;
    }
}
