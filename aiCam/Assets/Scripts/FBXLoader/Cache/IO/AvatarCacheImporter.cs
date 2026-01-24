using System;
using System.IO;
using System.IO.Compression;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AICam.AvatarCache.IO
{
    /// <summary>
    /// アバターキャッシュのインポーター
    /// .avatarcache形式（ZIP）からインポート
    /// </summary>
    public static class AvatarCacheImporter
    {
        /// <summary>
        /// .avatarcache形式からインポート
        /// </summary>
        public static async UniTask<string> ImportAsync(string importPath, string cacheRootPath)
        {
            if (string.IsNullOrEmpty(importPath))
                throw new ArgumentNullException(nameof(importPath));

            if (string.IsNullOrEmpty(cacheRootPath))
                throw new ArgumentNullException(nameof(cacheRootPath));

            if (!File.Exists(importPath))
                throw new FileNotFoundException($"Import file not found: {importPath}");

            // マニフェストからキャッシュIDを取得
            var cacheId = ExtractCacheIdFromArchive(importPath);
            if (string.IsNullOrEmpty(cacheId))
            {
                // キャッシュIDが取得できない場合はファイル名から生成
                cacheId = Path.GetFileNameWithoutExtension(importPath) + "_" + DateTime.Now.Ticks;
            }

            // インポート先ディレクトリ
            var importDir = Path.Combine(cacheRootPath, "AvatarCache", cacheId);

            // 既存ディレクトリがあれば削除
            if (Directory.Exists(importDir))
            {
                Directory.Delete(importDir, true);
            }

            Directory.CreateDirectory(importDir);

            // ZIPを展開
            await UniTask.RunOnThreadPool(() =>
            {
                ZipFile.ExtractToDirectory(importPath, importDir);
            });

            Debug.Log($"[AvatarCacheImporter] Imported: {cacheId} to {importDir}");
            return cacheId;
        }

        /// <summary>
        /// インポートファイルの互換性チェック
        /// </summary>
        public static ImportCompatibility CheckCompatibility(string importPath)
        {
            var result = new ImportCompatibility
            {
                isCompatible = false,
                cacheFormatVersion = 0,
                unityVersion = "",
                platform = "",
                needsTextureRecompression = false
            };

            if (string.IsNullOrEmpty(importPath) || !File.Exists(importPath))
                return result;

            try
            {
                using var archive = ZipFile.OpenRead(importPath);

                // manifest.jsonを読み込み
                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                {
                    Debug.LogWarning("[AvatarCacheImporter] manifest.json not found");
                    return result;
                }

                using var stream = manifestEntry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();

                var manifest = JsonUtility.FromJson<AvatarCacheManifest>(json);
                if (manifest == null)
                {
                    Debug.LogWarning("[AvatarCacheImporter] Failed to parse manifest");
                    return result;
                }

                result.cacheFormatVersion = manifest.cacheFormatVersion;
                result.unityVersion = manifest.unityVersion ?? "";
                result.platform = manifest.platform ?? "";

                // バージョン互換性チェック
                result.isCompatible = manifest.cacheFormatVersion <= AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION;

                // プラットフォーム互換性チェック
                var currentPlatform = Application.platform.ToString();
                if (!string.IsNullOrEmpty(manifest.platform) && manifest.platform != currentPlatform)
                {
                    result.needsTextureRecompression = true;
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarCacheImporter] Compatibility check failed: {e.Message}");
                return result;
            }
        }

        /// <summary>
        /// アーカイブからキャッシュIDを抽出
        /// </summary>
        private static string ExtractCacheIdFromArchive(string archivePath)
        {
            try
            {
                using var archive = ZipFile.OpenRead(archivePath);

                var manifestEntry = archive.GetEntry("manifest.json");
                if (manifestEntry == null)
                    return null;

                using var stream = manifestEntry.Open();
                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();

                var manifest = JsonUtility.FromJson<AvatarCacheManifest>(json);
                return manifest?.cacheId;
            }
            catch
            {
                return null;
            }
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
