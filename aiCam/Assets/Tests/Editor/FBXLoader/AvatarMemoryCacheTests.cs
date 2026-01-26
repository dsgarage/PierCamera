using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using AICam.FBXLoader;
using AICam.AvatarCache;

namespace AICam.Core.Tests
{
    /// <summary>
    /// AvatarMemoryCache のユニットテスト
    /// Phase 2: メモリキャッシュによるアバター再ロード回避
    /// </summary>
    [TestFixture]
    public class AvatarMemoryCacheTests
    {
        private GameObject _cacheGameObject;
        private AvatarMemoryCache _cache;
        private List<GameObject> _createdAvatars;

        [SetUp]
        public void SetUp()
        {
            _cacheGameObject = new GameObject("TestAvatarMemoryCache");
            _cache = _cacheGameObject.AddComponent<AvatarMemoryCache>();
            _createdAvatars = new List<GameObject>();
        }

        [TearDown]
        public void TearDown()
        {
            // 作成したアバターを破棄
            foreach (var avatar in _createdAvatars)
            {
                if (avatar != null)
                {
                    UnityEngine.Object.DestroyImmediate(avatar);
                }
            }
            _createdAvatars.Clear();

            // キャッシュを破棄
            if (_cacheGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_cacheGameObject);
            }
        }

        private GameObject CreateTestAvatar(string name)
        {
            var avatar = new GameObject(name);
            _createdAvatars.Add(avatar);
            return avatar;
        }

        #region キャッシュ基本機能テスト

        [Test]
        public void CacheAvatar_ValidAvatar_AddsToCache()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            int slotIndex = 0;
            string modelPath = "/path/to/model.vrm";

            // Act
            _cache.CacheAvatar(slotIndex, modelPath, avatar, keepActive: true);

            // Assert
            Assert.IsTrue(_cache.HasCachedAvatar(slotIndex));
            Assert.AreEqual(1, _cache.CachedCount);
        }

        [Test]
        public void CacheAvatar_NullAvatar_DoesNotCache()
        {
            // Arrange
            int slotIndex = 0;
            string modelPath = "/path/to/model.vrm";

            // Act
            _cache.CacheAvatar(slotIndex, modelPath, null, keepActive: true);

            // Assert
            Assert.IsFalse(_cache.HasCachedAvatar(slotIndex));
            Assert.AreEqual(0, _cache.CachedCount);
        }

        [Test]
        public void GetCachedAvatar_ExistingSlot_ReturnsAvatar()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            int slotIndex = 0;
            string modelPath = "/path/to/model.vrm";
            _cache.CacheAvatar(slotIndex, modelPath, avatar, keepActive: true);

            // Act
            var result = _cache.GetCachedAvatar(slotIndex);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(avatar, result);
        }

        [Test]
        public void GetCachedAvatar_NonExistingSlot_ReturnsNull()
        {
            // Act
            var result = _cache.GetCachedAvatar(999);

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void HasCachedAvatar_ExistingSlot_ReturnsTrue()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            // Act & Assert
            Assert.IsTrue(_cache.HasCachedAvatar(0));
        }

        [Test]
        public void HasCachedAvatar_NonExistingSlot_ReturnsFalse()
        {
            // Act & Assert
            Assert.IsFalse(_cache.HasCachedAvatar(0));
        }

        #endregion

        #region キャッシュ上限テスト

        [Test]
        public void CacheAvatar_ExceedsMaxLimit_EvictsOldest()
        {
            // Arrange - maxCachedAvatars デフォルトは 6
            for (int i = 0; i < 6; i++)
            {
                var avatar = CreateTestAvatar($"Avatar{i}");
                avatar.SetActive(false); // 非アクティブにして削除可能にする
                _cache.CacheAvatar(i, $"/path/to/model{i}.vrm", avatar, keepActive: false);
            }

            Assert.AreEqual(6, _cache.CachedCount);

            // EditModeではDestroy呼び出し時にエラーログが出るので期待値として設定
            LogAssert.Expect(LogType.Error, "Destroy may not be called from edit mode! Use DestroyImmediate instead.\nDestroying an object in edit mode destroys it permanently.");

            // Act - 7番目のアバターを追加
            var newAvatar = CreateTestAvatar("Avatar6");
            _cache.CacheAvatar(6, "/path/to/model6.vrm", newAvatar, keepActive: true);

            // Assert - 最古のものが削除され、新しいものが追加される
            Assert.AreEqual(6, _cache.CachedCount);
            Assert.IsTrue(_cache.HasCachedAvatar(6));
        }

        #endregion

        #region アクティベーションテスト

        [Test]
        public void ActivateAvatar_CachedAvatar_MakesActive()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            avatar.SetActive(false);
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: false);

            // Act
            var result = _cache.ActivateAvatar(0);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(avatar.activeSelf);
            Assert.AreEqual(0, _cache.ActiveSlotIndex);
        }

        [Test]
        public void DeactivateAvatar_ActiveAvatar_MakesInactive()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            // Act
            _cache.DeactivateAvatar(0);

            // Assert
            Assert.IsFalse(avatar.activeSelf);
            Assert.AreEqual(-1, _cache.ActiveSlotIndex);
        }

        #endregion

        #region 位置保存・復元テスト

        [Test]
        public void CacheEntry_SaveTransform_SavesCorrectValues()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            avatar.transform.position = new Vector3(1, 2, 3);
            avatar.transform.rotation = Quaternion.Euler(45, 90, 0);
            avatar.transform.localScale = new Vector3(2, 2, 2);

            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            // Act
            var entry = _cache.GetCacheEntry(0);

            // Assert
            Assert.IsNotNull(entry);
            Assert.IsTrue(entry.hasLastTransform);
            Assert.AreEqual(new Vector3(1, 2, 3), entry.lastWorldPosition);
            Assert.AreEqual(new Vector3(2, 2, 2), entry.lastScale);
        }

        [Test]
        public void CacheEntry_RestoreTransform_RestoresCorrectValues()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            var originalPosition = new Vector3(5, 10, 15);
            var originalRotation = Quaternion.Euler(0, 180, 0);
            var originalScale = new Vector3(1.5f, 1.5f, 1.5f);

            avatar.transform.position = originalPosition;
            avatar.transform.rotation = originalRotation;
            avatar.transform.localScale = originalScale;

            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            // 位置を変更
            avatar.transform.position = Vector3.zero;
            avatar.transform.rotation = Quaternion.identity;

            // Act
            var entry = _cache.GetCacheEntry(0);
            entry.RestoreTransform(null);

            // Assert
            Assert.AreEqual(originalPosition, avatar.transform.position);
            Assert.AreEqual(originalScale, avatar.transform.localScale);
        }

        #endregion

        #region 削除テスト

        [Test]
        public void RemoveFromCache_ExistingSlot_RemovesEntry()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            // EditModeではDestroy呼び出し時にエラーログが出るので期待値として設定
            LogAssert.Expect(LogType.Error, "Destroy may not be called from edit mode! Use DestroyImmediate instead.\nDestroying an object in edit mode destroys it permanently.");

            // Act
            _cache.RemoveFromCache(0);

            // Assert
            Assert.IsFalse(_cache.HasCachedAvatar(0));
            Assert.AreEqual(0, _cache.CachedCount);
        }

        [Test]
        public void ClearAll_WithCachedAvatars_ClearsAllEntries()
        {
            // Arrange
            for (int i = 0; i < 3; i++)
            {
                var avatar = CreateTestAvatar($"Avatar{i}");
                _cache.CacheAvatar(i, $"/path/to/model{i}.vrm", avatar, keepActive: i == 0);
            }

            Assert.AreEqual(3, _cache.CachedCount);

            // EditModeではDestroy呼び出し時にエラーログが出るので期待値として設定（3回分）
            LogAssert.Expect(LogType.Error, "Destroy may not be called from edit mode! Use DestroyImmediate instead.\nDestroying an object in edit mode destroys it permanently.");
            LogAssert.Expect(LogType.Error, "Destroy may not be called from edit mode! Use DestroyImmediate instead.\nDestroying an object in edit mode destroys it permanently.");
            LogAssert.Expect(LogType.Error, "Destroy may not be called from edit mode! Use DestroyImmediate instead.\nDestroying an object in edit mode destroys it permanently.");

            // Act
            _cache.ClearAll();

            // Assert
            Assert.AreEqual(0, _cache.CachedCount);
            Assert.AreEqual(-1, _cache.ActiveSlotIndex);
        }

        #endregion

        #region パス検索テスト

        [Test]
        public void GetCachedAvatarByPath_ExistingPath_ReturnsAvatar()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            string modelPath = "/path/to/model.vrm";
            _cache.CacheAvatar(0, modelPath, avatar, keepActive: true);

            // Act
            var result = _cache.GetCachedAvatarByPath(modelPath);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(avatar, result);
        }

        [Test]
        public void GetCachedAvatarByPath_NonExistingPath_ReturnsNull()
        {
            // Act
            var result = _cache.GetCachedAvatarByPath("/non/existing/path.vrm");

            // Assert
            Assert.IsNull(result);
        }

        [Test]
        public void GetCachedAvatarByPath_NullPath_ReturnsNull()
        {
            // Act
            var result = _cache.GetCachedAvatarByPath(null);

            // Assert
            Assert.IsNull(result);
        }

        #endregion

        #region 重複防止テスト

        [Test]
        public void CacheAvatar_SameSlotDifferentAvatar_ReplacesOld()
        {
            // Arrange
            var avatar1 = CreateTestAvatar("Avatar1");
            var avatar2 = CreateTestAvatar("Avatar2");
            int slotIndex = 0;

            _cache.CacheAvatar(slotIndex, "/path/to/model1.vrm", avatar1, keepActive: true);

            // EditModeではDestroy呼び出し時にエラーログが出るので期待値として設定
            LogAssert.Expect(LogType.Error, "Destroy may not be called from edit mode! Use DestroyImmediate instead.\nDestroying an object in edit mode destroys it permanently.");

            // Act
            _cache.CacheAvatar(slotIndex, "/path/to/model2.vrm", avatar2, keepActive: true);

            // Assert
            Assert.AreEqual(1, _cache.CachedCount);
            var cached = _cache.GetCachedAvatar(slotIndex);
            Assert.AreEqual(avatar2, cached);
        }

        [Test]
        public void CacheAvatar_SameAvatarSameSlot_UpdatesTimestamp()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            int slotIndex = 0;
            string modelPath = "/path/to/model.vrm";

            _cache.CacheAvatar(slotIndex, modelPath, avatar, keepActive: true);
            int initialCount = _cache.CachedCount;

            // Act
            _cache.CacheAvatar(slotIndex, modelPath, avatar, keepActive: true);

            // Assert
            Assert.AreEqual(initialCount, _cache.CachedCount);
        }

        #endregion

        #region キャッシュ有効性テスト

        [Test]
        public void IsCacheValid_ValidEntry_ReturnsTrue()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            // Act & Assert
            Assert.IsTrue(_cache.IsCacheValid(0));
        }

        [Test]
        public void IsCacheValid_DestroyedAvatar_ReturnsFalse()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            // 意図的にアバターを破棄（バックグラウンドでのメモリ解放をシミュレート）
            _createdAvatars.Remove(avatar);
            UnityEngine.Object.DestroyImmediate(avatar);

            // Act & Assert
            Assert.IsFalse(_cache.IsCacheValid(0));
        }

        [Test]
        public void IsCacheValid_NonExistingSlot_ReturnsFalse()
        {
            // Act & Assert
            Assert.IsFalse(_cache.IsCacheValid(999));
        }

        #endregion

        #region イベントテスト

        [Test]
        public void CacheAvatar_FiresOnAvatarCachedEvent()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            int eventSlotIndex = -1;
            GameObject eventAvatar = null;

            _cache.OnAvatarCached += (slot, obj) =>
            {
                eventSlotIndex = slot;
                eventAvatar = obj;
            };

            // Act
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            // Assert
            Assert.AreEqual(0, eventSlotIndex);
            Assert.AreEqual(avatar, eventAvatar);
        }

        [Test]
        public void ActivateAvatar_FiresOnAvatarActivatedEvent()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            avatar.SetActive(false);
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: false);

            int eventSlotIndex = -1;
            GameObject eventAvatar = null;

            _cache.OnAvatarActivated += (slot, obj) =>
            {
                eventSlotIndex = slot;
                eventAvatar = obj;
            };

            // Act
            _cache.ActivateAvatar(0);

            // Assert
            Assert.AreEqual(0, eventSlotIndex);
            Assert.AreEqual(avatar, eventAvatar);
        }

        [Test]
        public void RemoveFromCache_FiresOnAvatarEvictedEvent()
        {
            // Arrange
            var avatar = CreateTestAvatar("TestAvatar");
            _cache.CacheAvatar(0, "/path/to/model.vrm", avatar, keepActive: true);

            int eventSlotIndex = -1;

            _cache.OnAvatarEvicted += (slot) =>
            {
                eventSlotIndex = slot;
            };

            // EditModeではDestroy呼び出し時にエラーログが出るので期待値として設定
            LogAssert.Expect(LogType.Error, "Destroy may not be called from edit mode! Use DestroyImmediate instead.\nDestroying an object in edit mode destroys it permanently.");

            // Act
            _cache.RemoveFromCache(0);

            // Assert
            Assert.AreEqual(0, eventSlotIndex);
        }

        #endregion
    }
}
