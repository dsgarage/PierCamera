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
    /// Phase 5: エンドツーエンド統合テスト
    ///
    /// テスト対象:
    /// - 完全なユーザーフロー（初回ロード → キャッシュ作成 → 再ロード）
    /// - アプリ再起動シミュレーション
    /// - 複数スロット間の切り替え
    /// - キャッシュ削除後の再ロード
    /// </summary>
    [TestFixture]
    public class Phase5_EndToEndTests : AvatarCacheTestBase
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
        public IEnumerator E2E_初回VRMロード後にバイナリキャッシュが作成されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };

            // Act - ユーザーがスロットをタップ
            Debug.Log("[E2E] ユーザーがスロット0をタップ（初回）");
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // キャッシュ作成完了を待機
            await UniTask.Delay(2000);

            // Assert
            Assert.IsTrue(result.Success, "初回ロードが成功するべき");
            Assert.IsFalse(result.WasCacheHit, "初回はキャッシュヒットではないべき");
            Assert.IsTrue(slotData.HasBinaryCache, "バイナリキャッシュIDが設定されるべき");
            Assert.IsTrue(_integrator.HasBinaryCache(slotData.binaryCacheId), "バイナリキャッシュが作成されるべき");

            Debug.Log($"[E2E] 初回ロード完了: cacheId={slotData.binaryCacheId}");

            // クリーンアップ
            if (result.Avatar != null)
            {
                Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator E2E_2回目のスロットタップでバイナリキャッシュからロードされること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - 初回ロード
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };

            Debug.Log("[E2E] 初回ロード開始");
            var result1 = await _memoryCache.SwitchToSlotAsync(0, slotData);
            await UniTask.Delay(2000);

            // メモリキャッシュをクリア（他のスロットに切り替えた状態をシミュレート）
            if (result1.Avatar != null)
            {
                Object.Destroy(result1.Avatar);
            }
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // Act - 2回目のタップ
            Debug.Log("[E2E] ユーザーがスロット0を再タップ");
            var startTime = Time.realtimeSinceStartup;
            var result2 = await _memoryCache.SwitchToSlotAsync(0, slotData);
            var loadTime = Time.realtimeSinceStartup - startTime;

            // Assert
            Assert.IsTrue(result2.Success, "2回目のロードが成功するべき");
            Assert.IsTrue(result2.WasCacheHit, "バイナリキャッシュからロードされるべき");
            Assert.Less(loadTime, 2f, "2秒未満でロードされるべき");

            Debug.Log($"[E2E] 2回目のロード時間: {loadTime:F3}秒 (目標: <2秒)");

            // クリーンアップ
            if (result2.Avatar != null)
            {
                Object.Destroy(result2.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator E2E_アプリ再起動シミュレーション後に高速ロードされること() => UniTask.ToCoroutine(async () =>
        {
            // === アプリセッション1: 初回ロード ===
            Debug.Log("[E2E] === セッション1: 初回起動 ===");

            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };

            var result1 = await _memoryCache.SwitchToSlotAsync(0, slotData);
            await UniTask.Delay(2000);

            // スロットデータをJSON保存（永続化シミュレート）
            var savedJson = JsonUtility.ToJson(slotData);
            Debug.Log($"[E2E] スロットデータ保存: {savedJson}");

            // アバターを破棄
            if (result1.Avatar != null)
            {
                Object.Destroy(result1.Avatar);
            }
            await UniTask.Yield();

            // === アプリ再起動シミュレート ===
            Debug.Log("[E2E] === セッション2: アプリ再起動 ===");

            // メモリキャッシュを完全にクリア
            _memoryCache.ClearAll();

            // JSONからスロットデータを復元
            var restoredSlotData = JsonUtility.FromJson<AvatarSlotData>(savedJson);

            // Act - 再起動後のスロットタップ
            var startTime = Time.realtimeSinceStartup;
            var result2 = await _memoryCache.SwitchToSlotAsync(0, restoredSlotData);
            var loadTime = Time.realtimeSinceStartup - startTime;

            // Assert
            Assert.IsTrue(result2.Success, "再起動後のロードが成功するべき");
            Assert.IsTrue(result2.WasCacheHit, "バイナリキャッシュからロードされるべき");
            Assert.Less(loadTime, 2f, "2秒未満でロードされるべき（爆速ロード）");

            Debug.Log($"[E2E] 再起動後のロード時間: {loadTime:F3}秒 (目標: <2秒, 爆速ロード達成!)");

            // クリーンアップ
            if (result2.Avatar != null)
            {
                Object.Destroy(result2.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator E2E_複数スロット間の切り替えが正常に動作すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - スロット0とスロット1を準備
            var slotData0 = new AvatarSlotData(0) { modelFilePath = TestVrmPath };
            var slotData1 = new AvatarSlotData(1) { modelFilePath = TestVrmPath };

            // スロット0をロード
            Debug.Log("[E2E] スロット0をロード");
            var result0 = await _memoryCache.SwitchToSlotAsync(0, slotData0);
            await UniTask.Delay(2000);
            Assert.IsTrue(result0.Success);

            // スロット1に切り替え
            Debug.Log("[E2E] スロット1に切り替え");
            var result1 = await _memoryCache.SwitchToSlotAsync(1, slotData1);
            await UniTask.Delay(2000);
            Assert.IsTrue(result1.Success);

            // スロット0に戻る
            Debug.Log("[E2E] スロット0に戻る");
            var startTime = Time.realtimeSinceStartup;
            var result0Again = await _memoryCache.SwitchToSlotAsync(0, slotData0);
            var loadTime = Time.realtimeSinceStartup - startTime;

            // Assert
            Assert.IsTrue(result0Again.Success, "スロット切り替えが成功するべき");
            Assert.IsTrue(result0Again.WasCacheHit, "メモリまたはバイナリキャッシュからロードされるべき");

            Debug.Log($"[E2E] スロット切り替え時間: {loadTime:F3}秒");

            // クリーンアップ
            _memoryCache.ClearAll();
        });

        [UnityTest]
        public IEnumerator E2E_キャッシュ削除後にVRMから再ロードされること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - キャッシュを作成
            var slotData = new AvatarSlotData(0) { modelFilePath = TestVrmPath };

            var result1 = await _memoryCache.SwitchToSlotAsync(0, slotData);
            await UniTask.Delay(2000);

            var cacheId = slotData.binaryCacheId;
            Assert.IsTrue(_integrator.HasBinaryCache(cacheId), "キャッシュが存在するべき");

            // キャッシュを削除
            Debug.Log("[E2E] バイナリキャッシュを削除");
            _integrator.DeleteBinaryCache(cacheId);
            slotData.ClearBinaryCache();

            // メモリキャッシュもクリア
            if (result1.Avatar != null)
            {
                Object.Destroy(result1.Avatar);
            }
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // Act - 再ロード
            Debug.Log("[E2E] キャッシュ削除後の再ロード");
            var result2 = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // Assert
            Assert.IsTrue(result2.Success, "再ロードが成功するべき");
            Assert.IsFalse(result2.WasCacheHit, "VRMからフルロードされるべき");

            Debug.Log("[E2E] VRMからの再ロード成功");

            // クリーンアップ
            if (result2.Avatar != null)
            {
                Object.Destroy(result2.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator E2E_ロード時間比較_VRM対バイナリキャッシュ() => UniTask.ToCoroutine(async () =>
        {
            // === VRMからのロード時間測定 ===
            var slotData = new AvatarSlotData(0) { modelFilePath = TestVrmPath };

            var vrmStartTime = Time.realtimeSinceStartup;
            var result1 = await _memoryCache.SwitchToSlotAsync(0, slotData);
            var vrmLoadTime = Time.realtimeSinceStartup - vrmStartTime;

            await UniTask.Delay(2000); // キャッシュ作成完了を待機

            // メモリキャッシュをクリア
            if (result1.Avatar != null)
            {
                Object.Destroy(result1.Avatar);
            }
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // === バイナリキャッシュからのロード時間測定 ===
            var cacheStartTime = Time.realtimeSinceStartup;
            var result2 = await _memoryCache.SwitchToSlotAsync(0, slotData);
            var cacheLoadTime = Time.realtimeSinceStartup - cacheStartTime;

            // Assert
            Debug.Log("========================================");
            Debug.Log($"[E2E] ロード時間比較:");
            Debug.Log($"  VRMからのロード:           {vrmLoadTime:F3}秒");
            Debug.Log($"  バイナリキャッシュから:     {cacheLoadTime:F3}秒");
            Debug.Log($"  高速化率:                  {vrmLoadTime / cacheLoadTime:F1}倍");
            Debug.Log("========================================");

            Assert.Less(cacheLoadTime, vrmLoadTime, "バイナリキャッシュの方が高速であるべき");

            // クリーンアップ
            if (result2.Avatar != null)
            {
                Object.Destroy(result2.Avatar);
            }
        });
    }
}
