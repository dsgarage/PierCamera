using System;
using System.Collections;
using System.IO;
using System.Reflection;
using AICam.AvatarCache;
using AICam.Tests.PlayMode.AvatarCache;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRM;
using UniGLTF;

namespace AICam.Tests.PlayMode.Integration
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
        private TestAvatarLoader _testLoader;

        /// <summary>
        /// テスト用のIAvatarLoader実装
        /// VRMファイルを実際にロードする
        /// </summary>
        private class TestAvatarLoader : IAvatarLoader
        {
            private RuntimeGltfInstance _loadedInstance;

            public async UniTask<AvatarLoadResult> LoadAsync(string filePath, Transform parent, Action<float> onProgress = null)
            {
                try
                {
                    if (!File.Exists(filePath))
                    {
                        return AvatarLoadResult.Failed($"File not found: {filePath}");
                    }

                    onProgress?.Invoke(10f);

                    var bytes = await File.ReadAllBytesAsync(filePath);
                    onProgress?.Invoke(30f);

                    _loadedInstance = await VrmUtility.LoadBytesAsync(
                        path: Path.GetFileName(filePath),
                        bytes: bytes,
                        awaitCaller: new RuntimeOnlyAwaitCaller()
                    );

                    onProgress?.Invoke(90f);

                    _loadedInstance.EnableUpdateWhenOffscreen();
                    _loadedInstance.ShowMeshes();

                    var avatar = _loadedInstance.Root;
                    if (parent != null)
                    {
                        avatar.transform.SetParent(parent);
                    }

                    onProgress?.Invoke(100f);

                    Debug.Log($"[TestAvatarLoader] VRM loaded: {avatar.name}");
                    return AvatarLoadResult.Succeeded(avatar);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[TestAvatarLoader] Load failed: {e.Message}");
                    return AvatarLoadResult.Failed(e.Message);
                }
            }

            public void ClearCurrentModel()
            {
                if (_loadedInstance != null)
                {
                    _loadedInstance.Dispose();
                    _loadedInstance = null;
                }
            }
        }

        public override void SetUp()
        {
            base.SetUp();

            // シングルトンInstanceをリセット（リフレクションで強制クリア）
            var instanceField = typeof(AvatarMemoryCache).GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            if (instanceField != null && instanceField.CanWrite)
            {
                instanceField.SetValue(null, null);
            }
            else
            {
                // プロパティに直接セットできない場合、バッキングフィールドを探す
                var backingField = typeof(AvatarMemoryCache).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
                if (backingField != null)
                {
                    backingField.SetValue(null, null);
                }
            }

            // AvatarMemoryCacheのセットアップ
            _memoryCacheObject = new GameObject("TestMemoryCache");
            _memoryCache = _memoryCacheObject.AddComponent<AvatarMemoryCache>();

            // テスト用ローダーを作成して設定
            _testLoader = new TestAvatarLoader();
            _memoryCache.SetLoader(_testLoader);

            // AvatarCacheIntegratorのセットアップ
            _integrator = new AvatarCacheIntegrator(TestCacheDirectory);

            // メモリキャッシュにインテグレーターを設定
            _memoryCache.SetCacheIntegrator(_integrator);
        }

        public override void TearDown()
        {
            // テストローダーをクリーンアップ
            _testLoader?.ClearCurrentModel();
            _testLoader = null;

            if (_memoryCacheObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_memoryCacheObject);
            }
            _memoryCache = null;
            _integrator = null;

            // シングルトンInstanceをリセット
            var backingField = typeof(AvatarMemoryCache).GetField("<Instance>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static);
            if (backingField != null)
            {
                backingField.SetValue(null, null);
            }

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
            UnityEngine.Object.Destroy(originalAvatar);
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
                UnityEngine.Object.Destroy(result.Avatar);
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
                UnityEngine.Object.Destroy(result.Avatar);
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
                UnityEngine.Object.Destroy(result.Avatar);
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

            UnityEngine.Object.Destroy(originalAvatar);
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
                UnityEngine.Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator SwitchToSlotAsync_バイナリキャッシュからロード時にZ負方向を向くこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - VRMをロードしてバイナリキャッシュを作成
            var originalAvatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(originalAvatar, TestVrmPath);

            // スロットデータを作成（保存されたトランスフォームなし）
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };
            slotData.SetBinaryCacheId(cacheId);
            // lastTransformは設定しない（HasSavedTransform = false）

            // 元のアバターを破棄（メモリキャッシュをクリア）
            UnityEngine.Object.Destroy(originalAvatar);
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // Act
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // Assert
            Assert.IsTrue(result.Success, "ロードが成功するべき");
            Assert.IsNotNull(result.Avatar, "アバターがロードされるべき");

            // 回転を検証: Y軸180度回転（Z-方向を向く）
            var rotation = result.Avatar.transform.rotation;
            var expectedRotation = Quaternion.Euler(0, 180, 0);

            // Quaternionの比較は角度差で行う（浮動小数点誤差対策）
            float angleDifference = Quaternion.Angle(rotation, expectedRotation);
            Assert.Less(angleDifference, 1f, $"アバターはZ-方向（Y軸180度）を向くべき。実際の回転: {rotation.eulerAngles}, 期待: {expectedRotation.eulerAngles}");

            Debug.Log($"[Phase3Test] バイナリキャッシュからロード時の回転: {rotation.eulerAngles}");

            // クリーンアップ
            if (result.Avatar != null)
            {
                UnityEngine.Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator SwitchToSlotAsync_保存された回転がidentityの場合もZ負方向を向くこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - VRMをロードしてバイナリキャッシュを作成
            var originalAvatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(originalAvatar, TestVrmPath);

            // スロットデータを作成（identity回転が保存されている状態をシミュレート）
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };
            slotData.SetBinaryCacheId(cacheId);

            // identity回転でトランスフォームを保存（旧バージョンで保存された状態をシミュレート）
            originalAvatar.transform.position = new Vector3(1, 0, 2);
            originalAvatar.transform.rotation = Quaternion.identity; // identity回転
            originalAvatar.transform.localScale = Vector3.one;
            slotData.SaveTransform(originalAvatar.transform);

            // 元のアバターを破棄（メモリキャッシュをクリア）
            UnityEngine.Object.Destroy(originalAvatar);
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // Act
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // Assert
            Assert.IsTrue(result.Success, "ロードが成功するべき");
            Assert.IsNotNull(result.Avatar, "アバターがロードされるべき");

            // 位置は復元されるべき
            Assert.AreEqual(1f, result.Avatar.transform.position.x, 0.01f, "X位置が復元されるべき");
            Assert.AreEqual(2f, result.Avatar.transform.position.z, 0.01f, "Z位置が復元されるべき");

            // 回転を検証: identity回転が保存されていても、Y軸180度回転に補正されるべき
            var rotation = result.Avatar.transform.rotation;
            var expectedRotation = Quaternion.Euler(0, 180, 0);

            float angleDifference = Quaternion.Angle(rotation, expectedRotation);
            Assert.Less(angleDifference, 1f, $"identity回転が保存されていても、Z-方向（Y軸180度）を向くべき。実際の回転: {rotation.eulerAngles}, 期待: {expectedRotation.eulerAngles}");

            Debug.Log($"[Phase3Test] identity回転保存時のロード後回転: {rotation.eulerAngles}");

            // クリーンアップ
            if (result.Avatar != null)
            {
                UnityEngine.Object.Destroy(result.Avatar);
            }
        });

        [UnityTest]
        public IEnumerator SwitchToSlotAsync_ユーザー設定の回転は維持されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange - VRMをロードしてバイナリキャッシュを作成
            var originalAvatar = await LoadVrmAsync();
            var cacheId = await _integrator.CreateBinaryCacheAsync(originalAvatar, TestVrmPath);

            // スロットデータを作成（ユーザーが設定した回転が保存されている状態）
            var slotData = new AvatarSlotData(0)
            {
                modelFilePath = TestVrmPath
            };
            slotData.SetBinaryCacheId(cacheId);

            // ユーザーが設定した回転（例: 45度回転）を保存
            var userRotation = Quaternion.Euler(0, 45, 0);
            originalAvatar.transform.position = new Vector3(1, 0, 2);
            originalAvatar.transform.rotation = userRotation;
            originalAvatar.transform.localScale = Vector3.one;
            slotData.SaveTransform(originalAvatar.transform);

            // 元のアバターを破棄（メモリキャッシュをクリア）
            UnityEngine.Object.Destroy(originalAvatar);
            await UniTask.Yield();
            _memoryCache.ClearAll();

            // Act
            var result = await _memoryCache.SwitchToSlotAsync(0, slotData);

            // Assert
            Assert.IsTrue(result.Success, "ロードが成功するべき");
            Assert.IsNotNull(result.Avatar, "アバターがロードされるべき");

            // ユーザーが設定した回転は維持されるべき
            var rotation = result.Avatar.transform.rotation;
            float angleDifference = Quaternion.Angle(rotation, userRotation);
            Assert.Less(angleDifference, 1f, $"ユーザー設定の回転は維持されるべき。実際の回転: {rotation.eulerAngles}, 期待: {userRotation.eulerAngles}");

            Debug.Log($"[Phase3Test] ユーザー設定回転のロード後: {rotation.eulerAngles}");

            // クリーンアップ
            if (result.Avatar != null)
            {
                UnityEngine.Object.Destroy(result.Avatar);
            }
        });
    }
}
