using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;

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

        [Header("VRM Avatar")]
        [SerializeField] private AICam.VRM.VRMAvatarManager vrmAvatarManager;
        [SerializeField] private AICam.VRM.AvatarInstanceManager avatarInstanceManager;
        [SerializeField] private PlaceAvatarOnPlaneOnly placeAvatarOnPlaneOnly;

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

        // ファイル選択ポップアップ関連
        private VisualElement fileLoadPopup;
        private Button loadFromLocalButton;
        private Button loadFromPhotoLibraryButton;
        private Button fileLoadCancelButton;
        private Button currentEmptySlotButton;

        // アバタースロット管理
        private Dictionary<string, AvatarSlotData> avatarSlots = new Dictionary<string, AvatarSlotData>();

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

            // VRMAvatarManagerが未割り当ての場合、シーンから検索または作成
            if (vrmAvatarManager == null)
            {
                Debug.Log("🔍 VRMAvatarManager not assigned, searching in scene...");
                vrmAvatarManager = FindObjectOfType<AICam.VRM.VRMAvatarManager>();

                if (vrmAvatarManager != null)
                {
                    Debug.Log($"✅ Found VRMAvatarManager: {vrmAvatarManager.name}");
                }
                else
                {
                    Debug.Log("🏗️ VRMAvatarManager not found, creating new one...");
                    var go = new GameObject("VRMAvatarManager");
                    vrmAvatarManager = go.AddComponent<AICam.VRM.VRMAvatarManager>();
                    Debug.Log($"✅ Created VRMAvatarManager: {go.name}");
                }
            }

            // AvatarInstanceManagerが未割り当ての場合、シーンから検索
            if (avatarInstanceManager == null)
            {
                Debug.Log("🔍 AvatarInstanceManager not assigned, searching in scene...");
                avatarInstanceManager = FindObjectOfType<AICam.VRM.AvatarInstanceManager>();

                if (avatarInstanceManager != null)
                {
                    Debug.Log($"✅ Found AvatarInstanceManager: {avatarInstanceManager.name}");
                }
                else
                {
                    Debug.LogError("❌ AvatarInstanceManager not found in scene!");
                    Debug.LogError("❌ Avatar instantiation will not work. Please add AvatarInstanceManager to the scene.");
                }
            }

            // PlaceAvatarOnPlaneOnlyが未割り当ての場合、シーンから検索
            if (placeAvatarOnPlaneOnly == null)
            {
                Debug.Log("🔍 PlaceAvatarOnPlaneOnly not assigned, searching in scene...");
                placeAvatarOnPlaneOnly = FindObjectOfType<PlaceAvatarOnPlaneOnly>();

                if (placeAvatarOnPlaneOnly != null)
                {
                    Debug.Log($"✅ Found PlaceAvatarOnPlaneOnly: {placeAvatarOnPlaneOnly.name}");
                }
                else
                {
                    Debug.LogError("❌ PlaceAvatarOnPlaneOnly not found in scene!");
                    Debug.LogError("❌ VRM avatar placement will not work. Please add PlaceAvatarOnPlaneOnly to the scene.");
                }
            }

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

            // ファイルロードポップアップを作成（初期状態では非表示）
            Debug.Log("🔧 Creating file load popup...");
            CreateFileLoadPopup(root);
            Debug.Log($"🔧 File load popup created: {(fileLoadPopup != null ? "✅" : "❌")}");

            // 既存のボタンに長押しイベントを登録
            Debug.Log("🔧 Registering long press for existing buttons...");
            RegisterLongPressForExistingButtons();

            // 保存されたスロットデータを読み込み
            LoadSavedAvatarSlots();
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

            // ボタンのクリックイベントを登録（アバタースロット機能）
            newButton.RegisterCallback<ClickEvent>(evt =>
            {
                Debug.Log($"🔘 Bottom button #{newButton.name} clicked");
                OnAvatarSlotClicked(newButton);
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
        /// 既存のアバタースロットボタンに長押しイベントを登録
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

                // クリックイベントも登録（アバタースロット機能）
                button.RegisterCallback<ClickEvent>(evt =>
                {
                    Debug.Log($"🔘 Existing button #{button.name} clicked");
                    OnAvatarSlotClicked(button);
                });
            }

            Debug.Log($"✅ Long press and click events registered for {buttons.Count - 1} buttons");
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

            // スロットデータを削除
            if (avatarSlots.ContainsKey(currentLongPressButton.name))
            {
                AvatarSlotPersistence.DeleteSlot(currentLongPressButton.name);
                avatarSlots.Remove(currentLongPressButton.name);
                Debug.Log($"🗑 Removed slot data for: {currentLongPressButton.name}");
            }

            // 現在のアバターをクリア（テンプレート＋インスタンス）
            if (avatarInstanceManager != null)
            {
                var currentInstance = avatarInstanceManager.CurrentInstance;
                if (currentInstance != null)
                {
                    Debug.Log($"🗑 Clearing current avatar instance: {currentInstance.name}");
                    avatarInstanceManager.ClearInstance();
                }
            }

            // VRMAvatarManagerのテンプレートもクリア
            if (vrmAvatarManager != null)
            {
                vrmAvatarManager.ClearCurrentAvatar();
            }

            // ボタンを削除
            bottomButtonContainer.Remove(currentLongPressButton);
            HideDeletePopup();

            // Medium impact for deletion
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// ファイル選択ポップアップを作成
        /// </summary>
        void CreateFileLoadPopup(VisualElement root)
        {
            fileLoadPopup = new VisualElement();
            fileLoadPopup.name = "fileLoadPopup";
            fileLoadPopup.AddToClassList("file-load-popup");

            // 絶対配置を有効化
            fileLoadPopup.style.position = Position.Absolute;
            fileLoadPopup.pickingMode = PickingMode.Position;

            // ローカルファイルからロードボタン
            loadFromLocalButton = new Button();
            loadFromLocalButton.text = "ローカルから読み込み";
            loadFromLocalButton.AddToClassList("file-load-popup-button");
            loadFromLocalButton.AddToClassList("primary");
            loadFromLocalButton.RegisterCallback<ClickEvent>(evt =>
            {
                Debug.Log("📁 Local button clicked in popup");
                OnLoadFromLocalClicked();
            });

            // フォトライブラリからロードボタン
            loadFromPhotoLibraryButton = new Button();
            loadFromPhotoLibraryButton.text = "写真から読み込み";
            loadFromPhotoLibraryButton.AddToClassList("file-load-popup-button");
            loadFromPhotoLibraryButton.RegisterCallback<ClickEvent>(evt =>
            {
                Debug.Log("🖼 Photo library button clicked in popup");
                OnLoadFromPhotoLibraryClicked();
            });

            // キャンセルボタン
            fileLoadCancelButton = new Button();
            fileLoadCancelButton.text = "キャンセル";
            fileLoadCancelButton.AddToClassList("file-load-popup-button");
            fileLoadCancelButton.RegisterCallback<ClickEvent>(evt =>
            {
                Debug.Log("❌ Cancel button clicked in popup");
                HideFileLoadPopup(clearButton: true); // キャンセル時はボタン参照もクリア
            });

            fileLoadPopup.Add(loadFromLocalButton);
            fileLoadPopup.Add(loadFromPhotoLibraryButton);
            fileLoadPopup.Add(fileLoadCancelButton);
            root.Add(fileLoadPopup);

            Debug.Log("✅ File load popup created");
        }

        /// <summary>
        /// ファイル選択ポップアップを表示
        /// </summary>
        void ShowFileLoadPopup(Button targetButton)
        {
            if (fileLoadPopup == null)
            {
                Debug.LogError("❌ fileLoadPopup is null!");
                return;
            }

            if (targetButton == null)
            {
                Debug.LogError("❌ targetButton is null!");
                return;
            }

            currentEmptySlotButton = targetButton;

            // ポップアップをボタンの上部に配置
            var buttonBounds = targetButton.worldBound;
            Debug.Log($"📍 Button bounds: x={buttonBounds.x}, y={buttonBounds.y}, width={buttonBounds.width}, height={buttonBounds.height}");

            // ポップアップサイズ: 200px x 140px
            float popupWidth = 200f;
            float popupHeight = 150f;

            // ボタンの中央にポップアップを配置（水平方向）
            float popupLeft = buttonBounds.x + (buttonBounds.width / 2) - (popupWidth / 2);

            // ボタンの上に配置（垂直方向） - 10pxの余白
            float popupTop = buttonBounds.y - popupHeight - 10;

            Debug.Log($"📍 Popup position: left={popupLeft}, top={popupTop}");

            fileLoadPopup.style.left = popupLeft;
            fileLoadPopup.style.top = popupTop;
            fileLoadPopup.style.display = DisplayStyle.Flex;

            Debug.Log($"📋 File load popup shown for {targetButton.name}");

            // Medium impact for popup appearance
            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// ファイル選択ポップアップを非表示
        /// </summary>
        /// <param name="clearButton">currentEmptySlotButtonもクリアするか（キャンセル時のみtrue）</param>
        void HideFileLoadPopup(bool clearButton = false)
        {
            if (fileLoadPopup == null) return;

            fileLoadPopup.style.display = DisplayStyle.None;

            if (clearButton)
            {
                currentEmptySlotButton = null;
                Debug.Log("❌ File load popup hidden and button cleared");
            }
            else
            {
                Debug.Log("❌ File load popup hidden (button reference kept)");
            }

            // Light impact for popup close
            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        /// <summary>
        /// ローカルファイルから読み込みボタンがクリックされた時の処理
        /// </summary>
        void OnLoadFromLocalClicked()
        {
            Debug.Log("📁 Load from local clicked - method called");
            HideFileLoadPopup();

            Debug.Log("📁 Calling NativeFilePicker.PickFile...");

            // NativeFilePicker.PickFile is async and returns void
            // Platform-specific file type filtering
#if UNITY_IOS && !UNITY_EDITOR
            string[] fileTypes = new string[] { "public.data", "public.content", "public.item" };
            Debug.Log("📁 iOS: Using UTI types for VRM files");
#elif UNITY_ANDROID && !UNITY_EDITOR
            string[] fileTypes = new string[] { "*/*" };
            Debug.Log("📁 Android: Using MIME type for VRM files");
#else
            string[] fileTypes = new string[] { "*/*" };
            Debug.Log("📁 Editor/Standalone: Using wildcard for VRM files");
#endif

            NativeFilePicker.PickFile((path) =>
            {
                Debug.Log($"📁 NativeFilePicker callback invoked with path: {path}");
                if (!string.IsNullOrEmpty(path))
                {
                    Debug.Log($"✅ File picked: {path}");
                    OnFileSelected(path).Forget();
                }
                else
                {
                    Debug.Log("📁 File selection cancelled (path is null or empty)");
                    // ファイル選択がキャンセルされた場合もボタン参照をクリア
                    currentEmptySlotButton = null;
                }
            }, fileTypes);

            Debug.Log("📁 NativeFilePicker.PickFile called (async)");
        }

        /// <summary>
        /// フォトライブラリから読み込みボタンがクリックされた時の処理
        /// </summary>
        void OnLoadFromPhotoLibraryClicked()
        {
            Debug.Log("🖼 Load from photo library clicked - method called");
            HideFileLoadPopup();

            Debug.Log("🖼 Calling NativeFilePicker.PickFile for images...");

            // NativeFilePicker.PickFile is async and returns void
            NativeFilePicker.PickFile((path) =>
            {
                Debug.Log($"🖼 NativeFilePicker callback invoked with path: {path}");
                if (!string.IsNullOrEmpty(path))
                {
                    Debug.Log($"✅ Image picked: {path}");
                    OnFileSelected(path).Forget();
                }
                else
                {
                    Debug.Log("🖼 Photo selection cancelled (path is null or empty)");
                }
            }, new string[] { "public.image" }); // iOS: public.image for images

            Debug.Log("🖼 NativeFilePicker.PickFile called (async)");
        }

        /// <summary>
        /// ファイルが選択された時の処理
        /// </summary>
        async UniTask OnFileSelected(string filePath)
        {
            if (currentEmptySlotButton == null)
            {
                Debug.LogError("❌ currentEmptySlotButton is null!");
                return;
            }

            Debug.Log($"✅ File selected: {filePath}");

            // 一時ディレクトリのファイルを永続的なディレクトリにコピー
            string persistentPath = filePath;
            if (filePath.Contains("/tmp/") || filePath.Contains("/Inbox/"))
            {
                Debug.Log($"📋 File is in temporary directory, copying to persistent storage...");

                // 永続的なディレクトリパスを作成
                string persistentDir = System.IO.Path.Combine(Application.persistentDataPath, "VRM");
                if (!System.IO.Directory.Exists(persistentDir))
                {
                    System.IO.Directory.CreateDirectory(persistentDir);
                    Debug.Log($"📁 Created directory: {persistentDir}");
                }

                // ファイル名からスペースを削除
                string fileName = System.IO.Path.GetFileName(filePath);
                string safeFileName = fileName.Replace(" ", "_");
                persistentPath = System.IO.Path.Combine(persistentDir, safeFileName);

                // ファイルをコピー
                Debug.Log($"📋 Copying from: {filePath}");
                Debug.Log($"📋 Copying to: {persistentPath}");

                System.IO.File.Copy(filePath, persistentPath, true);
                Debug.Log($"✅ File copied successfully");
            }

            // VRMをAR空間にロード（永続パスを使用）
            Debug.Log($"📦 Loading VRM to AR space: {persistentPath}");
            await LoadVRMAsync(persistentPath);

            // サムネイルを生成（永続パスを使用）
            Texture2D thumbnail = await GenerateThumbnailAsync(persistentPath);

            // サムネイルをBase64エンコード
            string thumbnailBase64 = null;
            if (thumbnail != null)
            {
                byte[] bytes = thumbnail.EncodeToPNG();
                thumbnailBase64 = Convert.ToBase64String(bytes);
            }

            // スロットデータを作成・保存（永続パスを使用）
            var slotData = new AvatarSlotData(currentEmptySlotButton.name, persistentPath, thumbnailBase64);
            AvatarSlotPersistence.SaveSlot(slotData);
            avatarSlots[currentEmptySlotButton.name] = slotData;

            // ボタンアイコンを更新
            UpdateButtonIcon(currentEmptySlotButton, thumbnail);

            Debug.Log($"✅ Avatar slot setup complete for {currentEmptySlotButton.name}");

            // ボタン参照をクリア（処理完了）
            currentEmptySlotButton = null;

            // Heavy impact for successful load
            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        /// <summary>
        /// サムネイルを生成
        /// </summary>
        async UniTask<Texture2D> GenerateThumbnailAsync(string filePath)
        {
            if (vrmAvatarManager == null)
            {
                Debug.LogWarning("⚠️ VRMAvatarManager is not assigned, using placeholder thumbnail");
                return CreatePlaceholderThumbnail();
            }

            try
            {
                // 現在ロードされているアバターからサムネイルを生成
                if (vrmAvatarManager.CurrentAvatar != null)
                {
                    var thumbnail = await vrmAvatarManager.GenerateThumbnailAsync(vrmAvatarManager.CurrentAvatar);
                    Debug.Log($"🖼 Generated thumbnail from current avatar");
                    return thumbnail;
                }
                else
                {
                    Debug.LogWarning("⚠️ No avatar loaded, using placeholder thumbnail");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Failed to generate thumbnail: {e.Message}");
            }

            // フォールバック: プレースホルダーサムネイルを返す
            return CreatePlaceholderThumbnail();
        }

        /// <summary>
        /// プレースホルダーサムネイルを作成
        /// </summary>
        Texture2D CreatePlaceholderThumbnail()
        {
            var thumbnail = new Texture2D(64, 64);
            Color[] pixels = new Color[64 * 64];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0.5f, 0.5f, 0.8f, 1f);
            }
            thumbnail.SetPixels(pixels);
            thumbnail.Apply();
            return thumbnail;
        }

        /// <summary>
        /// ボタンアイコンを更新
        /// </summary>
        void UpdateButtonIcon(Button button, Texture2D thumbnail)
        {
            if (button == null || thumbnail == null) return;

            button.style.backgroundImage = new StyleBackground(thumbnail);
            Debug.Log($"✅ Updated button icon for {button.name}");
        }

        /// <summary>
        /// 保存されたアバタースロットを読み込み
        /// </summary>
        void LoadSavedAvatarSlots()
        {
            var collection = AvatarSlotPersistence.LoadAllSlots();
            Debug.Log($"📂 Loading {collection.slots.Count} saved avatar slots");

            foreach (var slotData in collection.slots)
            {
                Debug.Log($"📥 Loading slot: {slotData.slotId}");
                Debug.Log($"   FilePath: {slotData.filePath}");
                Debug.Log($"   Has thumbnail: {!string.IsNullOrEmpty(slotData.thumbnailBase64)}");

                avatarSlots[slotData.slotId] = slotData;

                // ボタンを検索
                var button = bottomButtonContainer?.Q<Button>(slotData.slotId);
                Debug.Log($"   Button found: {button != null}");

                if (button != null && !string.IsNullOrEmpty(slotData.thumbnailBase64))
                {
                    // Base64からテクスチャを復元
                    try
                    {
                        byte[] bytes = Convert.FromBase64String(slotData.thumbnailBase64);
                        var thumbnail = new Texture2D(2, 2);
                        thumbnail.LoadImage(bytes);
                        UpdateButtonIcon(button, thumbnail);
                        Debug.Log($"✅ Restored thumbnail for {slotData.slotId}");
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"❌ Failed to restore thumbnail for {slotData.slotId}: {e.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// アバタースロットボタンがクリックされた時の処理
        /// </summary>
        async void OnAvatarSlotClicked(Button button)
        {
            Debug.Log($"🔘 OnAvatarSlotClicked called for button: {button.name}");
            Debug.Log($"📊 avatarSlots contains {avatarSlots.Count} entries");

            if (avatarSlots.TryGetValue(button.name, out var slotData))
            {
                // スロットにデータがある場合：VRMを読み込み
                Debug.Log($"✅ Found slot data for {button.name}");
                Debug.Log($"📦 File path: {slotData.filePath}");
                Debug.Log($"📅 Last used: {slotData.lastUsed}");

                // ファイルが存在するか確認
                if (System.IO.File.Exists(slotData.filePath))
                {
                    Debug.Log($"✅ File exists at {slotData.filePath}");
                    await LoadVRMAsync(slotData.filePath);
                }
                else
                {
                    Debug.LogError($"❌ File not found: {slotData.filePath}");
                    Debug.LogError($"❌ The VRM file may have been moved or deleted. Please load a new file.");
                }

                // Selection feedback
                TapticEngine.Selection();
            }
            else
            {
                // スロットが空の場合：ファイル選択ポップアップを表示
                Debug.Log($"➕ Empty slot clicked: {button.name}");
                ShowFileLoadPopup(button);
            }
        }

        /// <summary>
        /// VRMファイルを読み込んでテンプレートとして設定
        /// </summary>
        async UniTask LoadVRMAsync(string filePath)
        {
            Debug.Log($"📦 LoadVRMAsync called with: {filePath}");

            if (vrmAvatarManager == null)
            {
                Debug.LogError("❌ VRMAvatarManager is not assigned! Please assign it in the Inspector.");
                Debug.LogError("❌ VRM will not be loaded. Check CameraCaptureController component.");
                return;
            }

            Debug.Log($"✅ VRMAvatarManager is assigned: {vrmAvatarManager.name}");

            if (avatarInstanceManager == null)
            {
                Debug.LogError("❌ AvatarInstanceManager is not assigned!");
                Debug.LogError("❌ VRM template will not be set. Check scene setup.");
                return;
            }

            Debug.Log($"✅ AvatarInstanceManager is assigned: {avatarInstanceManager.name}");

            try
            {
                Debug.Log($"📦 Calling VRMAvatarManager.LoadVRMFromPathAsync...");
                var template = await vrmAvatarManager.LoadVRMFromPathAsync(filePath);

                if (template != null)
                {
                    Debug.Log($"✅ VRM template loaded: {template.name}");

                    // AvatarInstanceManagerにテンプレートを設定
                    Debug.Log($"📍 Setting template to AvatarInstanceManager...");
                    avatarInstanceManager.SetTemplate(template);
                    Debug.Log($"✅ Template set to AvatarInstanceManager successfully");
                    Debug.Log($"ℹ️ Tap on a plane to instantiate the avatar");
                }
                else
                {
                    Debug.LogError($"❌ Failed to load VRM from {filePath}");
                    Debug.LogError($"❌ VRMAvatarManager.LoadVRMFromPathAsync returned null");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error loading VRM: {e.Message}");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 指定されたスクリーン座標がUI Toolkit のパネル上にあるかチェック
        /// PlaceAvatarOnPlaneOnlyから呼び出される
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
    }
}