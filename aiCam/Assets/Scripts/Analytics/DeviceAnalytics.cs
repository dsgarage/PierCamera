using UnityEngine;
using System.Collections.Generic;

#if FIREBASE_ANALYTICS
using Firebase;
using Firebase.Analytics;
using Firebase.Extensions;
#endif

namespace PierCamera.Analytics
{
    /// <summary>
    /// デバイス情報をFirebase Analyticsに送信するクラス
    /// </summary>
    public class DeviceAnalytics : MonoBehaviour
    {
        private static DeviceAnalytics _instance;
        public static DeviceAnalytics Instance => _instance;

        [Header("Settings")]
        [SerializeField] private bool logOnStart = true;
        [SerializeField] private bool debugMode = true;

        private bool _isInitialized = false;

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

            InitializeFirebase();
        }

        private void InitializeFirebase()
        {
#if FIREBASE_ANALYTICS
            FirebaseApp.CheckAndFixDependenciesAsync().ContinueWithOnMainThread(task =>
            {
                var dependencyStatus = task.Result;
                if (dependencyStatus == DependencyStatus.Available)
                {
                    _isInitialized = true;
                    Debug.Log("[DeviceAnalytics] Firebase initialized successfully");

                    if (logOnStart)
                    {
                        LogDeviceInfo();
                    }
                }
                else
                {
                    Debug.LogError($"[DeviceAnalytics] Firebase initialization failed: {dependencyStatus}");
                }
            });
#else
            Debug.Log("[DeviceAnalytics] Firebase Analytics not available (FIREBASE_ANALYTICS not defined)");
            if (logOnStart && debugMode)
            {
                LogDeviceInfoDebug();
            }
#endif
        }

        /// <summary>
        /// デバイス情報をFirebase Analyticsに送信
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

#if FIREBASE_ANALYTICS
            if (!_isInitialized)
            {
                Debug.LogWarning("[DeviceAnalytics] Firebase not initialized yet");
                return;
            }

            FirebaseAnalytics.LogEvent("device_info", new Parameter[]
            {
                new Parameter("device_model", deviceModel),
                new Parameter("device_name", friendlyName),
                new Parameter("os_version", osVersion),
                new Parameter("has_lidar", hasLiDAR ? "yes" : "no"),
                new Parameter("device_category", category.ToString()),
                new Parameter("memory_mb", SystemInfo.systemMemorySize),
                new Parameter("graphics_memory_mb", SystemInfo.graphicsMemorySize)
            });

            // ユーザープロパティとしても設定（セグメント分析用）
            FirebaseAnalytics.SetUserProperty("device_category", category.ToString());
            FirebaseAnalytics.SetUserProperty("has_lidar", hasLiDAR ? "yes" : "no");
#endif
        }

        /// <summary>
        /// デバッグ用：Firebase無しでデバイス情報をログ出力
        /// </summary>
        private void LogDeviceInfoDebug()
        {
            string deviceModel = SystemInfo.deviceModel;
            string friendlyName = GetFriendlyDeviceName(deviceModel);
            bool hasLiDAR = HasLiDAR(deviceModel);
            DeviceCategory category = GetDeviceCategory(deviceModel);

            Debug.Log("=== Device Analytics (Debug Mode) ===");
            Debug.Log($"Model: {friendlyName} ({deviceModel})");
            Debug.Log($"OS: {SystemInfo.operatingSystem}");
            Debug.Log($"LiDAR: {hasLiDAR}");
            Debug.Log($"Category: {category}");
            Debug.Log($"Memory: {SystemInfo.systemMemorySize}MB");
            Debug.Log($"Graphics Memory: {SystemInfo.graphicsMemorySize}MB");
            Debug.Log("=====================================");
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
                // iPhone17,x = iPhone 16シリーズ
                // iPhone16,x = iPhone 15シリーズ
                if (deviceModel.StartsWith("iPhone17,1") || deviceModel.StartsWith("iPhone17,2") ||
                    deviceModel.StartsWith("iPhone16,1") || deviceModel.StartsWith("iPhone16,2"))
                {
                    return DeviceCategory.HighEnd; // iPhone 15/16 Pro/Max
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

                // iPhone17,3/4/5 = iPhone 16/Plus
                // iPhone16,3/4 = iPhone 15/Plus
                // iPhone15,4/5 = iPhone 14/Plus
                // iPhone14,4/5/7/8 = iPhone 13/mini
                // iPhone13,1/2 = iPhone 12/mini
                if (deviceModel.StartsWith("iPhone17,") ||
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
#if FIREBASE_ANALYTICS
            if (!_isInitialized)
            {
                Debug.LogWarning("[DeviceAnalytics] Firebase not initialized");
                return;
            }

            if (parameters == null || parameters.Count == 0)
            {
                FirebaseAnalytics.LogEvent(eventName);
            }
            else
            {
                var paramList = new List<Parameter>();
                foreach (var kvp in parameters)
                {
                    if (kvp.Value is string strVal)
                        paramList.Add(new Parameter(kvp.Key, strVal));
                    else if (kvp.Value is int intVal)
                        paramList.Add(new Parameter(kvp.Key, intVal));
                    else if (kvp.Value is long longVal)
                        paramList.Add(new Parameter(kvp.Key, longVal));
                    else if (kvp.Value is double doubleVal)
                        paramList.Add(new Parameter(kvp.Key, doubleVal));
                    else
                        paramList.Add(new Parameter(kvp.Key, kvp.Value.ToString()));
                }
                FirebaseAnalytics.LogEvent(eventName, paramList.ToArray());
            }

            if (debugMode)
            {
                Debug.Log($"[DeviceAnalytics] Event logged: {eventName}");
            }
#else
            if (debugMode)
            {
                Debug.Log($"[DeviceAnalytics] Event (debug): {eventName}");
            }
#endif
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
