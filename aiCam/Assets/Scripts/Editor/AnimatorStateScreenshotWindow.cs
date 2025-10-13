// AnimatorStateScreenshotWindow.cs
// Unity Editor only. Place this file under an `Editor` folder.

using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public class AnimatorStateScreenshotWindow : EditorWindow
{
    // ====== 入力 ======
    [Header("対象")]
    [SerializeField] private GameObject targetObject;
    [SerializeField] private int layerIndex = 0;

    // 内部でtargetObjectのAnimatorから取得
    private AnimatorController GetAnimatorController()
    {
        if (targetObject == null) return null;
        var animator = targetObject.GetComponent<Animator>();
        if (animator == null) return null;
        return animator.runtimeAnimatorController as AnimatorController;
    }

    [Header("撮影設定")]
    [SerializeField] private Camera captureCamera; // 未指定なら一時カメラ
    [SerializeField] private SkinnedMeshRenderer targetRenderer; // カメラの中心参照用
    [SerializeField] private bool fitWholeBody = false; // 全身を入れる
    [SerializeField] private float cameraDistance = 0.5f; // カメラ距離倍率
    [SerializeField] private Vector2 cameraOffset = Vector2.zero; // カメラのXYオフセット
    [SerializeField] private int captureWidth = 1024;
    [SerializeField] private int captureHeight = 1024;
    [SerializeField] private float normalizedTime = 0.0f; // 0.0〜1.0
    [SerializeField] private Color backgroundColor = new Color(0, 0, 0, 0);
    [SerializeField] private bool transparentBackground = true;

    [Header("出力")]
    [SerializeField] private string outputDirectory = "";
    [SerializeField] private string filePrefix = "State_";
    [SerializeField] private bool includeSubStatePathInFileName = true;
    [SerializeField] private bool openFolderAfterSave = false;

    [Header("デバッグ")]
    [SerializeField] private bool debugMode = false;
    [SerializeField] private float debugSwitchInterval = 1.0f; // 秒
    [SerializeField] private bool debugAutoCapture = false; // デバッグ時に自動撮影

    // 個別撮影UI
    private int _selectedIndex = -1;

    // デバッグモード用
    private bool _isDebugRunning = false;
    private int _debugCurrentIndex = 0;
    private double _debugLastSwitchTime = 0;
    private int _debugCaptureCount = 0;

    // タブUI
    private int _currentTab = 0; // 0: 撮影, 1: プレビュー
    private readonly string[] _tabLabels = new string[] { "撮影", "プレビュー" };

    // プレビュー用
    private Vector2 _previewScrollPosition = Vector2.zero;
    private Dictionary<string, Texture2D> _previewTextures = new Dictionary<string, Texture2D>();
    private int _previewThumbnailSize = 128;

    // 収集データ
    private struct StateEntry
    {
        public AnimatorState state;     // デバッグ用参照
        public string fullPath;         // "Layer/SubSM/.../State"
        public Motion motion;           // State.motion（Clip or BlendTree）
        public string FileSafeName(bool includePath)
        {
            string raw = includePath
                ? fullPath.Replace('/', '_')
                : (state != null ? state.name : "State");
            foreach (var c in Path.GetInvalidFileNameChars()) raw = raw.Replace(c, '_');
            return raw;
        }
    }
    private readonly List<StateEntry> _entries = new List<StateEntry>();
    private string[] _entryLabels = Array.Empty<string>();

    [MenuItem("Tools/dsgarage/Animator State Screenshot Tool (Sampling)")]
    public static void Open()
    {
        var w = GetWindow<AnimatorStateScreenshotWindow>("Animator State Shot");
        w.minSize = new Vector2(540, 520);
        w.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;

        // ウィンドウ起動時にoutputDirectoryが未設定の場合、自動的にルールに従ったディレクトリを設定
        if (string.IsNullOrEmpty(outputDirectory))
        {
            UpdateOutputDirectory();
        }
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        StopDebugMode();
    }

    private void OnEditorUpdate()
    {
        if (!_isDebugRunning) return;

        double currentTime = EditorApplication.timeSinceStartup;
        if (currentTime - _debugLastSwitchTime >= debugSwitchInterval)
        {
            SwitchToNextDebugState();
            // SwitchToNextDebugStateの後に時刻を更新
            _debugLastSwitchTime = currentTime;
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();

        // タブUI
        _currentTab = GUILayout.Toolbar(_currentTab, _tabLabels, GUILayout.Height(28));

        EditorGUILayout.Space();

        // タブに応じて表示を切り替え
        if (_currentTab == 0)
        {
            DrawCaptureTab();
        }
        else if (_currentTab == 1)
        {
            DrawPreviewTab();
        }
    }

    private void DrawCaptureTab()
    {
        EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        targetObject = (GameObject)EditorGUILayout.ObjectField("Target GameObject", targetObject, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            Debug.Log($"[OnGUI] Target GameObject が変更されました: {targetObject?.name ?? "null"}");
            // Target変更時に全LayerをEntryに戻す
            if (targetObject != null)
            {
                Debug.Log("[OnGUI] ResetAllLayersToEntry() を呼び出します");
                ResetAllLayersToEntry();
            }
            // Target変更時にStateリストを更新
            TryBuildStateList();
            // Target変更時にoutputDirectoryを更新
            UpdateOutputDirectory();
        }

        // AnimatorControllerを表示（読み取り専用）
        var animatorController = GetAnimatorController();
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Animator Controller", animatorController, typeof(AnimatorController), false);
        EditorGUI.EndDisabledGroup();

        // Layer選択（Popup）
        if (animatorController != null && animatorController.layers.Length > 0)
        {
            string[] layerNames = new string[animatorController.layers.Length];
            for (int i = 0; i < animatorController.layers.Length; i++)
            {
                layerNames[i] = animatorController.layers[i].name;
            }

            int newLayer = EditorGUILayout.Popup("Layer", layerIndex, layerNames);
            if (newLayer != layerIndex)
            {
                Debug.Log($"[Layer変更] {layerIndex} → {newLayer} ('{layerNames[newLayer]}')");
                layerIndex = newLayer;
                TryBuildStateList();
                // Layer変更時にoutputDirectoryを強制更新（outputDirectoryをクリアして再設定）
                Debug.Log($"[Layer変更] outputDirectoryをクリアして再設定します");
                outputDirectory = "";
                UpdateOutputDirectory();
            }
        }
        else
        {
            // AnimatorControllerがnullまたはLayerが0の場合はIntFieldで表示
            int newLayer = EditorGUILayout.IntField("Layer Index", layerIndex);
            if (newLayer != layerIndex)
            {
                layerIndex = newLayer;
                TryBuildStateList();
                // Layer変更時にoutputDirectoryを更新
                UpdateOutputDirectory();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("撮影設定", EditorStyles.boldLabel);
        captureCamera = (Camera)EditorGUILayout.ObjectField("Capture Camera", captureCamera, typeof(Camera), true);

        EditorGUILayout.LabelField("カメラ自動配置設定", EditorStyles.miniBoldLabel);
        targetRenderer = (SkinnedMeshRenderer)EditorGUILayout.ObjectField("Target Renderer (中心参照)", targetRenderer, typeof(SkinnedMeshRenderer), true);

        EditorGUI.BeginChangeCheck();
        fitWholeBody = EditorGUILayout.Toggle("全身を入れる", fitWholeBody);
        cameraDistance = EditorGUILayout.Slider("カメラ距離倍率", cameraDistance, 0.5f, 5.0f);
        cameraOffset = EditorGUILayout.Vector2Field("カメラオフセット (X, Y)", cameraOffset);
        if (EditorGUI.EndChangeCheck())
        {
            // カメラ設定が変更されたらプレビュー更新
            if (captureCamera == null && targetObject != null)
            {
                PreviewCameraPosition();
            }
        }

        if (captureCamera == null && targetObject != null)
        {
            if (GUILayout.Button("カメラ位置をプレビュー", GUILayout.Height(24)))
            {
                PreviewCameraPosition();
            }
        }

        captureWidth = Mathf.Max(8, EditorGUILayout.IntField("Width (px)", captureWidth));
        captureHeight = Mathf.Max(8, EditorGUILayout.IntField("Height (px)", captureHeight));
        normalizedTime = Mathf.Clamp01(EditorGUILayout.Slider("Normalized Time", normalizedTime, 0f, 1f));
        backgroundColor = EditorGUILayout.ColorField("Background Color", backgroundColor);
        transparentBackground = EditorGUILayout.Toggle(new GUIContent("Transparent Background (PNG α)"), transparentBackground);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("出力", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Output Directory");
        EditorGUILayout.SelectableLabel(string.IsNullOrEmpty(outputDirectory) ? "(未選択)" : outputDirectory, GUILayout.Height(18));
        if (GUILayout.Button("選択", GUILayout.Width(64)))
        {
            var selected = EditorUtility.OpenFolderPanel("出力フォルダを選択", string.IsNullOrEmpty(outputDirectory) ? Application.dataPath : outputDirectory, "");
            if (!string.IsNullOrEmpty(selected)) outputDirectory = selected;
        }
        EditorGUILayout.EndHorizontal();
        filePrefix = EditorGUILayout.TextField("File Prefix", filePrefix);
        includeSubStatePathInFileName = EditorGUILayout.Toggle(new GUIContent("ファイル名にサブステートのパスを含める"), includeSubStatePathInFileName);
        openFolderAfterSave = EditorGUILayout.Toggle(new GUIContent("保存後にフォルダを開く"), openFolderAfterSave);

        EditorGUILayout.Space();
        DrawStateListUI();

        EditorGUILayout.Space();
        if (targetObject != null && targetObject.GetComponent<Animator>() != null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Aポーズにリセット", GUILayout.Height(24)))
                {
                    ResetToAPose();
                }

                if (GUILayout.Button("全LayerをEntryに戻す", GUILayout.Height(24)))
                {
                    ResetAllLayersToEntry();
                }
            }
        }

        EditorGUILayout.Space();
        DrawCaptureModeUI();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "• Animator を再生せず、各 State の Motion（AnimationClip/BlendTree）を Editor の AnimationMode で直接サンプルしてポーズを当てた後に撮影します。\n" +
            "• 表情（ブレンドシェイプ）、ボーン、その他のカーブは Clip に入っている値がそのまま反映されます。\n" +
            "• BlendTree は現在「既定値（全パラメータ=0）」から辿れる最初の Clip をサンプルします。必要なら UI でパラメータ注入を拡張可能です。",
            MessageType.Info);
    }

    private void DrawPreviewTab()
    {
        EditorGUILayout.LabelField("プレビュー", EditorStyles.boldLabel);

        var animatorController = GetAnimatorController();
        if (animatorController == null)
        {
            EditorGUILayout.HelpBox("Target GameObject を設定してください。", MessageType.Info);
            return;
        }

        // サムネイルサイズスライダー
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("サムネイルサイズ", GUILayout.Width(120));
        _previewThumbnailSize = (int)EditorGUILayout.Slider(_previewThumbnailSize, 64, 256);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // スクロールビュー開始
        _previewScrollPosition = EditorGUILayout.BeginScrollView(_previewScrollPosition);

        // レイヤーごとにサムネイルを表示
        for (int i = 0; i < animatorController.layers.Length; i++)
        {
            var layer = animatorController.layers[i];

            EditorGUILayout.LabelField($"Layer: {layer.name}", EditorStyles.boldLabel);

            // このレイヤーのスクリーンショットが保存されているディレクトリを取得
            string layerDirectory = GetLayerOutputDirectory(i);

            if (!Directory.Exists(layerDirectory))
            {
                EditorGUILayout.HelpBox($"ディレクトリが見つかりません: {layerDirectory}", MessageType.Info);
                EditorGUILayout.Space();
                continue;
            }

            // PNG ファイルを検索
            string[] pngFiles = Directory.GetFiles(layerDirectory, "*.png");

            if (pngFiles.Length == 0)
            {
                EditorGUILayout.HelpBox("スクリーンショットがありません。", MessageType.Info);
                EditorGUILayout.Space();
                continue;
            }

            // グリッド表示
            int thumbnailsPerRow = Mathf.Max(1, (int)(position.width / (_previewThumbnailSize + 10)));
            int currentColumn = 0;

            EditorGUILayout.BeginHorizontal();

            foreach (string filePath in pngFiles)
            {
                // テクスチャをロード（キャッシュあり）
                Texture2D thumbnail = LoadThumbnail(filePath);

                if (thumbnail != null)
                {
                    EditorGUILayout.BeginVertical(GUILayout.Width(_previewThumbnailSize));

                    // サムネイル表示
                    GUILayout.Box(thumbnail, GUILayout.Width(_previewThumbnailSize), GUILayout.Height(_previewThumbnailSize));

                    // ファイル名表示
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    GUILayout.Label(fileName, EditorStyles.wordWrappedLabel, GUILayout.Width(_previewThumbnailSize));

                    EditorGUILayout.EndVertical();

                    currentColumn++;

                    // 改行判定
                    if (currentColumn >= thumbnailsPerRow)
                    {
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.BeginHorizontal();
                        currentColumn = 0;
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space();
        }

        EditorGUILayout.EndScrollView();
    }

    private Texture2D LoadThumbnail(string filePath)
    {
        // キャッシュチェック
        if (_previewTextures.ContainsKey(filePath))
        {
            return _previewTextures[filePath];
        }

        // ファイルが存在しない場合
        if (!File.Exists(filePath))
        {
            return null;
        }

        // テクスチャをロード
        byte[] fileData = File.ReadAllBytes(filePath);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

        if (texture.LoadImage(fileData))
        {
            // キャッシュに追加
            _previewTextures[filePath] = texture;
            return texture;
        }

        // ロード失敗
        UnityEngine.Object.DestroyImmediate(texture);
        return null;
    }

    private void LoadPreviewThumbnails()
    {
        // キャッシュをクリア
        foreach (var kvp in _previewTextures)
        {
            if (kvp.Value != null)
            {
                UnityEngine.Object.DestroyImmediate(kvp.Value);
            }
        }
        _previewTextures.Clear();

        // UIを再描画
        Repaint();

        Debug.Log("[LoadPreviewThumbnails] プレビューサムネイルをクリアしました。");
    }

    private string GetLayerOutputDirectory(int targetLayerIndex)
    {
        var animatorController = GetAnimatorController();
        if (animatorController == null)
        {
            return null;
        }

        // Assets/Spriteフォルダパス
        string spriteFolderPath = Path.Combine(Application.dataPath, "Sprite");

        string controllerName = animatorController.name;
        // 不正な文字を除去
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            controllerName = controllerName.Replace(c, '_');
        }

        // Layer名を取得
        string layerName = "Layer0";
        if (targetLayerIndex >= 0 && targetLayerIndex < animatorController.layers.Length)
        {
            layerName = animatorController.layers[targetLayerIndex].name;
            // 不正な文字を除去
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                layerName = layerName.Replace(c, '_');
            }
        }

        // Assets/Sprite/{AnimatorController名}/{Layer名}
        string finalPath = Path.Combine(spriteFolderPath, controllerName, layerName);
        return finalPath;
    }

    private void DrawCaptureModeUI()
    {
        EditorGUILayout.LabelField("撮影モード", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(_isDebugRunning);
            debugSwitchInterval = EditorGUILayout.FloatField("切替間隔(秒)", Mathf.Max(0.5f, debugSwitchInterval));
            EditorGUI.EndDisabledGroup();
        }

        EditorGUI.BeginDisabledGroup(_isDebugRunning);
        debugAutoCapture = EditorGUILayout.Toggle("自動撮影", debugAutoCapture);
        EditorGUI.EndDisabledGroup();

        using (new EditorGUILayout.HorizontalScope())
        {
            // 自動撮影時は出力先も必要
            bool canStart = debugAutoCapture ? CanRun() : CanRunDebug();
            EditorGUI.BeginDisabledGroup(!canStart || _entries.Count == 0);

            if (!_isDebugRunning)
            {
                string buttonLabel = debugAutoCapture ? "撮影" : "Animation確認";
                if (GUILayout.Button(buttonLabel, GUILayout.Height(32)))
                {
                    StartDebugMode();
                }
            }
            else
            {
                if (GUILayout.Button("停止", GUILayout.Height(32)))
                {
                    StopDebugMode();
                }

                string statusText = _entries.Count > 0
                    ? (debugAutoCapture
                        ? $"実行中: {_debugCurrentIndex + 1}/{_entries.Count} - {_entries[_debugCurrentIndex].fullPath} (撮影数: {_debugCaptureCount})"
                        : $"実行中: {_debugCurrentIndex + 1}/{_entries.Count} - {_entries[_debugCurrentIndex].fullPath}")
                    : "実行中";
                EditorGUILayout.LabelField(statusText, EditorStyles.wordWrappedLabel);
            }

            EditorGUI.EndDisabledGroup();
        }

        if (debugAutoCapture && string.IsNullOrEmpty(outputDirectory))
        {
            EditorGUILayout.HelpBox("自動撮影を有効にする場合は出力先フォルダを指定してください。", MessageType.Warning);
        }
    }

    private void DrawStateListUI()
    {
        EditorGUILayout.LabelField("個別撮影", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("State再読み込み", GUILayout.Width(140)))
            {
                TryBuildStateList();
            }

            EditorGUI.BeginDisabledGroup(_entries.Count == 0 || _isDebugRunning);
            int newSelected = _entries.Count == 0
                ? -1
                : EditorGUILayout.Popup(_selectedIndex < 0 ? 0 : _selectedIndex, _entryLabels);
            if (newSelected != _selectedIndex)
            {
                _selectedIndex = newSelected;
                // 選択されたStateに切り替える
                if (_selectedIndex >= 0 && _selectedIndex < _entries.Count && targetObject != null)
                {
                    var entry = _entries[_selectedIndex];
                    Debug.Log($"[個別撮影] State選択: {entry.fullPath}");

                    // Animatorでポーズを適用してプレビュー
                    ApplyStatePoseBySampling(targetObject, entry, normalizedTime);
                }
            }

            if (GUILayout.Button("選択Stateを撮影", GUILayout.Width(160)))
            {
                if (CanRun() && _entries.Count > 0)
                {
                    if (_selectedIndex < 0 || _selectedIndex >= _entries.Count) _selectedIndex = 0;
                    ExecuteCapture(allStates: false);
                }
            }
            EditorGUI.EndDisabledGroup();
        }
    }

    private bool CanRun()
    {
        if (targetObject == null) return false;
        var animatorController = GetAnimatorController();
        if (animatorController == null) return false;
        if (string.IsNullOrEmpty(outputDirectory)) return false;
        if (layerIndex < 0 || layerIndex >= (animatorController.layers?.Length ?? 0)) return false;
        if (targetObject.GetComponent<Animator>() == null) return false;
        return true;
    }

    // デバッグモード用：出力先不要
    private bool CanRunDebug()
    {
        if (targetObject == null) return false;
        var animatorController = GetAnimatorController();
        if (animatorController == null) return false;
        if (layerIndex < 0 || layerIndex >= (animatorController.layers?.Length ?? 0)) return false;
        if (targetObject.GetComponent<Animator>() == null) return false;
        return true;
    }

    private void TryBuildStateList()
    {
        _entries.Clear();
        _entryLabels = Array.Empty<string>();
        _selectedIndex = -1;

        var animatorController = GetAnimatorController();
        if (animatorController == null)
        {
            Debug.LogWarning("[TryBuildStateList] AnimatorControllerがnullです");
            return;
        }

        Debug.Log($"[TryBuildStateList] layerIndex: {layerIndex}, layers.Length: {animatorController.layers.Length}");

        if (layerIndex < 0 || layerIndex >= animatorController.layers.Length)
        {
            Debug.LogWarning($"[TryBuildStateList] layerIndexが範囲外です: {layerIndex}");
            return;
        }

        var layer = animatorController.layers[layerIndex];
        Debug.Log($"[TryBuildStateList] 選択されたLayer: Index={layerIndex}, Name='{layer.name}'");

        CollectStatesRecursive(layer.name, layer.stateMachine, _entries);

        _entryLabels = new string[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
        {
            _entryLabels[i] = _entries[i].fullPath;
        }
        if (_entries.Count > 0) _selectedIndex = 0;

        Debug.Log($"[TryBuildStateList] 収集したState数: {_entries.Count}");
        if (_entries.Count > 0)
        {
            Debug.Log($"[TryBuildStateList] 最初のState: {_entries[0].fullPath}");
        }
    }

    // ====== 収集 ======
    private void CollectStatesRecursive(string prefix, AnimatorStateMachine sm, List<StateEntry> list)
    {
        if (sm == null) return;

        foreach (var cs in sm.states)
        {
            if (cs.state != null)
            {
                list.Add(new StateEntry
                {
                    state = cs.state,
                    motion = cs.state.motion,
                    fullPath = $"{prefix}/{cs.state.name}"
                });
            }
        }
        foreach (var csm in sm.stateMachines)
        {
            if (csm.stateMachine != null)
            {
                CollectStatesRecursive($"{prefix}/{csm.stateMachine.name}", csm.stateMachine, list);
            }
        }
    }

    // ====== 実行（サンプリング→撮影） ======
    private void ExecuteCapture(bool allStates)
    {
        Debug.Log($"[ExecuteCapture] 開始 - allStates: {allStates}");

        if (!CanRun())
        {
            Debug.LogWarning("[ExecuteCapture] CanRun()がfalseを返しました");
            EditorUtility.DisplayDialog("エラー", "入力が不足しています。Target GameObject（Animator付き）・出力先を確認してください。", "OK");
            return;
        }
        if (_entries.Count == 0) TryBuildStateList();
        if (_entries.Count == 0)
        {
            EditorUtility.DisplayDialog("情報", "指定レイヤーに State が見つかりませんでした。", "OK");
            return;
        }

        if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

        var animator = targetObject.GetComponent<Animator>();
        bool prevAnimatorEnabled = animator.enabled;

        Debug.Log("[ExecuteCapture] カメラ準備開始");

        // カメラ準備
        Camera cam = captureCamera;
        bool createdTempCamera = false;
        Vector3 prevCameraPosition = Vector3.zero;
        Quaternion prevCameraRotation = Quaternion.identity;
        bool needRestoreCamera = false;

        if (cam == null)
        {
            Debug.Log("[ExecuteCapture] 一時カメラを作成");
            createdTempCamera = true;
            var go = new GameObject("Temp_CaptureCamera");
            cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            cam.orthographic = false;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;
        }
        else
        {
            Debug.Log($"[ExecuteCapture] 指定カメラを使用: {cam.name}");
            // 指定カメラの場合は初期状態を保存
            prevCameraPosition = cam.transform.position;
            prevCameraRotation = cam.transform.rotation;
            needRestoreCamera = true;

            if (cam.clearFlags == CameraClearFlags.Skybox && transparentBackground)
                cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
        }

        // カメラ位置を設定（一時カメラの場合、または全身を入れる設定の場合のみ）
        if (createdTempCamera || fitWholeBody)
        {
            Debug.Log("[ExecuteCapture] SetupCameraPositionを呼び出します");
            SetupCameraPosition(cam);
            Debug.Log("[ExecuteCapture] SetupCameraPosition完了");
        }
        else
        {
            Debug.Log("[ExecuteCapture] 全身を入れる設定がOFFのため、カメラ位置は変更しません");
        }

        var rt = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        var prevActive = RenderTexture.active;
        var prevTarget = cam.targetTexture;

        int success = 0, fail = 0;

        try
        {
            cam.targetTexture = rt;

            // Animator の自動評価を止める（サンプル結果が上書きされないように）
            animator.enabled = false;

            // 対象リスト
            IEnumerable<StateEntry> targetList = allStates
                ? _entries
                : new[] { _entries[Mathf.Clamp(_selectedIndex, 0, _entries.Count - 1)] };

            foreach (var entry in targetList)
            {
                try
                {
                    // 1) AnimatorでStateを切り替え（Scene Viewでの表示用）
                    if (entry.state != null)
                    {
                        // Animatorを一時的に有効化
                        bool wasEnabled = animator.enabled;
                        if (!wasEnabled) animator.enabled = true;

                        // Stateを再生
                        animator.Play(entry.state.nameHash, layerIndex, normalizedTime);

                        // 少し更新してStateを反映
                        animator.Update(0.01f);

                        Debug.Log($"[ExecuteCapture] State切替: {entry.fullPath} (Hash: {entry.state.nameHash})");

                        // Animatorを無効に戻す
                        animator.enabled = false;
                    }

                    // 2) ポーズ適用（撮影用）
                    if (!ApplyStatePoseBySampling(targetObject, entry, normalizedTime))
                    {
                        Debug.LogWarning($"State '{entry.fullPath}'：サンプルできる AnimationClip が見つかりませんでした。スキップします。");
                        continue;
                    }

                    // カメラ位置を各Stateごとに更新（全身を入れる設定またはカメラ未指定の場合）
                    if (createdTempCamera || fitWholeBody)
                    {
                        SetupCameraPosition(cam);
                    }

                    // 3) レンダリング
                    RenderTexture.active = rt;
                    if (transparentBackground)
                    {
                        GL.PushMatrix();
                        GL.LoadPixelMatrix(0, captureWidth, captureHeight, 0);
                        GL.Clear(true, true, backgroundColor);
                        GL.PopMatrix();
                    }
                    cam.Render();

                    // 4) PNG保存
                    var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false, false);
                    tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
                    tex.Apply(false, false);
                    var png = tex.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(tex);

                    // 保存先ディレクトリを取得（デバッグモードと同じ動作）
                    string saveDirectory = GetOutputDirectory();
                    if (string.IsNullOrEmpty(saveDirectory))
                    {
                        Debug.LogError($"[ExecuteCapture] 保存先ディレクトリが取得できませんでした。");
                        fail++;
                        continue;
                    }

                    // ディレクトリが存在しない場合は作成
                    if (!Directory.Exists(saveDirectory))
                    {
                        Directory.CreateDirectory(saveDirectory);
                        Debug.Log($"[ExecuteCapture] ディレクトリ作成: {saveDirectory}");
                    }

                    string fname = $"{filePrefix}{entry.FileSafeName(includeSubStatePathInFileName)}.png";
                    string path = Path.Combine(saveDirectory, fname);
                    File.WriteAllBytes(path, png);

                    success++;
                    Debug.Log($"Saved: {path}");

                    // Scene Viewを更新
                    UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
                }
                catch (Exception e)
                {
                    Debug.LogError($"State '{entry.fullPath}' の保存に失敗: {e}");
                    fail++;
                }
            }
        }
        finally
        {
            // 復元と掃除
            cam.targetTexture = prevTarget;
            if (RenderTexture.active == rt) RenderTexture.active = null;
            RenderTexture.active = prevActive;

            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);
            }

            // 指定カメラの位置を元に戻す
            if (needRestoreCamera && cam != null)
            {
                cam.transform.position = prevCameraPosition;
                cam.transform.rotation = prevCameraRotation;
                Debug.Log($"[カメラ復元] Position: {prevCameraPosition}, Rotation: {prevCameraRotation.eulerAngles}");
            }

            animator.enabled = prevAnimatorEnabled;

            if (createdTempCamera && captureCamera == null && cam != null)
            {
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }

            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog("完了", $"スクリーンショット完了\n成功: {success}\n失敗: {fail}\n出力先: {outputDirectory}", "OK");

        // 撮影成功時は自動的にプレビュータブに切り替えて更新
        if (success > 0)
        {
            // プレビューキャッシュをクリア
            foreach (var kvp in _previewTextures)
            {
                if (kvp.Value != null)
                {
                    UnityEngine.Object.DestroyImmediate(kvp.Value);
                }
            }
            _previewTextures.Clear();

            // プレビュータブに切り替え
            _currentTab = 1;

            // UIを再描画
            Repaint();

            Debug.Log("[ExecuteCapture] プレビュータブに切り替えました。");
        }

        if (openFolderAfterSave && success > 0) EditorUtility.RevealInFinder(outputDirectory);
    }

    /// <summary>
    /// 指定Stateの Motion を辿って AnimationClip を見つけ、AnimationMode.SampleAnimationClip で静的ポーズを適用。
    /// BlendTree の場合は「全パラメータ=0」で到達できる最初の AnimationClip を選びます（ネストにも対応）。
    /// </summary>
    private static bool ApplyStatePoseBySampling(GameObject target, StateEntry entry, float normalized)
    {
        // Motion から Clip を探索
        if (!TryGetRepresentativeClip(entry.motion, out var clip))
            return false;

        float t = Mathf.Clamp01(normalized) * Mathf.Max(clip.length, 0.0001f);

        // Editorのアニメーションモードでサンプル
        // 既存ポーズを壊さないよう、開始〜終了を囲む
        AnimationMode.StartAnimationMode();
        try
        {
            // NOTE: SampleAnimationClip は target 配下の全Transform/SkinnedMeshRendererのカーブを適用します
            AnimationMode.SampleAnimationClip(target, clip, t);
            // 反映をUIにも通知
            UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        }
        finally
        {
            AnimationMode.StopAnimationMode();
        }
        return true;
    }

    /// <summary>
    /// Motion から「代表Clip」を取得。AnimationClipならそれを、BlendTreeならパラメータ0で辿れる最初のClip（ネスト可）を返す。
    /// </summary>
    private static bool TryGetRepresentativeClip(Motion motion, out AnimationClip clip)
    {
        clip = null;
        if (motion == null) return false;

        var ac = motion as AnimationClip;
        if (ac != null)
        {
            clip = ac;
            return true;
        }

        var bt = motion as BlendTree;
        if (bt != null)
        {
            // パラメータ0（既定値）で最初に到達できる子を探索（ネストに対応）
            foreach (var child in bt.children)
            {
                if (TryGetRepresentativeClip(child.motion, out clip))
                    return true;
            }
            return false;
        }

        return false;
    }

    // ====== デバッグモード ======
    private void StartDebugMode()
    {
        if (_entries.Count == 0)
        {
            TryBuildStateList();
        }

        if (_entries.Count == 0)
        {
            EditorUtility.DisplayDialog("エラー", "Stateが見つかりません。Target GameObject（Animator付き）とLayer Indexを確認してください。", "OK");
            return;
        }

        if (debugAutoCapture)
        {
            if (!CanRun())
            {
                EditorUtility.DisplayDialog("エラー", "自動撮影を有効にする場合は、Target GameObject（Animator付き）と出力先フォルダを設定してください。", "OK");
                return;
            }
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
        }
        else
        {
            if (!CanRunDebug())
            {
                EditorUtility.DisplayDialog("エラー", "Target GameObject（Animator付き）を設定してください。", "OK");
                return;
            }
        }

        _isDebugRunning = true;
        _debugCurrentIndex = -1; // -1から始めて、最初の呼び出しでState 0になる
        _debugCaptureCount = 0;

        // OnEditorUpdateで即座に最初のStateに切り替わるように、過去の時刻を設定
        _debugLastSwitchTime = EditorApplication.timeSinceStartup - debugSwitchInterval;

        string modeText = debugAutoCapture ? "自動撮影モード" : "プレビューモード";
        Debug.Log($"デバッグモード開始 ({modeText}): {_entries.Count}個のStateを{debugSwitchInterval}秒間隔で切り替えます。");
    }

    private void StopDebugMode()
    {
        if (_isDebugRunning)
        {
            _isDebugRunning = false;
            if (debugAutoCapture)
            {
                Debug.Log($"デバッグモード停止。撮影完了: {_debugCaptureCount}枚");

                if (_debugCaptureCount > 0)
                {
                    // プレビューキャッシュをクリア
                    foreach (var kvp in _previewTextures)
                    {
                        if (kvp.Value != null)
                        {
                            UnityEngine.Object.DestroyImmediate(kvp.Value);
                        }
                    }
                    _previewTextures.Clear();

                    // プレビュータブに切り替え
                    _currentTab = 1;

                    // UIを再描画
                    Repaint();

                    Debug.Log("[StopDebugMode] プレビュータブに切り替えました。");

                    if (openFolderAfterSave)
                    {
                        string saveDirectory = GetOutputDirectory();
                        if (!string.IsNullOrEmpty(saveDirectory))
                        {
                            EditorUtility.RevealInFinder(saveDirectory);
                        }
                    }
                }
            }
            else
            {
                Debug.Log("デバッグモード停止");
            }
        }
    }

    private void SwitchToNextDebugState()
    {
        Debug.Log($"[SwitchToNextDebugState] 呼び出し開始 - 現在のIndex: {_debugCurrentIndex}");

        if (_entries.Count == 0 || targetObject == null) return;

        // 次のインデックスへ
        _debugCurrentIndex++;
        Debug.Log($"[SwitchToNextDebugState] インデックスを更新: {_debugCurrentIndex}");

        // 全State撮影完了チェック
        if (_debugCurrentIndex >= _entries.Count)
        {
            Debug.Log($"[デバッグ] 全{_entries.Count}個のState切り替え完了。自動停止します。");
            StopDebugMode();
            return;
        }

        var animator = targetObject.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("Target GameObject に Animator コンポーネントがありません。");
            StopDebugMode();
            return;
        }

        var entry = _entries[_debugCurrentIndex];
        Debug.Log($"[SwitchToNextDebugState] 処理するState: Index={_debugCurrentIndex}, Name={entry.fullPath}");

        // まずAnimatorでStateを切り替え（Scene Viewでの表示用）
        if (entry.state != null)
        {
            // Animatorを有効化
            bool wasEnabled = animator.enabled;
            if (!wasEnabled) animator.enabled = true;

            // Stateを再生
            animator.Play(entry.state.nameHash, layerIndex, normalizedTime);

            // 少し更新してStateを反映
            animator.Update(0.01f);

            Debug.Log($"[デバッグ] State切替完了: {_debugCurrentIndex + 1}/{_entries.Count} - {entry.fullPath} (Hash: {entry.state.nameHash})");
        }
        else
        {
            Debug.LogWarning($"State '{entry.fullPath}'：Stateオブジェクトが見つかりません。");
        }

        // 自動撮影が有効な場合、スクリーンショットを撮影
        if (debugAutoCapture)
        {
            Debug.Log($"[SwitchToNextDebugState] 自動撮影開始 - State: {entry.fullPath}");

            // ポーズ適用（通常撮影と同じ方法で）
            if (!ApplyStatePoseBySampling(targetObject, entry, normalizedTime))
            {
                Debug.LogWarning($"State '{entry.fullPath}'：サンプルできる AnimationClip が見つかりませんでした。スキップします。");
            }
            else
            {
                // 全身を入れる設定またはカメラ未指定の場合は自動配置、それ以外は指定カメラをそのまま使用
                if (fitWholeBody || captureCamera == null)
                {
                    CaptureScreenshotWithAutoPosition(entry);
                }
                else
                {
                    CaptureScreenshotWithFixedCamera(entry);
                }
                Debug.Log($"[SwitchToNextDebugState] 自動撮影完了 - State: {entry.fullPath}");
            }
        }

        // Scene Viewを更新
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

        // UIを再描画
        Repaint();

        Debug.Log($"[SwitchToNextDebugState] 処理完了 - 次のIndex: {_debugCurrentIndex}");
    }

    // 自動カメラ配置で撮影（全身を入れる、またはカメラ未指定の場合）
    private void CaptureScreenshotWithAutoPosition(StateEntry entry)
    {
        Debug.Log($"[CaptureScreenshotWithAutoPosition] 開始 - State: {entry.fullPath}");

        try
        {
            // カメラ準備
            Camera cam = captureCamera;
            bool createdTempCamera = false;
            Vector3 prevCameraPosition = Vector3.zero;
            Quaternion prevCameraRotation = Quaternion.identity;
            bool needRestoreCamera = false;

            if (cam == null)
            {
                Debug.Log("[CaptureScreenshotWithAutoPosition] 一時カメラを作成");
                createdTempCamera = true;
                var go = new GameObject("Temp_DebugCaptureCamera");
                cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = backgroundColor;
                cam.orthographic = false;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 1000f;
            }
            else
            {
                Debug.Log($"[CaptureScreenshotWithAutoPosition] 指定カメラを使用（位置を自動調整）: {cam.name}");
                // 指定カメラの場合は初期状態を保存
                prevCameraPosition = cam.transform.position;
                prevCameraRotation = cam.transform.rotation;
                needRestoreCamera = true;
            }

            // カメラ位置を自動設定
            Debug.Log("[CaptureScreenshotWithAutoPosition] SetupCameraPositionを呼び出します");
            SetupCameraPosition(cam);
            Debug.Log("[CaptureScreenshotWithAutoPosition] SetupCameraPosition完了");

            var rt = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var prevActive = RenderTexture.active;
            var prevTarget = cam.targetTexture;

            try
            {
                cam.targetTexture = rt;
                RenderTexture.active = rt;

                if (transparentBackground)
                {
                    GL.PushMatrix();
                    GL.LoadPixelMatrix(0, captureWidth, captureHeight, 0);
                    GL.Clear(true, true, backgroundColor);
                    GL.PopMatrix();
                }

                cam.Render();

                var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
                tex.Apply(false, false);
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);

                // 保存先ディレクトリを取得
                string saveDirectory = GetOutputDirectory();
                if (string.IsNullOrEmpty(saveDirectory))
                {
                    Debug.LogError("[CaptureScreenshotWithAutoPosition] 保存先ディレクトリが取得できませんでした。");
                    return;
                }

                // ディレクトリが存在しない場合は作成
                if (!Directory.Exists(saveDirectory))
                {
                    Directory.CreateDirectory(saveDirectory);
                    Debug.Log($"[CaptureScreenshotWithAutoPosition] ディレクトリ作成: {saveDirectory}");
                }

                string fname = $"{filePrefix}{entry.FileSafeName(includeSubStatePathInFileName)}.png";
                string path = Path.Combine(saveDirectory, fname);
                File.WriteAllBytes(path, png);

                _debugCaptureCount++;
                Debug.Log($"[デバッグ撮影] 保存完了: {path}");
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;

                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }

                // 指定カメラの位置を元に戻す
                if (needRestoreCamera && cam != null)
                {
                    cam.transform.position = prevCameraPosition;
                    cam.transform.rotation = prevCameraRotation;
                }

                if (createdTempCamera && cam != null)
                {
                    UnityEngine.Object.DestroyImmediate(cam.gameObject);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[CaptureScreenshotWithAutoPosition] State '{entry.fullPath}' の撮影に失敗: {e}");
        }
    }

    // 指定カメラの位置固定で撮影（全身を入れる設定がOFFの場合）
    private void CaptureScreenshotWithFixedCamera(StateEntry entry)
    {
        Debug.Log($"[CaptureScreenshotWithFixedCamera] 開始 - State: {entry.fullPath}");

        if (captureCamera == null)
        {
            Debug.LogError("[CaptureScreenshotWithFixedCamera] Capture Cameraが指定されていません。");
            return;
        }

        try
        {
            Camera cam = captureCamera;
            Debug.Log($"[CaptureScreenshotWithFixedCamera] 指定カメラを使用（位置固定）: {cam.name}, Position: {cam.transform.position}");

            var rt = new RenderTexture(captureWidth, captureHeight, 24, RenderTextureFormat.ARGB32);
            rt.Create();
            var prevActive = RenderTexture.active;
            var prevTarget = cam.targetTexture;

            try
            {
                cam.targetTexture = rt;
                RenderTexture.active = rt;

                if (transparentBackground)
                {
                    GL.PushMatrix();
                    GL.LoadPixelMatrix(0, captureWidth, captureHeight, 0);
                    GL.Clear(true, true, backgroundColor);
                    GL.PopMatrix();
                }

                cam.Render();

                var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false, false);
                tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
                tex.Apply(false, false);
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);

                // 保存先ディレクトリを取得
                string saveDirectory = GetOutputDirectory();
                if (string.IsNullOrEmpty(saveDirectory))
                {
                    Debug.LogError("[CaptureScreenshotWithFixedCamera] 保存先ディレクトリが取得できませんでした。");
                    return;
                }

                // ディレクトリが存在しない場合は作成
                if (!Directory.Exists(saveDirectory))
                {
                    Directory.CreateDirectory(saveDirectory);
                    Debug.Log($"[CaptureScreenshotWithFixedCamera] ディレクトリ作成: {saveDirectory}");
                }

                string fname = $"{filePrefix}{entry.FileSafeName(includeSubStatePathInFileName)}.png";
                string path = Path.Combine(saveDirectory, fname);
                File.WriteAllBytes(path, png);

                _debugCaptureCount++;
                Debug.Log($"[CaptureScreenshotWithFixedCamera] 保存完了: {path}");
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;

                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[CaptureScreenshotWithFixedCamera] State '{entry.fullPath}' の撮影に失敗: {e}");
        }
    }

    // ====== Aポーズリセット ======
    private void ResetToAPose()
    {
        if (targetObject == null) return;

        var animator = targetObject.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning("Target GameObject に Animator コンポーネントがありません。");
            return;
        }

        // Animatorを有効化
        animator.enabled = true;

        // すべてのパラメータをリセット
        var animatorController = GetAnimatorController();
        if (animatorController != null)
        {
            foreach (var param in animatorController.parameters)
            {
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        animator.SetFloat(param.name, param.defaultFloat);
                        break;
                    case AnimatorControllerParameterType.Int:
                        animator.SetInteger(param.name, param.defaultInt);
                        break;
                    case AnimatorControllerParameterType.Bool:
                        animator.SetBool(param.name, param.defaultBool);
                        break;
                    case AnimatorControllerParameterType.Trigger:
                        animator.ResetTrigger(param.name);
                        break;
                }
            }
        }

        // デフォルトStateに戻す
        if (animatorController != null && layerIndex >= 0 && layerIndex < animatorController.layers.Length)
        {
            var layer = animatorController.layers[layerIndex];
            var defaultState = layer.stateMachine.defaultState;

            if (defaultState != null)
            {
                animator.Play(defaultState.nameHash, layerIndex, 0f);
            }
            else
            {
                // デフォルトStateがない場合は、最初のStateを使用
                if (layer.stateMachine.states.Length > 0)
                {
                    animator.Play(layer.stateMachine.states[0].state.nameHash, layerIndex, 0f);
                }
            }
        }

        // Animatorを更新してStateを反映
        animator.Update(0f);

        // Scene Viewを更新
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

        Debug.Log($"[Aポーズリセット] Target GameObject '{targetObject.name}' をAポーズにリセットしました。");
    }

    private void ResetAllLayersToEntry()
    {
        Debug.Log("[ResetAllLayersToEntry] メソッド開始");

        if (targetObject == null)
        {
            Debug.LogWarning("[ResetAllLayersToEntry] targetObject が null です");
            return;
        }

        var animator = targetObject.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning("[ResetAllLayersToEntry] Target GameObject に Animator コンポーネントがありません。");
            return;
        }

        Debug.Log($"[ResetAllLayersToEntry] Animator 取得成功: {animator.name}");

        // Animatorを有効化
        animator.enabled = true;
        Debug.Log($"[ResetAllLayersToEntry] Animator.enabled = true に設定");

        var animatorController = GetAnimatorController();
        if (animatorController == null)
        {
            Debug.LogWarning("[ResetAllLayersToEntry] AnimatorController が取得できませんでした。");
            return;
        }

        Debug.Log($"[ResetAllLayersToEntry] AnimatorController 取得成功: {animatorController.name}");

        // すべてのパラメータをリセット
        foreach (var param in animatorController.parameters)
        {
            switch (param.type)
            {
                case AnimatorControllerParameterType.Float:
                    animator.SetFloat(param.name, param.defaultFloat);
                    break;
                case AnimatorControllerParameterType.Int:
                    animator.SetInteger(param.name, param.defaultInt);
                    break;
                case AnimatorControllerParameterType.Bool:
                    animator.SetBool(param.name, param.defaultBool);
                    break;
                case AnimatorControllerParameterType.Trigger:
                    animator.ResetTrigger(param.name);
                    break;
            }
        }

        // すべてのLayerをEntryに戻す
        int layerCount = animatorController.layers.Length;
        for (int i = 0; i < layerCount; i++)
        {
            var layer = animatorController.layers[i];
            var stateMachine = layer.stateMachine;

            // EntryからデフォルトStateへの遷移を再生
            if (stateMachine.defaultState != null)
            {
                animator.Play(stateMachine.defaultState.nameHash, i, 0f);
                Debug.Log($"[Layer {i} ({layer.name})] デフォルトState '{stateMachine.defaultState.name}' に遷移");
            }
            else if (stateMachine.states.Length > 0)
            {
                // デフォルトStateがない場合は最初のStateを使用
                animator.Play(stateMachine.states[0].state.nameHash, i, 0f);
                Debug.Log($"[Layer {i} ({layer.name})] 最初のState '{stateMachine.states[0].state.name}' に遷移");
            }
            else
            {
                Debug.LogWarning($"[Layer {i} ({layer.name})] Stateが見つかりませんでした。");
            }
        }

        // Animatorを更新してStateを反映（少し時間を進める）
        animator.Update(0.01f);

        // 再度更新して確実に反映
        animator.Update(0.01f);

        // Scene Viewを更新
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

        Debug.Log($"[全LayerをEntryに戻す] Target GameObject '{targetObject.name}' の全{layerCount}個のLayerをEntryに戻しました。");
    }

    // ====== カメラ設定 ======
    private void PreviewCameraPosition()
    {
        if (targetObject == null) return;

        // Scene ViewのカメラにGizmoを描画するため、SceneViewを再描画
        SceneView.RepaintAll();

        // 一時的なプレビュー用GameObjectを作成してカメラ位置を可視化
        var previewGO = GameObject.Find("_CameraPreview");
        if (previewGO == null)
        {
            previewGO = new GameObject("_CameraPreview");
        }

        // カメラ位置を計算
        Vector3 center;
        float size;

        if (targetRenderer != null)
        {
            Bounds bounds = targetRenderer.bounds;
            if (fitWholeBody)
            {
                var allRenderers = targetObject.GetComponentsInChildren<Renderer>(true);
                foreach (var r in allRenderers)
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            center = bounds.center;
            size = bounds.size.magnitude;
        }
        else
        {
            Bounds bounds = CalculateBoundsRecursive(targetObject);
            center = bounds.center;
            size = bounds.size.magnitude;
        }

        float dist = Mathf.Max(size, 1.5f) * cameraDistance;
        Vector3 basePosition = center + new Vector3(0, dist * 0.3f, -dist * 1.2f);
        Vector3 offsetPosition = basePosition + new Vector3(cameraOffset.x, cameraOffset.y, 0);

        previewGO.transform.position = offsetPosition;
        previewGO.transform.LookAt(center, Vector3.up);

        // Scene Viewをこの位置に移動
        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null)
        {
            sceneView.LookAt(center, Quaternion.LookRotation(center - offsetPosition), dist);
        }

        Debug.Log($"[カメラプレビュー] Position: {offsetPosition}, LookAt: {center}");
    }

    private void SetupCameraPosition(Camera cam)
    {
        Debug.Log($"[SetupCameraPosition] 開始 - Camera: {cam.name}, targetObject: {targetObject?.name}");

        if (cam == null)
        {
            Debug.LogError("[SetupCameraPosition] カメラがnullです");
            return;
        }

        if (targetObject == null)
        {
            Debug.LogError("[SetupCameraPosition] targetObjectがnullです");
            return;
        }

        // 対象のBoundsを取得
        Bounds bounds;
        if (targetRenderer != null)
        {
            Debug.Log($"[SetupCameraPosition] targetRendererを使用: {targetRenderer.name}");
            bounds = targetRenderer.bounds;
            if (fitWholeBody)
            {
                Debug.Log("[SetupCameraPosition] 全身を入れるモード");
                // 全身を入れる場合は、全Rendererを含める
                var allRenderers = targetObject.GetComponentsInChildren<Renderer>(true);
                Debug.Log($"[SetupCameraPosition] Renderer数: {allRenderers.Length}");
                foreach (var r in allRenderers)
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
        }
        else
        {
            Debug.Log("[SetupCameraPosition] CalculateBoundsRecursiveを使用");
            bounds = CalculateBoundsRecursive(targetObject);
        }

        Vector3 center = bounds.center;
        Vector3 size = bounds.size;
        Debug.Log($"[SetupCameraPosition] Bounds - Center: {center}, Size: {size}");

        // カメラの視野角から必要な距離を計算
        float fovY = cam.fieldOfView;
        float aspect = (float)captureWidth / captureHeight;
        Debug.Log($"[SetupCameraPosition] FOV: {fovY}°, Aspect: {aspect}");

        // カメラを少し上から見下ろす角度に調整
        float lookDownAngle = 15f;
        Quaternion cameraRotation = Quaternion.Euler(lookDownAngle, 0, 0);

        // カメラの方向ベクトル
        Vector3 forward = cameraRotation * Vector3.forward;
        Vector3 up = cameraRotation * Vector3.up;
        Vector3 right = Vector3.Cross(forward, up).normalized;

        // オブジェクトのサイズを考慮した距離計算
        // 垂直方向と水平方向の両方を考慮
        float verticalSize = size.y;
        float horizontalSize = Mathf.Max(size.x, size.z);

        // 垂直FOVから必要な距離を計算
        float halfFovYRad = fovY * 0.5f * Mathf.Deg2Rad;
        float distanceForVertical = (verticalSize * 0.5f) / Mathf.Tan(halfFovYRad);

        // 水平FOVから必要な距離を計算
        float fovX = Camera.VerticalToHorizontalFieldOfView(fovY, aspect);
        float halfFovXRad = fovX * 0.5f * Mathf.Deg2Rad;
        float distanceForHorizontal = (horizontalSize * 0.5f) / Mathf.Tan(halfFovXRad);

        Debug.Log($"[SetupCameraPosition] distanceForVertical: {distanceForVertical:F2}, distanceForHorizontal: {distanceForHorizontal:F2}");

        // より大きい方の距離を採用（両方の条件を満たすため）
        float requiredDistance = Mathf.Max(distanceForVertical, distanceForHorizontal);

        // 深度も考慮（Z方向のサイズの半分を追加）
        requiredDistance += size.z * 0.5f;

        // 安全マージンを追加（20%）
        requiredDistance *= 1.2f;

        // 距離倍率を適用
        requiredDistance *= cameraDistance;
        requiredDistance = Mathf.Max(requiredDistance, 1.0f); // 最低距離

        Debug.Log($"[SetupCameraPosition] 最終距離: {requiredDistance:F2}");

        // カメラ位置を計算（centerから後方に距離を取る）
        Vector3 basePosition = center - forward * requiredDistance;

        // オフセットを適用（カメラ座標系で）
        Vector3 offsetPosition = basePosition + right * cameraOffset.x + up * cameraOffset.y;

        Debug.Log($"[SetupCameraPosition] カメラ移動前 Position: {cam.transform.position}");
        cam.transform.position = offsetPosition;
        cam.transform.rotation = Quaternion.LookRotation(center - offsetPosition, Vector3.up);
        Debug.Log($"[SetupCameraPosition] カメラ移動後 Position: {cam.transform.position}, Rotation: {cam.transform.rotation.eulerAngles}");

        string rendererInfo = targetRenderer != null ? $"TargetRenderer: {targetRenderer.name}" : "全Renderer";
        Debug.Log($"[カメラ配置完了] {rendererInfo}, FitWholeBody: {fitWholeBody}, Center: {center}, CamPos: {offsetPosition}, Distance: {requiredDistance:F2}, BoundsSize: {size}, FOV: {fovY}°/{fovX:F1}°");
    }

    // ====== Utils ======
    private static Bounds CalculateBoundsRecursive(GameObject root)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    /// <summary>
    /// 保存先のディレクトリパスを取得
    /// Assets/Sprite/{AnimatorController名}/{Layer名}/ の構造で作成
    /// </summary>
    private string GetOutputDirectory()
    {
        // outputDirectoryが指定されていればそれを使用
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            return outputDirectory;
        }

        // AnimatorController名を取得
        var animatorController = GetAnimatorController();
        if (animatorController == null)
        {
            return null;
        }

        // Assets/Spriteフォルダが存在しない場合は作成
        string spriteFolderPath = Path.Combine(Application.dataPath, "Sprite");
        if (!Directory.Exists(spriteFolderPath))
        {
            Directory.CreateDirectory(spriteFolderPath);
            Debug.Log($"[GetOutputDirectory] Assets/Spriteフォルダを作成しました: {spriteFolderPath}");
        }

        string controllerName = animatorController.name;
        // 不正な文字を除去
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            controllerName = controllerName.Replace(c, '_');
        }

        // Layer名を取得
        string layerName = "Layer0";
        if (layerIndex >= 0 && layerIndex < animatorController.layers.Length)
        {
            layerName = animatorController.layers[layerIndex].name;
            // 不正な文字を除去
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                layerName = layerName.Replace(c, '_');
            }
        }

        // Assets/Sprite/{AnimatorController名}/{Layer名}
        string finalPath = Path.Combine(spriteFolderPath, controllerName, layerName);
        return finalPath;
    }

    /// <summary>
    /// outputDirectoryフィールドを自動的に更新する
    /// ウィンドウ起動時、Target変更時、Layer変更時に呼ばれる
    /// </summary>
    private void UpdateOutputDirectory()
    {
        // 既にoutputDirectoryが手動で指定されている場合は何もしない
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            return;
        }

        // GetOutputDirectory()を使って自動ディレクトリを取得
        string autoDirectory = GetOutputDirectory();
        if (!string.IsNullOrEmpty(autoDirectory))
        {
            outputDirectory = autoDirectory;
            Debug.Log($"[UpdateOutputDirectory] 出力先を自動設定: {outputDirectory}");
        }
    }
}
