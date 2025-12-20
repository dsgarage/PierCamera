using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI.Scaling
{
    /// <summary>
    /// セーフエリアをデバッグ表示するコンポーネント
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class SafeAreaVisualizer : MonoBehaviour
    {
        [Header("Display Settings")]
        [SerializeField] private bool showVisualizer = true;
        [SerializeField] private Color safeAreaColor = new Color(0, 1, 0, 0.2f);
        [SerializeField] private Color unsafeAreaColor = new Color(1, 0, 0, 0.3f);
        [SerializeField] private bool showLabels = true;

        [Header("Simulation")]
        [SerializeField] private bool simulateSafeArea = false;
        [SerializeField] private DevicePreset devicePreset = DevicePreset.iPhone14Pro;

        public enum DevicePreset
        {
            None,
            iPhone14Pro,      // 2556x1179, Safe: 59 top, 34 bottom
            iPhone14ProMax,   // 2796x1290, Safe: 59 top, 34 bottom
            iPhoneSE,         // 1334x750, No notch
            iPadPro12,        // 2732x2048, No notch
            Custom
        }

        [Header("Custom Safe Area (for DevicePreset.Custom)")]
        [SerializeField] private Rect customSafeArea = new Rect(0, 34, 1179, 2556 - 59 - 34);

        private UIDocument uiDocument;
        private VisualElement root;
        private VisualElement visualizerContainer;
        private VisualElement topUnsafe, bottomUnsafe, leftUnsafe, rightUnsafe;
        private Label topLabel, bottomLabel, leftLabel, rightLabel;

        private Rect lastSafeArea;

        private void OnEnable()
        {
            uiDocument = GetComponent<UIDocument>();
            if (uiDocument == null) return;

            root = uiDocument.rootVisualElement;
            if (root == null) return;

            CreateVisualizer();
            UpdateVisualizer();
        }

        private void OnDisable()
        {
            if (visualizerContainer != null)
            {
                visualizerContainer.RemoveFromHierarchy();
                visualizerContainer = null;
            }
        }

        private void Update()
        {
            if (!showVisualizer)
            {
                if (visualizerContainer != null)
                {
                    visualizerContainer.style.display = DisplayStyle.None;
                }
                return;
            }

            if (visualizerContainer != null)
            {
                visualizerContainer.style.display = DisplayStyle.Flex;
            }

            Rect currentSafeArea = GetSafeArea();
            if (currentSafeArea != lastSafeArea)
            {
                lastSafeArea = currentSafeArea;
                UpdateVisualizer();
            }
        }

        private Rect GetSafeArea()
        {
#if UNITY_EDITOR
            if (simulateSafeArea)
            {
                return GetSimulatedSafeArea();
            }
#endif
            return Screen.safeArea;
        }

        private Rect GetSimulatedSafeArea()
        {
            switch (devicePreset)
            {
                case DevicePreset.iPhone14Pro:
                    // 縦向き: 1179x2556, Safe: top 59, bottom 34
                    return new Rect(0, 34, Screen.width, Screen.height - 59 - 34);

                case DevicePreset.iPhone14ProMax:
                    // 縦向き: 1290x2796, Safe: top 59, bottom 34
                    return new Rect(0, 34, Screen.width, Screen.height - 59 - 34);

                case DevicePreset.iPhoneSE:
                case DevicePreset.iPadPro12:
                    // ノッチなし
                    return new Rect(0, 0, Screen.width, Screen.height);

                case DevicePreset.Custom:
                    return customSafeArea;

                default:
                    return Screen.safeArea;
            }
        }

        private void CreateVisualizer()
        {
            visualizerContainer = new VisualElement();
            visualizerContainer.name = "safe-area-visualizer";
            visualizerContainer.pickingMode = PickingMode.Ignore;
            visualizerContainer.style.position = Position.Absolute;
            visualizerContainer.style.top = 0;
            visualizerContainer.style.left = 0;
            visualizerContainer.style.right = 0;
            visualizerContainer.style.bottom = 0;

            // 上部の危険エリア
            topUnsafe = CreateUnsafeArea("top-unsafe");
            topUnsafe.style.position = Position.Absolute;
            topUnsafe.style.top = 0;
            topUnsafe.style.left = 0;
            topUnsafe.style.right = 0;

            topLabel = CreateLabel();
            topUnsafe.Add(topLabel);

            // 下部の危険エリア
            bottomUnsafe = CreateUnsafeArea("bottom-unsafe");
            bottomUnsafe.style.position = Position.Absolute;
            bottomUnsafe.style.bottom = 0;
            bottomUnsafe.style.left = 0;
            bottomUnsafe.style.right = 0;

            bottomLabel = CreateLabel();
            bottomUnsafe.Add(bottomLabel);

            // 左の危険エリア
            leftUnsafe = CreateUnsafeArea("left-unsafe");
            leftUnsafe.style.position = Position.Absolute;
            leftUnsafe.style.top = 0;
            leftUnsafe.style.left = 0;
            leftUnsafe.style.bottom = 0;

            leftLabel = CreateLabel();
            leftUnsafe.Add(leftLabel);

            // 右の危険エリア
            rightUnsafe = CreateUnsafeArea("right-unsafe");
            rightUnsafe.style.position = Position.Absolute;
            rightUnsafe.style.top = 0;
            rightUnsafe.style.right = 0;
            rightUnsafe.style.bottom = 0;

            rightLabel = CreateLabel();
            rightUnsafe.Add(rightLabel);

            visualizerContainer.Add(topUnsafe);
            visualizerContainer.Add(bottomUnsafe);
            visualizerContainer.Add(leftUnsafe);
            visualizerContainer.Add(rightUnsafe);

            root.Add(visualizerContainer);
        }

        private VisualElement CreateUnsafeArea(string name)
        {
            var element = new VisualElement();
            element.name = name;
            element.pickingMode = PickingMode.Ignore;
            element.style.backgroundColor = unsafeAreaColor;
            element.style.alignItems = Align.Center;
            element.style.justifyContent = Justify.Center;
            return element;
        }

        private Label CreateLabel()
        {
            var label = new Label();
            label.pickingMode = PickingMode.Ignore;
            label.style.color = Color.white;
            label.style.fontSize = 12;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.display = showLabels ? DisplayStyle.Flex : DisplayStyle.None;
            return label;
        }

        private void UpdateVisualizer()
        {
            if (visualizerContainer == null) return;

            Rect safeArea = GetSafeArea();
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            // UIToolkitのスケールを考慮
            var scaler = GetComponent<UIToolkitCanvasScaler>();
            float scale = scaler != null ? scaler.CurrentScale : 1f;
            if (scale <= 0) scale = 1f;

            // 各エリアのサイズを計算
            float topHeight = (screenHeight - safeArea.yMax) / scale;
            float bottomHeight = safeArea.y / scale;
            float leftWidth = safeArea.x / scale;
            float rightWidth = (screenWidth - safeArea.xMax) / scale;

            // 上部
            topUnsafe.style.height = topHeight;
            topUnsafe.style.display = topHeight > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            topLabel.text = $"Top: {topHeight:F0}px";
            topLabel.style.display = showLabels && topHeight > 20 ? DisplayStyle.Flex : DisplayStyle.None;

            // 下部
            bottomUnsafe.style.height = bottomHeight;
            bottomUnsafe.style.display = bottomHeight > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            bottomLabel.text = $"Bottom: {bottomHeight:F0}px";
            bottomLabel.style.display = showLabels && bottomHeight > 20 ? DisplayStyle.Flex : DisplayStyle.None;

            // 左
            leftUnsafe.style.width = leftWidth;
            leftUnsafe.style.display = leftWidth > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            leftLabel.text = $"L: {leftWidth:F0}";
            leftLabel.style.display = showLabels && leftWidth > 30 ? DisplayStyle.Flex : DisplayStyle.None;

            // 右
            rightUnsafe.style.width = rightWidth;
            rightUnsafe.style.display = rightWidth > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            rightLabel.text = $"R: {rightWidth:F0}";
            rightLabel.style.display = showLabels && rightWidth > 30 ? DisplayStyle.Flex : DisplayStyle.None;

            // 色を更新
            topUnsafe.style.backgroundColor = unsafeAreaColor;
            bottomUnsafe.style.backgroundColor = unsafeAreaColor;
            leftUnsafe.style.backgroundColor = unsafeAreaColor;
            rightUnsafe.style.backgroundColor = unsafeAreaColor;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && visualizerContainer != null)
            {
                UpdateVisualizer();
            }
        }
#endif
    }
}
