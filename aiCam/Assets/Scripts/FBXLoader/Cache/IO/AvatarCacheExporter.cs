using System;
using System.IO;
using System.IO.Compression;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AICam.AvatarCache.IO
{
    /// <summary>
    /// アバターキャッシュのエクスポーター
    /// .avatarcache形式（ZIP）でエクスポート
    /// </summary>
    public static class AvatarCacheExporter
    {
        private static string _cacheRootPath;

        /// <summary>
        /// キャッシュルートパスを設定
        /// </summary>
        public static void SetCacheRootPath(string cacheRootPath)
        {
            _cacheRootPath = cacheRootPath;
        }

        /// <summary>
        /// キャッシュを.avatarcache形式でエクスポート
        /// </summary>
        public static async UniTask ExportAsync(string cacheId, string exportPath)
        {
            if (string.IsNullOrEmpty(cacheId))
                throw new ArgumentNullException(nameof(cacheId));

            if (string.IsNullOrEmpty(exportPath))
                throw new ArgumentNullException(nameof(exportPath));

            // キャッシュディレクトリを取得
            var cacheDir = GetCacheDirectory(cacheId);

            if (!Directory.Exists(cacheDir))
                throw new DirectoryNotFoundException($"Cache directory not found: {cacheDir}");

            // エクスポート先ディレクトリを作成
            var exportDir = Path.GetDirectoryName(exportPath);
            if (!string.IsNullOrEmpty(exportDir) && !Directory.Exists(exportDir))
            {
                Directory.CreateDirectory(exportDir);
            }

            // 既存ファイルがあれば削除
            if (File.Exists(exportPath))
            {
                File.Delete(exportPath);
            }

            // ZIPアーカイブを作成
            await UniTask.RunOnThreadPool(() =>
            {
                ZipFile.CreateFromDirectory(cacheDir, exportPath, System.IO.Compression.CompressionLevel.Optimal, false);
            });

            Debug.Log($"[AvatarCacheExporter] Exported: {exportPath}");
        }

        /// <summary>
        /// エクスポートファイルのバリデーション
        /// </summary>
        public static bool ValidateExportFile(string exportPath)
        {
            if (string.IsNullOrEmpty(exportPath) || !File.Exists(exportPath))
                return false;

            try
            {
                using var archive = ZipFile.OpenRead(exportPath);

                // manifest.jsonが存在するかチェック
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                {
                    Debug.LogWarning("[AvatarCacheExporter] manifest.json not found in archive");
                    return false;
                }

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarCacheExporter] Validation failed: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// キャッシュディレクトリのパスを取得
        /// </summary>
        private static string GetCacheDirectory(string cacheId)
        {
            // 複数の可能性のあるパスを試す
            var possiblePaths = new[]
            {
                // 設定されたルートパス
                !string.IsNullOrEmpty(_cacheRootPath)
                    ? Path.Combine(_cacheRootPath, "AvatarCache", cacheId)
                    : null,

                // Application.persistentDataPath
                Path.Combine(Application.persistentDataPath, "AvatarCache", cacheId),

                // Application.temporaryCachePath（テスト用）
                Path.Combine(Application.temporaryCachePath, "AvatarCache", cacheId),

                // テスト用ディレクトリ（AvatarCacheTest）
                Path.Combine(Application.temporaryCachePath, "AvatarCacheTest", "AvatarCache", cacheId),

                // テストディレクトリ（直接指定されたパス）
                cacheId.Contains("/") || cacheId.Contains("\\")
                    ? cacheId
                    : null
            };

            foreach (var path in possiblePaths)
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    return path;
                }
            }

            // デフォルトパスを返す
            if (!string.IsNullOrEmpty(_cacheRootPath))
            {
                return Path.Combine(_cacheRootPath, "AvatarCache", cacheId);
            }

            return Path.Combine(Application.persistentDataPath, "AvatarCache", cacheId);
        }
    }
}
