using System;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// メッシュキャッシュヘッダー
    /// </summary>
    [Serializable]
    public class MeshCacheHeader
    {
        public const string MAGIC = "MESH";
        public int version;
        public int meshCount;
    }

    /// <summary>
    /// 個別メッシュ情報
    /// </summary>
    [Serializable]
    public class MeshInfo
    {
        public string name;
        public int vertexCount;
        public int subMeshCount;
        public int[] triangleCounts;
        public bool hasNormals;
        public bool hasTangents;
        public bool hasUV;
        public bool hasUV2;
        public bool hasColors;
        public bool hasBoneWeights;
    }
}
