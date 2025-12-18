using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アラートバーコントローラのスタブクラス
    /// </summary>
    public static class AlertBarController
    {
        public enum AlertType
        {
            Info,
            Warning,
            Error,
            Success
        }

        public static void ShowInfo(string message)
        {
            Debug.Log($"[Info] {message}");
        }

        public static void ShowWarning(string message)
        {
            Debug.LogWarning($"[Warning] {message}");
        }

        public static void ShowError(string message)
        {
            Debug.LogError($"[Error] {message}");
        }

        public static void ShowSuccess(string message)
        {
            Debug.Log($"[Success] {message}");
        }

        public static void Show(string message, AlertType type = AlertType.Info)
        {
            switch (type)
            {
                case AlertType.Info:
                    ShowInfo(message);
                    break;
                case AlertType.Warning:
                    ShowWarning(message);
                    break;
                case AlertType.Error:
                    ShowError(message);
                    break;
                case AlertType.Success:
                    ShowSuccess(message);
                    break;
            }
        }

        public static void Hide()
        {
            // スタブ実装
        }

        // Error methods
        public static void ErrorFileNotFound(string message)
        {
            Debug.LogError($"[AlertBarController] File not found: {message}");
        }

        public static void ErrorFileFormatInvalid(string message)
        {
            Debug.LogError($"[AlertBarController] Invalid file format: {message}");
        }

        public static void ErrorVrmLoadFailed(string message)
        {
            Debug.LogError($"[AlertBarController] VRM load failed: {message}");
        }

        public static void ErrorFbxLoadFailed(string message)
        {
            Debug.LogError($"[AlertBarController] FBX load failed: {message}");
        }

        public static void ErrorAvatarBuildFailed(string message)
        {
            Debug.LogError($"[AlertBarController] Avatar build failed: {message}");
        }

        // Warning methods
        public static void WarnManifestNotFound(string message)
        {
            Debug.LogWarning($"[AlertBarController] Manifest not found: {message}");
        }

        public static void WarnVrmVersionUnknown()
        {
            Debug.LogWarning("[AlertBarController] VRM version unknown");
        }
    }
}
