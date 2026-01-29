#if UNITY_IOS
using UnityEngine;
using UnityEditor;
using UnityEditor.Build.Reporting;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;

namespace AICam.Editor.UaaL
{
    /// <summary>
    /// UaaL統合管理ウィンドウ
    /// メニュー: Tools > UaaL > Integration Manager
    /// </summary>
    public class UaaLIntegrationWindow : EditorWindow
    {
        private string unityBuildPath = "";
        private string rnProjectPath = "";
        private Vector2 scrollPosition;
        private bool showAdvancedSettings = false;
        private bool showVersionSettings = true;

        // ステータス
        private bool frameworkExists = false;
        private bool dataFolderExists = false;
        private bool xcodeConfigured = false;

        // ビルドオプション
        private bool compressAfterBuild = false;

        // Version config
        private UaaLVersionConfig versionConfig;
        private List<string> availableMilestones = new List<string>();
        private int selectedMilestoneIndex = 0;
        private bool isFetchingMilestones = false;

        [MenuItem("Tools/UaaL/Integration Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<UaaLIntegrationWindow>("UaaL Integration");
            window.minSize = new Vector2(500, 400);
            window.Show();
        }

        private void OnEnable()
        {
            // デフォルトパスを設定
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            unityBuildPath = Path.Combine(projectRoot, "Builds/iOS");
            rnProjectPath = Path.GetFullPath(Path.Combine(projectRoot, "../arCamRN"));

            // Load version config
            versionConfig = UaaLVersionConfig.GetOrCreateConfig();

            RefreshStatus();
        }

        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Space(10);
            EditorGUILayout.LabelField("Unity as a Library Integration", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "このツールはUnityプロジェクトをReact Nativeプロジェクトに\nUaaLとして統合します。",
                MessageType.Info
            );

            GUILayout.Space(10);

            // バージョン設定
            DrawVersionSection();

            GUILayout.Space(10);

            // パス設定
            DrawPathSettings();

            GUILayout.Space(10);

            // ステータス表示
            DrawStatusSection();

            GUILayout.Space(10);

            // アクションボタン
            DrawActionButtons();

            GUILayout.Space(10);

            // 詳細設定
            DrawAdvancedSettings();

            EditorGUILayout.EndScrollView();
        }

        private void DrawVersionSection()
        {
            showVersionSettings = EditorGUILayout.Foldout(showVersionSettings, "Version Settings", true);

            if (!showVersionSettings) return;

            EditorGUILayout.BeginVertical("box");

            if (versionConfig == null)
            {
                versionConfig = UaaLVersionConfig.GetOrCreateConfig();
            }

            // Current version display
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Current Version:", GUILayout.Width(120));
            EditorGUILayout.LabelField(versionConfig.GetFullVersionString(), EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            // Marketing version (editable)
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Marketing Version:", GUILayout.Width(120));
            string newVersion = EditorGUILayout.TextField(versionConfig.version);
            if (newVersion != versionConfig.version)
            {
                versionConfig.version = newVersion;
                EditorUtility.SetDirty(versionConfig);
            }
            EditorGUILayout.EndHorizontal();

            // Build number
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Build Number:", GUILayout.Width(120));
            int newBuildNumber = EditorGUILayout.IntField(versionConfig.buildNumber);
            if (newBuildNumber != versionConfig.buildNumber)
            {
                versionConfig.buildNumber = newBuildNumber;
                EditorUtility.SetDirty(versionConfig);
            }
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            // GitHub milestone sync
            EditorGUILayout.LabelField("GitHub Milestone Sync", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Repository:", GUILayout.Width(120));
            versionConfig.githubRepo = EditorGUILayout.TextField(versionConfig.githubRepo);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (availableMilestones.Count > 0)
            {
                EditorGUILayout.LabelField("Milestone:", GUILayout.Width(120));
                selectedMilestoneIndex = EditorGUILayout.Popup(selectedMilestoneIndex, availableMilestones.ToArray());

                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    if (selectedMilestoneIndex >= 0 && selectedMilestoneIndex < availableMilestones.Count)
                    {
                        versionConfig.SetVersionFromMilestone(availableMilestones[selectedMilestoneIndex]);
                    }
                }
            }
            else
            {
                EditorGUILayout.LabelField("Milestone:", GUILayout.Width(120));
                EditorGUILayout.LabelField(string.IsNullOrEmpty(versionConfig.currentMilestone)
                    ? "(Click Fetch to load)"
                    : versionConfig.currentMilestone);
            }

            GUI.enabled = !isFetchingMilestones;
            if (GUILayout.Button(isFetchingMilestones ? "..." : "Fetch", GUILayout.Width(60)))
            {
                FetchGitHubMilestones();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(5);

            // Last build info
            if (!string.IsNullOrEmpty(versionConfig.lastBuildTime))
            {
                EditorGUILayout.LabelField($"Last Build: {versionConfig.lastBuildTime}", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
        }

        private void FetchGitHubMilestones()
        {
            if (string.IsNullOrEmpty(versionConfig.githubRepo))
            {
                EditorUtility.DisplayDialog("Error", "Please set GitHub repository first.", "OK");
                return;
            }

            isFetchingMilestones = true;
            availableMilestones.Clear();

            try
            {
                // gh CLIのパスを探す
                string ghPath = "/opt/homebrew/bin/gh";
                if (!File.Exists(ghPath))
                {
                    ghPath = "/usr/local/bin/gh";
                }
                if (!File.Exists(ghPath))
                {
                    // PATHから探す
                    ghPath = "gh";
                }

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = ghPath,
                        Arguments = $"api repos/{versionConfig.githubRepo}/milestones --jq \".[].title\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    var lines = output.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        string trimmed = line.Trim();
                        if (!string.IsNullOrEmpty(trimmed))
                        {
                            availableMilestones.Add(trimmed);
                        }
                    }

                    // Find current milestone index
                    if (!string.IsNullOrEmpty(versionConfig.currentMilestone))
                    {
                        selectedMilestoneIndex = availableMilestones.IndexOf(versionConfig.currentMilestone);
                        if (selectedMilestoneIndex < 0) selectedMilestoneIndex = 0;
                    }

                    UnityEngine.Debug.Log($"[UaaL] Fetched {availableMilestones.Count} milestones from GitHub");
                }
                else
                {
                    if (!string.IsNullOrEmpty(error))
                    {
                        UnityEngine.Debug.LogWarning($"[UaaL] GitHub CLI error: {error}");
                    }
                    EditorUtility.DisplayDialog("Error", "Failed to fetch milestones.\n\nMake sure:\n1. GitHub CLI (gh) is installed\n2. You are authenticated (gh auth login)\n3. Repository name is correct", "OK");
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[UaaL] Failed to fetch milestones: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to run GitHub CLI:\n{e.Message}", "OK");
            }
            finally
            {
                isFetchingMilestones = false;
                Repaint();
            }
        }

        private void DrawPathSettings()
        {
            EditorGUILayout.LabelField("Path Settings", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            unityBuildPath = EditorGUILayout.TextField("Unity Build Path", unityBuildPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("Select Unity Build Folder", unityBuildPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    unityBuildPath = path;
                    RefreshStatus();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            rnProjectPath = EditorGUILayout.TextField("RN Project Path", rnProjectPath);
            if (GUILayout.Button("Browse", GUILayout.Width(60)))
            {
                string path = EditorUtility.OpenFolderPanel("Select RN Project Folder", rnProjectPath, "");
                if (!string.IsNullOrEmpty(path))
                {
                    rnProjectPath = path;
                    RefreshStatus();
                }
            }
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button("Refresh Status"))
            {
                RefreshStatus();
            }
        }

        private void DrawStatusSection()
        {
            EditorGUILayout.LabelField("Integration Status", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");

            // Unity Build
            DrawStatusRow("Unity Build", Directory.Exists(unityBuildPath) && File.Exists(Path.Combine(unityBuildPath, "Unity-iPhone.xcodeproj/project.pbxproj")));

            // RN Project
            DrawStatusRow("RN Project", Directory.Exists(rnProjectPath) && File.Exists(Path.Combine(rnProjectPath, "package.json")));

            EditorGUILayout.EndVertical();

            GUILayout.Space(5);

            // Framework詳細ステータス
            EditorGUILayout.LabelField("Framework Status (詳細)", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            DrawFrameworkDetailStatus();
            EditorGUILayout.EndVertical();

            GUILayout.Space(5);

            // Data folder詳細
            EditorGUILayout.LabelField("Data Folder Status", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            DrawDataFolderStatus();
            EditorGUILayout.EndVertical();

            GUILayout.Space(5);

            // Xcode Config
            EditorGUILayout.BeginVertical("box");
            xcodeConfigured = UaaLXcodeProjectModifier.ValidateRNXcodeProject(rnProjectPath);
            DrawStatusRow("Xcode Configuration", xcodeConfigured);
            EditorGUILayout.EndVertical();
        }

        private void DrawFrameworkDetailStatus()
        {
            // ビルド出力先
            string buildFrameworkPath = Path.Combine(unityBuildPath, "build/Release-iphoneos/UnityFramework.framework");
            string buildBinaryPath = Path.Combine(buildFrameworkPath, "UnityFramework");

            // RNプロジェクト内
            string rnFrameworkPath = Path.Combine(rnProjectPath, "ios/Frameworks/UnityFramework.framework");
            string rnBinaryPath = Path.Combine(rnFrameworkPath, "UnityFramework");

            EditorGUILayout.LabelField("【ビルド出力】", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  Path: {buildFrameworkPath}", EditorStyles.miniLabel);

            if (Directory.Exists(buildFrameworkPath))
            {
                if (File.Exists(buildBinaryPath))
                {
                    var fileInfo = new FileInfo(buildBinaryPath);
                    string size = FormatFileSize(fileInfo.Length);
                    string date = fileInfo.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss");
                    DrawStatusRowWithDetail("  Binary", true, $"{size}, {date}");
                }
                else
                {
                    DrawStatusRowWithDetail("  Binary", false, "❌ バイナリなし（xcodebuild未実行）");
                }
            }
            else
            {
                DrawStatusRowWithDetail("  Framework", false, "❌ フォルダなし");
            }

            GUILayout.Space(5);

            EditorGUILayout.LabelField("【RNプロジェクト】", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  Path: {rnFrameworkPath}", EditorStyles.miniLabel);

            frameworkExists = Directory.Exists(rnFrameworkPath);
            if (frameworkExists)
            {
                if (File.Exists(rnBinaryPath))
                {
                    var fileInfo = new FileInfo(rnBinaryPath);
                    string size = FormatFileSize(fileInfo.Length);
                    string date = fileInfo.LastWriteTime.ToString("yyyy/MM/dd HH:mm:ss");
                    DrawStatusRowWithDetail("  Binary", true, $"{size}, {date}");

                    // ビルド出力と比較
                    if (File.Exists(buildBinaryPath))
                    {
                        var buildInfo = new FileInfo(buildBinaryPath);
                        if (fileInfo.Length == buildInfo.Length)
                        {
                            DrawStatusRowWithDetail("  同期状態", true, "ビルド出力と一致");
                        }
                        else
                        {
                            DrawStatusRowWithDetail("  同期状態", false, $"サイズ不一致（要再コピー）");
                        }
                    }
                }
                else
                {
                    DrawStatusRowWithDetail("  Binary", false, "❌ バイナリなし（コピー不完全）");
                }
            }
            else
            {
                DrawStatusRowWithDetail("  Framework", false, "❌ 未コピー");
            }
        }

        private void DrawDataFolderStatus()
        {
            string buildDataPath = Path.Combine(unityBuildPath, "Data");
            string rnDataPath = Path.Combine(rnProjectPath, "ios/Data");

            // ビルド出力
            if (Directory.Exists(buildDataPath))
            {
                int fileCount = Directory.GetFiles(buildDataPath, "*", SearchOption.AllDirectories).Length;
                DrawStatusRowWithDetail("ビルド出力", true, $"{fileCount} files");
            }
            else
            {
                DrawStatusRowWithDetail("ビルド出力", false, "❌ なし");
            }

            // RNプロジェクト
            dataFolderExists = Directory.Exists(rnDataPath);
            if (dataFolderExists)
            {
                int fileCount = Directory.GetFiles(rnDataPath, "*", SearchOption.AllDirectories).Length;
                string bootConfig = File.Exists(Path.Combine(rnDataPath, "boot.config")) ? "✓" : "✗";
                string globalGameManagers = File.Exists(Path.Combine(rnDataPath, "globalgamemanagers")) ? "✓" : "✗";
                DrawStatusRowWithDetail("RNプロジェクト", true, $"{fileCount} files");
                EditorGUILayout.LabelField($"    boot.config: {bootConfig}, globalgamemanagers: {globalGameManagers}", EditorStyles.miniLabel);
            }
            else
            {
                DrawStatusRowWithDetail("RNプロジェクト", false, "❌ 未コピー");
            }
        }

        private void DrawStatusRow(string label, bool status)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(200));

            GUIStyle style = new GUIStyle(EditorStyles.label);
            style.normal.textColor = status ? Color.green : Color.red;
            EditorGUILayout.LabelField(status ? "✅ OK" : "❌ Missing", style);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatusRowWithDetail(string label, bool status, string detail)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(120));

            GUIStyle statusStyle = new GUIStyle(EditorStyles.label);
            statusStyle.normal.textColor = status ? Color.green : Color.red;
            EditorGUILayout.LabelField(status ? "✅" : "❌", statusStyle, GUILayout.Width(25));

            EditorGUILayout.LabelField(detail, EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            return $"{size:0.##} {sizes[order]}";
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginVertical("box");

            // Step 1: Build Unity
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("1. Build Unity for iOS", GUILayout.Width(200));
            GUI.backgroundColor = new Color(0.4f, 0.7f, 1.0f);
            if (GUILayout.Button("Release", GUILayout.Width(70)))
            {
                BuildUnityForiOS(isDebug: false);
            }
            GUI.backgroundColor = new Color(1.0f, 0.7f, 0.4f);
            if (GUILayout.Button("Debug", GUILayout.Width(70)))
            {
                BuildUnityForiOS(isDebug: true);
            }
            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("Settings", GUILayout.Width(60)))
            {
                EditorWindow.GetWindow(System.Type.GetType("UnityEditor.BuildPlayerWindow,UnityEditor"));
            }
            EditorGUILayout.EndHorizontal();

            // Zip compression option
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(204);
            compressAfterBuild = EditorGUILayout.ToggleLeft("ビルド後にZip圧縮する", compressAfterBuild);
            EditorGUILayout.EndHorizontal();

            // Step 2: Build UnityFramework
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("2. Build UnityFramework", GUILayout.Width(200));
            GUI.enabled = Directory.Exists(unityBuildPath);
            if (GUILayout.Button("Build Framework"))
            {
                BuildUnityFramework();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // Step 3: Copy to RN
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("3. Copy to RN Project", GUILayout.Width(200));
            GUI.enabled = Directory.Exists(unityBuildPath) && Directory.Exists(rnProjectPath);
            if (GUILayout.Button("Copy Files"))
            {
                CopyFilesToRN();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // Step 4: Configure Xcode
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("4. Configure Xcode Project", GUILayout.Width(200));
            GUI.enabled = frameworkExists;
            if (GUILayout.Button("Configure"))
            {
                ConfigureXcodeProject();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            GUILayout.Space(10);

            // One-click integration
            GUI.enabled = Directory.Exists(unityBuildPath) && Directory.Exists(rnProjectPath);
            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            if (GUILayout.Button("🚀 Full Integration (Steps 2-4)", GUILayout.Height(40)))
            {
                FullIntegration();
            }
            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            EditorGUILayout.EndVertical();
        }

        private void DrawAdvancedSettings()
        {
            showAdvancedSettings = EditorGUILayout.Foldout(showAdvancedSettings, "Advanced Settings", true);

            if (showAdvancedSettings)
            {
                EditorGUILayout.BeginVertical("box");

                if (GUILayout.Button("Open Unity Build Folder"))
                {
                    if (Directory.Exists(unityBuildPath))
                        EditorUtility.RevealInFinder(unityBuildPath);
                }

                if (GUILayout.Button("Open RN Project Folder"))
                {
                    if (Directory.Exists(rnProjectPath))
                        EditorUtility.RevealInFinder(rnProjectPath);
                }

                if (GUILayout.Button("Open RN Xcode Workspace"))
                {
                    string workspacePath = Path.Combine(rnProjectPath, "ios/arCamRN.xcworkspace");
                    if (Directory.Exists(workspacePath))
                    {
                        Process.Start("open", $"\"{workspacePath}\"");
                    }
                }

                GUILayout.Space(10);

                if (GUILayout.Button("Clean Integration (Remove copied files)"))
                {
                    if (EditorUtility.DisplayDialog("Clean Integration",
                        "This will remove UnityFramework.framework and Data folder from RN project. Continue?",
                        "Yes", "No"))
                    {
                        CleanIntegration();
                    }
                }

                EditorGUILayout.EndVertical();
            }
        }

        private void RefreshStatus()
        {
            Repaint();
        }

        private void BuildUnityForiOS(bool isDebug = false)
        {
            // ビルドパスを確認
            if (string.IsNullOrEmpty(unityBuildPath))
            {
                EditorUtility.DisplayDialog("Error", "Please set Unity Build Path first.", "OK");
                return;
            }

            // プラットフォーム確認
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                if (EditorUtility.DisplayDialog("Switch Platform",
                    "Current platform is not iOS.\n\nSwitch to iOS platform first?",
                    "Switch", "Cancel"))
                {
                    EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS);
                    EditorUtility.DisplayDialog("Platform Switched",
                        "Switched to iOS platform.\n\nPlease click 'Build iOS' again after Unity finishes recompiling.",
                        "OK");
                }
                return;
            }

            string buildType = isDebug ? "Debug" : "Release";

            // バージョン情報を表示
            string versionInfo = versionConfig != null
                ? $"Version: {versionConfig.version}\nBuild: {versionConfig.buildNumber} → {versionConfig.buildNumber + 1}"
                : "";

            string zipInfo = compressAfterBuild ? "\n✅ ビルド後にZip圧縮します" : "";

            // ビルド確認
            if (!EditorUtility.DisplayDialog($"Build iOS ({buildType})",
                $"Build Unity project for iOS?\n\n" +
                $"Configuration: {buildType}\n" +
                $"Output: {unityBuildPath}\n" +
                $"{versionInfo}{zipInfo}\n\n" +
                "⚠️ Unity Editor will freeze during build.\n" +
                "This may take 5-10 minutes.",
                "Build", "Cancel"))
            {
                return;
            }

            // ビルド番号をインクリメント
            if (versionConfig != null)
            {
                versionConfig.IncrementBuildNumber();
                // PlayerSettingsにも反映
                PlayerSettings.bundleVersion = versionConfig.GetMarketingVersion();
                PlayerSettings.iOS.buildNumber = versionConfig.GetBuildVersion();
                UnityEngine.Debug.Log($"[UaaL] Version updated: {versionConfig.GetFullVersionString()}");
            }

            // ビルドフォルダを作成
            if (!Directory.Exists(unityBuildPath))
            {
                Directory.CreateDirectory(unityBuildPath);
            }

            UnityEngine.Debug.Log($"[UaaL] Starting iOS build ({buildType}): {unityBuildPath}");

            // ビルドオプション
            BuildOptions options = BuildOptions.ShowBuiltPlayer;
            if (isDebug)
            {
                options |= BuildOptions.Development;
                options |= BuildOptions.AllowDebugging;
            }

            BuildPlayerOptions buildOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = unityBuildPath,
                target = BuildTarget.iOS,
                options = options
            };

            // ビルド実行（同期処理、Editorはフリーズする）
            var report = BuildPipeline.BuildPlayer(buildOptions);

            if (report.summary.result == BuildResult.Succeeded)
            {
                UnityEngine.Debug.Log($"[UaaL] iOS build succeeded ({buildType}): {unityBuildPath}");
                RefreshStatus();

                // Zip圧縮
                string zipPath = null;
                if (compressAfterBuild)
                {
                    zipPath = CompressBuildFolder(buildType);
                }

                string zipMessage = zipPath != null ? $"\n\nZip: {zipPath}" : "";
                EditorUtility.DisplayDialog("Build Succeeded",
                    $"iOS build completed! ({buildType})\n\nOutput: {unityBuildPath}{zipMessage}\n\n" +
                    "You can now proceed with Step 2 (Build UnityFramework).",
                    "OK");
            }
            else
            {
                UnityEngine.Debug.LogError($"[UaaL] iOS build failed: {report.summary.result}");
                EditorUtility.DisplayDialog("Build Failed",
                    $"iOS build failed.\n\nResult: {report.summary.result}\n\n" +
                    "Check Console for details.",
                    "OK");
            }
        }

        private string CompressBuildFolder(string buildType)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Compressing", "Creating zip archive...", 0.5f);

                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string version = versionConfig != null ? versionConfig.GetFullVersionString() : "unknown";
                string zipFileName = $"iOS_{buildType}_{version}_{timestamp}.zip";
                string zipPath = Path.Combine(Path.GetDirectoryName(unityBuildPath), zipFileName);

                // zipコマンドを使用（macOS）
                string script = $@"
cd ""{Path.GetDirectoryName(unityBuildPath)}""
zip -r -q ""{zipFileName}"" ""{Path.GetFileName(unityBuildPath)}""
";
                ExecuteShellCommand(script);

                if (File.Exists(zipPath))
                {
                    var fileInfo = new FileInfo(zipPath);
                    UnityEngine.Debug.Log($"[UaaL] Created zip: {zipPath} ({FormatFileSize(fileInfo.Length)})");
                    return zipPath;
                }
                else
                {
                    UnityEngine.Debug.LogWarning("[UaaL] Zip file was not created");
                    return null;
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[UaaL] Failed to compress: {e.Message}");
                return null;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private string[] GetEnabledScenes()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                {
                    scenes.Add(scene.path);
                }
            }
            return scenes.ToArray();
        }

        /// <summary>
        /// 確認ダイアログなしでiOSビルド（Full Integration用）
        /// </summary>
        private bool BuildUnityForiOSSilent(bool isDebug = false)
        {
            if (string.IsNullOrEmpty(unityBuildPath))
            {
                UnityEngine.Debug.LogError("[UaaL] Unity Build Path is not set");
                return false;
            }

            // プラットフォーム確認
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.iOS)
            {
                EditorUtility.DisplayDialog("Platform Error",
                    "Current platform is not iOS.\n\n" +
                    "Please switch to iOS platform first:\n" +
                    "File → Build Settings → iOS → Switch Platform",
                    "OK");
                return false;
            }

            string buildType = isDebug ? "Debug" : "Release";

            // ビルド番号をインクリメント
            if (versionConfig != null)
            {
                versionConfig.IncrementBuildNumber();
                PlayerSettings.bundleVersion = versionConfig.GetMarketingVersion();
                PlayerSettings.iOS.buildNumber = versionConfig.GetBuildVersion();
                UnityEngine.Debug.Log($"[UaaL] Version updated: {versionConfig.GetFullVersionString()}");
            }

            if (!Directory.Exists(unityBuildPath))
            {
                Directory.CreateDirectory(unityBuildPath);
            }

            UnityEngine.Debug.Log($"[UaaL] Starting iOS build (silent, {buildType}): {unityBuildPath}");

            BuildOptions options = BuildOptions.None;
            if (isDebug)
            {
                options |= BuildOptions.Development;
                options |= BuildOptions.AllowDebugging;
            }

            BuildPlayerOptions buildOptions = new BuildPlayerOptions
            {
                scenes = GetEnabledScenes(),
                locationPathName = unityBuildPath,
                target = BuildTarget.iOS,
                options = options
            };

            var report = BuildPipeline.BuildPlayer(buildOptions);

            if (report.summary.result == BuildResult.Succeeded)
            {
                UnityEngine.Debug.Log($"[UaaL] iOS build succeeded ({buildType}): {unityBuildPath}");

                // Zip圧縮
                if (compressAfterBuild)
                {
                    CompressBuildFolder(buildType);
                }

                return true;
            }
            else
            {
                UnityEngine.Debug.LogError($"[UaaL] iOS build failed: {report.summary.result}");
                EditorUtility.DisplayDialog("Build Failed",
                    $"iOS build failed: {report.summary.result}\n\nCheck Console for details.",
                    "OK");
                return false;
            }
        }

        private void BuildUnityFramework()
        {
            EditorUtility.DisplayProgressBar("Building UnityFramework", "This may take a few minutes...", 0.5f);

            try
            {
                string script = $@"
cd ""{unityBuildPath}""
xcodebuild -project Unity-iPhone.xcodeproj \
    -scheme UnityFramework \
    -configuration Release \
    -sdk iphoneos \
    ONLY_ACTIVE_ARCH=NO \
    BUILD_DIR=""{unityBuildPath}/build""
";
                ExecuteShellCommand(script);
                RefreshStatus();

                EditorUtility.DisplayDialog("Build Complete", "UnityFramework build completed.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void CopyFilesToRN()
        {
            EditorUtility.DisplayProgressBar("Copying Files", "Copying UnityFramework and Data...", 0.5f);

            try
            {
                string frameworksDir = Path.Combine(rnProjectPath, "ios/Frameworks");
                Directory.CreateDirectory(frameworksDir);

                // Framework コピー
                string[] frameworkPaths = {
                    Path.Combine(unityBuildPath, "build/Release-iphoneos/UnityFramework.framework"),
                    Path.Combine(unityBuildPath, "Products/Release-iphoneos/UnityFramework.framework")
                };

                string frameworkSource = null;
                foreach (var path in frameworkPaths)
                {
                    if (Directory.Exists(path))
                    {
                        frameworkSource = path;
                        break;
                    }
                }

                if (frameworkSource != null)
                {
                    string dest = Path.Combine(frameworksDir, "UnityFramework.framework");
                    if (Directory.Exists(dest))
                        Directory.Delete(dest, true);

                    CopyDirectory(frameworkSource, dest);
                    UnityEngine.Debug.Log($"[UaaL] Copied framework to: {dest}");
                }
                else
                {
                    EditorUtility.DisplayDialog("Warning", "UnityFramework.framework not found. Please build it first.", "OK");
                }

                // Data フォルダコピー
                string dataSource = Path.Combine(unityBuildPath, "Data");
                if (Directory.Exists(dataSource))
                {
                    string dataDest = Path.Combine(rnProjectPath, "ios/Data");
                    if (Directory.Exists(dataDest))
                        Directory.Delete(dataDest, true);

                    CopyDirectory(dataSource, dataDest);
                    UnityEngine.Debug.Log($"[UaaL] Copied Data folder to: {dataDest}");
                }

                RefreshStatus();
                EditorUtility.DisplayDialog("Copy Complete", "Files copied successfully.", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void ConfigureXcodeProject()
        {
            EditorUtility.DisplayProgressBar("Configuring Xcode", "Updating project settings...", 0.5f);

            try
            {
                // 基本設定をPBXProject APIで更新
                bool basicSuccess = UaaLXcodeProjectModifier.ModifyRNXcodeProject(rnProjectPath);

                // Ruby xcodeproj gemでEmbed Framework設定を試行
                bool rubySuccess = TryRunRubyXcodeScript();

                RefreshStatus();

                if (rubySuccess)
                {
                    EditorUtility.DisplayDialog("Configuration Complete",
                        "Xcode project fully configured!\n\n" +
                        "✅ Framework Search Paths\n" +
                        "✅ Other Linker Flags (-ObjC)\n" +
                        "✅ UnityFramework.framework (Embed & Sign)\n" +
                        "✅ Data folder (folder reference)",
                        "OK");
                }
                else if (basicSuccess)
                {
                    EditorUtility.DisplayDialog("Partial Configuration",
                        "Basic Xcode settings configured.\n\n" +
                        "⚠️ Please manually configure in Xcode:\n" +
                        "1. Add UnityFramework.framework → Embed & Sign\n" +
                        "2. Add Data folder → Create folder references\n\n" +
                        "Tip: Install 'gem install xcodeproj' for full automation.",
                        "OK");
                }
                else
                {
                    EditorUtility.DisplayDialog("Configuration Failed",
                        "Failed to configure Xcode project. Please configure manually.",
                        "OK");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private bool TryRunRubyXcodeScript()
        {
            // Rubyスクリプトのパス
            string scriptPath = Path.Combine(Application.dataPath, "Editor/UaaL/xcode_embed_framework.rb");

            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.Log("[UaaL] Ruby script not found, skipping xcodeproj automation");
                return false;
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/usr/bin/ruby",
                        Arguments = $"\"{scriptPath}\" \"{rnProjectPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                    UnityEngine.Debug.Log($"[UaaL Ruby] {output}");

                if (process.ExitCode == 0)
                {
                    UnityEngine.Debug.Log("[UaaL] Ruby xcodeproj script completed successfully");
                    return true;
                }
                else
                {
                    if (!string.IsNullOrEmpty(error))
                        UnityEngine.Debug.LogWarning($"[UaaL Ruby] {error}");
                    return false;
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.Log($"[UaaL] Ruby script failed (gem may not be installed): {e.Message}");
                return false;
            }
        }

        private void FullIntegration()
        {
            // Unity Buildが必要かどうか確認
            bool needsUnityBuild = !Directory.Exists(unityBuildPath) ||
                                   !File.Exists(Path.Combine(unityBuildPath, "Unity-iPhone.xcodeproj/project.pbxproj"));

            string message = needsUnityBuild
                ? "This will:\n" +
                  "1. Build Unity for iOS\n" +
                  "2. Build UnityFramework\n" +
                  "3. Copy files to RN project\n" +
                  "4. Configure Xcode project\n\n" +
                  "This may take 10-15 minutes. Continue?"
                : "This will:\n" +
                  "1. Build UnityFramework\n" +
                  "2. Copy files to RN project\n" +
                  "3. Configure Xcode project\n\n" +
                  "This may take several minutes. Continue?";

            if (!EditorUtility.DisplayDialog("Full Integration", message, "Yes", "No"))
            {
                return;
            }

            // Unity Buildが必要な場合は実行
            if (needsUnityBuild)
            {
                BuildUnityForiOSSilent();
            }

            BuildUnityFramework();
            CopyFilesToRN();
            ConfigureXcodeProject();

            EditorUtility.DisplayDialog("Integration Complete",
                "UaaL integration completed!\n\n" +
                "Next steps:\n" +
                "1. Open arCamRN.xcworkspace in Xcode\n" +
                "2. Verify UnityFramework is Embed & Sign\n" +
                "3. Build and run the app",
                "OK");
        }

        private void CleanIntegration()
        {
            string frameworkPath = Path.Combine(rnProjectPath, "ios/Frameworks/UnityFramework.framework");
            string dataPath = Path.Combine(rnProjectPath, "ios/Data");

            if (Directory.Exists(frameworkPath))
                Directory.Delete(frameworkPath, true);

            if (Directory.Exists(dataPath))
                Directory.Delete(dataPath, true);

            RefreshStatus();
            UnityEngine.Debug.Log("[UaaL] Integration cleaned");
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
                CopyDirectory(dir, destSubDir);
            }
        }

        private static void ExecuteShellCommand(string script)
        {
            string tempScript = Path.GetTempFileName() + ".sh";
            File.WriteAllText(tempScript, script);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/bash",
                    Arguments = tempScript,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (!string.IsNullOrEmpty(output))
                UnityEngine.Debug.Log($"[UaaL] {output}");

            if (!string.IsNullOrEmpty(error))
                UnityEngine.Debug.LogWarning($"[UaaL] {error}");

            File.Delete(tempScript);
        }
    }
}
#endif
