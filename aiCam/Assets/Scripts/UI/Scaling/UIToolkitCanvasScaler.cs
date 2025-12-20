using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI.Scaling
{
    /// <summary>
    /// UI Toolkit用のCanvasScaler
    /// uGUIのCanvasScalerと同等の機能を提供し、セーフエリア対応も行う
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UIToolkitCanvasScaler : MonoBehaviour
    {
        #region Enums

        public enum ScaleMode
        {
            /// <summary>固定ピクセルサイズ</summary>
            ConstantPixelSize,
            /// <summary>画面サイズに合わせてスケール</summary>
            ScaleWithScreenSize,
            /// <summary>物理サイズに合わせてスケール</summary>
            ConstantPhysicalSize
        }

        public enum ScreenMatchMode
        {
            /// <summary>幅または高さに合わせる</summary>
            MatchWidthOrHeight,
            /// <summary>幅に合わせて拡大</summary>
            Expand,
            /// <summary>幅に合わせて縮小</summary>
            Shrink
        }

        #endregion

        #region Serialized Fields

        [Header("Scale Mode")]
        [SerializeField] private ScaleMode scaleMode = ScaleMode.ScaleWithScreenSize;

        [Header("Scale With Screen Size")]
        [SerializeField] private Vector2 referenceResolution = new Vector2(1920, 1080);
        [SerializeField] private ScreenMatchMode screenMatchMode = ScreenMatchMode.MatchWidthOrHeight;
        [SerializeField, Range(0, 1)] private float matchWidthOrHeight = 0.5f;

        [Header("Constant Physical Size")]
        [SerializeField] private float referenceDpi = 96f;
        [SerializeField] private float fallbackDpi = 96f;

        [Header("Safe Area")]
        [SerializeField] private bool applySafeArea = true;
        [SerializeField] private bool applyToTop = true;
        [SerializeField] private bool applyToBottom = true;
        [SerializeField] private bool applyToLeft = true;
        [SerializeField] private bool applyToRight = true;

        [Header("Debug")]
        [SerializeField] private bool showDebugInfo = false;
        [SerializeField] private bool simulateSafeArea = false;
        [SerializeField] private Rect simulatedSafeArea = new Rect(44, 47, 1125 - 44, 2436 - 47 - 34);

        #endregion

        #region Private Fields

        private UIDocument uiDocument;
        private PanelSettings panelSettings;
        private VisualElement root;
        private VisualElement safeAreaContainer;

        private Rect lastSafeArea;
        private Vector2Int lastScreenSize;
        private float currentScale = 1f;

        #endregion

        #region Events

        /// <summary>スケール変更時に発火</summary>
        public event Action<float> OnScaleChanged;

        /// <summary>セーフエリア変更時に発火</summary>
        public event Action<Rect> OnSafeAreaChanged;

        #endregion

        #region Properties

        /// <summary>現在のスケール値</summary>
        public float CurrentScale => currentScale;

        /// <summary>現在のセーフエリア（スクリーン座標）</summary>
        public Rect CurrentSafeArea => GetCurrentSafeArea();

        /// <summary>セーフエリアのパディング（UIToolkit座標、スケール適用済み）</summary>
        public Vector4 SafeAreaPadding { get; private set; }

        /// <summary>参照解像度</summary>
        public Vector2 ReferenceResolution
        {
            get => referenceResolution;
            set
            {
                referenceResolution = value;
                UpdateScale();
            }
        }

        /// <summary>Match Width or Height の値（0=Width, 1=Height）</summary>
        public float MatchWidthOrHeight
        {
            get => matchWidthOrHeight;
            set
            {
                matchWidthOrHeight = Mathf.Clamp01(value);
                UpdateScale();
            }
        }

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            uiDocument = GetComponent<UIDocument>();
        }

        private void Start()
        {
            // Startで初期化（UIDocumentのロード完了後）
            InitializeIfNeeded();
        }

        private void OnEnable()
        {
            if (uiDocument == null) return;

            panelSettings = uiDocument.panelSettings;

            // rootVisualElementがまだ準備できていない場合はStartで初期化
            if (uiDocument.rootVisualElement != null && uiDocument.rootVisualElement.childCount > 0)
            {
                InitializeIfNeeded();
            }
        }

        private void Update()
        {
            // 初期化がまだの場合は再試行
            if (!isInitialized)
            {
                InitializeIfNeeded();
            }

            CheckForChanges();
        }

        private bool isInitialized = false;

        private void InitializeIfNeeded()
        {
            if (isInitialized) return;
            if (uiDocument == null) return;

            root = uiDocument.rootVisualElement;
            panelSettings = uiDocument.panelSettings;

            if (root == null || root.childCount == 0)
            {
                return; // まだ準備できていない
            }

            Debug.Log($"[UIToolkitCanvasScaler] Initializing with {root.childCount} children");

            SetupSafeAreaContainer();
            UpdateScale();
            UpdateSafeArea();

            isInitialized = true;
        }

        #endregion

        #region Setup

        private void SetupSafeAreaContainer()
        {
            // .rootクラスを持つ要素を探す（UXMLで定義されたメインコンテナ）
            safeAreaContainer = root.Q<VisualElement>(className: "root");

            if (safeAreaContainer == null)
            {
                // .rootがない場合は、rootVisualElement自体を使用
                safeAreaContainer = root;
                Debug.Log("[UIToolkitCanvasScaler] Using rootVisualElement as safe area container");
            }
            else
            {
                Debug.Log("[UIToolkitCanvasScaler] Found .root element as safe area container");
            }
        }

        #endregion

        #region Scale Calculation

        private void CheckForChanges()
        {
            var screenSize = new Vector2Int(Screen.width, Screen.height);
            var safeArea = GetCurrentSafeArea();

            if (screenSize != lastScreenSize)
            {
                lastScreenSize = screenSize;
                UpdateScale();
            }

            if (safeArea != lastSafeArea)
            {
                lastSafeArea = safeArea;
                UpdateSafeArea();
            }
        }

        private void UpdateScale()
        {
            if (panelSettings == null) return;

            float newScale = CalculateScale();

            if (Mathf.Abs(newScale - currentScale) > 0.001f)
            {
                currentScale = newScale;

                // PanelSettingsを更新
                ApplyToPanelSettings();

                OnScaleChanged?.Invoke(currentScale);

                if (showDebugInfo)
                {
                    Debug.Log($"[UIToolkitCanvasScaler] Scale updated: {currentScale:F3}");
                }
            }
        }

        private float CalculateScale()
        {
            switch (scaleMode)
            {
                case ScaleMode.ConstantPixelSize:
                    return 1f;

                case ScaleMode.ScaleWithScreenSize:
                    return CalculateScaleWithScreenSize();

                case ScaleMode.ConstantPhysicalSize:
                    return CalculateConstantPhysicalSize();

                default:
                    return 1f;
            }
        }

        private float CalculateScaleWithScreenSize()
        {
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            float scaleWidth = screenWidth / referenceResolution.x;
            float scaleHeight = screenHeight / referenceResolution.y;

            switch (screenMatchMode)
            {
                case ScreenMatchMode.MatchWidthOrHeight:
                    float logWidth = Mathf.Log(scaleWidth, 2);
                    float logHeight = Mathf.Log(scaleHeight, 2);
                    float logWeightedAverage = Mathf.Lerp(logWidth, logHeight, matchWidthOrHeight);
                    return Mathf.Pow(2, logWeightedAverage);

                case ScreenMatchMode.Expand:
                    return Mathf.Min(scaleWidth, scaleHeight);

                case ScreenMatchMode.Shrink:
                    return Mathf.Max(scaleWidth, scaleHeight);

                default:
                    return 1f;
            }
        }

        private float CalculateConstantPhysicalSize()
        {
            float currentDpi = Screen.dpi;
            if (currentDpi <= 0) currentDpi = fallbackDpi;

            return currentDpi / referenceDpi;
        }

        private void ApplyToPanelSettings()
        {
            if (panelSettings == null) return;

            // PanelSettingsのスケールモードと参照解像度を設定
            panelSettings.scaleMode = scaleMode switch
            {
                ScaleMode.ConstantPixelSize => PanelScaleMode.ConstantPixelSize,
                ScaleMode.ScaleWithScreenSize => PanelScaleMode.ScaleWithScreenSize,
                ScaleMode.ConstantPhysicalSize => PanelScaleMode.ConstantPhysicalSize,
                _ => PanelScaleMode.ScaleWithScreenSize
            };

            panelSettings.referenceResolution = new Vector2Int(
                Mathf.RoundToInt(referenceResolution.x),
                Mathf.RoundToInt(referenceResolution.y)
            );

            panelSettings.screenMatchMode = screenMatchMode switch
            {
                ScreenMatchMode.MatchWidthOrHeight => PanelScreenMatchMode.MatchWidthOrHeight,
                ScreenMatchMode.Expand => PanelScreenMatchMode.Expand,
                ScreenMatchMode.Shrink => PanelScreenMatchMode.Shrink,
                _ => PanelScreenMatchMode.MatchWidthOrHeight
            };

            panelSettings.match = matchWidthOrHeight;
            panelSettings.referenceDpi = referenceDpi;
            panelSettings.fallbackDpi = fallbackDpi;
        }

        #endregion

        #region Safe Area

        private Rect GetCurrentSafeArea()
        {
#if UNITY_EDITOR
            if (simulateSafeArea)
            {
                return simulatedSafeArea;
            }
#endif
            return Screen.safeArea;
        }

        private void UpdateSafeArea()
        {
            if (!applySafeArea || safeAreaContainer == null) return;

            Rect safeArea = GetCurrentSafeArea();
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            // セーフエリアのパディングを計算（スクリーン座標）
            float leftPadding = applyToLeft ? safeArea.x : 0;
            float rightPadding = applyToRight ? (screenWidth - safeArea.xMax) : 0;
            float topPadding = applyToTop ? (screenHeight - safeArea.yMax) : 0;
            float bottomPadding = applyToBottom ? safeArea.y : 0;

            // スケールを適用してUIToolkit座標に変換
            float scale = currentScale > 0 ? currentScale : 1f;
            float scaledLeft = leftPadding / scale;
            float scaledRight = rightPadding / scale;
            float scaledTop = topPadding / scale;
            float scaledBottom = bottomPadding / scale;

            SafeAreaPadding = new Vector4(scaledLeft, scaledTop, scaledRight, scaledBottom);

            // safeAreaContainerにパディングを適用
            safeAreaContainer.style.paddingLeft = scaledLeft;
            safeAreaContainer.style.paddingRight = scaledRight;
            safeAreaContainer.style.paddingTop = scaledTop;
            safeAreaContainer.style.paddingBottom = scaledBottom;

            OnSafeAreaChanged?.Invoke(safeArea);

            if (showDebugInfo)
            {
                Debug.Log($"[UIToolkitCanvasScaler] SafeArea updated: L={scaledLeft:F1} R={scaledRight:F1} T={scaledTop:F1} B={scaledBottom:F1}");
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// スケールとセーフエリアを強制更新
        /// </summary>
        public void ForceUpdate()
        {
            UpdateScale();
            UpdateSafeArea();
        }

        /// <summary>
        /// スクリーン座標をUIToolkit座標に変換
        /// </summary>
        public Vector2 ScreenToPanel(Vector2 screenPosition)
        {
            if (root?.panel == null) return screenPosition;

            // Y座標を反転（Screen座標は左下原点、UIToolkitは左上原点）
            screenPosition.y = Screen.height - screenPosition.y;

            return RuntimePanelUtils.ScreenToPanel(root.panel, screenPosition);
        }

        /// <summary>
        /// UIToolkit座標をスクリーン座標に変換
        /// </summary>
        public Vector2 PanelToScreen(Vector2 panelPosition)
        {
            if (root?.panel == null) return panelPosition;

            Vector2 screenPos = RuntimePanelUtils.ScreenToPanel(root.panel, panelPosition);
            screenPos.y = Screen.height - screenPos.y;

            return screenPos;
        }

        /// <summary>
        /// 参照解像度でのサイズをスクリーンサイズに変換
        /// </summary>
        public float ReferenceToScreen(float referenceValue)
        {
            return referenceValue * currentScale;
        }

        /// <summary>
        /// スクリーンサイズを参照解像度でのサイズに変換
        /// </summary>
        public float ScreenToReference(float screenValue)
        {
            return currentScale > 0 ? screenValue / currentScale : screenValue;
        }

        #endregion

        #region Editor Debug

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (Application.isPlaying && uiDocument != null)
            {
                UpdateScale();
                UpdateSafeArea();
            }
        }

        private void OnDrawGizmosSelected()
        {
            if (!showDebugInfo) return;

            // エディタでセーフエリアを視覚化
            Rect safeArea = GetCurrentSafeArea();
            float screenWidth = Screen.width;
            float screenHeight = Screen.height;

            // セーフエリア外を半透明の赤で表示
            Gizmos.color = new Color(1, 0, 0, 0.3f);

            // 上
            if (screenHeight - safeArea.yMax > 0)
            {
                Debug.Log($"[SafeArea] Top margin: {screenHeight - safeArea.yMax}px");
            }

            // 下
            if (safeArea.y > 0)
            {
                Debug.Log($"[SafeArea] Bottom margin: {safeArea.y}px");
            }

            // 左
            if (safeArea.x > 0)
            {
                Debug.Log($"[SafeArea] Left margin: {safeArea.x}px");
            }

            // 右
            if (screenWidth - safeArea.xMax > 0)
            {
                Debug.Log($"[SafeArea] Right margin: {screenWidth - safeArea.xMax}px");
            }
        }
#endif

        #endregion
    }
}
