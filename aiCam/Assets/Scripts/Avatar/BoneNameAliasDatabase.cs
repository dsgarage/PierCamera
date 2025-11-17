using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace AICam.Avatar
{
    /****************************************************
     *  BoneNameAliasDatabase (新フォーマット対応版)
     *  -----------------------------------------------
     *  - Resources/BoneAliasMap.json をロードして
     *    <HumanBoneName, List<Alias>> にマッピング
     *  - 新フォーマット対応： { "HumanBoneName": ["alias1", "alias2", ...] }
     *  - Editor からの追加 / 保存もサポート
     *  - 必須15ボーンの完全カバレッジ確保機能
     ****************************************************/
    public class BoneNameAliasDatabase
    {
        private Dictionary<string, List<string>> aliasMap;
        private readonly Dictionary<string, string> aliasToHuman = new(StringComparer.OrdinalIgnoreCase);

        public BoneNameAliasDatabase(string resPath) => Load(resPath);

        // -------------------- JSON ロード（新フォーマット対応） --------------------
        private void Load(string path)
        {
            TextAsset json = Resources.Load<TextAsset>(path);
            if (json == null)
            {
                Debug.LogWarning($"[BoneAliasDB] JSON not found: Resources/{path}.json");
                InitializeDefaultMapping();
                return;
            }

            try
            {
                // 新フォーマット対応の簡単なパーサーを使用
                ParseJsonFormat(json.text);

                // 逆引き辞書構築
                aliasToHuman.Clear();
                foreach (var kvp in aliasMap)
                {
                    foreach (string alias in kvp.Value)
                    {
                        if (!aliasToHuman.ContainsKey(alias))
                            aliasToHuman[alias] = kvp.Key;
                    }
                }

                Debug.Log($"[BoneAliasDB] Loaded {aliasMap.Count} bone mappings from {path}");
                EnsureRequiredBonesMapping();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BoneAliasDB] Failed to parse JSON: {ex.Message}");
                InitializeDefaultMapping();
            }
        }

        // -------------------- シンプルJSONパーサー --------------------
        private void ParseJsonFormat(string jsonText)
        {
            aliasMap = new Dictionary<string, List<string>>();

            // Remove whitespace and braces
            jsonText = jsonText.Trim();
            if (jsonText.StartsWith("{")) jsonText = jsonText.Substring(1);
            if (jsonText.EndsWith("}")) jsonText = jsonText.Substring(0, jsonText.Length - 1);

            // Split by lines and process each entry
            string[] lines = jsonText.Split('\n');
            string currentKey = null;
            List<string> currentAliases = new List<string>();
            bool inArray = false;

            foreach (string line in lines)
            {
                string trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine == ",") continue;

                if (trimmedLine.Contains(":") && trimmedLine.Contains("\""))
                {
                    // New key found
                    if (currentKey != null && currentAliases.Count > 0)
                    {
                        aliasMap[currentKey] = new List<string>(currentAliases);
                        currentAliases.Clear();
                    }

                    // Extract key
                    int colonIndex = trimmedLine.IndexOf(':');
                    if (colonIndex > 0)
                    {
                        string keyPart = trimmedLine.Substring(0, colonIndex).Trim();
                        currentKey = keyPart.Trim('"', ' ', '\t');

                        // Check if array starts on same line
                        string valuePart = trimmedLine.Substring(colonIndex + 1).Trim();
                        if (valuePart.StartsWith("["))
                        {
                            inArray = true;
                            string arrayContent = valuePart.Substring(1); // Remove [
                            if (arrayContent.EndsWith("],") || arrayContent.EndsWith("]"))
                            {
                                // Single line array
                                arrayContent = arrayContent.TrimEnd(',', ']', ' ', '\t');
                                ParseArrayContent(arrayContent, currentAliases);
                                if (currentKey != null)
                                {
                                    aliasMap[currentKey] = new List<string>(currentAliases);
                                    currentAliases.Clear();
                                    currentKey = null;
                                }
                                inArray = false;
                            }
                            else
                            {
                                ParseArrayContent(arrayContent, currentAliases);
                            }
                        }
                    }
                }
                else if (inArray)
                {
                    // Continue parsing array
                    string arrayLine = trimmedLine;
                    if (arrayLine.EndsWith("],") || arrayLine.EndsWith("]"))
                    {
                        // End of array
                        arrayLine = arrayLine.TrimEnd(',', ']', ' ', '\t');
                        ParseArrayContent(arrayLine, currentAliases);
                        if (currentKey != null)
                        {
                            aliasMap[currentKey] = new List<string>(currentAliases);
                            currentAliases.Clear();
                            currentKey = null;
                        }
                        inArray = false;
                    }
                    else
                    {
                        ParseArrayContent(arrayLine, currentAliases);
                    }
                }
            }

            // Handle last entry
            if (currentKey != null && currentAliases.Count > 0)
            {
                aliasMap[currentKey] = new List<string>(currentAliases);
            }
        }

        private void ParseArrayContent(string content, List<string> aliases)
        {
            if (string.IsNullOrEmpty(content)) return;

            // Split by comma and clean each value
            string[] values = content.Split(',');
            foreach (string value in values)
            {
                string cleanValue = value.Trim().Trim('"', ' ', '\t');
                if (!string.IsNullOrEmpty(cleanValue))
                {
                    aliases.Add(cleanValue);
                }
            }
        }

        // -------------------- 必須ボーンのマッピング確保 --------------------
        private void EnsureRequiredBonesMapping()
        {
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (!HumanTrait.RequiredBone(i)) continue;

                string boneName = HumanTrait.BoneName[i];
                if (!aliasMap.ContainsKey(boneName))
                {
                    // 必須ボーンがマッピングにない場合は最低限自分自身を追加
                    aliasMap[boneName] = new List<string> { boneName };
                    aliasToHuman[boneName] = boneName;
                    Debug.LogWarning($"[BoneAliasDB] Added missing required bone mapping: {boneName}");
                }
            }
        }

        // -------------------- デフォルトマッピング初期化 --------------------
        private void InitializeDefaultMapping()
        {
            aliasMap = new Dictionary<string, List<string>>();
            aliasToHuman.Clear();

            // 必須15ボーンの最低限マッピングを作成
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (!HumanTrait.RequiredBone(i)) continue;

                string boneName = HumanTrait.BoneName[i];
                aliasMap[boneName] = new List<string> { boneName };
                aliasToHuman[boneName] = boneName;
            }

            Debug.LogWarning("[BoneAliasDB] Initialized with default mapping for required bones only");
        }

        // -------------------- Alias 追加 (Editor 用) --------------------
        public void AddAlias(string boneName, string alias)
        {
            if (aliasMap == null) aliasMap = new Dictionary<string, List<string>>();

            if (!aliasMap.TryGetValue(boneName, out var list))
                aliasMap[boneName] = list = new List<string>();

            if (!list.Contains(alias, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(alias);
                if (!aliasToHuman.ContainsKey(alias))
                    aliasToHuman[alias] = boneName;
            }
        }

        // -------------------- JSON 保存 (新フォーマット) --------------------
        public void Save(string resPath)
        {
#if UNITY_EDITOR
            if (aliasMap == null) aliasMap = new Dictionary<string, List<string>>();

            // 新フォーマットで手動構築: { "HumanBoneName": ["alias1", "alias2", ...] }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("{");

            bool first = true;
            foreach (var kvp in aliasMap)
            {
                if (!first) sb.AppendLine(",");
                first = false;

                sb.Append($"    \"{kvp.Key}\": [");
                for (int i = 0; i < kvp.Value.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append($"\"{kvp.Value[i]}\"");
                }
                sb.Append("]");
            }

            sb.AppendLine();
            sb.AppendLine("}");

            File.WriteAllText(resPath, sb.ToString());
            AssetDatabase.Refresh();
            Debug.Log($"[BoneAliasDB] Saved {aliasMap.Count} bone mappings to {resPath}");
#else
            Debug.LogWarning("[BoneAliasDB] Save() is Editor-only.");
#endif
        }

        // -------------------- 取得メソッド --------------------
        public List<string> GetAliases(string humanBone)
        {
            if (aliasMap == null) InitializeDefaultMapping();
            return aliasMap.TryGetValue(humanBone, out var l)
                   ? new List<string>(l)
                   : new List<string> { humanBone };
        }

        public string FindHumanBoneNameByAlias(string boneName)
        {
            if (aliasToHuman == null) return null;
            return aliasToHuman.TryGetValue(boneName, out var human) ? human : null;
        }

        public IEnumerable<BoneAliasData> GetAllEntries()
        {
            if (aliasMap == null) yield break;
            foreach (var kv in aliasMap)
                yield return new BoneAliasData
                {
                    humanBoneName = kv.Key,
                    aliases = kv.Value
                };
        }

        // -------------------- 必須ボーンカバレッジ確認 --------------------
        public (int required, int covered, float percentage) GetRequiredBonesCoverage()
        {
            int totalRequired = 0;
            int coveredRequired = 0;

            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (!HumanTrait.RequiredBone(i)) continue;

                totalRequired++;
                string boneName = HumanTrait.BoneName[i];
                if (aliasMap != null && aliasMap.ContainsKey(boneName) && aliasMap[boneName].Count > 0)
                {
                    coveredRequired++;
                }
            }

            float percentage = totalRequired > 0 ? (float)coveredRequired / totalRequired * 100f : 0f;
            return (totalRequired, coveredRequired, percentage);
        }

        // -------------------- DTO (Editor / Runtime 共用) --------------------
        [Serializable]
        public class BoneAliasData
        {
            public string humanBoneName;
            public List<string> aliases;
        }

        [Serializable]
        public class BoneAliasDataList
        {
            public List<BoneAliasData> entries;
        }
    }
}
