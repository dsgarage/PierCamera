using System;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// 表情マニフェスト
    /// </summary>
    [Serializable]
    public class ExpressionManifest
    {
        public int version;
        public ExpressionEntry[] expressions;
    }

    /// <summary>
    /// 表情エントリ
    /// </summary>
    [Serializable]
    public class ExpressionEntry
    {
        public int index;
        public string name;
        public string preset;
        public string iconPath;
        public string dataPath;
    }

    /// <summary>
    /// 表情データ
    /// </summary>
    [Serializable]
    public class ExpressionData
    {
        public int version;
        public string name;
        public string preset;
        public BlendShapeValue[] blendShapeValues;
    }

    /// <summary>
    /// BlendShape値
    /// </summary>
    [Serializable]
    public class BlendShapeValue
    {
        public string name;
        public float value;
    }
}
