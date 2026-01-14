using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using AICam.Analytics.DTOs;
using AICam.Core;

namespace AICam.Analytics
{
    /// <summary>
    /// テレメトリクライアント
    /// REST APIでサーバーにテレメトリデータを送信
    /// </summary>
    public class TelemetryClient : MonoBehaviour
    {
        public static TelemetryClient Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private string serverBaseUrl = "https://your-server.com";
        [SerializeField] private string apiKey = "";
        [SerializeField] private bool enableTelemetry = true;
        [SerializeField] private int timeoutSeconds = 30;
        [SerializeField] private int maxRetries = 3;
        [SerializeField] private float retryDelaySeconds = 5f;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLog = true;

        private Queue<PendingRequest> _pendingRequests = new Queue<PendingRequest>();
        private bool _isProcessingQueue = false;

        private class PendingRequest
        {
            public string Endpoint;
            public string JsonPayload;
            public int RetryCount;
            public Action<bool> OnComplete;
        }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            AICamLogger.Log(AICamLogger.Category.Init, "TelemetryClient initialized");
        }

        /// <summary>
        /// テレメトリが有効かどうか
        /// </summary>
        public bool IsEnabled => enableTelemetry && !string.IsNullOrEmpty(serverBaseUrl) && !string.IsNullOrEmpty(apiKey);

        /// <summary>
        /// サーバーURLを設定
        /// </summary>
        public void SetServerUrl(string url)
        {
            serverBaseUrl = url;
        }

        /// <summary>
        /// APIキーを設定
        /// </summary>
        public void SetApiKey(string key)
        {
            apiKey = key;
        }

        /// <summary>
        /// テレメトリの有効/無効を設定
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            enableTelemetry = enabled;
        }

        /// <summary>
        /// アバターロードテレメトリを送信
        /// </summary>
        public void SendAvatarLoadTelemetry(AvatarLoadTelemetryDTO dto, Action<bool> onComplete = null)
        {
            if (!IsEnabled)
            {
                AICamLogger.Log(AICamLogger.Category.Telemetry, "Telemetry disabled, skipping send");
                onComplete?.Invoke(false);
                return;
            }

            string json = JsonUtility.ToJson(dto);
            QueueRequest("/api/v1/telemetry/avatar-load", json, onComplete);
        }

        /// <summary>
        /// リクエストをキューに追加
        /// </summary>
        private void QueueRequest(string endpoint, string jsonPayload, Action<bool> onComplete = null)
        {
            var request = new PendingRequest
            {
                Endpoint = endpoint,
                JsonPayload = jsonPayload,
                RetryCount = 0,
                OnComplete = onComplete
            };

            _pendingRequests.Enqueue(request);

            if (!_isProcessingQueue)
            {
                StartCoroutine(ProcessQueue());
            }
        }

        /// <summary>
        /// キューを処理
        /// </summary>
        private IEnumerator ProcessQueue()
        {
            _isProcessingQueue = true;

            while (_pendingRequests.Count > 0)
            {
                var request = _pendingRequests.Dequeue();
                bool success = false;

                yield return StartCoroutine(PostTelemetry(request.Endpoint, request.JsonPayload, result =>
                {
                    success = result;
                }));

                if (!success && request.RetryCount < maxRetries)
                {
                    request.RetryCount++;
                    AICamLogger.Log(AICamLogger.Category.Telemetry,
                        $"Request failed, retry {request.RetryCount}/{maxRetries} in {retryDelaySeconds}s");

                    yield return new WaitForSeconds(retryDelaySeconds * request.RetryCount); // Exponential backoff
                    _pendingRequests.Enqueue(request);
                }
                else
                {
                    request.OnComplete?.Invoke(success);
                }
            }

            _isProcessingQueue = false;
        }

        /// <summary>
        /// テレメトリをPOST送信
        /// </summary>
        private IEnumerator PostTelemetry(string endpoint, string jsonPayload, Action<bool> onResult)
        {
            string url = serverBaseUrl.TrimEnd('/') + endpoint;

            if (enableDebugLog)
            {
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"Sending to {url}");
            }

            using (var request = new UnityWebRequest(url, "POST"))
            {
                byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("X-API-Key", apiKey);
                request.timeout = timeoutSeconds;

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    AICamLogger.Log(AICamLogger.Category.Telemetry, $"Successfully sent to {endpoint}");

                    if (enableDebugLog)
                    {
                        AICamLogger.Log(AICamLogger.Category.Telemetry, $"Response: {request.downloadHandler.text}");
                    }

                    onResult?.Invoke(true);
                }
                else
                {
                    AICamLogger.LogWarning(AICamLogger.Category.Telemetry,
                        $"Failed to send: {request.error} (HTTP {request.responseCode})");
                    onResult?.Invoke(false);
                }
            }
        }

        /// <summary>
        /// 保留中のリクエストを強制送信
        /// </summary>
        public void FlushPendingRequests()
        {
            if (!_isProcessingQueue && _pendingRequests.Count > 0)
            {
                StartCoroutine(ProcessQueue());
            }
        }

        /// <summary>
        /// 保留中のリクエスト数を取得
        /// </summary>
        public int GetPendingRequestCount()
        {
            return _pendingRequests.Count;
        }

        /// <summary>
        /// 保留中のリクエストをクリア
        /// </summary>
        public void ClearPendingRequests()
        {
            _pendingRequests.Clear();
        }
    }
}
