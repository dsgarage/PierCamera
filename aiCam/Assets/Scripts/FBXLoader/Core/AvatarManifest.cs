using UnityEngine;
using System;
using System.Collections.Generic;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバターマニフェストのスタブクラス
    /// </summary>
    [Serializable]
    public class AvatarManifest
    {
        public string AvatarId { get; set; }
        public string AvatarName { get; set; }
        public string FilePath { get; set; }
        public string IconPath { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUsedAt { get; set; }

        public List<AvatarManifestEntry> Entries { get; set; } = new List<AvatarManifestEntry>();

        public static AvatarManifest Load(string path)
        {
            // スタブ実装
            return new AvatarManifest();
        }

        public static void Save(AvatarManifest manifest, string path)
        {
            // スタブ実装
        }

        public void AddEntry(AvatarManifestEntry entry)
        {
            Entries.Add(entry);
        }

        public void RemoveEntry(string avatarId)
        {
            Entries.RemoveAll(e => e.AvatarId == avatarId);
        }

        public AvatarManifestEntry FindEntry(string avatarId)
        {
            return Entries.Find(e => e.AvatarId == avatarId);
        }
    }

    [Serializable]
    public class AvatarManifestEntry
    {
        public string AvatarId { get; set; }
        public string AvatarName { get; set; }
        public string FilePath { get; set; }
        public string IconPath { get; set; }
        public int SlotIndex { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime LastUsedAt { get; set; }
    }
}
