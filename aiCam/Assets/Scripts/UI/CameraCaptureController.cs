using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// UIToolkit版のカメラ撮影コントローラー
    /// タップで写真撮影、長押しで動画撮影を行う
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CameraCaptureController : MonoBehaviour
    {
        [Header("Capture Settings")]
        [SerializeField] private ARPhotoController photoController;

        private VisualElement captureButton;
        private VisualElement innerCircle;
        private VisualElement progressRing;
        private VisualElement progressArc;
        private VisualElement flashOverlay;
        private VisualElement galleryThumbnail;

        // ビューア要素
        private VisualElement viewerOverlay;
        private Image viewerImage;

        // パネル要素
        private VisualElement topPanel;
        private VisualElement bottomPanel;
        private VisualElement bottomButtonContainer;
        private Button bottomButtonAdd;
        private int bottomButtonCount = 3;

        private bool isPressed = false;
        private bool isRecording = false;
        private float pressTime = 0f;
        private const float longPressThreshold = 0.5f;
        private const float maxRecordTime = 5f;
        private bool lastMediaIsVideo = false;

        private Texture2D lastCapturedPhoto;
        private string lastCapturedVideoPath;

        void OnEnable()
        {
            Debug.Log("🔧 CameraCaptureController OnEnable called");

            var uiDoc = GetComponent<UIDocument>();
            if (uiDoc == null)
            {
                Debug.LogError("❌ UIDocument component not found!");
                return;
            }

            var root = uiDoc.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("❌ Root VisualElement is null!");
                return;
            }

            Debug.Log($"✅ Root element found: {root.name}");

            captureButton = root.Q<VisualElement>("captureButton");
            innerCircle = root.Q<VisualElement>("innerCircle");
            progressRing = root.Q<VisualElement>("progressRing");
            progressArc = root.Q<VisualElement>("progressArc");
            flashOverlay = root.Q<VisualElement>("flashOverlay");
            galleryThumbnail = root.Q<VisualElement>("galleryThumbnail");

            viewerOverlay = root.Q<VisualElement>("viewerOverlay");
            viewerImage = root.Q<Image>("viewerImage");

            topPanel = root.Q<VisualElement>("topPanel");
            bottomPanel = root.Q<VisualElement>("bottomPanel");
            bottomButtonContainer = root.Q<VisualElement>("bottomButtonContainer");
            bottomButtonAdd = root.Q<Button>("bottomButtonAdd");

            // ScrollViewの設定（物理スクロール対応）
            var bottomScrollView = root.Q<ScrollView>("bottomScrollView");
            if (bottomScrollView != null)
            {
                bottomScrollView.mode = ScrollViewMode.Horizontal;

                // 実機用：スクロールバー非表示、エディタ用：Auto表示
#if UNITY_EDITOR
                bottomScrollView.horizontalScrollerVisibility = ScrollerVisibility.Auto;
#else
                bottomScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
#endif
                bottomScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;

                // 物理スクロール設定
                bottomScrollView.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
                bottomScrollView.elasticity = 0.1f;
                bottomScrollView.scrollDecelerationRate = 0.135f;

                // 横スクロールのみ有効化
                bottomScrollView.horizontalPageSize = 0;
                bottomScrollView.verticalPageSize = 0;
                bottomScrollView.nestedInteractionKind = ScrollView.NestedInteractionKind.Default;

                // ContentContainerのflex設定を強制
                bottomScrollView.contentContainer.style.flexDirection = FlexDirection.Row;
                bottomScrollView.contentContainer.style.flexWrap = Wrap.NoWrap;

                // マウスホイールスクロール（エディタ用）
                bottomScrollView.mouseWheelScrollSize = 30f;

                Debug.Log($"✅ ScrollView configured: mode={bottomScrollView.mode}, touchBehavior={bottomScrollView.touchScrollBehavior}");
            }

            Debug.Log($"captureButton: {(captureButton != null ? "✅" : "❌")}");
            Debug.Log($"innerCircle: {(innerCircle != null ? "✅" : "❌")}");
            Debug.Log($"progressRing: {(progressRing != null ? "✅" : "❌")}");
            Debug.Log($"progressArc: {(progressArc != null ? "✅" : "❌")}");
            Debug.Log($"flashOverlay: {(flashOverlay != null ? "✅" : "❌")}");
            Debug.Log($"galleryThumbnail: {(galleryThumbnail != null ? "✅" : "❌")}");
            Debug.Log($"viewerOverlay: {(viewerOverlay != null ? "✅" : "❌")}");
            Debug.Log($"viewerImage: {(viewerImage != null ? "✅" : "❌")}");

            if (captureButton != null)
            {
                captureButton.RegisterCallback<PointerDownEvent>(OnPointerDown);
                captureButton.RegisterCallback<PointerUpEvent>(OnPointerUp);
                captureButton.RegisterCallback<ClickEvent>(evt => Debug.Log("🖱 Capture button clicked!"));
                Debug.Log("✅ Capture button events registered");
            }
            else
            {
                Debug.LogError("❌ captureButton is null - cannot register events");
            }

            if (galleryThumbnail != null)
            {
                galleryThumbnail.RegisterCallback<ClickEvent>(evt => OpenViewer());
                Debug.Log("✅ Gallery thumbnail events registered");
            }

            if (viewerOverlay != null)
            {
                viewerOverlay.RegisterCallback<ClickEvent>(evt => CloseViewer());
                Debug.Log("✅ Viewer overlay events registered");
            }

            if (bottomButtonAdd != null)
            {
                bottomButtonAdd.RegisterCallback<ClickEvent>(evt => AddBottomPanelButton());
                Debug.Log("✅ Add button events registered");
            }
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            Debug.Log("👇 OnPointerDown triggered");
            isPressed = true;
            pressTime = 0f;

#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            Debug.Log($"👆 OnPointerUp triggered (pressTime: {pressTime}s, isRecording: {isRecording})");
            isPressed = false;

            if (isRecording)
            {
                // 録画中だった場合は停止
                StopRecording();
            }
            else if (pressTime < longPressThreshold)
            {
                // 短押しの場合は写真撮影
                TakePhoto();
            }

            pressTime = 0f;
        }

        void Update()
        {
            if (isPressed)
            {
                pressTime += Time.deltaTime;

                // 長押し判定: 0.5秒経過したら録画開始
                if (!isRecording && pressTime >= longPressThreshold)
                {
                    StartRecording();
                }

                // 録画中の処理
                if (isRecording)
                {
                    float recordTime = pressTime - longPressThreshold;
                    float progress = Mathf.Clamp01(recordTime / maxRecordTime);

                    UpdateProgressRing(progress);

                    // 最大録画時間に達したら自動停止
                    if (progress >= 1f)
                    {
                        isPressed = false;
                        StopRecording();
                    }
                }
            }
        }

        void StartRecording()
        {
            isRecording = true;
            Debug.Log("🎬 録画開始");

            // UIの状態変更
            innerCircle?.AddToClassList("recording");
            progressRing?.AddToClassList("active");

#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        void TakePhoto()
        {
            Debug.Log("📸 写真撮影");
            FlashEffect();

            lastMediaIsVideo = false;

            if (photoController != null)
            {
                photoController.Capture();
            }
            else
            {
                Debug.LogWarning("ARPhotoController is not assigned");
            }

            // 仮の白画像をサムネイルに設定
            lastCapturedPhoto = new Texture2D(64, 64);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }
            lastCapturedPhoto.SetPixels(pixels);
            lastCapturedPhoto.Apply();

            if (galleryThumbnail != null)
            {
                galleryThumbnail.style.backgroundImage = new StyleBackground(lastCapturedPhoto);
            }

            ResetButtonState();

#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }

        void StopRecording()
        {
            if (!isRecording) return;

            Debug.Log("🎥 動画撮影終了");
            FlashEffect();

            lastMediaIsVideo = true;
            lastCapturedVideoPath = Application.persistentDataPath + "/lastVideo.mp4";

            // 仮の赤サムネイル
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

        void FlashEffect()
        {
            if (flashOverlay == null) return;

            flashOverlay.style.opacity = 1;
            flashOverlay.schedule.Execute(() =>
            {
                flashOverlay.style.opacity = 0;
            }).StartingIn(100);
        }

        void UpdateProgressRing(float progress)
        {
            if (progressArc == null) return;

            // 連続的な円形プログレス表示（12時位置から時計回り）
            // rotate: -90degにより、12時位置スタート
            // 進行度を360度の角度に変換し、各辺の表示を細かく制御

            Color red = new Color(1f, 0f, 0f, 1f);
            Color transparent = new Color(0f, 0f, 0f, 0f);

            // 進行度を角度に変換（0-360度）
            float angle = progress * 360f;

            // 各辺は90度ずつ担当
            // rotate: -90deg により: top=12時~3時, right=3時~6時, bottom=6時~9時, left=9時~12時

            // 上辺 (0-90度 = 12時~3時)
            Color topColor;
            if (angle < 90f)
            {
                // 0-90度の範囲で線形補間
                topColor = Color.Lerp(transparent, red, angle / 90f);
            }
            else
            {
                topColor = red;
            }

            // 右辺 (90-180度 = 3時~6時)
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

            // 下辺 (180-270度 = 6時~9時)
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

            // 左辺 (270-360度 = 9時~12時)
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

        void ResetButtonState()
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

        void OpenViewer()
        {
            Debug.Log("🖼 OpenViewer called");

            if (viewerOverlay == null)
            {
                Debug.LogWarning("⚠️ viewerOverlay is null");
                return;
            }

            viewerOverlay.style.display = DisplayStyle.Flex;
            Debug.Log("✅ Viewer opened");

            if (lastMediaIsVideo)
            {
                // 動画の場合は今後実装
                Debug.Log("📹 Video mode (not implemented)");
                if (viewerImage != null)
                {
                    viewerImage.style.display = DisplayStyle.None;
                }
            }
            else
            {
                if (viewerImage != null && lastCapturedPhoto != null)
                {
                    viewerImage.style.display = DisplayStyle.Flex;
                    viewerImage.image = lastCapturedPhoto;
                    Debug.Log("✅ Photo displayed in viewer");
                }
                else
                {
                    Debug.LogWarning("⚠️ No photo to display");
                }
            }
        }

        void CloseViewer()
        {
            Debug.Log("✋ CloseViewer called");

            if (viewerOverlay != null)
            {
                viewerOverlay.style.display = DisplayStyle.None;
                Debug.Log("✅ Viewer closed");
            }
            else
            {
                Debug.LogWarning("⚠️ viewerOverlay is null");
            }
        }

        /// <summary>
        /// ARPhotoControllerを設定（外部から呼び出し可能）
        /// </summary>
        public void SetPhotoController(ARPhotoController controller)
        {
            photoController = controller;
        }

        /// <summary>
        /// 録画中かどうかを取得
        /// </summary>
        public bool IsRecording => isRecording;

        /// <summary>
        /// 最後にキャプチャした写真のサムネイルを更新
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
        /// 下部パネルにボタンを追加
        /// </summary>
        void AddBottomPanelButton()
        {
            if (bottomButtonContainer == null)
            {
                Debug.LogWarning("⚠️ bottomButtonContainer is null");
                return;
            }

            bottomButtonCount++;
            Debug.Log($"➕ Adding bottom panel button #{bottomButtonCount}");

            // 新しいボタンを作成
            var newButton = new Button();
            newButton.name = $"bottomButton{bottomButtonCount}";
            newButton.AddToClassList("bottom-panel-button");

            // +ボタンの直前に挿入
            int addButtonIndex = bottomButtonContainer.IndexOf(bottomButtonAdd);
            bottomButtonContainer.Insert(addButtonIndex, newButton);

            // ボタンのクリックイベントを登録
            newButton.RegisterCallback<ClickEvent>(evt =>
            {
                Debug.Log($"🔘 Bottom button #{newButton.name} clicked");
            });

#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#endif
        }
    }
}