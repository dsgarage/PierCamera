using System;
using System.IO;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// メッシュのバイナリシリアライザー
    /// </summary>
    public static class MeshCacheSerializer
    {
        public const string MAGIC = "MESH";

        /// <summary>
        /// メッシュをバイナリにシリアライズ
        /// </summary>
        public static void SerializeToBinary(Mesh[] meshes, string filePath)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// バイナリからメッシュをデシリアライズ
        /// </summary>
        public static Mesh[] DeserializeFromBinary(string filePath)
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
