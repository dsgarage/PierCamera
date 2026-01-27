using System.Collections;
using System.IO;
using AICam.AvatarCache;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
#if BLENDSHAPE_CONTROLLER
using DSGarage.BlendShape;
using AICam.VRM;
#endif

namespace AICam.Tests.PlayMode.Integration
{
    /// <summary>
    /// Phase 11: 表情アイコン統合テスト (Issue #464-#467)
    ///
    /// テスト対象:
    /// - AvatarSlotData の expressionIconFolderPath 拡張
    /// - VrmExpressionBridge のユーティリティメソッド
    /// - ExpressionIconService のパス生成・エラーハンドリング
    /// - AvatarSlotCache.ClearSlot での表情アイコンフォルダ削除
    /// </summary>
    [TestFixture]
    public class Phase11_ExpressionIconTests
    {
        private string _testDir;

        [SetUp]
        public void SetUp()
        {
            _testDir = Path.Combine(Application.temporaryCachePath, "Phase11Test");
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }
            Directory.CreateDirectory(_testDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, true);
            }

#if BLENDSHAPE_CONTROLLER
            // ExpressionIconService シングルトンをクリーンアップ
            var serviceObj = GameObject.Find("ExpressionIconService");
            if (serviceObj != null)
            {
                Object.Destroy(serviceObj);
            }
#endif
        }

        #region AvatarSlotData 表情アイコン拡張テスト

        [Test]
        public void AvatarSlotData_expressionIconFolderPathの初期値が空文字列であること()
        {
            // Arrange & Act
            var slotData = new AvatarSlotData(0);

            // Assert
            Assert.AreEqual(string.Empty, slotData.expressionIconFolderPath);
            Debug.Log("[Phase11Test] expressionIconFolderPath 初期値検証成功");
        }

        [Test]
        public void AvatarSlotData_HasExpressionIconsがフォルダ存在時にtrueを返すこと()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            var iconFolder = Path.Combine(_testDir, "expression_icons");
            Directory.CreateDirectory(iconFolder);
            slotData.expressionIconFolderPath = iconFolder;

            // Act & Assert
            Assert.IsTrue(slotData.HasExpressionIcons);
            Debug.Log("[Phase11Test] HasExpressionIcons=true 検証成功");
        }

        [Test]
        public void AvatarSlotData_HasExpressionIconsがフォルダ非存在時にfalseを返すこと()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            slotData.expressionIconFolderPath = Path.Combine(_testDir, "nonexistent_folder");

            // Act & Assert
            Assert.IsFalse(slotData.HasExpressionIcons);
            Debug.Log("[Phase11Test] HasExpressionIcons=false (非存在) 検証成功");
        }

        [Test]
        public void AvatarSlotData_HasExpressionIconsがパス空文字列でfalseを返すこと()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);

            // Act & Assert
            Assert.IsFalse(slotData.HasExpressionIcons);
            Debug.Log("[Phase11Test] HasExpressionIcons=false (空文字列) 検証成功");
        }

        [Test]
        public void AvatarSlotData_GetExpressionIconPathが正しいパスを返すこと()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            slotData.expressionIconFolderPath = "/path/to/icons";

            // Act
            var path = slotData.GetExpressionIconPath("01_Smile");

            // Assert
            Assert.AreEqual(Path.Combine("/path/to/icons", "01_Smile.png"), path);
            Debug.Log($"[Phase11Test] GetExpressionIconPath 検証成功: {path}");
        }

        [Test]
        public void AvatarSlotData_GetExpressionIconPathが空キーで空文字列を返すこと()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            slotData.expressionIconFolderPath = "/path/to/icons";

            // Act & Assert
            Assert.AreEqual(string.Empty, slotData.GetExpressionIconPath(""));
            Assert.AreEqual(string.Empty, slotData.GetExpressionIconPath(null));
            Debug.Log("[Phase11Test] GetExpressionIconPath 空キー検証成功");
        }

        [Test]
        public void AvatarSlotData_Clearでexpression関連フィールドがクリアされること()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            slotData.expressionIconFolderPath = "/some/path";
            slotData.avatarName = "TestAvatar";

            // Act
            slotData.Clear();

            // Assert
            Assert.AreEqual(string.Empty, slotData.expressionIconFolderPath);
            Assert.IsFalse(slotData.HasExpressionIcons);
            Debug.Log("[Phase11Test] Clear 検証成功");
        }

        [Test]
        public void AvatarSlotData_JSON永続化でexpressionIconFolderPathが保存されること()
        {
            // Arrange
            var slotData = new AvatarSlotData(0);
            slotData.expressionIconFolderPath = "/path/to/expression/icons";
            slotData.avatarName = "TestAvatar";

            // Act - シリアライズ
            var json = JsonUtility.ToJson(slotData);

            // Act - デシリアライズ
            var restored = JsonUtility.FromJson<AvatarSlotData>(json);

            // Assert
            Assert.AreEqual("/path/to/expression/icons", restored.expressionIconFolderPath);
            Debug.Log($"[Phase11Test] JSON永続化検証成功");
        }

        [Test]
        public void AvatarSlotData_JSON互換性_expressionIconFolderPathなしでもデシリアライズできること()
        {
            // Arrange - expressionIconFolderPath を含まない旧形式JSON
            var json = "{\"slotIndex\":0,\"avatarName\":\"TestAvatar\",\"modelFilePath\":\"\",\"isValid\":false}";

            // Act
            var restored = JsonUtility.FromJson<AvatarSlotData>(json);

            // Assert
            Assert.IsNotNull(restored);
            Assert.AreEqual(string.Empty, restored.expressionIconFolderPath);
            Assert.IsFalse(restored.HasExpressionIcons);
            Debug.Log("[Phase11Test] JSON後方互換性検証成功");
        }

        #endregion

        #region AvatarSlotCache ClearSlot テスト

        [Test]
        public void AvatarSlotCache_ClearSlotで表情アイコンフォルダが削除されること()
        {
            // Arrange
            var cache = new AvatarSlotCache();
            cache.Initialize(3);

            var iconFolder = Path.Combine(_testDir, "slot0_icons");
            Directory.CreateDirectory(iconFolder);
            File.WriteAllText(Path.Combine(iconFolder, "test.png"), "dummy");

            cache.slots[0].expressionIconFolderPath = iconFolder;

            // Act
            cache.ClearSlot(0);

            // Assert
            Assert.IsFalse(Directory.Exists(iconFolder), "表情アイコンフォルダが削除されるべき");
            Assert.AreEqual(string.Empty, cache.slots[0].expressionIconFolderPath);
            Debug.Log("[Phase11Test] ClearSlot 表情アイコンフォルダ削除検証成功");
        }

        [Test]
        public void AvatarSlotCache_ClearSlotでフォルダ未設定でもエラーにならないこと()
        {
            // Arrange
            var cache = new AvatarSlotCache();
            cache.Initialize(3);

            // expressionIconFolderPath は初期値（空文字列）のまま

            // Act & Assert - 例外が発生しないことを確認
            Assert.DoesNotThrow(() => cache.ClearSlot(0));
            Debug.Log("[Phase11Test] ClearSlot フォルダ未設定時の安全性検証成功");
        }

        #endregion

#if BLENDSHAPE_CONTROLLER
        #region VrmExpressionBridge テスト

        [Test]
        public void VrmExpressionBridge_IsVRoidStudioAvatarがnullでfalseを返すこと()
        {
            // Act & Assert
            Assert.IsFalse(VrmExpressionBridge.IsVRoidStudioAvatar(null));
            Debug.Log("[Phase11Test] IsVRoidStudioAvatar null検証成功");
        }

        [Test]
        public void VrmExpressionBridge_IsVRoidStudioAvatarがFcl無しメッシュでfalseを返すこと()
        {
            // Arrange - Fcl_ ブレンドシェイプを持たない空のGameObject
            var go = new GameObject("NonVRoidAvatar");

            // Act & Assert
            Assert.IsFalse(VrmExpressionBridge.IsVRoidStudioAvatar(go));

            // Cleanup
            Object.Destroy(go);
            Debug.Log("[Phase11Test] IsVRoidStudioAvatar Fcl_なし検証成功");
        }

        [Test]
        public void VrmExpressionBridge_GetStandardExpressionSetが13表情を含むこと()
        {
            // Act
            var set = VrmExpressionBridge.GetStandardExpressionSet();

            // Assert
            Assert.IsNotNull(set);
            Assert.AreEqual(13, set.Count, "StandardExpressions は 13 表情を含むべき");
            Assert.AreEqual("StandardExpressions", set.setName);

            Debug.Log($"[Phase11Test] GetStandardExpressionSet 検証成功: {set.Count} 表情");
            foreach (var entry in set.expressions)
            {
                Debug.Log($"  - {entry.name}: {entry.blendShapes.Count} blendshapes");
            }
        }

        [Test]
        public void VrmExpressionBridge_StandardExpressionSetのNeutralが空のblendShapesを持つこと()
        {
            // Act
            var set = VrmExpressionBridge.GetStandardExpressionSet();

            // Assert - 最初のエントリ (00_Neutral) は空の blendShapes
            Assert.IsTrue(set.expressions.Count > 0);
            var neutral = set.expressions[0];
            Assert.AreEqual("00_Neutral", neutral.name);
            Assert.AreEqual(0, neutral.blendShapes.Count, "Neutral は blendShapes が空であるべき");
            Debug.Log("[Phase11Test] StandardExpressionSet Neutral 検証成功");
        }

        [Test]
        public void VrmExpressionBridge_StandardExpressionSetのSmileが正しいblendShapesを持つこと()
        {
            // Act
            var set = VrmExpressionBridge.GetStandardExpressionSet();

            // Assert - 2番目のエントリ (01_Smile) のblendShapesを確認
            Assert.IsTrue(set.expressions.Count > 1);
            var smile = set.expressions[1];
            Assert.AreEqual("01_Smile", smile.name);
            Assert.IsTrue(smile.blendShapes.ContainsKey("Fcl_EYE_Joy"));
            Assert.AreEqual(100f, smile.blendShapes["Fcl_EYE_Joy"]);
            Assert.IsTrue(smile.blendShapes.ContainsKey("Fcl_MTH_Fun"));
            Assert.AreEqual(100f, smile.blendShapes["Fcl_MTH_Fun"]);
            Debug.Log("[Phase11Test] StandardExpressionSet Smile 検証成功");
        }

        #endregion

        #region ExpressionIconService テスト

        [Test]
        public void ExpressionIconService_GetBaseFolderが正しいパスを返すこと()
        {
            // Act
            var baseFolder = ExpressionIconService.GetBaseFolder();

            // Assert
            Assert.IsTrue(baseFolder.Contains("AvatarSlots"));
            Assert.IsTrue(baseFolder.Contains("expression_icons"));
            Assert.IsTrue(baseFolder.StartsWith(Application.persistentDataPath));
            Debug.Log($"[Phase11Test] GetBaseFolder 検証成功: {baseFolder}");
        }

        [Test]
        public void ExpressionIconService_GetOutputFolderが正しいパスを返すこと()
        {
            // Act
            var outputFolder = ExpressionIconService.GetOutputFolder("TestAvatar");

            // Assert
            var expected = Path.Combine(ExpressionIconService.GetBaseFolder(), "TestAvatar");
            Assert.AreEqual(expected, outputFolder);
            Debug.Log($"[Phase11Test] GetOutputFolder 検証成功: {outputFolder}");
        }

        [UnityTest]
        public IEnumerator ExpressionIconService_シングルトンインスタンスが取得できること()
        {
            // Act
            var instance = ExpressionIconService.Instance;

            yield return null;

            // Assert
            Assert.IsNotNull(instance);
            Assert.IsFalse(instance.IsGenerating);
            Debug.Log("[Phase11Test] ExpressionIconService シングルトン検証成功");
        }

        [UnityTest]
        public IEnumerator ExpressionIconService_GenerateForSlotがnullアバターでエラーコールバックを呼ぶこと()
        {
            // Arrange
            var instance = ExpressionIconService.Instance;
            string errorMessage = null;
            bool errorCalled = false;

            yield return null;

            // Act
            instance.GenerateForSlot(
                null,
                0,
                "TestAvatar",
                onComplete: (_) => Assert.Fail("null アバターで onComplete が呼ばれるべきではない"),
                onError: (error) =>
                {
                    errorCalled = true;
                    errorMessage = error;
                }
            );

            yield return null;

            // Assert
            Assert.IsTrue(errorCalled, "onError が呼ばれるべき");
            Assert.AreEqual("Avatar is null", errorMessage);
            Debug.Log($"[Phase11Test] GenerateForSlot null アバターエラー検証成功: {errorMessage}");
        }

        [UnityTest]
        public IEnumerator ExpressionIconService_既存アイコンがある場合スキップすること()
        {
            // Arrange
            var instance = ExpressionIconService.Instance;

            yield return null;

            // テスト用のアバターGameObjectを作成
            var avatar = new GameObject("TestSkipAvatar");
            // SkinnedMeshRenderer を追加して最低限の構成にする
            var child = new GameObject("Mesh");
            child.transform.SetParent(avatar.transform);
            child.AddComponent<SkinnedMeshRenderer>();

            // 既存アイコンフォルダを事前作成
            var baseFolder = ExpressionIconService.GetBaseFolder();
            var iconFolder = Path.Combine(baseFolder, "TestSkipAvatar");
            Directory.CreateDirectory(iconFolder);
            File.WriteAllBytes(Path.Combine(iconFolder, "00_Neutral.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            string completedPath = null;

            // Act
            instance.GenerateForSlot(
                avatar,
                0,
                "TestSkipAvatar",
                onComplete: (path) => { completedPath = path; },
                onError: (error) => Assert.Fail($"エラーが発生すべきではない: {error}")
            );

            yield return null;

            // Assert
            Assert.IsNotNull(completedPath, "既存アイコンがある場合 onComplete が即座に呼ばれるべき");
            Assert.AreEqual(iconFolder, completedPath);
            Debug.Log($"[Phase11Test] 既存アイコンスキップ検証成功: {completedPath}");

            // Cleanup
            Object.Destroy(avatar);
            if (Directory.Exists(iconFolder))
            {
                Directory.Delete(iconFolder, true);
            }
        }

        #endregion
#endif
    }
}
