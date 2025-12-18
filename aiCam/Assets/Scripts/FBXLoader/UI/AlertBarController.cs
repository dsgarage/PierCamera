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
    }
}
