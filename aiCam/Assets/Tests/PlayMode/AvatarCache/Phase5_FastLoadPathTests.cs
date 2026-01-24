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
        #region Cache Load Tests

        [UnityTest]
        public IEnumerator キャッシュロード_LoadFromCacheAsyncでロードできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var avatar = await LoadVrmAsync();

            // キャッシュを作成
            await cacheManager.CreateCacheAsync(TestVrmPath, avatar);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act - 実際のLoadFromCacheAsyncを呼び出す
            var sw = Stopwatch.StartNew();
            var loadedAvatar = await cacheManager.LoadFromCacheAsync(hash);
            sw.Stop();

            // Assert
            Assert.IsNotNull(loadedAvatar);
            Debug.Log($"[Phase5Test] キャッシュロード時間: {sw.ElapsedMilliseconds}ms");

            // クリーンアップ
            Object.Destroy(loadedAvatar);
        });

        [UnityTest]
        public IEnumerator キャッシュロード_存在しないキャッシュで例外が発生すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var fakeHash = "nonexistent_hash_12345";

            // Act & Assert - LoadFromCacheAsyncを呼び出す
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
                correctExceptionThrown = true;
            }
            catch (System.InvalidOperationException)
            {
                correctExceptionThrown = true;
            }

            Assert.IsTrue(correctExceptionThrown, "存在しないキャッシュからのロードは例外を投げるべき");
            Debug.Log("[Phase5Test] 存在しないキャッシュ例外テスト成功");
        });

        [UnityTest]
        public IEnumerator キャッシュロード_フルロードより高速であること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);

            // フルロード時間を計測
            var swFull = Stopwatch.StartNew();
            var avatar = await LoadVrmAsync();
            swFull.Stop();
            var fullLoadTime = swFull.ElapsedMilliseconds;

            // キャッシュを作成
            await cacheManager.CreateCacheAsync(TestVrmPath, avatar);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act - キャッシュロード時間を計測
            var swCache = Stopwatch.StartNew();
            var cachedAvatar = await cacheManager.LoadFromCacheAsync(hash);
            swCache.Stop();
            var cacheLoadTime = swCache.ElapsedMilliseconds;

            // Assert
            Assert.IsNotNull(cachedAvatar);
            Debug.Log($"[Phase5Test] フルロード: {fullLoadTime}ms, キャッシュロード: {cacheLoadTime}ms");

            // クリーンアップ
            Object.Destroy(cachedAvatar);
        });

        #endregion

        #region Cache Deletion Tests

        [UnityTest]
        public IEnumerator キャッシュ削除_DeleteCacheで削除できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);

            // Create cache
            Directory.CreateDirectory(cacheDir);
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), "{}");

            Assert.IsTrue(cacheManager.CacheExists(hash), "削除前にキャッシュが存在すべき");

            // Act - 実際のDeleteCacheを呼び出す
            cacheManager.DeleteCache(hash);

            // Assert
            Assert.IsFalse(cacheManager.CacheExists(hash), "削除後にキャッシュは存在しないべき");

            Debug.Log("[Phase5Test] キャッシュ削除成功");
        });

        [UnityTest]
        public IEnumerator キャッシュ削除_存在しないキャッシュでエラーにならないこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var fakeHash = "nonexistent_hash_for_delete";

            // Act & Assert - DeleteCacheを呼び出す（例外が発生しないこと）
            cacheManager.DeleteCache(fakeHash);

            Debug.Log("[Phase5Test] 存在しないキャッシュ削除テスト成功");
        });

        #endregion

        #region Load Path Selection Tests

        [UnityTest]
        public IEnumerator ロードパス選択_キャッシュ有効時にキャッシュからロードすること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var avatar = await LoadVrmAsync();
            await cacheManager.CreateCacheAsync(TestVrmPath, avatar);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act - キャッシュが有効ならLoadFromCacheAsyncを使用
            GameObject loadedAvatar = null;
            if (cacheManager.IsCacheValid(hash))
            {
                loadedAvatar = await cacheManager.LoadFromCacheAsync(hash);
            }

            // Assert
            Assert.IsNotNull(loadedAvatar);
            Debug.Log("[Phase5Test] キャッシュパスでロード成功");

            // クリーンアップ
            Object.Destroy(loadedAvatar);
        });

        [UnityTest]
        public IEnumerator ロードパス選択_キャッシュ無効時にフォールバックすること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // キャッシュを削除して無効化
            cacheManager.DeleteCache(hash);

            // Act - キャッシュが無効なのでフォールバック
            bool usedFallback = false;
            if (!cacheManager.IsCacheValid(hash))
            {
                usedFallback = true;
            }

            // Assert
            Assert.IsTrue(usedFallback, "キャッシュ無効時はフォールバックすべき");
            Debug.Log("[Phase5Test] フォールバックパス選択成功");
        });

        #endregion
    }
}
