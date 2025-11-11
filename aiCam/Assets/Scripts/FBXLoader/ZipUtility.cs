using System.IO;
using System.IO.Compression;
using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// ZIP展開ユーティリティ
    /// .NET標準のSystem.IO.Compression.ZipFileを使用
    /// </summary>
    public static class ZipUtility
    {
        /// <summary>
        /// ZIPファイルを指定フォルダに展開
        /// </summary>
        /// <param name="zipPath">ZIPファイルのパス</param>
        /// <param name="outputFolder">展開先フォルダ</param>
        public static void Extract(string zipPath, string outputFolder)
        {
            if (!File.Exists(zipPath))
            {
                Debug.LogError($"[ZipUtility] ZIP file not found: {zipPath}");
                return;
            }

            if (!Directory.Exists(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            Debug.Log($"[ZipUtility] Extracting: {zipPath}");
            Debug.Log($"[ZipUtility] To: {outputFolder}");

            try
            {
                // .NET標準のZipFileを使用
                ZipFile.ExtractToDirectory(zipPath, outputFolder);

                Debug.Log($"[ZipUtility] Extraction complete");

                // 展開されたファイル一覧をログ出力
                string[] files = Directory.GetFiles(outputFolder, "*.*", SearchOption.AllDirectories);
                Debug.Log($"[ZipUtility] Extracted {files.Length} files:");

                foreach (string file in files)
                {
                    Debug.Log($"  - {Path.GetFileName(file)}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ZipUtility] Extraction failed: {e.Message}");
                Debug.LogException(e);
                throw;
            }
        }

        /// <summary>
        /// ZIP内のファイル一覧を取得（展開せずに確認）
        /// </summary>
        public static string[] GetFileList(string zipPath)
        {
            if (!File.Exists(zipPath))
            {
                Debug.LogError($"[ZipUtility] ZIP file not found: {zipPath}");
                return new string[0];
            }

            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    string[] fileNames = new string[archive.Entries.Count];
                    for (int i = 0; i < archive.Entries.Count; i++)
                    {
                        fileNames[i] = archive.Entries[i].FullName;
                    }
                    return fileNames;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ZipUtility] Failed to read ZIP: {e.Message}");
                return new string[0];
            }
        }
    }
}
