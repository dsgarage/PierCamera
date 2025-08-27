// aiCam / IOSPlistPostProcess.cs
// Build PostProcess for iOS: Info.plist に必須の UsageDescription を安全に追加し、
// 英日ローカライズ用 InfoPlist.strings を自動生成します。
#if UNITY_IOS
#nullable enable
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;

public static class iOSPlistPostprocess
{
    // 一番最後に動かして他のポストプロセスの変更を上書きしない
    [PostProcessBuild(int.MaxValue)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        // 1) Info.plist を編集
        string plistPath = Path.Combine(path, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        var root  = plist.root;

        // 既にキーが存在する場合は上書きしない（翻訳・ブランド文言の破壊を避ける）
        AddDefaultIfMissing(root, "NSCameraUsageDescription",
            "This app uses the camera for AR. / このアプリはAR表示のためにカメラを使用します。");
        AddDefaultIfMissing(root, "NSPhotoLibraryAddUsageDescription",
            "This app saves captured photos to your library. / 撮影した写真をフォトライブラリに保存します。");
        AddDefaultIfMissing(root, "NSMicrophoneUsageDescription",
            "This app uses the microphone when recording video. / 動画録画時にマイクを使用します。");

        // 開発言語 & ローカライズ対象
        root.SetString("CFBundleDevelopmentRegion", "en");
        var loc = root.CreateArray("CFBundleLocalizations");
        loc.AddString("en");
        loc.AddString("ja");

        File.WriteAllText(plistPath, plist.WriteToString());

        // 2) ローカライズ用 InfoPlist.strings を生成（英/日）
        // Xcodeは <lang>.lproj/InfoPlist.strings を自動認識
        WriteStrings(path, "en", new []
        {
            ("NSCameraUsageDescription",        "This app uses the camera for AR."),
            ("NSPhotoLibraryAddUsageDescription","This app saves captured photos to your Photo Library."),
            ("NSMicrophoneUsageDescription",    "This app uses the microphone when recording video.")
        });

        WriteStrings(path, "ja", new []
        {
            ("NSCameraUsageDescription",        "このアプリはAR表示のためにカメラを使用します"),
            ("NSPhotoLibraryAddUsageDescription","撮影した写真をフォトライブラリに保存します"),
            ("NSMicrophoneUsageDescription",    "動画録画時にマイクを使用します")
        });

        // 3) 生成した lproj フォルダを Xcode プロジェクトに追加（フォルダ参照）
        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);
#if UNITY_2019_3_OR_NEWER
        string mainTarget = proj.GetUnityMainTargetGuid();
        string frameworkTarget = proj.GetUnityFrameworkTargetGuid();
#else
        string mainTarget = proj.TargetGuidByName("Unity-iPhone");
        string frameworkTarget = mainTarget;
#endif
        AddFolderIfNeeded(proj, mainTarget, path, "en.lproj");
        AddFolderIfNeeded(proj, mainTarget, path, "ja.lproj");

        proj.WriteToFile(projPath);
        AssetDatabase.Refresh();
    }

    private static void AddDefaultIfMissing(PlistElementDict root, string key, string value)
    {
        if (!root.values.ContainsKey(key))
            root.SetString(key, value);
    }

    private static void WriteStrings(string projRoot, string lang, (string key, string val)[] pairs)
    {
        string dir = Path.Combine(projRoot, $"{lang}.lproj");
        Directory.CreateDirectory(dir);
        string fpath = Path.Combine(dir, "InfoPlist.strings");
        using (var sw = new StreamWriter(fpath, false, new System.Text.UTF8Encoding(false)))
        {
            foreach (var (key,val) in pairs)
                sw.WriteLine($"\"{key}\" = \"{Escape(val)}\";");
        }
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static void AddFolderIfNeeded(PBXProject proj, string targetGuid, string projRoot, string folderName)
    {
        string abs = Path.Combine(projRoot, folderName);
        if (!Directory.Exists(abs)) return;
        var guid = proj.AddFolderReference(abs, folderName);
        proj.AddFileToBuild(targetGuid, guid);
    }
}
#endif
