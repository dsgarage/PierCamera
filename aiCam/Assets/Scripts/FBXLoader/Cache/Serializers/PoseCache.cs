using System;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// ポーズマニフェスト
    /// </summary>
    [Serializable]
    public class PoseManifest
    {
        public int version;
        public PoseEntry[] poses;
    }

    /// <summary>
    /// ポーズエントリ
    /// </summary>
    [Serializable]
    public class PoseEntry
    {
        public int index;
        public string name;
        public string iconPath;
        public string animationPath;
        public bool isDefault;
    }

    /// <summary>
    /// アニメーションキャッシュヘッダー
    /// </summary>
    [Serializable]
    public class AnimationCacheHeader
    {
        public const string MAGIC = "ANIM";
        public int version;
        public string clipName;
        public float frameRate;
        public float length;
        public int wrapMode;
    }
}
