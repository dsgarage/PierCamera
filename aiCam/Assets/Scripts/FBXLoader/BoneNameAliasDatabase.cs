using System.Collections.Generic;
using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// ボーン名のエイリアスデータベース
    /// Resources/BoneAliasMap.jsonからロードする（オプショナル）
    /// </summary>
    public class BoneNameAliasDatabase
    {
        private Dictionary<string, List<string>> aliasMap = new();

        public BoneNameAliasDatabase(string resourcePath = "BoneAliasMap")
        {
            // Resourcesからロード（存在しない場合はスキップ）
            var jsonAsset = Resources.Load<TextAsset>(resourcePath);
            if (jsonAsset != null)
            {
                try
                {
                    var data = JsonUtility.FromJson<AliasMapData>(jsonAsset.text);
                    if (data != null && data.aliases != null)
                    {
                        foreach (var entry in data.aliases)
                        {
                            aliasMap[entry.bone] = new List<string>(entry.names);
                        }
                        Debug.Log($"[BoneNameAliasDatabase] Loaded {aliasMap.Count} bone aliases from {resourcePath}");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[BoneNameAliasDatabase] Failed to parse {resourcePath}: {e.Message}");
                }
            }
            else
            {
                Debug.Log($"[BoneNameAliasDatabase] No alias file found at Resources/{resourcePath} (optional)");
            }
        }

        /// <summary>
        /// 指定されたボーン名に対するエイリアスリストを取得
        /// </summary>
        public IEnumerable<string> GetAliases(string boneName)
        {
            if (aliasMap.TryGetValue(boneName, out var aliases))
                return aliases;
            return System.Array.Empty<string>();
        }

        [System.Serializable]
        private class AliasMapData
        {
            public AliasEntry[] aliases;
        }

        [System.Serializable]
        private class AliasEntry
        {
            public string bone;
            public string[] names;
        }
    }
}
