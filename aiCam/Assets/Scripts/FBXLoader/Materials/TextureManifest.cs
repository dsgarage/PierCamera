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
        public List<TextureManifestEntry> Entries { get; set; } = new List<TextureManifestEntry>();

        public TextureManifestEntry FindByGuid(string guid)
        {
            return Entries.Find(e => e.Guid == guid);
        }

        public TextureManifestEntry FindByPath(string path)
        {
            return Entries.Find(e => e.Path == path);
        }

        public void AddEntry(TextureManifestEntry entry)
        {
            Entries.Add(entry);
        }

        public void RemoveEntry(string guid)
        {
            Entries.RemoveAll(e => e.Guid == guid);
        }
    }

    [Serializable]
    public class TextureManifestEntry
    {
        public string Guid { get; set; }
        public string Path { get; set; }
        public string Name { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
    }
}
