using System;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// ボーン階層キャッシュデータ
    /// </summary>
    [Serializable]
    public class BoneHierarchyCache
    {
        public int version;
        public BoneInfo[] bones;
    }

    /// <summary>
    /// 個別ボーン情報
    /// </summary>
    [Serializable]
    public class BoneInfo
    {
        public string name;
        public string path;
        public int parentIndex;
        public float[] localPosition;  // Vector3
        public float[] localRotation;  // Quaternion
        public float[] localScale;     // Vector3
    }
}
