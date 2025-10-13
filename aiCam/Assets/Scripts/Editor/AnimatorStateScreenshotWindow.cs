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
    [SerializeField] private bool fitWholeBody = true; // 全身を入れる
    [SerializeField] private float cameraDistance = 2.0f; // カメラ距離倍率
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
    [SerializeField] private bool openFolderAfterSave = true;

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
            _debugLastSwitchTime = currentTime;
            SwitchToNextDebugState();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("対象", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        targetObject = (GameObject)EditorGUILayout.ObjectField("Target GameObject", targetObject, typeof(GameObject), true);
        if (EditorGUI.EndChangeCheck())
        {
            // Target変更時にStateリストを更新
            TryBuildStateList();
        }

        // AnimatorControllerを表示（読み取り専用）
        var animatorController = GetAnimatorController();
        EditorGUI.BeginDisabledGroup(true);
        EditorGUILayout.ObjectField("Animator Controller", animatorController, typeof(AnimatorController), false);
        EditorGUI.EndDisabledGroup();

        int newLayer = EditorGUILayout.IntField("Layer Index", layerIndex);
        if (newLayer != layerIndex)
        {
            layerIndex = newLayer;
            TryBuildStateList();
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
        DrawDebugModeUI();

        EditorGUILayout.Space();
        DrawStateListUI();

        EditorGUILayout.Space(8);
        using (new EditorGUI.DisabledScope(!CanRun()))
        {
            if (GUILayout.Button("全Stateをスクリーンショット保存", GUILayout.Height(32)))
            {
                ExecuteCapture(allStates: true);
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "• Animator を再生せず、各 State の Motion（AnimationClip/BlendTree）を Editor の AnimationMode で直接サンプルしてポーズを当てた後に撮影します。\n" +
            "• 表情（ブレンドシェイプ）、ボーン、その他のカーブは Clip に入っている値がそのまま反映されます。\n" +
            "• BlendTree は現在「既定値（全パラメータ=0）」から辿れる最初の Clip をサンプルします。必要なら UI でパラメータ注入を拡張可能です。",
            MessageType.Info);
    }

    private void DrawDebugModeUI()
    {
        EditorGUILayout.LabelField("デバッグモード", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            debugMode = EditorGUILayout.Toggle("デバッグモード有効", debugMode);

            if (debugMode)
            {
                EditorGUI.BeginDisabledGroup(_isDebugRunning);
                debugSwitchInterval = EditorGUILayout.FloatField("切替間隔(秒)", Mathf.Max(0.5f, debugSwitchInterval));
                EditorGUI.EndDisabledGroup();
            }
        }

        if (debugMode)
        {
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
                    string buttonLabel = debugAutoCapture ? "デバッグ開始（自動撮影）" : "デバッグ開始（自動切替）";
                    if (GUILayout.Button(buttonLabel, GUILayout.Height(28)))
                    {
                        StartDebugMode();
                    }
                }
                else
                {
                    if (GUILayout.Button("デバッグ停止", GUILayout.Height(28)))
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
            if (newSelected != _selectedIndex) _selectedIndex = newSelected;

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
        if (animatorController == null) return;
        if (layerIndex < 0 || layerIndex >= animatorController.layers.Length) return;

        var layer = animatorController.layers[layerIndex];
        CollectStatesRecursive(layer.name, layer.stateMachine, _entries);

        _entryLabels = new string[_entries.Count];
        for (int i = 0; i < _entries.Count; i++)
        {
            _entryLabels[i] = _entries[i].fullPath;
        }
        if (_entries.Count > 0) _selectedIndex = 0;
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
        if (!CanRun())
        {
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

        // カメラ準備
        Camera cam = captureCamera;
        bool createdTempCamera = false;
        if (cam == null)
        {
            createdTempCamera = true;
            var go = new GameObject("Temp_CaptureCamera");
            cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
            cam.orthographic = false;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = 1000f;

            // カメラ位置を設定
            SetupCameraPosition(cam);
        }
        else
        {
            if (cam.clearFlags == CameraClearFlags.Skybox && transparentBackground)
                cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = backgroundColor;
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
                    // 1) ポーズ適用
                    if (!ApplyStatePoseBySampling(targetObject, entry, normalizedTime))
                    {
                        Debug.LogWarning($"State '{entry.fullPath}'：サンプルできる AnimationClip が見つかりませんでした。スキップします。");
                        continue;
                    }

                    // 2) レンダリング
                    RenderTexture.active = rt;
                    if (transparentBackground)
                    {
                        GL.PushMatrix();
                        GL.LoadPixelMatrix(0, captureWidth, captureHeight, 0);
                        GL.Clear(true, true, backgroundColor);
                        GL.PopMatrix();
                    }
                    cam.Render();

                    // 3) PNG保存
                    var tex = new Texture2D(captureWidth, captureHeight, TextureFormat.RGBA32, false, false);
                    tex.ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0, false);
                    tex.Apply(false, false);
                    var png = tex.EncodeToPNG();
                    UnityEngine.Object.DestroyImmediate(tex);

                    string fname = $"{filePrefix}{entry.FileSafeName(includeSubStatePathInFileName)}.png";
                    string path = Path.Combine(outputDirectory, fname);
                    File.WriteAllBytes(path, png);

                    success++;
                    Debug.Log($"Saved: {path}");
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

            animator.enabled = prevAnimatorEnabled;

            if (createdTempCamera && captureCamera == null && cam != null)
            {
                UnityEngine.Object.DestroyImmediate(cam.gameObject);
            }

            AssetDatabase.Refresh();
        }

        EditorUtility.DisplayDialog("完了", $"スクリーンショット完了\n成功: {success}\n失敗: {fail}\n出力先: {outputDirectory}", "OK");
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
        _debugCurrentIndex = 0;
        _debugCaptureCount = 0;
        _debugLastSwitchTime = EditorApplication.timeSinceStartup;

        // 最初のStateを適用
        SwitchToNextDebugState();

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
                if (_debugCaptureCount > 0 && openFolderAfterSave)
                {
                    EditorUtility.RevealInFinder(outputDirectory);
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
        if (_entries.Count == 0 || targetObject == null) return;

        var animator = targetObject.GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("Target GameObject に Animator コンポーネントがありません。");
            StopDebugMode();
            return;
        }

        var entry = _entries[_debugCurrentIndex];

        // AnimatorのStateを実際に切り替える
        if (entry.state != null)
        {
            // Animatorを有効化
            bool wasEnabled = animator.enabled;
            if (!wasEnabled) animator.enabled = true;

            // Stateを再生
            animator.Play(entry.state.nameHash, layerIndex, normalizedTime);

            // 少し更新してStateを反映
            animator.Update(0.01f);

            Debug.Log($"[デバッグ] State切替: {_debugCurrentIndex + 1}/{_entries.Count} - {entry.fullPath} (Hash: {entry.state.nameHash})");

            // 自動撮影が有効な場合、スクリーンショットを撮影
            if (debugAutoCapture)
            {
                CaptureScreenshot(entry);
            }
        }
        else
        {
            Debug.LogWarning($"State '{entry.fullPath}'：Stateオブジェクトが見つかりません。");
        }

        // 次のインデックスへ
        _debugCurrentIndex++;

        // 全State撮影完了チェック
        if (_debugCurrentIndex >= _entries.Count)
        {
            Debug.Log($"[デバッグ] 全{_entries.Count}個のState切り替え完了。自動停止します。");
            StopDebugMode();
            return;
        }

        // Scene Viewを更新
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

        // UIを再描画
        Repaint();
    }

    private void CaptureScreenshot(StateEntry entry)
    {
        try
        {
            // カメラ準備
            Camera cam = captureCamera;
            bool createdTempCamera = false;
            if (cam == null)
            {
                createdTempCamera = true;
                var go = new GameObject("Temp_DebugCaptureCamera");
                cam = go.AddComponent<Camera>();
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = backgroundColor;
                cam.orthographic = false;
                cam.nearClipPlane = 0.01f;
                cam.farClipPlane = 1000f;

                // カメラ位置を設定
                SetupCameraPosition(cam);
            }

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

                string fname = $"{filePrefix}{entry.FileSafeName(includeSubStatePathInFileName)}.png";
                string path = Path.Combine(outputDirectory, fname);
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

                if (createdTempCamera && cam != null)
                {
                    UnityEngine.Object.DestroyImmediate(cam.gameObject);
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[デバッグ撮影] State '{entry.fullPath}' の撮影に失敗: {e}");
        }
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
        Vector3 center;
        float size;

        if (targetRenderer != null)
        {
            // TargetRendererを基準にする
            Bounds bounds = targetRenderer.bounds;

            if (fitWholeBody)
            {
                // 全身を入れる場合は、全Rendererを含める
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
            // TargetRendererが未指定の場合は従来通り
            Bounds bounds = CalculateBoundsRecursive(targetObject);
            center = bounds.center;
            size = bounds.size.magnitude;
        }

        // カメラ距離を計算
        float dist = Mathf.Max(size, 1.5f) * cameraDistance;

        // カメラ位置（正面やや上から）+ オフセット
        Vector3 basePosition = center + new Vector3(0, dist * 0.3f, -dist * 1.2f);
        Vector3 offsetPosition = basePosition + new Vector3(cameraOffset.x, cameraOffset.y, 0);

        cam.transform.position = offsetPosition;
        cam.transform.LookAt(center, Vector3.up);

        Debug.Log($"[カメラ配置] Center: {center}, Distance: {dist}, Size: {size}, Offset: {cameraOffset}");
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
}
