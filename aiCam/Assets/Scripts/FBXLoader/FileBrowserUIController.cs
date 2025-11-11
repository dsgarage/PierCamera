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

        private Button btnOpen, btnLoad, btnExtract;
        private Button btnDeleteAvatar, btnCancelPopup;
        private VisualElement progressPanel;
        private VisualElement popupOverlay;
        private TextField logField;
        private ProgressBar progressBar;
        private Label loadingLabel;

        private FileBrowserController fileBrowser;
        private RuntimeFBXLoaderBridge loaderBridge;

        // アバターのロード状態
        private bool isAvatarLoaded = false;

        // 長押し検出用（選択ボタン）
        private float openPointerDownTime;
        private bool isOpenPointerDown;

        private const float LONG_PRESS_DURATION = 0.8f;

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
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            var root = uiDocument.rootVisualElement;

            // UI要素を取得
            btnOpen = root.Q<Button>("BtnOpen");
            btnLoad = root.Q<Button>("BtnLoad");
            btnExtract = root.Q<Button>("BtnExtract");
            btnDeleteAvatar = root.Q<Button>("BtnDeleteAvatar");
            btnCancelPopup = root.Q<Button>("BtnCancelPopup");
            progressPanel = root.Q<VisualElement>("ProgressPanel");
            popupOverlay = root.Q<VisualElement>("PopupOverlay");
            loadingLabel = root.Q<Label>("LoadingLabel");
            progressBar = root.Q<ProgressBar>("ProgressBar");
            logField = root.Q<TextField>("LogField");

            // 他のコンポーネントを検索
            fileBrowser = FindFirstObjectByType<FileBrowserController>();
            loaderBridge = FindFirstObjectByType<RuntimeFBXLoaderBridge>();

            // ボタンイベント登録
            btnOpen.clicked += OnOpenClicked;
            btnLoad.clicked += OnLoadOrDeleteClicked;
            btnExtract.clicked += OnExtractClicked;
            btnDeleteAvatar.clicked += OnDeleteAvatarClicked;
            btnCancelPopup.clicked += OnCancelPopupClicked;

            // 長押しイベント登録（選択ボタン）
            btnOpen.RegisterCallback<PointerDownEvent>(OnOpenPointerDown);
            btnOpen.RegisterCallback<PointerUpEvent>(OnOpenPointerUp);

            // 初期状態
            progressPanel.style.display = DisplayStyle.None;
            UpdateStatus("待機中...");
            btnLoad.SetEnabled(false);
            btnExtract.SetEnabled(false);

            AppendLog("システム初期化完了");
        }

        void Update()
        {
            // 長押し検出（選択ボタン）
            if (isOpenPointerDown && Time.time - openPointerDownTime >= LONG_PRESS_DURATION)
            {
                isOpenPointerDown = false;
                OnOpenLongPress();
            }
        }

        void OnOpenPointerDown(PointerDownEvent evt)
        {
            openPointerDownTime = Time.time;
            isOpenPointerDown = true;
        }

        void OnOpenPointerUp(PointerUpEvent evt)
        {
            isOpenPointerDown = false;
        }

        void OnOpenLongPress()
        {
            AppendLog("長押し検出 - 解凍済みフォルダから選択");
            OpenExtractedFolder();
        }

        void UpdateLoadButtonState()
        {
            if (isAvatarLoaded)
            {
                // アバターがロード済み → 削除ボタンに変更
                btnLoad.text = "削除";
                btnLoad.style.backgroundColor = new StyleColor(new Color(220f/255f, 53f/255f, 69f/255f)); // 赤色
                btnLoad.SetEnabled(true);
            }
            else
            {
                // アバター未ロード → ロードボタン
                btnLoad.text = "ロード";
                btnLoad.style.backgroundColor = new StyleColor(new Color(58f/255f, 165f/255f, 93f/255f)); // 緑色
                // ファイルが選択されている場合のみ有効化
                // この処理はOnFileSelectedで行う
            }
        }

        void OnLoadOrDeleteClicked()
        {
            if (isAvatarLoaded)
            {
                // アバターがロード済み → 削除ポップアップを表示
                AppendLog("アバター削除メニューを表示");
                ShowDeletePopup();
            }
            else
            {
                // アバター未ロード → ロード処理
                OnLoadClicked();
            }
        }

        void ShowDeletePopup()
        {
            if (popupOverlay != null)
            {
                popupOverlay.style.display = DisplayStyle.Flex;
            }
        }

        void HideDeletePopup()
        {
            if (popupOverlay != null)
            {
                popupOverlay.style.display = DisplayStyle.None;
            }
        }

        void OnDeleteAvatarClicked()
        {
            AppendLog("アバターを削除中...");
            HideDeletePopup();

            if (loaderBridge != null)
            {
                loaderBridge.ClearCurrentModel();
                AppendLog("アバターを削除しました");
                UpdateStatus("待機中...");

                // 状態を更新
                isAvatarLoaded = false;
                UpdateLoadButtonState();
            }
            else
            {
                AppendLog("エラー: RuntimeFBXLoaderBridgeが見つかりません");
            }
        }

        void OnCancelPopupClicked()
        {
            AppendLog("削除をキャンセルしました");
            HideDeletePopup();
        }

        void OpenExtractedFolder()
        {
            if (fileBrowser == null) return;

            // 解凍済みフォルダのベースパスを取得
#if UNITY_IOS && !UNITY_EDITOR
            string basePath = Application.persistentDataPath;
#else
            string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            string basePath = System.IO.Path.Combine(userProfile, "Downloads");
#endif

            // ExtractedFBXとExtractedUnityPackageの両方をチェック
            string extractedFBX = System.IO.Path.Combine(basePath, "ExtractedFBX");
            string extractedUnityPackage = System.IO.Path.Combine(basePath, "ExtractedUnityPackage");

            string targetFolder = null;

            // 最近変更されたフォルダを優先
            if (System.IO.Directory.Exists(extractedFBX) && System.IO.Directory.Exists(extractedUnityPackage))
            {
                var fbxTime = System.IO.Directory.GetLastWriteTime(extractedFBX);
                var pkgTime = System.IO.Directory.GetLastWriteTime(extractedUnityPackage);
                targetFolder = fbxTime > pkgTime ? extractedFBX : extractedUnityPackage;
            }
            else if (System.IO.Directory.Exists(extractedFBX))
            {
                targetFolder = extractedFBX;
            }
            else if (System.IO.Directory.Exists(extractedUnityPackage))
            {
                targetFolder = extractedUnityPackage;
            }

            if (targetFolder != null)
            {
                AppendLog($"解凍済みフォルダを開く: {targetFolder}");
                OpenFilePickerInFolder(targetFolder);
            }
            else
            {
                AppendLog("解凍済みフォルダが見つかりません");
                UpdateStatus("解凍済みフォルダなし");
            }
        }

        void OpenFilePickerInFolder(string folderPath)
        {
#if UNITY_EDITOR
            // Unity Editorでの動作：指定フォルダから開く
            string path = UnityEditor.EditorUtility.OpenFilePanel("Select VRM or FBX from Extracted Folder", folderPath, "vrm,fbx");

            if (!string.IsNullOrEmpty(path))
            {
                // 選択されたファイルをFileBrowserControllerに設定
                System.Reflection.FieldInfo selectedPathField = typeof(FileBrowserController).GetField("SelectedPath",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                selectedPathField?.SetValue(fileBrowser, path);

                OnFileSelected(true, path);
            }
            else
            {
                AppendLog("選択をキャンセル");
            }
#else
            // モバイルでは通常のファイルピッカーを使用
            // （特定フォルダから開く機能はNativeFilePickerではサポートされていない）
            AppendLog("モバイルでは通常のファイル選択を使用してください");
            OnOpenClicked();
#endif
        }

        void OnOpenClicked()
        {
            AppendLog("ファイルピッカーを起動");

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

                string lowerPath = path.ToLower();

                // ZIP/UnityPackageファイルの場合は解凍ボタンを有効化
                bool needsExtraction = lowerPath.EndsWith(".zip") || lowerPath.EndsWith(".unitypackage");

                // VRM/FBXファイルの場合はロードボタンを有効化
                bool isModelFile = lowerPath.EndsWith(".vrm") || lowerPath.EndsWith(".fbx");

                if (needsExtraction)
                {
                    string fileType = lowerPath.EndsWith(".zip") ? "ZIP" : "UnityPackage";
                    UpdateStatus($"{fileType}ファイル選択済み - 解凍してください");
                    AppendLog($"{fileType}ファイルを検出。解凍ボタンを押してください");
                    btnExtract.SetEnabled(true);
                    btnLoad.SetEnabled(false);
                }
                else if (isModelFile)
                {
                    UpdateStatus("ファイル選択済み");
                    btnLoad.SetEnabled(true);
                    btnExtract.SetEnabled(false);
                }
            }
            else
            {
                AppendLog("選択をキャンセル");
                UpdateStatus("待機中...");
                btnLoad.SetEnabled(false);
                btnExtract.SetEnabled(false);
            }
        }

        void OnExtractClicked()
        {
            AppendLog("パッケージを解凍中...");
            UpdateStatus("解凍中...");
            btnOpen.SetEnabled(false);
            btnLoad.SetEnabled(false);
            btnExtract.SetEnabled(false);

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
                btnLoad.SetEnabled(true);
                btnExtract.SetEnabled(false);
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
            btnLoad.SetEnabled(false);
            btnExtract.SetEnabled(false);

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
            loadingLabel.text = $"ロード中... {percent:F0}%";
        }

        void OnComplete(bool success)
        {
            UpdateStatus("待機中...", showProgress: false);
            btnOpen.SetEnabled(true);
            btnExtract.SetEnabled(false);

            if (success)
            {
                AppendLog("モデルのロードに成功");
                UpdateStatus("ロード完了");

                // 状態を更新
                isAvatarLoaded = true;
                UpdateLoadButtonState();
            }
            else
            {
                AppendLog("ロード失敗");
                UpdateStatus("ロード失敗");

                // ロード失敗時は元の状態に戻す
                btnLoad.SetEnabled(false);
                isAvatarLoaded = false;
                UpdateLoadButtonState();
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
    }
}
