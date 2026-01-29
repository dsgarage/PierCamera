using System;
using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using AICam.AvatarCache;
using AICam.AvatarCache.IO;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバターキャッシュエクスポート確認ポップアップ（UIToolkit版）
    /// Issue #458: ダブルタップでエクスポートポップアップを表示
    /// </summary>
    public class ExportPopup : MonoBehaviour
    {
        public static ExportPopup Instance { get; private set; }

        [Header("Settings")]
        [SerializeField] private string exportDirectory = "AvatarExports";

        // UIToolkit要素
        private VisualElement overlay;
        private VisualElement panel;
        private Label titleLabel;
        private Label messageLabel;
        private VisualElement loadingIndicator;
        private VisualElement buttonContainer;
        private Button exportButton;
        private Button cancelButton;

        // スロットデータ
        private int currentSlotIndex = -1;
        private AvatarSlotData currentSlotData;
        private Action<bool, string> onExportComplete;
        private bool isInitialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // 重複インスタンスの場合、コンポーネントのみ削除（gameObjectは他のコンポーネントが使用している可能性）
                Destroy(this);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            // Start で初期化（UIDocument が準備完了している可能性が高い）
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
        /// <param name="rootElement">rootVisualElement（nullの場合は自動検索）</param>
        public void InitializeUIWithRoot(VisualElement rootElement)
        {
            if (isInitialized) return;

            VisualElement root = rootElement;

            // rootが指定されていない場合は自動検索
            if (root == null)
            {
                // シーン内のUIDocumentを検索
                var uiDocument = FindFirstObjectByType<UIDocument>();
                if (uiDocument == null)
                {
                    Debug.LogWarning("[ExportPopup] UIDocument not found in scene");
                    return;
                }

                root = uiDocument.rootVisualElement;
                if (root == null)
                {
                    Debug.LogWarning("[ExportPopup] Root visual element is null");
                    return;
                }
            }

            // 要素を取得
            overlay = root.Q<VisualElement>("exportPopupOverlay");
            if (overlay == null)
            {
                Debug.LogWarning("[ExportPopup] exportPopupOverlay not found in UXML");
                return;
            }

            panel = root.Q<VisualElement>("exportPopupPanel");
            titleLabel = root.Q<Label>("exportPopupTitle");
            messageLabel = root.Q<Label>("exportPopupMessage");
            loadingIndicator = root.Q<VisualElement>("exportLoadingIndicator");
            buttonContainer = root.Q<VisualElement>("exportButtonContainer");
            exportButton = root.Q<Button>("exportConfirmButton");
            cancelButton = root.Q<Button>("exportCancelButton");

            // ボタンイベント設定
            if (exportButton != null)
            {
                exportButton.clicked += OnExportClicked;
            }
            if (cancelButton != null)
            {
                cancelButton.clicked += OnCancelClicked;
            }

            // オーバーレイクリックでキャンセル（パネル外クリック）
            overlay.RegisterCallback<ClickEvent>(evt =>
            {
                // パネル内のクリックは無視
                if (evt.target == overlay)
                {
                    OnCancelClicked();
                }
            });

            // 初期状態は非表示
            overlay.RemoveFromClassList("visible");

            isInitialized = true;
            Debug.Log("[ExportPopup] UI initialized (UIToolkit)");
        }

        /// <summary>
        /// エクスポートポップアップを表示
        /// </summary>
        public void Show(int slotIndex, AvatarSlotData slotData, Action<bool, string> onComplete = null)
        {
            Show(slotIndex, slotData, null, onComplete);
        }

        /// <summary>
        /// エクスポートポップアップを表示（rootVisualElement指定版）
        /// </summary>
        /// <param name="slotIndex">スロットインデックス</param>
        /// <param name="slotData">スロットデータ</param>
        /// <param name="rootElement">rootVisualElement（nullの場合は自動検索）</param>
        /// <param name="onComplete">完了コールバック</param>
        public void Show(int slotIndex, AvatarSlotData slotData, VisualElement rootElement, Action<bool, string> onComplete = null)
        {
            // 遅延初期化（rootElementを渡す）
            if (!isInitialized)
            {
                InitializeUIWithRoot(rootElement);
            }

            if (!isInitialized || overlay == null)
            {
                Debug.LogError("[ExportPopup] UI not initialized, cannot show popup");
                onComplete?.Invoke(false, null);
                return;
            }

            if (slotData == null || !slotData.IsConfigured)
            {
                Debug.LogWarning("[ExportPopup] Cannot show popup for unconfigured slot");
                return;
            }

            currentSlotIndex = slotIndex;
            currentSlotData = slotData;
            onExportComplete = onComplete;

            // テキスト更新
            if (titleLabel != null)
            {
                titleLabel.text = "アバターをエクスポート";
            }
            if (messageLabel != null)
            {
                string avatarName = !string.IsNullOrEmpty(slotData.avatarName)
                    ? slotData.avatarName
                    : $"スロット {slotIndex + 1}";
                messageLabel.text = $"「{avatarName}」を\n.avatarcache形式で保存しますか？";
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

            Debug.Log($"[ExportPopup] Showing export popup for slot {slotIndex}");
        }

        /// <summary>
        /// ポップアップを非表示
        /// </summary>
        public void Hide()
        {
            overlay?.RemoveFromClassList("visible");

            currentSlotIndex = -1;
            currentSlotData = null;
            onExportComplete = null;
        }

        /// <summary>
        /// 初期化済みかどうか
        /// </summary>
        public bool IsInitialized => isInitialized;

        /// <summary>
        /// エクスポートボタンクリック
        /// </summary>
        private void OnExportClicked()
        {
            if (currentSlotData == null || !currentSlotData.HasBinaryCache)
            {
                Debug.LogWarning("[ExportPopup] No binary cache available for export");
                AlertBarController.WarnManifestNotFound("バイナリキャッシュがありません。アバターを一度ロードしてください。");
                Hide();
                return;
            }

            // エクスポート実行（Fire-and-forget）
            ExecuteExportAsync().Forget();
        }

        /// <summary>
        /// エクスポート処理を非同期で実行
        /// </summary>
        private async UniTaskVoid ExecuteExportAsync()
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
                messageLabel.text = "エクスポート中...";
            }

            try
            {
                // エクスポート実行
                string exportPath = await ExportAvatarCacheAsync();

                if (!string.IsNullOrEmpty(exportPath))
                {
                    Debug.Log($"[ExportPopup] Export successful: {exportPath}");
                    AlertBarController.ShowInfo($"エクスポート完了: {System.IO.Path.GetFileName(exportPath)}");
                    onExportComplete?.Invoke(true, exportPath);
                }
                else
                {
                    Debug.LogError("[ExportPopup] Export failed");
                    AlertBarController.ErrorVrmLoadFailed("エクスポートに失敗しました");
                    onExportComplete?.Invoke(false, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[ExportPopup] Export error: {e.Message}");
                AlertBarController.ErrorVrmLoadFailed($"エクスポートエラー: {e.Message}");
                onExportComplete?.Invoke(false, null);
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
            Debug.Log("[ExportPopup] Export cancelled");
            onExportComplete?.Invoke(false, null);
            Hide();
        }

        /// <summary>
        /// アバターキャッシュをエクスポート
        /// </summary>
        private async UniTask<string> ExportAvatarCacheAsync()
        {
            if (currentSlotData == null || string.IsNullOrEmpty(currentSlotData.binaryCacheId))
            {
                return null;
            }

            // エクスポートディレクトリを作成
            string exportDir = System.IO.Path.Combine(Application.persistentDataPath, exportDirectory);
            if (!System.IO.Directory.Exists(exportDir))
            {
                System.IO.Directory.CreateDirectory(exportDir);
            }

            // ファイル名を生成（アバター名 + タイムスタンプ）
            string safeName = GetSafeFileName(currentSlotData.avatarName ?? $"avatar_{currentSlotIndex}");
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string fileName = $"{safeName}_{timestamp}.avatarcache";
            string exportPath = System.IO.Path.Combine(exportDir, fileName);

            // キャッシュルートパスを設定
            AvatarCacheExporter.SetCacheRootPath(Application.persistentDataPath);

            // エクスポート実行（難読化有効）
            AvatarCacheExporter.EnableObfuscation = true;
            await AvatarCacheExporter.ExportAsync(currentSlotData.binaryCacheId, exportPath);

            return exportPath;
        }

        /// <summary>
        /// ファイル名として安全な文字列に変換
        /// </summary>
        private string GetSafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "avatar";
            }

            // 無効な文字を置換
            char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            // 長すぎる場合は切り詰め
            if (name.Length > 50)
            {
                name = name.Substring(0, 50);
            }

            return name;
        }

        /// <summary>
        /// ボタンの有効/無効を設定
        /// </summary>
        private void SetButtonsEnabled(bool enabled)
        {
            if (exportButton != null)
            {
                exportButton.SetEnabled(enabled);
            }
            if (cancelButton != null)
            {
                cancelButton.SetEnabled(enabled);
            }
        }

        private void OnDestroy()
        {
            // イベント解除
            if (exportButton != null)
            {
                exportButton.clicked -= OnExportClicked;
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
