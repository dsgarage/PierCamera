using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems; // ★ 追加：各種Depth列挙体
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

/// <summary>
/// 方法②：撮影の瞬間だけUIを非表示にし、AR背景＋3Dのみをスクショ保存。
/// iOSのネイティブ撮影モード（互換）も従来どおり残しています。
/// </summary>
public sealed class ARPhotoController : MonoBehaviour
{
    [Header("References (optional)")]
    [SerializeField] private ARSession arSession;                 // 未割当なら自動取得
    [SerializeField] private OcclusionToggle occlusionToggle;     // 任意。なければ未設定でOK

    [Header("UI Hiding (Method #2)")]
    [Tooltip("撮影時に一時的に不可視にするUI（CanvasGroup）を割り当て")]
    [SerializeField] private CanvasGroup[] uiToHide;
    [Tooltip("重複タップで多重撮影を許可するか")]
    [SerializeField] private bool allowConcurrentCapture = false;

    [Header("iOS Save Mode")]
    [SerializeField] private SaveModeIOS iosSaveMode = SaveModeIOS.CompositeScreenshot; // 既定: 合成スクショ

    public enum SaveModeIOS
    {
        CompositeScreenshot,  // 推奨: 画面に見えるまま保存（ARSession停止なし、UIは本スクリプトで非表示化）
        NativeCamera          // 互換: ネイティブ撮影（ARSession一時停止→必ず復帰）
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void ARNative_SavePNGToPhotos(byte[] pngBytes, int length);
    [DllImport("__Internal")] private static extern void ARNative_CaptureOneShot();
#endif

    private bool _isCapturing;

    private void Awake()
    {
        if (!arSession)
            arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
    }

    // ★ 修正：OcclusionToggleに依存せずローカルで深度を復帰
    private IEnumerator RestoreDepthNextFrame()
    {
        yield return null; // 次フレームまで待つ

        var occ = UnityEngine.Object.FindFirstObjectByType<AROcclusionManager>(FindObjectsInactive.Exclude);
        if (!occ) yield break;

        try { occ.requestedEnvironmentDepthMode = EnvironmentDepthMode.Medium; } catch { }
        try { occ.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion; } catch { }
        try { occ.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled; } catch { }
        try { occ.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled; } catch { }
    }

    /// <summary>UIボタンなどから呼ぶ。</summary>
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

    /// <summary>
    /// 方法②：画面スクショ保存。ただし撮影直前にUI(CanvasGroup)を不可視化してUIだけを写さない。
    /// ARSessionは停止しないため安全。
    /// </summary>
    private IEnumerator CaptureCompositedAndSave()
    {
        _isCapturing = true;
        try
        {
            // --- ① UIを不可視化（描画・クリックともに無効化） ---
            SetUIVisible(false);

            // レイアウト／キャンバスの反映を待つ → そのフレームの描画完了を待つ
            yield return null;
            yield return new WaitForEndOfFrame();

            // --- ② 画面キャプチャ（= AR背景＋3Dのみ。UIは非表示のため入らない） ---
            var tex = ScreenCapture.CaptureScreenshotAsTexture(); // RGBA32
            if (tex == null)
            {
                Debug.LogError("[ARPhoto] CaptureScreenshotAsTexture returned null");
                yield break;
            }

            var png = tex.EncodeToPNG();
            Destroy(tex);

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
            // --- ③ UIを必ず復帰 ---
            SetUIVisible(true);
            _isCapturing = false;
        }
    }

    // UIの表示/非表示をまとめて切り替え
    private void SetUIVisible(bool visible)
    {
        if (uiToHide == null) return;
        foreach (var cg in uiToHide)
        {
            if (!cg) continue;
            cg.alpha = visible ? 1f : 0f;
            cg.interactable = visible;
            cg.blocksRaycasts = visible;
        }
    }

#if UNITY_IOS && !UNITY_EDITOR
    /// <summary>
    /// iOSネイティブ撮影（互換）。ARSession を一時停止するため、try/finallyで必ず復帰させる。
    /// ※このモードはUIの可視/不可視に関わらず「画面キャプチャ」ではないので、UIはもともと写りません。
    /// </summary>
    private IEnumerator CaptureIOS_Native()
    {
        // 1) 必要なら深度を一時停止
        occlusionToggle?.DisableDepthNow();
        yield return null;

        // 2) セッションを止める（現在ONのときだけ）
        bool wasRunning = (arSession != null && arSession.enabled);
        if (arSession != null && wasRunning)
            arSession.enabled = false;

        try
        {
            // 3) ネイティブ撮影呼び出し
            ARNative_CaptureOneShot();

            // 端末側の保存完了を軽く待つ（必要に応じて調整）
            yield return new WaitForSeconds(0.8f);
        }
        finally
        {
            // 4) 例外や早期returnがあっても必ず復帰（finally内ではyieldしない）
            if (arSession != null)
                arSession.enabled = true;

            // 深度復帰は次フレームに回す
            StartCoroutine(RestoreDepthNextFrame());
        }
    }
#endif

#if UNITY_ANDROID
    // Android 10+ : MediaStore + RELATIVE_PATH で Pictures/aiCam に保存
    // Android 9-  : 旧外部ストレージ直書き + MediaScanner フォールバック
    private static void SavePngToAndroidGallery(byte[] pngBytes, string fileName)
    {
        using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
        {
            int sdkInt;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                sdkInt = version.GetStatic<int>("SDK_INT");
            }

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
                {
                    picturesDir = environment.CallStatic<AndroidJavaObject>("getExternalStoragePublicDirectory",
                        environment.GetStatic<string>("DIRECTORY_PICTURES")).Call<string>("getAbsolutePath");
                }
                var folder = Path.Combine(picturesDir, "aiCam");
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

                var absPath = Path.Combine(folder, fileName);
                File.WriteAllBytes(absPath, pngBytes);

                using (var ms = new AndroidJavaClass("android.media.MediaScannerConnection"))
                {
                    ms.CallStatic("scanFile", activity, new string[] { absPath }, null, null);
                }
            }
        }
    }
#endif
}