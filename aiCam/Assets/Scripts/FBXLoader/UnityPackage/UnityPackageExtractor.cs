using System;
using System.IO;
using UnityEngine;
using Unity.SharpZipLib.GZip;
using Unity.SharpZipLib.Tar;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Unityパッケージ（.unitypackage）を解凍し、ファイル名を復元するクラス
    /// iOS対応: Application.persistentDataPathに解凍
    /// </summary>
    public class UnityPackageExtractor
    {
        /// <summary>
        /// 指定された.unitypackageファイルを解凍し、ファイル名を復元します
        /// </summary>
        /// <param name="unityPackagePath">解凍する.unitypackageファイルのパス</param>
        /// <param name="extractedFolderPath">解凍先のフォルダパス（空の場合はApplication.persistentDataPath/ExtractedUnityPackageを使用）</param>
        public void Extract(string unityPackagePath, string extractedFolderPath = "")
        {
            if (string.IsNullOrEmpty(unityPackagePath))
            {
                throw new ArgumentException("パスが無効です。", nameof(unityPackagePath));
            }

            if (!File.Exists(unityPackagePath))
            {
                throw new FileNotFoundException("指定されたファイルが見つかりません。", unityPackagePath);
            }

            // iOS対応: 解凍先がない場合はApplication.persistentDataPathを使用
            if (string.IsNullOrEmpty(extractedFolderPath))
            {
                extractedFolderPath = Path.Combine(Application.persistentDataPath, "ExtractedUnityPackage");
            }

            Debug.Log($"[UnityPackageExtractor] Extract to: {extractedFolderPath}");

            // 解凍先フォルダが存在しない場合は作成
            if (!Directory.Exists(extractedFolderPath))
            {
                Directory.CreateDirectory(extractedFolderPath);
            }

            // 一時ディレクトリを作成
            string tempDirectory = Path.Combine(extractedFolderPath, "temp_unitypackage");
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
            Directory.CreateDirectory(tempDirectory);

            Debug.Log($"[UnityPackageExtractor] Extracting unitypackage: {unityPackagePath}");

            // .unitypackageファイルを読み込み、gzipストリームとして解凍
            using (FileStream fs = new FileStream(unityPackagePath, FileMode.Open, FileAccess.Read))
            using (GZipInputStream gzipStream = new GZipInputStream(fs))
            using (TarInputStream tarStream = new TarInputStream(gzipStream))
            {
                TarEntry entry;

                // TARアーカイブ内の各エントリを処理
                while ((entry = tarStream.GetNextEntry()) != null)
                {
                    if (entry.IsDirectory)
                    {
                        // ディレクトリの場合はスキップ
                        continue;
                    }

                    // 解凍先のファイルパスを生成
                    string outPath = Path.Combine(tempDirectory, entry.Name);

                    // ディレクトリが存在しない場合は作成
                    string directoryName = Path.GetDirectoryName(outPath);
                    if (!Directory.Exists(directoryName))
                    {
                        Directory.CreateDirectory(directoryName);
                    }

                    // エントリの内容をファイルに書き込む
                    using (FileStream outStream = File.Create(outPath))
                    {
                        tarStream.CopyEntryContents(outStream);
                    }
                }
            }

            Debug.Log($"[UnityPackageExtractor] Tar extraction complete, restoring file structure...");

            // 解凍したファイルから元のファイル名とパスを復元
            int fileCount = 0;
            foreach (string guidDir in Directory.GetDirectories(tempDirectory))
            {
                string assetPathNameFile = Path.Combine(guidDir, "pathname");
                string assetFile = Path.Combine(guidDir, "asset");

                if (File.Exists(assetPathNameFile) && File.Exists(assetFile))
                {
                    // 元のパスを取得
                    string relativePath = File.ReadAllText(assetPathNameFile).Replace("\r", "").Replace("\n", "");

                    // 絶対パスを生成
                    string destinationPath = Path.Combine(extractedFolderPath, relativePath);

                    // セキュリティ対策として、解凍先のパスを検証
                    if (!destinationPath.StartsWith(extractedFolderPath, StringComparison.Ordinal))
                    {
                        Debug.LogWarning($"[UnityPackageExtractor] Invalid path detected, skipping: {relativePath}");
                        continue;
                    }

                    // ディレクトリが存在しない場合は作成
                    string destinationDir = Path.GetDirectoryName(destinationPath);
                    if (!Directory.Exists(destinationDir))
                    {
                        Directory.CreateDirectory(destinationDir);
                    }

                    // ファイルをコピー
                    File.Copy(assetFile, destinationPath, true);
                    fileCount++;

                    Debug.Log($"[UnityPackageExtractor] Restored: {relativePath}");
                }
            }

            // 一時ディレクトリを削除
            Directory.Delete(tempDirectory, true);

            Debug.Log($"[UnityPackageExtractor] Extraction complete! {fileCount} files restored to: {extractedFolderPath}");
        }
    }
}
