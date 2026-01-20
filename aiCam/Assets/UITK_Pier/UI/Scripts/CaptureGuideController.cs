using UnityEngine;
using UnityEngine.UIElements;
using System.Collections;
#if UNITY_EDITOR
using UnityEngine.InputSystem;
#endif

public enum CaptureAspect { OneOne, FourThree, SixteenNine }

public class CaptureGuideController : MonoBehaviour
{
    [SerializeField] private UIDocument doc;
    [SerializeField] private CaptureAspect initialAspect = CaptureAspect.FourThree;

    private VisualElement root;
    private VisualElement topOverlay;
    private VisualElement imageAspect;
    private VisualElement bottomOverlay;

    private void Awake()
    {
        if (!doc) doc = GetComponent<UIDocument>();

        root = doc.rootVisualElement;

        // UXMLのnameと一致していることが重要
        topOverlay = root.Q<VisualElement>("topOverlay");
        imageAspect = root.Q<VisualElement>("imageAspect");
        bottomOverlay = root.Q<VisualElement>("bottomOverlay");
    }

    private Coroutine applyRoutine;

    private void Start()
    {
        if (applyRoutine != null) StopCoroutine(applyRoutine);
        applyRoutine = StartCoroutine(ApplyWhenReady(initialAspect));
    }

    private IEnumerator ApplyWhenReady(CaptureAspect aspect)
    {
        for (int i = 0; i < 10; i++)
        {
            yield return null;
            if (root.resolvedStyle.width > 0 && root.resolvedStyle.height > 0)
            {
                ApplyAspectInternal(aspect);
                yield break;
            }
        }
        Debug.LogError("CaptureGuide: root size not ready.");
    }

    public void SetAspect(CaptureAspect aspect)
    {
        if (applyRoutine != null) StopCoroutine(applyRoutine);
        applyRoutine = StartCoroutine(ApplyWhenReady(aspect));
    }

    private void ApplyAspectInternal(CaptureAspect aspect)
    {
        float r;              // width/height Important: Portrait 4:3 = 3/4
        float topFixed = 0f;
        float bottomFixed = 0f;

        switch (aspect)
        {
            case CaptureAspect.FourThree:
                r = 3f / 4f;
                bottomFixed = 210f;
                break;

            case CaptureAspect.OneOne:
                r = 1f;
                bottomFixed = 286f;
                break;

            case CaptureAspect.SixteenNine:
                r = 9f / 16f;
                topFixed = 48f;
                break;

            default:
                r = 3f / 4f;
                bottomFixed = 210f;
                break;
        }

        // ---- ImageAspect priority: imageAspectを縮める前にFixed overlayを縮める ----
        float screenW0 = root.resolvedStyle.width;
        float screenH0 = root.resolvedStyle.height;

        float desiredW = screenW0;
        float desiredH = desiredW / r;

        // 画面に desiredH が入らない場合は、横いっぱい設計そのものが無理なので（極端に横長等）後段で縮む
        // ただし、入る場合は「入るだけの余白」を overlay に割り当てるのが最優先。
        float availableOverlay = Mathf.Max(0f, screenH0 - desiredH);

        // 固定Overlayが “入る余白” を超えるなら削る（imageAspect を守る）
        float fixedSum = topFixed + bottomFixed;
        float reduce = Mathf.Max(0f, fixedSum - availableOverlay);

        if (reduce > 0f)
        {
            // 削る優先順位：あなたの設計思想に合わせる
            if (aspect == CaptureAspect.FourThree || aspect == CaptureAspect.OneOne)
            {
                // bottom優先：まず top を削る → 次に bottom
                float cutTop = Mathf.Min(reduce, topFixed);
                topFixed -= cutTop;
                reduce -= cutTop;

                if (reduce > 0f)
                    bottomFixed = Mathf.Max(0f, bottomFixed - reduce);
            }
            else // SixteenNine
            {
                // top優先：まず bottom を削る → 次に top
                float cutBottom = Mathf.Min(reduce, bottomFixed);
                bottomFixed -= cutBottom;
                reduce -= cutBottom;

                if (reduce > 0f)
                    topFixed = Mathf.Max(0f, topFixed - reduce);
            }
        }
        // Top/Bottom overlay の固定・可変切替
        ApplyOverlayRule(topOverlay, fixedPx: topFixed);
        ApplyOverlayRule(bottomOverlay, fixedPx: bottomFixed);

        // 画面サイズ（rootがレイアウト確定後であることが前提）
        float screenW = screenW0;
        float screenH = screenH0;

        float availW = Mathf.Max(0f, screenW);
        float availH = Mathf.Max(0f, screenH - topFixed - bottomFixed);

        // まず幅いっぱいで計算して、はみ出るなら縮める
        float targetW = availW;
        float targetH = availW / r;

        if (targetH > availH)
        {
            targetH = availH;
            targetW = availH * r;
        }

        imageAspect.style.width = targetW;
        imageAspect.style.height = targetH;

        // imageAspectを強制的に同固定されるかC#側で定義
        imageAspect.style.flexGrow = 0;
        imageAspect.style.flexShrink = 0;
    }

    private void ApplyOverlayRule(VisualElement overlay, float fixedPx)
    {
        if (fixedPx > 0f)
        {
            overlay.style.height = fixedPx;
            overlay.style.flexGrow = 0;
        }
        else
        {
            overlay.style.height = StyleKeyword.Auto;
            overlay.style.flexGrow = 1;
        }
    }

    // Debug:画面が崩れたときに確認
    /* #if UNITY_EDITOR
    private IEnumerator LogOverlayHeightsNextFrame(CaptureAspect aspect, float targetW, float targetH)
    {
        yield return null; // 1フレーム待つ（style反映後）

        float topH = topOverlay.resolvedStyle.height;
        float bottomH = bottomOverlay.resolvedStyle.height;

        float rootW = root.resolvedStyle.width;
        float rootH = root.resolvedStyle.height;

        float imageHResolved = imageAspect.resolvedStyle.height;

        Debug.Log(
            $"Aspect={aspect} " +
            $"image={targetW:F1}x{targetH:F1} ratio={(targetW/targetH):F3} " +
            $"topOverlayH={topH:F1}px bottomOverlayH={bottomH:F1}px" +
            $"resolved imageAspectH={imageHResolved:F1}px"
        );
    }
    # endif */

    // デバッグ用
    #if UNITY_EDITOR
    private void Update()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        if (kb.digit1Key.wasPressedThisFrame) SetAspect(CaptureAspect.FourThree);
        if (kb.digit2Key.wasPressedThisFrame) SetAspect(CaptureAspect.OneOne);
        if (kb.digit3Key.wasPressedThisFrame) SetAspect(CaptureAspect.SixteenNine);
    }
    #endif
}