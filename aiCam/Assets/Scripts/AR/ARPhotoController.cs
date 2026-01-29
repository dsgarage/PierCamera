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

    [Header("Editor Debug")]
    [Tooltip("Editorモードで Full 撮影時に強制的に縦長アスペクト比(9:19.5)を適用")]
    [SerializeField] private bool editorFullUseDeviceAspect = true;

    [Tooltip("Editorモードでデバッグ用にUI込みのスクリーンショットも保存")]
    [SerializeField] private bool editorSaveWithUI = true;

    // アスペクト比設定（0 = Full、それ以外は幅/高さの比率）
    private float targetAspectRatio = 0f;

    /// <summary>
    /// 撮影時のアスペクト比を設定（0 = Full、16/9 = 16:9、3/2 = 3:2 など）
    /// </summary>
    public void SetAspectRatio(float aspectRatio)
    {
        targetAspectRatio = aspectRatio;
        Debug.Log($"📐 ARPhotoController: Aspect ratio set to {aspectRatio:F3}");
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

    // UaaL用: パス付き撮影完了イベント（RN連携用）
    public event System.Action<string, int, int> OnPhotoCapturedWithPath;

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
        if (iosSaveMode == SaveModeIOS.NativeCamera)
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
#if UNITY_EDITOR
            // Editorモード：デバッグ用にUI込みスクリーンショットを先に保存
            if (editorSaveWithUI)
            {
                // UI状態を強制的に可視化
                SetUIVisible(true);

                // UI Toolkitの描画を確実に反映させるため複数フレーム待機
                yield return null;
                yield return null;
                yield return null;
                yield return new WaitForEndOfFrame();

                Texture2D uiScreenshot = CaptureScreenWithUI();
                if (uiScreenshot != null)
                {
                    SaveScreenshotWithUI(uiScreenshot);
                    UnityEngine.Object.Destroy(uiScreenshot);
                }
            }
#endif

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

            // サムネイル用のコピーを作成（256x256にリサイズ、アスペクト比を維持）
            Texture2D thumbnail = CreateThumbnailWithAspectRatio(tex, 256);
            OnPhotoCaptured?.Invoke(thumbnail);

            // JPEGでエンコード（品質90）
            byte[] imageBytes = tex.EncodeToJPG(90);
            UnityEngine.Object.Destroy(tex);

            // ファイル名: YYYYMMDDhhmmss_AspectRatio.jpg
            string aspectRatioName = GetAspectRatioName(targetAspectRatio);
            string fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{aspectRatioName}.jpg";

#if UNITY_EDITOR
            // Editorモード: プロジェクト直下のjpgフォルダに保存
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string jpgFolder = Path.Combine(projectRoot, "jpg");

            // jpgフォルダが存在しない場合は作成
            if (!Directory.Exists(jpgFolder))
            {
                Directory.CreateDirectory(jpgFolder);
                Debug.Log($"📁 Created jpg folder: {jpgFolder}");
            }

            string path = Path.Combine(jpgFolder, fileName);
            File.WriteAllBytes(path, imageBytes);
            Debug.Log($"📸 [ARPhoto] Saved to jpg folder: {path}");

            // UaaLイベント発火（Editor用テスト）
            OnPhotoCapturedWithPath?.Invoke(path, Screen.width, Screen.height);
#elif UNITY_ANDROID
            try
            {
                SavePngToAndroidGallery(imageBytes, fileName);
                Debug.Log("[ARPhoto] Saved to Android Photos.");
            }
            catch (Exception e)
            {
                var fallback = Path.Combine(Application.persistentDataPath, fileName);
                File.WriteAllBytes(fallback, imageBytes);
                Debug.LogWarning("[ARPhoto] MediaStore failed. Saved to: " + fallback + "\n" + e);
            }
#elif UNITY_IOS
            // UaaL用: RNと共有可能な一時パスに保存
            string uaalPath = Path.Combine(Application.temporaryCachePath, fileName);
            File.WriteAllBytes(uaalPath, imageBytes);
            Debug.Log($"[ARPhoto] Saved for UaaL: {uaalPath}");

            // UaaLイベント発火（RNに通知）
            OnPhotoCapturedWithPath?.Invoke(uaalPath, Screen.width, Screen.height);

            try
            {
                ARNative_SavePNGToPhotos(imageBytes, imageBytes.Length);
                Debug.Log("[ARPhoto] Saved to iOS Photos.");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[ARPhoto] iOS native save failed: " + e);
            }
#else
            var path = Path.Combine(Application.persistentDataPath, fileName);
            File.WriteAllBytes(path, imageBytes);
            Debug.Log("[ARPhoto] Saved (other platform): " + path);
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
        int screenWidth = Screen.width;
        int screenHeight = Screen.height;

        Debug.Log($"📱 [ARPhoto] Screen resolution: {screenWidth}x{screenHeight} (aspect: {(float)screenHeight / screenWidth:F3})");

        RenderTexture rt = RenderTexture.GetTemporary(screenWidth, screenHeight, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousRT = captureCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;

        try
        {
            // カメラでRenderTextureにレンダリング
            captureCamera.targetTexture = rt;
            captureCamera.Render();

            // RenderTextureからTexture2Dに転送
            RenderTexture.active = rt;

            // アスペクト比に応じてクロップ領域を計算（カメラの最大画角の中心からクロップ）
            int finalWidth = screenWidth;
            int finalHeight = screenHeight;
            int cropX = 0;
            int cropY = 0;

            // エディタモードでのFullモード時、実機相当のアスペクト比を適用
            float effectiveAspectRatio = targetAspectRatio;
#if UNITY_EDITOR
            Debug.Log($"🔍 [ARPhoto] targetAspectRatio={targetAspectRatio:F3}, editorFullUseDeviceAspect={editorFullUseDeviceAspect}");
            if (targetAspectRatio == 0f && editorFullUseDeviceAspect)
            {
                // iPhone 17 Pro Max 相当の縦長アスペクト比 (9:19.5)
                effectiveAspectRatio = 19.5f / 9f; // ≈ 2.167
                Debug.Log($"📱 [ARPhoto] Editor Full mode: Using device aspect ratio {effectiveAspectRatio:F3} (9:19.5)");
            }
#endif

            if (effectiveAspectRatio > 0f)
            {
                // アスペクト比が設定されている場合（Full以外、またはEditor Fullで実機相当）
                // カメラの最大画角（screenWidth x screenHeight）の中心から指定アスペクト比でクロップ

                // モバイルは縦長画面なので、effectiveAspectRatioを「高さ/幅」として扱う
                // 16:9 → 縦長で撮ると 9:16（高さ/幅 = 16/9 = 1.778）
                float targetHeightWidthRatio = effectiveAspectRatio;  // 高さ/幅
                float screenHeightWidthRatio = (float)screenHeight / screenWidth;

                if (screenHeightWidthRatio > targetHeightWidthRatio)
                {
                    // 画面が目標より縦長 → 上下をクロップ
                    finalWidth = screenWidth;
                    finalHeight = Mathf.RoundToInt(screenWidth * targetHeightWidthRatio);
                    cropX = 0;
                    cropY = (screenHeight - finalHeight) / 2;
                }
                else
                {
                    // 画面が目標より横長 → 左右をクロップ
                    finalHeight = screenHeight;
                    finalWidth = Mathf.RoundToInt(screenHeight / targetHeightWidthRatio);
                    cropX = (screenWidth - finalWidth) / 2;
                    cropY = 0;
                }

                Debug.Log($"📐 Cropping from center: target H/W ratio={targetHeightWidthRatio:F3}, screen={screenWidth}x{screenHeight} (H/W={screenHeightWidthRatio:F3}), crop={finalWidth}x{finalHeight}, offset=({cropX},{cropY})");
            }
            else
            {
                Debug.Log($"📐 No crop: Full mode (aspect=0) - using full camera view");
            }

            Texture2D tex = new Texture2D(finalWidth, finalHeight, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(cropX, cropY, finalWidth, finalHeight), 0, 0);
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
    private static void SavePngToAndroidGallery(byte[] imageBytes, string fileName)
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
                        // MIME typeをファイル拡張子から判定
                        string mimeType = fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                         fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                            ? "image/jpeg"
                            : "image/png";

                        values.Call<AndroidJavaObject>("put", DISPLAY_NAME, fileName);
                        values.Call<AndroidJavaObject>("put", MIME_TYPE, mimeType);
                        values.Call<AndroidJavaObject>("put", RELATIVE_PATH, "Pictures/aiCam");

                        var uri = resolver.Call<AndroidJavaObject>("insert",
                            mediaStoreImagesMedia.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI"),
                            values);
                        if (uri == null) throw new Exception("resolver.insert returned null Uri");

                        using (var os = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                        {
                            os.Call("write", new object[] { imageBytes });
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
                File.WriteAllBytes(absPath, imageBytes);

                using (var ms = new AndroidJavaClass("android.media.MediaScannerConnection"))
                    ms.CallStatic("scanFile", activity, new string[] { absPath }, null, null);
            }
        }
    }
#endif

    // サムネイル作成用のヘルパーメソッド（アスペクト比維持版）
    private static Texture2D CreateThumbnailWithAspectRatio(Texture2D source, int maxSize)
    {
        // 元画像のアスペクト比を計算
        float sourceAspect = (float)source.width / source.height;

        // maxSize内に収まるサイズを計算（アスペクト比を維持）
        int targetWidth, targetHeight;
        if (source.width > source.height)
        {
            targetWidth = maxSize;
            targetHeight = Mathf.RoundToInt(maxSize / sourceAspect);
        }
        else
        {
            targetHeight = maxSize;
            targetWidth = Mathf.RoundToInt(maxSize * sourceAspect);
        }

        RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
        rt.filterMode = FilterMode.Bilinear;

        RenderTexture.active = rt;
        Graphics.Blit(source, rt);

        Texture2D thumbnail = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
        thumbnail.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
        thumbnail.Apply();

        RenderTexture.active = null;
        RenderTexture.ReleaseTemporary(rt);

        Debug.Log($"🖼️ Created thumbnail: {source.width}x{source.height} → {targetWidth}x{targetHeight} (aspect: {sourceAspect:F3})");
        return thumbnail;
    }

    // サムネイル作成用のヘルパーメソッド（従来版・互換性のため残す）
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

    /// <summary>
    /// アスペクト比から表示名を取得
    /// </summary>
    private string GetAspectRatioName(float aspectRatio)
    {
        // 許容誤差を考慮して判定
        const float epsilon = 0.01f;

        if (aspectRatio == 0f)
        {
            return "Full";
        }
        else if (Mathf.Abs(aspectRatio - 16f / 9f) < epsilon)
        {
            return "16x9";
        }
        else if (Mathf.Abs(aspectRatio - 3f / 2f) < epsilon)
        {
            return "3x2";
        }
        else if (Mathf.Abs(aspectRatio - 1f) < epsilon)
        {
            return "1x1";
        }
        else
        {
            // カスタムアスペクト比の場合は比率を文字列化
            return $"{aspectRatio:F2}".Replace(".", "_");
        }
    }

    /// <summary>
    /// UI込みでスクリーン全体をキャプチャ（Editor専用）
    /// </summary>
    private Texture2D CaptureScreenWithUI()
    {
        int width = Screen.width;
        int height = Screen.height;

        Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false);
        screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        screenshot.Apply();

        Debug.Log($"📸 [ARPhoto] Captured screen with UI: {width}x{height}");
        return screenshot;
    }

    /// <summary>
    /// UI込みスクリーンショットを保存（Editor専用）
    /// 常にフルスクリーンで保存（アスペクト比クロップなし）
    /// </summary>
    private void SaveScreenshotWithUI(Texture2D screenshot)
    {
        // JPEGエンコード（フルスクリーンのまま保存）
        byte[] imageBytes = screenshot.EncodeToJPG(90);

        // ファイル名: YYYYMMDDhhmmss_AspectRatio_withUI.jpg
        string aspectRatioName = GetAspectRatioName(targetAspectRatio);
        string fileName = $"{DateTime.Now:yyyyMMddHHmmss}_{aspectRatioName}_withUI.jpg";

        // jpgフォルダに保存
        string projectRoot = Path.GetDirectoryName(Application.dataPath);
        string jpgFolder = Path.Combine(projectRoot, "jpg");

        if (!Directory.Exists(jpgFolder))
        {
            Directory.CreateDirectory(jpgFolder);
        }

        string path = Path.Combine(jpgFolder, fileName);
        File.WriteAllBytes(path, imageBytes);
        Debug.Log($"📸 [ARPhoto] Saved screenshot with UI: {path}");
    }

    /// <summary>
    /// ダウンロードフォルダのパスを取得（macOS/Windows対応）
    /// </summary>
    private string GetDownloadsPath()
    {
#if UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
        string home = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
        return Path.Combine(home, "Downloads");
#elif UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
#else
        // その他のプラットフォームではApplication.persistentDataPathを使用
        return Application.persistentDataPath;
#endif
    }
}