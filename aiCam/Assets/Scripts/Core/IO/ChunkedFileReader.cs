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
    ///
    /// 注: 低スペック端末（iPhone 11以下）のみチャンク読み込みを使用
    /// 高スペック端末では一括読み込みで速度優先
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
        /// 低スペック端末かどうか（遅延初期化）
        /// </summary>
        private static bool? _isLowSpecDevice;

        /// <summary>
        /// 低スペック端末（iPhone 11以下）かどうかを判定
        /// DeviceAnalytics.GetDeviceCategory() == LowEnd と同等のロジック
        /// 注: AICam.Core は AICam.Analytics を参照できないため、ここに判定ロジックを直接実装
        /// </summary>
        private static bool IsLowSpecDevice
        {
            get
            {
                if (!_isLowSpecDevice.HasValue)
                {
                    _isLowSpecDevice = CheckIsLowSpecDevice();
                    Debug.Log($"[ChunkedFileReader] IsLowSpecDevice={_isLowSpecDevice.Value}, DeviceModel={SystemInfo.deviceModel}");
                }
                return _isLowSpecDevice.Value;
            }
        }

        /// <summary>
        /// 低スペック端末（iPhone 11以下、SE）かどうかをチェック
        /// iPhone12以降（iPhone13,x〜）は高スペックとみなす
        /// </summary>
        private static bool CheckIsLowSpecDevice()
        {
            string deviceModel = SystemInfo.deviceModel;

            // iPhoneでない場合はUnknown扱い（チャンク読み込みを使用）
            if (!deviceModel.StartsWith("iPhone"))
            {
                return true;
            }

            // iPhone識別子のパターン: "iPhoneXX,Y"
            // iPhone12以降: iPhone13,x〜 (iPhone13,1 = iPhone 12 mini)
            // iPhone11以前: iPhone12,x以下
            try
            {
                // "iPhone" の後の数字を取得
                string numPart = deviceModel.Substring(6); // "iPhone" の後
                int commaIndex = numPart.IndexOf(',');
                if (commaIndex > 0)
                {
                    string majorStr = numPart.Substring(0, commaIndex);
                    if (int.TryParse(majorStr, out int major))
                    {
                        // iPhone13,x以降（iPhone 12シリーズ以降）は高スペック
                        // iPhone12,x以下（iPhone 11シリーズ以前）は低スペック
                        return major < 13;
                    }
                }
            }
            catch
            {
                // パース失敗時は安全側に倒す（チャンク読み込み）
            }

            return true; // 判定できない場合は低スペック扱い
        }

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

            // 高スペック端末または小さいファイル（1MB未満）は一括読み込み
            if (!IsLowSpecDevice || fileSize < 1024 * 1024)
            {
                string reason = !IsLowSpecDevice ? "high-spec device" : $"small file ({fileSize / 1024}KB)";
                Debug.Log($"[ChunkedFileReader] Using standard read ({reason}): {Path.GetFileName(filePath)}");
                onProgress?.Invoke(1.0f);
                return await File.ReadAllBytesAsync(filePath, cancellationToken);
            }

            Debug.Log($"[ChunkedFileReader] Chunked read started (low-spec device): {Path.GetFileName(filePath)} ({fileSize / 1024 / 1024}MB)");

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
