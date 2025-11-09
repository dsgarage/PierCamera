using UnityEngine;
using UnityEngine.UIElements;
using NativeFilePickerNamespace;

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

        // 削除ポップアップ関連
        private VisualElement deletePopup;
        private Button deleteButton;
        private Button cancelButton;
        private Button currentLongPressButton;
        private float longPressTime = 0f;
        private const float longPressThresholdForDelete = 0.5f;
        private bool isLongPressing = false;

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

            // ARPhotoControllerのイベント登録
            if (photoController != null)
            {
                photoController.OnPhotoCaptured += OnPhotoCapturedHandler;
            }

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

            // 削除ポップアップを作成（初期状態では非表示）
            Debug.Log("🔧 Creating delete popup...");
            CreateDeletePopup(root);
            Debug.Log($"🔧 Delete popup created: {(deletePopup != null ? "✅" : "❌")}");

            // 既存のボタンに長押しイベントを登録
            Debug.Log("🔧 Registering long press for existing buttons...");
            RegisterLongPressForExistingButtons();
        }

        void OnDisable()
        {
            // ARPhotoControllerのイベント解除
            if (photoController != null)
            {
                photoController.OnPhotoCaptured -= OnPhotoCapturedHandler;
            }
        }

        void OnPhotoCapturedHandler(Texture2D thumbnail)
        {
            Debug.Log("📸 Photo captured, updating thumbnail");
            lastCapturedPhoto = thumbnail;
            lastMediaIsVideo = false;

            if (galleryThumbnail != null)
            {
                galleryThumbnail.style.backgroundImage = new StyleBackground(thumbnail);
            }
        }

        void OnPointerDown(PointerDownEvent evt)
        {
            Debug.Log("👇 OnPointerDown triggered");
            isPressed = true;
            pressTime = 0f;

            // Light impact for button press
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
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

            // アバタースロットボタンの長押し検出
            if (isLongPressing && currentLongPressButton != null)
            {
                longPressTime += Time.deltaTime;

                // デバッグログ（0.1秒ごと）
                if (Mathf.FloorToInt(longPressTime * 10) != Mathf.FloorToInt((longPressTime - Time.deltaTime) * 10))
                {
                    Debug.Log($"⏱ Long press time: {longPressTime:F2}s / {longPressThresholdForDelete}s");
                }

                if (longPressTime >= longPressThresholdForDelete)
                {
                    Debug.Log($"✅ Long press threshold reached! Showing popup for {currentLongPressButton.name}");
                    ShowDeletePopup(currentLongPressButton);
                    isLongPressing = false;
                    longPressTime = 0f;
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

            // Heavy impact for recording start
            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        void TakePhoto()
        {
            Debug.Log("📸 写真撮影");
            FlashEffect();

            if (photoController != null)
            {
                photoController.Capture();
                // サムネイルはOnPhotoCapturedHandlerで更新される
            }
            else
            {
                Debug.LogWarning("ARPhotoController is not assigned");
            }

            ResetButtonState();

            // Medium impact for photo capture
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
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

            // 長押しイベントを登録（ClickEventより先に登録）
            RegisterLongPressForButton(newButton);

            // ボタンのクリックイベントを登録
            newButton.RegisterCallback<ClickEvent>(evt =>
            {
                Debug.Log($"🔘 Bottom button #{newButton.name} clicked");
                TapticEngine.Selection();

                // 空のスロットの場合はファイルピッカーを開く
                OpenFilePicker(newButton);
            });

            // Light impact for button addition
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        /// <summary>
        /// 削除ポップアップを作成
        /// </summary>
        void CreateDeletePopup(VisualElement root)
        {
            deletePopup = new VisualElement();
            deletePopup.name = "deletePopup";
            deletePopup.AddToClassList("delete-popup");

            // 絶対配置を有効化
            deletePopup.style.position = Position.Absolute;
            deletePopup.pickingMode = PickingMode.Position;

            // 削除ボタン
            deleteButton = new Button();
            deleteButton.text = "削除";
            deleteButton.AddToClassList("delete-popup-button");
            deleteButton.AddToClassList("delete");
            deleteButton.RegisterCallback<ClickEvent>(evt => OnDeleteButtonClicked());

            // キャンセルボタン
            cancelButton = new Button();
            cancelButton.text = "キャンセル";
            cancelButton.AddToClassList("delete-popup-button");
            cancelButton.RegisterCallback<ClickEvent>(evt => HideDeletePopup());

            deletePopup.Add(deleteButton);
            deletePopup.Add(cancelButton);
            root.Add(deletePopup);

            Debug.Log("✅ Delete popup created");
        }

        /// <summary>
        /// 既存のアバタースロットボタンに長押しイベントとクリックイベントを登録
        /// </summary>
        void RegisterLongPressForExistingButtons()
        {
            if (bottomButtonContainer == null) return;

            var buttons = bottomButtonContainer.Query<Button>().ToList();
            foreach (var button in buttons)
            {
                // +ボタンは除外
                if (button == bottomButtonAdd) continue;

                RegisterLongPressForButton(button);

                // クリックイベントも登録（ファイルピッカー用）
                button.RegisterCallback<ClickEvent>(evt =>
                {
                    Debug.Log($"🔘 Bottom button #{button.name} clicked");
                    TapticEngine.Selection();

                    // 空のスロットの場合はファイルピッカーを開く
                    OpenFilePicker(button);
                });
            }

            Debug.Log($"✅ Long press and click registered for {buttons.Count - 1} buttons");
        }

        /// <summary>
        /// ボタンに長押しイベントを登録
        /// </summary>
        void RegisterLongPressForButton(Button button)
        {
            // PointerDownEventで長押し開始を検出（TrickleDownフェーズで優先キャプチャ）
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                isLongPressing = true;
                currentLongPressButton = button;
                longPressTime = 0f;
                Debug.Log($"👇 Long press started on {button.name}");
            }, TrickleDown.TrickleDown);

            // PointerUpEventで長押しをキャンセル
            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                Debug.Log($"👆 Long press released on {button.name} (time: {longPressTime:F2}s, isLongPressing: {isLongPressing})");

                // 短押しの場合はClickEventに任せる
                if (longPressTime < longPressThresholdForDelete)
                {
                    Debug.Log($"📌 Short press detected, allowing click event");
                }
                else
                {
                    Debug.Log($"⏱ Long press detected, suppressing click event");
                    evt.StopPropagation();
                }

                isLongPressing = false;
                longPressTime = 0f;
            }, TrickleDown.TrickleDown);

            // PointerLeaveEventで長押しをキャンセル（ボタンから離れた場合）
            button.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                if (isLongPressing)
                {
                    Debug.Log($"↖️ Pointer left {button.name}, cancelling long press");
                    isLongPressing = false;
                    longPressTime = 0f;
                }
            });
        }

        /// <summary>
        /// 削除ポップアップを表示
        /// </summary>
        void ShowDeletePopup(Button targetButton)
        {
            if (deletePopup == null)
            {
                Debug.LogError("❌ deletePopup is null!");
                return;
            }

            if (targetButton == null)
            {
                Debug.LogError("❌ targetButton is null!");
                return;
            }

            // ポップアップをボタンの上部に配置
            var buttonBounds = targetButton.worldBound;
            Debug.Log($"📍 Button bounds: x={buttonBounds.x}, y={buttonBounds.y}, width={buttonBounds.width}, height={buttonBounds.height}");

            // ポップアップサイズ: 120px x 80px (USSで定義)
            float popupWidth = 120f;
            float popupHeight = 90f; // 少し余裕を持たせる

            // ボタンの中央にポップアップを配置（水平方向）
            float popupLeft = buttonBounds.x + (buttonBounds.width / 2) - (popupWidth / 2);

            // ボタンの上に配置（垂直方向） - 10pxの余白
            float popupTop = buttonBounds.y - popupHeight - 10;

            Debug.Log($"📍 Popup position: left={popupLeft}, top={popupTop}");

            deletePopup.style.left = popupLeft;
            deletePopup.style.top = popupTop;
            deletePopup.style.display = DisplayStyle.Flex;

            Debug.Log($"📋 Delete popup shown for {targetButton.name}");
            Debug.Log($"📋 Popup display style: {deletePopup.style.display}");
            Debug.Log($"📋 Popup position type: {deletePopup.style.position}");

            // Heavy impact for popup appearance
            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        /// <summary>
        /// 削除ポップアップを非表示
        /// </summary>
        void HideDeletePopup()
        {
            if (deletePopup == null) return;

            deletePopup.style.display = DisplayStyle.None;
            currentLongPressButton = null;
            Debug.Log("❌ Delete popup hidden");

            // Light impact for cancel
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        /// <summary>
        /// 削除ボタンがクリックされた時の処理
        /// </summary>
        void OnDeleteButtonClicked()
        {
            if (currentLongPressButton == null || bottomButtonContainer == null)
            {
                HideDeletePopup();
                return;
            }

            Debug.Log($"🗑 Deleting button: {currentLongPressButton.name}");

            // ボタンを削除
            bottomButtonContainer.Remove(currentLongPressButton);
            HideDeletePopup();

            // Medium impact for deletion
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// Check if screen position is over UI Toolkit panel (top or bottom)
        /// </summary>
        public bool IsPointOverUIPanel(Vector2 screenPosition)
        {
            if (topPanel != null && topPanel.worldBound.Contains(screenPosition))
            {
                return true;
            }

            if (bottomPanel != null && bottomPanel.worldBound.Contains(screenPosition))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// ファイルピッカーを開く（VRMファイル選択用）
        /// </summary>
        void OpenFilePicker(Button targetButton)
        {
            Debug.Log($"📂 Opening file picker for button: {targetButton.name}");

            NativeFilePicker.PickFile((path) =>
            {
                if (path == null)
                {
                    Debug.Log("❌ File picker cancelled");
                    return;
                }

                Debug.Log($"✅ File selected: {path}");

                // ここにVRMファイル読み込み処理を追加
                // 現在は選択されたパスをログに出力するだけ
                TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
            }, new string[] { ".vrm" });
        }
    }
}