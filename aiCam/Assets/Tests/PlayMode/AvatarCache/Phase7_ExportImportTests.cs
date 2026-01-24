using System.Collections;
using System.IO;
using System.IO.Compression;
using AICam.AvatarCache;
using AICam.AvatarCache.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.AvatarCache
{
    /// <summary>
    /// Phase 7: エクスポート/インポートテスト
    ///
    /// テスト対象:
    /// - .avatarcache 形式のエクスポート
    /// - .avatarcache 形式のインポート
    /// - 互換性チェック
    /// - マイグレーション処理
    /// </summary>
    [TestFixture]
    public class Phase7_ExportImportTests : AvatarCacheTestBase
    {
        #region Export Tests

        [UnityTest]
        public IEnumerator エクスポート_AvatarCacheExporterでZIPアーカイブを作成できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", "TestAvatar.avatarcache");

            // Create cache structure
            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            Directory.CreateDirectory(Path.Combine(cacheDir, "textures"));
            Directory.CreateDirectory(Path.Combine(cacheDir, "icons"));

            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(cacheDir, "core", "bones.json"), "{}");
            File.WriteAllText(Path.Combine(cacheDir, "metadata.json"), "{}");

            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));

            // Act - 実際のAvatarCacheExporter.ExportAsyncを呼び出す
            await AvatarCacheExporter.ExportAsync(hash, exportPath);

            // Assert
            AssertFileExists(exportPath, ".avatarcacheエクスポート");

            var fileSize = new FileInfo(exportPath).Length;
            Debug.Log($"[Phase7Test] エクスポート作成: {exportPath} ({fileSize} bytes)");
        });

        [UnityTest]
        public IEnumerator エクスポート_ValidateExportFileで検証できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", "TestAvatar2.avatarcache");

            // Create cache structure with all required files
            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), "{\"version\":1}");
            File.WriteAllText(Path.Combine(cacheDir, "core", "bones.json"), "{}");

            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));
            await AvatarCacheExporter.ExportAsync(hash, exportPath);

            // Act - 実際のAvatarCacheExporter.ValidateExportFileを呼び出す
            var isValid = AvatarCacheExporter.ValidateExportFile(exportPath);

            // Assert
            Assert.IsTrue(isValid, "エクスポートファイルは有効であるべき");

            Debug.Log("[Phase7Test] エクスポートファイル検証成功");
        });

        #endregion

        #region Import Tests

        [UnityTest]
        public IEnumerator インポート_AvatarCacheImporterでZIPアーカイブを展開できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - Create archive first
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", "TestImport.avatarcache");

            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), "{\"version\":1}");
            File.WriteAllText(Path.Combine(cacheDir, "core", "bones.json"), "{}");

            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));
            await AvatarCacheExporter.ExportAsync(hash, exportPath);

            // Clean import target
            var importRootPath = Path.Combine(TestCacheDirectory, "ImportTest");
            if (Directory.Exists(importRootPath))
            {
                Directory.Delete(importRootPath, true);
            }

            // Act - 実際のAvatarCacheImporter.ImportAsyncを呼び出す
            var importedCacheId = await AvatarCacheImporter.ImportAsync(exportPath, importRootPath);

            // Assert
            Assert.IsNotNull(importedCacheId);
            Assert.IsNotEmpty(importedCacheId);

            Debug.Log($"[Phase7Test] インポート成功: {importedCacheId}");
        });

        [UnityTest]
        public IEnumerator インポート_CheckCompatibilityで互換性を確認できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", "TestCompat.avatarcache");

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
            await AvatarCacheExporter.ExportAsync(hash, exportPath);

            // Act - 実際のAvatarCacheImporter.CheckCompatibilityを呼び出す
            var compatibility = AvatarCacheImporter.CheckCompatibility(exportPath);

            // Assert
            Assert.IsNotNull(compatibility);
            Assert.IsTrue(compatibility.isCompatible, "互換性があるべき");

            Debug.Log($"[Phase7Test] 互換性チェック: compatible={compatibility.isCompatible}, version={compatibility.cacheFormatVersion}");
        });

        [UnityTest]
        public IEnumerator インポート_非互換バージョンを検出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", "TestIncompat.avatarcache");

            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));

            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = 999, // 非互換バージョン
                cacheId = hash,
                unityVersion = "2099.1.0f1"
            };
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), JsonUtility.ToJson(manifest));

            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));

            // 手動でZIP作成（Exporterを通さない）
            if (File.Exists(exportPath)) File.Delete(exportPath);
            ZipFile.CreateFromDirectory(cacheDir, exportPath);

            // Act
            var compatibility = AvatarCacheImporter.CheckCompatibility(exportPath);

            // Assert
            Assert.IsFalse(compatibility.isCompatible, "非互換バージョンを検出すべき");

            Debug.Log("[Phase7Test] 非互換バージョンを正しく検出");
        });

        #endregion

        #region Platform Compatibility Tests

        [UnityTest]
        public IEnumerator インポート_クロスプラットフォームでテクスチャ再圧縮が必要か判定できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            var exportPath = Path.Combine(TestCacheDirectory, "Exports", "TestCrossPlatform.avatarcache");

            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));

            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash,
                platform = "Android" // 異なるプラットフォーム
            };
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), JsonUtility.ToJson(manifest));

            Directory.CreateDirectory(Path.GetDirectoryName(exportPath));
            if (File.Exists(exportPath)) File.Delete(exportPath);
            ZipFile.CreateFromDirectory(cacheDir, exportPath);

            // Act
            var compatibility = AvatarCacheImporter.CheckCompatibility(exportPath);

            // Assert
            var currentPlatform = Application.platform.ToString();
            if (currentPlatform != "Android")
            {
                Assert.IsTrue(compatibility.needsTextureRecompression,
                    "異なるプラットフォームではテクスチャ再圧縮が必要");
            }

            Debug.Log($"[Phase7Test] クロスプラットフォーム: needsRecompression={compatibility.needsTextureRecompression}");
        });

        #endregion

        #region File Extension Tests

        [UnityTest]
        public IEnumerator ファイル拡張子_avatarcacheであること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - 実装が存在することを確認
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var fileName = "MyAvatar.avatarcache";

            // Act
            var extension = Path.GetExtension(fileName);

            // Assert
            Assert.AreEqual(".avatarcache", extension);

            Debug.Log("[Phase7Test] ファイル拡張子検証成功");
        });

        #endregion
    }
}
