using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

/// <summary>
/// Xcodeプロジェクトから-ld64フラグを自動的に除去するPostprocessスクリプト
/// iOS 17以降では-ld64が非推奨のため、ビルドエラーを回避します
/// </summary>
public class RemoveLd64LinkerFlag
{
    [PostProcessBuild(999)] // 他のPostprocessの後に実行
    public static void OnPostprocessBuild(BuildTarget buildTarget, string pathToBuiltProject)
    {
        // iOSビルドの場合のみ実行
        if (buildTarget != BuildTarget.iOS)
            return;

        Debug.Log("[RemoveLd64LinkerFlag] Starting Xcode project modification...");

        string projectPath = pathToBuiltProject + "/Unity-iPhone.xcodeproj/project.pbxproj";

        // PBXProjectを読み込み
        PBXProject project = new PBXProject();
        project.ReadFromFile(projectPath);

        // メインターゲット(Unity-iPhone)のGUIDを取得
        string mainTargetGuid = project.GetUnityMainTargetGuid();
        string frameworkTargetGuid = project.GetUnityFrameworkTargetGuid();

        bool modified = false;

        // Unity-iPhone ターゲットのOther Linker Flagsから-ld64を除去
        modified |= RemoveLd64FromTarget(project, mainTargetGuid, "Unity-iPhone");

        // UnityFramework ターゲットのOther Linker Flagsから-ld64を除去
        modified |= RemoveLd64FromTarget(project, frameworkTargetGuid, "UnityFramework");

        if (modified)
        {
            // 変更を保存
            project.WriteToFile(projectPath);
            Debug.Log("[RemoveLd64LinkerFlag] Successfully removed -ld64 flags from Xcode project!");
        }
        else
        {
            Debug.Log("[RemoveLd64LinkerFlag] No -ld64 flags found. Project already clean.");
        }
    }

    /// <summary>
    /// 指定されたターゲットのOther Linker Flagsから-ld64を除去
    /// </summary>
    private static bool RemoveLd64FromTarget(PBXProject project, string targetGuid, string targetName)
    {
        bool modified = false;

        // Other Linker Flagsを取得
        var flags = project.GetBuildPropertyForAnyConfig(targetGuid, "OTHER_LDFLAGS");

        if (!string.IsNullOrEmpty(flags))
        {
            // -ld64が含まれているか確認
            if (flags.Contains("-ld64"))
            {
                Debug.Log($"[RemoveLd64LinkerFlag] Found -ld64 in {targetName}");
                Debug.Log($"  Before: {flags}");

                // -ld64を除去（複数のパターンに対応）
                string newFlags = flags
                    .Replace("-ld64", "")
                    .Replace("  ", " ") // 連続するスペースを1つに
                    .Trim();

                Debug.Log($"  After:  {newFlags}");

                // 新しい値を設定
                if (string.IsNullOrEmpty(newFlags))
                {
                    // フラグが空になった場合は削除
                    project.SetBuildProperty(targetGuid, "OTHER_LDFLAGS", "");
                }
                else
                {
                    project.SetBuildProperty(targetGuid, "OTHER_LDFLAGS", newFlags);
                }

                modified = true;
            }
        }

        if (!modified)
        {
            Debug.Log($"[RemoveLd64LinkerFlag] No -ld64 flags found in {targetName}");
        }

        return modified;
    }
}
