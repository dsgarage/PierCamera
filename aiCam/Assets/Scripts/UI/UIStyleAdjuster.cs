using UnityEngine;
using UnityEngine.UI;

namespace AICam.UI
{
    /// <summary>
    /// UI要素のサイズをスタイリッシュに調整するコンポーネント
    /// iPhone 17 Pro Max等の縦長画面に最適化
    /// </summary>
    [ExecuteAlways]
    public class UIStyleAdjuster : MonoBehaviour
    {
        [System.Serializable]
        public class UIScaleProfile
        {
            public string profileName;

            [Header("Top Toolbar Icons")]
            [Tooltip("上部ツールバーアイコンのサイズ (デフォルト: 70x70 → 48x48推奨)")]
            public float topIconSize = 48f;
            [Tooltip("上部ツールバーのパディング")]
            public RectOffset topToolbarPadding = new RectOffset(10, 10, 8, 8);
            [Tooltip("アイコン間のスペース")]
            public float topIconSpacing = 12f;

            [Header("Side Control Buttons")]
            [Tooltip("左側コントロールボタンのサイズ (デフォルト: 60x60 → 44x44推奨)")]
            public float sideButtonSize = 44f;
            [Tooltip("サイドボタン間のスペース")]
            public float sideButtonSpacing = 10f;

            [Header("Capture Button")]
            [Tooltip("キャプチャボタンのサイズ (デフォルト: 200x200 → 120x120推奨)")]
            public float captureButtonSize = 120f;
            [Tooltip("内側リングのサイズ比率")]
            public float captureInnerRingRatio = 0.85f;

            [Header("Avatar Slot Panel")]
            [Tooltip("アバタースロットのサイズ (デフォルト: 100x100 → 72x72推奨)")]
            public float avatarSlotSize = 72f;
            [Tooltip("スロット間のスペース")]
            public float avatarSlotSpacing = 8f;
            [Tooltip("スロットパネルのパディング")]
            public RectOffset slotPanelPadding = new RectOffset(12, 12, 8, 8);

            [Header("Face Control Panel")]
            [Tooltip("表情パネルのボタンサイズ (デフォルト: 70x70 → 52x52推奨)")]
            public float faceControlButtonSize = 52f;
        }

        [Header("Profile")]
        [SerializeField] private UIScaleProfile currentProfile;

        [Header("Target References")]
        [SerializeField] private Transform topToolbarPanel;      // Panel_SettingButton
        [SerializeField] private Transform sideControlPanel;     // 左側ボタンコンテナ
        [SerializeField] private Transform captureButton;        // Btn_Capture
        [SerializeField] private Transform avatarSlotPanel;      // AvatarSlotPanel
        [SerializeField] private Transform faceControlPanel;     // FaceControlPanel

        [Header("Settings")]
        [SerializeField] private bool autoApplyOnStart = true;
        [SerializeField] private bool autoFindReferences = true;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = true;

        // デフォルトプロファイル（スタイリッシュ）
        public static UIScaleProfile StylishProfile => new UIScaleProfile
        {
            profileName = "Stylish (iPhone 17 Pro Max)",
            topIconSize = 48f,
            topToolbarPadding = new RectOffset(10, 10, 8, 8),
            topIconSpacing = 12f,
            sideButtonSize = 44f,
            sideButtonSpacing = 10f,
            captureButtonSize = 120f,
            captureInnerRingRatio = 0.85f,
            avatarSlotSize = 72f,
            avatarSlotSpacing = 8f,
            slotPanelPadding = new RectOffset(12, 12, 8, 8),
            faceControlButtonSize = 52f
        };

        // コンパクトプロファイル
        public static UIScaleProfile CompactProfile => new UIScaleProfile
        {
            profileName = "Compact",
            topIconSize = 40f,
            topToolbarPadding = new RectOffset(8, 8, 6, 6),
            topIconSpacing = 10f,
            sideButtonSize = 38f,
            sideButtonSpacing = 8f,
            captureButtonSize = 100f,
            captureInnerRingRatio = 0.85f,
            avatarSlotSize = 60f,
            avatarSlotSpacing = 6f,
            slotPanelPadding = new RectOffset(10, 10, 6, 6),
            faceControlButtonSize = 44f
        };

        private void Awake()
        {
            if (currentProfile == null)
            {
                currentProfile = StylishProfile;
            }
        }

        private void Start()
        {
            if (autoFindReferences)
            {
                FindUIReferences();
            }

            if (autoApplyOnStart)
            {
                ApplyProfile();
            }
        }

        /// <summary>
        /// UI参照を自動検索
        /// </summary>
        public void FindUIReferences()
        {
            // Panel_SettingButton (上部ツールバー)
            if (topToolbarPanel == null)
            {
                var found = FindDeepChild(transform.root, "Panel_SettingButton");
                if (found != null) topToolbarPanel = found;
            }

            // Btn_Capture
            if (captureButton == null)
            {
                var found = FindDeepChild(transform.root, "Btn_Capture");
                if (found != null) captureButton = found;
            }

            // AvatarSlotPanel
            if (avatarSlotPanel == null)
            {
                var found = FindDeepChild(transform.root, "AvatarSlotPanel");
                if (found != null) avatarSlotPanel = found;
            }

            // FaceControlPanel
            if (faceControlPanel == null)
            {
                var found = FindDeepChild(transform.root, "FaceControlPanel");
                if (found != null) faceControlPanel = found;
            }

            // 左側コントロールパネル（Btn_Captureの近くにある）
            if (sideControlPanel == null && captureButton != null)
            {
                // Btn_Captureの親から探す
                var parent = captureButton.parent;
                if (parent != null)
                {
                    // 子要素でボタンを含むパネルを探す
                    foreach (Transform child in parent)
                    {
                        if (child.name.Contains("Control") || child.name.Contains("Side"))
                        {
                            sideControlPanel = child;
                            break;
                        }
                    }
                }
            }

            if (showDebugInfo)
            {
                Debug.Log($"[UIStyleAdjuster] Found references:");
                Debug.Log($"  TopToolbar: {(topToolbarPanel != null ? topToolbarPanel.name : "NOT FOUND")}");
                Debug.Log($"  Capture: {(captureButton != null ? captureButton.name : "NOT FOUND")}");
                Debug.Log($"  AvatarSlot: {(avatarSlotPanel != null ? avatarSlotPanel.name : "NOT FOUND")}");
                Debug.Log($"  FaceControl: {(faceControlPanel != null ? faceControlPanel.name : "NOT FOUND")}");
            }
        }

        /// <summary>
        /// 現在のプロファイルを適用
        /// </summary>
        [ContextMenu("Apply Profile")]
        public void ApplyProfile()
        {
            if (currentProfile == null)
            {
                Debug.LogWarning("[UIStyleAdjuster] No profile set!");
                return;
            }

            if (showDebugInfo)
            {
                Debug.Log($"[UIStyleAdjuster] Applying profile: {currentProfile.profileName}");
            }

            ApplyTopToolbarStyle();
            ApplySideControlStyle();
            ApplyCaptureButtonStyle();
            ApplyAvatarSlotStyle();
            ApplyFaceControlStyle();
        }

        /// <summary>
        /// 上部ツールバーにスタイル適用
        /// </summary>
        private void ApplyTopToolbarStyle()
        {
            if (topToolbarPanel == null) return;

            // HorizontalLayoutGroupを調整
            var layout = topToolbarPanel.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = currentProfile.topToolbarPadding;
                layout.spacing = currentProfile.topIconSpacing;
            }

            // 子要素のボタンサイズを調整
            foreach (Transform child in topToolbarPanel)
            {
                var rect = child.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(currentProfile.topIconSize, currentProfile.topIconSize);
                }

                var layoutElement = child.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.preferredWidth = currentProfile.topIconSize;
                    layoutElement.preferredHeight = currentProfile.topIconSize;
                    layoutElement.minWidth = currentProfile.topIconSize;
                    layoutElement.minHeight = currentProfile.topIconSize;
                }

                // 子のImageも調整
                var image = child.GetComponent<Image>();
                if (image != null)
                {
                    // アイコン画像のアスペクト比を維持
                    image.preserveAspect = true;
                }
            }

            if (showDebugInfo)
            {
                Debug.Log($"[UIStyleAdjuster] Applied top toolbar style: {currentProfile.topIconSize}px icons");
            }
        }

        /// <summary>
        /// 左側コントロールボタンにスタイル適用
        /// </summary>
        private void ApplySideControlStyle()
        {
            if (sideControlPanel == null) return;

            var layout = sideControlPanel.GetComponent<VerticalLayoutGroup>();
            if (layout != null)
            {
                layout.spacing = currentProfile.sideButtonSpacing;
            }

            foreach (Transform child in sideControlPanel)
            {
                var rect = child.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(currentProfile.sideButtonSize, currentProfile.sideButtonSize);
                }

                var layoutElement = child.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.preferredWidth = currentProfile.sideButtonSize;
                    layoutElement.preferredHeight = currentProfile.sideButtonSize;
                }
            }

            if (showDebugInfo)
            {
                Debug.Log($"[UIStyleAdjuster] Applied side control style: {currentProfile.sideButtonSize}px buttons");
            }
        }

        /// <summary>
        /// キャプチャボタンにスタイル適用
        /// </summary>
        private void ApplyCaptureButtonStyle()
        {
            if (captureButton == null) return;

            var rect = captureButton.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.sizeDelta = new Vector2(currentProfile.captureButtonSize, currentProfile.captureButtonSize);
            }

            // 内側リングがある場合は調整
            var innerRing = captureButton.Find("InnerRing") ?? captureButton.Find("Inner");
            if (innerRing != null)
            {
                var innerRect = innerRing.GetComponent<RectTransform>();
                if (innerRect != null)
                {
                    float innerSize = currentProfile.captureButtonSize * currentProfile.captureInnerRingRatio;
                    innerRect.sizeDelta = new Vector2(innerSize, innerSize);
                }
            }

            if (showDebugInfo)
            {
                Debug.Log($"[UIStyleAdjuster] Applied capture button style: {currentProfile.captureButtonSize}px");
            }
        }

        /// <summary>
        /// アバタースロットパネルにスタイル適用
        /// </summary>
        private void ApplyAvatarSlotStyle()
        {
            if (avatarSlotPanel == null) return;

            var layout = avatarSlotPanel.GetComponent<HorizontalLayoutGroup>();
            if (layout != null)
            {
                layout.padding = currentProfile.slotPanelPadding;
                layout.spacing = currentProfile.avatarSlotSpacing;
            }

            // 各スロットのサイズを調整
            foreach (Transform child in avatarSlotPanel)
            {
                var rect = child.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.sizeDelta = new Vector2(currentProfile.avatarSlotSize, currentProfile.avatarSlotSize);
                }

                var layoutElement = child.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.preferredWidth = currentProfile.avatarSlotSize;
                    layoutElement.preferredHeight = currentProfile.avatarSlotSize;
                    layoutElement.minWidth = currentProfile.avatarSlotSize;
                    layoutElement.minHeight = currentProfile.avatarSlotSize;
                }

                // スロット番号のフォントサイズも調整
                var numberText = child.Find("SlotNumberText");
                if (numberText != null)
                {
                    var tmp = numberText.GetComponent<TMPro.TextMeshProUGUI>();
                    if (tmp != null)
                    {
                        // スロットサイズに比例してフォントサイズを調整
                        tmp.fontSize = Mathf.RoundToInt(currentProfile.avatarSlotSize * 0.16f);
                    }
                }

                // ＋マークのフォントサイズも調整
                var plusText = FindDeepChild(child, "PlusText");
                if (plusText != null)
                {
                    var tmp = plusText.GetComponent<TMPro.TextMeshProUGUI>();
                    if (tmp != null)
                    {
                        tmp.fontSize = Mathf.RoundToInt(currentProfile.avatarSlotSize * 0.48f);
                    }
                }
            }

            if (showDebugInfo)
            {
                Debug.Log($"[UIStyleAdjuster] Applied avatar slot style: {currentProfile.avatarSlotSize}px slots");
            }
        }

        /// <summary>
        /// 表情コントロールパネルにスタイル適用
        /// </summary>
        private void ApplyFaceControlStyle()
        {
            if (faceControlPanel == null) return;

            // 表情ボタンコンテナを探す
            var buttonContainer = faceControlPanel.Find("ButtonContainer") ?? faceControlPanel;

            foreach (Transform child in buttonContainer)
            {
                var rect = child.GetComponent<RectTransform>();
                if (rect != null && child.GetComponent<Button>() != null)
                {
                    rect.sizeDelta = new Vector2(currentProfile.faceControlButtonSize, currentProfile.faceControlButtonSize);
                }

                var layoutElement = child.GetComponent<LayoutElement>();
                if (layoutElement != null)
                {
                    layoutElement.preferredWidth = currentProfile.faceControlButtonSize;
                    layoutElement.preferredHeight = currentProfile.faceControlButtonSize;
                }
            }

            // GridLayoutGroupがある場合は調整
            var gridLayout = buttonContainer.GetComponent<GridLayoutGroup>();
            if (gridLayout != null)
            {
                gridLayout.cellSize = new Vector2(currentProfile.faceControlButtonSize, currentProfile.faceControlButtonSize);
            }

            if (showDebugInfo)
            {
                Debug.Log($"[UIStyleAdjuster] Applied face control style: {currentProfile.faceControlButtonSize}px buttons");
            }
        }

        /// <summary>
        /// スタイリッシュプロファイルを適用
        /// </summary>
        [ContextMenu("Apply Stylish Profile")]
        public void ApplyStylishProfile()
        {
            currentProfile = StylishProfile;
            ApplyProfile();
        }

        /// <summary>
        /// コンパクトプロファイルを適用
        /// </summary>
        [ContextMenu("Apply Compact Profile")]
        public void ApplyCompactProfile()
        {
            currentProfile = CompactProfile;
            ApplyProfile();
        }

        /// <summary>
        /// 深い階層の子オブジェクトを検索
        /// </summary>
        private Transform FindDeepChild(Transform parent, string name)
        {
            if (parent == null) return null;

            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;

                var found = FindDeepChild(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

#if UNITY_EDITOR
        [ContextMenu("Debug: Print Current Sizes")]
        private void DebugPrintCurrentSizes()
        {
            Debug.Log("=== Current UI Element Sizes ===");

            if (topToolbarPanel != null)
            {
                Debug.Log($"Top Toolbar ({topToolbarPanel.name}):");
                foreach (Transform child in topToolbarPanel)
                {
                    var rect = child.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        Debug.Log($"  {child.name}: {rect.sizeDelta}");
                    }
                }
            }

            if (captureButton != null)
            {
                var rect = captureButton.GetComponent<RectTransform>();
                Debug.Log($"Capture Button: {rect?.sizeDelta}");
            }

            if (avatarSlotPanel != null)
            {
                Debug.Log($"Avatar Slot Panel ({avatarSlotPanel.name}):");
                foreach (Transform child in avatarSlotPanel)
                {
                    var rect = child.GetComponent<RectTransform>();
                    if (rect != null)
                    {
                        Debug.Log($"  {child.name}: {rect.sizeDelta}");
                    }
                }
            }
        }
#endif
    }
}
