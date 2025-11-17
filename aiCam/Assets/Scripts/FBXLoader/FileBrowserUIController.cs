using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.FBXLoader
{
    /// <summary>
    /// UIToolkitベースのFBXローダーUIコントローラー
    /// </summary>
    public class FileBrowserUIController : MonoBehaviour
    {
        [SerializeField] private UIDocument uiDocument;

        private Button btnOpen, btnLoad;
        private VisualElement progressPanel;
        private TextField logField;
        private ProgressBar progressBar;
        private Label loadingLabel;

        private FileBrowserController fileBrowser;
        private RuntimeFBXLoaderBridge loaderBridge;

        private bool isModelLoaded = false;

        // FileBrowserUIController.cs（末尾の方に追記）
        private void OnEnable()
        {
            var root = uiDocument.rootVisualElement;
            root.RegisterCallback<GeometryChangedEvent>(_ => UpdateCompact(root));
            UpdateCompact(root);
        }

        private void UpdateCompact(VisualElement root)
        {
            // 閾値は好みで調整。800未満なら縦積み
            bool compact = root.worldBound.width < 800f;
            root.EnableInClassList("compact", compact);
        }

        
        void Awake()
        {
            Debug.Log("[FileBrowserUIController] Awake called");

            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            var root = uiDocument.rootVisualElement;

            // UI要素を取得
            btnOpen = root.Q<Button>("BtnOpen");
            btnLoad = root.Q<Button>("BtnLoad");
            progressPanel = root.Q<VisualElement>("ProgressPanel");
            loadingLabel = root.Q<Label>("LoadingLabel");
            progressBar = root.Q<ProgressBar>("ProgressBar");
            logField = root.Q<TextField>("LogField");

            Debug.Log($"[FileBrowserUIController] UI Elements - btnOpen: {btnOpen != null}, btnLoad: {btnLoad != null}, progressPanel: {progressPanel != null}");

            // 他のコンポーネントを検索
            fileBrowser = FindFirstObjectByType<FileBrowserController>();
            loaderBridge = FindFirstObjectByType<RuntimeFBXLoaderBridge>();

            Debug.Log($"[FileBrowserUIController] Components - fileBrowser: {fileBrowser != null}, loaderBridge: {loaderBridge != null}");

            // ボタンイベント登録
            btnOpen.clicked += OnOpenClicked;
            btnLoad.clicked += OnLoadOrDeleteClicked;

            // 初期状態
            progressPanel.style.display = DisplayStyle.None;
            UpdateStatus("待機中...");
            UpdateLoadButton(false);

            AppendLog("システム初期化完了");
            Debug.Log("[FileBrowserUIController] Initialization complete");
        }

        void OnOpenClicked()
        {
            Debug.Log("[FileBrowserUIController] OnOpenClicked called");
            AppendLog("ファイルピッカーを起動");

            if (fileBrowser != null)
            {
                Debug.Log("[FileBrowserUIController] Calling fileBrowser.OpenFilePicker with OnFileSelected callback");
                fileBrowser.OpenFilePicker(OnFileSelected);
            }
            else
            {
                Debug.LogError("[FileBrowserUIController] FileBrowserController is null!");
                AppendLog("エラー: FileBrowserControllerが見つかりません");
                UpdateStatus("エラー");
            }
        }

        void OnFileSelected(bool success, string path)
        {
            Debug.Log($"[FileBrowserUIController] OnFileSelected called - success: {success}, path: {path}");

            if (success)
            {
                // 既存のモデルがあれば削除
                if (isModelLoaded)
                {
                    Debug.Log("[FileBrowserUIController] Clearing existing model before loading new file");
                    AppendLog("既存のモデルを削除中...");
                    ClearCurrentModel();
                }

                AppendLog($"選択: {System.IO.Path.GetFileName(path)}");

                // ZIPファイルの場合は自動的に解凍
                bool isZip = path.ToLower().EndsWith(".zip");

                // VRM/FBXファイルの場合はロードボタンを有効化
                bool isModelFile = path.ToLower().EndsWith(".vrm") || path.ToLower().EndsWith(".fbx");

                Debug.Log($"[FileBrowserUIController] File type - isZip: {isZip}, isModelFile: {isModelFile}");

                if (isZip)
                {
                    Debug.Log("[FileBrowserUIController] ZIP file detected, starting auto extract");
                    UpdateStatus("ZIPファイル検出 - 自動解凍中...");
                    AppendLog("ZIPファイルを検出。自動的に解凍します");
                    AutoExtract();
                }
                else if (isModelFile)
                {
                    Debug.Log("[FileBrowserUIController] Model file detected, enabling Load button");
                    UpdateStatus("ファイル選択済み");
                    UpdateLoadButton(true);
                }
            }
            else
            {
                Debug.Log("[FileBrowserUIController] File selection cancelled");
                AppendLog("選択をキャンセル");
                UpdateStatus("待機中...");
                UpdateLoadButton(false);
            }
        }

        void AutoExtract()
        {
            btnOpen.SetEnabled(false);
            UpdateLoadButton(false);

            if (fileBrowser != null)
            {
                fileBrowser.ExtractZipPackage(OnExtractComplete);
            }
            else
            {
                AppendLog("エラー: FileBrowserControllerが見つかりません");
                OnExtractComplete(false, null);
            }
        }

        void OnExtractComplete(bool success, string extractedFilePath)
        {
            btnOpen.SetEnabled(true);

            if (success && !string.IsNullOrEmpty(extractedFilePath))
            {
                AppendLog($"解凍完了: {System.IO.Path.GetFileName(extractedFilePath)}");
                UpdateLoadButton(true);
                UpdateStatus("解凍完了 - ロード可能");
            }
            else
            {
                AppendLog("解凍失敗");
                UpdateStatus("解凍失敗");
            }
        }

        void OnLoadOrDeleteClicked()
        {
            if (isModelLoaded)
            {
                // 削除モード
                Debug.Log("[FileBrowserUIController] Delete button clicked");
                AppendLog("モデルを削除中...");
                ClearCurrentModel();
                UpdateStatus("待機中...");
                AppendLog("モデルを削除しました");
            }
            else
            {
                // ロードモード
                Debug.Log("[FileBrowserUIController] Load button clicked");
                AppendLog("モデルをロード中...");
                UpdateStatus("ロード中...", showProgress: true);
                btnOpen.SetEnabled(false);
                UpdateLoadButton(false);

                if (loaderBridge != null)
                {
                    Debug.Log("[FileBrowserUIController] Calling loaderBridge.StartRuntimeLoad");
                    loaderBridge.StartRuntimeLoad(OnProgress, OnComplete);
                }
                else
                {
                    Debug.LogError("[FileBrowserUIController] RuntimeFBXLoaderBridge is null!");
                    AppendLog("エラー: RuntimeFBXLoaderBridgeが見つかりません");
                    OnComplete(false);
                }
            }
        }

        void OnProgress(float percent)
        {
            progressBar.value = percent;
            loadingLabel.text = $"ロード中... {percent:F0}%";
        }

        void OnComplete(bool success)
        {
            UpdateStatus("待機中...", showProgress: false);
            btnOpen.SetEnabled(true);

            if (success)
            {
                AppendLog("モデルのロードに成功");
                UpdateStatus("ロード完了");
                isModelLoaded = true;
                UpdateLoadButton(true, isDeleteMode: true);
            }
            else
            {
                AppendLog("ロード失敗");
                UpdateStatus("ロード失敗");
                isModelLoaded = false;
                UpdateLoadButton(false);
            }
        }

        void AppendLog(string message)
        {
            string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            logField.value += $"[{timestamp}] {message}\n";

            // ログを最下部にスクロール
            var root = uiDocument.rootVisualElement;
            var scrollView = root.Q<ScrollView>("LogScroll");
            if (scrollView != null)
            {
                scrollView.schedule.Execute(() =>
                {
                    scrollView.scrollOffset = new Vector2(0, float.MaxValue);
                }).StartingIn(10);
            }
        }

        /// <summary>
        /// 外部からログを追加するためのパブリックメソッド
        /// </summary>
        public void AddLog(string message)
        {
            AppendLog(message);
        }

        /// <summary>
        /// ログをクリア
        /// </summary>
        public void ClearLog()
        {
            logField.value = "";
        }

        /// <summary>
        /// ステータス表示を更新
        /// </summary>
        private void UpdateStatus(string status, bool showProgress = false)
        {
            loadingLabel.text = status;

            if (showProgress)
            {
                progressPanel.style.display = DisplayStyle.Flex;
            }
            else
            {
                progressPanel.style.display = DisplayStyle.None;
                progressBar.value = 0;
            }
        }

        /// <summary>
        /// ロード/削除ボタンの状態を更新
        /// </summary>
        private void UpdateLoadButton(bool enabled, bool isDeleteMode = false)
        {
            if (isDeleteMode)
            {
                btnLoad.text = "削除";
                btnLoad.SetEnabled(true);
                btnLoad.RemoveFromClassList("success");
                btnLoad.AddToClassList("danger");
                // 赤色に変更
                btnLoad.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(0.863f, 0.208f, 0.271f)); // rgb(220, 53, 69)
            }
            else
            {
                btnLoad.text = "ロード開始";
                btnLoad.SetEnabled(enabled);
                btnLoad.RemoveFromClassList("danger");
                btnLoad.AddToClassList("success");
                // 緑色に戻す
                btnLoad.style.backgroundColor = new UnityEngine.UIElements.StyleColor(new UnityEngine.Color(0.227f, 0.647f, 0.365f)); // rgb(58, 165, 93)
            }
        }

        /// <summary>
        /// 現在のモデルをクリア
        /// </summary>
        private void ClearCurrentModel()
        {
            if (loaderBridge != null)
            {
                Debug.Log("[FileBrowserUIController] Clearing current model via loaderBridge");
                loaderBridge.ClearCurrentModel();
                isModelLoaded = false;
                UpdateLoadButton(false);
            }
            else
            {
                Debug.LogWarning("[FileBrowserUIController] Cannot clear model - loaderBridge is null");
            }
        }
    }
}
