using System;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// BlendShapeキャッシュヘッダー
    /// </summary>
    [Serializable]
    public class BlendShapeCacheHeader
    {
        public const string MAGIC = "BLND";
        public int version;
        public int meshCount;
    }

    /// <summary>
    /// メッシュごとのBlendShape情報
    /// </summary>
    [Serializable]
    public class MeshBlendShapeInfo
    {
        public string meshName;
        public int blendShapeCount;
        public string[] blendShapeNames;
    }
}
