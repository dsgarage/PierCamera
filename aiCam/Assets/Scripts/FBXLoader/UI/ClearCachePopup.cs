using System;
using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using AICam.AvatarCache;

namespace AICam.FBXLoader
{
    /// <summary>
    /// キャッシュクリア確認ポップアップ（UIToolkit版）
    /// 空のスロット長押しでキャッシュクリアポップアップを表示
    /// </summary>
    public class ClearCachePopup : MonoBehaviour
    {
        public static ClearCachePopup Instance { get; private set; }

        // UIToolkit要素
        private VisualElement overlay;
        private VisualElement panel;
        private Label titleLabel;
        private Label messageLabel;
        private VisualElement loadingIndicator;
        private VisualElement buttonContainer;
        private Button confirmButton;
        private Button cancelButton;

        // コールバック
        private Action<bool> onComplete;
        private bool isInitialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            InitializeUI();
        }

        /// <summary>
        /// UIToolkit要素を初期化（遅延初期化対応）
        /// </summary>
        private void InitializeUI()
        {
            InitializeUIWithRoot(null);
        }

        /// <summary>
        /// 指定されたrootVisualElementでUIToolkit要素を初期化
        /// </summary>
        public void InitializeUIWithRoot(VisualElement rootElement)
        {
            if (isInitialized) return;

            VisualElement root = rootElement;

            // rootが指定されていない場合は自動検索
            if (root == null)
            {
                var uiDocument = FindFirstObjectByType<UIDocument>();
                if (uiDocument == null)
                {
                    Debug.LogWarning("[ClearCachePopup] UIDocument not found in scene");
                    return;
                }

                root = uiDocument.rootVisualElement;
                if (root == null)
                {
                    Debug.LogWarning("[ClearCachePopup] Root visual element is null");
                    return;
                }
            }

            // 要素を取得
            overlay = root.Q<VisualElement>("clearCachePopupOverlay");
            if (overlay == null)
            {
                Debug.LogWarning("[ClearCachePopup] clearCachePopupOverlay not found in UXML");
                return;
            }

            panel = root.Q<VisualElement>("clearCachePopupPanel");
            titleLabel = root.Q<Label>("clearCachePopupTitle");
            messageLabel = root.Q<Label>("clearCachePopupMessage");
            loadingIndicator = root.Q<VisualElement>("clearCacheLoadingIndicator");
            buttonContainer = root.Q<VisualElement>("clearCacheButtonContainer");
            confirmButton = root.Q<Button>("clearCacheConfirmButton");
            cancelButton = root.Q<Button>("clearCacheCancelButton");

            // ボタンイベント設定
            if (confirmButton != null)
            {
                confirmButton.clicked += OnConfirmClicked;
            }
            if (cancelButton != null)
            {
                cancelButton.clicked += OnCancelClicked;
            }

            // オーバーレイクリックでキャンセル（パネル外クリック）
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == overlay)
                {
                    OnCancelClicked();
                }
            });

            // 初期状態は非表示
            overlay.RemoveFromClassList("visible");

            isInitialized = true;
            Debug.Log("[ClearCachePopup] UI initialized");
        }

        /// <summary>
        /// キャッシュクリアポップアップを表示
        /// </summary>
        public void Show(Action<bool> onCompleteCallback = null)
        {
            Show(null, onCompleteCallback);
        }

        /// <summary>
        /// キャッシュクリアポップアップを表示（rootVisualElement指定版）
        /// </summary>
        public void Show(VisualElement rootElement, Action<bool> onCompleteCallback = null)
        {
            // 遅延初期化
            if (!isInitialized)
            {
                InitializeUIWithRoot(rootElement);
            }

            if (!isInitialized || overlay == null)
            {
                Debug.LogError("[ClearCachePopup] UI not initialized, cannot show popup");
                onCompleteCallback?.Invoke(false);
                return;
            }

            onComplete = onCompleteCallback;

            // テキスト更新
            if (titleLabel != null)
            {
                titleLabel.text = "キャッシュをクリア";
            }
            if (messageLabel != null)
            {
                messageLabel.text = "すべてのアバターキャッシュを削除しますか？\n\nこの操作は取り消せません。";
            }

            // ローディング非表示
            if (loadingIndicator != null)
            {
                loadingIndicator.RemoveFromClassList("visible");
            }

            // ボタン有効化
            SetButtonsEnabled(true);

            // ボタンコンテナ表示
            if (buttonContainer != null)
            {
                buttonContainer.style.display = DisplayStyle.Flex;
            }

            // ポップアップ表示
            if (overlay != null)
            {
                overlay.AddToClassList("visible");
            }

            Debug.Log("[ClearCachePopup] Showing clear cache popup");
        }

        /// <summary>
        /// ポップアップを非表示
        /// </summary>
        public void Hide()
        {
            overlay?.RemoveFromClassList("visible");
            onComplete = null;
        }

        /// <summary>
        /// 初期化済みかどうか
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// 確認ボタンクリック
        /// </summary>
        private void OnConfirmClicked()
        {
            // キャッシュクリア実行
            ExecuteClearCacheAsync().Forget();
        }

        /// <summary>
        /// キャッシュクリア処理を非同期で実行
        /// </summary>
        private async UniTaskVoid ExecuteClearCacheAsync()
        {
            // ローディング表示
            SetButtonsEnabled(false);
            if (loadingIndicator != null)
            {
                loadingIndicator.AddToClassList("visible");
            }
            if (buttonContainer != null)
            {
                buttonContainer.style.display = DisplayStyle.None;
            }
            if (messageLabel != null)
            {
                messageLabel.text = "キャッシュをクリア中...";
            }

            try
            {
                // キャッシュクリア実行
                await ClearAllCacheAsync();

                Debug.Log("[ClearCachePopup] Cache cleared successfully");
                AlertBarController.ShowInfo("キャッシュをクリアしました");
                onComplete?.Invoke(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ClearCachePopup] Clear cache error: {e.Message}");
                AlertBarController.ErrorVrmLoadFailed($"キャッシュクリアエラー: {e.Message}");
                onComplete?.Invoke(false);
            }
            finally
            {
                Hide();
            }
        }

        /// <summary>
        /// キャンセルボタンクリック
        /// </summary>
        private void OnCancelClicked()
        {
            Debug.Log("[ClearCachePopup] Clear cache cancelled");
            onComplete?.Invoke(false);
            Hide();
        }

        /// <summary>
        /// すべてのキャッシュをクリア
        /// </summary>
        private async UniTask ClearAllCacheAsync()
        {
            // AvatarSlotManagerのキャッシュをクリア
            var slotManager = AvatarSlotManager.Instance;
            if (slotManager != null)
            {
                // 全スロットをクリア
                var cache = slotManager.Cache;
                if (cache != null)
                {
                    for (int i = 0; i < cache.maxSlots; i++)
                    {
                        slotManager.ClearSlot(i);
                    }
                }
            }

            // メモリキャッシュをクリア
            var memoryCache = AvatarMemoryCache.Instance;
            if (memoryCache != null)
            {
                memoryCache.ClearAll();
            }

            // バイナリキャッシュディレクトリをクリア
            string binaryCachePath = System.IO.Path.Combine(Application.persistentDataPath, "AvatarBinaryCache");
            if (System.IO.Directory.Exists(binaryCachePath))
            {
                try
                {
                    System.IO.Directory.Delete(binaryCachePath, true);
                    Debug.Log($"[ClearCachePopup] Deleted binary cache directory: {binaryCachePath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ClearCachePopup] Failed to delete binary cache directory: {e.Message}");
                }
            }

            // アイコンキャッシュディレクトリをクリア
            string iconCachePath = System.IO.Path.Combine(Application.persistentDataPath, "AvatarSlots");
            if (System.IO.Directory.Exists(iconCachePath))
            {
                try
                {
                    // アイコンファイルのみ削除（JSONは残す）
                    var iconFiles = System.IO.Directory.GetFiles(iconCachePath, "*.png");
                    foreach (var file in iconFiles)
                    {
                        System.IO.File.Delete(file);
                    }
                    Debug.Log($"[ClearCachePopup] Deleted {iconFiles.Length} icon files");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[ClearCachePopup] Failed to delete icon files: {e.Message}");
                }
            }

            // 少し待機して処理の完了を確認
            await UniTask.Delay(100);
        }

        /// <summary>
        /// ボタンの有効/無効を設定
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            if (confirmButton != null)
            {
                confirmButton.SetEnabled(enabled);
            }
            if (cancelButton != null)
            {
                cancelButton.SetEnabled(enabled);
            }
        }

        private void OnDestroy()
        {
            // イベント解除
            if (confirmButton != null)
            {
                confirmButton.clicked -= OnConfirmClicked;
            }
            if (cancelButton != null)
            {
                cancelButton.clicked -= OnCancelClicked;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
