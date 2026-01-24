using System;
using Cysharp.Threading.Tasks;

namespace AICam.AvatarCache.IO
{
    /// <summary>
    /// アバターキャッシュのエクスポーター
    /// .avatarcache形式（ZIP）でエクスポート
    /// </summary>
    public class AvatarCacheExporter
    {
        /// <summary>
        /// キャッシュを.avatarcache形式でエクスポート
        /// </summary>
        public static UniTask ExportAsync(string cacheId, string exportPath)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// エクスポートファイルのバリデーション
        /// </summary>
        public static bool ValidateExportFile(string exportPath)
        {
            throw new NotImplementedException();
        }
    }
}
