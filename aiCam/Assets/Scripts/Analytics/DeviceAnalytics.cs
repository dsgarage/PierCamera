using UnityEngine;
using System.Collections.Generic;
using AICam.Analytics;

namespace PierCamera.Analytics
{
    /// <summary>
    /// デバイス情報をAnalyticsに送信するクラス
    ///
    /// 注: Firebase SDKを直接使用せず、AnalyticsBridge経由で親アプリに情報を送信
    /// 親アプリ側でFirebase Analytics SDKを使用して実際の送信を行う
    /// </summary>
    public class DeviceAnalytics : MonoBehaviour
    {
        private static DeviceAnalytics _instance;
        public static DeviceAnalytics Instance => _instance;

        [Header("Settings")]
        [SerializeField] private bool logOnStart = true;
        [SerializeField] private bool debugMode = true;

        // LiDAR搭載機種のリスト（iPhone識別子）
        private static readonly HashSet<string> LiDARDevices = new HashSet<string>
        {
            // iPhone 12 Pro / Pro Max
            "iPhone13,3", "iPhone13,4",
            // iPhone 13 Pro / Pro Max
            "iPhone14,2", "iPhone14,3",
            // iPhone 14 Pro / Pro Max
            "iPhone15,2", "iPhone15,3",
            // iPhone 15 Pro / Pro Max
            "iPhone16,1", "iPhone16,2",
            // iPhone 16 Pro / Pro Max
            "iPhone17,1", "iPhone17,2",
            // iPhone 17 Pro / Pro Max
            "iPhone18,1", "iPhone18,2", "iPhone18,3", "iPhone18,4",
            // iPad Pro (2020以降)
            "iPad8,9", "iPad8,10", "iPad8,11", "iPad8,12",
            "iPad13,4", "iPad13,5", "iPad13,6", "iPad13,7",
            "iPad13,8", "iPad13,9", "iPad13,10", "iPad13,11",
            "iPad14,3", "iPad14,4", "iPad14,5", "iPad14,6"
        };

        // デバイスカテゴリ分類
        public enum DeviceCategory
        {
            HighEnd,      // iPhone 15/16 Pro/Max
            MidRange,     // iPhone 12-14 Pro/Max
            Standard,     // iPhone 12-16 無印/Plus/mini
            LowEnd,       // iPhone 11以前, SE
            Unknown
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            // AnalyticsBridgeを初期化
            AnalyticsBridge.Initialize();
        }

        private void Start()
        {
            if (logOnStart)
            {
                LogDeviceInfo();
            }
        }

        /// <summary>
        /// デバイス情報をAnalyticsに送信
        /// </summary>
        public void LogDeviceInfo()
        {
            string deviceModel = SystemInfo.deviceModel;
            string osVersion = SystemInfo.operatingSystem;
            bool hasLiDAR = HasLiDAR(deviceModel);
            DeviceCategory category = GetDeviceCategory(deviceModel);
            string friendlyName = GetFriendlyDeviceName(deviceModel);

            if (debugMode)
            {
                Debug.Log($"[DeviceAnalytics] Device: {friendlyName} ({deviceModel})");
                Debug.Log($"[DeviceAnalytics] OS: {osVersion}");
                Debug.Log($"[DeviceAnalytics] LiDAR: {hasLiDAR}");
                Debug.Log($"[DeviceAnalytics] Category: {category}");
            }

            // AnalyticsBridge経由で親アプリに送信
            AnalyticsBridge.LogDeviceInfo(
                deviceModel,
                friendlyName,
                osVersion,
                hasLiDAR,
                category.ToString(),
                SystemInfo.systemMemorySize,
                SystemInfo.graphicsMemorySize
            );
        }

        /// <summary>
        /// LiDAR搭載機種かどうかを判定
        /// </summary>
        public static bool HasLiDAR(string deviceModel = null)
        {
            if (string.IsNullOrEmpty(deviceModel))
            {
                deviceModel = SystemInfo.deviceModel;
            }
            return LiDARDevices.Contains(deviceModel);
        }

        /// <summary>
        /// デバイスカテゴリを判定
        /// </summary>
        public static DeviceCategory GetDeviceCategory(string deviceModel = null)
        {
            if (string.IsNullOrEmpty(deviceModel))
            {
                deviceModel = SystemInfo.deviceModel;
            }

            // iPhone識別子からカテゴリを判定
            if (deviceModel.StartsWith("iPhone"))
            {
                // iPhone18,x = iPhone 17シリーズ
                // iPhone17,x = iPhone 16シリーズ
                // iPhone16,x = iPhone 15シリーズ
                if (deviceModel.StartsWith("iPhone18,1") || deviceModel.StartsWith("iPhone18,2") ||
                    deviceModel.StartsWith("iPhone18,3") || deviceModel.StartsWith("iPhone18,4") ||
                    deviceModel.StartsWith("iPhone17,1") || deviceModel.StartsWith("iPhone17,2") ||
                    deviceModel.StartsWith("iPhone16,1") || deviceModel.StartsWith("iPhone16,2"))
                {
                    return DeviceCategory.HighEnd; // iPhone 15/16/17 Pro/Max
                }

                // iPhone15,2/3 = iPhone 14 Pro/Max
                // iPhone14,2/3 = iPhone 13 Pro/Max
                // iPhone13,3/4 = iPhone 12 Pro/Max
                if (deviceModel.StartsWith("iPhone15,2") || deviceModel.StartsWith("iPhone15,3") ||
                    deviceModel.StartsWith("iPhone14,2") || deviceModel.StartsWith("iPhone14,3") ||
                    deviceModel.StartsWith("iPhone13,3") || deviceModel.StartsWith("iPhone13,4"))
                {
                    return DeviceCategory.MidRange; // iPhone 12-14 Pro/Max
                }

                // iPhone18,5/6 = iPhone 17/Plus (推定)
                // iPhone17,3/4/5 = iPhone 16/Plus
                // iPhone16,3/4 = iPhone 15/Plus
                // iPhone15,4/5 = iPhone 14/Plus
                // iPhone14,4/5/7/8 = iPhone 13/mini
                // iPhone13,1/2 = iPhone 12/mini
                if (deviceModel.StartsWith("iPhone18,5") || deviceModel.StartsWith("iPhone18,6") ||
                    deviceModel.StartsWith("iPhone17,") ||
                    deviceModel.StartsWith("iPhone16,") ||
                    deviceModel.StartsWith("iPhone15,4") || deviceModel.StartsWith("iPhone15,5") ||
                    deviceModel.StartsWith("iPhone14,4") || deviceModel.StartsWith("iPhone14,5") ||
                    deviceModel.StartsWith("iPhone14,7") || deviceModel.StartsWith("iPhone14,8") ||
                    deviceModel.StartsWith("iPhone13,1") || deviceModel.StartsWith("iPhone13,2"))
                {
                    return DeviceCategory.Standard; // iPhone 12-16 無印/Plus/mini
                }

                // iPhone 11以前、SE
                return DeviceCategory.LowEnd;
            }

            return DeviceCategory.Unknown;
        }

        /// <summary>
        /// デバイス識別子から人間が読める名前に変換
        /// </summary>
        public static string GetFriendlyDeviceName(string deviceModel = null)
        {
            if (string.IsNullOrEmpty(deviceModel))
            {
                deviceModel = SystemInfo.deviceModel;
            }

            var nameMap = new Dictionary<string, string>
            {
                // iPhone 17
                {"iPhone18,1", "iPhone 17 Pro"},
                {"iPhone18,2", "iPhone 17 Pro Max"},
                {"iPhone18,3", "iPhone 17 Pro"},
                {"iPhone18,4", "iPhone 17 Pro Max"},
                {"iPhone18,5", "iPhone 17"},
                {"iPhone18,6", "iPhone 17 Plus"},
                // iPhone 16
                {"iPhone17,1", "iPhone 16 Pro"},
                {"iPhone17,2", "iPhone 16 Pro Max"},
                {"iPhone17,3", "iPhone 16"},
                {"iPhone17,4", "iPhone 16 Plus"},
                // iPhone 15
                {"iPhone16,1", "iPhone 15 Pro"},
                {"iPhone16,2", "iPhone 15 Pro Max"},
                {"iPhone15,4", "iPhone 15"},
                {"iPhone15,5", "iPhone 15 Plus"},
                // iPhone 14
                {"iPhone15,2", "iPhone 14 Pro"},
                {"iPhone15,3", "iPhone 14 Pro Max"},
                {"iPhone14,7", "iPhone 14"},
                {"iPhone14,8", "iPhone 14 Plus"},
                // iPhone 13
                {"iPhone14,2", "iPhone 13 Pro"},
                {"iPhone14,3", "iPhone 13 Pro Max"},
                {"iPhone14,4", "iPhone 13 mini"},
                {"iPhone14,5", "iPhone 13"},
                // iPhone 12
                {"iPhone13,1", "iPhone 12 mini"},
                {"iPhone13,2", "iPhone 12"},
                {"iPhone13,3", "iPhone 12 Pro"},
                {"iPhone13,4", "iPhone 12 Pro Max"},
                // iPhone 11
                {"iPhone12,1", "iPhone 11"},
                {"iPhone12,3", "iPhone 11 Pro"},
                {"iPhone12,5", "iPhone 11 Pro Max"},
                // iPhone SE
                {"iPhone14,6", "iPhone SE (3rd)"},
                {"iPhone12,8", "iPhone SE (2nd)"},
                // iPhone XS/XR
                {"iPhone11,2", "iPhone XS"},
                {"iPhone11,4", "iPhone XS Max"},
                {"iPhone11,6", "iPhone XS Max"},
                {"iPhone11,8", "iPhone XR"},
                // iPhone X
                {"iPhone10,3", "iPhone X"},
                {"iPhone10,6", "iPhone X"},
            };

            return nameMap.TryGetValue(deviceModel, out string name) ? name : deviceModel;
        }

        /// <summary>
        /// カスタムイベントを送信
        /// </summary>
        public void LogCustomEvent(string eventName, Dictionary<string, object> parameters = null)
        {
            string parametersJson = "{}";

            if (parameters != null && parameters.Count > 0)
            {
                var jsonParts = new List<string>();
                foreach (var kvp in parameters)
                {
                    string value;
                    if (kvp.Value is string strVal)
                        value = $"\"{EscapeJson(strVal)}\"";
                    else if (kvp.Value is bool boolVal)
                        value = boolVal ? "true" : "false";
                    else
                        value = kvp.Value.ToString();

                    jsonParts.Add($"\"{EscapeJson(kvp.Key)}\":{value}");
                }
                parametersJson = "{" + string.Join(",", jsonParts) + "}";
            }

            // AnalyticsBridge経由で親アプリに送信
            AnalyticsBridge.LogEvent(eventName, parametersJson);

            if (debugMode)
            {
                Debug.Log($"[DeviceAnalytics] Event logged: {eventName}");
            }
        }

        /// <summary>
        /// JSON文字列エスケープ
        /// </summary>
        private static string EscapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return "";
            return str.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        /// <summary>
        /// アプリ起動イベント
        /// </summary>
        public void LogAppLaunch()
        {
            LogCustomEvent("app_launch", new Dictionary<string, object>
            {
                {"device_category", GetDeviceCategory().ToString()},
                {"has_lidar", HasLiDAR() ? "yes" : "no"}
            });
        }

        /// <summary>
        /// 写真撮影イベント
        /// </summary>
        public void LogPhotoCapture(string mode, bool withAvatar)
        {
            LogCustomEvent("photo_capture", new Dictionary<string, object>
            {
                {"mode", mode},
                {"with_avatar", withAvatar ? "yes" : "no"}
            });
        }

        /// <summary>
        /// アバターロードイベント
        /// </summary>
        public void LogAvatarLoad(string format, bool success, float loadTimeSeconds)
        {
            LogCustomEvent("avatar_load", new Dictionary<string, object>
            {
                {"format", format}, // "vrm" or "fbx"
                {"success", success ? "yes" : "no"},
                {"load_time_seconds", loadTimeSeconds}
            });
        }
    }
}
