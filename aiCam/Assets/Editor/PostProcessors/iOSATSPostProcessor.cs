using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.IO;

namespace AICam.Editor.Build
{
    /// <summary>
    /// iOSビルド後にInfo.plistを編集してApp Transport Security (ATS)の例外を追加
    /// HTTP接続を許可するために必要
    /// </summary>
    public static class iOSATSPostProcessor
    {
        [PostProcessBuild(100)]
        public static void OnPostProcessBuild(BuildTarget buildTarget, string pathToBuiltProject)
        {
            if (buildTarget != BuildTarget.iOS)
                return;

            string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
            PlistDocument plist = new PlistDocument();
            plist.ReadFromFile(plistPath);

            // NSAppTransportSecurity設定を追加
            PlistElementDict rootDict = plist.root;

            // NSAppTransportSecurityキーを取得または作成
            PlistElementDict atsDict;
            if (rootDict.values.ContainsKey("NSAppTransportSecurity"))
            {
                atsDict = rootDict["NSAppTransportSecurity"].AsDict();
            }
            else
            {
                atsDict = rootDict.CreateDict("NSAppTransportSecurity");
            }

            // 特定ドメインの例外を設定（セキュリティのため全許可ではなく特定ドメインのみ）
            PlistElementDict exceptionDomains;
            if (atsDict.values.ContainsKey("NSExceptionDomains"))
            {
                exceptionDomains = atsDict["NSExceptionDomains"].AsDict();
            }
            else
            {
                exceptionDomains = atsDict.CreateDict("NSExceptionDomains");
            }

            // テレメトリサーバーのドメイン例外を追加
            // 153.126.176.139 (IPアドレス直接指定)
            AddDomainException(exceptionDomains, "153.126.176.139");

            // 将来のドメイン名用（必要に応じて追加）
            // AddDomainException(exceptionDomains, "your-telemetry-server.com");

            plist.WriteToFile(plistPath);

            UnityEngine.Debug.Log("[iOSATSPostProcessor] Added ATS exception for telemetry server (153.126.176.139)");
        }

        private static void AddDomainException(PlistElementDict exceptionDomains, string domain)
        {
            PlistElementDict domainDict;
            if (exceptionDomains.values.ContainsKey(domain))
            {
                domainDict = exceptionDomains[domain].AsDict();
            }
            else
            {
                domainDict = exceptionDomains.CreateDict(domain);
            }

            // HTTP接続を許可
            domainDict.SetBoolean("NSExceptionAllowsInsecureHTTPLoads", true);

            // サブドメインにも適用（IPアドレスの場合は不要だが念のため）
            domainDict.SetBoolean("NSIncludesSubdomains", true);

            // 最小TLSバージョンを指定しない（HTTP許可のため）
            // domainDict.SetString("NSExceptionMinimumTLSVersion", "TLSv1.2");
        }
    }
}
