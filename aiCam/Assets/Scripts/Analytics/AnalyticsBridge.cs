using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace AICam.Analytics
{
    /// <summary>
    /// Analytics ブリッジ
    /// Unity側からの情報を親アプリ（iOS）のFirebase SDKに渡す
    ///
    /// 設計方針：
    /// - 親→PierCamera の参照関係を遵守
    /// - 既存のNativeCallProxy (sendMessageToMobileApp) を使用
    /// - JSON形式でメッセージを送信し、親アプリ側でFirebase SDKを呼び出す
    /// </summary>
    public static class AnalyticsBridge
    {
        private const string TAG = "[AnalyticsBridge]";

        // メッセージタイプ定義
        private const string TYPE_SET_CUSTOM_KEY = "analytics_setCustomKey";
        private const string TYPE_LOG = "analytics_log";
        private const string TYPE_LOG_ERROR = "analytics_logError";
        private const string TYPE_LOG_EVENT = "analytics_logEvent";
        private const string TYPE_SET_USER_PROPERTY = "analytics_setUserProperty";

#if UNITY_IOS && !UNITY_EDITOR
        // 既存のNativeCallProxyを使用
        [DllImport("__Internal")]
        private static extern void sendMessageToMobileApp(string message);
#endif

        /// <summary>
        /// 親アプリにメッセージを送信
        /// </summary>
        private static void SendMessage(string jsonMessage)
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                sendMessageToMobileApp(jsonMessage);
                Debug.Log($"{TAG} Sent: {jsonMessage}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"{TAG} SendMessage failed: {e.Message}");
            }
#else
            Debug.Log($"{TAG} [Local] {jsonMessage}");
#endif
        }

        #region Crashlytics Functions

        /// <summary>
        /// カスタムキーを設定（Crashlytics用）
        /// </summary>
        public static void SetCustomKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;

            string json = $"{{\"type\":\"{TYPE_SET_CUSTOM_KEY}\",\"key\":\"{EscapeJson(key)}\",\"value\":\"{EscapeJson(value ?? "")}\"}}";
            SendMessage(json);
        }

        /// <summary>
        /// ログメッセージを記録（Crashlytics用）
        /// </summary>
        public static void Log(string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            string json = $"{{\"type\":\"{TYPE_LOG}\",\"message\":\"{EscapeJson(message)}\"}}";
            SendMessage(json);
        }

        /// <summary>
        /// 非致命的エラーを記録（Crashlytics用）
        /// </summary>
        public static void LogNonFatalError(string domain, string message)
        {
            string json = $"{{\"type\":\"{TYPE_LOG_ERROR}\",\"domain\":\"{EscapeJson(domain ?? "Unity")}\",\"message\":\"{EscapeJson(message ?? "")}\"}}";
            SendMessage(json);
        }

        #endregion

        #region Analytics Functions

        /// <summary>
        /// イベントを送信（Firebase Analytics用）
        /// </summary>
        /// <param name="eventName">イベント名</param>
        /// <param name="parametersJson">パラメータのJSON文字列（例: {"key1":"value1","key2":123}）</param>
        public static void LogEvent(string eventName, string parametersJson = null)
        {
            if (string.IsNullOrEmpty(eventName)) return;

            // parametersJsonはすでにJSON形式なので、そのまま埋め込む
            string paramsStr = string.IsNullOrEmpty(parametersJson) ? "{}" : parametersJson;
            string json = $"{{\"type\":\"{TYPE_LOG_EVENT}\",\"eventName\":\"{EscapeJson(eventName)}\",\"parameters\":{paramsStr}}}";
            SendMessage(json);
        }

        /// <summary>
        /// ユーザープロパティを設定（Firebase Analytics用）
        /// </summary>
        public static void SetUserProperty(string name, string value)
        {
            if (string.IsNullOrEmpty(name)) return;

            string json = $"{{\"type\":\"{TYPE_SET_USER_PROPERTY}\",\"name\":\"{EscapeJson(name)}\",\"value\":\"{EscapeJson(value ?? "")}\"}}";
            SendMessage(json);
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// アバター情報をCrashlyticsに設定
        /// </summary>
        public static void SetAvatarInfo(string fileName, long fileSize)
        {
            SetCustomKey("avatar_filename", fileName ?? "unknown");
            SetCustomKey("avatar_filesize_bytes", fileSize.ToString());

            float fileSizeMB = fileSize / 1024f / 1024f;
            SetCustomKey("avatar_filesize_mb", fileSizeMB.ToString("F2"));

            Log($"Avatar loaded: {fileName} ({fileSize} bytes, {fileSizeMB:F2} MB)");
        }

        /// <summary>
        /// スロット情報を設定
        /// </summary>
        public static void SetSlotInfo(int slotIndex)
        {
            SetCustomKey("avatar_slot_index", slotIndex.ToString());
        }

        /// <summary>
        /// アバターロードエラーを記録
        /// </summary>
        public static void LogAvatarLoadError(string fileName, string errorMessage)
        {
            SetCustomKey("avatar_load_error_file", fileName ?? "unknown");
            Log($"Avatar load error: {fileName} - {errorMessage}");
            LogNonFatalError("AvatarLoad", $"{fileName}: {errorMessage}");
        }

        /// <summary>
        /// デバイス情報イベントを送信
        /// </summary>
        public static void LogDeviceInfo(string deviceModel, string friendlyName, string osVersion,
            bool hasLiDAR, string category, int memoryMB, int graphicsMemoryMB)
        {
            string json = $"{{\"device_model\":\"{EscapeJson(deviceModel)}\"," +
                          $"\"device_name\":\"{EscapeJson(friendlyName)}\"," +
                          $"\"os_version\":\"{EscapeJson(osVersion)}\"," +
                          $"\"has_lidar\":\"{(hasLiDAR ? "yes" : "no")}\"," +
                          $"\"device_category\":\"{EscapeJson(category)}\"," +
                          $"\"memory_mb\":{memoryMB}," +
                          $"\"graphics_memory_mb\":{graphicsMemoryMB}}}";

            LogEvent("device_info", json);

            SetUserProperty("device_category", category);
            SetUserProperty("has_lidar", hasLiDAR ? "yes" : "no");
        }

        /// <summary>
        /// JSON文字列エスケープ
        /// </summary>
        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        #endregion

        #region Initialization (Optional)

        /// <summary>
        /// ブリッジを初期化（オプション - 親アプリに初期化通知を送信）
        /// </summary>
        public static void Initialize()
        {
            string json = $"{{\"type\":\"analytics_init\",\"version\":\"1.0\"}}";
            SendMessage(json);
            Debug.Log($"{TAG} Initialized");
        }

        #endregion
    }
}
