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
        // フィールド
        public string avatarName;
        public string modelFileName;
        public string fileType;
        public string vrmVersion;
        public string AvatarId;
        public string FilePath;
        public string IconPath;
        public DateTime CreatedAt;
        public DateTime LastUsedAt;

        public List<AvatarManifestEntry> Entries = new List<AvatarManifestEntry>();
        public HumanoidBoneInfo humanoidBones;

        [Serializable]
        public class HumanoidBoneInfo
        {
            public bool isValid;
            public bool isHuman;
            public int boneCount;
            public List<string> mappedBones = new List<string>();
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

        public void SaveToFile(string path)
        {
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                string json = JsonUtility.ToJson(this, true);
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
                avatarName = model != null ? model.name : Path.GetFileNameWithoutExtension(modelFilePath),
                modelFileName = Path.GetFileName(modelFilePath),
                fileType = "VRM",
                vrmVersion = vrmVersion,
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
                avatarName = model != null ? model.name : Path.GetFileNameWithoutExtension(modelFilePath),
                modelFileName = Path.GetFileName(modelFilePath),
                fileType = "FBX",
                AvatarId = Guid.NewGuid().ToString(),
                CreatedAt = DateTime.Now
            };
            return manifest;
        }

        public bool IsEmpty()
        {
            return string.IsNullOrEmpty(FilePath) && string.IsNullOrEmpty(avatarName);
        }
    }

    [Serializable]
    public class AvatarManifestEntry
    {
        public string AvatarId;
        public string AvatarName;
        public string FilePath;
        public string IconPath;
        public int SlotIndex;
        public DateTime CreatedAt;
        public DateTime LastUsedAt;
    }
}
