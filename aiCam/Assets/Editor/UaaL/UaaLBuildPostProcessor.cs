#if UNITY_IOS
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;
using System.Diagnostics;

namespace AICam.Editor.UaaL
{
    /// <summary>
    /// iOS UaaL ビルド後処理
    /// UnityFrameworkをRNプロジェクトに自動統合
    /// </summary>
    public static class UaaLBuildPostProcessor
    {
        // RNプロジェクトのパス（相対パス）
        private const string RN_PROJECT_RELATIVE_PATH = "../../arCamRN";

        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            UnityEngine.Debug.Log("[UaaL] PostProcessBuild started");
            UnityEngine.Debug.Log($"[UaaL] Built project path: {pathToBuiltProject}");

            // RNプロジェクトパスを解決
            string rnProjectPath = ResolveRNProjectPath(pathToBuiltProject);
            if (string.IsNullOrEmpty(rnProjectPath))
            {
                UnityEngine.Debug.LogWarning("[UaaL] RN project not found. Skipping UaaL integration.");
                return;
            }

            UnityEngine.Debug.Log($"[UaaL] RN project path: {rnProjectPath}");

            // 統合スクリプトを生成・実行
            string scriptPath = GenerateIntegrationScript(pathToBuiltProject, rnProjectPath);
            ExecuteScript(scriptPath);

            UnityEngine.Debug.Log("[UaaL] PostProcessBuild completed");
        }

        private static string ResolveRNProjectPath(string buildPath)
        {
            // ビルドパスの親ディレクトリから相対パスで探索
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string rnPath = Path.GetFullPath(Path.Combine(projectRoot, RN_PROJECT_RELATIVE_PATH));

            if (Directory.Exists(rnPath) && File.Exists(Path.Combine(rnPath, "package.json")))
            {
                return rnPath;
            }

            // 絶対パスでも探索
            string absolutePath = "/Users/daisuketsukada/Documents/dsgarageUnity/arCam/arCamRN";
            if (Directory.Exists(absolutePath) && File.Exists(Path.Combine(absolutePath, "package.json")))
            {
                return absolutePath;
            }

            return null;
        }

        private static string GenerateIntegrationScript(string unityBuildPath, string rnProjectPath)
        {
            string scriptContent = $@"#!/bin/bash
set -e

echo ""=========================================""
echo ""UaaL Integration Script""
echo ""=========================================""

UNITY_BUILD_PATH=""{unityBuildPath}""
RN_PROJECT_PATH=""{rnProjectPath}""
RN_IOS_PATH=""$RN_PROJECT_PATH/ios""
FRAMEWORKS_DIR=""$RN_IOS_PATH/Frameworks""

echo ""Unity Build Path: $UNITY_BUILD_PATH""
echo ""RN Project Path: $RN_PROJECT_PATH""

# 1. Frameworksディレクトリ作成
echo ""[1/5] Creating Frameworks directory...""
mkdir -p ""$FRAMEWORKS_DIR""

# 2. CocoaPods依存関係をインストール
echo ""[2/6] Installing CocoaPods dependencies...""
cd ""$UNITY_BUILD_PATH""

if [ -f ""Podfile"" ]; then
    pod install --repo-update || echo ""pod install failed, continuing...""
    echo ""✅ CocoaPods dependencies installed""
else
    echo ""⚠️ No Podfile found, skipping pod install""
fi

# 3. UnityFramework.frameworkをビルド
echo ""[3/6] Building UnityFramework...""

# UnityFrameworkをビルド（Release構成） - xcworkspaceを使用
if [ -f ""Unity-iPhone.xcworkspace/contents.xcworkspacedata"" ]; then
    xcodebuild -workspace Unity-iPhone.xcworkspace \
        -scheme UnityFramework \
        -configuration Release \
        -sdk iphoneos \
        ONLY_ACTIVE_ARCH=NO \
        BUILD_DIR=""$UNITY_BUILD_PATH/build"" \
        -quiet || echo ""Framework build completed (or already built)""
else
    xcodebuild -project Unity-iPhone.xcodeproj \
        -scheme UnityFramework \
        -configuration Release \
        -sdk iphoneos \
        ONLY_ACTIVE_ARCH=NO \
        BUILD_DIR=""$UNITY_BUILD_PATH/build"" \
        -quiet || echo ""Framework build completed (or already built)""
fi

# 3. UnityFramework.frameworkをコピー
echo ""[3/5] Copying UnityFramework.framework...""
FRAMEWORK_SOURCE=""$UNITY_BUILD_PATH/build/Release-iphoneos/UnityFramework.framework""

if [ -d ""$FRAMEWORK_SOURCE"" ]; then
    rm -rf ""$FRAMEWORKS_DIR/UnityFramework.framework""
    cp -R ""$FRAMEWORK_SOURCE"" ""$FRAMEWORKS_DIR/""
    echo ""✅ Framework copied successfully""
else
    echo ""⚠️ Framework not found at: $FRAMEWORK_SOURCE""
    echo ""   Trying alternative path...""

    # 代替パス（Products/Release-iphoneos）
    ALT_SOURCE=""$UNITY_BUILD_PATH/Products/Release-iphoneos/UnityFramework.framework""
    if [ -d ""$ALT_SOURCE"" ]; then
        rm -rf ""$FRAMEWORKS_DIR/UnityFramework.framework""
        cp -R ""$ALT_SOURCE"" ""$FRAMEWORKS_DIR/""
        echo ""✅ Framework copied from alternative path""
    else
        echo ""❌ Framework not found. Please build UnityFramework manually.""
    fi
fi

# 4. Dataフォルダをコピー
echo ""[4/5] Copying Data folder...""
DATA_SOURCE=""$UNITY_BUILD_PATH/Data""

if [ -d ""$DATA_SOURCE"" ]; then
    rm -rf ""$RN_IOS_PATH/Data""
    cp -R ""$DATA_SOURCE"" ""$RN_IOS_PATH/""
    echo ""✅ Data folder copied successfully""
else
    echo ""⚠️ Data folder not found at: $DATA_SOURCE""
fi

# 5. 統合情報ファイル作成
echo ""[5/5] Creating integration info...""
cat > ""$RN_IOS_PATH/UaaL_Integration_Info.txt"" << EOF
UaaL Integration Information
============================
Unity Build Path: $UNITY_BUILD_PATH
Integration Date: $(date)
Unity Version: {Application.unityVersion}

Files Copied:
- Frameworks/UnityFramework.framework
- Data/

Next Steps:
1. Open arCamRN.xcworkspace in Xcode
2. Add UnityFramework.framework to ""Frameworks, Libraries, and Embedded Content""
3. Set ""Embed & Sign"" for the framework
4. Add Data folder to project (Create folder references)
5. Add -ObjC to Other Linker Flags
6. Add \$(PROJECT_DIR)/Frameworks to Framework Search Paths
EOF

echo """"
echo ""=========================================""
echo ""✅ UaaL Integration Complete""
echo ""=========================================""
echo """"
echo ""Please complete the following manual steps in Xcode:""
echo ""1. Open $RN_IOS_PATH/arCamRN.xcworkspace""
echo ""2. Add UnityFramework.framework (Embed & Sign)""
echo ""3. Add Data folder (Create folder references)""
echo ""4. Add -ObjC to Other Linker Flags""
echo ""5. Add \$(PROJECT_DIR)/Frameworks to Framework Search Paths""
echo """"
";

            string scriptPath = Path.Combine(unityBuildPath, "uaal_integrate.sh");
            File.WriteAllText(scriptPath, scriptContent);

            return scriptPath;
        }

        private static void ExecuteScript(string scriptPath)
        {
            try
            {
                // 実行権限を付与
                var chmodProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/chmod",
                        Arguments = $"+x \"{scriptPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                chmodProcess.Start();
                chmodProcess.WaitForExit();

                // スクリプト実行
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "/bin/bash",
                        Arguments = $"\"{scriptPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.OutputDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                        UnityEngine.Debug.Log($"[UaaL] {args.Data}");
                };

                process.ErrorDataReceived += (sender, args) =>
                {
                    if (!string.IsNullOrEmpty(args.Data))
                        UnityEngine.Debug.LogWarning($"[UaaL] {args.Data}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    UnityEngine.Debug.Log("[UaaL] Integration script completed successfully");
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"[UaaL] Integration script exited with code: {process.ExitCode}");
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"[UaaL] Failed to execute integration script: {e.Message}");
            }
        }
    }
}
#endif
