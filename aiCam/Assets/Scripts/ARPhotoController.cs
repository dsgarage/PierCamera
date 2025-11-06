using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public sealed class ARPhotoController : MonoBehaviour
{
    [Header("References (optional)")]
    [SerializeField] private ARSession arSession;
    [SerializeField] private OcclusionToggle occlusionToggle;
    [SerializeField] private Camera captureCamera;

    // ▼ 追加：マスクのピクセル移動量を参照するため
    [Header("Square Overlay Binding (optional)")]
    [Tooltip("保存時のクロップ位置を UI マスクの移動量(offsetPx)に同期させる場合に割り当て")]
    [SerializeField] private SquareCropOverlay squareOverlay;

    [Header("UI Hiding")]
    [SerializeField] private CanvasGroup[] uiToHide;
    [SerializeField] private UnityEngine.UIElements.UIDocument[] uiDocumentsToHide;
    [SerializeField] private bool allowConcurrentCapture = false;

    [Header("iOS Save Mode")]
    [SerializeField] private SaveModeIOS iosSaveMode = SaveModeIOS.CompositeScreenshot;

    [Header("Plane Visualization")]
    [SerializeField] private string planeVizLayerName = "ARPlaneViz";

    [Header("UI Layer Exclusion")]
    [SerializeField] private string uiLayerName = "UI";

    [Header("Aspect / Crop")]
    [Tooltip("true なら保存前に正方形へクロップします")]
    [SerializeField] private bool saveAsSquare = false;

    [Tooltip("正方形クロップの基準位置（既定: 中央）")]
    [SerializeField] private SquareAnchor squareAnchor = SquareAnchor.Center;

    public bool SaveAsSquare => saveAsSquare;
    public SquareAnchor CurrentSquareAnchor => squareAnchor;

    public enum SquareAnchor
    {
        Center, Top, Bottom, Left, Right,
        TopLeft, TopRight, BottomLeft, BottomRight
    }

    public enum SaveModeIOS
    {
        CompositeScreenshot,
        NativeCamera
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void ARNative_SavePNGToPhotos(byte[] pngBytes, int length);
    [DllImport("__Internal")] private static extern void ARNative_CaptureOneShot();
#endif

    private bool _isCapturing;
    private int _savedCullingMask;
    private bool _maskPatched;

    // 撮影完了時のコールバック
    public event System.Action<Texture2D> OnPhotoCaptured;

    private void Awake()
    {
        if (!arSession)
            arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
        if (!captureCamera)
            captureCamera = Camera.main;
    }

    private IEnumerator RestoreDepthNextFrame()
    {
        yield return null;
        var occ = UnityEngine.Object.FindFirstObjectByType<AROcclusionManager>(FindObjectsInactive.Exclude);
        if (!occ) yield break;
        try { occ.requestedEnvironmentDepthMode = EnvironmentDepthMode.Medium; } catch { }
        try { occ.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion; } catch { }
        try { occ.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled; } catch { }
        try { occ.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled; } catch { }
    }

    public void Capture()
    {
        if (!allowConcurrentCapture && _isCapturing) return;

#if UNITY_IOS && !UNITY_EDITOR
        if (iosSaveMode == SaveModeIOS.NativeCamera && !saveAsSquare)
        {
            StartCoroutine(CaptureIOS_Native());
            return;
        }
#endif
        StartCoroutine(CaptureCompositedAndSave());
    }

    private IEnumerator CaptureCompositedAndSave()
    {
        _isCapturing = true;
        try
        {
            SetUIVisible(false);
            BeginExcludePlaneLayer();

            // レイアウト反映（複数フレーム待機でUI Toolkitの描画を確実に反映）
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            // ① カメラから直接レンダリング（UIレイヤーを除外）
            Texture2D tex = CaptureFromCamera();
            if (!tex)
            {
                Debug.LogError("[ARPhoto] CaptureFromCamera returned null");
                yield break;
            }

            // ② 正方形クロップ（オフセット追従を反映）
            if (saveAsSquare)
            {
                // ★ MOD: 「中心ベース + SafeArea対応」の写像で保存テクスチャ上のRectを決定
                var cropRect = ComputeSquareCropOnTexture_CenterBased(
                    tex.width, tex.height,
                    squareAnchor,
                    (squareOverlay != null) ? squareOverlay.offsetPx : Vector2.zero
                );

                var sq = CropToRect(tex, cropRect);
                UnityEngine.Object.Destroy(tex);
                tex = sq;
            }

            // サムネイル用のコピーを作成（64x64にリサイズ）
            Texture2D thumbnail = CreateThumbnail(tex, 64, 64);
            OnPhotoCaptured?.Invoke(thumbnail);

            var png = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);

            string fileName = $"aiCam_{DateTime.Now:yyyyMMdd_HHmmss}.png";

#if UNITY_ANDROID
            try
            {
                SavePngToAndroidGallery(png, fileName);
                Debug.Log("[ARPhoto] Saved to Android Photos.");
            }
            catch (Exception e)
            {
                var fallback = Path.Combine(Application.persistentDataPath, fileName);
                File.WriteAllBytes(fallback, png);
                Debug.LogWarning("[ARPhoto] MediaStore failed. Saved to: " + fallback + "\n" + e);
            }
#elif UNITY_IOS && !UNITY_EDITOR
            try
            {
                ARNative_SavePNGToPhotos(png, png.Length);
                Debug.Log("[ARPhoto] Saved to iOS Photos.");
            }
            catch (Exception e)
            {
                var fallback = Path.Combine(Application.persistentDataPath, fileName);
                File.WriteAllBytes(fallback, png);
                Debug.LogWarning("[ARPhoto] iOS native save failed. Saved to: " + fallback + "\n" + e);
            }
#else
            var path = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllBytes(path, png);
            Debug.Log("[ARPhoto] Saved (editor/other): " + path);
#endif
        }
        finally
        {
            EndExcludePlaneLayer();
            SetUIVisible(true);
            _isCapturing = false;
        }
    }

    private Texture2D CaptureFromCamera()
    {
        if (!captureCamera)
        {
            Debug.LogError("[ARPhoto] Capture camera is null");
            return null;
        }

        // 現在の画面解像度でRenderTextureを作成
        int width = Screen.width;
        int height = Screen.height;

        RenderTexture rt = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousRT = captureCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            // カメラでRenderTextureにレンダリング
            captureCamera.targetTexture = rt;
            captureCamera.Render();

            // RenderTextureからTexture2Dに転送
            RenderTexture.active = rt;
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            return tex;
        }
        finally
        {
            // 元の状態に戻す
            captureCamera.targetTexture = previousRT;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(rt);
        }
    }

    private void BeginExcludePlaneLayer()
    {
        if (!captureCamera) return;

        _savedCullingMask = captureCamera.cullingMask;
        _maskPatched = true;
        int maskToExclude = 0;

        // Plane Vizレイヤーを除外
        int planeLayer = LayerMask.NameToLayer(planeVizLayerName);
        if (planeLayer >= 0)
        {
            maskToExclude |= (1 << planeLayer);
        }
        else
        {
            Debug.LogWarning($"[ARPhoto] Layer '{planeVizLayerName}' not found. Create it and assign to Plane prefab.");
        }

        // UIレイヤーを除外
        int uiLayer = LayerMask.NameToLayer(uiLayerName);
        if (uiLayer >= 0)
        {
            maskToExclude |= (1 << uiLayer);
        }
        else
        {
            Debug.LogWarning($"[ARPhoto] Layer '{uiLayerName}' not found.");
        }

        captureCamera.cullingMask = _savedCullingMask & ~maskToExclude;
        Debug.Log($"[ARPhoto] Culling mask: original={_savedCullingMask}, excluded={maskToExclude}, new={captureCamera.cullingMask}");
    }

    private void EndExcludePlaneLayer()
    {
        if (!captureCamera || !_maskPatched) return;
        captureCamera.cullingMask = _savedCullingMask;
        _maskPatched = false;
    }

    private void SetUIVisible(bool visible)
    {
        // CanvasGroup (uGUI用)
        if (uiToHide != null)
        {
            foreach (var cg in uiToHide)
            {
                if (!cg) continue;
                cg.alpha = visible ? 1f : 0f;
                cg.interactable = visible;
                cg.blocksRaycasts = visible;
            }
        }

        // UIDocument (UI Toolkit用)
        if (uiDocumentsToHide != null)
        {
            foreach (var doc in uiDocumentsToHide)
            {
                if (!doc || doc.rootVisualElement == null) continue;
                doc.rootVisualElement.style.display = visible
                    ? UnityEngine.UIElements.DisplayStyle.Flex
                    : UnityEngine.UIElements.DisplayStyle.None;
            }
        }
    }

#if UNITY_IOS && !UNITY_EDITOR
    private IEnumerator CaptureIOS_Native()
    {
        occlusionToggle?.DisableDepthNow();
        yield return null;

        bool wasRunning = (arSession != null && arSession.enabled);
        if (arSession != null && wasRunning)
            arSession.enabled = false;

        try
        {
            ARNative_CaptureOneShot();
            yield return new WaitForSeconds(0.8f);
        }
        finally
        {
            if (arSession != null)
                arSession.enabled = true;
            StartCoroutine(RestoreDepthNextFrame());
        }
    }
#endif

#if UNITY_ANDROID
    private static void SavePngToAndroidGallery(byte[] pngBytes, string fileName)
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
        {
            int sdkInt;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                sdkInt = version.GetStatic<int>("SDK_INT");

            if (sdkInt >= 29)
            {
                using (var mediaStoreImagesMedia = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
                using (var mediaStoreMediaColumns = new AndroidJavaClass("android.provider.MediaStore$MediaColumns"))
                {
                    string DISPLAY_NAME = mediaStoreMediaColumns.GetStatic<string>("DISPLAY_NAME");
                    string MIME_TYPE    = mediaStoreMediaColumns.GetStatic<string>("MIME_TYPE");
                    string RELATIVE_PATH= mediaStoreMediaColumns.GetStatic<string>("RELATIVE_PATH");

                    using (var values = new AndroidJavaObject("android.content.ContentValues"))
                    {
                        values.Call<AndroidJavaObject>("put", DISPLAY_NAME, fileName);
                        values.Call<AndroidJavaObject>("put", MIME_TYPE, "image/png");
                        values.Call<AndroidJavaObject>("put", RELATIVE_PATH, "Pictures/aiCam");

                        var uri = resolver.Call<AndroidJavaObject>("insert",
                            mediaStoreImagesMedia.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"),
                            values);
                        if (uri == null) throw new Exception("resolver.insert returned null Uri");

                        using (var os = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                        {
                            os.Call("write", new object[] { pngBytes });
                            os.Call("flush");
                            os.Call("close");
                        }
                    }
                }
            }
            else
            {
                string picturesDir;
                using (var environment = new AndroidJavaClass("android.os.Environment"))
                    picturesDir = environment.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory",
                        environment.GetStatic<string>("DIRECTORY_PICTURES")).Call<string>("getAbsolutePath");

                var folder = Path.Combine(picturesDir, "aiCam");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var absPath = Path.Combine(folder, fileName);
                File.WriteAllBytes(absPath, pngBytes);

                using (var ms = new AndroidJavaClass("android.media.MediaScannerConnection"))
                    ms.CallStatic("scanFile", activity, new string[] { absPath }, null, null);
            }
        }
    }
#endif

    // -------------------- ここからクロップ系（オフセット対応） --------------------

    // 任意の矩形で安全に切り出す（テクスチャ座標系）
    private static Texture2D CropToRect(Texture2D src, RectInt r)
    {
        // 範囲保護
        int x = Mathf.Clamp(r.x, 0, Mathf.Max(0, src.width  - 1));
        int y = Mathf.Clamp(r.y, 0, Mathf.Max(0, src.height - 1));
        int w = Mathf.Clamp(r.width,  1, src.width  - x);
        int h = Mathf.Clamp(r.height, 1, src.height - y);

        Color[] pixels = src.GetPixels(x, y, w, h);
        var dst = new Texture2D(w, h, TextureFormat.RGBA32, false);
        dst.SetPixels(pixels);
        dst.Apply();
        return dst;
    }

    // 現在の画面サイズ基準の正方形トリミング矩形（UIオフセットも反映）
    public RectInt GetCurrentSquareCropRectPixels()
    {
        Vector2 uiOffsetPx = (squareOverlay != null) ? squareOverlay.offsetPx : Vector2.zero;
        return ComputeSquareCropRect(Screen.width, Screen.height, squareAnchor, uiOffsetPx);
    }

    // 幅・高さとアンカー＋ピクセルオフセットから正方形の切り出し矩形を計算（左下原点, ピクセル）
    public static RectInt ComputeSquareCropRect(int width, int height, SquareAnchor anchor, Vector2 extraOffsetPx)
    {
        int s = Mathf.Min(width, height);
        int x = (width  - s) / 2;
        int y = (height - s) / 2;

        switch (anchor)
        {
            case SquareAnchor.Top:         y = height - s; break;
            case SquareAnchor.Bottom:      y = 0; break;
            case SquareAnchor.Left:        x = 0; break;
            case SquareAnchor.Right:       x = width - s; break;
            case SquareAnchor.TopLeft:     x = 0; y = height - s; break;
            case SquareAnchor.TopRight:    x = width - s; y = height - s; break;
            case SquareAnchor.BottomLeft:  x = 0; y = 0; break;
            case SquareAnchor.BottomRight: x = width - s; y = 0; break;
            // Center は既定値
        }

        // UIで動かした分（右+/上+）を加算
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

    // 互換：オフセット無し（既存APIを残す）
    public static RectInt ComputeSquareCropRect(int width, int height, SquareAnchor anchor)
        => ComputeSquareCropRect(width, height, anchor, Vector2.zero);

    // サムネイル作成用のヘルパーメソッド
    private static Texture2D CreateThumbnail(Texture2D source, int width, int height)
    {
        RenderTexture rt = RenderTexture.GetTemporary(width, height);
        rt.filterMode = FilterMode.Bilinear;

        RenderTexture.active = rt;
        Graphics.Blit(source, rt);

        Texture2D thumbnail = new Texture2D(width, height, TextureFormat.RGBA32, false);
        thumbnail.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        thumbnail.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        return thumbnail;
    }

    // ★ MOD: 中心ベース + Safe Area 対応で保存テクスチャ上の正方形Rectを算出
    private RectInt ComputeSquareCropOnTexture_CenterBased(
        int texW, int texH, SquareAnchor anchor, Vector2 uiOffsetPx)
    {
        // 1) SafeArea内で最終スクエアを計算（SquareCropOverlayと整合）
        Rect sa = Screen.safeArea;
        int saW = Mathf.RoundToInt(sa.width);
        int saH = Mathf.RoundToInt(sa.height);
        RectInt sqInSafe = ComputeSquareCropRect(saW, saH, anchor, uiOffsetPx);

        // 2) 画面（SafeArea原点込み）の中心（左下原点）
        float scrCX = (sa.x / 2) + (sqInSafe.x + sqInSafe.width  * 0.5f);
        float scrCY = (sa.y / 2) + (sqInSafe.y + sqInSafe.height * 0.5f);

        // 3) Screen→tex の線形写像
        float sx = texW / Mathf.Max(1f, (float)Screen.width);
        float sy = texH / Mathf.Max(1f, (float)Screen.height);

        int texCX = Mathf.RoundToInt(scrCX * sx);
        int texCY = Mathf.RoundToInt(scrCY * sy);

        // 4) サイズは“実際のUI枠幅”を等方スケール（見た目そのまま）
        int sideOnTex = Mathf.Max(1, Mathf.RoundToInt(sqInSafe.width * Mathf.Min(sx, sy)));

        // 5) 左下原点は Floor で上方向の+1pxブレを抑制 → クランプ
        int tx = Mathf.FloorToInt(texCX - sideOnTex / 2f);
        int ty = Mathf.FloorToInt(texCY - sideOnTex / 2f);

        tx = Mathf.Clamp(tx, 0, texW - sideOnTex);
        ty = Mathf.Clamp(ty, 0, texH - sideOnTex);

        Debug.Log($"[ARPhoto] Screen {Screen.width}x{Screen.height}  tex {texW}x{texH}");
        Debug.Log($"[ARPhoto] sqInSafe={sqInSafe}  sx={sx:F3} sy={sy:F3}  sideOnTex={sideOnTex}");
        Debug.Log($"[ARPhoto] tx,ty={tx},{ty}  texCX,texCY={texCX},{texCY}");

        return new RectInt(tx, ty, sideOnTex, sideOnTex);
    }
}