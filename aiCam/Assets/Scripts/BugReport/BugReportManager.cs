using UnityEngine;
using System;
using System.IO;
using System.Text;
using System.Collections;

namespace AICam.BugReport
{
    /// <summary>
    /// バグレポート機能
    /// Issue #413: App内にバグレポの実装
    ///
    /// 収集情報:
    /// - スクリーンショット
    /// - デバイス情報
    /// - ユーザーコメント
    /// - 最近のログ
    /// </summary>
    public class BugReportManager : MonoBehaviour
    {
        public static BugReportManager Instance { get; private set; }

        [Header("Settings")]
        [Tooltip("バグレポート送信先メールアドレス")]
        [SerializeField] private string reportEmailAddress = "bug.repo@pier.is";

        [Tooltip("メールの件名プレフィックス")]
        [SerializeField] private string emailSubjectPrefix = "[Pier Bug Report]";

        [Header("Debug")]
        [SerializeField] private bool enableDebugLog = true;

        // イベント
        public event Action OnReportStarted;
        public event Action<bool> OnReportCompleted;

        // スクリーンショット保存用
        private string lastScreenshotPath;
        private Texture2D lastScreenshot;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                Log("[BugReportManager] Initialized");
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// バグレポートプロセスを開始
        /// </summary>
        public void StartBugReport()
        {
            Log("[BugReportManager] Starting bug report process...");
            OnReportStarted?.Invoke();

            // スクリーンショットを撮影してからメールを開く
            StartCoroutine(CaptureAndOpenMail());
        }

        private IEnumerator CaptureAndOpenMail()
        {
            // 1フレーム待機してUIを非表示にする時間を与える
            yield return null;

            // スクリーンショットを撮影
            yield return StartCoroutine(CaptureScreenshot());

            // デバイス情報を収集
            string deviceInfo = GetDeviceInfo();

            // メールを開く
            OpenMailComposer(deviceInfo);

            OnReportCompleted?.Invoke(true);
        }

        private IEnumerator CaptureScreenshot()
        {
            // 1フレーム待機してレンダリングを完了させる
            yield return new WaitForEndOfFrame();

            try
            {
                // スクリーンショットを撮影
                lastScreenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
                lastScreenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                lastScreenshot.Apply();

                // PNGとして保存
                byte[] bytes = lastScreenshot.EncodeToPNG();
                string filename = $"bugreport_{DateTime.Now:yyyyMMdd_HHmmss}.png";
                lastScreenshotPath = Path.Combine(Application.temporaryCachePath, filename);
                File.WriteAllBytes(lastScreenshotPath, bytes);

                Log($"[BugReportManager] Screenshot saved: {lastScreenshotPath}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[BugReportManager] Failed to capture screenshot: {ex.Message}");
                lastScreenshotPath = null;
            }
        }

        /// <summary>
        /// デバイス情報を収集
        /// </summary>
        private string GetDeviceInfo()
        {
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("--- Device Information ---");
            sb.AppendLine($"Device Model: {SystemInfo.deviceModel}");
            sb.AppendLine($"Device Name: {SystemInfo.deviceName}");
            sb.AppendLine($"Device Type: {SystemInfo.deviceType}");
            sb.AppendLine($"OS: {SystemInfo.operatingSystem}");
            sb.AppendLine($"OS Family: {SystemInfo.operatingSystemFamily}");

            sb.AppendLine();
            sb.AppendLine("--- App Information ---");
            sb.AppendLine($"App Version: {Application.version}");
            sb.AppendLine($"Unity Version: {Application.unityVersion}");
            sb.AppendLine($"Bundle ID: {Application.identifier}");
            sb.AppendLine($"Platform: {Application.platform}");

            sb.AppendLine();
            sb.AppendLine("--- Hardware ---");
            sb.AppendLine($"Processor: {SystemInfo.processorType}");
            sb.AppendLine($"Processor Count: {SystemInfo.processorCount}");
            sb.AppendLine($"System Memory: {SystemInfo.systemMemorySize} MB");
            sb.AppendLine($"Graphics Device: {SystemInfo.graphicsDeviceName}");
            sb.AppendLine($"Graphics Memory: {SystemInfo.graphicsMemorySize} MB");
            sb.AppendLine($"Graphics API: {SystemInfo.graphicsDeviceType}");

            sb.AppendLine();
            sb.AppendLine("--- Screen ---");
            sb.AppendLine($"Resolution: {Screen.width} x {Screen.height}");
            sb.AppendLine($"DPI: {Screen.dpi}");
            sb.AppendLine($"Orientation: {Screen.orientation}");

            sb.AppendLine();
            sb.AppendLine("--- Performance ---");
            sb.AppendLine($"Current FPS: {(int)(1.0f / Time.deltaTime)}");
            sb.AppendLine($"Target FPS: {Application.targetFrameRate}");
            sb.AppendLine($"Time Since Start: {Time.realtimeSinceStartup:F1} seconds");

#if UNITY_IOS
            sb.AppendLine();
            sb.AppendLine("--- iOS Specific ---");
            sb.AppendLine($"iOS Version: {UnityEngine.iOS.Device.systemVersion}");
            sb.AppendLine($"Device Generation: {UnityEngine.iOS.Device.generation}");
#endif

            return sb.ToString();
        }

        /// <summary>
        /// メール作成画面を開く
        /// </summary>
        private void OpenMailComposer(string deviceInfo)
        {
            string subject = $"{emailSubjectPrefix} {Application.version} - {DateTime.Now:yyyy-MM-dd HH:mm}";

            StringBuilder body = new StringBuilder();
            body.AppendLine("--- Please describe the issue below ---");
            body.AppendLine();
            body.AppendLine("[問題の説明を記入してください]");
            body.AppendLine();
            body.AppendLine();
            body.AppendLine("--- Steps to Reproduce ---");
            body.AppendLine("1. ");
            body.AppendLine("2. ");
            body.AppendLine("3. ");
            body.AppendLine();
            body.AppendLine();
            body.AppendLine(deviceInfo);

            if (!string.IsNullOrEmpty(lastScreenshotPath) && File.Exists(lastScreenshotPath))
            {
                body.AppendLine();
                body.AppendLine("--- Screenshot ---");
                body.AppendLine($"Screenshot saved at: {lastScreenshotPath}");
                body.AppendLine("(Please attach manually from Photos app if email client supports)");
            }

            // URLエンコード
            string encodedSubject = Uri.EscapeDataString(subject);
            string encodedBody = Uri.EscapeDataString(body.ToString());

            // mailto URLを生成
            string mailtoUrl = $"mailto:{reportEmailAddress}?subject={encodedSubject}&body={encodedBody}";

            Log($"[BugReportManager] Opening mail composer: {reportEmailAddress}");

            // メールアプリを開く
            Application.OpenURL(mailtoUrl);
        }

        /// <summary>
        /// スクリーンショットを写真ライブラリに保存（オプション）
        /// ユーザーがメールに添付しやすくするため
        /// </summary>
        public void SaveScreenshotToGallery()
        {
            if (string.IsNullOrEmpty(lastScreenshotPath) || !File.Exists(lastScreenshotPath))
            {
                Debug.LogWarning("[BugReportManager] No screenshot available to save");
                return;
            }

#if UNITY_IOS && !UNITY_EDITOR
            // iOSではNativeGalleryプラグインが必要
            // ここでは簡易的にファイルパスを表示
            Log($"[BugReportManager] Screenshot path: {lastScreenshotPath}");
#endif
        }

        /// <summary>
        /// 最後のスクリーンショットパスを取得
        /// </summary>
        public string GetLastScreenshotPath()
        {
            return lastScreenshotPath;
        }

        private void Log(string message)
        {
            if (enableDebugLog)
            {
                Debug.Log(message);
            }
        }

        private void OnDestroy()
        {
            // 一時ファイルをクリーンアップ
            if (!string.IsNullOrEmpty(lastScreenshotPath) && File.Exists(lastScreenshotPath))
            {
                try
                {
                    File.Delete(lastScreenshotPath);
                }
                catch { }
            }

            if (lastScreenshot != null)
            {
                Destroy(lastScreenshot);
            }
        }
    }
}
