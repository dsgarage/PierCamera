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

        private Button btnOpen, btnExtract, btnLoad;
        private VisualElement loadingPanel;
        private TextField logField;
        private ProgressBar progressBar;
        private Label loadingLabel;
        private Label statusLabel;

        private FileBrowserController fileBrowser;
        private RuntimeFBXLoaderBridge loaderBridge;

        void Awake()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            var root = uiDocument.rootVisualElement;

            // UI要素を取得
            btnOpen = root.Q<Button>("BtnOpen");
            btnExtract = root.Q<Button>("BtnExtract");
            btnLoad = root.Q<Button>("BtnLoad");
            statusLabel = root.Q<Label>("StatusLabel");
            loadingPanel = root.Q<VisualElement>("LoadingPanel");
            loadingLabel = root.Q<Label>("LoadingLabel");
            progressBar = root.Q<ProgressBar>("ProgressBar");
            logField = root.Q<TextField>("LogField");

            // 他のコンポーネントを検索
            fileBrowser = FindFirstObjectByType<FileBrowserController>();
            loaderBridge = FindFirstObjectByType<RuntimeFBXLoaderBridge>();

            // ボタンイベント登録
            btnOpen.clicked += OnOpenClicked;
            btnExtract.clicked += OnExtractClicked;
            btnLoad.clicked += OnLoadClicked;

            // 初期状態
            UpdateStatus("準備完了");
            btnExtract.SetEnabled(false);
            btnLoad.SetEnabled(false);

            AppendLog("システム初期化完了");
        }

        void OnOpenClicked()
        {
            AppendLog("ファイルピッカーを起動");
            UpdateStatus("ファイル選択中...");

            if (fileBrowser != null)
            {
                fileBrowser.OpenFilePicker(OnFileSelected);
            }
            else
            {
                AppendLog("エラー: FileBrowserControllerが見つかりません");
                UpdateStatus("エラー");
            }
        }

        void OnFileSelected(bool success, string path)
        {
            if (success)
            {
                AppendLog($"選択: {System.IO.Path.GetFileName(path)}");

                // ZIPファイルの場合は解凍ボタンを有効化
                bool isZip = path.ToLower().EndsWith(".zip");
                btnExtract.SetEnabled(isZip);

                // VRM/FBXファイルの場合はロードボタンを有効化
                bool isModelFile = path.ToLower().EndsWith(".vrm") || path.ToLower().EndsWith(".fbx");
                btnLoad.SetEnabled(isModelFile);

                if (isZip)
                {
                    UpdateStatus("ZIPファイル選択済み");
                    AppendLog("ZIPファイルを検出。「解凍」をタップしてください");
                }
                else if (isModelFile)
                {
                    UpdateStatus("ファイル選択済み");
                }
            }
            else
            {
                AppendLog("選択をキャンセル");
                UpdateStatus("準備完了");
                btnExtract.SetEnabled(false);
                btnLoad.SetEnabled(false);
            }
        }

        void OnExtractClicked()
        {
            AppendLog("ZIPパッケージを解凍中...");
            UpdateStatus("解凍中...", showProgress: true);
            btnOpen.SetEnabled(false);
            btnExtract.SetEnabled(false);
            btnLoad.SetEnabled(false);

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
            UpdateStatus("準備完了", showProgress: false);
            btnOpen.SetEnabled(true);

            if (success && !string.IsNullOrEmpty(extractedFilePath))
            {
                AppendLog($"解凍完了: {System.IO.Path.GetFileName(extractedFilePath)}");
                btnExtract.SetEnabled(false);
                btnLoad.SetEnabled(true);
                UpdateStatus("解凍完了 - ロード可能");
            }
            else
            {
                AppendLog("解凍失敗");
                btnExtract.SetEnabled(true);
                UpdateStatus("解凍失敗");
            }
        }

        void OnLoadClicked()
        {
            AppendLog("モデルをロード中...");
            UpdateStatus("ロード中...", showProgress: true);
            btnOpen.SetEnabled(false);
            btnExtract.SetEnabled(false);
            btnLoad.SetEnabled(false);

            if (loaderBridge != null)
            {
                loaderBridge.StartRuntimeLoad(OnProgress, OnComplete);
            }
            else
            {
                AppendLog("エラー: RuntimeFBXLoaderBridgeが見つかりません");
                OnComplete(false);
            }
        }

        void OnProgress(float percent)
        {
            progressBar.value = percent;
            loadingLabel.text = $"{percent:F0}%";
        }

        void OnComplete(bool success)
        {
            UpdateStatus("準備完了", showProgress: false);
            btnOpen.SetEnabled(true);
            btnExtract.SetEnabled(false);
            btnLoad.SetEnabled(false);

            if (success)
            {
                AppendLog("モデルのロードに成功");
                UpdateStatus("ロード完了");
            }
            else
            {
                AppendLog("ロード失敗");
                UpdateStatus("ロード失敗");
            }
        }

        void AppendLog(string message)
        {
            string timestamp = System.DateTime.Now.ToString("HH:mm:ss");
            logField.value += $"[{timestamp}] {message}\n";

            // ログを最下部にスクロール
            logField.schedule.Execute(() =>
            {
                var scrollView = logField.Q<ScrollView>();
                if (scrollView != null)
                {
                    scrollView.scrollOffset = new Vector2(0, float.MaxValue);
                }
            }).StartingIn(10);
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
            statusLabel.text = status;

            if (showProgress)
            {
                loadingPanel.style.display = DisplayStyle.Flex;
            }
            else
            {
                loadingPanel.style.display = DisplayStyle.None;
                progressBar.value = 0;
                loadingLabel.text = "";
            }
        }
    }
}
