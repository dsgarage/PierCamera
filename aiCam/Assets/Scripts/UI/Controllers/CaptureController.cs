using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// 撮影ボタンのUI制御（タップ→写真、長押し→動画）を管理するコントローラー。
    /// </summary>
    public class CaptureController
    {
        private readonly VisualElement captureButton;
        private readonly VisualElement innerCircle;
        private readonly VisualElement progressRing;
        private readonly VisualElement progressArc;
        private readonly VisualElement flashOverlay;
        private readonly VisualElement galleryThumbnail;

        private ARPhotoController photoController;
        private readonly System.Action<Texture2D, bool> onOpenViewer;

        private bool isPressed;
        private bool isRecording;
        private float pressTime;
        private const float longPressThreshold = 0.5f;
        private const float maxRecordTime = 5f;
        private bool lastMediaIsVideo;
        private Texture2D lastCapturedPhoto;
        private string lastCapturedVideoPath;

        /// <summary>
        /// 録画中かどうかを取得。
        /// </summary>
        public bool IsRecording => isRecording;

        /// <summary>
        /// UIブロッキング判定用に公開。
        /// </summary>
        public VisualElement CaptureButton => captureButton;
        public VisualElement GalleryThumbnail => galleryThumbnail;

        public CaptureController(VisualElement root, ARPhotoController photoController, System.Action<Texture2D, bool> onOpenViewer)
        {
            this.photoController = photoController;
            this.onOpenViewer = onOpenViewer;

            captureButton = root.Q<VisualElement>("captureButton");
            innerCircle = root.Q<VisualElement>("innerCircle");
            progressRing = root.Q<VisualElement>("progressRing");
            progressArc = root.Q<VisualElement>("progressArc");
            flashOverlay = root.Q<VisualElement>("flashOverlay");
            galleryThumbnail = root.Q<VisualElement>("galleryThumbnail");

            if (captureButton != null)
            {
                captureButton.RegisterCallback<PointerDownEvent>(OnPointerDown);
                captureButton.RegisterCallback<PointerUpEvent>(OnPointerUp);
            }

            if (galleryThumbnail != null)
            {
                galleryThumbnail.RegisterCallback<ClickEvent>(evt =>
                    onOpenViewer?.Invoke(lastCapturedPhoto, lastMediaIsVideo));
            }

            if (photoController != null)
            {
                photoController.OnPhotoCaptured += OnPhotoCapturedHandler;
            }
        }

        /// <summary>
        /// ARPhotoControllerを差し替える。
        /// </summary>
        public void SetPhotoController(ARPhotoController controller)
        {
            if (photoController != null)
            {
                photoController.OnPhotoCaptured -= OnPhotoCapturedHandler;
            }

            photoController = controller;

            if (photoController != null)
            {
                photoController.OnPhotoCaptured += OnPhotoCapturedHandler;
            }
        }

        /// <summary>
        /// 最後にキャプチャした写真のサムネイルを更新。
        /// </summary>
        public void UpdateLastCapturedPhoto(Texture2D photo)
        {
            lastCapturedPhoto = photo;
            lastMediaIsVideo = false;

            if (galleryThumbnail != null && photo != null)
            {
                galleryThumbnail.style.backgroundImage = new StyleBackground(photo);
            }
        }

        /// <summary>
        /// 撮影ボタンの状態更新。CCC.Update() から呼ぶ。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (!isPressed) return;

            pressTime += deltaTime;

            if (!isRecording && pressTime >= longPressThreshold)
            {
                StartRecording();
            }

            if (isRecording)
            {
                float recordTime = pressTime - longPressThreshold;
                float progress = Mathf.Clamp01(recordTime / maxRecordTime);

                UpdateProgressRing(progress);

                if (progress >= 1f)
                {
                    isPressed = false;
                    StopRecording();
                }
            }
        }

        /// <summary>
        /// イベント登録を解除する。CCC.OnDisable() から呼ぶ。
        /// </summary>
        public void Dispose()
        {
            if (photoController != null)
            {
                photoController.OnPhotoCaptured -= OnPhotoCapturedHandler;
            }
        }

        private void OnPhotoCapturedHandler(Texture2D thumbnail)
        {
            Debug.Log("📸 Photo captured, updating thumbnail");
            lastCapturedPhoto = thumbnail;
            lastMediaIsVideo = false;

            if (galleryThumbnail != null)
            {
                galleryThumbnail.style.backgroundImage = new StyleBackground(thumbnail);
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            Debug.Log("👇 OnPointerDown triggered");
            isPressed = true;
            pressTime = 0f;

            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            Debug.Log($"👆 OnPointerUp triggered (pressTime: {pressTime}s, isRecording: {isRecording})");
            isPressed = false;

            if (isRecording)
            {
                StopRecording();
            }
            else if (pressTime < longPressThreshold)
            {
                TakePhoto();
            }

            pressTime = 0f;
        }

        private void StartRecording()
        {
            isRecording = true;
            Debug.Log("🎬 録画開始");

            innerCircle?.AddToClassList("recording");
            progressRing?.AddToClassList("active");

            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        private void TakePhoto()
        {
            Debug.Log("📸 写真撮影");
            FlashEffect();

            if (photoController != null)
            {
                photoController.Capture();
            }
            else
            {
                Debug.LogWarning("ARPhotoController is not assigned");
            }

            ResetButtonState();

            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        private void StopRecording()
        {
            if (!isRecording) return;

            Debug.Log("🎥 動画撮影終了");
            FlashEffect();

            lastMediaIsVideo = true;
            lastCapturedVideoPath = Application.persistentDataPath + "/lastVideo.mp4";

            Texture2D dummyFrame = new Texture2D(64, 64);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.red;
            }
            dummyFrame.SetPixels(pixels);
            dummyFrame.Apply();

            if (galleryThumbnail != null)
            {
                galleryThumbnail.style.backgroundImage = new StyleBackground(dummyFrame);
            }

            ResetButtonState();
            isRecording = false;
        }

        private void FlashEffect()
        {
            if (flashOverlay == null) return;

            flashOverlay.style.opacity = 1;
            flashOverlay.schedule.Execute(() =>
            {
                flashOverlay.style.opacity = 0;
            }).StartingIn(100);
        }

        private void UpdateProgressRing(float progress)
        {
            if (progressArc == null) return;

            Color red = new Color(1f, 0f, 0f, 1f);
            Color transparent = new Color(0f, 0f, 0f, 0f);

            float angle = progress * 360f;

            Color topColor;
            if (angle < 90f)
            {
                topColor = Color.Lerp(transparent, red, angle / 90f);
            }
            else
            {
                topColor = red;
            }

            Color rightColor;
            if (angle < 90f)
            {
                rightColor = transparent;
            }
            else if (angle < 180f)
            {
                topColor = red;
                rightColor = Color.Lerp(transparent, red, (angle - 90f) / 90f);
            }
            else
            {
                topColor = red;
                rightColor = red;
            }

            Color bottomColor;
            if (angle < 180f)
            {
                bottomColor = transparent;
            }
            else if (angle < 270f)
            {
                topColor = red;
                rightColor = red;
                bottomColor = Color.Lerp(transparent, red, (angle - 180f) / 90f);
            }
            else
            {
                topColor = red;
                rightColor = red;
                bottomColor = red;
            }

            Color leftColor;
            if (angle < 270f)
            {
                leftColor = transparent;
            }
            else
            {
                topColor = red;
                rightColor = red;
                bottomColor = red;
                leftColor = Color.Lerp(transparent, red, (angle - 270f) / 90f);
            }

            progressArc.style.borderTopColor = topColor;
            progressArc.style.borderRightColor = rightColor;
            progressArc.style.borderBottomColor = bottomColor;
            progressArc.style.borderLeftColor = leftColor;
        }

        private void ResetButtonState()
        {
            innerCircle?.RemoveFromClassList("recording");
            progressRing?.RemoveFromClassList("active");

            if (progressArc != null)
            {
                Color transparent = new Color(0f, 0f, 0f, 0f);
                progressArc.style.borderTopColor = transparent;
                progressArc.style.borderRightColor = transparent;
                progressArc.style.borderBottomColor = transparent;
                progressArc.style.borderLeftColor = transparent;
            }
        }
    }
}
