using UnityEngine;

/// <summary>
/// Safe Areaに合わせてRectTransformを調整するコンポーネント
/// iPhone Dynamic Island, ノッチ, ホームインジケーター等に対応
/// </summary>
[RequireComponent(typeof(RectTransform))]
[ExecuteAlways]
public class SafeAreaAdjuster : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("上部（Dynamic Island / ノッチ）のSafeAreaを適用")]
    [SerializeField] private bool applyTop = true;

    [Tooltip("下部（ホームインジケーター）のSafeAreaを適用")]
    [SerializeField] private bool applyBottom = true;

    [Tooltip("左右のSafeAreaを適用")]
    [SerializeField] private bool applySides = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = false;

    private RectTransform rectTransform;
    private Rect lastSafeArea;
    private Vector2Int lastScreenSize;
    private ScreenOrientation lastOrientation;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
    }

    private void OnEnable()
    {
        ApplySafeArea();
    }

    private void Update()
    {
        // Safe Areaまたは画面サイズが変更された場合のみ更新
        if (HasScreenChanged())
        {
            ApplySafeArea();
        }
    }

    private bool HasScreenChanged()
    {
        Rect safeArea = Screen.safeArea;
        Vector2Int screenSize = new Vector2Int(Screen.width, Screen.height);
        ScreenOrientation orientation = Screen.orientation;

        if (safeArea != lastSafeArea || screenSize != lastScreenSize || orientation != lastOrientation)
        {
            lastSafeArea = safeArea;
            lastScreenSize = screenSize;
            lastOrientation = orientation;
            return true;
        }

        return false;
    }

    private void ApplySafeArea()
    {
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        Rect safeArea = Screen.safeArea;
        Vector2 screenSize = new Vector2(Screen.width, Screen.height);

        if (screenSize.x <= 0 || screenSize.y <= 0)
        {
            return;
        }

        // Safe Areaの正規化座標を計算
        Vector2 anchorMin = safeArea.position / screenSize;
        Vector2 anchorMax = (safeArea.position + safeArea.size) / screenSize;

        // 各方向の適用を制御
        if (!applyTop)
        {
            anchorMax.y = 1f;
        }
        if (!applyBottom)
        {
            anchorMin.y = 0f;
        }
        if (!applySides)
        {
            anchorMin.x = 0f;
            anchorMax.x = 1f;
        }

        // RectTransformに適用
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        if (showDebugInfo)
        {
            Debug.Log($"[SafeAreaAdjuster] Applied SafeArea: {safeArea}, " +
                     $"Screen: {screenSize}, " +
                     $"AnchorMin: {anchorMin}, AnchorMax: {anchorMax}");
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // Editorで設定変更時に即座に反映
        if (rectTransform == null)
        {
            rectTransform = GetComponent<RectTransform>();
        }

        if (Application.isPlaying || !gameObject.activeInHierarchy)
        {
            return;
        }

        // Editorプレビュー用の擬似SafeArea（iPhone 17 Pro Max風）
        ApplySafeArea();
    }
#endif
}
