using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// アスペクト比の切り替え・マスク表示を管理するコントローラー。
    /// </summary>
    public class AspectRatioController
    {
        private readonly VisualElement root;
        private readonly Button sideButton2;
        private readonly VisualElement topMask;
        private readonly VisualElement bottomMask;
        private readonly VisualElement leftMask;
        private readonly VisualElement rightMask;

        private ARPhotoController photoController;

        private int aspectRatioState = 0;

        private readonly string[] aspectRatioIcons = new string[]
        {
            "Sprite/PictIcon/SideBear/02_01_Full",
            "Sprite/PictIcon/SideBear/02_02_169",
            "Sprite/PictIcon/SideBear/02_03_32",
            "Sprite/PictIcon/SideBear/02_04_11"  // 1:1 (正方形)
        };

        private readonly float[] aspectRatios = new float[]
        {
            0f,      // Full (0 = カメラの最大画角)
            16f/9f,  // 16:9
            3f/2f,   // 3:2
            1f       // 1:1 (正方形)
        };

        /// <summary>
        /// 現在のアスペクト比値を取得。
        /// </summary>
        public float CurrentAspectRatio => aspectRatios[aspectRatioState];

        public AspectRatioController(VisualElement root, Button sideButton2, ARPhotoController photoController)
        {
            this.root = root;
            this.sideButton2 = sideButton2;
            this.photoController = photoController;

            topMask = root.Q<VisualElement>("topMask");
            bottomMask = root.Q<VisualElement>("bottomMask");
            leftMask = root.Q<VisualElement>("leftMask");
            rightMask = root.Q<VisualElement>("rightMask");

            // GeometryChangedEvent で初回レイアウト確定後にマスクを更新
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);

            // sideButton2 のクリックでアスペクト比切り替え
            if (sideButton2 != null)
            {
                sideButton2.RegisterCallback<ClickEvent>(OnSideButton2Clicked);
            }

            // 初期アスペクト比を設定
            if (photoController != null)
            {
                photoController.SetAspectRatio(aspectRatios[aspectRatioState]);
            }
        }

        /// <summary>
        /// ARPhotoController を差し替える。
        /// </summary>
        public void SetPhotoController(ARPhotoController controller)
        {
            photoController = controller;
        }

        /// <summary>
        /// イベント登録を解除する。CCC.OnDisable() から呼ぶ。
        /// </summary>
        public void Dispose()
        {
            if (root != null)
            {
                root.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            }

            if (sideButton2 != null)
            {
                sideButton2.UnregisterCallback<ClickEvent>(OnSideButton2Clicked);
            }
        }

        private void OnSideButton2Clicked(ClickEvent evt)
        {
            Debug.Log("📐 Side button 2 (Aspect Ratio) clicked");
            TapticEngine.Selection();

            aspectRatioState = (aspectRatioState + 1) % aspectRatioIcons.Length;

            if (sideButton2 != null)
            {
                var iconPath = aspectRatioIcons[aspectRatioState];
                var icon = Resources.Load<Texture2D>(iconPath);

                if (icon != null)
                {
                    sideButton2.style.backgroundImage = new StyleBackground(icon);
                    Debug.Log($"✅ Aspect ratio changed to: {iconPath}");
                }
                else
                {
                    Debug.LogWarning($"⚠️ Icon not found: {iconPath}");
                }
            }

            UpdateAspectMask();

            if (photoController != null)
            {
                photoController.SetAspectRatio(aspectRatios[aspectRatioState]);
            }
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            Debug.Log($"📐 GeometryChangedEvent: {root.resolvedStyle.width}x{root.resolvedStyle.height}");
            UpdateAspectMask();
        }

        private void UpdateAspectMask()
        {
            if (topMask == null || bottomMask == null)
            {
                Debug.LogWarning("⚠️ topMask or bottomMask is null");
                return;
            }

            Debug.Log($"📐 UpdateAspectMask called: state={aspectRatioState}");

            float targetAspect = aspectRatios[aspectRatioState];

            if (targetAspect == 0f)
            {
                topMask.style.display = DisplayStyle.None;
                bottomMask.style.display = DisplayStyle.None;
                if (leftMask != null) leftMask.style.display = DisplayStyle.None;
                if (rightMask != null) rightMask.style.display = DisplayStyle.None;
                Debug.Log("📐 Aspect masks hidden (Full mode)");
                return;
            }

            float screenWidth = root.resolvedStyle.width;
            float screenHeight = root.resolvedStyle.height;

            if (float.IsNaN(screenWidth) || float.IsNaN(screenHeight) || screenWidth <= 0 || screenHeight <= 0)
            {
                Debug.LogWarning($"⚠️ resolvedStyle not ready: {screenWidth}x{screenHeight}");
                return;
            }

            float targetHeightWidthRatio = targetAspect;
            float screenHeightWidthRatio = screenHeight / screenWidth;

            Debug.Log($"📐 UI Size (resolvedStyle): {screenWidth}x{screenHeight}, screen H/W ratio: {screenHeightWidthRatio:F3}, target H/W ratio: {targetHeightWidthRatio:F3}");

            float maskWidth = 0f;
            float maskHeight = 0f;
            bool isVerticalCrop = screenHeightWidthRatio > targetHeightWidthRatio;

            if (screenHeightWidthRatio > targetHeightWidthRatio)
            {
                float targetHeight = screenWidth * targetHeightWidthRatio;
                maskHeight = (screenHeight - targetHeight) / 2f;
                Debug.Log($"📐 Vertical crop: target height={targetHeight}px, mask height={maskHeight}px");
            }
            else
            {
                float targetWidth = screenHeight / targetHeightWidthRatio;
                maskWidth = (screenWidth - targetWidth) / 2f;
                Debug.Log($"📐 Horizontal crop: target width={targetWidth}px, mask width={maskWidth}px");
            }

            if (isVerticalCrop)
            {
                if (leftMask != null) leftMask.style.display = DisplayStyle.None;
                if (rightMask != null) rightMask.style.display = DisplayStyle.None;

                topMask.style.display = DisplayStyle.Flex;
                topMask.style.position = Position.Absolute;
                topMask.style.left = 0;
                topMask.style.right = 0;
                topMask.style.top = 0;
                topMask.style.width = screenWidth;
                topMask.style.height = maskHeight;
                topMask.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                topMask.style.opacity = 1f;
                topMask.pickingMode = PickingMode.Ignore;
                Debug.Log($"📐 Top mask SET: {screenWidth}x{maskHeight}px");

                bottomMask.style.display = DisplayStyle.Flex;
                bottomMask.style.position = Position.Absolute;
                bottomMask.style.left = 0;
                bottomMask.style.right = 0;
                bottomMask.style.bottom = 0;
                bottomMask.style.width = screenWidth;
                bottomMask.style.height = maskHeight;
                bottomMask.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                bottomMask.style.opacity = 1f;
                bottomMask.pickingMode = PickingMode.Ignore;
                Debug.Log($"📐 Bottom mask SET: {screenWidth}x{maskHeight}px");
            }
            else
            {
                topMask.style.display = DisplayStyle.None;
                bottomMask.style.display = DisplayStyle.None;

                if (leftMask != null)
                {
                    leftMask.style.display = DisplayStyle.Flex;
                    leftMask.style.position = Position.Absolute;
                    leftMask.style.left = 0;
                    leftMask.style.top = 0;
                    leftMask.style.bottom = 0;
                    leftMask.style.width = maskWidth;
                    leftMask.style.height = screenHeight;
                    leftMask.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                    leftMask.style.opacity = 1f;
                    leftMask.pickingMode = PickingMode.Ignore;
                    Debug.Log($"📐 Left mask SET: {maskWidth}x{screenHeight}px");
                }

                if (rightMask != null)
                {
                    rightMask.style.display = DisplayStyle.Flex;
                    rightMask.style.position = Position.Absolute;
                    rightMask.style.right = 0;
                    rightMask.style.top = 0;
                    rightMask.style.bottom = 0;
                    rightMask.style.width = maskWidth;
                    rightMask.style.height = screenHeight;
                    rightMask.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 1f);
                    rightMask.style.opacity = 1f;
                    rightMask.pickingMode = PickingMode.Ignore;
                    Debug.Log($"📐 Right mask SET: {maskWidth}x{screenHeight}px");
                }
            }
        }
    }
}
