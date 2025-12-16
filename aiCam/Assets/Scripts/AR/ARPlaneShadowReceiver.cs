using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace AICam.AR
{
    /// <summary>
    /// Issue #75: AR平面にシャドウレシーバーマテリアルを適用
    /// アバターからの落ち影をAR平面上に表示する
    /// </summary>
    public class ARPlaneShadowReceiver : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ARPlaneManager planeManager;

        [Header("Shadow Settings")]
        [Tooltip("シャドウレシーバーシェーダー")]
        [SerializeField] private Shader shadowReceiverShader;

        [Tooltip("シャドウの濃さ (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float shadowIntensity = 0.6f;

        [Tooltip("シャドウの色")]
        [SerializeField] private Color shadowColor = new Color(0, 0, 0, 1);

        [Header("State")]
        [SerializeField] private bool shadowEnabled = true;

        private Material shadowMaterial;
        private readonly Dictionary<ARPlane, MeshRenderer> planeRenderers = new Dictionary<ARPlane, MeshRenderer>();

        // シェーダープロパティID
        private static readonly int ShadowIntensityId = Shader.PropertyToID("_ShadowIntensity");
        private static readonly int ShadowColorId = Shader.PropertyToID("_ShadowColor");

        #region Unity Lifecycle

        void Awake()
        {
            if (planeManager == null)
            {
                planeManager = FindFirstObjectByType<ARPlaneManager>();
            }

            if (planeManager == null)
            {
                Debug.LogError("[ARPlaneShadowReceiver] ARPlaneManager not found!");
                enabled = false;
                return;
            }

            // シェーダーを取得
            if (shadowReceiverShader == null)
            {
                shadowReceiverShader = Shader.Find("AICam/ARPlaneShadowReceiver");
            }

            if (shadowReceiverShader == null)
            {
                Debug.LogError("[ARPlaneShadowReceiver] Shadow receiver shader not found!");
                enabled = false;
                return;
            }

            // マテリアルを作成
            CreateShadowMaterial();
        }

        void OnEnable()
        {
            if (planeManager != null)
            {
                planeManager.planesChanged += OnPlanesChanged;
            }

            // 既存の平面にシャドウを適用
            ApplyShadowToAllPlanes();
        }

        void OnDisable()
        {
            if (planeManager != null)
            {
                planeManager.planesChanged -= OnPlanesChanged;
            }
        }

        void OnDestroy()
        {
            if (shadowMaterial != null)
            {
                Destroy(shadowMaterial);
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// シャドウの有効/無効を設定
        /// </summary>
        public void SetShadowEnabled(bool enabled)
        {
            shadowEnabled = enabled;
            Debug.Log($"[ARPlaneShadowReceiver] Shadow enabled: {enabled}");

            foreach (var kvp in planeRenderers)
            {
                if (kvp.Value != null)
                {
                    kvp.Value.enabled = enabled;
                }
            }
        }

        /// <summary>
        /// シャドウが有効かどうか
        /// </summary>
        public bool IsShadowEnabled => shadowEnabled;

        /// <summary>
        /// シャドウの濃さを設定
        /// </summary>
        public void SetShadowIntensity(float intensity)
        {
            shadowIntensity = Mathf.Clamp01(intensity);
            if (shadowMaterial != null)
            {
                shadowMaterial.SetFloat(ShadowIntensityId, shadowIntensity);
            }
            Debug.Log($"[ARPlaneShadowReceiver] Shadow intensity: {shadowIntensity}");
        }

        /// <summary>
        /// 現在のシャドウの濃さを取得
        /// </summary>
        public float ShadowIntensity => shadowIntensity;

        /// <summary>
        /// シャドウの色を設定
        /// </summary>
        public void SetShadowColor(Color color)
        {
            shadowColor = color;
            if (shadowMaterial != null)
            {
                shadowMaterial.SetColor(ShadowColorId, shadowColor);
            }
        }

        #endregion

        #region Private Methods

        private void CreateShadowMaterial()
        {
            shadowMaterial = new Material(shadowReceiverShader);
            shadowMaterial.name = "ARPlaneShadowMaterial";
            shadowMaterial.SetFloat(ShadowIntensityId, shadowIntensity);
            shadowMaterial.SetColor(ShadowColorId, shadowColor);
            Debug.Log("[ARPlaneShadowReceiver] Shadow material created");
        }

        private void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            // 新しい平面にシャドウを適用
            if (args.added != null)
            {
                foreach (var plane in args.added)
                {
                    ApplyShadowToPlane(plane);
                }
            }

            // 削除された平面をトラッキングから外す
            if (args.removed != null)
            {
                foreach (var plane in args.removed)
                {
                    if (planeRenderers.ContainsKey(plane))
                    {
                        planeRenderers.Remove(plane);
                    }
                }
            }
        }

        private void ApplyShadowToAllPlanes()
        {
            if (planeManager == null) return;

            foreach (var plane in planeManager.trackables)
            {
                ApplyShadowToPlane(plane);
            }
        }

        private void ApplyShadowToPlane(ARPlane plane)
        {
            if (plane == null || shadowMaterial == null) return;

            // 既にシャドウが適用されている場合はスキップ
            if (planeRenderers.ContainsKey(plane)) return;

            // ARPlaneのMeshRendererを取得
            var meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                // ARPlaneMeshVisualizerを確認
                var visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
                if (visualizer != null)
                {
                    meshRenderer = visualizer.GetComponent<MeshRenderer>();
                }
            }

            if (meshRenderer == null)
            {
                // 子オブジェクトからMeshRendererを探す
                meshRenderer = plane.GetComponentInChildren<MeshRenderer>();
            }

            if (meshRenderer != null)
            {
                // シャドウレシーバーマテリアルを適用
                // 既存のマテリアルリストに追加（元のマテリアルを保持）
                var materials = new List<Material>(meshRenderer.sharedMaterials);

                // シャドウマテリアルがまだ追加されていない場合のみ追加
                bool hasShadowMaterial = false;
                foreach (var mat in materials)
                {
                    if (mat != null && mat.name.Contains("Shadow"))
                    {
                        hasShadowMaterial = true;
                        break;
                    }
                }

                if (!hasShadowMaterial)
                {
                    materials.Add(shadowMaterial);
                    meshRenderer.materials = materials.ToArray();
                    Debug.Log($"[ARPlaneShadowReceiver] Shadow applied to plane: {plane.name}");
                }

                planeRenderers[plane] = meshRenderer;

                // 現在の状態に合わせて表示/非表示
                if (!shadowEnabled)
                {
                    meshRenderer.enabled = false;
                }
            }
            else
            {
                Debug.LogWarning($"[ARPlaneShadowReceiver] No MeshRenderer found on plane: {plane.name}");
            }
        }

        #endregion

#if UNITY_EDITOR
        void OnValidate()
        {
            if (Application.isPlaying && shadowMaterial != null)
            {
                shadowMaterial.SetFloat(ShadowIntensityId, shadowIntensity);
                shadowMaterial.SetColor(ShadowColorId, shadowColor);
            }
        }
#endif
    }
}
