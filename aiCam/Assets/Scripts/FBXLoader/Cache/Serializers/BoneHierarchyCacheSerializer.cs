using System;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// ボーン階層のシリアライザー
    /// </summary>
    public static class BoneHierarchyCacheSerializer
    {
        /// <summary>
        /// アバターからボーン階層を抽出
        /// </summary>
        public static BoneHierarchyCache ExtractFromAvatar(GameObject avatar)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ボーン階層をJSONにシリアライズ
        /// </summary>
        public static string SerializeToJson(BoneHierarchyCache cache)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// JSONからボーン階層をデシリアライズ
        /// </summary>
        public static BoneHierarchyCache DeserializeFromJson(string json)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// ボーン階層からGameObjectを再構築
        /// </summary>
        public static GameObject Reconstruct(BoneHierarchyCache cache)
        {
            throw new NotImplementedException();
        }
    }
}
