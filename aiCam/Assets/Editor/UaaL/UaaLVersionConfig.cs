#if UNITY_IOS
using UnityEngine;
using UnityEditor;
using System.IO;

namespace AICam.Editor.UaaL
{
    /// <summary>
    /// UaaL Version Configuration
    /// Stores version info synced with GitHub milestone
    /// </summary>
    [CreateAssetMenu(fileName = "UaaLVersionConfig", menuName = "UaaL/Version Config")]
    public class UaaLVersionConfig : ScriptableObject
    {
        private const string CONFIG_PATH = "Assets/Editor/UaaL/UaaLVersionConfig.asset";

        [Header("Version Settings")]
        [Tooltip("Marketing version (e.g., 0.6.6) - synced with GitHub milestone")]
        public string version = "1.0.0";

        [Tooltip("Build number - increments on each build")]
        public int buildNumber = 1;

        [Header("GitHub Settings")]
        [Tooltip("GitHub repository (owner/repo format)")]
        public string githubRepo = "dolami-inc/pier";

        [Tooltip("Current milestone title")]
        public string currentMilestone = "";

        [Header("Build Info")]
        [Tooltip("Last build timestamp")]
        public string lastBuildTime = "";

        [Tooltip("Last build platform")]
        public string lastBuildPlatform = "";

        /// <summary>
        /// Get or create the singleton config instance
        /// </summary>
        public static UaaLVersionConfig GetOrCreateConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<UaaLVersionConfig>(CONFIG_PATH);

            if (config == null)
            {
                config = CreateInstance<UaaLVersionConfig>();

                // Ensure directory exists
                string dir = Path.GetDirectoryName(CONFIG_PATH);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                AssetDatabase.CreateAsset(config, CONFIG_PATH);
                AssetDatabase.SaveAssets();
                Debug.Log($"[UaaL] Created version config at: {CONFIG_PATH}");
            }

            return config;
        }

        /// <summary>
        /// Increment build number and save
        /// </summary>
        public void IncrementBuildNumber()
        {
            buildNumber++;
            lastBuildTime = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            lastBuildPlatform = "iOS";
            EditorUtility.SetDirty(this);
            AssetDatabase.SaveAssets();
            Debug.Log($"[UaaL] Build number incremented to: {buildNumber}");
        }

        /// <summary>
        /// Set version from GitHub milestone
        /// </summary>
        public void SetVersionFromMilestone(string milestoneTitle)
        {
            // Extract version from milestone title (e.g., "v0.6.6" -> "0.6.6")
            string newVersion = milestoneTitle.TrimStart('v', 'V');

            // Validate version format
            if (System.Text.RegularExpressions.Regex.IsMatch(newVersion, @"^\d+\.\d+\.\d+$"))
            {
                version = newVersion;
                currentMilestone = milestoneTitle;
                EditorUtility.SetDirty(this);
                AssetDatabase.SaveAssets();
                Debug.Log($"[UaaL] Version set to: {version} from milestone: {milestoneTitle}");
            }
            else
            {
                Debug.LogWarning($"[UaaL] Invalid version format: {milestoneTitle}");
            }
        }

        /// <summary>
        /// Get full version string (version + build number)
        /// </summary>
        public string GetFullVersionString()
        {
            return $"{version} ({buildNumber})";
        }

        /// <summary>
        /// Get CFBundleShortVersionString (marketing version)
        /// </summary>
        public string GetMarketingVersion()
        {
            return version;
        }

        /// <summary>
        /// Get CFBundleVersion (build number)
        /// </summary>
        public string GetBuildVersion()
        {
            return buildNumber.ToString();
        }
    }
}
#endif
