using System;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// マテリアルのシリアライザー
    /// </summary>
    public static class MaterialCacheSerializer
    {
        /// <summary>
        /// レンダラーからマテリアル情報を抽出
        /// </summary>
        public static MaterialCache ExtractFromRenderers(Renderer[] renderers)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// マテリアル情報をJSONにシリアライズ
        /// </summary>
        public static string SerializeToJson(MaterialCache cache)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// JSONからマテリアル情報をデシリアライズ
        /// </summary>
        public static MaterialCache DeserializeFromJson(string json)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// マテリアル情報からマテリアルを再構築
        /// </summary>
        public static Material[] Reconstruct(MaterialCache cache, Texture2D[] textures)
        {
            throw new NotImplementedException();
        }
    }
}
