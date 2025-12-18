using UniSIL.ShaderInference.TextureLoading;

namespace AICam.FBXLoader
{
    /// <summary>
    /// UniSIL TextureManifest用の拡張メソッド
    /// </summary>
    public static class TextureManifestExtensions
    {
        /// <summary>
        /// GUIDでテクスチャエントリを検索
        /// </summary>
        public static TextureManifest.TextureEntry FindByGuid(this TextureManifest manifest, string guid)
        {
            if (manifest == null || manifest.textures == null || string.IsNullOrEmpty(guid))
            {
                return null;
            }

            foreach (var entry in manifest.textures)
            {
                if (entry.guid == guid)
                {
                    return entry;
                }
            }

            return null;
        }
    }
}
