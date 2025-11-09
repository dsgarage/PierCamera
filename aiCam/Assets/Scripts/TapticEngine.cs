using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// iOS Taptic Engine wrapper for Unity
/// Provides access to UIImpactFeedbackGenerator, UISelectionFeedbackGenerator, and UINotificationFeedbackGenerator
/// </summary>
public static class TapticEngine
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void TapticEngine_ImpactLight();

    [DllImport("__Internal")]
    private static extern void TapticEngine_ImpactMedium();

    [DllImport("__Internal")]
    private static extern void TapticEngine_ImpactHeavy();

    [DllImport("__Internal")]
    private static extern void TapticEngine_ImpactRigid();

    [DllImport("__Internal")]
    private static extern void TapticEngine_ImpactSoft();

    [DllImport("__Internal")]
    private static extern void TapticEngine_Selection();

    [DllImport("__Internal")]
    private static extern void TapticEngine_NotificationSuccess();

    [DllImport("__Internal")]
    private static extern void TapticEngine_NotificationWarning();

    [DllImport("__Internal")]
    private static extern void TapticEngine_NotificationError();
#endif

    public enum ImpactStyle
    {
        Light,
        Medium,
        Heavy,
        Rigid,  // iOS 13+
        Soft    // iOS 13+
    }

    public enum NotificationType
    {
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// Trigger impact feedback with specified style
    /// </summary>
    public static void Impact(ImpactStyle style = ImpactStyle.Medium)
    {
#if UNITY_IOS && !UNITY_EDITOR
        switch (style)
        {
            case ImpactStyle.Light:
                TapticEngine_ImpactLight();
                break;
            case ImpactStyle.Medium:
                TapticEngine_ImpactMedium();
                break;
            case ImpactStyle.Heavy:
                TapticEngine_ImpactHeavy();
                break;
            case ImpactStyle.Rigid:
                TapticEngine_ImpactRigid();
                break;
            case ImpactStyle.Soft:
                TapticEngine_ImpactSoft();
                break;
        }
#elif UNITY_ANDROID
        // Android fallback
        Handheld.Vibrate();
#else
        // Editor/other platforms - do nothing
        Debug.Log($"[TapticEngine] Impact({style}) - Editor mode");
#endif
    }

    /// <summary>
    /// Trigger selection feedback (used for picker/slider changes)
    /// </summary>
    public static void Selection()
    {
#if UNITY_IOS && !UNITY_EDITOR
        TapticEngine_Selection();
#elif UNITY_ANDROID
        Handheld.Vibrate();
#else
        Debug.Log("[TapticEngine] Selection() - Editor mode");
#endif
    }

    /// <summary>
    /// Trigger notification feedback with specified type
    /// </summary>
    public static void Notification(NotificationType type)
    {
#if UNITY_IOS && !UNITY_EDITOR
        switch (type)
        {
            case NotificationType.Success:
                TapticEngine_NotificationSuccess();
                break;
            case NotificationType.Warning:
                TapticEngine_NotificationWarning();
                break;
            case NotificationType.Error:
                TapticEngine_NotificationError();
                break;
        }
#elif UNITY_ANDROID
        Handheld.Vibrate();
#else
        Debug.Log($"[TapticEngine] Notification({type}) - Editor mode");
#endif
    }
}
