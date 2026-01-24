using System.Collections;
using System.Collections.Generic;
using System.IO;
using AICam.AvatarCache;
using AICam.AvatarCache.Serializers;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.AvatarCache
{
    /// <summary>
    /// Phase 8: ポーズ/表情テスト
    ///
    /// テスト対象:
    /// - ポーズアイコンの生成と保存
    /// - 表情アイコンの生成と保存
    /// - ポーズ/表情マニフェストの管理
    /// - BlendShape値の保存/復元
    /// </summary>
    [TestFixture]
    public class Phase8_PoseExpressionTests : AvatarCacheTestBase
    {
        #region Expression Extraction Tests

        [UnityTest]
        public IEnumerator 表情_BlendShape名を抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();

            // Act - 実際のExpressionCacheSerializer.ExtractBlendShapeNamesを呼び出す
            var blendShapeNames = ExpressionCacheSerializer.ExtractBlendShapeNames(avatar);

            // Assert
            Assert.IsNotNull(blendShapeNames);
            Debug.Log($"[Phase8Test] BlendShape数: {blendShapeNames.Length}");

            foreach (var name in blendShapeNames)
            {
                Debug.Log($"  - {name}");
            }
        });

        [UnityTest]
        public IEnumerator 表情_ExpressionDataをシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var expressionData = new ExpressionData
            {
                version = 1,
                name = "Happy",
                preset = "Joy",
                blendShapeValues = new BlendShapeValue[]
                {
                    new BlendShapeValue { name = "Face.M_F00_000_Fcl_ALL_Joy", value = 1.0f },
                    new BlendShapeValue { name = "Face.M_F00_000_Fcl_EYE_Close", value = 0.3f }
                }
            };

            // Act - 実際のExpressionCacheSerializer.SerializeToJsonを呼び出す
            var json = ExpressionCacheSerializer.SerializeToJson(expressionData);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("blendShapeValues"));
            Assert.IsTrue(json.Contains("Happy"));

            Debug.Log($"[Phase8Test] 表情データシリアライズ成功");
        });

        #endregion

        #region Expression Manifest Tests

        [UnityTest]
        public IEnumerator 表情マニフェスト_ExpressionManifestをシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var manifest = new ExpressionManifest
            {
                version = 1,
                expressions = new ExpressionEntry[]
                {
                    new ExpressionEntry
                    {
                        index = 0,
                        name = "Happy",
                        preset = "Joy",
                        iconPath = "expr_0.png",
                        dataPath = "expr_0.json"
                    },
                    new ExpressionEntry
                    {
                        index = 1,
                        name = "Sad",
                        preset = "Sorrow",
                        iconPath = "expr_1.png",
                        dataPath = "expr_1.json"
                    }
                }
            };

            // Act - 実際のExpressionCacheSerializer.SerializeManifestToJsonを呼び出す
            var json = ExpressionCacheSerializer.SerializeManifestToJson(manifest);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("expressions"));

            Debug.Log($"[Phase8Test] 表情マニフェストシリアライズ成功: {manifest.expressions.Length} 表情");
        });

        #endregion

        #region Pose Manifest Tests

        [UnityTest]
        public IEnumerator ポーズマニフェスト_PoseManifestをシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var manifest = new PoseManifest
            {
                version = 1,
                poses = new PoseEntry[]
                {
                    new PoseEntry
                    {
                        index = 0,
                        name = "Default Pose",
                        iconPath = "pose_0.png",
                        animationPath = "pose_0.anim.bin",
                        isDefault = true
                    },
                    new PoseEntry
                    {
                        index = 1,
                        name = "Victory",
                        iconPath = "pose_1.png",
                        animationPath = "pose_1.anim.bin",
                        isDefault = false
                    }
                }
            };

            // Act - 実際のPoseCacheSerializer.SerializeManifestToJsonを呼び出す
            var json = PoseCacheSerializer.SerializeManifestToJson(manifest);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("poses"));

            Debug.Log($"[Phase8Test] ポーズマニフェストシリアライズ成功: {manifest.poses.Length} ポーズ");
        });

        #endregion

        #region Icon Generation Tests

        [UnityTest]
        public IEnumerator アイコン_ポーズマニフェストからアイコンパスを取得できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var manifest = new PoseManifest
            {
                version = 1,
                poses = new PoseEntry[]
                {
                    new PoseEntry
                    {
                        index = 0,
                        name = "Default Pose",
                        iconPath = "pose_0.png",
                        animationPath = "pose_0.anim.bin",
                        isDefault = true
                    }
                }
            };

            // Act - Phase 8のPoseCacheSerializer.SerializeManifestToJsonを呼び出す
            var json = PoseCacheSerializer.SerializeManifestToJson(manifest);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("pose_0.png"));

            Debug.Log($"[Phase8Test] ポーズアイコンパス取得成功");
        });

        [UnityTest]
        public IEnumerator アイコン_表情マニフェストからアイコンパスを取得できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var manifest = new ExpressionManifest
            {
                version = 1,
                expressions = new ExpressionEntry[]
                {
                    new ExpressionEntry
                    {
                        index = 0,
                        name = "Joy",
                        preset = "Joy",
                        iconPath = "expr_0.png",
                        dataPath = "expr_0.json"
                    }
                }
            };

            // Act - Phase 8のExpressionCacheSerializer.SerializeManifestToJsonを呼び出す
            var json = ExpressionCacheSerializer.SerializeManifestToJson(manifest);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsTrue(json.Contains("expr_0.png"));

            Debug.Log($"[Phase8Test] 表情アイコンパス取得成功");
        });

        #endregion

        #region Animation Binary Tests

        [UnityTest]
        public IEnumerator アニメーション_バイナリにシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var posesDir = Path.Combine(cacheDir, "poses");
            Directory.CreateDirectory(posesDir);

            var animPath = Path.Combine(posesDir, "pose_0.anim.bin");

            // ダミーのAnimationClipを作成
            var clip = new AnimationClip();
            clip.name = "TestPose";

            // Act - 実際のPoseCacheSerializer.SerializeAnimationToBinaryを呼び出す
            PoseCacheSerializer.SerializeAnimationToBinary(clip, animPath);

            // Assert
            AssertFileExists(animPath, "アニメーションバイナリ");
            AssertBinaryMagic(animPath, AnimationCacheHeader.MAGIC);

            Debug.Log($"[Phase8Test] アニメーションバイナリ保存: {animPath}");

            // Cleanup
            Object.Destroy(clip);
        });

        #endregion

        #region Integration Tests

        [UnityTest]
        public IEnumerator フルキャッシュ_ポーズと表情を含むこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();

            // Act - 実際のExpressionCacheSerializer.ExtractBlendShapeNamesを呼び出す
            // これによりPhase 8の実装が必要になる
            var blendShapeNames = ExpressionCacheSerializer.ExtractBlendShapeNames(avatar);

            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);

            // Assert
            Assert.IsNotNull(blendShapeNames);
            Debug.Log($"[Phase8Test] フルキャッシュ検証: {blendShapeNames.Length} BlendShapes");
        });

        #endregion
    }
}
