using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Unity.SharpZipLib.GZip;
using Unity.SharpZipLib.Tar;
using UniSIL.ShaderInference;
using UniSIL.ShaderInference.TextureLoading;
using UniSIL.ShaderInference.MaterialLoading;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Unityパッケージ（.unitypackage）を解凍し、ファイル名を復元するクラス
    /// iOS対応: Application.persistentDataPathに解凍
    /// UniSIL統合: 解凍時にTextureManifestとMaterialManifestを自動生成
    /// </summary>
    public class UnityPackageExtractor
    {
        /// <summary>
        /// 解凍進捗ログのコールバック
        /// </summary>
        public System.Action<string> OnExtractionLog;

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

            string logMsg = $"[Extract] Starting extraction to: {extractedFolderPath}";
            Debug.Log(logMsg);
            OnExtractionLog?.Invoke(logMsg);

            // 解凍先フォルダが存在しない場合は作成
            if (!Directory.Exists(extractedFolderPath))
            {
                Directory.CreateDirectory(extractedFolderPath);
                logMsg = $"[Extract] Created output directory";
                Debug.Log(logMsg);
                OnExtractionLog?.Invoke(logMsg);
            }

            // 一時ディレクトリを作成
            string tempDirectory = Path.Combine(extractedFolderPath, "temp_unitypackage");
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
            Directory.CreateDirectory(tempDirectory);

            logMsg = $"[Extract] Reading tar archive...";
            Debug.Log(logMsg);
            OnExtractionLog?.Invoke(logMsg);

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

            logMsg = $"[Extract] Tar extraction complete, restoring file structure...";
            Debug.Log(logMsg);
            OnExtractionLog?.Invoke(logMsg);

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

            logMsg = $"[Extract] ✓ Extraction complete! {fileCount} files restored";
            Debug.Log(logMsg);
            OnExtractionLog?.Invoke(logMsg);

            // UniSIL統合: Manifestを生成
            GenerateManifests(extractedFolderPath);
        }

        /// <summary>
        /// 解凍されたディレクトリからTextureManifestとMaterialManifestを生成
        /// </summary>
        /// <param name="extractedFolderPath">解凍先のフォルダパス</param>
        private void GenerateManifests(string extractedFolderPath)
        {
            try
            {
                string logMsg = $"[Manifest] Generating manifests...";
                Debug.Log(logMsg);
                OnExtractionLog?.Invoke(logMsg);

                // TextureManifest生成
                GenerateTextureManifest(extractedFolderPath);

                // MaterialManifest生成
                GenerateMaterialManifest(extractedFolderPath);

                logMsg = $"[Manifest] ✓ Manifest generation complete!";
                Debug.Log(logMsg);
                OnExtractionLog?.Invoke(logMsg);
            }
            catch (Exception ex)
            {
                string errorMsg = $"[Manifest] ✗ Failed to generate manifests: {ex.Message}";
                Debug.LogWarning(errorMsg);
                OnExtractionLog?.Invoke(errorMsg);
            }
        }

        /// <summary>
        /// TextureManifestを生成（解凍先フォルダ直下に1つ保存）
        /// </summary>
        private void GenerateTextureManifest(string extractedFolderPath)
        {
            string logMsg = $"[Manifest] Scanning textures...";
            Debug.Log(logMsg);
            OnExtractionLog?.Invoke(logMsg);

            // 解凍先フォルダ全体を対象にTextureManifestを生成
            var manifest = RuntimeTextureManifestBuilder.BuildManifest(
                textureDirectory: extractedFolderPath,
                outputDirectory: extractedFolderPath
            );

            if (manifest != null && manifest.textureCount > 0)
            {
                logMsg = $"[Manifest] ✓ TextureManifest: {manifest.textureCount} textures";
                Debug.Log(logMsg);
                OnExtractionLog?.Invoke(logMsg);
            }
            else
            {
                logMsg = $"[Manifest] No textures found";
                Debug.Log(logMsg);
                OnExtractionLog?.Invoke(logMsg);
            }
        }

        /// <summary>
        /// MaterialManifestを生成（解凍先フォルダ直下に1つ保存）
        /// </summary>
        private void GenerateMaterialManifest(string extractedFolderPath)
        {
            string logMsg = $"[Manifest] Scanning materials...";
            Debug.Log(logMsg);
            OnExtractionLog?.Invoke(logMsg);

            // 解凍先フォルダ全体を対象にMaterialManifestを生成
            var manifest = RuntimeMaterialManifestBuilder.BuildManifest(
                materialDirectory: extractedFolderPath,
                outputDirectory: extractedFolderPath
            );

            if (manifest != null && manifest.materialCount > 0)
            {
                logMsg = $"[Manifest] ✓ MaterialManifest: {manifest.materialCount} materials";
                Debug.Log(logMsg);
                OnExtractionLog?.Invoke(logMsg);

                // シェーダー名を解決（ShaderGuidDictionaryを使用）
                var shaderGuidDict = ShaderGuidDictionaryLoader.LoadDictionary();
                if (shaderGuidDict != null)
                {
                    RuntimeMaterialManifestBuilder.ResolveShaderNames(manifest, shaderGuidDict);

                    // シェーダー名が解決されたManifestを再保存
                    string manifestPath = Path.Combine(extractedFolderPath, "MaterialManifest.json");
                    string json = JsonUtility.ToJson(manifest, prettyPrint: true);
                    File.WriteAllText(manifestPath, json);

                    logMsg = $"[Manifest] Shader names resolved and saved";
                    Debug.Log(logMsg);
                    OnExtractionLog?.Invoke(logMsg);
                }
                else
                {
                    logMsg = $"[Manifest] Warning: ShaderGuidDictionary not found, shader names will be 'Unknown'";
                    Debug.LogWarning(logMsg);
                    OnExtractionLog?.Invoke(logMsg);
                }
            }
            else
            {
                logMsg = $"[Manifest] No materials found";
                Debug.Log(logMsg);
                OnExtractionLog?.Invoke(logMsg);
            }
        }

        /// <summary>
        /// テクスチャディレクトリを検索
        /// </summary>
        private List<string> FindTextureDirectories(string rootPath)
        {
            List<string> directories = new List<string>();

            // 一般的なテクスチャディレクトリ名
            string[] commonNames = { "Texture", "Textures", "Material", "Materials", "Images" };

            foreach (string commonName in commonNames)
            {
                string[] foundDirs = Directory.GetDirectories(rootPath, commonName, SearchOption.AllDirectories);
                directories.AddRange(foundDirs);
            }

            // テクスチャファイル（.png, .jpg, .jpeg）が直接含まれているディレクトリも追加
            string[] imageExtensions = { "*.png", "*.jpg", "*.jpeg" };
            foreach (string ext in imageExtensions)
            {
                string[] imageFiles = Directory.GetFiles(rootPath, ext, SearchOption.AllDirectories);
                if (imageFiles.Length > 0)
                {
                    // ルートディレクトリ自体にテクスチャがある場合
                    if (!directories.Contains(rootPath))
                    {
                        directories.Add(rootPath);
                    }
                    break;
                }
            }

            return directories;
        }

        /// <summary>
        /// マテリアルディレクトリを検索
        /// </summary>
        private List<string> FindMaterialDirectories(string rootPath)
        {
            List<string> directories = new List<string>();

            // 一般的なマテリアルディレクトリ名
            string[] commonNames = { "Material", "Materials" };

            foreach (string commonName in commonNames)
            {
                string[] foundDirs = Directory.GetDirectories(rootPath, commonName, SearchOption.AllDirectories);
                directories.AddRange(foundDirs);
            }

            // .matファイルが直接含まれているディレクトリも追加
            string[] matFiles = Directory.GetFiles(rootPath, "*.mat", SearchOption.AllDirectories);
            if (matFiles.Length > 0)
            {
                // ルートディレクトリ自体にマテリアルがある場合
                if (!directories.Contains(rootPath))
                {
                    directories.Add(rootPath);
                }

                // 各.matファイルの親ディレクトリを追加
                foreach (string matFile in matFiles)
                {
                    string parentDir = Path.GetDirectoryName(matFile);
                    if (!directories.Contains(parentDir))
                    {
                        directories.Add(parentDir);
                    }
                }
            }

            return directories;
        }
    }
}
