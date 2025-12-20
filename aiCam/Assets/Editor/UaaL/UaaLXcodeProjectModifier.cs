#if UNITY_IOS
using UnityEngine;
using UnityEditor;
using UnityEditor.iOS.Xcode;
using System.IO;

namespace AICam.Editor.UaaL
{
    /// <summary>
    /// RN側のXcodeプロジェクト設定を自動更新
    /// </summary>
    public static class UaaLXcodeProjectModifier
    {
        /// <summary>
        /// RNプロジェクトのXcode設定を更新
        /// </summary>
        public static bool ModifyRNXcodeProject(string rnProjectPath)
        {
            string xcodeProjectPath = Path.Combine(rnProjectPath, "ios/arCamRN.xcodeproj/project.pbxproj");

            if (!File.Exists(xcodeProjectPath))
            {
                Debug.LogError($"[UaaL] Xcode project not found: {xcodeProjectPath}");
                return false;
            }

            Debug.Log($"[UaaL] Modifying Xcode project: {xcodeProjectPath}");

            PBXProject pbxProject = new PBXProject();
            pbxProject.ReadFromFile(xcodeProjectPath);

            // メインターゲットのGUIDを取得
            string mainTargetGuid = pbxProject.GetUnityMainTargetGuid();
            string frameworkTargetGuid = pbxProject.GetUnityFrameworkTargetGuid();

            // RNアプリのターゲット名でGUIDを取得
            string appTargetGuid = pbxProject.TargetGuidByName("arCamRN");
            if (string.IsNullOrEmpty(appTargetGuid))
            {
                // 代替方法: 全ターゲットをリスト
                Debug.LogWarning("[UaaL] Could not find 'arCamRN' target, using main target");
                appTargetGuid = mainTargetGuid;
            }

            // 1. Framework Search Paths
            Debug.Log("[UaaL] Adding Framework Search Paths...");
            pbxProject.AddBuildProperty(appTargetGuid, "FRAMEWORK_SEARCH_PATHS", "$(PROJECT_DIR)/Frameworks");
            pbxProject.AddBuildProperty(appTargetGuid, "FRAMEWORK_SEARCH_PATHS", "$(inherited)");

            // 2. Other Linker Flags
            Debug.Log("[UaaL] Adding Other Linker Flags...");
            pbxProject.AddBuildProperty(appTargetGuid, "OTHER_LDFLAGS", "-ObjC");

            // 3. UnityFramework.frameworkの追加はXcodeで手動設定が必要
            // PBXProject APIではEmbed & Sign設定が完全にサポートされていないため
            string frameworkPath = Path.Combine(rnProjectPath, "ios/Frameworks/UnityFramework.framework");
            if (Directory.Exists(frameworkPath))
            {
                Debug.Log("[UaaL] UnityFramework.framework detected.");
                Debug.Log("[UaaL] ⚠️ Please manually add to Xcode: Embed & Sign");
            }

            // 4. Dataフォルダの追加もXcodeで手動設定が必要
            // フォルダ参照として追加する必要があるため
            string dataPath = Path.Combine(rnProjectPath, "ios/Data");
            if (Directory.Exists(dataPath))
            {
                Debug.Log("[UaaL] Data folder detected.");
                Debug.Log("[UaaL] ⚠️ Please manually add to Xcode: Create folder references");
            }

            // 5. Enable Bitcode = NO (UnityFrameworkとの互換性)
            Debug.Log("[UaaL] Disabling Bitcode...");
            pbxProject.SetBuildProperty(appTargetGuid, "ENABLE_BITCODE", "NO");

            // 保存
            pbxProject.WriteToFile(xcodeProjectPath);

            Debug.Log("[UaaL] Xcode project modification completed");
            return true;
        }

        /// <summary>
        /// Xcodeプロジェクトが正しく設定されているか検証
        /// </summary>
        public static bool ValidateRNXcodeProject(string rnProjectPath)
        {
            string xcodeProjectPath = Path.Combine(rnProjectPath, "ios/arCamRN.xcodeproj/project.pbxproj");

            if (!File.Exists(xcodeProjectPath))
            {
                return false;
            }

            string content = File.ReadAllText(xcodeProjectPath);

            bool hasFrameworkSearchPath = content.Contains("$(PROJECT_DIR)/Frameworks");
            bool hasObjCFlag = content.Contains("-ObjC");
            bool hasUnityFramework = content.Contains("UnityFramework.framework");

            return hasFrameworkSearchPath && hasObjCFlag;
        }
    }
}
#endif
