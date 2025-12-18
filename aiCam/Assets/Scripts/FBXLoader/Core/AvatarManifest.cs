using UnityEngine;
using System;
using System.IO;
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
        public HumanoidBoneInfo humanoidBones;

        [Serializable]
        public class HumanoidBoneInfo
        {
            public bool isValid;
            public bool isHuman;
            public int boneCount;
        }

        public static string GetManifestPath(string modelFilePath)
        {
            if (string.IsNullOrEmpty(modelFilePath)) return string.Empty;
            string directory = Path.GetDirectoryName(modelFilePath);
            string baseName = Path.GetFileNameWithoutExtension(modelFilePath);
            return Path.Combine(directory, $"{baseName}_manifest.json");
        }

        public static AvatarManifest LoadFromFile(string path)
        {
            // スタブ実装
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return null;
            }

            try
            {
                string json = File.ReadAllText(path);
                return JsonUtility.FromJson<AvatarManifest>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarManifest] Failed to load from file: {e.Message}");
                return null;
            }
        }

        public static AvatarManifest Load(string path)
        {
            return LoadFromFile(path);
        }

        public static void Save(AvatarManifest manifest, string path)
        {
            // スタブ実装
            try
            {
                string json = JsonUtility.ToJson(manifest, true);
                File.WriteAllText(path, json);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarManifest] Failed to save to file: {e.Message}");
            }
        }

        public static AvatarManifest CreateFromVRM(string modelFilePath, GameObject model, string vrmVersion)
        {
            var manifest = new AvatarManifest
            {
                FilePath = modelFilePath,
                AvatarName = model != null ? model.name : Path.GetFileNameWithoutExtension(modelFilePath),
                AvatarId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.Now
            };
            return manifest;
        }

        public static AvatarManifest CreateFromFBX(string modelFilePath, GameObject model)
        {
            var manifest = new AvatarManifest
            {
                FilePath = modelFilePath,
                AvatarName = model != null ? model.name : Path.GetFileNameWithoutExtension(modelFilePath),
                AvatarId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.Now
            };
            return manifest;
        }

        public bool IsEmpty()
        {
            return string.IsNullOrEmpty(FilePath) && string.IsNullOrEmpty(AvatarName);
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
