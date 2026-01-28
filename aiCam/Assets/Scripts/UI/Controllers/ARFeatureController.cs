using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;

namespace AICam.UI
{
    /// <summary>
    /// AR機能のトグル制御（平面表示、トーチ）を管理するコントローラー。
    /// </summary>
    public class ARFeatureController
    {
        private readonly Button topButton5;
        private readonly Button sideButton3;
        private readonly bool enableDebugLogging;
        private readonly System.Action<string, string> onWarning;

        private bool isPlaneVisible = true;
        private bool isTorchEnabled;
        private ARPlaneVisibilityController cachedPlaneVisibilityController;
        private ARCameraManager cachedARCameraManager;

        public ARFeatureController(
            VisualElement root,
            bool enableDebugLogging,
            System.Action<string, string> onWarning)
        {
            this.enableDebugLogging = enableDebugLogging;
            this.onWarning = onWarning;

            var sideButton1 = root.Q<Button>("sideButton1");
            sideButton3 = root.Q<Button>("sideButton3");
            topButton5 = root.Q<Button>("topButton5");

            if (sideButton1 != null)
            {
                sideButton1.RegisterCallback<ClickEvent>(evt => OnSideButton1Clicked());
                if (enableDebugLogging) Debug.Log("✅ Side button 1 events registered");
            }

            if (sideButton3 != null)
            {
                sideButton3.RegisterCallback<ClickEvent>(evt => OnSideButton3Clicked());
                if (enableDebugLogging) Debug.Log("✅ Side button 3 events registered");
            }

            if (enableDebugLogging) Debug.Log($"🔘 topButton5: {(topButton5 != null ? "✅ found" : "❌ NOT FOUND")}");
            if (topButton5 != null)
            {
                topButton5.RegisterCallback<ClickEvent>(evt => OnTopButton5Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 5 (Plane Visibility) click event registered");

                UpdatePlaneVisibilityIcon();
            }
        }

        private void OnSideButton1Clicked()
        {
            Debug.Log("⚙️ Side button 1 (Preference) clicked");
            TapticEngine.Selection();
        }

        private void OnSideButton3Clicked()
        {
            Debug.Log("⚡ Side button 3 (Flash) clicked");
            TapticEngine.Selection();

            if (cachedARCameraManager == null)
            {
                cachedARCameraManager = Object.FindFirstObjectByType<ARCameraManager>();
            }

            if (cachedARCameraManager == null)
            {
                Debug.LogWarning("[Torch] ARCameraManager not found");
                onWarning?.Invoke("W452", "カメラが見つかりません");
                return;
            }

            isTorchEnabled = !isTorchEnabled;

            cachedARCameraManager.requestedCameraTorchMode = isTorchEnabled
                ? UnityEngine.XR.ARSubsystems.XRCameraTorchMode.On
                : UnityEngine.XR.ARSubsystems.XRCameraTorchMode.Off;

            Debug.Log($"[Torch] Torch mode set to: {(isTorchEnabled ? "ON" : "OFF")}");

            UpdateTorchIcon();
        }

        private void UpdateTorchIcon()
        {
            if (sideButton3 == null) return;

            if (isTorchEnabled)
            {
                sideButton3.RemoveFromClassList("torch-off");
                sideButton3.AddToClassList("torch-on");
            }
            else
            {
                sideButton3.RemoveFromClassList("torch-on");
                sideButton3.AddToClassList("torch-off");
            }
        }

        private void OnTopButton5Click()
        {
            Debug.Log($"🔲 Top button 5 clicked: Toggle Plane Visibility (current: {isPlaneVisible})");
            TapticEngine.Selection();
            TogglePlaneVisibility();
        }

        private void TogglePlaneVisibility()
        {
            isPlaneVisible = !isPlaneVisible;
            Debug.Log($"🔲 Plane visibility toggled to: {isPlaneVisible}");

            if (cachedPlaneVisibilityController == null)
            {
                cachedPlaneVisibilityController = Object.FindFirstObjectByType<ARPlaneVisibilityController>();
            }

            if (cachedPlaneVisibilityController != null)
            {
                cachedPlaneVisibilityController.SetPlanesVisible(isPlaneVisible);
                Debug.Log($"✅ Plane visibility set to: {isPlaneVisible}");
            }
            else
            {
                Debug.LogWarning("⚠️ ARPlaneVisibilityController not found in scene");
            }

            UpdatePlaneVisibilityIcon();
        }

        private void UpdatePlaneVisibilityIcon()
        {
            if (topButton5 == null) return;

            if (isPlaneVisible)
            {
                topButton5.RemoveFromClassList("plane-hidden");
                topButton5.AddToClassList("plane-visible");
            }
            else
            {
                topButton5.RemoveFromClassList("plane-visible");
                topButton5.AddToClassList("plane-hidden");
            }

            Debug.Log($"🔲 Plane button icon updated: {(isPlaneVisible ? "visible" : "hidden")}");
        }
    }
}
