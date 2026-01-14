using System;
using System.Diagnostics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace AICam.Core
{
    /// <summary>
    /// 統一ログシステム
    ///
    /// - カテゴリ別のタグ付きログ出力
    /// - 初期化ログ等はDEVELOPMENT_BUILDでのみ出力
    /// - エラー/警告は常に出力
    /// </summary>
    public static class AICamLogger
    {
        /// <summary>
        /// ログカテゴリ
        /// </summary>
        public enum Category
        {
            Init,       // 初期化・セットアップ
            Avatar,     // アバターロード・キャッシュ・スロット管理
            AR,         // AR関連操作
            UI,         // UI操作
            Telemetry,  // テレメトリ・アナリティクス
            Lighting,   // ライティング
            Debug       // デバッグ用（開発ビルドのみ）
        }

        /// <summary>
        /// 通常ログ（開発ビルドのみ出力）
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Log(Category category, string message)
        {
            Debug.Log($"[{category}] {message}");
        }

        /// <summary>
        /// 通常ログ（開発ビルドのみ出力）- コンテキスト付き
        /// </summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Log(Category category, string message, UnityEngine.Object context)
        {
            Debug.Log($"[{category}] {message}", context);
        }

        /// <summary>
        /// 警告ログ（常に出力）
        /// </summary>
        public static void LogWarning(Category category, string message)
        {
            Debug.LogWarning($"[{category}] {message}");
        }

        /// <summary>
        /// 警告ログ（常に出力）- コンテキスト付き
        /// </summary>
        public static void LogWarning(Category category, string message, UnityEngine.Object context)
        {
            Debug.LogWarning($"[{category}] {message}", context);
        }

        /// <summary>
        /// エラーログ（常に出力）
        /// </summary>
        public static void LogError(Category category, string message)
        {
            Debug.LogError($"[{category}] {message}");
        }

        /// <summary>
        /// エラーログ（常に出力）- コンテキスト付き
        /// </summary>
        public static void LogError(Category category, string message, UnityEngine.Object context)
        {
            Debug.LogError($"[{category}] {message}", context);
        }

        /// <summary>
        /// 例外ログ（常に出力）
        /// </summary>
        public static void LogException(Category category, Exception exception)
        {
            Debug.LogError($"[{category}] Exception: {exception.Message}");
            Debug.LogException(exception);
        }

        /// <summary>
        /// リリースビルドでも出力するログ（重要なイベント用）
        /// 使用は最小限に
        /// </summary>
        public static void LogRelease(Category category, string message)
        {
            Debug.Log($"[{category}] {message}");
        }
    }
}
