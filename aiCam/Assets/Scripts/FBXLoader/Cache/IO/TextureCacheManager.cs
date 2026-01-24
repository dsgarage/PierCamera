using System;
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
        public UniTask SaveTextureAsync(Texture2D texture, string textureId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// テクスチャをロード
        /// </summary>
        public UniTask<Texture2D> LoadTextureAsync(string textureId)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// マテリアルからテクスチャを抽出して保存
        /// </summary>
        public UniTask<string[]> ExtractAndSaveTexturesAsync(Material[] materials)
        {
            throw new NotImplementedException();
        }
    }
}
