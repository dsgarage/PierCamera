using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// シンプルな平面表示制御
/// ARPlane Prefab自体のMaterialのAlphaを変更する方式
/// </summary>
public class ARPlaneVisibilityControllerSimple : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private ARPlaneManager planeManager;

    [Header("Materials")]
    [Tooltip("平面表示用のマテリアル")]
    [SerializeField] private Material planeMaterial;

    [Tooltip("非表示用の透明マテリアル")]
    [SerializeField] private Material transparentMaterial;

    private Material originalMaterial;
    private bool isVisible = true;

    void Awake()
    {
        if (!planeManager)
        {
            planeManager = FindFirstObjectByType<ARPlaneManager>();
        }

        // 元のマテリアルを保存
        if (planeManager && planeManager.planePrefab)
        {
            var renderer = planeManager.planePrefab.GetComponent<MeshRenderer>();
            if (renderer && renderer.sharedMaterial)
            {
                originalMaterial = renderer.sharedMaterial;
            }
        }
    }

    /// <summary>
    /// 平面の表示/非表示を切り替え
    /// </summary>
    public void SetPlanesVisible(bool visible)
    {
        if (isVisible == visible)
            return;

        isVisible = visible;

        if (!planeManager)
            return;

        // Prefabのマテリアルを変更（新規生成される平面に適用）
        var renderer = planeManager.planePrefab?.GetComponent<MeshRenderer>();
        if (renderer)
        {
            renderer.sharedMaterial = visible ? originalMaterial : transparentMaterial;
        }

        // 既存の平面すべてに適用
        foreach (var plane in planeManager.trackables)
        {
            SetPlaneVisibility(plane, visible);
        }

        Debug.Log($"[ARPlaneVisibilityControllerSimple] Set planes visible: {visible}");
    }

    private void SetPlaneVisibility(ARPlane plane, bool visible)
    {
        if (!plane)
            return;

        var renderer = plane.GetComponent<MeshRenderer>();
        if (renderer)
        {
            renderer.sharedMaterial = visible ? originalMaterial : transparentMaterial;
        }
    }

    public bool IsPlanesVisible => isVisible;

    public void TogglePlanesVisibility()
    {
        SetPlanesVisible(!isVisible);
    }
}
