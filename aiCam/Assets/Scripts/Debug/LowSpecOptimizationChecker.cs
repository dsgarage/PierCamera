using System;
using System.Diagnostics;
using UnityEngine;
using AICam.Core.IO;
using AICam.Core.Texture;
using Debug = UnityEngine.Debug;

namespace AICam.Debug
{
    /// <summary>
    /// 低スペック端末最適化の実機テスト用チェッカー
    /// Issue #440: Phase 1-4 の動作確認
    /// </summary>
    public class LowSpecOptimizationChecker : MonoBehaviour
    {
        [Header("テスト設定")]
        [SerializeField] private string testVrmPath = "";

        [Header("結果表示")]
        [SerializeField] private bool showOnGUI = true;

        private string _lastResult = "";
        private GUIStyle _guiStyle;

        private void Start()
        {
            _guiStyle = new GUIStyle
            {
                fontSize = 24,
                normal = { textColor = Color.white }
            };
        }

        /// <summary>
        /// Phase 4: テクスチャ圧縮の確認
        /// </summary>
        [ContextMenu("Check Texture Compression")]
        public void CheckTextureCompression()
        {
            var deserializer = new CompressedTextureDeserializer(enableCompression: true);

            var result = new System.Text.StringBuilder();
            result.AppendLine("=== Phase 4: Texture Compression ===");
            result.AppendLine($"Compression Available: {deserializer.IsCompressionAvailable}");
            result.AppendLine($"Platform: {Application.platform}");

#if RUNTIME_TEXTURE_COMPRESSOR
            result.AppendLine("RuntimeTextureCompressor: ENABLED");
#else
            result.AppendLine("RuntimeTextureCompressor: NOT INSTALLED");
#endif

            _lastResult = result.ToString();
            Debug.Log(_lastResult);
        }

        /// <summary>
        /// Phase 3: Chunked読み込みの確認
        /// </summary>
        [ContextMenu("Check Chunked File Reader")]
        public async void CheckChunkedFileReader()
        {
            if (string.IsNullOrEmpty(testVrmPath))
            {
                _lastResult = "Error: testVrmPath is empty";
                Debug.LogError(_lastResult);
                return;
            }

            var result = new System.Text.StringBuilder();
            result.AppendLine("=== Phase 3: Chunked File Reader ===");
            result.AppendLine($"File: {testVrmPath}");

            try
            {
                var sw = Stopwatch.StartNew();
                float lastProgress = 0;
                int progressCallCount = 0;

                var bytes = await ChunkedFileReader.ReadAllBytesAsync(
                    testVrmPath,
                    progress =>
                    {
                        progressCallCount++;
                        lastProgress = progress;
                    }
                );

                sw.Stop();

                result.AppendLine($"Size: {bytes.Length / 1024f / 1024f:F2} MB");
                result.AppendLine($"Time: {sw.ElapsedMilliseconds} ms");
                result.AppendLine($"Progress callbacks: {progressCallCount}");
                result.AppendLine($"Final progress: {lastProgress:P0}");
            }
            catch (Exception e)
            {
                result.AppendLine($"Error: {e.Message}");
            }

            _lastResult = result.ToString();
            Debug.Log(_lastResult);
        }

        /// <summary>
        /// メモリ使用量の確認
        /// </summary>
        [ContextMenu("Check Memory Usage")]
        public void CheckMemoryUsage()
        {
            var result = new System.Text.StringBuilder();
            result.AppendLine("=== Memory Usage ===");
            result.AppendLine($"Total Allocated: {UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1024f / 1024f:F2} MB");
            result.AppendLine($"Total Reserved: {UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1024f / 1024f:F2} MB");
            result.AppendLine($"Mono Heap: {UnityEngine.Profiling.Profiler.GetMonoHeapSizeLong() / 1024f / 1024f:F2} MB");
            result.AppendLine($"Mono Used: {UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() / 1024f / 1024f:F2} MB");

            // テクスチャメモリ概算
            var textures = Resources.FindObjectsOfTypeAll<Texture2D>();
            long textureMemory = 0;
            foreach (var tex in textures)
            {
                textureMemory += UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
            }
            result.AppendLine($"Texture Memory: {textureMemory / 1024f / 1024f:F2} MB ({textures.Length} textures)");

            _lastResult = result.ToString();
            Debug.Log(_lastResult);
        }

        /// <summary>
        /// 全チェックを実行
        /// </summary>
        [ContextMenu("Run All Checks")]
        public void RunAllChecks()
        {
            CheckTextureCompression();
            CheckMemoryUsage();
        }

        private void OnGUI()
        {
            if (!showOnGUI || string.IsNullOrEmpty(_lastResult)) return;

            GUI.Label(new Rect(10, 10, Screen.width - 20, Screen.height - 20), _lastResult, _guiStyle);
        }
    }
}
