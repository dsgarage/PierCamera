using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AICam.FBXLoader
{
    /// <summary>
    /// FBXインポート時のログとスクリーンショットを自動保存
    /// </summary>
    public class FBXImportLogger : MonoBehaviour
    {
        private static FBXImportLogger instance;
        private List<string> logEntries = new List<string>();
        private bool isCapturing = false;
        private string currentSessionId;
        private string logsDirectory = "FBXImportLogs";

        void Awake()
        {
            if (instance == null)
            {
                instance = this;
                DontDestroyOnLoad(gameObject);
                Application.logMessageReceived += HandleLog;
            }
            else
            {
                Destroy(gameObject);
            }
        }

        void OnDestroy()
        {
            if (instance == this)
            {
                Application.logMessageReceived -= HandleLog;
            }
        }

        /// <summary>
        /// ログキャプチャを開始
        /// </summary>
        public static void StartCapture(string sessionId = null)
        {
            if (instance == null)
            {
                GameObject go = new GameObject("FBXImportLogger");
                instance = go.AddComponent<FBXImportLogger>();
            }

            instance.currentSessionId = sessionId ?? $"FBX_Import_{DateTime.Now:yyyyMMdd_HHmmss}";
            instance.logEntries.Clear();
            instance.isCapturing = true;
            instance.logEntries.Add($"=== FBX Import Log Session: {instance.currentSessionId} ===");
            instance.logEntries.Add($"Started: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            instance.logEntries.Add("");

            Debug.Log($"[FBXImportLogger] Started capturing logs for session: {instance.currentSessionId}");
        }

        /// <summary>
        /// ログキャプチャを停止し、ファイルに保存
        /// </summary>
        public static void StopCaptureAndSave(bool takeScreenshot = true)
        {
            if (instance == null || !instance.isCapturing) return;

            instance.isCapturing = false;
            instance.logEntries.Add("");
            instance.logEntries.Add($"Ended: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            instance.logEntries.Add($"=== End of Log ===");

            // ディレクトリ作成
            string logsPath = Path.Combine(Application.dataPath, "..", instance.logsDirectory);
            if (!Directory.Exists(logsPath))
            {
                Directory.CreateDirectory(logsPath);
            }

            // ログファイル保存
            string logFilePath = Path.Combine(logsPath, $"{instance.currentSessionId}.txt");
            try
            {
                File.WriteAllLines(logFilePath, instance.logEntries);
                Debug.Log($"[FBXImportLogger] Log saved to: {logFilePath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[FBXImportLogger] Failed to save log: {e.Message}");
            }

            // スクリーンショット保存
            if (takeScreenshot)
            {
                instance.CaptureScreenshot(logsPath);
            }
        }

        /// <summary>
        /// スクリーンショットを撮影
        /// </summary>
        private void CaptureScreenshot(string directory)
        {
            string screenshotPath = Path.Combine(directory, $"{currentSessionId}.png");

            // Unity 2019.3以降
            ScreenCapture.CaptureScreenshot(screenshotPath);
            Debug.Log($"[FBXImportLogger] Screenshot saved to: {screenshotPath}");
        }

        /// <summary>
        /// 6方向（前後左右上下）のスクリーンショットを1枚の画像にまとめて撮影
        /// </summary>
        public static void CaptureMultiAngleScreenshot(GameObject targetModel)
        {
            if (instance == null) return;
            instance.StartCoroutine(instance.CaptureMultiAngleCoroutine(targetModel));
        }

        private System.Collections.IEnumerator CaptureMultiAngleCoroutine(GameObject targetModel)
        {
            if (targetModel == null)
            {
                Debug.LogError("[FBXImportLogger] Target model is null");
                yield break;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("[FBXImportLogger] Main camera not found");
                yield break;
            }

            // 元のカメラ位置・回転を保存
            Vector3 originalCameraPos = mainCamera.transform.position;
            Quaternion originalCameraRot = mainCamera.transform.rotation;

            // モデルの中心とサイズを取得
            Bounds bounds = CalculateModelBounds(targetModel);
            Vector3 center = bounds.center;
            float maxSize = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            float distance = maxSize * 1.5f; // カメラ距離（モデルに近づける）

            int captureWidth = 512;
            int captureHeight = 512;
            var captures = new Texture2D[6];
            string[] angleNames = { "Front", "Back", "Left", "Right", "Top", "Bottom" };

            // 6方向からキャプチャ
            for (int i = 0; i < 6; i++)
            {
                // カメラ位置設定
                Vector3 cameraPos = center;
                Quaternion cameraRot = Quaternion.identity;

                switch (i)
                {
                    case 0: // Front
                        cameraPos += Vector3.forward * distance;
                        cameraRot = Quaternion.Euler(0, 180, 0);
                        break;
                    case 1: // Back
                        cameraPos += Vector3.back * distance;
                        cameraRot = Quaternion.Euler(0, 0, 0);
                        break;
                    case 2: // Left
                        cameraPos += Vector3.left * distance;
                        cameraRot = Quaternion.Euler(0, 90, 0);
                        break;
                    case 3: // Right
                        cameraPos += Vector3.right * distance;
                        cameraRot = Quaternion.Euler(0, -90, 0);
                        break;
                    case 4: // Top
                        cameraPos += Vector3.up * distance;
                        cameraRot = Quaternion.Euler(90, 0, 0);
                        break;
                    case 5: // Bottom
                        cameraPos += Vector3.down * distance;
                        cameraRot = Quaternion.Euler(-90, 0, 0);
                        break;
                }

                mainCamera.transform.position = cameraPos;
                mainCamera.transform.rotation = cameraRot;

                yield return new WaitForEndOfFrame();

                // RenderTextureでキャプチャ
                RenderTexture rt = new RenderTexture(captureWidth, captureHeight, 24);
                mainCamera.targetTexture = rt;
                mainCamera.Render();

                RenderTexture.active = rt;
                captures[i] = new Texture2D(captureWidth, captureHeight, TextureFormat.RGB24, false);
                captures[i].ReadPixels(new Rect(0, 0, captureWidth, captureHeight), 0, 0);
                captures[i].Apply();

                mainCamera.targetTexture = null;
                RenderTexture.active = null;
                Destroy(rt);

                Debug.Log($"[FBXImportLogger] Captured {angleNames[i]} view");
            }

            // カメラを元に戻す
            mainCamera.transform.position = originalCameraPos;
            mainCamera.transform.rotation = originalCameraRot;

            // 6枚を3x2グリッドに配置して合成
            // レイアウト: [Front] [Back]  [Left]
            //           [Right] [Top]   [Bottom]
            int gridWidth = 3;
            int gridHeight = 2;
            int compositeWidth = captureWidth * gridWidth;
            int compositeHeight = captureHeight * gridHeight;
            Texture2D composite = new Texture2D(compositeWidth, compositeHeight, TextureFormat.RGB24, false);

            int[] gridOrder = { 0, 1, 2, 3, 4, 5 }; // Front, Back, Left, Right, Top, Bottom

            for (int i = 0; i < 6; i++)
            {
                int gridX = i % gridWidth;
                int gridY = gridHeight - 1 - (i / gridWidth);
                int startX = gridX * captureWidth;
                int startY = gridY * captureHeight;

                composite.SetPixels(startX, startY, captureWidth, captureHeight, captures[gridOrder[i]].GetPixels());

                // ラベルを追加
                DrawTextOnTexture(composite, startX + 10, startY + captureHeight - 30, angleNames[gridOrder[i]]);
            }

            composite.Apply();

            // 保存
            string logsPath = Path.Combine(Application.dataPath, "..", logsDirectory);
            string filePath = Path.Combine(logsPath, $"{currentSessionId}_MultiAngle.png");
            File.WriteAllBytes(filePath, composite.EncodeToPNG());

            Debug.Log($"[FBXImportLogger] Multi-angle screenshot saved to: {filePath}");

            // クリーンアップ
            for (int i = 0; i < 6; i++)
            {
                if (captures[i] != null) Destroy(captures[i]);
            }
            Destroy(composite);
        }

        private Bounds CalculateModelBounds(GameObject model)
        {
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                return new Bounds(model.transform.position, Vector3.one);
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }
            return bounds;
        }

        private void DrawTextOnTexture(Texture2D texture, int x, int y, string text)
        {
            // シンプルなテキスト描画（白い矩形背景 + ラベル表示）
            // 実際のテキスト描画はUnityのGUI機能では難しいため、背景のみ描画
            int bgWidth = 100;
            int bgHeight = 25;
            Color bgColor = new Color(0, 0, 0, 0.7f);

            for (int py = 0; py < bgHeight; py++)
            {
                for (int px = 0; px < bgWidth; px++)
                {
                    int setX = x + px;
                    int setY = y + py;
                    if (setX >= 0 && setX < texture.width && setY >= 0 && setY < texture.height)
                    {
                        texture.SetPixel(setX, setY, bgColor);
                    }
                }
            }
        }

        /// <summary>
        /// ログをキャプチャ
        /// </summary>
        private void HandleLog(string logString, string stackTrace, LogType type)
        {
            if (!isCapturing) return;

            // フィルタリング（必要に応じて）
            if (ShouldCaptureLog(logString, type))
            {
                string prefix = type switch
                {
                    LogType.Error => "[ERROR] ",
                    LogType.Warning => "[WARN] ",
                    LogType.Exception => "[EXCEPTION] ",
                    _ => ""
                };

                logEntries.Add($"{prefix}{logString}");

                // スタックトレースも保存（エラーと例外のみ）
                if ((type == LogType.Error || type == LogType.Exception) && !string.IsNullOrEmpty(stackTrace))
                {
                    logEntries.Add(stackTrace);
                    logEntries.Add("");
                }
            }
        }

        /// <summary>
        /// このログをキャプチャすべきか判定
        /// </summary>
        private bool ShouldCaptureLog(string log, LogType type)
        {
            // FBX関連、Avatar関連、TriLib関連のログのみキャプチャ
            if (log.Contains("[RuntimeFBXLoaderBridge]") ||
                log.Contains("[RuntimeHumanoidAvatarBuilder]") ||
                log.Contains("[SkeletonBone]") ||
                log.Contains("[FixJointOrientation]") ||
                log.Contains("TriLib") ||
                type == LogType.Error ||
                type == LogType.Exception ||
                type == LogType.Warning)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 現在のセッションIDを取得
        /// </summary>
        public static string GetCurrentSessionId()
        {
            return instance?.currentSessionId;
        }
    }
}
