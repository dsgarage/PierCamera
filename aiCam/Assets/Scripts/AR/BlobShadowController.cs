using UnityEngine;

namespace AICam.AR
{
    /// <summary>
    /// アバターの足元に丸い影（Blob Shadow）を表示するコントローラー
    /// 軽量で、ポーズに関係なく常に足元に表示される
    /// </summary>
    public class BlobShadowController : MonoBehaviour
    {
        [Header("Shadow Settings")]
        [SerializeField] private float shadowSize = 0.8f;
        [SerializeField] private float shadowIntensity = 0.5f;
        [SerializeField] private float heightOffset = 0.01f;

        [Header("References")]
        [SerializeField] private Transform avatarRoot;
        [SerializeField] private MeshRenderer shadowRenderer;

        // 内部状態
        private Material shadowMaterial;
        private bool isEnabled = true;
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

        // シングルトン（シーン内に1つ）
        private static BlobShadowController _instance;
        public static BlobShadowController Instance => _instance;

        void Awake()
        {
            _instance = this;

            // シャドウメッシュが未設定の場合は自動生成
            if (shadowRenderer == null)
            {
                CreateShadowQuad();
            }
            else
            {
                // 外部から設定されたレンダラーを使用
                shadowMaterial = shadowRenderer.material;
                UpdateShadowColor();
            }

            // 初期状態で非表示
            gameObject.SetActive(false);
        }

        /// <summary>
        /// シャドウ用のQuadを自動生成
        /// </summary>
        void CreateShadowQuad()
        {
            // Quadメッシュを作成
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = CreateQuadMesh();

            shadowRenderer = gameObject.AddComponent<MeshRenderer>();

            // マテリアル作成（Sprites/Defaultが最も安定して透明テクスチャ+色乗算をサポート）
            Shader shadowShader = null;

            // 優先順: Sprites/Default > URP 2D Sprite > URP Unlit > Standard
            shadowShader = Shader.Find("Sprites/Default");
            if (shadowShader != null && shadowShader.name != "Hidden/InternalErrorShader")
            {
                shadowMaterial = new Material(shadowShader);
                shadowMaterial.renderQueue = 3000;
                Debug.Log("[BlobShadow] Using Sprites/Default shader");
            }
            else
            {
                shadowShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (shadowShader != null && shadowShader.name != "Hidden/InternalErrorShader")
                {
                    shadowMaterial = new Material(shadowShader);
                    shadowMaterial.renderQueue = 3000;
                    Debug.Log("[BlobShadow] Using URP 2D Sprite shader");
                }
                else
                {
                    shadowShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (shadowShader != null && shadowShader.name != "Hidden/InternalErrorShader")
                    {
                        shadowMaterial = new Material(shadowShader);
                        // URP Unlit透明設定
                        shadowMaterial.SetFloat("_Surface", 1); // Transparent
                        shadowMaterial.SetFloat("_Blend", 0); // Alpha
                        shadowMaterial.SetFloat("_ZWrite", 0);
                        shadowMaterial.SetFloat("_AlphaClip", 0);
                        shadowMaterial.SetFloat("_Cull", 0); // Off - 両面描画
                        shadowMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        shadowMaterial.DisableKeyword("_ALPHATEST_ON");
                        shadowMaterial.renderQueue = 3000;
                        Debug.Log("[BlobShadow] Using URP Unlit shader");
                    }
                    else
                    {
                        // フォールバック: Standard透明
                        shadowShader = Shader.Find("Standard");
                        if (shadowShader == null)
                        {
                            Debug.LogError("[BlobShadow] No suitable shader found!");
                            return;
                        }
                        shadowMaterial = new Material(shadowShader);
                        shadowMaterial.SetFloat("_Mode", 3); // Transparent
                        shadowMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                        shadowMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                        shadowMaterial.SetInt("_ZWrite", 0);
                        shadowMaterial.DisableKeyword("_ALPHATEST_ON");
                        shadowMaterial.EnableKeyword("_ALPHABLEND_ON");
                        shadowMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                        shadowMaterial.renderQueue = 3000;
                        Debug.Log("[BlobShadow] Using Standard shader fallback");
                    }
                }
            }

            // 影テクスチャを生成（黒色の円形グラデーション）
            shadowMaterial.mainTexture = CreateShadowTexture(256);
            // 色と強度を設定
            UpdateShadowColor();

            shadowRenderer.material = shadowMaterial;
            shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            shadowRenderer.receiveShadows = false;

            // 回転を水平に設定
            transform.localRotation = Quaternion.Euler(90, 0, 0);
        }

        /// <summary>
        /// Quadメッシュを作成
        /// </summary>
        Mesh CreateQuadMesh()
        {
            var mesh = new Mesh();
            mesh.name = "BlobShadowQuad";

            float half = 0.5f;
            mesh.vertices = new Vector3[]
            {
                new Vector3(-half, -half, 0),
                new Vector3(half, -half, 0),
                new Vector3(-half, half, 0),
                new Vector3(half, half, 0)
            };

            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(0, 1),
                new Vector2(1, 1)
            };

            mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
            mesh.RecalculateNormals();

            return mesh;
        }

        /// <summary>
        /// 円形グラデーションの影テクスチャを生成（黒色）
        /// </summary>
        Texture2D CreateShadowTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.name = "BlobShadowTexture";

            float center = size / 2f;
            float maxDist = center;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);

                    // 中心から外側へグラデーション（スムーズステップ）
                    float t = Mathf.Clamp01(dist / maxDist);
                    float alpha = 1f - Mathf.SmoothStep(0f, 1f, t);

                    // 中心付近をより濃くする
                    alpha = Mathf.Pow(alpha, 0.7f);

                    // 黒色の影（アルファで透明度を制御）
                    // 強度はSetIntensityで動的に変更可能
                    texture.SetPixel(x, y, new Color(0, 0, 0, alpha));
                }
            }

            texture.Apply();
            texture.wrapMode = TextureWrapMode.Clamp;

            return texture;
        }

        void LateUpdate()
        {
            if (!isEnabled || avatarRoot == null) return;

            // アバターの足元に追従
            Vector3 footPosition = GetFootPosition();
            transform.position = new Vector3(
                footPosition.x,
                footPosition.y + heightOffset,
                footPosition.z
            );

            // サイズ更新
            transform.localScale = new Vector3(shadowSize, shadowSize, 1f);
        }

        /// <summary>
        /// アバターの足元位置を取得
        /// </summary>
        Vector3 GetFootPosition()
        {
            if (avatarRoot == null) return Vector3.zero;

            // アバターのBoundsから足元を推定
            var renderers = avatarRoot.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return avatarRoot.position;

            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers)
            {
                bounds.Encapsulate(r.bounds);
            }

            return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }

        /// <summary>
        /// シャドウの色を更新（強度をアルファで制御）
        /// </summary>
        void UpdateShadowColor()
        {
            if (shadowMaterial == null) return;

            // テクスチャは黒なので、色は白でアルファだけで強度を制御
            // これにより、テクスチャの黒 × 白 = 黒（テクスチャそのまま）
            // アルファは shader によってテクスチャのアルファと乗算される
            Color color = new Color(1f, 1f, 1f, shadowIntensity);

            if (shadowMaterial.HasProperty(ColorProperty))
            {
                shadowMaterial.SetColor(ColorProperty, color);
            }
            if (shadowMaterial.HasProperty(BaseColorProperty))
            {
                shadowMaterial.SetColor(BaseColorProperty, color);
            }
        }

        // ========== Public API ==========

        /// <summary>
        /// アバターを設定して影を有効化
        /// </summary>
        public void SetAvatar(Transform avatar)
        {
            avatarRoot = avatar;
            gameObject.SetActive(avatar != null && isEnabled);
            Debug.Log($"[BlobShadow] Avatar set: {avatar?.name ?? "null"}");
        }

        /// <summary>
        /// 影の有効/無効を設定
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            isEnabled = enabled;
            gameObject.SetActive(enabled && avatarRoot != null);
            Debug.Log($"[BlobShadow] Enabled: {enabled}");
        }

        /// <summary>
        /// 影の強度を設定（0.0〜1.0）
        /// </summary>
        public void SetIntensity(float intensity)
        {
            shadowIntensity = Mathf.Clamp01(intensity);
            UpdateShadowColor();
            Debug.Log($"[BlobShadow] Intensity: {shadowIntensity}");
        }

        /// <summary>
        /// 影のサイズを設定（メートル単位）
        /// </summary>
        public void SetSize(float size)
        {
            shadowSize = Mathf.Max(0.1f, size);
        }

        /// <summary>
        /// 現在の影の強度を取得
        /// </summary>
        public float GetIntensity() => shadowIntensity;

        /// <summary>
        /// 現在の影の有効状態を取得
        /// </summary>
        public bool IsEnabled() => isEnabled;
    }
}
