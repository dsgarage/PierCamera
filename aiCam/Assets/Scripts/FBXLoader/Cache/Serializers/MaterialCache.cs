using System;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// マテリアルキャッシュデータ
    /// </summary>
    [Serializable]
    public class MaterialCache
    {
        public int version;
        public MaterialInfo[] materials;
    }

    /// <summary>
    /// 個別マテリアル情報
    /// </summary>
    [Serializable]
    public class MaterialInfo
    {
        public string name;
        public string shaderName;
        public int renderQueue;
        public string mainTexId;
        public float[] color;      // Color (RGBA)
        public float metallic;
        public float smoothness;
    }
}
