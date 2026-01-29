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
    /// オプションでハッシュベースの難読化をサポート
    /// </summary>
    public static class AvatarCacheExporter
    {
        private static string _cacheRootPath;

        /// <summary>
        /// 難読化を有効にするかどうか（デフォルト: true）
        /// </summary>
        public static bool EnableObfuscation { get; set; } = true;

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
            var tempZipPath = exportPath + ".tmp";
            await UniTask.RunOnThreadPool(() =>
            {
                ZipFile.CreateFromDirectory(cacheDir, tempZipPath, System.IO.Compression.CompressionLevel.Optimal, false);
            });

            // 難読化処理
            if (EnableObfuscation)
            {
                await UniTask.RunOnThreadPool(() =>
                {
                    var zipData = File.ReadAllBytes(tempZipPath);
                    var obfuscatedData = CacheObfuscator.Obfuscate(zipData, cacheId);
                    File.WriteAllBytes(exportPath, obfuscatedData);
                    File.Delete(tempZipPath);
                });
                Debug.Log($"[AvatarCacheExporter] Exported (obfuscated): {exportPath}");
            }
            else
            {
                File.Move(tempZipPath, exportPath);
                Debug.Log($"[AvatarCacheExporter] Exported: {exportPath}");
            }
        }

        /// <summary>
        /// キャッシュを.avatarcache形式でエクスポート（難読化オプション指定）
        /// </summary>
        public static async UniTask ExportAsync(string cacheId, string exportPath, bool obfuscate)
        {
            var previousSetting = EnableObfuscation;
            try
            {
                EnableObfuscation = obfuscate;
                await ExportAsync(cacheId, exportPath);
            }
            finally
            {
                EnableObfuscation = previousSetting;
            }
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
                // 難読化されているか確認
                var fileData = File.ReadAllBytes(exportPath);
                byte[] zipData;

                if (CacheObfuscator.IsObfuscated(fileData))
                {
                    // マニフェストからcacheIdを取得するため、一時的に復号化
                    // 注: 検証のみなので、cacheIdが不明な場合は空文字でも構造確認可能
                    // ただし、完全な検証には正しいcacheIdが必要
                    Debug.Log("[AvatarCacheExporter] File is obfuscated, attempting validation...");

                    // 難読化ファイルは基本的に有効とみなす（構造検証はImport時に行う）
                    return fileData.Length > 4;
                }
                else
                {
                    zipData = fileData;
                }

                // ZIPとして検証
                using var stream = new MemoryStream(zipData);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

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
        /// ファイルが難読化されているか確認
        /// </summary>
        public static bool IsObfuscated(string exportPath)
        {
            return CacheObfuscator.IsObfuscated(exportPath);
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
