using UnityEngine;
using System;
using System.Collections.Generic;

namespace AICam.FBXLoader
{
    /// <summary>
    /// テクスチャマニフェストのスタブクラス
    /// </summary>
    [Serializable]
    public class TextureManifest
    {
        public List<TextureManifestEntry> textures = new List<TextureManifestEntry>();

        public int textureCount => textures?.Count ?? 0;

        public List<TextureManifestEntry> Entries => textures;

        public TextureManifestEntry FindByGuid(string guid)
        {
            return textures?.Find(e => e.Guid == guid);
        }

        public TextureManifestEntry FindByPath(string path)
        {
            return textures?.Find(e => e.Path == path || e.relativePath == path);
        }

        public void AddEntry(TextureManifestEntry entry)
        {
            if (textures == null)
            {
                textures = new List<TextureManifestEntry>();
            }
            textures.Add(entry);
        }

        public void RemoveEntry(string guid)
        {
            textures?.RemoveAll(e => e.Guid == guid);
        }

        public bool IsValid()
        {
            return textures != null && textures.Count > 0;
        }
    }

    [Serializable]
    public class TextureManifestEntry
    {
        public string Guid;
        public string Path;
        public string Name;
        public string relativePath;
        public int Width;
        public int Height;
    }
}
