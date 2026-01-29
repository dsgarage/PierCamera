using System.Collections;
using System.IO;
using System.Text;
using AICam.AvatarCache;
using AICam.AvatarCache.IO;
using AICam.Tests.PlayMode.AvatarCache;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.Integration
{
    /// <summary>
    /// Phase 9: 難読化テスト
    ///
    /// テスト対象:
    /// - CacheObfuscatorの基本機能
    /// - 難読化エクスポート/インポート
    /// - 後方互換性（非難読化ファイルの読み込み）
    /// </summary>
    [TestFixture]
    public class Phase9_ObfuscationTests : AvatarCacheTestBase
    {
        #region CacheObfuscator Basic Tests

        [UnityTest]
        public IEnumerator 難読化_データを難読化できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var originalData = Encoding.UTF8.GetBytes("Hello, Avatar Cache!");
            var cacheId = "test_cache_id";

            // Act
            var obfuscated = CacheObfuscator.Obfuscate(originalData, cacheId);

            // Assert
            Assert.IsNotNull(obfuscated);
            Assert.AreNotEqual(originalData, obfuscated);
            Assert.IsTrue(CacheObfuscator.IsObfuscated(obfuscated));

            Debug.Log($"[Phase9Test] 難読化成功: {originalData.Length} -> {obfuscated.Length} bytes");
        });

        [UnityTest]
        public IEnumerator 難読化_データを復号化できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var originalText = "Hello, Avatar Cache! これはテストです。";
            var originalData = Encoding.UTF8.GetBytes(originalText);
            var cacheId = "test_cache_id";

            // Act
            var obfuscated = CacheObfuscator.Obfuscate(originalData, cacheId);
            var deobfuscated = CacheObfuscator.Deobfuscate(obfuscated, cacheId);

            // Assert
            Assert.IsNotNull(deobfuscated);
            var deobfuscatedText = Encoding.UTF8.GetString(deobfuscated);
            Assert.AreEqual(originalText, deobfuscatedText);

            Debug.Log($"[Phase9Test] 復号化成功: {deobfuscatedText}");
        });

        [UnityTest]
        public IEnumerator 難読化_異なるcacheIdでは復号化できないこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var originalText = "Secret Data";
            var originalData = Encoding.UTF8.GetBytes(originalText);
            var correctCacheId = "correct_id";
            var wrongCacheId = "wrong_id";

            // Act
            var obfuscated = CacheObfuscator.Obfuscate(originalData, correctCacheId);
            var deobfuscatedWithWrongKey = CacheObfuscator.Deobfuscate(obfuscated, wrongCacheId);

            // Assert
            var deobfuscatedText = Encoding.UTF8.GetString(deobfuscatedWithWrongKey);
            Assert.AreNotEqual(originalText, deobfuscatedText);

            Debug.Log("[Phase9Test] 異なるキーでの復号化が正しく失敗");
        });

        [UnityTest]
        public IEnumerator 難読化_マジックヘッダーが正しいこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var originalData = Encoding.UTF8.GetBytes("Test");
            var cacheId = "test";

            // Act
            var obfuscated = CacheObfuscator.Obfuscate(originalData, cacheId);

            // Assert
            var magic = Encoding.ASCII.GetString(obfuscated, 0, 4);
            Assert.AreEqual(CacheObfuscator.OBFUSCATION_MAGIC, magic);

            Debug.Log($"[Phase9Test] マジックヘッダー: {magic}");
        });

        #endregion

        #region File-based Obfuscation Tests

        [UnityTest]
        public IEnumerator 難読化_ファイルを難読化保存できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var testDir = Path.Combine(TestCacheDirectory, "ObfuscationTest");
            Directory.CreateDirectory(testDir);

            var sourcePath = Path.Combine(testDir, "source.txt");
            var destPath = Path.Combine(testDir, "obfuscated.bin");
            var cacheId = "file_test";

            File.WriteAllText(sourcePath, "Test file content for obfuscation");

            // Act
            CacheObfuscator.ObfuscateFile(sourcePath, destPath, cacheId);

            // Assert
            Assert.IsTrue(File.Exists(destPath));
            Assert.IsTrue(CacheObfuscator.IsObfuscated(destPath));

            Debug.Log("[Phase9Test] ファイル難読化成功");
        });

        [UnityTest]
        public IEnumerator 難読化_ファイルを復号化読み込みできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var testDir = Path.Combine(TestCacheDirectory, "ObfuscationTest2");
            Directory.CreateDirectory(testDir);

            var sourcePath = Path.Combine(testDir, "source.txt");
            var destPath = Path.Combine(testDir, "obfuscated.bin");
            var cacheId = "file_test_2";
            var originalContent = "Test file content 日本語テスト";

            File.WriteAllText(sourcePath, originalContent);
            CacheObfuscator.ObfuscateFile(sourcePath, destPath, cacheId);

            // Act
            var deobfuscatedData = CacheObfuscator.DeobfuscateFile(destPath, cacheId);
            var deobfuscatedContent = Encoding.UTF8.GetString(deobfuscatedData);

            // Assert
            Assert.AreEqual(originalContent, deobfuscatedContent);

            Debug.Log($"[Phase9Test] ファイル復号化成功: {deobfuscatedContent}");
        });

        #endregion

        #region Export/Import with Obfuscation Tests

        [UnityTest]
        public IEnumerator エクスポート_難読化オプション有効でエクスポートできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            // ファイル名をcacheId（hash）と一致させる（難読化キーの整合性のため）
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", $"{hash}.avatarcache");

            // Create cache structure
            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), "{\"version\":1}");
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));

            // Act - 難読化有効でエクスポート
            AvatarCacheExporter.EnableObfuscation = true;
            await AvatarCacheExporter.ExportAsync(hash, exportPath);

            // Assert
            Assert.IsTrue(File.Exists(exportPath));
            Assert.IsTrue(AvatarCacheExporter.IsObfuscated(exportPath));

            Debug.Log("[Phase9Test] 難読化エクスポート成功");
        });

        [UnityTest]
        public IEnumerator エクスポート_難読化オプション無効でエクスポートできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            // 非難読化の場合はファイル名は任意でOK
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", "NotObfuscated.avatarcache");

            // Create cache structure
            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), "{\"version\":1}");
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));

            // Act - 難読化無効でエクスポート
            await AvatarCacheExporter.ExportAsync(hash, exportPath, obfuscate: false);

            // Assert
            Assert.IsTrue(File.Exists(exportPath));
            Assert.IsFalse(AvatarCacheExporter.IsObfuscated(exportPath));

            Debug.Log("[Phase9Test] 非難読化エクスポート成功");
        });

        [UnityTest]
        public IEnumerator インポート_難読化ファイルをインポートできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            // ファイル名をcacheId（hash）と一致させる（難読化キーの整合性のため）
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", $"{hash}.avatarcache");

            // Create cache structure
            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash
            };
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), JsonUtility.ToJson(manifest));
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));

            // Export with obfuscation
            AvatarCacheExporter.EnableObfuscation = true;
            await AvatarCacheExporter.ExportAsync(hash, exportPath);

            // Clean import target
            var importRootPath = Path.Combine(TestCacheDirectory, "ImportObfuscatedTest");
            if (Directory.Exists(importRootPath))
            {
                Directory.Delete(importRootPath, true);
            }

            // Act
            var importedCacheId = await AvatarCacheImporter.ImportAsync(exportPath, importRootPath);

            // Assert
            Assert.IsNotNull(importedCacheId);
            Assert.IsNotEmpty(importedCacheId);

            var importedManifestPath = Path.Combine(importRootPath, "AvatarCache", importedCacheId, "manifest.json");
            Assert.IsTrue(File.Exists(importedManifestPath), "manifest.jsonがインポートされるべき");

            Debug.Log($"[Phase9Test] 難読化インポート成功: {importedCacheId}");
        });

        [UnityTest]
        public IEnumerator インポート_非難読化ファイルも引き続きインポートできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - 後方互換性テスト
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", "Legacy.avatarcache");

            // Create cache structure
            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash
            };
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), JsonUtility.ToJson(manifest));
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));

            // Export WITHOUT obfuscation (legacy format)
            await AvatarCacheExporter.ExportAsync(hash, exportPath, obfuscate: false);

            // Clean import target
            var importRootPath = Path.Combine(TestCacheDirectory, "ImportLegacyTest");
            if (Directory.Exists(importRootPath))
            {
                Directory.Delete(importRootPath, true);
            }

            // Act
            var importedCacheId = await AvatarCacheImporter.ImportAsync(exportPath, importRootPath);

            // Assert
            Assert.IsNotNull(importedCacheId);
            Assert.IsNotEmpty(importedCacheId);

            Debug.Log("[Phase9Test] 後方互換性テスト成功（非難読化ファイルのインポート）");
        });

        #endregion

        #region Compatibility Check Tests

        [UnityTest]
        public IEnumerator 互換性チェック_難読化ファイルの互換性を確認できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            // ファイル名をcacheId（hash）と一致させる（難読化キーの整合性のため）
            var exportPath = Path.Combine(TestCacheDirectory, "ExportsCompat", $"{hash}.avatarcache");

            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString()
            };
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), JsonUtility.ToJson(manifest));
            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));

            AvatarCacheExporter.EnableObfuscation = true;
            await AvatarCacheExporter.ExportAsync(hash, exportPath);

            // Act
            var compatibility = AvatarCacheImporter.CheckCompatibility(exportPath);

            // Assert
            Assert.IsTrue(compatibility.isCompatible, "互換性があるべき");

            Debug.Log($"[Phase9Test] 難読化ファイル互換性チェック成功: version={compatibility.cacheFormatVersion}");
        });

        #endregion
    }
}
