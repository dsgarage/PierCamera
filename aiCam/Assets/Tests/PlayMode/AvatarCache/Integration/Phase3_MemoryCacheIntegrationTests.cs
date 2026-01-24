using System.Collections;
using System.IO;
using AICam.AvatarCache;
using AICam.FBXLoader;
using AICam.Tests.PlayMode.AvatarCache;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.AvatarCache.Integration
{
    /// <summary>
    /// Phase 3: AvatarMemoryCache統合テスト
    ///
    /// テスト対象:
    /// - SwitchToSlotAsync でバイナリキャッシュを優先使用
    /// - バイナリキャッシュ破損時のフォールバック
    /// - メモリキャッシュとバイナリキャッシュの優先順位
    /// </summary>
    [TestFixture]
    public class Phase3_MemoryCacheIntegrationTests : AvatarCacheTestBase
    {
        private AvatarMemoryCache _memoryCache;
        private AvatarCacheIntegrator _integrator;
        private GameObject _memoryCacheObject;

        public override void SetUp()
        {
            base.SetUp();

            // AvatarMemoryCacheのセットアップ
            _memoryCacheObject = new GameObject("TestMemoryCache");
            _memoryCache = _memoryCacheObject.AddComponent<AvatarMemoryCache>();

            // AvatarCacheIntegratorのセットアップ
            _integrator = new AvatarCacheIntegrator(TestCacheDirectory);

            // メモリキャッシュにインテグレーターを設定
            _memoryCache.SetCacheIntegrator(_integrator);
        }

        public override void TearDown()
        {
            if (_memoryCacheObject != null)
            {
                Object.Destroy(_memoryCacheObject);
            }
            _memoryCache = null;
            _integrator = null;

            base.TearDown();
        }

        [UnityTest]
        public IEnumerator SwitchToSlotAsync_バイナリキャッシュがある場合に高速ロードされること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - VRMをロードしてバイナリキャッシュを作成
            var originalAvatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(originalAvatar, TestVrmPath);

            // スロットデータを作成
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };
            slotData.SetBinaryCacheId(cacheId);

            // 元のアバターを破棄（メモリキャッシュをクリア）
            Object.Destroy(originalAvatar);
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // Act
            var startTime = Time.realtimeSinceStartup;
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);
            var elapsed = Time.realtimeSinceStartup - startTime;

            // Assert
            Assert.IsTrue(result.Success, "ロードが成功するべき");
            Assert.IsTrue(result.WasCacheHit, "バイナリキャッシュヒットであるべき");
            Assert.IsNotNull(result.Avatar, "アバターがロードされるべき");

            Debug.Log($"[Phase3Test] バイナリキャッシュからのロード時間: {elapsed:F3}秒");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator SwitchToSlotAsync_バイナリキャッシュがない場合にVRMからロードされること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - バイナリキャッシュなしのスロットデータ
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
                // binaryCacheIdは設定しない
            };

            // Act
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // Assert
            Assert.IsTrue(result.Success, "ロードが成功するべき");
            Assert.IsFalse(result.WasCacheHit, "キャッシュヒットではないべき");
            Assert.IsNotNull(result.Avatar, "アバターがロードされるべき");

            Debug.Log("[Phase3Test] VRMからのフルロード成功");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator SwitchToSlotAsync_バイナリキャッシュ破損時にVRMフォールバックすること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - 無効なキャッシュIDを設定
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };
            slotData.SetBinaryCacheId("invalid-corrupted-cache-id");

            // Act
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // Assert
            Assert.IsTrue(result.Success, "フォールバックでロードが成功するべき");
            Assert.IsFalse(result.WasCacheHit, "キャッシュヒットではないべき（フォールバック）");
            Assert.IsNotNull(result.Avatar, "アバターがロードされるべき");

            Debug.Log("[Phase3Test] バイナリキャッシュ破損時のフォールバック成功");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator SwitchToSlotAsync_メモリキャッシュが優先されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - VRMをロードしてメモリキャッシュに登録
            var originalAvatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(originalAvatar, TestVrmPath);

            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };
            slotData.SetBinaryCacheId(cacheId);

            // メモリキャッシュに追加
            _memoryCache.CacheAvatar(0, TestVrmPath, originalAvatar, keepActive: false);

            // Act
            var startTime = Time.realtimeSinceStartup;
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);
            var elapsed = Time.realtimeSinceStartup - startTime;

            // Assert
            Assert.IsTrue(result.Success, "ロードが成功するべき");
            Assert.IsTrue(result.WasCacheHit, "キャッシュヒットであるべき");
            Assert.Less(elapsed, 0.1f, "メモリキャッシュからは瞬時にロードされるべき");

            Debug.Log($"[Phase3Test] メモリキャッシュからのロード時間: {elapsed:F3}秒");
        });

        [UnityTest]
        public IEnumerator SwitchToSlotAsync_進捗コールバックが呼ばれること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var originalAvatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(originalAvatar, TestVrmPath);

            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };
            slotData.SetBinaryCacheId(cacheId);

            Object.Destroy(originalAvatar);
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // Act
            float lastProgress = 0;
            int progressCallCount = 0;
            var result = await _memoryCache.SwitchToSlotAsync(
                0,
                slotData,
                onProgress: progress =>
                {
                    lastProgress = progress;
                    progressCallCount++;
                });

            // Assert
            Assert.Greater(progressCallCount, 0, "進捗コールバックが呼ばれるべき");
            Assert.AreEqual(100f, lastProgress, 0.1f, "最終進捗は100%であるべき");

            Debug.Log($"[Phase3Test] 進捗コールバック: {progressCallCount}回呼び出し, 最終={lastProgress}%");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });
    }
}
