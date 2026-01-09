using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;
using AICam.Core.IO;
using AICam.Core.Texture;
using Debug = UnityEngine.Debug;

namespace AICam.Debug
{
    /// <summary>
    /// 低スペック端末最適化の実機テスト用チェッカー
    /// Issue #440: Phase 1-4 の動作確認
    ///
    /// Xcodeログで確認する場合:
    /// 1. iPhoneにインストール
    /// 2. Xcode > Window > Devices and Simulators > 端末選択 > Open Console
    /// 3. フィルタに "[LowSpec]" を入力
    /// </summary>
    public class LowSpecOptimizationChecker : MonoBehaviour
    {
        private const string LOG_TAG = "[LowSpec]";

        [Header("テスト設定")]
        [Tooltip("StreamingAssets内のVRMファイル名 (例: test.vrm)")]
        [SerializeField] private string testVrmFileName = "";

        [Tooltip("起動時に自動でテストを実行")]
        [SerializeField] private bool runOnStart = true;

        [Tooltip("VRM読み込みテストも実行 (時間がかかる)")]
        [SerializeField] private bool includeFileLoadTest = false;

        [Header("結果表示")]
        [SerializeField] private bool showOnGUI = true;

        private List<string> _logHistory = new List<string>();
        private bool _isRunning = false;

        private void Start()
        {
            if (runOnStart)
            {
                Invoke(nameof(RunAllChecks), 1.5f);
            }
        }

        #region Logging

        private void Log(string message)
        {
            var logMessage = $"{LOG_TAG} {message}";
            Debug.Log(logMessage);
            _logHistory.Add(message);
            if (_logHistory.Count > 30) _logHistory.RemoveAt(0);
        }

        private void LogError(string message)
        {
            var logMessage = $"{LOG_TAG} [ERROR] {message}";
            Debug.LogError(logMessage);
            _logHistory.Add($"[ERROR] {message}");
        }

        private void LogSection(string title)
        {
            Log("");
            Log($"▼▼▼ {title} ▼▼▼");
        }

        private void LogResult(string key, string value, string expected = null)
        {
            var status = "";
            if (expected != null)
            {
                status = value == expected ? " ✓" : " ✗";
            }
            Log($"  {key}: {value}{status}");
        }

        private void LogResultBool(string key, bool value, bool expected)
        {
            var status = value == expected ? " ✓" : " ✗";
            Log($"  {key}: {value}{status}");
        }

        #endregion

        #region Check Methods

        /// <summary>
        /// 全チェックを実行
        /// </summary>
        [ContextMenu("Run All Checks")]
        public async void RunAllChecks()
        {
            if (_isRunning)
            {
                Log("Already running...");
                return;
            }

            _isRunning = true;
            _logHistory.Clear();

            Log("╔════════════════════════════════════════╗");
            Log("║  Low-Spec Optimization Check Start     ║");
            Log("║  Issue #440 Phase 1-4                  ║");
            Log("╚════════════════════════════════════════╝");

            // Step 1: 環境情報
            CheckEnvironment();

            // Step 2: Phase 4 - テクスチャ圧縮
            CheckTextureCompression();

            // Step 3: メモリ状態 (VRM読み込み前)
            CheckMemoryUsage("Before VRM Load");

            // Step 4: Phase 3 - ファイル読み込み (オプション)
            if (includeFileLoadTest && !string.IsNullOrEmpty(testVrmFileName))
            {
                await CheckChunkedFileReader();

                // Step 5: メモリ状態 (VRM読み込み後)
                CheckMemoryUsage("After VRM Load");
            }

            // 完了
            Log("");
            Log("╔════════════════════════════════════════╗");
            Log("║  Check Complete                        ║");
            Log("╚════════════════════════════════════════╝");

            _isRunning = false;
        }

        /// <summary>
        /// Step 1: 環境情報
        /// </summary>
        [ContextMenu("1. Check Environment")]
        public void CheckEnvironment()
        {
            LogSection("STEP 1: Environment");

            LogResult("Platform", Application.platform.ToString());
            LogResult("Device Model", SystemInfo.deviceModel);
            LogResult("OS", SystemInfo.operatingSystem);
            LogResult("Unity Version", Application.unityVersion);
            LogResult("Graphics API", SystemInfo.graphicsDeviceType.ToString());
            LogResult("Graphics Memory", $"{SystemInfo.graphicsMemorySize} MB");
            LogResult("System Memory", $"{SystemInfo.systemMemorySize} MB");

            // ASTC対応確認
            var supportsASTC = SystemInfo.SupportsTextureFormat(TextureFormat.ASTC_6x6);
            LogResultBool("ASTC Support", supportsASTC, true);
        }

        /// <summary>
        /// Step 2: Phase 4 - テクスチャ圧縮確認
        /// </summary>
        [ContextMenu("2. Check Texture Compression (Phase 4)")]
        public void CheckTextureCompression()
        {
            LogSection("STEP 2: Texture Compression (Phase 4)");

            // RuntimeTextureCompressor 確認
#if RUNTIME_TEXTURE_COMPRESSOR
            Log("  RuntimeTextureCompressor: INSTALLED ✓");
#else
            Log("  RuntimeTextureCompressor: NOT INSTALLED ✗");
            Log("  → Package not found. Textures will use RGBA32 (uncompressed)");
#endif

            try
            {
                var deserializer = new CompressedTextureDeserializer(enableCompression: true);
                LogResultBool("Compression Available", deserializer.IsCompressionAvailable, true);

                if (deserializer.IsCompressionAvailable)
                {
                    Log("  → VRM textures will be compressed to ASTC 6x6");
                    Log("  → Expected memory reduction: ~89%");
                }
                else
                {
                    Log("  → VRM textures will use RGBA32 (no compression)");
                }
            }
            catch (Exception e)
            {
                LogError($"CompressedTextureDeserializer error: {e.Message}");
            }
        }

        /// <summary>
        /// Step 3: メモリ使用量確認
        /// </summary>
        [ContextMenu("3. Check Memory Usage")]
        public void CheckMemoryUsage(string label = "Current")
        {
            LogSection($"STEP 3: Memory Usage ({label})");

            var totalAllocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            var monoUsed = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong();

            LogResult("Total Allocated", $"{totalAllocated / 1024f / 1024f:F1} MB");
            LogResult("Mono Used", $"{monoUsed / 1024f / 1024f:F1} MB");

            // テクスチャメモリ詳細
            var textures = Resources.FindObjectsOfTypeAll<Texture2D>();
            long textureMemory = 0;
            int compressedCount = 0;
            int uncompressedCount = 0;

            foreach (var tex in textures)
            {
                var size = UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex);
                textureMemory += size;

                if (tex.format == TextureFormat.ASTC_6x6 ||
                    tex.format == TextureFormat.ASTC_8x8 ||
                    tex.format == TextureFormat.ETC2_RGBA8)
                {
                    compressedCount++;
                }
                else if (tex.format == TextureFormat.RGBA32 ||
                         tex.format == TextureFormat.ARGB32)
                {
                    uncompressedCount++;
                }
            }

            LogResult("Texture Memory", $"{textureMemory / 1024f / 1024f:F1} MB");
            LogResult("Texture Count", $"{textures.Length}");
            LogResult("Compressed", $"{compressedCount}");
            LogResult("Uncompressed (RGBA)", $"{uncompressedCount}");

            // 警告
            if (textureMemory > 500 * 1024 * 1024)
            {
                Log("  ⚠ WARNING: Texture memory exceeds 500MB");
            }
        }

        /// <summary>
        /// Step 4: Phase 3 - Chunked読み込み確認
        /// </summary>
        [ContextMenu("4. Check Chunked File Reader (Phase 3)")]
        public async System.Threading.Tasks.Task CheckChunkedFileReader()
        {
            LogSection("STEP 4: Chunked File Reader (Phase 3)");

            var filePath = Path.Combine(Application.streamingAssetsPath, testVrmFileName);
            LogResult("Target File", testVrmFileName);

            if (string.IsNullOrEmpty(testVrmFileName))
            {
                LogError("testVrmFileName is not set");
                return;
            }

            // iOSでStreamingAssetsのパス確認
            Log($"  Full Path: {filePath}");

            try
            {
                var sw = Stopwatch.StartNew();
                int progressCallCount = 0;
                float lastProgress = 0;

                Log("  Loading...");

                var bytes = await ChunkedFileReader.ReadAllBytesAsync(
                    filePath,
                    progress =>
                    {
                        progressCallCount++;
                        lastProgress = progress;
                        // 25%刻みでログ出力
                        if (progressCallCount == 1 || (int)(progress * 4) > (int)(lastProgress * 4 - 0.01f))
                        {
                            Log($"  Progress: {progress:P0}");
                        }
                    }
                );

                sw.Stop();

                LogResult("File Size", $"{bytes.Length / 1024f / 1024f:F2} MB");
                LogResult("Load Time", $"{sw.ElapsedMilliseconds} ms");
                LogResult("Progress Callbacks", $"{progressCallCount}");

                // 性能評価
                var mbPerSec = (bytes.Length / 1024f / 1024f) / (sw.ElapsedMilliseconds / 1000f);
                LogResult("Throughput", $"{mbPerSec:F1} MB/s");

                if (sw.ElapsedMilliseconds < 100)
                {
                    Log("  ✓ Fast load (< 100ms)");
                }
            }
            catch (FileNotFoundException)
            {
                LogError($"File not found: {filePath}");
                Log("  → Place VRM file in StreamingAssets folder");
            }
            catch (Exception e)
            {
                LogError($"Load failed: {e.Message}");
            }
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            if (!showOnGUI || _logHistory.Count == 0) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                normal = { textColor = Color.white },
                alignment = TextAnchor.UpperLeft
            };

            // 背景
            GUI.color = new Color(0, 0, 0, 0.7f);
            GUI.DrawTexture(new Rect(5, 5, Screen.width - 10, 25 + _logHistory.Count * 18), Texture2D.whiteTexture);
            GUI.color = Color.white;

            // ログ表示
            var y = 10;
            foreach (var log in _logHistory)
            {
                var color = log.Contains("ERROR") ? Color.red :
                           log.Contains("✓") ? Color.green :
                           log.Contains("✗") ? Color.yellow :
                           log.Contains("WARNING") ? Color.yellow : Color.white;
                style.normal.textColor = color;
                GUI.Label(new Rect(10, y, Screen.width - 20, 20), log, style);
                y += 18;
            }

            // 再実行ボタン
            if (GUI.Button(new Rect(Screen.width - 110, Screen.height - 50, 100, 40), "Re-run"))
            {
                RunAllChecks();
            }
        }

        #endregion
    }
}
