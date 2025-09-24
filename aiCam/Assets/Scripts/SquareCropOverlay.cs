using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 正方形プレビュー用オーバーレイ（手動更新のみ）。
/// ※ 以前の挙動にロールバック：Canvas幅/高さでスケール換算（scaleFactor 未使用）
///    → 端末によっては境界に「ごく薄いスキマ」が残る場合あり（元の状態に戻します）。
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SquareCropOverlay : MonoBehaviour
{
    [Header("References")]
    public ARPhotoController photo;  // 任意
    public RectTransform topMask;
    public RectTransform bottomMask;
    public RectTransform leftMask;
    public RectTransform rightMask;
    public RectTransform frame;      // 任意：枠表示

    [Header("Behaviour")]
    public bool hideWhenNotSquare = true;

    [Header("Preview Fallback (when Photo not bound)")]
    public bool previewSquareOn = true;
    public ARPhotoController.SquareAnchor previewAnchor = ARPhotoController.SquareAnchor.Center;

    private RectTransform canvasRect;
    private CanvasGroup cg;

    void Awake()
    {
        canvasRect = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        DisableRaycastTarget(topMask);
        DisableRaycastTarget(bottomMask);
        DisableRaycastTarget(leftMask);
        DisableRaycastTarget(rightMask);
        DisableRaycastTarget(frame);
    }

    [ContextMenu("Crop UIを更新（成形）")]
    public void RefreshNow()
    {
        if (!canvasRect) canvasRect = GetComponent<RectTransform>();

        GetPreviewDims(out int w, out int h);
        bool squareOn; ARPhotoController.SquareAnchor anchor;
        if (!TryReadPhotoConfig(out squareOn, out anchor))
        {
            squareOn = previewSquareOn;
            anchor   = previewAnchor;
        }

        Apply(w, h, anchor, squareOn);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(gameObject);
        if (topMask)    UnityEditor.EditorUtility.SetDirty(topMask);
        if (bottomMask) UnityEditor.EditorUtility.SetDirty(bottomMask);
        if (leftMask)   UnityEditor.EditorUtility.SetDirty(leftMask);
        if (rightMask)  UnityEditor.EditorUtility.SetDirty(rightMask);
        if (frame)      UnityEditor.EditorUtility.SetDirty(frame);
#endif
    }

    private bool TryReadPhotoConfig(out bool squareOn, out ARPhotoController.SquareAnchor anchor)
    {
        squareOn = previewSquareOn;
        anchor   = previewAnchor;
        if (!photo) return false;

        var t = typeof(ARPhotoController);
        try
        {
            var pSave   = t.GetProperty("SaveAsSquare", BindingFlags.Instance | BindingFlags.Public);
            var pAnchor = t.GetProperty("CurrentSquareAnchor", BindingFlags.Instance | BindingFlags.Public);
            bool hit = false;
            if (pSave   != null) { squareOn = (bool)pSave.GetValue(photo); hit = true; }
            if (pAnchor != null) { anchor   = (ARPhotoController.SquareAnchor)pAnchor.GetValue(photo); hit = true; }
            if (hit) return true;
        } catch {}

        try
        {
            var fSave   = t.GetField("saveAsSquare", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var fAnchor = t.GetField("squareAnchor", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            bool hit = false;
            if (fSave   != null) { squareOn = (bool)fSave.GetValue(photo); hit = true; }
            if (fAnchor != null) { anchor   = (ARPhotoController.SquareAnchor)fAnchor.GetValue(photo); hit = true; }
            return hit;
        } catch {}

        return false;
    }

    // ★ ロールバック版：編集時は Canvas の見た目サイズ（UI単位）をそのまま使用
    private void GetPreviewDims(out int w, out int h)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && canvasRect)
        {
            var size = canvasRect.rect.size;     // UI単位そのまま
            w = Mathf.Max(1, Mathf.RoundToInt(size.x));
            h = Mathf.Max(1, Mathf.RoundToInt(size.y));
            return;
        }
#endif
        // 実行時は実画面ピクセル
        w = Mathf.Max(1, Screen.width);
        h = Mathf.Max(1, Screen.height);
    }

    // ★ ロールバック版：Canvas幅/高さで換算（ux/uy）。わずかなスキマが出る可能性あり
    private void Apply(int screenW, int screenH,
                       ARPhotoController.SquareAnchor anchor, bool squareOn)
    {
        if (hideWhenNotSquare && !squareOn) { if (cg) cg.alpha = 0f; return; }
        if (cg) cg.alpha = 1f;

        if (!canvasRect) canvasRect = GetComponent<RectTransform>();
        RectInt crop = ComputeSquareCropRect(screenW, screenH, anchor);

        float ux = canvasRect.rect.width  / Mathf.Max(1, screenW);
        float uy = canvasRect.rect.height / Mathf.Max(1, screenH);

        float leftU   = crop.x * ux;
        float rightU  = (screenW - (crop.x + crop.width)) * ux;
        float bottomU = crop.y * uy;
        float topU    = (screenH - (crop.y + crop.height)) * uy;

        if (topMask)    SetTop(topMask,    topU);
        if (bottomMask) SetBottom(bottomMask, bottomU);
        if (leftMask)   SetLeft(leftMask,  leftU);
        if (rightMask)  SetRight(rightMask, rightU);

        if (frame)
        {
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.zero;
            frame.pivot     = Vector2.zero;
            frame.anchoredPosition = new Vector2(crop.x * ux, crop.y * uy);
            frame.sizeDelta        = new Vector2(crop.width * ux, crop.height * uy);
        }
    }

    private static void SetTop(RectTransform rt, float height)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, height);
    }
    private static void SetBottom(RectTransform rt, float height)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot     = new Vector2(0.5f, 0);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(0, height);
    }
    private static void SetLeft(RectTransform rt, float width)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot     = new Vector2(0, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(width, 0);
    }
    private static void SetRight(RectTransform rt, float width)
    {
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(1, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(width, 0);
    }

    private static void DisableRaycastTarget(RectTransform rt)
    {
        if (!rt) return;
        var img = rt.GetComponent<Image>();
        if (img) img.raycastTarget = false;
    }

    private static RectInt ComputeSquareCropRect(int width, int height, ARPhotoController.SquareAnchor anchor)
    {
        int s = Mathf.Min(width, height);
        int x = (width  - s) / 2;
        int y = (height - s) / 2;

        switch (anchor)
        {
            case ARPhotoController.SquareAnchor.Top:         y = height - s; break;
            case ARPhotoController.SquareAnchor.Bottom:      y = 0; break;
            case ARPhotoController.SquareAnchor.Left:        x = 0; break;
            case ARPhotoController.SquareAnchor.Right:       x = width - s; break;
            case ARPhotoController.SquareAnchor.TopLeft:     x = 0; y = height - s; break;
            case ARPhotoController.SquareAnchor.TopRight:    x = width - s; y = height - s; break;
            case ARPhotoController.SquareAnchor.BottomLeft:  x = 0; y = 0; break;
            case ARPhotoController.SquareAnchor.BottomRight: x = width - s; y = 0; break;
        }

        x = Mathf.Clamp(x, 0, width  - s);
        y = Mathf.Clamp(y, 0, height - s);
        return new RectInt(x, y, s, s);
    }
}