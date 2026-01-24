using System.Collections;
using System.IO;
using AICam.AvatarCache;
using AICam.Tests.PlayMode.AvatarCache;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.Integration
{
    /// <summary>
    /// Phase 2: AvatarCacheIntegratorテスト
    ///
    /// テスト対象:
    /// - HasBinaryCache: キャッシュ存在確認
    /// - CreateBinaryCacheAsync: キャッシュ作成
    /// - LoadFromBinaryCacheAsync: キャッシュからロード
    /// - DeleteBinaryCache: キャッシュ削除
    /// </summary>
    [TestFixture]
    public class Phase2_CacheIntegratorTests : AvatarCacheTestBase
    {
        private AvatarCacheIntegrator _integrator;

        public override void SetUp()
        {
            base.SetUp();
            _integrator = new AvatarCacheIntegrator(TestCacheDirectory);
        }

        public override void TearDown()
        {
            _integrator = null;
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator AvatarCacheIntegrator_HasBinaryCacheでキャッシュ存在確認ができること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(avatar, TestVrmPath);

            // Act
            var exists = _integrator.HasBinaryCache(cacheId);
            var notExists = _integrator.HasBinaryCache("non-existent-cache-id");

            // Assert
            Assert.IsTrue(exists, "作成したキャッシュは存在するべき");
            Assert.IsFalse(notExists, "存在しないキャッシュはfalseを返すべき");
            Debug.Log($"[Phase2Test] HasBinaryCache検証成功: cacheId={cacheId}");
        });

        [UnityTest]
        public IEnumerator AvatarCacheIntegrator_CreateBinaryCacheAsyncでキャッシュ作成ができること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();

            // Act
            var cacheId = await _integrator.CreateBinaryCacheAsync(avatar, TestVrmPath);

            // Assert
            Assert.IsNotNull(cacheId);
            Assert.IsNotEmpty(cacheId);
            Assert.IsTrue(_integrator.HasBinaryCache(cacheId));

            // キャッシュディレクトリが作成されていることを確認
            var cacheDir = Path.Combine(TestCacheDirectory, "AvatarCache", cacheId);
            Assert.IsTrue(Directory.Exists(cacheDir), "キャッシュディレクトリが作成されるべき");

            Debug.Log($"[Phase2Test] CreateBinaryCacheAsync成功: {cacheId}");
        });

        [UnityTest]
        public IEnumerator AvatarCacheIntegrator_LoadFromBinaryCacheAsyncでロードができること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var originalAvatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(originalAvatar, TestVrmPath);

            // 元のアバターを破棄
            Object.Destroy(originalAvatar);
            await UniTask.Yield();

            // Act
            float lastProgress = 0;
            var loadedAvatar = await _integrator.LoadFromBinaryCacheAsync(
                cacheId,
                progress => lastProgress = progress);

            // Assert
            Assert.IsNotNull(loadedAvatar, "アバターがロードされるべき");
            Assert.AreEqual(100f, lastProgress, 0.1f, "進捗が100%になるべき");

            // クリーンアップ
            if (loadedAvatar != null)
            {
                Object.Destroy(loadedAvatar);
            }

            Debug.Log("[Phase2Test] LoadFromBinaryCacheAsync成功");
        });

        [UnityTest]
        public IEnumerator AvatarCacheIntegrator_DeleteBinaryCacheで削除ができること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(avatar, TestVrmPath);
            Assert.IsTrue(_integrator.HasBinaryCache(cacheId), "削除前は存在するべき");

            // Act
            _integrator.DeleteBinaryCache(cacheId);

            // Assert
            Assert.IsFalse(_integrator.HasBinaryCache(cacheId), "削除後は存在しないべき");
            Debug.Log("[Phase2Test] DeleteBinaryCache成功");
        });

        [UnityTest]
        public IEnumerator AvatarCacheIntegrator_存在しないキャッシュでnullを返すこと() => UniTask.ToCoroutine(async () =>
        {
            // Act
            var result = await _integrator.LoadFromBinaryCacheAsync("non-existent-cache-id");

            // Assert
            Assert.IsNull(result, "存在しないキャッシュはnullを返すべき");
            Debug.Log("[Phase2Test] 存在しないキャッシュの検証成功");
        });

        [UnityTest]
        public IEnumerator AvatarCacheIntegrator_同じVRMで同じcacheIdが返ること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar1 = await LoadVrmAsync();
            var cacheId1 = await _integrator.CreateBinaryCacheAsync(avatar1, TestVrmPath);

            // 同じパスで再度作成
            var cacheId2 = await _integrator.CreateBinaryCacheAsync(avatar1, TestVrmPath);

            // Assert
            Assert.AreEqual(cacheId1, cacheId2, "同じVRMファイルは同じcacheIdを返すべき");
            Debug.Log($"[Phase2Test] cacheId一貫性検証成功: {cacheId1}");
        });
    }
}
