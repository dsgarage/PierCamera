using System.Collections;
using System.IO;
using AICam.AvatarCache;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.AvatarCache.Integration
{
    /// <summary>
    /// Phase 1: AvatarSlotData拡張テスト
    ///
    /// テスト対象:
    /// - binaryCacheIdフィールドの追加
    /// - HasBinaryCacheプロパティ
    /// - JSON永続化での保存/復元
    /// </summary>
    [TestFixture]
    public class Phase1_SlotDataExtensionTests
    {
        private string _testCacheDir;

        [SetUp]
        public void SetUp()
        {
            _testCacheDir = Path.Combine(Application.temporaryCachePath, "IntegrationTest");
            if (Directory.Exists(_testCacheDir))
            {
                Directory.Delete(_testCacheDir, true);
            }
            Directory.CreateDirectory(_testCacheDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testCacheDir))
            {
                Directory.Delete(_testCacheDir, true);
            }
        }

        [Test]
        public void AvatarSlotData_binaryCacheIdを設定できること()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            var testCacheId = "abc123def456";

            // Act
            slotData.SetBinaryCacheId(testCacheId);

            // Assert
            Assert.AreEqual(testCacheId, slotData.binaryCacheId);
            Debug.Log($"[Phase1Test] binaryCacheId設定成功: {slotData.binaryCacheId}");
        }

        [Test]
        public void AvatarSlotData_HasBinaryCacheがtrueを返すこと()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);

            // Act - 設定前
            var beforeSet = slotData.HasBinaryCache;

            // Act - 設定後
            slotData.SetBinaryCacheId("test-cache-id");
            var afterSet = slotData.HasBinaryCache;

            // Assert
            Assert.IsFalse(beforeSet, "設定前はfalseであるべき");
            Assert.IsTrue(afterSet, "設定後はtrueであるべき");
            Debug.Log("[Phase1Test] HasBinaryCache検証成功");
        }

        [Test]
        public void AvatarSlotData_ClearBinaryCacheでクリアできること()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            slotData.SetBinaryCacheId("test-cache-id");

            // Act
            slotData.ClearBinaryCache();

            // Assert
            Assert.IsFalse(slotData.HasBinaryCache);
            Assert.IsTrue(string.IsNullOrEmpty(slotData.binaryCacheId));
            Debug.Log("[Phase1Test] ClearBinaryCache成功");
        }

        [Test]
        public void AvatarSlotData_JSON永続化でbinaryCacheIdが保存されること()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            slotData.SetBinaryCacheId("persistent-cache-id");
            slotData.modelFilePath = "/path/to/model.vrm";

            // Act - シリアライズ
            var json = JsonUtility.ToJson(slotData);

            // Act - デシリアライズ
            var restored = JsonUtility.FromJson<AvatarSlotData>(json);

            // Assert
            Assert.AreEqual("persistent-cache-id", restored.binaryCacheId);
            Assert.IsTrue(restored.HasBinaryCache);
            Debug.Log($"[Phase1Test] JSON永続化成功: {json}");
        }

        [Test]
        public void AvatarSlotData_空文字列のbinaryCacheIdはHasBinaryCacheがfalse()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);

            // Act
            slotData.binaryCacheId = "";

            // Assert
            Assert.IsFalse(slotData.HasBinaryCache);
            Debug.Log("[Phase1Test] 空文字列の検証成功");
        }
    }
}
