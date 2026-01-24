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
    /// Phase 1: マニフェスト・スロット分離テスト
    ///
    /// テスト対象:
    /// - SlotsData クラス（スロット情報、参照のみ）
    /// - AvatarCacheManifest クラス（キャッシュマニフェスト）
    /// - ファイルハッシュ計算
    /// - キャッシュディレクトリ管理
    /// </summary>
    [TestFixture]
    public class Phase1_ManifestTests : AvatarCacheTestBase
    {
        #region File Hash Tests

        [UnityTest]
        public IEnumerator ファイルハッシュ_AvatarCacheManagerで計算できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var filePath = TestVrmPath;

            // Act - 実際のAvatarCacheManager.CalculateFileHashを呼び出す
            var hash = AvatarCacheManager.CalculateFileHash(filePath);

            // Assert
            Assert.IsNotNull(hash);
            Assert.IsNotEmpty(hash);
            Assert.AreEqual(64, hash.Length, "SHA256ハッシュは64文字であるべき");

            Debug.Log($"[Phase1Test] ファイルハッシュ: {hash}");
        });

        [UnityTest]
        public IEnumerator ファイルハッシュ_同じファイルで一貫したハッシュが生成されること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var filePath = TestVrmPath;

            // Act
            var hash1 = AvatarCacheManager.CalculateFileHash(filePath);
            var hash2 = AvatarCacheManager.CalculateFileHash(filePath);

            // Assert
            Assert.AreEqual(hash1, hash2, "同じファイルは同じハッシュを生成すべき");

            Debug.Log($"[Phase1Test] 一貫したハッシュ検証成功");
        });

        [UnityTest]
        public IEnumerator ファイルハッシュ_有効な16進数文字列であること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var filePath = TestVrmPath;

            // Act
            var hash = AvatarCacheManager.CalculateFileHash(filePath);

            // Assert
            Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(hash, "^[a-f0-9]+$"),
                "ハッシュは小文字の16進数文字列であるべき");
        });

        #endregion

        #region Cache Directory Tests

        [UnityTest]
        public IEnumerator キャッシュディレクトリ_AvatarCacheManagerで取得できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act - 実際のAvatarCacheManager.GetCacheDirectoryPathを呼び出す
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);

            // Assert
            Assert.IsNotNull(cacheDir);
            Assert.IsTrue(cacheDir.Contains(hash), "キャッシュディレクトリにハッシュが含まれるべき");

            Debug.Log($"[Phase1Test] キャッシュディレクトリパス: {cacheDir}");
        });

        [UnityTest]
        public IEnumerator キャッシュ存在確認_AvatarCacheManagerでチェックできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act - 実際のAvatarCacheManager.CacheExistsを呼び出す
            var exists = cacheManager.CacheExists(hash);

            // Assert
            // キャッシュがまだ作成されていないのでfalseであるべき
            Assert.IsFalse(exists, "キャッシュが存在しないこと");

            Debug.Log($"[Phase1Test] キャッシュ存在確認: {exists}");
        });

        #endregion

        #region Manifest Tests

        [UnityTest]
        public IEnumerator マニフェスト_AvatarCacheManifestをシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            Directory.CreateDirectory(cacheDir);

            // 実際のAvatarCacheManifestクラスを使用
            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash,
                originalFileName = Path.GetFileName(TestVrmPath),
                createdAt = System.DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString()
            };

            var manifestPath = Path.Combine(cacheDir, "manifest.json");

            // Act
            var json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(manifestPath, json);

            // Assert - IsCacheValidを呼び出してマニフェスト読み込みをテスト
            var isValid = cacheManager.IsCacheValid(hash);

            AssertFileExists(manifestPath, "manifest.json");

            var loadedJson = File.ReadAllText(manifestPath);
            var loadedManifest = JsonUtility.FromJson<AvatarCacheManifest>(loadedJson);

            Assert.AreEqual(manifest.cacheFormatVersion, loadedManifest.cacheFormatVersion);
            Assert.AreEqual(manifest.cacheId, loadedManifest.cacheId);
            Assert.AreEqual(manifest.originalFileName, loadedManifest.originalFileName);

            Debug.Log($"[Phase1Test] マニフェスト保存: {manifestPath}");
        });

        [UnityTest]
        public IEnumerator マニフェスト_VRMメタデータを含むこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = cacheManager.GetCacheDirectoryPath(hash);
            Directory.CreateDirectory(cacheDir);

            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = AvatarCacheManager.CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash,
                vrmTitle = "TestAvatar",
                vrmAuthor = "TestAuthor",
                vrmVersion = "1.0"
            };

            // Act
            var json = JsonUtility.ToJson(manifest, true);
            var manifestPath = Path.Combine(cacheDir, "manifest.json");
            File.WriteAllText(manifestPath, json);

            // IsCacheValidを呼び出してマニフェスト読み込みをテスト
            var isValid = cacheManager.IsCacheValid(hash);

            // Assert
            Assert.IsTrue(json.Contains("vrmTitle"));
            Assert.IsTrue(json.Contains("vrmAuthor"));
            Assert.IsTrue(json.Contains("vrmVersion"));

            Debug.Log($"[Phase1Test] VRMメタデータを含むマニフェスト");
        });

        #endregion

        #region Slots Tests

        [UnityTest]
        public IEnumerator スロット_SlotsDataをシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var slotsPath = Path.Combine(TestCacheDirectory, "AvatarSlots", "slots.json");
            Directory.CreateDirectory(Path.GetDirectoryName(slotsPath));

            var persistenceManager = new PersistenceManager(slotsPath);

            // 実際のSlotsDataクラスを使用
            var slots = new SlotsData
            {
                version = 1,
                activeSlotIndex = 0,
                slots = new SlotEntry[]
                {
                    new SlotEntry
                    {
                        slotIndex = 0,
                        cacheId = hash,
                        displayName = "Test Avatar",
                        lastUsedAt = System.DateTime.UtcNow.ToString("o")
                    }
                }
            };

            // Act - PersistenceManagerを使用して保存
            persistenceManager.SaveSlots(slots);

            // Assert - PersistenceManagerを使用してロード
            var loadedSlots = persistenceManager.LoadSlots();

            Assert.AreEqual(slots.version, loadedSlots.version);
            Assert.AreEqual(slots.activeSlotIndex, loadedSlots.activeSlotIndex);
            Assert.AreEqual(1, loadedSlots.slots.Length);
            Assert.AreEqual(hash, loadedSlots.slots[0].cacheId);

            Debug.Log($"[Phase1Test] スロット保存: {slotsPath}");
        });

        [UnityTest]
        public IEnumerator スロット_複数スロットが同じキャッシュを共有できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var slotsPath = Path.Combine(TestCacheDirectory, "AvatarSlots", "slots_shared.json");
            Directory.CreateDirectory(Path.GetDirectoryName(slotsPath));

            var persistenceManager = new PersistenceManager(slotsPath);

            // 実際のSlotEntryクラスを使用
            var slots = new SlotsData
            {
                version = 1,
                activeSlotIndex = 0,
                slots = new SlotEntry[]
                {
                    new SlotEntry
                    {
                        slotIndex = 0,
                        cacheId = hash,
                        displayName = "Avatar Slot 0"
                    },
                    new SlotEntry
                    {
                        slotIndex = 1,
                        cacheId = hash, // 同じキャッシュ
                        displayName = "Avatar Slot 1"
                    }
                }
            };

            // Act - PersistenceManagerを使用して保存・ロード
            persistenceManager.SaveSlots(slots);
            var loadedSlots = persistenceManager.LoadSlots();

            // Assert
            Assert.AreEqual(loadedSlots.slots[0].cacheId, loadedSlots.slots[1].cacheId,
                "複数スロットが同じキャッシュを参照すべき");

            Debug.Log("[Phase1Test] 複数スロットが同じキャッシュを共有可能");
        });

        #endregion

        #region Cache Validity Tests

        [UnityTest]
        public IEnumerator キャッシュ有効性_AvatarCacheManagerで検証できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var cacheManager = new AvatarCacheManager(TestCacheDirectory);
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act - 実際のAvatarCacheManager.IsCacheValidを呼び出す
            var isValid = cacheManager.IsCacheValid(hash);

            // Assert
            // キャッシュがまだ作成されていないので無効であるべき
            Assert.IsFalse(isValid, "存在しないキャッシュは無効であるべき");

            Debug.Log($"[Phase1Test] キャッシュ有効性確認: {isValid}");
        });

        #endregion

        #region VRM Load Integration Test

        [UnityTest]
        public IEnumerator VRMロード_キャッシュが作成できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            Assert.IsNotNull(avatar, "アバターがロードされるべき");

            var cacheManager = new AvatarCacheManager(TestCacheDirectory);

            // Act - 実際のAvatarCacheManager.CreateCacheAsyncを呼び出す
            await cacheManager.CreateCacheAsync(TestVrmPath, avatar);

            // Assert
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            Assert.IsTrue(cacheManager.CacheExists(hash), "キャッシュが作成されるべき");

            Debug.Log($"[Phase1Test] VRMロード完了、キャッシュ作成: {avatar.name}");
        });

        #endregion
    }
}
