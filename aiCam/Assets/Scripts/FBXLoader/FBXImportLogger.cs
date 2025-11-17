using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// FBXインポート時のログとスクリーンショットを自動保存
    /// </summary>
    public class FBXImportLogger : MonoBehaviour
    {
        private static FBXImportLogger instance;
        private List<string> logEntries = new List<string>();
        private bool isCapturing = false;
        private string currentSessionId;
        private string logsDirectory = "FBXImportLogs";

        void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                Application.logMessageReceived += HandleLog;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                Application.logMessageReceived -= HandleLog;
            }
        }

        /// <summary>
        /// ログキャプチャを開始
        /// </summary>
        public static void StartCapture(string sessionId = null)
        {
            if (instance == null)
            {
                GameObject go = new GameObject("FBXImportLogger");
                instance = go.AddComponent<FBXImportLogger>();
            }

            instance.currentSessionId = sessionId ?? $"FBX_Import_{DateTime.Now:yyyyMMdd_HHmmss}";
            instance.logEntries.Clear();
            instance.isCapturing = true;
            instance.logEntries.Add($"=== FBX Import Log Session: {instance.currentSessionId} ===");
            instance.logEntries.Add($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            instance.logEntries.Add("");

            Debug.Log($"[FBXImportLogger] Started capturing logs for session: {instance.currentSessionId}");
        }

        /// <summary>
        /// ログキャプチャを停止し、ファイルに保存
        /// </summary>
        public static void StopCaptureAndSave(bool takeScreenshot = true)
        {
            if (instance == null || !instance.isCapturing) return;

            instance.isCapturing = false;
            instance.logEntries.Add("");
            instance.logEntries.Add($"Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            instance.logEntries.Add($"=== End of Log ===");

            // ディレクトリ作成
            string logsPath = Path.Combine(Application.dataPath, "..", instance.logsDirectory);
            if (!Directory.Exists(logsPath))
            {
                Directory.CreateDirectory(logsPath);
            }

            // ログファイル保存
            string logFilePath = Path.Combine(logsPath, $"{instance.currentSessionId}.txt");
            try
            {
                File.WriteAllLines(logFilePath, instance.logEntries);
                Debug.Log($"[FBXImportLogger] Log saved to: {logFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FBXImportLogger] Failed to save log: {e.Message}");
            }

            // スクリーンショット保存
            if (takeScreenshot)
            {
                instance.CaptureScreenshot(logsPath);
            }
        }

        /// <summary>
        /// スクリーンショットを撮影
        /// </summary>
        private void CaptureScreenshot(string directory)
        {
            string screenshotPath = Path.Combine(directory, $"{currentSessionId}.png");

            // Unity 2019.3以降
            ScreenCapture.CaptureScreenshot(screenshotPath);
            Debug.Log($"[FBXImportLogger] Screenshot saved to: {screenshotPath}");
        }

        /// <summary>
        /// ログをキャプチャ
        /// </summary>
        private void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (!isCapturing) return;

            // フィルタリング（必要に応じて）
            if (ShouldCaptureLog(logString, type))
            {
                string prefix = type switch
                {
                    LogType.Error => "[ERROR] ",
                    LogType.Warning => "[WARN] ",
                    LogType.Exception => "[EXCEPTION] ",
                    _ => ""
                };

                logEntries.Add($"{prefix}{logString}");

                // スタックトレースも保存（エラーと例外のみ）
                if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stackTrace))
                {
                    logEntries.Add(stackTrace);
                    logEntries.Add("");
                }
            }
        }

        /// <summary>
        /// このログをキャプチャすべきか判定
        /// </summary>
        private bool ShouldCaptureLog(string log, LogType type)
        {
            // FBX関連、Avatar関連、TriLib関連のログのみキャプチャ
            if (log.Contains("[RuntimeFBXLoaderBridge]") ||
                log.Contains("[RuntimeHumanoidAvatarBuilder]") ||
                log.Contains("[SkeletonBone]") ||
                log.Contains("[FixJointOrientation]") ||
                log.Contains("TriLib") ||
                type == LogType.Error ||
                type == LogType.Exception ||
                type == LogType.Warning)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 現在のセッションIDを取得
        /// </summary>
        public static string GetCurrentSessionId()
        {
            return instance?.currentSessionId;
        }
    }
}
