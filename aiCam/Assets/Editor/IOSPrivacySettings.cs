#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

public sealed class IOSPrivacySettings : IPreprocessBuildWithReport {
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) {
        if (report.summary.platform != BuildTarget.iOS) return;
        PlayerSettings.iOS.cameraUsageDescription = "このアプリはAR表示のためにカメラを使用します。";
        PlayerSettings.iOS.microphoneUsageDescription = "動画録画時にマイクを使用します。";
    }
}
#endif