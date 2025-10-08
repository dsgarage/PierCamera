using UnityEngine;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(ARCameraBackground))]
public class CameraViewportShifter : MonoBehaviour
{
    public SquareCropOverlay squareOverlay;
    [SerializeField] private string shaderName = "Hidden/URP/ARBackgroundOffset";

    private ARCameraBackground arBg;
    private Material mat;

    void Awake()
    {
        arBg = GetComponent<ARCameraBackground>();
    }

    void OnEnable()
    {
        var sh = Shader.Find(shaderName);
        if (!sh) { Debug.LogError($"Shader not found: {shaderName}"); return; }
        mat = new Material(sh);
        arBg.useCustomMaterial = true;
        arBg.customMaterial = mat;
        Apply();
    }

    void OnDisable()
    {
        if (arBg) { arBg.useCustomMaterial = false; arBg.customMaterial = null; }
        if (mat) Destroy(mat);
    }

    void LateUpdate() => Apply();

    void Apply()
    {
        if (!mat || !squareOverlay) return;

        int w = Mathf.Max(1, Screen.width);
        int h = Mathf.Max(1, Screen.height);

        // SquareCropOverlay と同等の最終クロップ矩形を生成
        var anchor = squareOverlay.previewAnchor;
        var crop = ComputeSquareCropRect(w, h, anchor, squareOverlay.offsetPx);

        // 正方形中心と画面中心の差分（ピクセル）
        int cx = crop.x + crop.width  / 2;
        int cy = crop.y + crop.height / 2;
        float dx = cx - w * 0.5f;
        float dy = cy - h * 0.5f;

        // 見た目で追従させるため背景は逆向きに移動（右へ動いたマスク→背景は左へ）
        Vector2 uvOffsetDisplay = new Vector2(-dx / w, -dy / h);

        mat.SetVector("_UvOffset", uvOffsetDisplay);
        mat.SetVector("_UvScale", Vector2.one);
    }

    // SquareCropOverlay の Compute と一致
    static RectInt ComputeSquareCropRect(int width, int height,
        ARPhotoController.SquareAnchor anchor, Vector2 extraOffsetPx)
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

        if (extraOffsetPx.sqrMagnitude > 0f)
        {
            x += Mathf.RoundToInt(extraOffsetPx.x);
            y += Mathf.RoundToInt(extraOffsetPx.y);
        }

        x = Mathf.Clamp(x, 0, width  - s);
        y = Mathf.Clamp(y, 0, height - s);
        return new RectInt(x, y, s, s);
    }
}