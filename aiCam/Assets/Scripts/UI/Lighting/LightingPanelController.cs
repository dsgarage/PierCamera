using UnityEngine;
using UnityEngine.UIElements;
using AICam.AR;

namespace AICam.UI
{
    /// <summary>
    /// Issue #120: ライティングパネルコントローラー
    /// プリセット、色温度、明るさ、影の調整UIを管理
    /// </summary>
    public class LightingPanelController : MonoBehaviour
    {
        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset lightingPanelAsset;

        [Header("Light Reference")]
        [SerializeField] private Light mainLight;

        // UI要素
        private VisualElement root;
        private VisualElement lightingPanelOverlay;
        private VisualElement lightingPanel;
        private Button closeButton;

        // プリセットボタン
        private Button presetAuto;
        private Button presetSunny;
        private Button presetCloudy;
        private Button presetIndoor;
        private Button presetWarm;
        private Button presetSunset;
        private Button currentPresetButton;

        // スライダー
        private Slider colorTempSlider;
        private Label colorTempValue;
        private Slider brightnessSlider;
        private Label brightnessValue;
        private Slider elevationSlider;
        private Label elevationValue;
        private Slider shadowIntensitySlider;
        private Label shadowIntensityValue;

        // トグル
        private Toggle shadowToggle;
        private Toggle arSyncToggle;

        // ソフトネスボタン
        private Button softHard;
        private Button softMedium;
        private Button softSoft;
        private Button currentSoftnessButton;

        // ライト方向コントロール
        private VisualElement lightDirectionBackground;
        private VisualElement lightDirectionKnob;
        private bool isDraggingKnob = false;

        // 現在の設定値
        private float colorTemperature = 5500f;
        private float brightness = 1.0f;
        private float lightAzimuth = 0f; // 水平角度（度）
        private float lightElevation = 50f; // 仰角（度）
        private float shadowIntensity = 0.6f;
        private LightShadows shadowSoftness = LightShadows.Soft;
        private bool isArSyncEnabled = true;

        // プリセット定義
        private readonly LightingPreset[] presets = new LightingPreset[]
        {
            new LightingPreset("Auto", 5500f, 1.0f, 50f, 0.6f, true),
            new LightingPreset("Sunny", 5500f, 1.5f, 60f, 0.8f, false),
            new LightingPreset("Cloudy", 6500f, 0.8f, 40f, 0.4f, false),
            new LightingPreset("Indoor", 4000f, 1.0f, 45f, 0.5f, false),
            new LightingPreset("Warm", 2700f, 0.7f, 35f, 0.5f, false),
            new LightingPreset("Sunset", 3000f, 0.6f, 15f, 1.0f, false),
        };

        public bool IsVisible => lightingPanelOverlay?.ClassListContains("visible") ?? false;

        void Awake()
        {
            // メインライトを自動検索
            if (mainLight == null)
            {
                var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var light in lights)
                {
                    if (light.type == LightType.Directional)
                    {
                        mainLight = light;
                        break;
                    }
                }
            }
        }

        void Start()
        {
            SetupUI();
        }

        void SetupUI()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            if (uiDocument == null)
            {
                Debug.LogError("[LightingPanel] UIDocument not found!");
                return;
            }

            root = uiDocument.rootVisualElement;

            // ライティングパネルをインスタンス化してルートに追加
            if (lightingPanelAsset != null)
            {
                var panelInstance = lightingPanelAsset.Instantiate();
                root.Add(panelInstance);

                lightingPanelOverlay = panelInstance.Q<VisualElement>("lightingPanelOverlay");
            }
            else
            {
                // アセットがない場合は既存のものを検索
                lightingPanelOverlay = root.Q<VisualElement>("lightingPanelOverlay");
            }

            if (lightingPanelOverlay == null)
            {
                Debug.LogWarning("[LightingPanel] lightingPanelOverlay not found!");
                return;
            }

            lightingPanel = lightingPanelOverlay.Q<VisualElement>("lightingPanel");

            // 閉じるボタン
            closeButton = lightingPanelOverlay.Q<Button>("lightingPanelClose");
            if (closeButton != null)
            {
                closeButton.clicked += Hide;
            }

            // オーバーレイクリックで閉じる
            lightingPanelOverlay.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == lightingPanelOverlay)
                {
                    Hide();
                }
            });

            // プリセットボタン
            SetupPresetButtons();

            // スライダー
            SetupSliders();

            // トグル
            SetupToggles();

            // ソフトネスボタン
            SetupSoftnessButtons();

            // ライト方向コントロール
            SetupLightDirectionControl();

            Debug.Log("[LightingPanel] UI setup complete");
        }

        void SetupPresetButtons()
        {
            presetAuto = lightingPanelOverlay.Q<Button>("presetAuto");
            presetSunny = lightingPanelOverlay.Q<Button>("presetSunny");
            presetCloudy = lightingPanelOverlay.Q<Button>("presetCloudy");
            presetIndoor = lightingPanelOverlay.Q<Button>("presetIndoor");
            presetWarm = lightingPanelOverlay.Q<Button>("presetWarm");
            presetSunset = lightingPanelOverlay.Q<Button>("presetSunset");

            currentPresetButton = presetAuto;

            presetAuto?.RegisterCallback<ClickEvent>(evt => SelectPreset(0, presetAuto));
            presetSunny?.RegisterCallback<ClickEvent>(evt => SelectPreset(1, presetSunny));
            presetCloudy?.RegisterCallback<ClickEvent>(evt => SelectPreset(2, presetCloudy));
            presetIndoor?.RegisterCallback<ClickEvent>(evt => SelectPreset(3, presetIndoor));
            presetWarm?.RegisterCallback<ClickEvent>(evt => SelectPreset(4, presetWarm));
            presetSunset?.RegisterCallback<ClickEvent>(evt => SelectPreset(5, presetSunset));
        }

        void SetupSliders()
        {
            // 色温度
            colorTempSlider = lightingPanelOverlay.Q<Slider>("colorTempSlider");
            colorTempValue = lightingPanelOverlay.Q<Label>("colorTempValue");
            if (colorTempSlider != null)
            {
                colorTempSlider.RegisterValueChangedCallback(evt =>
                {
                    colorTemperature = evt.newValue;
                    UpdateColorTempDisplay();
                    ApplyLighting();
                    ClearPresetSelection();
                });
            }

            // 明るさ
            brightnessSlider = lightingPanelOverlay.Q<Slider>("brightnessSlider");
            brightnessValue = lightingPanelOverlay.Q<Label>("brightnessValue");
            if (brightnessSlider != null)
            {
                brightnessSlider.RegisterValueChangedCallback(evt =>
                {
                    brightness = evt.newValue;
                    UpdateBrightnessDisplay();
                    ApplyLighting();
                    ClearPresetSelection();
                });
            }

            // 仰角
            elevationSlider = lightingPanelOverlay.Q<Slider>("elevationSlider");
            elevationValue = lightingPanelOverlay.Q<Label>("elevationValue");
            if (elevationSlider != null)
            {
                elevationSlider.RegisterValueChangedCallback(evt =>
                {
                    lightElevation = evt.newValue;
                    UpdateElevationDisplay();
                    ApplyLightDirection();
                    ClearPresetSelection();
                });
            }

            // 影の濃さ
            shadowIntensitySlider = lightingPanelOverlay.Q<Slider>("shadowIntensitySlider");
            shadowIntensityValue = lightingPanelOverlay.Q<Label>("shadowIntensityValue");
            if (shadowIntensitySlider != null)
            {
                shadowIntensitySlider.RegisterValueChangedCallback(evt =>
                {
                    shadowIntensity = evt.newValue;
                    UpdateShadowIntensityDisplay();
                    ApplyShadow();
                });
            }
        }

        void SetupToggles()
        {
            shadowToggle = lightingPanelOverlay.Q<Toggle>("shadowToggle");
            if (shadowToggle != null)
            {
                shadowToggle.RegisterValueChangedCallback(evt =>
                {
                    ApplyShadow();
                });
            }

            arSyncToggle = lightingPanelOverlay.Q<Toggle>("arSyncToggle");
            if (arSyncToggle != null)
            {
                arSyncToggle.RegisterValueChangedCallback(evt =>
                {
                    isArSyncEnabled = evt.newValue;
                    ApplyArSync();
                });
            }
        }

        void SetupSoftnessButtons()
        {
            softHard = lightingPanelOverlay.Q<Button>("softHard");
            softMedium = lightingPanelOverlay.Q<Button>("softMedium");
            softSoft = lightingPanelOverlay.Q<Button>("softSoft");

            currentSoftnessButton = softMedium;

            softHard?.RegisterCallback<ClickEvent>(evt => SelectSoftness(LightShadows.Hard, softHard));
            softMedium?.RegisterCallback<ClickEvent>(evt => SelectSoftness(LightShadows.Soft, softMedium));
            softSoft?.RegisterCallback<ClickEvent>(evt => SelectSoftness(LightShadows.Soft, softSoft)); // Unity doesn't have "extra soft"
        }

        void SetupLightDirectionControl()
        {
            lightDirectionBackground = lightingPanelOverlay.Q<VisualElement>("lightDirectionBackground");
            lightDirectionKnob = lightingPanelOverlay.Q<VisualElement>("lightDirectionKnob");

            if (lightDirectionBackground != null && lightDirectionKnob != null)
            {
                lightDirectionBackground.RegisterCallback<PointerDownEvent>(OnKnobPointerDown);
                lightDirectionBackground.RegisterCallback<PointerMoveEvent>(OnKnobPointerMove);
                lightDirectionBackground.RegisterCallback<PointerUpEvent>(OnKnobPointerUp);
                lightDirectionBackground.RegisterCallback<PointerLeaveEvent>(OnKnobPointerLeave);
            }
        }

        void OnKnobPointerDown(PointerDownEvent evt)
        {
            isDraggingKnob = true;
            lightDirectionBackground.CapturePointer(evt.pointerId);
            UpdateKnobPosition(evt.localPosition);
        }

        void OnKnobPointerMove(PointerMoveEvent evt)
        {
            if (isDraggingKnob)
            {
                UpdateKnobPosition(evt.localPosition);
            }
        }

        void OnKnobPointerUp(PointerUpEvent evt)
        {
            isDraggingKnob = false;
            lightDirectionBackground.ReleasePointer(evt.pointerId);
        }

        void OnKnobPointerLeave(PointerLeaveEvent evt)
        {
            if (isDraggingKnob)
            {
                isDraggingKnob = false;
                lightDirectionBackground.ReleasePointer(evt.pointerId);
            }
        }

        void UpdateKnobPosition(Vector2 localPosition)
        {
            if (lightDirectionBackground == null || lightDirectionKnob == null) return;

            var bounds = lightDirectionBackground.contentRect;
            float centerX = bounds.width / 2f;
            float centerY = bounds.height / 2f;
            float radius = Mathf.Min(centerX, centerY) - 20f;

            // 中心からのオフセット
            float dx = localPosition.x - centerX;
            float dy = localPosition.y - centerY;

            // 半径内に制限
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            if (distance > radius)
            {
                dx = dx / distance * radius;
                dy = dy / distance * radius;
            }

            // ノブ位置を更新
            lightDirectionKnob.style.left = centerX + dx - 12f;
            lightDirectionKnob.style.top = centerY + dy - 12f;
            lightDirectionKnob.style.translate = StyleKeyword.None;

            // 角度を計算（北=0°、時計回りで増加）
            lightAzimuth = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;

            ApplyLightDirection();
            ClearPresetSelection();
        }

        void SelectPreset(int index, Button button)
        {
            if (index < 0 || index >= presets.Length) return;

            var preset = presets[index];

            // プリセット値を適用
            colorTemperature = preset.colorTemperature;
            brightness = preset.brightness;
            lightElevation = preset.elevation;
            shadowIntensity = preset.shadowIntensity;
            isArSyncEnabled = preset.arSync;

            // UI更新
            if (colorTempSlider != null) colorTempSlider.SetValueWithoutNotify(colorTemperature);
            if (brightnessSlider != null) brightnessSlider.SetValueWithoutNotify(brightness);
            if (elevationSlider != null) elevationSlider.SetValueWithoutNotify(lightElevation);
            if (shadowIntensitySlider != null) shadowIntensitySlider.SetValueWithoutNotify(shadowIntensity);
            if (arSyncToggle != null) arSyncToggle.SetValueWithoutNotify(isArSyncEnabled);

            UpdateColorTempDisplay();
            UpdateBrightnessDisplay();
            UpdateElevationDisplay();
            UpdateShadowIntensityDisplay();

            // ボタン選択状態を更新
            currentPresetButton?.RemoveFromClassList("preset-selected");
            button?.AddToClassList("preset-selected");
            currentPresetButton = button;

            // ライティング適用
            ApplyLighting();
            ApplyLightDirection();
            ApplyShadow();
            ApplyArSync();

            TapticEngine.Selection();
            Debug.Log($"[LightingPanel] Selected preset: {preset.name}");
        }

        void ClearPresetSelection()
        {
            currentPresetButton?.RemoveFromClassList("preset-selected");
            currentPresetButton = null;
        }

        void SelectSoftness(LightShadows softness, Button button)
        {
            shadowSoftness = softness;

            currentSoftnessButton?.RemoveFromClassList("softness-selected");
            button?.AddToClassList("softness-selected");
            currentSoftnessButton = button;

            ApplyShadow();
            TapticEngine.Selection();
        }

        void UpdateColorTempDisplay()
        {
            if (colorTempValue != null)
            {
                colorTempValue.text = $"{colorTemperature:F0}K";
            }
        }

        void UpdateBrightnessDisplay()
        {
            if (brightnessValue != null)
            {
                brightnessValue.text = $"{brightness:F1}";
            }
        }

        void UpdateElevationDisplay()
        {
            if (elevationValue != null)
            {
                elevationValue.text = $"{lightElevation:F0}°";
            }
        }

        void UpdateShadowIntensityDisplay()
        {
            if (shadowIntensityValue != null)
            {
                shadowIntensityValue.text = $"{shadowIntensity:F1}";
            }
        }

        void ApplyLighting()
        {
            if (mainLight == null) return;

            // 色温度を色に変換
            mainLight.color = Mathf.CorrelatedColorTemperatureToRGB(colorTemperature);
            mainLight.intensity = brightness;

            Debug.Log($"[LightingPanel] Applied: ColorTemp={colorTemperature}K, Brightness={brightness}");
        }

        void ApplyLightDirection()
        {
            if (mainLight == null) return;

            // 方位角と仰角からライトの向きを計算
            Quaternion rotation = Quaternion.Euler(lightElevation, lightAzimuth, 0f);
            mainLight.transform.rotation = rotation;

            Debug.Log($"[LightingPanel] Light direction: Azimuth={lightAzimuth:F0}°, Elevation={lightElevation:F0}°");
        }

        void ApplyShadow()
        {
            if (mainLight == null) return;

            bool shadowEnabled = shadowToggle?.value ?? true;

            if (shadowEnabled)
            {
                mainLight.shadows = shadowSoftness;
                mainLight.shadowStrength = shadowIntensity;
            }
            else
            {
                mainLight.shadows = LightShadows.None;
            }

            Debug.Log($"[LightingPanel] Shadow: enabled={shadowEnabled}, intensity={shadowIntensity}, softness={shadowSoftness}");
        }

        void ApplyArSync()
        {
            var arLightEstimation = FindFirstObjectByType<ARLightEstimationController>();
            if (arLightEstimation != null)
            {
                arLightEstimation.enabled = isArSyncEnabled;
                Debug.Log($"[LightingPanel] AR Sync: {isArSyncEnabled}");
            }
        }

        /// <summary>
        /// パネルを表示
        /// </summary>
        public void Show()
        {
            if (lightingPanelOverlay != null)
            {
                lightingPanelOverlay.AddToClassList("visible");
                TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
                Debug.Log("[LightingPanel] Show");
            }
        }

        /// <summary>
        /// パネルを非表示
        /// </summary>
        public void Hide()
        {
            if (lightingPanelOverlay != null)
            {
                lightingPanelOverlay.RemoveFromClassList("visible");
                Debug.Log("[LightingPanel] Hide");
            }
        }

        /// <summary>
        /// 表示/非表示をトグル
        /// </summary>
        public void Toggle()
        {
            if (IsVisible)
            {
                Hide();
            }
            else
            {
                Show();
            }
        }
    }

    /// <summary>
    /// ライティングプリセット定義
    /// </summary>
    public struct LightingPreset
    {
        public string name;
        public float colorTemperature;
        public float brightness;
        public float elevation;
        public float shadowIntensity;
        public bool arSync;

        public LightingPreset(string name, float colorTemp, float brightness, float elevation, float shadowIntensity, bool arSync)
        {
            this.name = name;
            this.colorTemperature = colorTemp;
            this.brightness = brightness;
            this.elevation = elevation;
            this.shadowIntensity = shadowIntensity;
            this.arSync = arSync;
        }
    }
}
