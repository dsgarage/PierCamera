using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AICam.AvatarCache.IO
{
    /// <summary>
    /// テクスチャキャッシュマネージャー
    /// テクスチャの保存・ロード・圧縮を担当
    /// </summary>
    public class TextureCacheManager
    {
        private readonly string _texturesDir;

        public TextureCacheManager(string texturesDir)
        {
            _texturesDir = texturesDir;
        }

        /// <summary>
        /// テクスチャをPNGとして保存
        /// </summary>
        public async UniTask SaveTextureAsync(Texture2D texture, string textureId)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            if (string.IsNullOrEmpty(textureId))
                throw new ArgumentNullException(nameof(textureId));

            if (!Directory.Exists(_texturesDir))
            {
                Directory.CreateDirectory(_texturesDir);
            }

            var filePath = Path.Combine(_texturesDir, textureId + ".png");

            // テクスチャが読み取り可能かチェック、または圧縮フォーマットかチェック
            // EncodeToPNG()は圧縮フォーマット（DXT, ASTC, ETC2等）をサポートしないため、
            // 圧縮テクスチャは必ずRGBA32にコピーする必要がある
            Texture2D readableTexture = texture;
            bool needsCopy = !texture.isReadable || IsCompressedFormat(texture.format);

            if (needsCopy)
            {
                // 読み取り可能なRGBA32コピーを作成
                readableTexture = CreateReadableTexture(texture);
            }

            try
            {
                var pngData = readableTexture.EncodeToPNG();
                if (pngData == null)
                {
                    throw new InvalidOperationException($"Failed to encode texture to PNG. Format: {texture.format}, Size: {texture.width}x{texture.height}");
                }
                await File.WriteAllBytesAsync(filePath, pngData);
            }
            finally
            {
                // コピーを作成した場合は破棄
                if (readableTexture != texture)
                {
                    UnityEngine.Object.Destroy(readableTexture);
                }
            }
        }

        /// <summary>
        /// 圧縮フォーマットかどうかをチェック
        /// EncodeToPNG()がサポートしないフォーマット
        /// </summary>
        private static bool IsCompressedFormat(TextureFormat format)
        {
            switch (format)
            {
                // DXT圧縮
                case TextureFormat.DXT1:
                case TextureFormat.DXT5:
                case TextureFormat.DXT1Crunched:
                case TextureFormat.DXT5Crunched:
                // ETC圧縮
                case TextureFormat.ETC_RGB4:
                case TextureFormat.ETC2_RGB:
                case TextureFormat.ETC2_RGBA1:
                case TextureFormat.ETC2_RGBA8:
                case TextureFormat.ETC_RGB4Crunched:
                case TextureFormat.ETC2_RGBA8Crunched:
                // ASTC圧縮
                case TextureFormat.ASTC_4x4:
                case TextureFormat.ASTC_5x5:
                case TextureFormat.ASTC_6x6:
                case TextureFormat.ASTC_8x8:
                case TextureFormat.ASTC_10x10:
                case TextureFormat.ASTC_12x12:
                // PVRTC圧縮（iOS）
                case TextureFormat.PVRTC_RGB2:
                case TextureFormat.PVRTC_RGB4:
                case TextureFormat.PVRTC_RGBA2:
                case TextureFormat.PVRTC_RGBA4:
                // BC圧縮
                case TextureFormat.BC4:
                case TextureFormat.BC5:
                case TextureFormat.BC6H:
                case TextureFormat.BC7:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// テクスチャをロード
        /// </summary>
        public async UniTask<Texture2D> LoadTextureAsync(string textureId)
        {
            if (string.IsNullOrEmpty(textureId))
                throw new ArgumentNullException(nameof(textureId));

            var filePath = Path.Combine(_texturesDir, textureId + ".png");

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Texture not found: {filePath}");

            var pngData = await File.ReadAllBytesAsync(filePath);

            var texture = new Texture2D(2, 2);
            if (!texture.LoadImage(pngData))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException($"Failed to load texture: {filePath}");
            }

            texture.name = textureId;
            return texture;
        }

        /// <summary>
        /// マテリアルからテクスチャを抽出して保存
        /// </summary>
        public async UniTask<string[]> ExtractAndSaveTexturesAsync(Material[] materials)
        {
            if (materials == null)
                throw new ArgumentNullException(nameof(materials));

            var textureIds = new List<string>();
            var processedTextures = new HashSet<Texture2D>();

            foreach (var material in materials)
            {
                if (material == null) continue;

                // MainTexを取得
                if (material.HasProperty("_MainTex"))
                {
                    var tex = material.GetTexture("_MainTex") as Texture2D;
                    if (tex != null && !processedTextures.Contains(tex))
                    {
                        processedTextures.Add(tex);

                        var textureId = GenerateTextureId(tex);
                        await SaveTextureAsync(tex, textureId);
                        textureIds.Add(textureId);
                    }
                }

                // BumpMap（法線マップ）
                if (material.HasProperty("_BumpMap"))
                {
                    var tex = material.GetTexture("_BumpMap") as Texture2D;
                    if (tex != null && !processedTextures.Contains(tex))
                    {
                        processedTextures.Add(tex);

                        var textureId = GenerateTextureId(tex);
                        await SaveTextureAsync(tex, textureId);
                        textureIds.Add(textureId);
                    }
                }
            }

            return textureIds.ToArray();
        }

        /// <summary>
        /// プラットフォームの圧縮フォーマットサポート情報を取得
        /// </summary>
        public static CompressionSupportInfo GetCompressionSupport()
        {
            var info = new CompressionSupportInfo();

            // プラットフォーム別のサポート判定
#if UNITY_IOS
            info.supportsASTC = SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_6x6);
            info.supportsETC2 = false;
            info.supportsDXT = false;
            info.recommendedFormat = info.supportsASTC ? TextureFormat.ASTC_6x6 : TextureFormat.RGBA32;
#elif UNITY_ANDROID
            info.supportsASTC = SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_6x6);
            info.supportsETC2 = SystemInfo.SupportsTextureFormat(TextureFormat.ETC2_RGBA8);
            info.supportsDXT = false;
            info.recommendedFormat = info.supportsASTC ? TextureFormat.ASTC_6x6 :
                                     info.supportsETC2 ? TextureFormat.ETC2_RGBA8 : TextureFormat.RGBA32;
#else
            info.supportsASTC = SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_6x6);
            info.supportsETC2 = SystemInfo.SupportsTextureFormat(TextureFormat.ETC2_RGBA8);
            info.supportsDXT = SystemInfo.SupportsTextureFormat(TextureFormat.DXT5);
            info.recommendedFormat = info.supportsDXT ? TextureFormat.DXT5 : TextureFormat.RGBA32;
#endif

            return info;
        }

        /// <summary>
        /// テクスチャ圧縮による削減量を計算
        /// </summary>
        public static CompressionSavingsInfo CalculateCompressionSavings(Renderer[] renderers)
        {
            var info = new CompressionSavingsInfo();

            if (renderers == null)
                return info;

            var processedTextures = new HashSet<Texture2D>();

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null) continue;

                    // MainTexを取得
                    if (material.HasProperty("_MainTex"))
                    {
                        var tex = material.GetTexture("_MainTex") as Texture2D;
                        if (tex != null && !processedTextures.Contains(tex))
                        {
                            processedTextures.Add(tex);

                            // 非圧縮サイズ（RGBA32 = 4 bytes per pixel）
                            long uncompressedSize = (long)tex.width * tex.height * 4;
                            info.uncompressedBytes += uncompressedSize;

                            // ASTC 6x6圧縮サイズ（約0.89 bytes per pixel）
                            long compressedSize = (long)tex.width * tex.height * 128 / (6 * 6 * 8);
                            info.compressedBytes += compressedSize;
                        }
                    }
                }
            }

            // 削減率を計算
            if (info.uncompressedBytes > 0)
            {
                info.savingsRatio = 1.0f - (float)info.compressedBytes / info.uncompressedBytes;
            }

            return info;
        }

        /// <summary>
        /// テクスチャIDを生成
        /// </summary>
        private static string GenerateTextureId(Texture2D texture)
        {
            // テクスチャ名とインスタンスIDを組み合わせてユニークなIDを生成
            var name = string.IsNullOrEmpty(texture.name) ? "unnamed" : texture.name;
            // ファイル名として使えない文字を置換
            name = name.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return $"{name}_{texture.GetInstanceID()}";
        }

        /// <summary>
        /// 読み取り可能なテクスチャコピーを作成
        /// </summary>
        private static Texture2D CreateReadableTexture(Texture2D source)
        {
            // RenderTextureを使用してコピー
            var renderTex = RenderTexture.GetTemporary(
                source.width, source.height, 0, RenderTextureFormat.ARGB32);

            Graphics.Blit(source, renderTex);

            var previous = RenderTexture.active;
            RenderTexture.active = renderTex;

            var readableTexture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            readableTexture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readableTexture.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTex);

            return readableTexture;
        }
    }

    /// <summary>
    /// 圧縮サポート情報
    /// </summary>
    public struct CompressionSupportInfo
    {
        public bool supportsASTC;
        public bool supportsETC2;
        public bool supportsDXT;
        public TextureFormat recommendedFormat;
    }

    /// <summary>
    /// 圧縮削減量情報
    /// </summary>
    public struct CompressionSavingsInfo
    {
        public long uncompressedBytes;
        public long compressedBytes;
        public float savingsRatio;
    }
}
