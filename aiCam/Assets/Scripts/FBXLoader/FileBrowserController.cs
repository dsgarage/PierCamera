using UnityEngine;
using System;
using System.IO;

namespace AICam.FBXLoader
{
    /// <summary>
    /// NativeFilePickerを使用したファイル選択とZIP/UnityPackage展開を管理
    /// 対応形式: VRM, FBX, ZIP, UnityPackage
    /// macOS/Windows: ~/Downloads/に展開
    /// iOS: Application.persistentDataPathに展開（書き込み制限のため）
    /// </summary>
    public class FileBrowserController : MonoBehaviour
    {
        public string SelectedPath { get; private set; }
        public string ExtractedFolderPath { get; private set; }

        /// <summary>
        /// プラットフォーム別の解凍先ベースパスを取得
        /// </summary>
        private string GetExtractBasePath()
        {
#if UNITY_IOS && !UNITY_EDITOR
            // iOS: 書き込み制限があるためpersistentDataPathを使用
            return Application.persistentDataPath;
#else
            // macOS/Windows/Editor: ダウンロードフォルダを使用
            string userProfile = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
            return Path.Combine(userProfile, "Downloads");
#endif
        }

        private Action<bool, string> onFileSelectedCallback;
        private Action<bool, string> onExtractCompleteCallback;

        /// <summary>
        /// ファイルピッカーを開く
        /// </summary>
        public void OpenFilePicker(Action<bool, string> onComplete)
        {
            onFileSelectedCallback = onComplete;

            Debug.Log("[FileBrowserController] Opening file picker...");

#if UNITY_EDITOR
            // Unity Editorでの動作
            string path = UnityEditor.EditorUtility.OpenFilePanel("Select VRM, FBX, ZIP or UnityPackage File", "", "vrm,fbx,zip,unitypackage");

            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("[FileBrowserController] File selection cancelled");
                onFileSelectedCallback?.Invoke(false, null);
                return;
            }

            ProcessSelectedFile(path);
#else
            // モバイル（iOS/Android）での動作
            NativeFilePicker.Permission permission = NativeFilePicker.PickFile((path) =>
            {
                if (path == null)
                {
                    Debug.Log("[FileBrowserController] File selection cancelled");
                    onFileSelectedCallback?.Invoke(false, null);
                    return;
                }

                ProcessSelectedFile(path);
            }, new string[] { "vrm", "fbx", "zip", "unitypackage" });

            if (permission == NativeFilePicker.Permission.Denied)
            {
                Debug.LogError("[FileBrowserController] File picker permission denied");
                onFileSelectedCallback?.Invoke(false, null);
            }
#endif
        }

        private void ProcessSelectedFile(string path)
        {
            Debug.Log($"[FileBrowserController] Selected file: {path}");

            try
            {
                string lowerPath = path.ToLower();

                if (lowerPath.EndsWith(".zip") || lowerPath.EndsWith(".unitypackage"))
                {
                    // ZIP/UnityPackageファイルの場合は、解凍ボタン待ち
                    SelectedPath = path;
                    ExtractedFolderPath = null;

                    string fileType = lowerPath.EndsWith(".zip") ? "ZIP" : "UnityPackage";
                    Debug.Log($"[FileBrowserController] {fileType} file selected, waiting for extraction");
                    onFileSelectedCallback?.Invoke(true, path);
                }
                else if (lowerPath.EndsWith(".vrm") || lowerPath.EndsWith(".fbx"))
                {
                    // VRM/FBXファイルを直接選択
                    // 重要: ExtractedFolderPathは設定しない（ユーザーのフォルダを削除しないため）
                    SelectedPath = path;
                    ExtractedFolderPath = null;

                    Debug.Log($"[FileBrowserController] Model file selected: {SelectedPath}");
                    onFileSelectedCallback?.Invoke(true, SelectedPath);
                }
                else
                {
                    Debug.LogError($"[FileBrowserController] Unsupported file type: {path}");
                    onFileSelectedCallback?.Invoke(false, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileBrowserController] Error processing file: {e.Message}");
                Debug.LogException(e);
                onFileSelectedCallback?.Invoke(false, null);
            }
        }

        private string FindModelFileInFolder(string folder)
        {
            try
            {
                // まずVRMファイルを検索（優先）
                string[] vrmFiles = Directory.GetFiles(folder, "*.vrm", SearchOption.AllDirectories);
                if (vrmFiles.Length > 0)
                {
                    Debug.Log($"[FileBrowserController] Found VRM file: {vrmFiles[0]}");
                    return vrmFiles[0];
                }

                // VRMが見つからない場合はFBXファイルを検索
                string[] fbxFiles = Directory.GetFiles(folder, "*.fbx", SearchOption.AllDirectories);
                if (fbxFiles.Length > 0)
                {
                    Debug.Log($"[FileBrowserController] Found FBX file: {fbxFiles[0]}");
                    return fbxFiles[0];
                }

                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileBrowserController] Error searching for model file: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// ZIP/UnityPackageパッケージを解凍する
        /// </summary>
        public void ExtractZipPackage(Action<bool, string> onComplete)
        {
            onExtractCompleteCallback = onComplete;

            if (string.IsNullOrEmpty(SelectedPath))
            {
                Debug.LogError("[FileBrowserController] No file selected");
                onExtractCompleteCallback?.Invoke(false, null);
                return;
            }

            string lowerPath = SelectedPath.ToLower();
            bool isZip = lowerPath.EndsWith(".zip");
            bool isUnityPackage = lowerPath.EndsWith(".unitypackage");

            if (!isZip && !isUnityPackage)
            {
                Debug.LogError("[FileBrowserController] Selected file is not a ZIP or UnityPackage file");
                onExtractCompleteCallback?.Invoke(false, null);
                return;
            }

            try
            {
                string fileType = isZip ? "ZIP" : "UnityPackage";
                Debug.Log($"[FileBrowserController] Extracting {fileType} package...");

                // プラットフォーム別の解凍先パスを取得
                string basePath = GetExtractBasePath();
                string extractFolder = Path.Combine(basePath, isUnityPackage ? "ExtractedUnityPackage" : "ExtractedFBX");

                // 既存のフォルダがあれば削除
                if (Directory.Exists(extractFolder))
                {
                    Directory.Delete(extractFolder, true);
                }

                Directory.CreateDirectory(extractFolder);

                ExtractedFolderPath = extractFolder;

                // 解凍処理
                if (isUnityPackage)
                {
                    // UnityPackage解凍
                    UnityPackageExtractor extractor = new UnityPackageExtractor();
                    extractor.Extract(SelectedPath, extractFolder);
                }
                else
                {
                    // ZIP解凍
                    ZipUtility.Extract(SelectedPath, extractFolder);
                }

                Debug.Log($"[FileBrowserController] Extracted to: {extractFolder}");

                // 展開したフォルダ内のVRM/FBXファイルを検索
                string modelFile = FindModelFileInFolder(extractFolder);

                if (modelFile != null)
                {
                    SelectedPath = modelFile;
                    Debug.Log($"[FileBrowserController] Found model file: {modelFile}");
                    onExtractCompleteCallback?.Invoke(true, SelectedPath);
                }
                else
                {
                    Debug.LogError($"[FileBrowserController] No VRM/FBX file found in {fileType}");
                    onExtractCompleteCallback?.Invoke(false, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileBrowserController] Error extracting package: {e.Message}");
                Debug.LogException(e);
                onExtractCompleteCallback?.Invoke(false, null);
            }
        }

        /// <summary>
        /// 展開したフォルダをクリーンアップ
        /// </summary>
        public void CleanupExtractedFolder()
        {
            if (!string.IsNullOrEmpty(ExtractedFolderPath) && Directory.Exists(ExtractedFolderPath))
            {
                try
                {
                    Directory.Delete(ExtractedFolderPath, true);
                    Debug.Log($"[FileBrowserController] Cleaned up: {ExtractedFolderPath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[FileBrowserController] Failed to cleanup: {e.Message}");
                }
            }
        }

        void OnDestroy()
        {
            // アプリ終了時にクリーンアップ
            CleanupExtractedFolder();
        }
    }
}
