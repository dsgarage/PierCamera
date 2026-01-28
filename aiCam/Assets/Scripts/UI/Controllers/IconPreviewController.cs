using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// アイコンプレビューパネルの表示・非表示を管理するコントローラー。
    /// </summary>
    public class IconPreviewController
    {
        private readonly VisualElement iconPreviewPanel;
        private readonly VisualElement iconPreviewImage;
        private readonly Button iconPreviewRetake;
        private readonly Button iconPreviewConfirm;

        private System.Action onConfirmCallback;
        private System.Action onRetakeCallback;

        /// <summary>
        /// プレビューが表示中（display: flex）かどうか。
        /// </summary>
        public bool IsShowing => iconPreviewPanel != null &&
            iconPreviewPanel.resolvedStyle.display == DisplayStyle.Flex;

        /// <summary>
        /// タッチブロッキング判定用。visible クラスを持つかどうか。
        /// </summary>
        public bool IsVisible => iconPreviewPanel != null &&
            iconPreviewPanel.ClassListContains("visible");

        public IconPreviewController(VisualElement root)
        {
            iconPreviewPanel = root.Q<VisualElement>("iconPreviewPanel");
            iconPreviewImage = root.Q<VisualElement>("iconPreviewImage");
            iconPreviewRetake = root.Q<Button>("iconPreviewRetake");
            iconPreviewConfirm = root.Q<Button>("iconPreviewConfirm");

            if (iconPreviewConfirm != null)
            {
                iconPreviewConfirm.RegisterCallback<ClickEvent>(evt => OnConfirmClicked());
            }

            if (iconPreviewRetake != null)
            {
                iconPreviewRetake.RegisterCallback<ClickEvent>(evt => OnRetakeClicked());
            }
        }

        public void Show(Texture2D texture, System.Action onConfirm, System.Action onRetake = null)
        {
            if (iconPreviewPanel == null || iconPreviewImage == null)
            {
                Debug.LogWarning("⚠️ IconPreviewPanel elements not found");
                return;
            }

            onConfirmCallback = onConfirm;
            onRetakeCallback = onRetake;

            iconPreviewImage.style.backgroundImage = new StyleBackground(texture);

            if (iconPreviewRetake != null)
            {
                iconPreviewRetake.style.display = onRetake != null ? DisplayStyle.Flex : DisplayStyle.None;
            }

            iconPreviewPanel.style.display = DisplayStyle.Flex;
            iconPreviewPanel.style.opacity = 0;

            iconPreviewPanel.schedule.Execute(() =>
            {
                iconPreviewPanel.AddToClassList("visible");
                iconPreviewPanel.style.opacity = 1;
            }).StartingIn(10);

            Debug.Log($"🖼 IconPreview shown: {texture.width}x{texture.height}");
        }

        public void Hide()
        {
            if (iconPreviewPanel == null) return;

            iconPreviewPanel.RemoveFromClassList("visible");
            iconPreviewPanel.style.opacity = 0;

            iconPreviewPanel.schedule.Execute(() =>
            {
                iconPreviewPanel.style.display = DisplayStyle.None;
                iconPreviewImage.style.backgroundImage = null;
            }).StartingIn(300);

            onConfirmCallback = null;
            onRetakeCallback = null;

            Debug.Log("✅ IconPreview hidden");
        }

        private void OnConfirmClicked()
        {
            Debug.Log("✅ IconPreview confirm clicked");
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);

            var callback = onConfirmCallback;
            Hide();
            callback?.Invoke();
        }

        private void OnRetakeClicked()
        {
            Debug.Log("🔄 IconPreview retake clicked");
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);

            var callback = onRetakeCallback;
            Hide();
            callback?.Invoke();
        }
    }
}
