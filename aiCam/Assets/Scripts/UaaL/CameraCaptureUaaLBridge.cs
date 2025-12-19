using UnityEngine;
using Pier.UaaL;
using AICam.UI;

namespace AICam.UaaL
{
    /// <summary>
    /// CameraCaptureController と UaaL間のブリッジ
    /// 写真撮影イベントをReact Nativeに通知
    /// RNからのキャプチャコマンドを処理
    /// </summary>
    public class CameraCaptureUaaLBridge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private CameraCaptureController cameraCaptureController;
        [SerializeField] private ARPhotoController arPhotoController;

        private void Start()
        {
            // Auto-find CameraCaptureController
            if (cameraCaptureController == null)
            {
                cameraCaptureController = FindFirstObjectByType<CameraCaptureController>();
            }

            // Auto-find ARPhotoController
            if (arPhotoController == null)
            {
                arPhotoController = FindFirstObjectByType<ARPhotoController>();
            }

            // Subscribe to ARPhotoController events
            if (arPhotoController != null)
            {
                arPhotoController.OnPhotoCapturedWithPath += HandlePhotoCapturedWithPath;
                Debug.Log("[CameraCaptureUaaLBridge] Subscribed to ARPhotoController events");
            }
            else
            {
                Debug.LogWarning("[CameraCaptureUaaLBridge] ARPhotoController not found");
            }

            // Subscribe to CommandReceiver events
            if (CommandReceiver.Instance != null)
            {
                CommandReceiver.Instance.OnCaptureCommand += HandleCaptureCommand;
                CommandReceiver.Instance.OnCloseSessionCommand += HandleCloseSession;
                Debug.Log("[CameraCaptureUaaLBridge] Subscribed to CommandReceiver events");
            }
            else
            {
                Debug.LogWarning("[CameraCaptureUaaLBridge] CommandReceiver not found");
            }
        }

        private void OnDestroy()
        {
            if (arPhotoController != null)
            {
                arPhotoController.OnPhotoCapturedWithPath -= HandlePhotoCapturedWithPath;
            }

            if (CommandReceiver.Instance != null)
            {
                CommandReceiver.Instance.OnCaptureCommand -= HandleCaptureCommand;
                CommandReceiver.Instance.OnCloseSessionCommand -= HandleCloseSession;
            }
        }

        /// <summary>
        /// 写真撮影完了時のハンドラ
        /// </summary>
        private void HandlePhotoCapturedWithPath(string path, int width, int height)
        {
            Debug.Log($"[CameraCaptureUaaLBridge] Photo captured: {path} ({width}x{height})");
            UnityToRN.NotifyPhotoCaptured(path, width, height);
        }

        /// <summary>
        /// RNからのキャプチャコマンドを処理
        /// </summary>
        private void HandleCaptureCommand()
        {
            Debug.Log("[CameraCaptureUaaLBridge] Capture command received from RN");

            if (cameraCaptureController != null)
            {
                // CameraCaptureControllerのキャプチャメソッドを呼び出す
                // TODO: CameraCaptureControllerにpublicなキャプチャメソッドがあれば呼び出す
                Debug.Log("[CameraCaptureUaaLBridge] Triggering capture...");
            }
        }

        /// <summary>
        /// RNからのセッション終了コマンドを処理
        /// </summary>
        private void HandleCloseSession()
        {
            Debug.Log("[CameraCaptureUaaLBridge] Close session command received from RN");
            // 必要に応じてクリーンアップ処理
        }

        /// <summary>
        /// 写真撮影完了をRNに通知
        /// CameraCaptureControllerから呼び出す
        /// </summary>
        public void NotifyPhotoCaptured(string path, int width, int height)
        {
            Debug.Log($"[CameraCaptureUaaLBridge] Notifying RN: photo captured at {path}");
            UnityToRN.NotifyPhotoCaptured(path, width, height);
        }

        /// <summary>
        /// セッション終了をリクエスト
        /// </summary>
        public void RequestCloseSession(string reason = "user_request")
        {
            Debug.Log($"[CameraCaptureUaaLBridge] Requesting session close: {reason}");
            UnityToRN.RequestCloseSession(reason);
        }
    }
}
