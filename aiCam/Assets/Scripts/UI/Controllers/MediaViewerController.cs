using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// メディアビューア（viewerOverlay / viewerImage）の表示・非表示を管理するコントローラー。
    ///
    /// ## v0.8.0 変更履歴
    /// - Issue #476: ビューアクローズ時のコールバックを追加
    /// </summary>
    public class MediaViewerController
    {
        private readonly VisualElement viewerOverlay;
        private readonly Image viewerImage;
        private readonly System.Action onClosed;  // Issue #476

        /// <summary>
        /// ビューアが表示中かどうか。
        /// </summary>
        public bool IsViewerVisible => viewerOverlay != null &&
            viewerOverlay.resolvedStyle.display == DisplayStyle.Flex;

        public MediaViewerController(VisualElement root, System.Action onClosed = null)
        {
            this.onClosed = onClosed;
            viewerOverlay = root.Q<VisualElement>("viewerOverlay");
            viewerImage = root.Q<Image>("viewerImage");

            if (viewerOverlay != null)
            {
                viewerOverlay.RegisterCallback<ClickEvent>(evt => CloseViewer());
            }
        }

        /// <summary>
        /// ビューアを開く。
        /// </summary>
        /// <param name="photo">表示する写真（nullの場合は警告）</param>
        /// <param name="isVideo">動画モードかどうか</param>
        public void OpenViewer(Texture2D photo, bool isVideo)
        {
            Debug.Log("🖼 OpenViewer called");

            if (viewerOverlay == null)
            {
                Debug.LogWarning("⚠️ viewerOverlay is null");
                return;
            }

            viewerOverlay.style.display = DisplayStyle.Flex;
            Debug.Log("✅ Viewer opened");

            if (isVideo)
            {
                Debug.Log("📹 Video mode (not implemented)");
                if (viewerImage != null)
                {
                    viewerImage.style.display = DisplayStyle.None;
                }
            }
            else
            {
                if (viewerImage != null && photo != null)
                {
                    viewerImage.style.display = DisplayStyle.Flex;
                    viewerImage.image = photo;
                    Debug.Log("✅ Photo displayed in viewer");
                }
                else
                {
                    Debug.LogWarning("⚠️ No photo to display");
                }
            }
        }

        /// <summary>
        /// ビューアを閉じる。
        /// Issue #476: クローズ後の入力ブロック用コールバックを呼び出し
        /// </summary>
        public void CloseViewer()
        {
            Debug.Log("✋ CloseViewer called");

            if (viewerOverlay != null)
            {
                viewerOverlay.style.display = DisplayStyle.None;
                Debug.Log("✅ Viewer closed");

                // Issue #476: クローズを通知
                onClosed?.Invoke();
            }
            else
            {
                Debug.LogWarning("⚠️ viewerOverlay is null");
            }
        }
    }
}
