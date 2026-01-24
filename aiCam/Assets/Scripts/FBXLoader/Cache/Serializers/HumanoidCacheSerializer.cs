using System;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// Humanoidマッピングのシリアライザー
    /// </summary>
    public static class HumanoidCacheSerializer
    {
        /// <summary>
        /// AnimatorからHumanoidマッピングを抽出
        /// </summary>
        public static HumanoidCache ExtractFromAnimator(Animator animator)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// HumanoidマッピングをJSONにシリアライズ
        /// </summary>
        public static string SerializeToJson(HumanoidCache cache)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// JSONからHumanoidマッピングをデシリアライズ
        /// </summary>
        public static HumanoidCache DeserializeFromJson(string json)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// HumanoidマッピングをAvatarに適用
        /// </summary>
        public static Avatar CreateAvatar(HumanoidCache cache, GameObject root)
        {
            throw new NotImplementedException();
        }
    }
}
