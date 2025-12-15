using UnityEngine;
using System;
using System.IO;

namespace AICam.FBXLoader
{
    /// <summary>
    /// NativeFilePickerを使用したファイル選択とZIP展開を管理
    /// 対応形式: VRM, FBX, ZIP
    /// iOS: Application.persistentDataPathに展開（iCloudではなくアプリ内ディレクトリ）
    /// </summary>
    public class FileBrowserController : MonoBehaviour
    {
        public string SelectedPath { get; private set; }
        public string ExtractedFolderPath { get; private set; }

        private Action<bool, string> onFileSelectedCallback;
        private Action<bool, string> onExtractCompleteCallback;

        /// <summary>
        /// ファイルピッカーを開く
        /// </summary>
        public void OpenFilePicker(Action<bool, string> onComplete)
        {
            onFileSelectedCallback = onComplete;

            Debug.Log($"[FileBrowserController] OpenFilePicker called, callback is {(onComplete != null ? "NOT NULL" : "NULL")}");
            Debug.Log("[FileBrowserController] Opening file picker...");

#if UNITY_EDITOR
            // Unity Editorでの動作
            string path = UnityEditor.EditorUtility.OpenFilePanel("Select VRM, FBX or ZIP File", "", "vrm,fbx,zip");

            if (string.IsNullOrEmpty(path))
            {
                Debug.Log("[FileBrowserController] File selection cancelled");
                onFileSelectedCallback?.Invoke(false, null);
                return;
            }

            ProcessSelectedFile(path);
#else
            // モバイル（iOS/Android）での動作
            // NativeFilePicker v1.5.0+ では CheckPermission は bool を返す
            bool hasPermission = NativeFilePicker.CheckPermission();
            if (!hasPermission)
            {
                // 権限がない場合は非同期で権限リクエスト
                NativeFilePicker.RequestPermissionAsync((permission) =>
                {
                    if (permission != NativeFilePicker.Permission.Granted)
                    {
                        Debug.LogError("[FileBrowserController] File picker permission denied");
                        onFileSelectedCallback?.Invoke(false, null);
                        return;
                    }

                    // 権限取得後にファイルピッカーを開く
                    OpenFilePicker();
                });
            }
            else
            {
                // 既に権限がある場合は直接ファイルピッカーを開く
                OpenFilePicker();
            }
#endif
        }

        private void OpenFilePicker()
        {
            NativeFilePicker.PickFile((path) =>
            {
                if (path == null)
                {
                    Debug.Log("[FileBrowserController] File selection cancelled");
                    onFileSelectedCallback?.Invoke(false, null);
                    return;
                }

                ProcessSelectedFile(path);
            }, new string[] { "public.data" });  // iOS UTI for generic files
        }

        private void ProcessSelectedFile(string path)
        {
            Debug.Log($"[FileBrowserController] Selected file: {path}");

            try
            {
                if (path.ToLower().EndsWith(".zip"))
                {
                    // ZIPファイルの場合は展開
                    Debug.Log("[FileBrowserController] ZIP file detected, extracting...");

                    // iOS対応: Application.persistentDataPathを使用（iCloudではなくアプリ内）
                    string extractFolder = Path.Combine(Application.persistentDataPath, "ExtractedFBX");

                    // 既存のフォルダがあれば削除
                    if (Directory.Exists(extractFolder))
                    {
                        Directory.Delete(extractFolder, true);
                    }

                    Directory.CreateDirectory(extractFolder);

                    ExtractedFolderPath = extractFolder;

                    // ZIP展開
                    ZipUtility.Extract(path, extractFolder);

                    Debug.Log($"[FileBrowserController] Extracted to: {extractFolder}");

                    // 展開したフォルダ内のVRM/FBXファイルを検索
                    string modelFile = FindModelFileInFolder(extractFolder);

                    if (modelFile != null)
                    {
                        SelectedPath = modelFile;
                        Debug.Log($"[FileBrowserController] Found model file: {modelFile}");
                        Debug.Log($"[FileBrowserController] Invoking callback (ZIP path), callback is {(onFileSelectedCallback != null ? "NOT NULL" : "NULL")}");
                        onFileSelectedCallback?.Invoke(true, SelectedPath);
                        Debug.Log("[FileBrowserController] Callback invocation completed (ZIP path)");
                    }
                    else
                    {
                        Debug.LogError("[FileBrowserController] No VRM/FBX file found in ZIP");
                        Debug.Log($"[FileBrowserController] Invoking callback with failure, callback is {(onFileSelectedCallback != null ? "NOT NULL" : "NULL")}");
                        onFileSelectedCallback?.Invoke(false, null);
                    }
                }
                else if (path.ToLower().EndsWith(".vrm") || path.ToLower().EndsWith(".fbx"))
                {
                    // VRM/FBXファイルを直接選択
                    // 重要: ExtractedFolderPathは設定しない（ユーザーのフォルダを削除しないため）
                    SelectedPath = path;
                    ExtractedFolderPath = null;

                    Debug.Log($"[FileBrowserController] Model file selected: {SelectedPath}");
                    Debug.Log($"[FileBrowserController] Invoking callback, callback is {(onFileSelectedCallback != null ? "NOT NULL" : "NULL")}");
                    onFileSelectedCallback?.Invoke(true, SelectedPath);
                    Debug.Log("[FileBrowserController] Callback invocation completed");
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
        /// ZIPパッケージを解凍する
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

            if (!SelectedPath.ToLower().EndsWith(".zip"))
            {
                Debug.LogError("[FileBrowserController] Selected file is not a ZIP file");
                onExtractCompleteCallback?.Invoke(false, null);
                return;
            }

            try
            {
                Debug.Log("[FileBrowserController] Extracting ZIP package...");

                // iOS対応: Application.persistentDataPathを使用（iCloudではなくアプリ内）
                string extractFolder = Path.Combine(Application.persistentDataPath, "ExtractedFBX");

                // 既存のフォルダがあれば削除
                if (Directory.Exists(extractFolder))
                {
                    Directory.Delete(extractFolder, true);
                }

                Directory.CreateDirectory(extractFolder);

                ExtractedFolderPath = extractFolder;

                // ZIP展開
                ZipUtility.Extract(SelectedPath, extractFolder);

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
                    Debug.LogError("[FileBrowserController] No VRM/FBX file found in ZIP");
                    onExtractCompleteCallback?.Invoke(false, null);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[FileBrowserController] Error extracting ZIP: {e.Message}");
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
