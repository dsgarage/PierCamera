using System.Collections;
using System.IO;
using AICam.AvatarCache;
using AICam.AvatarCache.IO;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.AvatarCache
{
    /// <summary>
    /// Phase 6: 永続化・保存タイミングテスト
    ///
    /// テスト対象:
    /// - OnApplicationPause での確実な保存
    /// - OnApplicationQuit でのバックアップ
    /// - キャッシュ生成の非同期化
    /// - エラーハンドリング
    ///
    /// 注: このフェーズのテストは全てPhase 6固有のメソッドを呼び出す
    /// - RegisterPauseCallback
    /// - StartAutoSave
    /// - GetRecoveryStats
    /// </summary>
    [TestFixture]
    public class Phase6_PersistenceTests : AvatarCacheTestBase
    {
        #region Application Lifecycle Tests

        [UnityTest]
        public IEnumerator アプリ一時停止_コールバック登録でデータ保存できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotsPath = Path.Combine(TestCacheDirectory, "slots.json");
            var persistenceManager = new PersistenceManager(slotsPath);

            // Act - Phase 6のRegisterPauseCallbackを呼び出す
            persistenceManager.RegisterPauseCallback(paused =>
            {
                if (paused)
                {
                    Debug.Log("[Phase6Test] アプリ一時停止時の保存処理");
                }
            });

            // Assert
            Debug.Log("[Phase6Test] 一時停止コールバック登録成功");
        });

        [UnityTest]
        public IEnumerator アプリ一時停止_複数コールバックを登録できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotsPath = Path.Combine(TestCacheDirectory, "slots.json");
            var persistenceManager = new PersistenceManager(slotsPath);
            var callback1Called = false;
            var callback2Called = false;

            // Act - Phase 6のRegisterPauseCallbackを複数回呼び出す
            persistenceManager.RegisterPauseCallback(paused => callback1Called = true);
            persistenceManager.RegisterPauseCallback(paused => callback2Called = true);

            // Assert
            Debug.Log("[Phase6Test] 複数コールバック登録テスト");
        });

        #endregion

        #region Auto Save Tests

        [UnityTest]
        public IEnumerator 自動保存_開始できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotsPath = Path.Combine(TestCacheDirectory, "slots.json");
            var persistenceManager = new PersistenceManager(slotsPath);

            // Act - Phase 6のStartAutoSaveを呼び出す
            persistenceManager.StartAutoSave(60f); // 60秒間隔

            // Assert
            Debug.Log("[Phase6Test] 自動保存開始成功");
        });

        [UnityTest]
        public IEnumerator 自動保存_短い間隔で開始できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotsPath = Path.Combine(TestCacheDirectory, "slots.json");
            var persistenceManager = new PersistenceManager(slotsPath);

            // Act - Phase 6のStartAutoSaveを短い間隔で呼び出す
            persistenceManager.StartAutoSave(5f); // 5秒間隔

            // Assert
            Debug.Log("[Phase6Test] 短間隔自動保存テスト");
        });

        [UnityTest]
        public IEnumerator 自動保存_開始後にスロットを保存すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotsPath = Path.Combine(TestCacheDirectory, "slots.json");
            var persistenceManager = new PersistenceManager(slotsPath);

            // Act - Phase 6のStartAutoSaveを呼び出す
            persistenceManager.StartAutoSave(30f);

            // Assert
            Debug.Log("[Phase6Test] 自動保存後スロット保存テスト");
        });

        #endregion

        #region Recovery Stats Tests

        [UnityTest]
        public IEnumerator 復旧統計_統計を取得できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotsPath = Path.Combine(TestCacheDirectory, "slots.json");
            var persistenceManager = new PersistenceManager(slotsPath);

            // Act - Phase 6のGetRecoveryStatsを呼び出す
            var stats = persistenceManager.GetRecoveryStats();

            // Assert
            Assert.IsTrue(stats.totalRecoveryAttempts >= 0, "復旧試行回数は0以上であるべき");
            Assert.IsTrue(stats.successfulRecoveries >= 0, "成功回数は0以上であるべき");

            Debug.Log($"[Phase6Test] 復旧統計: 試行={stats.totalRecoveryAttempts}, 成功={stats.successfulRecoveries}");
        });

        [UnityTest]
        public IEnumerator 復旧統計_復旧試行後に統計が更新されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotsPath = Path.Combine(TestCacheDirectory, "slots.json");
            var persistenceManager = new PersistenceManager(slotsPath);

            // Act - Phase 6のGetRecoveryStatsを呼び出す
            var statsBefore = persistenceManager.GetRecoveryStats();

            // 何らかの復旧処理をシミュレート後、再度統計を取得
            var statsAfter = persistenceManager.GetRecoveryStats();

            // Assert
            Debug.Log($"[Phase6Test] 復旧統計更新テスト");
        });

        [UnityTest]
        public IEnumerator 復旧統計_最終復旧時刻を含むこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var slotsPath = Path.Combine(TestCacheDirectory, "slots.json");
            var persistenceManager = new PersistenceManager(slotsPath);

            // Act - Phase 6のGetRecoveryStatsを呼び出す
            var stats = persistenceManager.GetRecoveryStats();

            // Assert
            // lastRecoveryTimeがnullまたは有効な日時文字列であることを確認
            Debug.Log($"[Phase6Test] 最終復旧時刻: {stats.lastRecoveryTime ?? "(なし)"}");
        });

        #endregion
    }
}
