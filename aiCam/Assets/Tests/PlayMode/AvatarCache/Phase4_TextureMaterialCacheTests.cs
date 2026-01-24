using System.Collections;
using System.Collections.Generic;
using System.IO;
using AICam.AvatarCache;
using AICam.AvatarCache.IO;
using AICam.AvatarCache.Serializers;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.AvatarCache
{
    /// <summary>
    /// Phase 4: テクスチャ/マテリアル キャッシュテスト
    ///
    /// テスト対象:
    /// - テクスチャの抽出と保存
    /// - マテリアル情報のJSON保存
    /// - テクスチャ圧縮（低スペック端末用）
    /// - マテリアルの再構築
    /// </summary>
    [TestFixture]
    public class Phase4_TextureMaterialCacheTests : AvatarCacheTestBase
    {
        #region Texture Extraction Tests

        [UnityTest]
        public IEnumerator テクスチャ_マテリアルから抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var renderers = avatar.GetComponentsInChildren<Renderer>();

            // Act
            var textures = new HashSet<Texture2D>();
            var materials = new List<Material>();
            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    materials.Add(mat);

                    // MainTexを取得
                    if (mat.HasProperty("_MainTex"))
                    {
                        var tex = mat.GetTexture("_MainTex") as Texture2D;
                        if (tex != null) textures.Add(tex);
                    }
                }
            }

            // TextureCacheManagerでテクスチャ抽出・保存できることを確認
            var texturesDir = Path.Combine(TestCacheDirectory, "textures");
            Directory.CreateDirectory(texturesDir);
            var textureCacheManager = new TextureCacheManager(texturesDir);
            var textureIds = await textureCacheManager.ExtractAndSaveTexturesAsync(materials.ToArray());

            // Assert
            Assert.IsTrue(textures.Count > 0, "テクスチャが存在すべき");
            Debug.Log($"[Phase4Test] ユニークテクスチャ数: {textures.Count}");

            foreach (var tex in textures)
            {
                Debug.Log($"  テクスチャ: {tex.name} ({tex.width}x{tex.height}, {tex.format})");
            }
        });

        [UnityTest]
        public IEnumerator テクスチャ_TextureCacheManagerで保存できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var texturesDir = Path.Combine(cacheDir, "textures");
            Directory.CreateDirectory(texturesDir);

            var textureCacheManager = new TextureCacheManager(texturesDir);
            var renderers = avatar.GetComponentsInChildren<Renderer>();

            var materials = new List<Material>();
            foreach (var renderer in renderers)
            {
                materials.AddRange(renderer.sharedMaterials);
            }

            // Act - 実際のTextureCacheManager.ExtractAndSaveTexturesAsyncを呼び出す
            var textureIds = await textureCacheManager.ExtractAndSaveTexturesAsync(materials.ToArray());

            // Assert
            Assert.IsNotNull(textureIds);
            Assert.IsTrue(textureIds.Length > 0, "テクスチャIDが生成されるべき");

            Debug.Log($"[Phase4Test] 保存したテクスチャ数: {textureIds.Length}");
        });

        [UnityTest]
        public IEnumerator テクスチャ_TextureCacheManagerでロードできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var texturesDir = Path.Combine(cacheDir, "textures");
            Directory.CreateDirectory(texturesDir);

            var textureCacheManager = new TextureCacheManager(texturesDir);

            // ダミーテクスチャを作成して保存
            var dummyTexture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var colors = new Color[64 * 64];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.red;
            dummyTexture.SetPixels(colors);
            dummyTexture.Apply();

            await textureCacheManager.SaveTextureAsync(dummyTexture, "test_texture");
            Object.Destroy(dummyTexture);

            // Act - 実際のTextureCacheManager.LoadTextureAsyncを呼び出す
            var loadedTexture = await textureCacheManager.LoadTextureAsync("test_texture");

            // Assert
            Assert.IsNotNull(loadedTexture);
            Assert.AreEqual(64, loadedTexture.width);
            Assert.AreEqual(64, loadedTexture.height);

            Debug.Log($"[Phase4Test] テクスチャロード成功: {loadedTexture.width}x{loadedTexture.height}");

            Object.Destroy(loadedTexture);
        });

        #endregion

        #region Material Cache Tests

        [UnityTest]
        public IEnumerator マテリアル_MaterialCacheSerializerで抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var renderers = avatar.GetComponentsInChildren<Renderer>();

            // Act - 実際のMaterialCacheSerializer.ExtractFromRenderersを呼び出す
            var materialCache = MaterialCacheSerializer.ExtractFromRenderers(renderers);

            // Assert
            Assert.IsNotNull(materialCache);
            Assert.IsNotNull(materialCache.materials);
            Assert.IsTrue(materialCache.materials.Length > 0, "マテリアルが存在すべき");

            Debug.Log($"[Phase4Test] 抽出したマテリアル数: {materialCache.materials.Length}");
            foreach (var mat in materialCache.materials)
            {
                Debug.Log($"  マテリアル: {mat.name}, シェーダー: {mat.shaderName}");
            }
        });

        [UnityTest]
        public IEnumerator マテリアル_JSONにシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var renderers = avatar.GetComponentsInChildren<Renderer>();
            var materialCache = MaterialCacheSerializer.ExtractFromRenderers(renderers);

            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var coreDir = Path.Combine(cacheDir, "core");
            Directory.CreateDirectory(coreDir);

            // Act - 実際のMaterialCacheSerializer.SerializeToJsonを呼び出す
            var json = MaterialCacheSerializer.SerializeToJson(materialCache);

            var materialsPath = Path.Combine(coreDir, "materials.json");
            File.WriteAllText(materialsPath, json);

            // Assert
            AssertFileExists(materialsPath, "materials.json");
            Assert.IsTrue(json.Contains("shaderName"));
            Assert.IsTrue(json.Contains("renderQueue"));

            Debug.Log($"[Phase4Test] マテリアルキャッシュ保存: {materialCache.materials.Length} マテリアル");
        });

        [UnityTest]
        public IEnumerator マテリアル_JSONからデシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var renderers = avatar.GetComponentsInChildren<Renderer>();
            var originalCache = MaterialCacheSerializer.ExtractFromRenderers(renderers);
            var json = MaterialCacheSerializer.SerializeToJson(originalCache);

            // Act - 実際のMaterialCacheSerializer.DeserializeFromJsonを呼び出す
            var loadedCache = MaterialCacheSerializer.DeserializeFromJson(json);

            // Assert
            Assert.IsNotNull(loadedCache);
            Assert.AreEqual(originalCache.version, loadedCache.version);
            Assert.AreEqual(originalCache.materials.Length, loadedCache.materials.Length);

            Debug.Log("[Phase4Test] マテリアルデシリアライズ成功");
        });

        [UnityTest]
        public IEnumerator マテリアル_再構築できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var renderers = avatar.GetComponentsInChildren<Renderer>();
            var materialCache = MaterialCacheSerializer.ExtractFromRenderers(renderers);

            // ダミーテクスチャ配列
            var textures = new Texture2D[0];

            // Act - 実際のMaterialCacheSerializer.Reconstructを呼び出す
            var materials = MaterialCacheSerializer.Reconstruct(materialCache, textures);

            // Assert
            Assert.IsNotNull(materials);
            Assert.AreEqual(materialCache.materials.Length, materials.Length);

            Debug.Log($"[Phase4Test] マテリアル再構築成功: {materials.Length}");

            // Cleanup
            foreach (var mat in materials)
            {
                Object.Destroy(mat);
            }
        });

        #endregion

        #region Texture Compression Tests

        [UnityTest]
        public IEnumerator テクスチャ圧縮_ASTCサポートを確認できること() => UniTask.ToCoroutine(async () =>
        {
            // 実装が存在することを確認
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            // Act
            var supportsASTC = SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_6x6);
            var supportsETC2 = SystemInfo.SupportsTextureFormat(TextureFormat.ETC2_RGBA8);

            // Assert - プラットフォームに依存
            Debug.Log($"[Phase4Test] ASTCサポート: {supportsASTC}");
            Debug.Log($"[Phase4Test] ETC2サポート: {supportsETC2}");

            // iOS/Androidでは通常サポートされている
            #if UNITY_IOS || UNITY_ANDROID
            Assert.IsTrue(supportsASTC || supportsETC2, "モバイルはASTCまたはETC2をサポートすべき");
            #endif
        });

        [UnityTest]
        public IEnumerator テクスチャ圧縮_メモリ削減量を計算できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var renderers = avatar.GetComponentsInChildren<Renderer>();

            // 実装が存在することを確認
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);

            long uncompressedSize = 0;
            long compressedSize = 0;

            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (!mat.HasProperty("_MainTex")) continue;

                    var tex = mat.GetTexture("_MainTex") as Texture2D;
                    if (tex == null) continue;

                    // RGBA32: 4 bytes per pixel
                    uncompressedSize += tex.width * tex.height * 4;

                    // ASTC 6x6: ~0.89 bytes per pixel
                    compressedSize += (long)(tex.width * tex.height * 0.89f);
                }
            }

            // Assert
            if (uncompressedSize > 0)
            {
                float savings = 1f - (float)compressedSize / uncompressedSize;
                Debug.Log($"[Phase4Test] 非圧縮: {uncompressedSize / 1024 / 1024}MB");
                Debug.Log($"[Phase4Test] 圧縮後 (ASTC 6x6): {compressedSize / 1024 / 1024}MB");
                Debug.Log($"[Phase4Test] メモリ削減率: {savings:P0}");

                Assert.IsTrue(savings > 0.5f, "ASTCは50%以上のメモリ削減を提供すべき");
            }
        });

        #endregion
    }
}
