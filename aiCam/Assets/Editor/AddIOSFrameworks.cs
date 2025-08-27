// aiCam / AddIOSFrameworks.cs
// Build PostProcess for iOS: Xcode プロジェクトへ必要フレームワークを追加します。
#if UNITY_EDITOR && UNITY_IOS
#nullable enable
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class AddIOSFrameworks
{
    // 遅めに実行して他のポストプロセスより後から上書き
    [PostProcessBuild(1000)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        var projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);

#if UNITY_2019_3_OR_NEWER
        string mainTarget      = proj.GetUnityMainTargetGuid();
        string frameworkTarget = proj.GetUnityFrameworkTargetGuid();
#else
        string mainTarget      = proj.TargetGuidByName("Unity-iPhone");
        string frameworkTarget = mainTarget;
#endif
        // 必要フレームワークを追加（false = Required）
        AddFramework(proj, mainTarget,      "Photos.framework");
        AddFramework(proj, frameworkTarget, "Photos.framework");
        AddFramework(proj, frameworkTarget, "AVFoundation.framework");

        proj.WriteToFile(projPath);
    }

    private static void AddFramework(PBXProject proj, string targetGuid, string name)
    {
        // 既に追加済みでも Xcode 側で重複は統合される
        proj.AddFrameworkToProject(targetGuid, name, /*weak*/ false);
    }
}
#endif
