using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// ライティング/シャドウ設定パネルの表示・非表示・タブ切り替えを管理するコントローラー。
    /// </summary>
    public class SettingsPanelUIController
    {
        private LightingPanelController lightingPanelController;
        private readonly VisualElement settingsPanelBackdrop;
        private readonly VisualElement lightingPanelOverlay;
        private readonly VisualElement shadowPanelOverlay;
        private readonly VisualElement tabMood;
        private readonly VisualElement tabDirection;
        private readonly VisualElement lightingPanelMood;
        private readonly VisualElement lightingPanelDirection;
        private readonly bool enableDebugLogging;

        private readonly System.Action<string, string> onWarning;
        private readonly System.Action<string, string> onError;

        /// <summary>
        /// 設定パネルが表示中かどうか。
        /// </summary>
        public bool IsSettingsVisible =>
            settingsPanelBackdrop != null && settingsPanelBackdrop.ClassListContains("visible");

        public SettingsPanelUIController(
            VisualElement root,
            bool enableDebugLogging,
            System.Action<string, string> onWarning,
            System.Action<string, string> onError)
        {
            this.enableDebugLogging = enableDebugLogging;
            this.onWarning = onWarning;
            this.onError = onError;

            // Issue #120: パネル要素を取得
            settingsPanelBackdrop = root.Q<VisualElement>("settingsPanelBackdrop");
            lightingPanelOverlay = root.Q<VisualElement>("lightingPanelOverlay");
            shadowPanelOverlay = root.Q<VisualElement>("shadowPanelOverlay");

            // Issue #450: Lighting Panel タブ要素を取得
            tabMood = root.Q<VisualElement>("tabMood");
            tabDirection = root.Q<VisualElement>("tabDirection");
            lightingPanelMood = root.Q<VisualElement>("lightingPanelMood");
            lightingPanelDirection = root.Q<VisualElement>("lightingPanelDirection");

            // トップボタンのイベント登録
            var topButton1 = root.Q<Button>("topButton1");
            var topButton2 = root.Q<Button>("topButton2");

            if (enableDebugLogging)
            {
                Debug.Log($"🔘 topButton1: {(topButton1 != null ? "✅ found" : "❌ NOT FOUND")}");
                Debug.Log($"🔘 topButton2: {(topButton2 != null ? "✅ found" : "❌ NOT FOUND")}");
            }

            if (topButton1 != null)
            {
                topButton1.RegisterCallback<ClickEvent>(evt => OnTopButton1Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 1 (Light Estimation) click event registered");
            }

            if (topButton2 != null)
            {
                topButton2.RegisterCallback<ClickEvent>(evt => OnTopButton2Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 2 (Shadow) click event registered");
            }

            // バックドロップクリックイベント
            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.target == settingsPanelBackdrop)
                    {
                        if (enableDebugLogging) Debug.Log("🔲 Backdrop clicked directly - closing panels");
                        HideAllPanels();
                        evt.StopPropagation();
                    }
                });
                if (enableDebugLogging) Debug.Log("✅ Settings panel backdrop events registered");
            }

            // Close buttons
            var lightingCloseButton = root.Q<Button>("lightingPanelClose");
            if (lightingCloseButton != null)
            {
                lightingCloseButton.RegisterCallback<ClickEvent>(evt => HideAllPanels());
                if (enableDebugLogging) Debug.Log("✅ Lighting panel close button events registered");
            }

            var shadowCloseButton = root.Q<Button>("shadowPanelClose");
            if (shadowCloseButton != null)
            {
                shadowCloseButton.RegisterCallback<ClickEvent>(evt => HideAllPanels());
                if (enableDebugLogging) Debug.Log("✅ Shadow panel close button events registered");
            }

            // Issue #450: タブ切り替えイベント
            tabMood?.RegisterCallback<ClickEvent>(_ => ShowLightingMood());
            tabDirection?.RegisterCallback<ClickEvent>(_ => ShowLightingDirection());

            if (enableDebugLogging)
            {
                Debug.Log($"🔄 TabMood: {(tabMood != null ? "✅" : "❌")}");
                Debug.Log($"🔄 TabDirection: {(tabDirection != null ? "✅" : "❌")}");
                Debug.Log($"🔄 LightingPanelMood: {(lightingPanelMood != null ? "✅" : "❌")}");
                Debug.Log($"🔄 LightingPanelDirection: {(lightingPanelDirection != null ? "✅" : "❌")}");
                Debug.Log($"💡 SettingsPanelBackdrop: {(settingsPanelBackdrop != null ? "✅" : "❌")}");
                Debug.Log($"💡 LightingPanelOverlay: {(lightingPanelOverlay != null ? "✅" : "❌")}");
                Debug.Log($"🌑 ShadowPanelOverlay: {(shadowPanelOverlay != null ? "✅" : "❌")}");
            }
        }

        /// <summary>
        /// LightingPanelControllerを遅延取得・初期化。
        /// </summary>
        public LightingPanelController GetLightingPanelController()
        {
            if (lightingPanelController == null)
            {
                lightingPanelController = Object.FindFirstObjectByType<LightingPanelController>();
                if (lightingPanelController == null)
                {
                    Debug.Log("💡 LightingPanelController not found - creating automatically (lazy)");
                    var lightingObj = new GameObject("LightingPanelController");
                    lightingPanelController = lightingObj.AddComponent<LightingPanelController>();
                }

                if (lightingPanelController != null)
                {
                    lightingPanelController.Initialize();
                    lightingPanelController.OnWarning += (code, message) => onWarning?.Invoke(code, message);
                    lightingPanelController.OnError += (code, message) => onError?.Invoke(code, message);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log("[Init] LightingPanelController initialized (lazy)");
#endif
                }
            }
            return lightingPanelController;
        }

        /// <summary>
        /// Issue #442: ライティング・シャドウ設定を再適用。
        /// </summary>
        public void ReapplyLightingSettings()
        {
            var lightingPanel = GetLightingPanelController();
            if (lightingPanel != null)
            {
                lightingPanel.ReapplyAllSettings();
                Debug.Log("💡 Issue #442: Reapplied lighting and shadow settings");
            }
            else
            {
                Debug.LogWarning("⚠️ LightingPanelController could not be initialized");
            }
        }

        /// <summary>
        /// ライティングパネルを表示。
        /// </summary>
        public void ShowLightingPanel()
        {
            Debug.Log($"📋 ShowLightingPanel called");
            Debug.Log($"📋 settingsPanelBackdrop is null: {settingsPanelBackdrop == null}");
            Debug.Log($"📋 lightingPanelOverlay is null: {lightingPanelOverlay == null}");
            HideAllPanels();

            GetLightingPanelController();

            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.pickingMode = PickingMode.Position;
                settingsPanelBackdrop.AddToClassList("visible");
                Debug.Log($"📋 settingsPanelBackdrop classes after: {string.Join(", ", settingsPanelBackdrop.GetClasses())}");
                Debug.Log($"📋 settingsPanelBackdrop display: {settingsPanelBackdrop.resolvedStyle.display}");
            }
            else
            {
                Debug.LogWarning("⚠️ settingsPanelBackdrop is NULL - cannot show backdrop");
            }
            if (lightingPanelOverlay != null)
            {
                lightingPanelOverlay.AddToClassList("visible");
                Debug.Log($"📋 lightingPanelOverlay classes after: {string.Join(", ", lightingPanelOverlay.GetClasses())}");
                Debug.Log($"📋 lightingPanelOverlay display: {lightingPanelOverlay.resolvedStyle.display}");
                Debug.Log("💡 Lighting panel shown");
            }
            else
            {
                Debug.LogWarning("⚠️ lightingPanelOverlay is NULL - cannot show panel");
            }
        }

        /// <summary>
        /// シャドウパネルを表示。
        /// </summary>
        public void ShowShadowPanel()
        {
            Debug.Log($"📋 ShowShadowPanel called");
            Debug.Log($"📋 settingsPanelBackdrop is null: {settingsPanelBackdrop == null}");
            Debug.Log($"📋 shadowPanelOverlay is null: {shadowPanelOverlay == null}");
            HideAllPanels();

            GetLightingPanelController();

            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.pickingMode = PickingMode.Position;
                settingsPanelBackdrop.AddToClassList("visible");
                Debug.Log($"📋 settingsPanelBackdrop classes after: {string.Join(", ", settingsPanelBackdrop.GetClasses())}");
                Debug.Log($"📋 settingsPanelBackdrop display: {settingsPanelBackdrop.resolvedStyle.display}");
            }
            else
            {
                Debug.LogWarning("⚠️ settingsPanelBackdrop is NULL - cannot show backdrop");
            }
            if (shadowPanelOverlay != null)
            {
                shadowPanelOverlay.AddToClassList("visible");
                Debug.Log($"📋 shadowPanelOverlay classes after: {string.Join(", ", shadowPanelOverlay.GetClasses())}");
                Debug.Log($"📋 shadowPanelOverlay display: {shadowPanelOverlay.resolvedStyle.display}");
                Debug.Log("🌑 Shadow panel shown");
            }
            else
            {
                Debug.LogWarning("⚠️ shadowPanelOverlay is NULL - cannot show panel");
            }
        }

        /// <summary>
        /// すべてのパネルを非表示。
        /// </summary>
        public void HideAllPanels()
        {
            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.pickingMode = PickingMode.Ignore;
                settingsPanelBackdrop.RemoveFromClassList("visible");
            }
            if (lightingPanelOverlay != null)
            {
                lightingPanelOverlay.RemoveFromClassList("visible");
            }
            if (shadowPanelOverlay != null)
            {
                shadowPanelOverlay.RemoveFromClassList("visible");
            }
            Debug.Log("📋 All panels hidden");
        }

        private void OnTopButton1Click()
        {
            Debug.Log("💡 Top button 1 clicked: Opening Lighting Panel");
            TapticEngine.Selection();
            ShowLightingPanel();
        }

        private void OnTopButton2Click()
        {
            Debug.Log("🌑 Top button 2 clicked: Opening Shadow Panel");
            TapticEngine.Selection();
            ShowShadowPanel();
        }

        private void ShowLightingMood()
        {
            tabMood?.AddToClassList("is-selected");
            tabDirection?.RemoveFromClassList("is-selected");
            lightingPanelMood?.AddToClassList("is-active");
            lightingPanelDirection?.RemoveFromClassList("is-active");
            if (enableDebugLogging) Debug.Log("🔄 Switched to Mood tab");
        }

        private void ShowLightingDirection()
        {
            tabDirection?.AddToClassList("is-selected");
            tabMood?.RemoveFromClassList("is-selected");
            lightingPanelDirection?.AddToClassList("is-active");
            lightingPanelMood?.RemoveFromClassList("is-active");
            if (enableDebugLogging) Debug.Log("🔄 Switched to Direction tab");
        }
    }
}
