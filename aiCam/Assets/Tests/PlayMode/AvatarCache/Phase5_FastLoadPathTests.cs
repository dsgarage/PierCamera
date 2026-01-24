using System.Collections;
using System.Diagnostics;
using System.IO;
using AICam.AvatarCache;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Debug = UnityEngine.Debug;

namespace AICam.Tests.PlayMode.AvatarCache
{
    /// <summary>
    /// Phase 5: 高速ロードパステスト
    ///
    /// テスト対象:
    /// - キャッシュからの即時復元
    /// - ロード時間の計測と検証
    /// - キャッシュ存在チェック
    /// - フォールバック処理
    /// </summary>
    [TestFixture]
    public class Phase5_FastLoadPathTests : AvatarCacheTestBase
    {
        #region Cache Existence Tests

        [UnityTest]
        public IEnumerator キャッシュ存在確認_AvatarCacheManagerで検出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            var manifestPath = Path.Combine(cacheDir, "manifest.json");

            // Create cache
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(manifestPath, "{}");

            // Act - 実際のAvatarCacheManager.CacheExistsを呼び出す
            var cacheExists = cacheManager.CacheExists(hash);

            // Assert
            Assert.IsTrue(cacheExists, "既存キャッシュを検出すべき");

            Debug.Log("[Phase5Test] キャッシュ存在確認テスト成功");
        });

        [UnityTest]
        public IEnumerator キャッシュ存在確認_キャッシュなしを検出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);

            // Ensure cache doesn't exist
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }

            // Act - 実際のAvatarCacheManager.CacheExistsを呼び出す
            var cacheExists = cacheManager.CacheExists(hash);

            // Assert
            Assert.IsFalse(cacheExists, "キャッシュなしを検出すべき");

            Debug.Log("[Phase5Test] キャッシュなし検出テスト成功");
        });

        #endregion

        #region Cache Validity Tests

        [UnityTest]
        public IEnumerator キャッシュ有効性_IsCacheValidで検証できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);

            // Create valid cache structure
            Directory.CreateDirectory(cacheDir);
            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash
            };
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"),
                JsonUtility.ToJson(manifest));

            // Act - 実際のAvatarCacheManager.IsCacheValidを呼び出す
            var isValid = cacheManager.IsCacheValid(hash);

            // Assert
            Assert.IsTrue(isValid, "有効なキャッシュが検出されるべき");

            Debug.Log("[Phase5Test] キャッシュ有効性検証成功");
        });

        [UnityTest]
        public IEnumerator キャッシュ有効性_古いバージョンは無効と判定されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);

            // Create cache with old version
            Directory.CreateDirectory(cacheDir);
            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = 999, // 無効なバージョン
                cacheId = hash
            };
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"),
                JsonUtility.ToJson(manifest));

            // Act - 実際のAvatarCacheManager.IsCacheValidを呼び出す
            var isValid = cacheManager.IsCacheValid(hash);

            // Assert
            Assert.IsFalse(isValid, "古いバージョンのキャッシュは無効であるべき");

            Debug.Log("[Phase5Test] 古いバージョン拒否テスト成功");
        });

        #endregion

        #region Load Time Tests

        [UnityTest]
        public IEnumerator フルロード_ロード時間を計測できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - 実装が存在することを確認
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            var sw = Stopwatch.StartNew();

            // Act
            var avatar = await LoadVrmAsync();

            sw.Stop();

            // Assert
            Assert.IsNotNull(avatar);
            Debug.Log($"[Phase5Test] VRMフルロード時間: {sw.ElapsedMilliseconds}ms, cacheId: {hash}");

            // VRMロードは通常3-8秒かかる - このテストでは計測のみ
        });

        [UnityTest]
        public IEnumerator キャッシュロード_AvatarCacheManagerでロードできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var avatar = await LoadVrmAsync();

            // キャッシュを作成
            await cacheManager.CreateCacheAsync(TestVrmPath, avatar);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act - キャッシュからロード時間を計測
            var sw = Stopwatch.StartNew();
            var loadedAvatar = await cacheManager.LoadFromCacheAsync(hash);
            sw.Stop();

            // Assert
            Assert.IsNotNull(loadedAvatar);
            Debug.Log($"[Phase5Test] キャッシュロード時間: {sw.ElapsedMilliseconds}ms");

            // クリーンアップ
            Object.Destroy(loadedAvatar);
        });

        #endregion

        #region Fallback Tests

        [UnityTest]
        public IEnumerator フォールバック_無効キャッシュで例外が発生すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var fakeHash = "nonexistent_hash_12345";

            // Act & Assert
            bool correctExceptionThrown = false;
            try
            {
                await cacheManager.LoadFromCacheAsync(fakeHash);
            }
            catch (System.NotImplementedException)
            {
                // NotImplementedExceptionは実装前のスタブなのでテスト失敗とする
                throw;
            }
            catch (System.IO.FileNotFoundException)
            {
                // 実装後はFileNotFoundExceptionまたはカスタム例外を期待
                correctExceptionThrown = true;
            }
            catch (System.InvalidOperationException)
            {
                // または InvalidOperationException
                correctExceptionThrown = true;
            }

            Assert.IsTrue(correctExceptionThrown, "存在しないキャッシュからのロードはFileNotFoundExceptionまたはInvalidOperationExceptionを投げるべき");

            Debug.Log("[Phase5Test] フォールバックテスト成功");
        });

        #endregion

        #region Load Path Selection Tests

        [UnityTest]
        public IEnumerator ロードパス選択_キャッシュ利用可能時にキャッシュを使用すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);

            Directory.CreateDirectory(cacheDir);
            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash
            };
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"),
                JsonUtility.ToJson(manifest));

            // Act - ロードパス選択
            var shouldUseCache = cacheManager.IsCacheValid(hash);

            // Assert
            Assert.IsTrue(shouldUseCache, "キャッシュパスを選択すべき");
            Debug.Log("[Phase5Test] キャッシュパスを選択");
        });

        [UnityTest]
        public IEnumerator ロードパス選択_キャッシュ利用不可時にソースを選択すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);

            // キャッシュを削除
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, true);
            }

            // Act - ロードパス選択
            var cacheValid = cacheManager.IsCacheValid(hash);
            var sourceExists = File.Exists(TestVrmPath);
            var shouldUseSource = !cacheValid && sourceExists;

            // Assert
            Assert.IsTrue(shouldUseSource, "ソースパスを選択すべき");
            Debug.Log("[Phase5Test] ソースパスを選択（キャッシュ利用不可）");
        });

        #endregion

        #region Cache Deletion Tests

        [UnityTest]
        public IEnumerator キャッシュ削除_AvatarCacheManagerで削除できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);

            // Create cache
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), "{}");

            Assert.IsTrue(cacheManager.CacheExists(hash), "削除前にキャッシュが存在すべき");

            // Act - 実際のAvatarCacheManager.DeleteCacheを呼び出す
            cacheManager.DeleteCache(hash);

            // Assert
            Assert.IsFalse(cacheManager.CacheExists(hash), "削除後にキャッシュは存在しないべき");

            Debug.Log("[Phase5Test] キャッシュ削除成功");
        });

        #endregion
    }
}
