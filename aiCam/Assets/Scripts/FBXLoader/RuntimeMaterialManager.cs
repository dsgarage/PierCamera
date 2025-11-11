using System.Collections.Generic;
using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Runtime Material管理システム
    /// シェーダー名とテクスチャパスをキーにしたマテリアルのキャッシュ/プーリング
    /// </summary>
    public class RuntimeMaterialManager
    {
        private static RuntimeMaterialManager _instance;
        public static RuntimeMaterialManager Instance => _instance ?? (_instance = new RuntimeMaterialManager());

        // シェーダー名 → マテリアルテンプレートのキャッシュ
        private readonly Dictionary<string, Material> shaderTemplates = new Dictionary<string, Material>();

        // 完全なマテリアルキー（シェーダー + テクスチャパス + カラー） → マテリアルインスタンス
        private readonly Dictionary<string, Material> materialCache = new Dictionary<string, Material>();

        /// <summary>
        /// マテリアルを取得または作成（キャッシュ対応）
        /// </summary>
        public Material GetOrCreateMaterial(string shaderName, string texturePath = null, Color? baseColor = null)
        {
            // キャッシュキー生成
            string cacheKey = GenerateCacheKey(shaderName, texturePath, baseColor);

            if (materialCache.TryGetValue(cacheKey, out Material cachedMat))
            {
                if (cachedMat != null) return cachedMat;
                // nullになっている場合はキャッシュから削除
                materialCache.Remove(cacheKey);
            }

            // 新しいマテリアルを作成
            Material mat = CreateMaterial(shaderName, texturePath, baseColor);
            if (mat != null)
            {
                materialCache[cacheKey] = mat;
            }

            return mat;
        }

        /// <summary>
        /// シェーダーテンプレートを取得または作成
        /// </summary>
        private Material GetOrCreateShaderTemplate(string shaderName)
        {
            if (shaderTemplates.TryGetValue(shaderName, out Material template))
            {
                if (template != null) return template;
                shaderTemplates.Remove(shaderName);
            }

            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[RuntimeMaterialManager] Shader not found: {shaderName}, using Standard");
                shader = Shader.Find("Standard");
            }

            if (shader == null) return null;

            template = new Material(shader) { name = $"{shaderName}_Template" };
            shaderTemplates[shaderName] = template;

            return template;
        }

        /// <summary>
        /// マテリアルを作成
        /// </summary>
        private Material CreateMaterial(string shaderName, string texturePath, Color? baseColor)
        {
            Material template = GetOrCreateShaderTemplate(shaderName);
            if (template == null) return null;

            // テンプレートからインスタンス化
            Material mat = new Material(template);

            // ベースカラー設定
            if (baseColor.HasValue && mat.HasProperty("_Color"))
            {
                mat.color = baseColor.Value;
            }

            // テクスチャ設定
            if (!string.IsNullOrEmpty(texturePath))
            {
                Texture2D tex = LoadTexture(texturePath);
                if (tex != null && mat.HasProperty("_MainTex"))
                {
                    mat.mainTexture = tex;
                }
            }

            return mat;
        }

        /// <summary>
        /// テクスチャをロード（キャッシュ対応は今後の拡張ポイント）
        /// </summary>
        private Texture2D LoadTexture(string path)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;

            try
            {
                byte[] data = System.IO.File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (tex.LoadImage(data))
                {
                    tex.name = System.IO.Path.GetFileNameWithoutExtension(path);
                    return tex;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RuntimeMaterialManager] Failed to load texture: {path}, {e.Message}");
            }

            return null;
        }

        /// <summary>
        /// キャッシュキーを生成
        /// </summary>
        private string GenerateCacheKey(string shaderName, string texturePath, Color? baseColor)
        {
            // シェーダー名 + テクスチャパス + カラーのハッシュ
            string key = shaderName ?? "Standard";

            if (!string.IsNullOrEmpty(texturePath))
                key += $"|tex:{texturePath}";

            if (baseColor.HasValue)
                key += $"|col:{baseColor.Value.r:F3},{baseColor.Value.g:F3},{baseColor.Value.b:F3},{baseColor.Value.a:F3}";

            return key;
        }

        /// <summary>
        /// キャッシュをクリア
        /// </summary>
        public void ClearCache()
        {
            // マテリアルインスタンスを破棄
            foreach (var mat in materialCache.Values)
            {
                if (mat != null) Object.Destroy(mat);
            }
            materialCache.Clear();

            // テンプレートを破棄
            foreach (var mat in shaderTemplates.Values)
            {
                if (mat != null) Object.Destroy(mat);
            }
            shaderTemplates.Clear();

            Debug.Log("[RuntimeMaterialManager] Cache cleared");
        }

        /// <summary>
        /// 統計情報を取得
        /// </summary>
        public void LogStats()
        {
            Debug.Log($"[RuntimeMaterialManager] Cached materials: {materialCache.Count}, Shader templates: {shaderTemplates.Count}");
        }
    }
}
