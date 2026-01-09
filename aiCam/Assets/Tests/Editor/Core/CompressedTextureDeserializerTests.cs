using System;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using AICam.Core.Texture;
using UniGLTF;

namespace AICam.Core.Tests
{
    /// <summary>
    /// CompressedTextureDeserializer のユニットテスト
    /// Phase 4: VRMテクスチャメモリ最適化
    /// </summary>
    [TestFixture]
    public class CompressedTextureDeserializerTests
    {
        private string _tempDirectory;
        private string _testPngPath;
        private string _testJpgPath;
        private byte[] _pngBytes;
        private byte[] _jpgBytes;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _tempDirectory = Path.Combine(Application.temporaryCachePath, "CompressedTextureDeserializerTests");
            Directory.CreateDirectory(_tempDirectory);

            // テスト用のPNG画像を作成 (32x32 赤色)
            _testPngPath = Path.Combine(_tempDirectory, "test.png");
            CreateTestPng(_testPngPath, 32, 32, Color.red);
            _pngBytes = File.ReadAllBytes(_testPngPath);

            // テスト用のJPG画像を作成 (64x64 青色)
            _testJpgPath = Path.Combine(_tempDirectory, "test.jpg");
            CreateTestJpg(_testJpgPath, 64, 64, Color.blue);
            _jpgBytes = File.ReadAllBytes(_testJpgPath);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        private void CreateTestPng(string path, int width, int height, Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private void CreateTestJpg(string path, int width, int height, Color color)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = color;
            }
            texture.SetPixels(pixels);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToJPG(90));
            UnityEngine.Object.DestroyImmediate(texture);
        }

        #region 初期化テスト

        [Test]
        public void Constructor_Default_CreatesInstance()
        {
            // Act
            var deserializer = new CompressedTextureDeserializer();

            // Assert
            Assert.IsNotNull(deserializer);
        }

        [Test]
        public void Constructor_DisableCompression_CreatesInstance()
        {
            // Act
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);

            // Assert
            Assert.IsNotNull(deserializer);
            Assert.IsFalse(deserializer.IsCompressionAvailable);
        }

        [Test]
        public void IsCompressionAvailable_ReturnsExpectedValue()
        {
            // Arrange
            var deserializer = new CompressedTextureDeserializer(enableCompression: true);

            // Assert
#if RUNTIME_TEXTURE_COMPRESSOR
            // パッケージがインストールされている場合はtrue
            Assert.IsTrue(deserializer.IsCompressionAvailable);
#else
            // パッケージがインストールされていない場合はfalse
            Assert.IsFalse(deserializer.IsCompressionAvailable);
#endif
        }

        #endregion

        #region テクスチャロードテスト

        [Test]
        public async Task LoadTextureAsync_ValidPng_ReturnsTexture()
        {
            // Arrange
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);
            var textureInfo = CreateTextureInfo(_pngBytes, "image/png");
            var awaitCaller = new ImmediateCaller();

            // Act
            var texture = await deserializer.LoadTextureAsync(textureInfo, awaitCaller);

            // Assert
            Assert.IsNotNull(texture);
            Assert.AreEqual(32, texture.width);
            Assert.AreEqual(32, texture.height);

            // クリーンアップ
            UnityEngine.Object.DestroyImmediate(texture);
        }

        [Test]
        public async Task LoadTextureAsync_ValidJpg_ReturnsTexture()
        {
            // Arrange
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);
            var textureInfo = CreateTextureInfo(_jpgBytes, "image/jpeg");
            var awaitCaller = new ImmediateCaller();

            // Act
            var texture = await deserializer.LoadTextureAsync(textureInfo, awaitCaller);

            // Assert
            Assert.IsNotNull(texture);
            Assert.AreEqual(64, texture.width);
            Assert.AreEqual(64, texture.height);

            // クリーンアップ
            UnityEngine.Object.DestroyImmediate(texture);
        }

        [Test]
        public async Task LoadTextureAsync_NullImageData_ReturnsNull()
        {
            // Arrange
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);
            var textureInfo = CreateTextureInfo(null, "image/png");
            var awaitCaller = new ImmediateCaller();

            // Act
            var texture = await deserializer.LoadTextureAsync(textureInfo, awaitCaller);

            // Assert
            Assert.IsNull(texture);
        }

        [Test]
        public async Task LoadTextureAsync_EmptyImageData_ReturnsNull()
        {
            // Arrange
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);
            var textureInfo = CreateTextureInfo(new byte[0], "image/png");
            var awaitCaller = new ImmediateCaller();

            // Act
            var texture = await deserializer.LoadTextureAsync(textureInfo, awaitCaller);

            // Assert
            Assert.IsNull(texture);
        }

        #endregion

        #region フォールバックテスト

        [Test]
        public async Task LoadTextureAsync_WithoutCompressor_FallsBackToDefault()
        {
            // Arrange - 圧縮無効の状態
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);
            var textureInfo = CreateTextureInfo(_pngBytes, "image/png");
            var awaitCaller = new ImmediateCaller();

            // Act
            var texture = await deserializer.LoadTextureAsync(textureInfo, awaitCaller);

            // Assert
            Assert.IsNotNull(texture);
            // デフォルトロードではRGBA32またはRGB24フォーマット
            Assert.That(texture.format, Is.EqualTo(TextureFormat.RGBA32).Or.EqualTo(TextureFormat.ARGB32).Or.EqualTo(TextureFormat.RGB24));

            // クリーンアップ
            UnityEngine.Object.DestroyImmediate(texture);
        }

        #endregion

        #region テクスチャ設定テスト

        [Test]
        public async Task LoadTextureAsync_AppliesFilterMode()
        {
            // Arrange
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);
            var textureInfo = CreateTextureInfo(_pngBytes, "image/png", filterMode: FilterMode.Point);
            var awaitCaller = new ImmediateCaller();

            // Act
            var texture = await deserializer.LoadTextureAsync(textureInfo, awaitCaller);

            // Assert
            Assert.IsNotNull(texture);
            Assert.AreEqual(FilterMode.Point, texture.filterMode);

            // クリーンアップ
            UnityEngine.Object.DestroyImmediate(texture);
        }

        [Test]
        public async Task LoadTextureAsync_AppliesWrapMode()
        {
            // Arrange
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);
            var textureInfo = CreateTextureInfo(_pngBytes, "image/png",
                wrapModeU: TextureWrapMode.Repeat,
                wrapModeV: TextureWrapMode.Mirror);
            var awaitCaller = new ImmediateCaller();

            // Act
            var texture = await deserializer.LoadTextureAsync(textureInfo, awaitCaller);

            // Assert
            Assert.IsNotNull(texture);
            Assert.AreEqual(TextureWrapMode.Repeat, texture.wrapModeU);
            Assert.AreEqual(TextureWrapMode.Mirror, texture.wrapModeV);

            // クリーンアップ
            UnityEngine.Object.DestroyImmediate(texture);
        }

        #endregion

        #region キャッシュテスト

        [Test]
        public void ClearCache_DoesNotThrow()
        {
            // Arrange
            var deserializer = new CompressedTextureDeserializer(enableCompression: false);

            // Act & Assert
            Assert.DoesNotThrow(() => deserializer.ClearCache());
        }

        #endregion

        #region ヘルパーメソッド

        private DeserializingTextureInfo CreateTextureInfo(
            byte[] imageData,
            string mimeType,
            bool useMipmap = false,
            UniGLTF.ColorSpace colorSpace = UniGLTF.ColorSpace.Gamma,
            FilterMode filterMode = FilterMode.Bilinear,
            TextureWrapMode wrapModeU = TextureWrapMode.Clamp,
            TextureWrapMode wrapModeV = TextureWrapMode.Clamp)
        {
            return new DeserializingTextureInfo(
                imageData: imageData,
                dataMimeType: mimeType,
                colorSpace: colorSpace,
                useMipmap: useMipmap,
                filterMode: filterMode,
                wrapModeU: wrapModeU,
                wrapModeV: wrapModeV
            );
        }

        #endregion
    }
}
