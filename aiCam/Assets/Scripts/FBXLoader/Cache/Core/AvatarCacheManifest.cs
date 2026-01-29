using System;

namespace AICam.AvatarCache
{
    /// <summary>
    /// アバターキャッシュマニフェスト
    /// 各キャッシュディレクトリのmanifest.jsonにシリアライズされる
    /// </summary>
    [Serializable]
    public class AvatarCacheManifest
    {
        public int cacheFormatVersion;
        public string cacheId;
        public string originalFileName;
        public string createdAt;
        public string unityVersion;
        public string platform;

        // VRMメタデータ
        public string vrmTitle;
        public string vrmAuthor;
        public string vrmVersion;
    }
}
