using System;
using System.Collections.Generic;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// マテリアルのシリアライザー
    /// </summary>
    public static class MaterialCacheSerializer
    {
        private const int CURRENT_VERSION = 1;

        /// <summary>
        /// レンダラーからマテリアル情報を抽出
        /// </summary>
        public static MaterialCache ExtractFromRenderers(Renderer[] renderers)
        {
            if (renderers == null)
                throw new ArgumentNullException(nameof(renderers));

            var materialInfoList = new List<MaterialInfo>();
            var processedMaterials = new HashSet<Material>();

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null || processedMaterials.Contains(material))
                        continue;

                    processedMaterials.Add(material);

                    var info = new MaterialInfo
                    {
                        name = material.name,
                        shaderName = material.shader != null ? material.shader.name : "Standard",
                        renderQueue = material.renderQueue
                    };

                    // MainTex ID
                    if (material.HasProperty("_MainTex"))
                    {
                        var tex = material.GetTexture("_MainTex") as Texture2D;
                        if (tex != null)
                        {
                            info.mainTexId = GenerateTextureId(tex);
                        }
                    }

                    // Color
                    if (material.HasProperty("_Color"))
                    {
                        var color = material.GetColor("_Color");
                        info.color = new float[] { color.r, color.g, color.b, color.a };
                    }
                    else
                    {
                        info.color = new float[] { 1, 1, 1, 1 };
                    }

                    // Metallic
                    if (material.HasProperty("_Metallic"))
                    {
                        info.metallic = material.GetFloat("_Metallic");
                    }

                    // Smoothness / Glossiness
                    if (material.HasProperty("_Glossiness"))
                    {
                        info.smoothness = material.GetFloat("_Glossiness");
                    }
                    else if (material.HasProperty("_Smoothness"))
                    {
                        info.smoothness = material.GetFloat("_Smoothness");
                    }

                    materialInfoList.Add(info);
                }
            }

            return new MaterialCache
            {
                version = CURRENT_VERSION,
                materials = materialInfoList.ToArray()
            };
        }

        /// <summary>
        /// マテリアル情報をJSONにシリアライズ
        /// </summary>
        public static string SerializeToJson(MaterialCache cache)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            return JsonUtility.ToJson(cache, true);
        }

        /// <summary>
        /// JSONからマテリアル情報をデシリアライズ
        /// </summary>
        public static MaterialCache DeserializeFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            return JsonUtility.FromJson<MaterialCache>(json);
        }

        /// <summary>
        /// マテリアル情報からマテリアルを再構築
        /// </summary>
        public static Material[] Reconstruct(MaterialCache cache, Texture2D[] textures)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            if (cache.materials == null || cache.materials.Length == 0)
                return Array.Empty<Material>();

            // テクスチャIDからテクスチャへのマップを作成
            var textureMap = new Dictionary<string, Texture2D>();
            if (textures != null)
            {
                foreach (var tex in textures)
                {
                    if (tex != null && !string.IsNullOrEmpty(tex.name))
                    {
                        textureMap[tex.name] = tex;
                    }
                }
            }

            var materials = new Material[cache.materials.Length];

            for (int i = 0; i < cache.materials.Length; i++)
            {
                var info = cache.materials[i];
                materials[i] = CreateMaterial(info, textureMap);
            }

            return materials;
        }

        /// <summary>
        /// MaterialInfoからMaterialを作成
        /// </summary>
        private static Material CreateMaterial(MaterialInfo info, Dictionary<string, Texture2D> textureMap)
        {
            // シェーダーを検索
            var shader = Shader.Find(info.shaderName);
            if (shader == null)
            {
                // フォールバックシェーダー
                shader = Shader.Find("Standard");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                }
            }

            var material = new Material(shader);
            material.name = info.name;
            material.renderQueue = info.renderQueue;

            // Color
            if (info.color != null && info.color.Length >= 4 && material.HasProperty("_Color"))
            {
                material.SetColor("_Color", new Color(info.color[0], info.color[1], info.color[2], info.color[3]));
            }

            // BaseColor (URP)
            if (info.color != null && info.color.Length >= 4 && material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", new Color(info.color[0], info.color[1], info.color[2], info.color[3]));
            }

            // MainTex
            if (!string.IsNullOrEmpty(info.mainTexId) && textureMap.TryGetValue(info.mainTexId, out var mainTex))
            {
                if (material.HasProperty("_MainTex"))
                {
                    material.SetTexture("_MainTex", mainTex);
                }
                if (material.HasProperty("_BaseMap"))
                {
                    material.SetTexture("_BaseMap", mainTex);
                }
            }

            // Metallic
            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", info.metallic);
            }

            // Smoothness / Glossiness
            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", info.smoothness);
            }
            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", info.smoothness);
            }

            return material;
        }

        /// <summary>
        /// テクスチャIDを生成
        /// </summary>
        private static string GenerateTextureId(Texture2D texture)
        {
            var name = string.IsNullOrEmpty(texture.name) ? "unnamed" : texture.name;
            name = name.Replace("/", "_").Replace("\\", "_").Replace(":", "_");
            return $"{name}_{texture.GetInstanceID()}";
        }
    }
}
