using System;
using System.IO;
using UnityEngine;

namespace AICam.Analytics
{
    /// <summary>
    /// Crashlytics連携ヘルパー
    /// アバターロード時のファイル情報をクラッシュレポートに付加する
    ///
    /// 注: Firebase SDKを直接使用せず、AnalyticsBridge経由で親アプリに情報を送信
    /// 親アプリ側でFirebase Crashlytics SDKを使用して実際の送信を行う
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
            try
            {
                // AnalyticsBridge経由で親アプリに送信
                AnalyticsBridge.SetAvatarInfo(fileName, fileSize);

                float fileSizeMB = fileSize / 1024f / 1024f;
                Debug.Log($"{TAG} Set avatar info - Name: {fileName}, Size: {fileSize} bytes ({fileSizeMB:F2} MB)");
            }
            catch (Exception e)
            {
                Debug.LogError($"{TAG} Failed to set avatar info: {e.Message}");
            }
        }

        /// <summary>
        /// アバターロードエラーを記録（非致命的エラー）
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="errorMessage">エラーメッセージ</param>
        public static void LogAvatarLoadError(string filePath, string errorMessage)
        {
            try
            {
                string fileName = !string.IsNullOrEmpty(filePath) ? Path.GetFileName(filePath) : "unknown";

                // AnalyticsBridge経由で親アプリに送信
                AnalyticsBridge.LogAvatarLoadError(fileName, errorMessage);

                Debug.Log($"{TAG} Logged avatar load error: {fileName} - {errorMessage}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{TAG} Failed to log error: {e.Message}");
            }
        }

        /// <summary>
        /// スロット情報を設定
        /// </summary>
        /// <param name="slotIndex">スロットインデックス</param>
        public static void SetSlotInfo(int slotIndex)
        {
            try
            {
                // AnalyticsBridge経由で親アプリに送信
                AnalyticsBridge.SetSlotInfo(slotIndex);

                Debug.Log($"{TAG} Set slot info - Index: {slotIndex}");
            }
            catch (Exception e)
            {
                Debug.LogError($"{TAG} Failed to set slot info: {e.Message}");
            }
        }
    }
}
