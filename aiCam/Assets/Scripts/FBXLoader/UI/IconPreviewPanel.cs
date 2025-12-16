using System;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アイコン撮影後のプレビュー表示パネル
    /// 画面サイズに合わせてアスペクト比を維持しながら最大サイズで表示
    /// </summary>
    public class IconPreviewPanel : MonoBehaviour
    {
        public static IconPreviewPanel Instance { get; private set; }

        [Header("UI References")]
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private RawImage previewImage;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button retakeButton;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Settings")]
        [SerializeField] private float padding = 100f;
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float fadeOutDuration = 0.2f;

        private Texture2D currentTexture;
        private Action onConfirm;
        private Action onRetake;
        private RectTransform previewRectTransform;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            if (previewImage != null)
            {
                previewRectTransform = previewImage.GetComponent<RectTransform>();
            }

            if (confirmButton != null)
            {
                confirmButton.onClick.AddListener(OnConfirmClicked);
            }

            if (retakeButton != null)
            {
                retakeButton.onClick.AddListener(OnRetakeClicked);
            }

            // 初期状態は非表示
            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }

            // テクスチャの解放
            CleanupTexture();
        }

        /// <summary>
        /// プレビューを表示
        /// </summary>
        /// <param name="texture">表示するテクスチャ</param>
        /// <param name="onConfirmCallback">確認ボタン押下時のコールバック</param>
        /// <param name="onRetakeCallback">再撮影ボタン押下時のコールバック（nullの場合はボタン非表示）</param>
        public async UniTask ShowPreview(Texture2D texture, Action onConfirmCallback, Action onRetakeCallback = null)
        {
            if (texture == null)
            {
                Debug.LogError("[IconPreviewPanel] Texture is null");
                return;
            }

            currentTexture = texture;
            onConfirm = onConfirmCallback;
            onRetake = onRetakeCallback;

            // プレビュー画像を設定
            if (previewImage != null)
            {
                previewImage.texture = texture;
                AdjustPreviewSize(texture.width, texture.height);
            }

            // 再撮影ボタンの表示/非表示
            if (retakeButton != null)
            {
                retakeButton.gameObject.SetActive(onRetakeCallback != null);
            }

            // パネルを表示
            if (panelRoot != null)
            {
                panelRoot.SetActive(true);
            }

            // フェードイン
            await FadeIn();

            Debug.Log($"[IconPreviewPanel] Showing preview: {texture.width}x{texture.height}");
        }

        /// <summary>
        /// ファイルからテクスチャを読み込んでプレビュー表示
        /// </summary>
        public async UniTask ShowPreviewFromFile(string filePath, Action onConfirmCallback, Action onRetakeCallback = null)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                Debug.LogError($"[IconPreviewPanel] File not found: {filePath}");
                return;
            }

            try
            {
                byte[] bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                Texture2D texture = new Texture2D(2, 2);

                if (texture.LoadImage(bytes))
                {
                    await ShowPreview(texture, onConfirmCallback, onRetakeCallback);
                }
                else
                {
                    Debug.LogError($"[IconPreviewPanel] Failed to load image: {filePath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[IconPreviewPanel] Error loading file: {e.Message}");
            }
        }

        /// <summary>
        /// プレビューを非表示
        /// </summary>
        public async UniTask HidePreview()
        {
            await FadeOut();

            if (panelRoot != null)
            {
                panelRoot.SetActive(false);
            }

            // テクスチャは解放しない（呼び出し元で管理）
            if (previewImage != null)
            {
                previewImage.texture = null;
            }

            onConfirm = null;
            onRetake = null;

            Debug.Log("[IconPreviewPanel] Preview hidden");
        }

        /// <summary>
        /// 画面サイズに合わせてプレビューサイズを調整
        /// アスペクト比を維持しながら最大サイズで表示
        /// </summary>
        private void AdjustPreviewSize(int textureWidth, int textureHeight)
        {
            if (previewRectTransform == null) return;

            // 画面サイズを取得
            float screenWidth = Screen.width - padding * 2;
            float screenHeight = Screen.height - padding * 2;

            // テクスチャのアスペクト比
            float textureAspect = (float)textureWidth / textureHeight;

            // 画面のアスペクト比
            float screenAspect = screenWidth / screenHeight;

            float newWidth, newHeight;

            if (textureAspect > screenAspect)
            {
                // 横長 - 幅を基準にスケール
                newWidth = screenWidth;
                newHeight = screenWidth / textureAspect;
            }
            else
            {
                // 縦長または正方形 - 高さを基準にスケール
                newHeight = screenHeight;
                newWidth = screenHeight * textureAspect;
            }

            previewRectTransform.sizeDelta = new Vector2(newWidth, newHeight);

            Debug.Log($"[IconPreviewPanel] Adjusted preview size: {newWidth}x{newHeight} (texture: {textureWidth}x{textureHeight})");
        }

        private void OnConfirmClicked()
        {
            Debug.Log("[IconPreviewPanel] Confirm clicked");

            var callback = onConfirm;
            _ = HidePreview();
            callback?.Invoke();
        }

        private void OnRetakeClicked()
        {
            Debug.Log("[IconPreviewPanel] Retake clicked");

            var callback = onRetake;
            _ = HidePreview();
            callback?.Invoke();
        }

        private async UniTask FadeIn()
        {
            if (canvasGroup == null) return;

            canvasGroup.alpha = 0f;
            float elapsed = 0f;

            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
                await UniTask.Yield();
            }

            canvasGroup.alpha = 1f;
        }

        private async UniTask FadeOut()
        {
            if (canvasGroup == null) return;

            float elapsed = 0f;

            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
                await UniTask.Yield();
            }

            canvasGroup.alpha = 0f;
        }

        private void CleanupTexture()
        {
            // 外部から渡されたテクスチャは解放しない
            currentTexture = null;
        }

        /// <summary>
        /// プレビューが表示中かどうか
        /// </summary>
        public bool IsShowing => panelRoot != null && panelRoot.activeSelf;
    }
}
