using UnityEngine;
using UnityEngine.UIElements;
using AICam.Core;

namespace AICam.UI
{
    /// <summary>
    /// アラート通知サービス。alertBar / alertMessage / alertClose を管理し、
    /// ShowInfo / ShowWarning / ShowError / HideAlert を提供する。
    /// </summary>
    public class AlertService : IAlertService
    {
        private readonly VisualElement alertBar;
        private readonly Label alertMessage;
        private readonly Button alertClose;

        public bool IsAlertVisible =>
            alertBar != null && alertBar.ClassListContains("visible");

        public Rect AlertWorldBound =>
            alertBar != null ? alertBar.worldBound : Rect.zero;

        public AlertService(VisualElement root)
        {
            alertBar = root.Q<VisualElement>("alertBar");
            alertMessage = root.Q<Label>("alertMessage");
            alertClose = root.Q<Button>("alertClose");

            if (alertClose != null)
            {
                alertClose.RegisterCallback<ClickEvent>(evt => HideAlert());
            }
        }

        public void ShowInfo(string code, string message, float autoDismissSeconds = 3f)
        {
            if (alertBar == null || alertMessage == null)
            {
                Debug.LogWarning("[AlertService] AlertBar elements not found");
                return;
            }

            alertMessage.text = $"{code}:{message}";

            alertBar.RemoveFromClassList("warning");
            alertBar.RemoveFromClassList("error");
            alertBar.RemoveFromClassList("info");
            alertBar.AddToClassList("info");

            ShowFadeIn();

            Debug.Log($"[AlertService] Info: {code}:{message}");

            if (autoDismissSeconds > 0)
            {
                alertBar.schedule.Execute(() => HideAlert())
                    .StartingIn((long)(autoDismissSeconds * 1000));
            }
        }

        public void ShowWarning(string code, string message, float autoDismissSeconds = 5f)
        {
            ShowAlertInternal(code, message, false, autoDismissSeconds);
        }

        public void ShowError(string code, string message, float autoDismissSeconds = 0f)
        {
            ShowAlertInternal(code, message, true, autoDismissSeconds);
        }

        public void HideAlert()
        {
            if (alertBar == null) return;

            alertBar.RemoveFromClassList("visible");
            alertBar.style.opacity = 0;

            alertBar.schedule.Execute(() =>
            {
                alertBar.style.display = DisplayStyle.None;
            }).StartingIn(300);
        }

        private void ShowAlertInternal(string code, string message, bool isError, float autoDismissSeconds)
        {
            if (alertBar == null || alertMessage == null)
            {
                Debug.LogWarning("[AlertService] AlertBar elements not found");
                return;
            }

            alertMessage.text = $"[{code}] {message}";

            alertBar.RemoveFromClassList("warning");
            alertBar.RemoveFromClassList("error");
            alertBar.AddToClassList(isError ? "error" : "warning");

            ShowFadeIn();

            Debug.Log($"[AlertService] Alert: [{code}] {message} (isError: {isError})");

            TapticEngine.Impact(isError ? TapticEngine.ImpactStyle.Heavy : TapticEngine.ImpactStyle.Medium);

            if (autoDismissSeconds > 0)
            {
                alertBar.schedule.Execute(() => HideAlert())
                    .StartingIn((long)(autoDismissSeconds * 1000));
            }
        }

        private void ShowFadeIn()
        {
            alertBar.style.display = DisplayStyle.Flex;
            alertBar.style.opacity = 0;

            alertBar.schedule.Execute(() =>
            {
                alertBar.AddToClassList("visible");
                alertBar.style.opacity = 1;
            }).StartingIn(10);
        }
    }
}
