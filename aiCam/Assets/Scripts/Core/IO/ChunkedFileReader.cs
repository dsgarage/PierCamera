using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AICam.Core.IO
{
    /// <summary>
    /// Issue #440: チャンク単位での非同期ファイル読み込み
    /// 低メモリ端末でのメモリスパイクとクラッシュを防止
    /// </summary>
    public static class ChunkedFileReader
    {
        /// <summary>
        /// チャンクサイズ（64KB）
        /// モバイル環境でのメモリ効率と読み込み速度のバランス
        /// </summary>
        private const int DefaultChunkSize = 64 * 1024;

        /// <summary>
        /// N チャンクごとにフレームを譲る
        /// 64KB × 8 = 512KB 読み込むごとに1フレーム待機
        /// </summary>
        private const int YieldInterval = 8;

        /// <summary>
        /// ファイルをチャンク単位で非同期読み込み
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="onProgress">進捗コールバック (0.0 - 1.0)</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>読み込んだバイト配列</returns>
        public static async UniTask<byte[]> ReadAllBytesAsync(
            string filePath,
            Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                throw new ArgumentNullException(nameof(filePath));
            }

            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException($"File not found: {filePath}", filePath);
            }

            var fileInfo = new FileInfo(filePath);
            long fileSize = fileInfo.Length;

            // 小さいファイル（1MB未満）は従来通り一括読み込み
            if (fileSize < 1024 * 1024)
            {
                Debug.Log($"[ChunkedFileReader] Small file ({fileSize / 1024}KB), using standard read: {Path.GetFileName(filePath)}");
                onProgress?.Invoke(1.0f);
                return await File.ReadAllBytesAsync(filePath, cancellationToken);
            }

            Debug.Log($"[ChunkedFileReader] Chunked read started: {Path.GetFileName(filePath)} ({fileSize / 1024 / 1024}MB)");

            try
            {
                using var stream = new FileStream(
                    filePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: DefaultChunkSize,
                    useAsync: true);

                byte[] buffer = new byte[fileSize];
                int offset = 0;
                int chunkCount = 0;

                while (offset < buffer.Length)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int bytesToRead = Math.Min(DefaultChunkSize, buffer.Length - offset);
                    int bytesRead = await stream.ReadAsync(
                        buffer, offset, bytesToRead, cancellationToken);

                    if (bytesRead == 0) break;

                    offset += bytesRead;
                    chunkCount++;

                    // 進捗通知
                    float progress = (float)offset / buffer.Length;
                    onProgress?.Invoke(progress);

                    // YieldInterval チャンクごとにフレームを譲る
                    if (chunkCount % YieldInterval == 0)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    }
                }

                Debug.Log($"[ChunkedFileReader] Chunked read completed: {chunkCount} chunks read");
                return buffer;
            }
            catch (OperationCanceledException)
            {
                Debug.Log("[ChunkedFileReader] Read cancelled");
                throw;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ChunkedFileReader] Read failed: {e.Message}");
                throw;
            }
        }

        /// <summary>
        /// ファイルをチャンク単位で非同期読み込み（進捗範囲指定版）
        /// 全体の進捗の一部としてファイル読み込みを行う場合に使用
        /// </summary>
        /// <param name="filePath">ファイルパス</param>
        /// <param name="onProgress">進捗コールバック (progressStart - progressEnd)</param>
        /// <param name="progressStart">進捗開始値 (0.0-1.0)</param>
        /// <param name="progressEnd">進捗終了値 (0.0-1.0)</param>
        /// <param name="cancellationToken">キャンセルトークン</param>
        /// <returns>読み込んだバイト配列</returns>
        public static async UniTask<byte[]> ReadAllBytesAsync(
            string filePath,
            Action<float> onProgress,
            float progressStart,
            float progressEnd,
            CancellationToken cancellationToken = default)
        {
            float progressRange = progressEnd - progressStart;

            return await ReadAllBytesAsync(
                filePath,
                progress => onProgress?.Invoke(progressStart + progress * progressRange),
                cancellationToken);
        }
    }
}
