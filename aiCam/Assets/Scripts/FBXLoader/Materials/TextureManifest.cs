using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// UniSILが生成するtexture_manifest.jsonのデータ構造
    /// </summary>
    [Serializable]
    public class TextureManifest
    {
        [Serializable]
        public class TextureInfo
        {
            public string guid;
            public string relativePath;
            public string originalPath;
            public int width;
            public int height;
            public string format;
            public long fileSizeBytes;

            /// <summary>
            /// ファイル名（拡張子なし）を取得
            /// </summary>
            public string GetFileNameWithoutExtension()
            {
                return Path.GetFileNameWithoutExtension(relativePath);
            }

            /// <summary>
            /// ファイル名（拡張子あり）を取得
            /// </summary>
            public string GetFileName()
            {
                return Path.GetFileName(relativePath);
            }
        }

        public List<TextureInfo> textures = new List<TextureInfo>();
        public string buildDate;
        public int textureCount;
        public string unityVersion;
        public int manifestVersion;

        /// <summary>
        /// texture_manifest.jsonからTextureManifestを読み込む
        /// </summary>
        /// <param name="manifestPath">texture_manifest.jsonへのパス</param>
        /// <returns>読み込まれたTextureManifest。失敗時はnull</returns>
        public static TextureManifest LoadFromFile(string manifestPath)
        {
            try
            {
                if (!File.Exists(manifestPath))
                {
                    Debug.LogWarning($"[TextureManifest] Manifest file not found: {manifestPath}");
                    return null;
                }

                string json = File.ReadAllText(manifestPath);
                var manifest = JsonUtility.FromJson<TextureManifest>(json);

                if (manifest == null || manifest.textures == null)
                {
                    Debug.LogError($"[TextureManifest] Failed to parse manifest: {manifestPath}");
                    return null;
                }

                Debug.Log($"[TextureManifest] Loaded {manifest.textureCount} textures from manifest");
                return manifest;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TextureManifest] Error loading manifest: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// ファイル名（拡張子なし）でテクスチャ情報を検索
        /// </summary>
        /// <param name="fileNameWithoutExtension">ファイル名（拡張子なし）</param>
        /// <returns>見つかったTextureInfo。見つからない場合はnull</returns>
        public TextureInfo FindByFileName(string fileNameWithoutExtension)
        {
            return textures.Find(t =>
                t.GetFileNameWithoutExtension().Equals(fileNameWithoutExtension, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// 部分一致でテクスチャ情報を検索
        /// </summary>
        /// <param name="partialName">検索文字列</param>
        /// <returns>マッチしたすべてのTextureInfo</returns>
        public List<TextureInfo> FindByPartialName(string partialName)
        {
            return textures.FindAll(t =>
                t.GetFileNameWithoutExtension().IndexOf(partialName, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// GUIDでテクスチャ情報を検索
        /// </summary>
        /// <param name="guid">テクスチャのGUID</param>
        /// <returns>見つかったTextureInfo。見つからない場合はnull</returns>
        public TextureInfo FindByGuid(string guid)
        {
            return textures.Find(t => t.guid == guid);
        }
    }
}
