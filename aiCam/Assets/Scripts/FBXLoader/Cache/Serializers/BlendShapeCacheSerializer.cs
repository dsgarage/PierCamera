using System;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// BlendShapeのバイナリシリアライザー
    /// </summary>
    public static class BlendShapeCacheSerializer
    {
        public const string MAGIC = "BLND";

        /// <summary>
        /// BlendShapeをバイナリにシリアライズ
        /// </summary>
        public static void SerializeToBinary(SkinnedMeshRenderer[] smrs, string filePath)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// バイナリからBlendShapeをデシリアライズしてメッシュに適用
        /// </summary>
        public static void DeserializeAndApply(string filePath, Mesh[] meshes)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// バイナリファイルのマジックナンバーを検証
        /// </summary>
        public static bool ValidateMagic(string filePath)
        {
            throw new NotImplementedException();
        }
    }
}
