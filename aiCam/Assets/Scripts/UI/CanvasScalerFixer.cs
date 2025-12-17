using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 実行時にCanvasScalerの設定をiPhone 17 Pro Max等の縦長画面用に修正する
/// 既存のシーンにあるCanvasに対して適用可能
/// </summary>
[RequireComponent(typeof(CanvasScaler))]
[ExecuteAlways]
public class CanvasScalerFixer : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("縦長画面（iPhone 17 Pro Max等）に最適化")]
    [SerializeField] private bool optimizeForTallScreens = true;

    [Tooltip("matchWidthOrHeight値（0.5 = バランス、1.0 = 高さ優先、0.0 = 幅優先）")]
    [Range(0f, 1f)]
    [SerializeField] private float matchWidthOrHeight = 0.5f;

    [Tooltip("リファレンス解像度（iPhone 17 Pro Max: 880x1912）")]
    [SerializeField] private Vector2 referenceResolution = new Vector2(880, 1912);

    private CanvasScaler canvasScaler;

    private void Awake()
    {
        ApplySettings();
    }

    private void OnEnable()
    {
        ApplySettings();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        ApplySettings();
    }
#endif

    private void ApplySettings()
    {
        if (canvasScaler == null)
        {
            canvasScaler = GetComponent<CanvasScaler>();
        }

        if (canvasScaler == null) return;

        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        canvasScaler.referenceResolution = referenceResolution;
        canvasScaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        canvasScaler.matchWidthOrHeight = matchWidthOrHeight;

        Debug.Log($"[CanvasScalerFixer] Applied settings: matchWidthOrHeight={matchWidthOrHeight}, " +
                 $"referenceResolution={referenceResolution}");
    }
}
