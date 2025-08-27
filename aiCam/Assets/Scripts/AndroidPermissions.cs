// aiCam / AndroidPermissions.cs
// Android 専用：カメラ・マイクなどの権限を簡易に確認・要求します。
// 公開APIは既存の EnsureCameraPermission のみ維持しています。
#nullable enable
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public sealed class AndroidPermissions : MonoBehaviour
{
#if UNITY_ANDROID
    /// <summary>
    /// カメラ権限を確認し、未付与ならシステムダイアログで要求します。
    /// UI は最小限。必要に応じてプロジェクト独自の説明UIを出すなど拡張してください。
    /// </summary>
    public void EnsureCameraPermission()
    {
        if (Permission.HasUserAuthorizedPermission(Permission.Camera)) return;

        // 事前説明（ローカライズ簡易版）
        string msg = Application.systemLanguage == SystemLanguage.Japanese
            ? "AR表示のためにカメラへのアクセス許可が必要です。"
            : "Camera access is required for AR.";
        Debug.Log($"[AndroidPermissions] {msg}");

        Permission.RequestUserPermission(Permission.Camera);
    }
#endif
}
