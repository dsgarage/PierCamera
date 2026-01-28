using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// バージョン情報の表示を管理するサービス。
    /// </summary>
    public class VersionInfoService
    {
        public VersionInfoService(VisualElement root)
        {
            var versionLabel = root.Q<Label>("versionLabel");
            if (versionLabel != null)
            {
                string version = Application.version;
                string buildNumber = GetBuildNumber();
                versionLabel.text = $"v{version} ({buildNumber})";
            }
        }

        private static string GetBuildNumber()
        {
#if UNITY_EDITOR
            return System.DateTime.Now.ToString("yyMMdd");
#elif UNITY_ANDROID
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var packageManager = activity.Call<AndroidJavaObject>("getPackageManager"))
                using (var packageInfo = packageManager.Call<AndroidJavaObject>("getPackageInfo",
                    activity.Call<string>("getPackageName"), 0))
                {
                    var versionCode = packageInfo.Get<int>("versionCode");
                    return versionCode.ToString();
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[VersionInfo] Failed to get Android versionCode: {e.Message}");
                return "1";
            }
#elif UNITY_IOS
            string buildGuid = Application.buildGUID;
            if (!string.IsNullOrEmpty(buildGuid) && buildGuid.Length >= 8)
            {
                return buildGuid.Substring(0, 8);
            }
            return "1";
#else
            return "1";
#endif
        }
    }
}
