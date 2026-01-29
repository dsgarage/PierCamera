using System;
using System.Collections.Generic;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// 表情キャッシュのシリアライザー
    /// </summary>
    public static class ExpressionCacheSerializer
    {
        /// <summary>
        /// アバターからBlendShape名を抽出
        /// </summary>
        public static string[] ExtractBlendShapeNames(GameObject avatar)
        {
            if (avatar == null)
                throw new ArgumentNullException(nameof(avatar));

            var blendShapeNames = new List<string>();
            var processedNames = new HashSet<string>();

            var skinnedMeshRenderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh == null)
                    continue;

                var mesh = smr.sharedMesh;
                var blendShapeCount = mesh.blendShapeCount;

                for (int i = 0; i < blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);

                    // メッシュ名を含めた完全な名前を作成
                    var fullName = $"{smr.name}.{name}";

                    if (!processedNames.Contains(fullName))
                    {
                        processedNames.Add(fullName);
                        blendShapeNames.Add(fullName);
                    }
                }
            }

            return blendShapeNames.ToArray();
        }

        /// <summary>
        /// 表情データをJSONにシリアライズ
        /// </summary>
        public static string SerializeToJson(ExpressionData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return JsonUtility.ToJson(data, true);
        }

        /// <summary>
        /// JSONから表情データをデシリアライズ
        /// </summary>
        public static ExpressionData DeserializeFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            return JsonUtility.FromJson<ExpressionData>(json);
        }

        /// <summary>
        /// 表情マニフェストをJSONにシリアライズ
        /// </summary>
        public static string SerializeManifestToJson(ExpressionManifest manifest)
        {
            if (manifest == null)
                throw new ArgumentNullException(nameof(manifest));

            return JsonUtility.ToJson(manifest, true);
        }

        /// <summary>
        /// JSONから表情マニフェストをデシリアライズ
        /// </summary>
        public static ExpressionManifest DeserializeManifestFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            return JsonUtility.FromJson<ExpressionManifest>(json);
        }

        /// <summary>
        /// アバターから現在のBlendShape値を抽出
        /// </summary>
        public static BlendShapeValue[] ExtractCurrentBlendShapeValues(GameObject avatar)
        {
            if (avatar == null)
                throw new ArgumentNullException(nameof(avatar));

            var values = new List<BlendShapeValue>();
            var skinnedMeshRenderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh == null)
                    continue;

                var mesh = smr.sharedMesh;
                var blendShapeCount = mesh.blendShapeCount;

                for (int i = 0; i < blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    var weight = smr.GetBlendShapeWeight(i);

                    // 0でない値のみ保存
                    if (Mathf.Abs(weight) > 0.001f)
                    {
                        values.Add(new BlendShapeValue
                        {
                            name = $"{smr.name}.{name}",
                            value = weight / 100f // 0-100 → 0-1 に正規化
                        });
                    }
                }
            }

            return values.ToArray();
        }

        /// <summary>
        /// BlendShape値をアバターに適用
        /// </summary>
        public static void ApplyBlendShapeValues(GameObject avatar, BlendShapeValue[] values)
        {
            if (avatar == null)
                throw new ArgumentNullException(nameof(avatar));

            if (values == null || values.Length == 0)
                return;

            var skinnedMeshRenderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            // 名前からSMRとインデックスへのマップを作成
            var blendShapeMap = new Dictionary<string, (SkinnedMeshRenderer smr, int index)>();

            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh == null)
                    continue;

                var mesh = smr.sharedMesh;
                var blendShapeCount = mesh.blendShapeCount;

                for (int i = 0; i < blendShapeCount; i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    var fullName = $"{smr.name}.{name}";
                    blendShapeMap[fullName] = (smr, i);
                }
            }

            // 値を適用
            foreach (var value in values)
            {
                if (blendShapeMap.TryGetValue(value.name, out var entry))
                {
                    entry.smr.SetBlendShapeWeight(entry.index, value.value * 100f); // 0-1 → 0-100
                }
            }
        }
    }
}
