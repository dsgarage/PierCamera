using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

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

    [Header("Mask Expansion")]
    [Tooltip("マスクを“外側へだけ”拡張する量（ピクセル）。内側境界は動かない")]
    [SerializeField, Min(0f)] private float outerBleedPx = 3f;

    [Header("Manual Offset (Pixels)")]
    [Tooltip("アンカー基準に加算するピクセルオフセット (X:右+, Y:上+)。0なら移動なし。")]
    public Vector2 offsetPx = Vector2.zero;

    private RectTransform canvasRect;
    private CanvasGroup cg;

    void Awake()
    {
        canvasRect = GetComponent<RectTransform>();
        cg = GetComponent<CanvasGroup>();
        if (!cg) cg = gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.interactable = false;

        FixMaskImage(topMask);
        FixMaskImage(bottomMask);
        FixMaskImage(leftMask);
        FixMaskImage(rightMask);
        FixMaskImage(frame);

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

        // 念のため毎回矯正
        FixMaskImage(topMask);
        FixMaskImage(bottomMask);
        FixMaskImage(leftMask);
        FixMaskImage(rightMask);
        FixMaskImage(frame);

        GetPreviewDims(out int w, out int h);
        bool squareOn; ARPhotoController.SquareAnchor anchor;
        if (!TryReadPhotoConfig(out squareOn, out anchor))
        {
            squareOn = previewSquareOn;
            anchor   = previewAnchor;
        }

        Apply(w, h, anchor, squareOn);

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRect);
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(gameObject);
        if (topMask)    UnityEditor.EditorUtility.SetDirty(topMask);
        if (bottomMask) UnityEditor.EditorUtility.SetDirty(bottomMask);
        if (leftMask)   UnityEditor.EditorUtility.SetDirty(leftMask);
        if (rightMask)  UnityEditor.EditorUtility.SetDirty(rightMask);
        if (frame)      UnityEditor.EditorUtility.SetDirty(frame);
        UnityEditor.SceneView.RepaintAll();
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

    // 編集時は Canvas の見た目サイズ（UI単位）をそのまま使用
    private void GetPreviewDims(out int w, out int h)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && canvasRect)
        {
            var size = canvasRect.rect.size; // UI単位
            w = Mathf.Max(1, Mathf.RoundToInt(size.x));
            h = Mathf.Max(1, Mathf.RoundToInt(size.y));
            return;
        }
#endif
        // 実行時は実画面ピクセル
        w = Mathf.Max(1, Screen.width);
        h = Mathf.Max(1, Screen.height);
    }

    private void Apply(int screenW, int screenH,
                       ARPhotoController.SquareAnchor anchor, bool squareOn)
    {
        if (hideWhenNotSquare && !squareOn) { if (cg) cg.alpha = 0f; return; }
        if (cg) cg.alpha = 1f;

        if (!canvasRect) canvasRect = GetComponent<RectTransform>();

        // ピクセルオフセット（0なら移動なし）
        Vector2 appliedOffsetPx = offsetPx;

        RectInt crop = ComputeSquareCropRect(screenW, screenH, anchor, appliedOffsetPx);

        float ux = canvasRect.rect.width  / Mathf.Max(1, screenW);  // UI単位 / px（横）
        float uy = canvasRect.rect.height / Mathf.Max(1, screenH);  // UI単位 / px（縦）

        // 正方形の“外側”の厚み（UI単位）
        float leftU   = crop.x * ux;
        float rightU  = (screenW - (crop.x + crop.width)) * ux;
        float bottomU = crop.y * uy;
        float topU    = (screenH - (crop.y + crop.height)) * uy;

        // 外側へだけ拡張する量（UI単位）
        float bleedX = outerBleedPx * ux;
        float bleedY = outerBleedPx * uy;

        // 内側固定・外側のみ拡張
        if (topMask)    SetTopOutset(topMask,    topU,    bleedY);
        if (bottomMask) SetBottomOutset(bottomMask, bottomU, bleedY);
        if (leftMask)   SetLeftOutset(leftMask,  leftU,  bleedX);
        if (rightMask)  SetRightOutset(rightMask, rightU, bleedX);

        // 枠
        if (frame)
        {
            frame.anchorMin = Vector2.zero;
            frame.anchorMax = Vector2.zero;
            frame.pivot     = Vector2.zero;
            frame.anchoredPosition = new Vector2(crop.x * ux, crop.y * uy);
            frame.sizeDelta        = new Vector2(crop.width * ux, crop.height * uy);
        }
    }

    // === “外側へだけ”広げる RectTransform ヘルパー ===
    private static void SetTopOutset(RectTransform rt, float insideHeightU, float bleedU)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(0.5f, 1);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.offsetMin = new Vector2(0f, -insideHeightU); // 内側境界
        rt.offsetMax = new Vector2(0f, +bleedU);        // 外側へ拡張
    }

    private static void SetBottomOutset(RectTransform rt, float insideHeightU, float bleedU)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(1, 0);
        rt.pivot     = new Vector2(0.5f, 0);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.offsetMin = new Vector2(0f, -bleedU);        // 外側へ拡張
        rt.offsetMax = new Vector2(0f, +insideHeightU); // 内側境界
    }

    private static void SetLeftOutset(RectTransform rt, float insideWidthU, float bleedU)
    {
        rt.anchorMin = new Vector2(0, 0);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot     = new Vector2(0, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.offsetMin = new Vector2(-bleedU, 0f);        // 外側へ拡張
        rt.offsetMax = new Vector2(+insideWidthU, 0f);  // 内側境界
    }

    private static void SetRightOutset(RectTransform rt, float insideWidthU, float bleedU)
    {
        rt.anchorMin = new Vector2(1, 0);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot     = new Vector2(1, 0.5f);
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.offsetMin = new Vector2(-insideWidthU, 0f);  // 内側境界
        rt.offsetMax = new Vector2(+bleedU, 0f);        // 外側へ拡張
    }

    // === 画像の見た目が矩形に追従するよう矯正 ===
    private static void FixMaskImage(RectTransform rt)
    {
        if (!rt) return;
        var img = rt.GetComponent<Image>();
        if (!img) return;

        img.type = Image.Type.Simple;
        img.preserveAspect = false;
        img.raycastTarget = false;

        img.SetVerticesDirty();
        img.SetMaterialDirty();

        if (img.sprite == null)
        {
            Debug.LogWarning($"[SquareCropOverlay] '{rt.name}' の Image.sprite が未設定です。色だけでは描画されません。何かしらのSpriteを割り当ててください。");
        }
    }

    private static void DisableRaycastTarget(RectTransform rt)
    {
        if (!rt) return;
        var img = rt.GetComponent<Image>();
        if (img) img.raycastTarget = false;
    }

    private static RectInt ComputeSquareCropRect(int width, int height,
                                                 ARPhotoController.SquareAnchor anchor,
                                                 Vector2 extraOffsetPx)
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
            // Center は初期値のまま
        }

        // ピクセルオフセット（右+/上+）
        if (extraOffsetPx.sqrMagnitude > 0f)
        {
            x += Mathf.RoundToInt(extraOffsetPx.x);
            y += Mathf.RoundToInt(extraOffsetPx.y);
        }

        // 画面内に収まるようクランプ
        x = Mathf.Clamp(x, 0, width  - s);
        y = Mathf.Clamp(y, 0, height - s);
        return new RectInt(x, y, s, s);
    }
}