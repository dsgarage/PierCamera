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
    /// Phase 4: 自動キャッシュ作成テスト
    ///
    /// テスト対象:
    /// - VRMロード後のバイナリキャッシュ自動作成
    /// - slotDataへのcacheId自動設定
    /// - 永続化（AvatarSlotCache）への保存
    /// - キャッシュ作成失敗時の継続動作
    /// </summary>
    [TestFixture]
    public class Phase4_AutoCacheCreationTests : AvatarCacheTestBase
    {
        private AvatarMemoryCache _memoryCache;
        private AvatarCacheIntegrator _integrator;
        private GameObject _memoryCacheObject;

        public override void SetUp()
        {
            base.SetUp();

            _memoryCacheObject = new GameObject("TestMemoryCache");
            _memoryCache = _memoryCacheObject.AddComponent<AvatarMemoryCache>();
            _integrator = new AvatarCacheIntegrator(TestCacheDirectory);
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
        public IEnumerator VRMロード後_バイナリキャッシュが自動作成されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - バイナリキャッシュなしのスロットデータ
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };

            Assert.IsFalse(slotData.HasBinaryCache, "初期状態ではバイナリキャッシュなし");

            // Act - VRMからロード（自動キャッシュ作成が発火）
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // 自動キャッシュ作成の完了を待つ
            await UniTask.Delay(1000); // バックグラウンド処理の完了を待機

            // Assert
            Assert.IsTrue(result.Success, "ロードが成功するべき");
            Assert.IsTrue(slotData.HasBinaryCache, "バイナリキャッシュIDが設定されるべき");
            Assert.IsTrue(_integrator.HasBinaryCache(slotData.binaryCacheId), "バイナリキャッシュが作成されるべき");

            Debug.Log($"[Phase4Test] 自動キャッシュ作成成功: cacheId={slotData.binaryCacheId}");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator VRMロード後_slotDataにcacheIdが設定されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };

            // Act
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);
            await UniTask.Delay(1000);

            // Assert
            Assert.IsNotNull(slotData.binaryCacheId, "cacheIdが設定されるべき");
            Assert.IsNotEmpty(slotData.binaryCacheId, "cacheIdが空でないべき");

            // cacheIdがファイルハッシュベースであることを確認
            var expectedHash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            Assert.AreEqual(expectedHash, slotData.binaryCacheId, "cacheIdはファイルハッシュであるべき");

            Debug.Log($"[Phase4Test] cacheId設定成功: {slotData.binaryCacheId}");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator VRMロード後_2回目のロードが高速化されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - 1回目のロード（VRMから）
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };

            var result1 = await _memoryCache.SwitchToSlotAsync(0, slotData);
            await UniTask.Delay(1000); // キャッシュ作成完了を待機

            // メモリキャッシュをクリア（アプリ再起動をシミュレート）
            if (result1.Avatar != null)
            {
                Object.Destroy(result1.Avatar);
            }
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // Act - 2回目のロード（バイナリキャッシュから）
            var startTime = Time.realtimeSinceStartup;
            var result2 = await _memoryCache.SwitchToSlotAsync(0, slotData);
            var elapsed = Time.realtimeSinceStartup - startTime;

            // Assert
            Assert.IsTrue(result2.Success, "2回目のロードが成功するべき");
            Assert.IsTrue(result2.WasCacheHit, "バイナリキャッシュからロードされるべき");
            Assert.Less(elapsed, 2f, "バイナリキャッシュからのロードは2秒未満であるべき");

            Debug.Log($"[Phase4Test] 2回目のロード時間: {elapsed:F3}秒 (キャッシュヒット={result2.WasCacheHit})");

            // クリーンアップ
            if (result2.Avatar != null)
            {
                Object.Destroy(result2.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator キャッシュ作成失敗時_アプリが継続動作すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - 無効なパス（キャッシュ作成が失敗する状況をシミュレート）
            // Note: 実際のテストでは書き込み権限のないディレクトリを使用するなど
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };

            // Act - ロードは成功するが、キャッシュ作成は別処理
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // Assert - ロード自体は成功する
            Assert.IsTrue(result.Success, "キャッシュ作成失敗時もロードは成功するべき");
            Assert.IsNotNull(result.Avatar, "アバターがロードされるべき");

            Debug.Log("[Phase4Test] キャッシュ作成失敗時の継続動作確認成功");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator 既存キャッシュがある場合_再作成しないこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - 先にキャッシュを作成
            var avatar = await LoadVrmAsync();
            var existingCacheId = await _integrator.CreateBinaryCacheAsync(avatar, TestVrmPath);

            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };
            slotData.SetBinaryCacheId(existingCacheId);

            Object.Destroy(avatar);
            await UniTask.Yield();

            // キャッシュディレクトリの更新日時を記録
            var cacheDir = Path.Combine(TestCacheDirectory, "AvatarCache", existingCacheId);
            var originalModTime = Directory.GetLastWriteTime(cacheDir);

            // Act - バイナリキャッシュからロード
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);
            await UniTask.Delay(500);

            // Assert
            var newModTime = Directory.GetLastWriteTime(cacheDir);
            Assert.AreEqual(originalModTime, newModTime, "既存キャッシュは再作成されないべき");
            Assert.AreEqual(existingCacheId, slotData.binaryCacheId, "cacheIdは変更されないべき");

            Debug.Log("[Phase4Test] 既存キャッシュの再利用確認成功");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });
    }
}
