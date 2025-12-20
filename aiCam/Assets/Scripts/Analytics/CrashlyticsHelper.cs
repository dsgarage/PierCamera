using System;
using System.IO;
using UnityEngine;

namespace AICam.Analytics
{
    /// <summary>
    /// Firebase Crashlytics連携ヘルパー
    /// アバターロード時のファイル情報をクラッシュレポートに付加する
    /// </summary>
    public static class CrashlyticsHelper
    {
        private const string TAG = "[Crashlytics]";

        /// <summary>
        /// アバター情報をCrashlyticsに設定
        /// クラッシュ発生時にこの情報がレポートに含まれる
        /// </summary>
        /// <param name="filePath">アバターファイルのパス</param>
        public static void SetAvatarInfo(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogWarning($"{TAG} filePath is null or empty");
                return;
            }

            try
            {
                string fileName = Path.GetFileName(filePath);
                long fileSize = 0;

                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    fileSize = fileInfo.Length;
                }

                SetAvatarInfo(fileName, fileSize);
            }
            catch (Exception e)
            {
                Debug.LogError($"{TAG} Failed to get file info: {e.Message}");
            }
        }

        /// <summary>
        /// アバター情報をCrashlyticsに設定
        /// </summary>
        /// <param name="fileName">ファイル名</param>
        /// <param name="fileSize">ファイルサイズ（バイト）</param>
        public static void SetAvatarInfo(string fileName, long fileSize)
        {
#if FIREBASE_CRASHLYTICS
            try
            {
                Firebase.Crashlytics.Crashlytics.SetCustomKey("avatar_filename", fileName ?? "unknown");
                Firebase.Crashlytics.Crashlytics.SetCustomKey("avatar_filesize_bytes", fileSize.ToString());

                // MBでも記録（見やすさのため）
                float fileSizeMB = fileSize / 1024f / 1024f;
                Firebase.Crashlytics.Crashlytics.SetCustomKey("avatar_filesize_mb", fileSizeMB.ToString("F2"));

                // ログも記録
                Firebase.Crashlytics.Crashlytics.Log($"Avatar loaded: {fileName} ({fileSize} bytes, {fileSizeMB:F2} MB)");

                Debug.Log($"{TAG} ✅ Set avatar info - Name: {fileName}, Size: {fileSize} bytes ({fileSizeMB:F2} MB)");
            }
            catch (Exception e)
            {
                Debug.LogError($"{TAG} Failed to set custom keys: {e.Message}");
            }
#else
            // Firebase未導入時はログのみ
            float fileSizeMB = fileSize / 1024f / 1024f;
            Debug.Log($"{TAG} (Firebase not configured) Avatar info - Name: {fileName}, Size: {fileSize} bytes ({fileSizeMB:F2} MB)");
#endif
        }

        /// <summary>
        /// アバターロードエラーを記録（非致命的エラー）
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="errorMessage">エラーメッセージ</param>
        public static void LogAvatarLoadError(string filePath, string errorMessage)
        {
#if FIREBASE_CRASHLYTICS
            try
            {
                string fileName = !string.IsNullOrEmpty(filePath) ? Path.GetFileName(filePath) : "unknown";
                Firebase.Crashlytics.Crashlytics.SetCustomKey("avatar_load_error_file", fileName);
                Firebase.Crashlytics.Crashlytics.Log($"Avatar load error: {fileName} - {errorMessage}");

                // 非致命的エラーとして記録
                Firebase.Crashlytics.Crashlytics.LogException(new Exception($"AvatarLoadError: {errorMessage}"));

                Debug.Log($"{TAG} ⚠️ Logged avatar load error: {fileName} - {errorMessage}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{TAG} Failed to log error: {e.Message}");
            }
#else
            string fileName = !string.IsNullOrEmpty(filePath) ? Path.GetFileName(filePath) : "unknown";
            Debug.Log($"{TAG} (Firebase not configured) Avatar load error - File: {fileName}, Error: {errorMessage}");
#endif
        }

        /// <summary>
        /// スロット情報を設定
        /// </summary>
        /// <param name="slotIndex">スロットインデックス</param>
        public static void SetSlotInfo(int slotIndex)
        {
#if FIREBASE_CRASHLYTICS
            try
            {
                Firebase.Crashlytics.Crashlytics.SetCustomKey("avatar_slot_index", slotIndex.ToString());
                Debug.Log($"{TAG} ✅ Set slot info - Index: {slotIndex}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{TAG} Failed to set slot info: {e.Message}");
            }
#else
            Debug.Log($"{TAG} (Firebase not configured) Slot info - Index: {slotIndex}");
#endif
        }
    }
}
