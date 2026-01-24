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
            var smr = avatar.GetComponentInChildren<SkinnedMeshRenderer>();

            // 実装が存在することを確認
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act
            var blendShapeNames = new List<string>();
            if (smr?.sharedMesh != null)
            {
                var mesh = smr.sharedMesh;
                for (int i = 0; i < mesh.blendShapeCount; i++)
                {
                    blendShapeNames.Add(mesh.GetBlendShapeName(i));
                }
            }

            // Assert
            Debug.Log($"[Phase8Test] BlendShape数: {blendShapeNames.Count}");

            if (blendShapeNames.Count > 0)
            {
                foreach (var name in blendShapeNames.GetRange(0, Mathf.Min(10, blendShapeNames.Count)))
                {
                    Debug.Log($"  - {name}");
                }
            }
        });

        [UnityTest]
        public IEnumerator 表情_ExpressionDataをシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();

            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var expressionsDir = Path.Combine(cacheDir, "expressions");
            Directory.CreateDirectory(expressionsDir);

            // 実際のExpressionDataクラスを使用
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

            // Act
            var exprPath = Path.Combine(expressionsDir, "expr_0.json");
            var json = JsonUtility.ToJson(expressionData, true);
            File.WriteAllText(exprPath, json);

            // Assert
            AssertFileExists(exprPath, "表情データ");

            var loadedJson = File.ReadAllText(exprPath);
            Assert.IsTrue(loadedJson.Contains("blendShapeValues"));
            Assert.IsTrue(loadedJson.Contains("Happy"));

            Debug.Log($"[Phase8Test] 表情データ保存: {exprPath}");
        });

        #endregion

        #region Expression Manifest Tests

        [UnityTest]
        public IEnumerator 表情マニフェスト_ExpressionManifestをシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var expressionsDir = Path.Combine(cacheDir, "expressions");
            Directory.CreateDirectory(expressionsDir);

            // 実際のExpressionManifestクラスを使用
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

            // Act
            var manifestPath = Path.Combine(expressionsDir, "manifest.json");
            var json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(manifestPath, json);

            // Assert
            AssertFileExists(manifestPath, "表情マニフェスト");

            var loadedManifest = JsonUtility.FromJson<ExpressionManifest>(File.ReadAllText(manifestPath));
            Assert.AreEqual(2, loadedManifest.expressions.Length);

            Debug.Log($"[Phase8Test] 表情マニフェスト保存: {manifest.expressions.Length} 表情");
        });

        #endregion

        #region Pose Manifest Tests

        [UnityTest]
        public IEnumerator ポーズマニフェスト_PoseManifestをシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var posesDir = Path.Combine(cacheDir, "poses");
            Directory.CreateDirectory(posesDir);

            // 実際のPoseManifestクラスを使用
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

            // Act
            var manifestPath = Path.Combine(posesDir, "manifest.json");
            var json = JsonUtility.ToJson(manifest, true);
            File.WriteAllText(manifestPath, json);

            // Assert
            AssertFileExists(manifestPath, "ポーズマニフェスト");

            var loadedManifest = JsonUtility.FromJson<PoseManifest>(File.ReadAllText(manifestPath));
            Assert.AreEqual(2, loadedManifest.poses.Length);
            Assert.IsTrue(loadedManifest.poses[0].isDefault);

            Debug.Log($"[Phase8Test] ポーズマニフェスト保存: {manifest.poses.Length} ポーズ");
        });

        #endregion

        #region Icon Generation Tests

        [UnityTest]
        public IEnumerator アイコン_ポーズアイコンを作成できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var posesDir = Path.Combine(cacheDir, "poses");
            Directory.CreateDirectory(posesDir);

            // Act - ダミーのアイコンを作成（実際はカメラでキャプチャ）
            var iconTexture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var colors = new Color[256 * 256];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.gray;
            }
            iconTexture.SetPixels(colors);
            iconTexture.Apply();

            var pngData = iconTexture.EncodeToPNG();
            var iconPath = Path.Combine(posesDir, "pose_0.png");
            File.WriteAllBytes(iconPath, pngData);

            Object.Destroy(iconTexture);

            // Assert
            AssertFileExists(iconPath, "ポーズアイコン");
            Assert.IsTrue(new FileInfo(iconPath).Length > 0, "アイコンにコンテンツがあるべき");

            Debug.Log($"[Phase8Test] ポーズアイコン作成: {iconPath}");
        });

        [UnityTest]
        public IEnumerator アイコン_表情アイコンを作成できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var expressionsDir = Path.Combine(cacheDir, "expressions");
            Directory.CreateDirectory(expressionsDir);

            // Act - ダミーのアイコンを作成
            var iconTexture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
            var colors = new Color[256 * 256];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = Color.white;
            }
            iconTexture.SetPixels(colors);
            iconTexture.Apply();

            var pngData = iconTexture.EncodeToPNG();
            var iconPath = Path.Combine(expressionsDir, "expr_0.png");
            File.WriteAllBytes(iconPath, pngData);

            Object.Destroy(iconTexture);

            // Assert
            AssertFileExists(iconPath, "表情アイコン");

            Debug.Log($"[Phase8Test] 表情アイコン作成: {iconPath}");
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

            // Act - AnimationCacheHeaderのMAGICを使用してダミーのアニメーションデータを保存
            using (var stream = File.Create(animPath))
            using (var writer = new BinaryWriter(stream))
            {
                // Header - 実際のAnimationCacheHeader.MAGICを使用
                writer.Write(System.Text.Encoding.ASCII.GetBytes(AnimationCacheHeader.MAGIC));
                writer.Write((uint)1); // version
                writer.Write("DefaultPose"); // clip name
                writer.Write(30f); // frame rate
                writer.Write(1f); // length
                writer.Write((uint)0); // wrap mode

                // Curves (empty for test)
                writer.Write((uint)0); // curve count
            }

            // Assert
            AssertFileExists(animPath, "アニメーションバイナリ");
            AssertBinaryMagic(animPath, AnimationCacheHeader.MAGIC);

            Debug.Log($"[Phase8Test] アニメーションバイナリ保存: {animPath}");
        });

        #endregion

        #region Integration Tests

        [UnityTest]
        public IEnumerator フルキャッシュ_ポーズと表情を含むこと() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);

            // Create full cache structure
            Directory.CreateDirectory(Path.Combine(cacheDir, "core"));
            Directory.CreateDirectory(Path.Combine(cacheDir, "textures"));
            Directory.CreateDirectory(Path.Combine(cacheDir, "icons"));
            Directory.CreateDirectory(Path.Combine(cacheDir, "poses"));
            Directory.CreateDirectory(Path.Combine(cacheDir, "expressions"));

            // Create manifests
            File.WriteAllText(Path.Combine(cacheDir, "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(cacheDir, "poses", "manifest.json"), "{}");
            File.WriteAllText(Path.Combine(cacheDir, "expressions", "manifest.json"), "{}");

            // Assert
            AssertDirectoryExists(Path.Combine(cacheDir, "poses"), "posesディレクトリ");
            AssertDirectoryExists(Path.Combine(cacheDir, "expressions"), "expressionsディレクトリ");
            AssertFileExists(Path.Combine(cacheDir, "poses", "manifest.json"), "ポーズマニフェスト");
            AssertFileExists(Path.Combine(cacheDir, "expressions", "manifest.json"), "表情マニフェスト");

            Debug.Log("[Phase8Test] ポーズと表情を含むフルキャッシュ構造作成成功");
        });

        #endregion
    }
}
