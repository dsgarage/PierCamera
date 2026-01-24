using System;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// Humanoidマッピングキャッシュデータ
    /// </summary>
    [Serializable]
    public class HumanoidCache
    {
        public int version;
        public HumanBoneMapping[] mappings;
    }

    /// <summary>
    /// HumanBone マッピング
    /// </summary>
    [Serializable]
    public class HumanBoneMapping
    {
        public string humanBoneName;
        public string bonePath;
    }
}
