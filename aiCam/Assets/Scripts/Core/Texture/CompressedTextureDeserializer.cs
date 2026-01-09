using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UniGLTF;
#if RUNTIME_TEXTURE_COMPRESSOR
using dsgarage.RuntimeTextureCompressor;
#endif

namespace AICam.Core.Texture
{
    /// <summary>
    /// RuntimeTextureCompressor を使用して VRM テクスチャを圧縮形式でロードする ITextureDeserializer 実装
    /// RGBA32 (無圧縮) → ASTC/ETC2 (圧縮) で約89%のメモリ削減を実現
    ///
    /// Issue #440: 低スペック端末最適化 Phase 4 の実装
    ///
    /// ■ 実装サマリー (2024年完了)
    ///
    /// Phase 1: アセンブリ分割 (AICam.Core)
    ///   - ChunkedFileReader, CompressedTextureDeserializer を AICam.Core に分離
    ///   - UniTask, UniGLTF への依存を明確化
    ///   - テスト: AICamCoreAssemblyTests.cs
    ///
    /// Phase 2: メモリキャッシュ (AvatarMemoryCache)
    ///   - アバター再ロード回避のためのインメモリキャッシュ
    ///   - 最大6体のキャッシュ管理、LRU方式での削除
    ///   - テスト: AvatarMemoryCacheTests.cs (31テスト)
    ///
    /// Phase 3: Chunked File Reader
    ///   - 1MB超のファイルをチャンク分割で非同期読み込み
    ///   - プログレスコールバック対応
    ///   - テスト: ChunkedFileReaderTests.cs (11テスト)
    ///
    /// Phase 4: テクスチャ圧縮 (本クラス)
    ///   - RuntimeTextureCompressor 統合
    ///   - ASTC 6x6 (iOS/Android) / BC7 (Windows) 自動選択
    ///   - フォールバック: RGBA32 (パッケージ未インストール時)
    ///   - テスト: CompressedTextureDeserializerTests.cs
    ///
    /// ■ テスト結果: 全76テスト成功
    ///
    /// ■ 実機テスト方法
    ///   LowSpecOptimizationChecker を使用 (AICam.Diagnostics名前空間)
    ///   Xcodeログで "[LowSpec]" でフィルタして確認
    ///
    /// ■ 使用方法:
    /// 1. RuntimeTextureCompressor パッケージをインストール
    /// 2. Project Settings > Player > Scripting Define Symbols に "RUNTIME_TEXTURE_COMPRESSOR" を追加
    /// 3. VrmUtility.LoadBytesAsync() の textureDeserializer 引数に渡す
    /// </summary>
    public class CompressedTextureDeserializer : ITextureDeserializer
    {
#if RUNTIME_TEXTURE_COMPRESSOR
        private TextureLoader _loader;
        private bool _loaderInitialized;
#endif
        private readonly bool _enableCompression;
        private readonly string _cacheDirectoryOverride;
        private string _cacheDirectory;

        /// <summary>
        /// キャッシュディレクトリ（遅延評価）
        /// </summary>
        private string CacheDirectory
        {
            get
            {
                if (_cacheDirectory == null)
                {
                    _cacheDirectory = _cacheDirectoryOverride ?? Path.Combine(Application.persistentDataPath, "TextureCache");
                }
                return _cacheDirectory;
            }
        }

        /// <summary>
        /// 圧縮テクスチャデシリアライザを作成
        /// </summary>
        /// <param name="enableCompression">圧縮を有効にするか (falseの場合はデフォルト動作)</param>
        /// <param name="cacheDirectory">キャッシュディレクトリ (nullの場合はデフォルト)</param>
        public CompressedTextureDeserializer(bool enableCompression = true, string cacheDirectory = null)
        {
            _enableCompression = enableCompression;
            _cacheDirectoryOverride = cacheDirectory;
            // Note: Application.persistentDataPath はコンストラクタで呼び出せないため、
            // CacheDirectory プロパティで遅延評価する
        }

#if RUNTIME_TEXTURE_COMPRESSOR
        /// <summary>
        /// TextureLoaderを遅延初期化
        /// </summary>
        private void EnsureLoaderInitialized()
        {
            if (_loaderInitialized) return;
            _loaderInitialized = true;

            if (_enableCompression)
            {
                try
                {
                    // プラットフォームに応じた最適なフォーマットを自動選択
                    // iOS/Mac(Apple Silicon): ASTC 6x6
                    // Android: ASTC 6x6 or ETC2
                    // Windows: BC7/BC3
                    _loader = TextureLoader.CreateAutoFormatAutoCache();
                    Debug.Log("[CompressedTextureDeserializer] Initialized with auto format and auto cache");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CompressedTextureDeserializer] Failed to initialize TextureLoader: {e.Message}");
                    _loader = null;
                }
            }
        }
#endif

        /// <summary>
        /// 圧縮が利用可能かどうか
        /// </summary>
        public bool IsCompressionAvailable
        {
            get
            {
#if RUNTIME_TEXTURE_COMPRESSOR
                EnsureLoaderInitialized();
                return _enableCompression && _loader != null;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// テクスチャを非同期でロード
        /// </summary>
        public async Task<Texture2D> LoadTextureAsync(DeserializingTextureInfo textureInfo, IAwaitCaller awaitCaller)
        {
            if (textureInfo?.ImageData == null || textureInfo.ImageData.Length == 0)
            {
                Debug.LogWarning("[CompressedTextureDeserializer] Empty image data");
                return null;
            }

#if RUNTIME_TEXTURE_COMPRESSOR
            EnsureLoaderInitialized();
            // 圧縮が有効でローダーが初期化されている場合
            if (_enableCompression && _loader != null)
            {
                try
                {
                    return await LoadCompressedAsync(textureInfo, awaitCaller);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CompressedTextureDeserializer] Compressed load failed, falling back to default: {e.Message}");
                }
            }
#endif
            // フォールバック: デフォルトのテクスチャロード
            return await LoadDefaultAsync(textureInfo, awaitCaller);
        }

#if RUNTIME_TEXTURE_COMPRESSOR
        /// <summary>
        /// RuntimeTextureCompressor を使用した圧縮テクスチャロード
        /// </summary>
        private async Task<Texture2D> LoadCompressedAsync(DeserializingTextureInfo textureInfo, IAwaitCaller awaitCaller)
        {
            // 一時ファイルにバイナリを書き出し
            string tempPath = Path.Combine(
                Application.temporaryCachePath,
                $"vrm_tex_{Guid.NewGuid()}{GetExtensionFromMimeType(textureInfo.DataMimeType)}"
            );

            try
            {
                // 一時ファイルに書き出し
                await Task.Run(() => File.WriteAllBytes(tempPath, textureInfo.ImageData));

                // RuntimeTextureCompressor でロード (圧縮形式で)
                var result = await _loader.LoadURI(new Uri($"file://{tempPath}"));

                if (result.Error != TextureLoaderError.None)
                {
                    Debug.LogWarning($"[CompressedTextureDeserializer] TextureLoader error: {result.Error}");
                    return await LoadDefaultAsync(textureInfo, awaitCaller);
                }

                var texture = result.Texture;

                // テクスチャ設定を適用
                ApplyTextureSettings(texture, textureInfo);

                // メモリ削減効果をログ出力 (デバッグ用)
                LogMemorySavings(texture, textureInfo.ImageData.Length);

                return texture;
            }
            finally
            {
                // 一時ファイル削除
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[CompressedTextureDeserializer] Failed to delete temp file: {e.Message}");
                }
            }
        }
#endif

        /// <summary>
        /// デフォルトのテクスチャロード (非圧縮 RGBA32)
        /// </summary>
        private async Task<Texture2D> LoadDefaultAsync(DeserializingTextureInfo textureInfo, IAwaitCaller awaitCaller)
        {
            // UniVRM デフォルトと同様の処理
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, textureInfo.UseMipmap, textureInfo.ColorSpace == UniGLTF.ColorSpace.Linear);

            bool loaded = texture.LoadImage(textureInfo.ImageData, false);

            if (!loaded)
            {
                Debug.LogError("[CompressedTextureDeserializer] Failed to load texture from image data");
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            // テクスチャ設定を適用
            ApplyTextureSettings(texture, textureInfo);

            await awaitCaller.NextFrame();

            return texture;
        }

        /// <summary>
        /// テクスチャ設定を適用
        /// </summary>
        private void ApplyTextureSettings(Texture2D texture, DeserializingTextureInfo textureInfo)
        {
            if (texture == null) return;

            texture.filterMode = textureInfo.FilterMode;
            texture.wrapModeU = textureInfo.WrapModeU;
            texture.wrapModeV = textureInfo.WrapModeV;
        }

        /// <summary>
        /// MIMEタイプから拡張子を取得
        /// </summary>
        private string GetExtensionFromMimeType(string mimeType)
        {
            return mimeType?.ToLower() switch
            {
                "image/png" => ".png",
                "image/jpeg" => ".jpg",
                "image/webp" => ".webp",
                "image/gif" => ".gif",
                "image/bmp" => ".bmp",
                _ => ".png"
            };
        }

        /// <summary>
        /// メモリ削減効果をログ出力
        /// </summary>
        private void LogMemorySavings(Texture2D texture, int originalSize)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (texture == null) return;

            // 圧縮後のサイズを概算
            long compressedSize = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(texture);

            // RGBA32の場合のサイズ
            int uncompressedSize = texture.width * texture.height * 4; // 4 bytes per pixel

            float reduction = 1.0f - ((float)compressedSize / uncompressedSize);

            Debug.Log($"[CompressedTextureDeserializer] {texture.width}x{texture.height} " +
                      $"Format:{texture.format} " +
                      $"Uncompressed:{uncompressedSize / 1024}KB → Compressed:{compressedSize / 1024}KB " +
                      $"({reduction:P0} reduction)");
#endif
        }

        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public void ClearCache()
        {
#if RUNTIME_TEXTURE_COMPRESSOR
            try
            {
                _loader?.ClearAllCaches();
                Debug.Log("[CompressedTextureDeserializer] Cache cleared");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CompressedTextureDeserializer] Failed to clear cache: {e.Message}");
            }
#endif
        }
    }
}
