using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using AICam.AR;

namespace AICam.UI
{
    /// <summary>
    /// Issue #120: ライティングパネルコントローラー
    /// プリセット、色温度、明るさ、影の調整UIを管理
    /// lilToonシェーダー対応（グローバルシェーダープロパティ使用）
    /// </summary>
    public class LightingPanelController : MonoBehaviour
    {
        [Header("UI Document")]
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private VisualTreeAsset lightingPanelAsset;

        [Header("Light Reference")]
        [SerializeField] private Light mainLight;

        // アラートイベント
        public event Action<string, string> OnWarning;
        public event Action<string, string> OnError;

        // lilToonグローバルシェーダープロパティID
        private static readonly int _LilMainLightColor = Shader.PropertyToID("_lil_MainLightColor");
        private static readonly int _LilMainLightDirection = Shader.PropertyToID("_lil_MainLightDirection");
        private static readonly int _LilEnvironmentStrength = Shader.PropertyToID("_lil_EnvironmentStrength");

        // 標準シェーダープロパティID（フォールバック用）
        private static readonly int _MainLightColor = Shader.PropertyToID("_MainLightColor");
        private static readonly int _MainLightDirection = Shader.PropertyToID("_MainLightDirection");

        // lilToon対応状態
        private bool lilToonSupported = false;

        // Issue #75: AR平面シャドウレシーバー
        private ARPlaneShadowReceiver arPlaneShadowReceiver;

        // UI要素
        private VisualElement root;
        private VisualElement settingsPanelBackdrop;
        private VisualElement lightingPanelOverlay;
        private VisualElement shadowPanelOverlay;
        private VisualElement lightingPanel;
        private VisualElement shadowPanel;
        private Button lightingCloseButton;
        private Button shadowCloseButton;

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

            // lilToonシェーダーの存在を確認
            DetectLilToonSupport();
        }

        /// <summary>
        /// lilToonシェーダーの存在を確認
        /// </summary>
        void DetectLilToonSupport()
        {
            // lilToonシェーダーが存在するか確認
            var lilToonShader = Shader.Find("lilToon");
            var lilToonLiteShader = Shader.Find("Hidden/lilToonLite");
            var lilToonMultiShader = Shader.Find("_lil/lilToonMulti");

            lilToonSupported = (lilToonShader != null || lilToonLiteShader != null || lilToonMultiShader != null);

            if (lilToonSupported)
            {
                Debug.Log("[LightingPanel] lilToon shader detected - using global shader properties");
            }
            else
            {
                Debug.Log("[LightingPanel] lilToon not detected - using standard Unity Light");
            }
        }

        void Start()
        {
            Debug.Log("[LightingPanel] Start() called");
            SetupUI();
        }

        void OnEnable()
        {
            Debug.Log("[LightingPanel] OnEnable() called");
            // UIDocumentがまだロードされていない場合、遅延初期化
            if (root == null && uiDocument != null && uiDocument.rootVisualElement != null)
            {
                Debug.Log("[LightingPanel] Re-initializing UI in OnEnable");
                SetupUI();
            }
        }

        /// <summary>
        /// 遅延初期化用（外部からの呼び出し）
        /// </summary>
        public void Initialize()
        {
            Debug.Log("[LightingPanel] Initialize() called externally");
            SetupUI();
        }

        void SetupUI()
        {
            Debug.Log("[LightingPanel] SetupUI() starting...");

            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
                Debug.Log($"[LightingPanel] GetComponent<UIDocument>: {uiDocument != null}");
            }

            // UIDocumentが見つからない場合は、シーン内から検索
            if (uiDocument == null)
            {
                uiDocument = FindFirstObjectByType<UIDocument>();
                Debug.Log($"[LightingPanel] FindFirstObjectByType<UIDocument>: {uiDocument != null}");
            }

            if (uiDocument == null)
            {
                Debug.LogError("[LightingPanel] UIDocument not found!");
                return;
            }

            root = uiDocument.rootVisualElement;
            Debug.Log($"[LightingPanel] Root element: {root != null}, childCount: {root?.childCount}");

            // ルート要素の全ての子を列挙（デバッグ用）
            if (root != null)
            {
                Debug.Log($"[LightingPanel] Root children:");
                foreach (var child in root.Children())
                {
                    Debug.Log($"  - {child.name ?? "(unnamed)"} class={string.Join(",", child.GetClasses())}");
                }
            }

            // Issue #120: 分離されたパネル要素を検索
            settingsPanelBackdrop = root.Q<VisualElement>("settingsPanelBackdrop");
            lightingPanelOverlay = root.Q<VisualElement>("lightingPanelOverlay");
            shadowPanelOverlay = root.Q<VisualElement>("shadowPanelOverlay");

            Debug.Log($"[LightingPanel] settingsPanelBackdrop: {settingsPanelBackdrop != null}");
            Debug.Log($"[LightingPanel] lightingPanelOverlay: {lightingPanelOverlay != null}");
            Debug.Log($"[LightingPanel] shadowPanelOverlay: {shadowPanelOverlay != null}");

            if (lightingPanelOverlay == null)
            {
                Debug.LogWarning("[LightingPanel] lightingPanelOverlay not found!");
                return;
            }

            if (shadowPanelOverlay == null)
            {
                Debug.LogWarning("[LightingPanel] shadowPanelOverlay not found!");
            }

            lightingPanel = lightingPanelOverlay.Q<VisualElement>("lightingPanel");
            shadowPanel = shadowPanelOverlay?.Q<VisualElement>("shadowPanel");

            // パネルオーバーレイ内のイベントがバックドロップに伝播しないようにする
            lightingPanelOverlay?.RegisterCallback<PointerDownEvent>(evt =>
            {
                Debug.Log($"[LightingPanel] PointerDown on lightingPanelOverlay: pos={evt.position}, target={evt.target}");
                // イベントの伝播を停止してバックドロップに到達しないようにする
                evt.StopPropagation();
            });

            shadowPanelOverlay?.RegisterCallback<PointerDownEvent>(evt =>
            {
                Debug.Log($"[LightingPanel] PointerDown on shadowPanelOverlay: pos={evt.position}, target={evt.target}");
                evt.StopPropagation();
            });

            // デバッグ: パネル全体でのポインターイベントを監視
            lightingPanel?.RegisterCallback<PointerDownEvent>(evt =>
            {
                Debug.Log($"[LightingPanel] PointerDown on lightingPanel: pos={evt.position}, target={evt.target}");
            }, TrickleDown.TrickleDown);

            // 閉じるボタン（CameraCaptureControllerで処理済みだが念のため）
            lightingCloseButton = lightingPanelOverlay.Q<Button>("lightingPanelClose");
            shadowCloseButton = shadowPanelOverlay?.Q<Button>("shadowPanelClose");

            // プリセットボタン（ライティングパネル内）
            SetupPresetButtons();

            // スライダー（両パネルから取得）
            SetupSliders();

            // トグル（両パネルから取得）
            SetupToggles();

            // ソフトネスボタン（シャドウパネル内）
            SetupSoftnessButtons();

            // ライト方向コントロール（ライティングパネル内）
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

            // .clicked イベントを使用（より確実）+ TrickleDownでPointerDownも追加
            SetupButtonWithDebug(presetAuto, "Auto", () => SelectPreset(0, presetAuto));
            SetupButtonWithDebug(presetSunny, "Sunny", () => SelectPreset(1, presetSunny));
            SetupButtonWithDebug(presetCloudy, "Cloudy", () => SelectPreset(2, presetCloudy));
            SetupButtonWithDebug(presetIndoor, "Indoor", () => SelectPreset(3, presetIndoor));
            SetupButtonWithDebug(presetWarm, "Warm", () => SelectPreset(4, presetWarm));
            SetupButtonWithDebug(presetSunset, "Sunset", () => SelectPreset(5, presetSunset));

            Debug.Log($"[LightingPanel] Preset buttons: Auto={presetAuto != null}, Sunny={presetSunny != null}, Cloudy={presetCloudy != null}");
        }

        void SetupButtonWithDebug(Button button, string name, Action onClick)
        {
            if (button == null) return;

            // focusableを有効に
            button.focusable = true;
            button.pickingMode = PickingMode.Position;

            // クリックイベント
            button.clicked += () =>
            {
                Debug.Log($"[LightingPanel] Button .clicked: {name}");
                onClick?.Invoke();
            };

            // デバッグ用: PointerDownをTrickleDownで受信（伝播は止めない）
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                Debug.Log($"[LightingPanel] PointerDown on {name}: pos={evt.position}");
                // 注意: StopPropagationするとClickEventが発火しなくなる可能性があるため、ここでは止めない
            }, TrickleDown.TrickleDown);

            // デバッグ用: PointerUpを監視
            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                Debug.Log($"[LightingPanel] PointerUp on {name}");
            });
        }

        void SetupSliders()
        {
            // 色温度（ライティングパネル内）
            colorTempSlider = lightingPanelOverlay.Q<Slider>("colorTempSlider");
            colorTempValue = lightingPanelOverlay.Q<Label>("colorTempValue");
            if (colorTempSlider != null)
            {
                colorTempSlider.focusable = true;
                colorTempSlider.RegisterValueChangedCallback(evt =>
                {
                    Debug.Log($"[LightingPanel] ColorTemp changed: {evt.newValue}");
                    colorTemperature = evt.newValue;
                    UpdateColorTempDisplay();
                    ApplyLighting();
                    ClearPresetSelection();
                });
            }

            // 明るさ（ライティングパネル内）
            brightnessSlider = lightingPanelOverlay.Q<Slider>("brightnessSlider");
            brightnessValue = lightingPanelOverlay.Q<Label>("brightnessValue");
            if (brightnessSlider != null)
            {
                brightnessSlider.focusable = true;
                brightnessSlider.RegisterValueChangedCallback(evt =>
                {
                    Debug.Log($"[LightingPanel] Brightness changed: {evt.newValue}");
                    brightness = evt.newValue;
                    UpdateBrightnessDisplay();
                    ApplyLighting();
                    ClearPresetSelection();
                });
            }

            // 仰角（ライティングパネル内）
            elevationSlider = lightingPanelOverlay.Q<Slider>("elevationSlider");
            elevationValue = lightingPanelOverlay.Q<Label>("elevationValue");
            if (elevationSlider != null)
            {
                elevationSlider.focusable = true;
                elevationSlider.RegisterValueChangedCallback(evt =>
                {
                    Debug.Log($"[LightingPanel] Elevation changed: {evt.newValue}");
                    lightElevation = evt.newValue;
                    UpdateElevationDisplay();
                    ApplyLightDirection();
                    ClearPresetSelection();
                });
            }

            Debug.Log($"[LightingPanel] Sliders: ColorTemp={colorTempSlider != null}, Brightness={brightnessSlider != null}, Elevation={elevationSlider != null}");

            // 影の濃さ（シャドウパネル内）
            if (shadowPanelOverlay != null)
            {
                shadowIntensitySlider = shadowPanelOverlay.Q<Slider>("shadowIntensitySlider");
                shadowIntensityValue = shadowPanelOverlay.Q<Label>("shadowIntensityValue");
                if (shadowIntensitySlider != null)
                {
                    shadowIntensitySlider.focusable = true;
                    shadowIntensitySlider.RegisterValueChangedCallback(evt =>
                    {
                        Debug.Log($"[LightingPanel] ShadowIntensity changed: {evt.newValue}");
                        shadowIntensity = evt.newValue;
                        UpdateShadowIntensityDisplay();
                        ApplyShadow();
                    });
                }
                Debug.Log($"[LightingPanel] Shadow slider: {shadowIntensitySlider != null}");
            }
        }

        void SetupToggles()
        {
            // シャドウトグル（シャドウパネル内）
            if (shadowPanelOverlay != null)
            {
                shadowToggle = shadowPanelOverlay.Q<Toggle>("shadowToggle");
                if (shadowToggle != null)
                {
                    shadowToggle.focusable = true;
                    shadowToggle.RegisterValueChangedCallback(evt =>
                    {
                        Debug.Log($"[LightingPanel] ShadowToggle changed: {evt.newValue}");
                        ApplyShadow();
                    });
                }
            }

            // AR同期トグル（ライティングパネル内）
            arSyncToggle = lightingPanelOverlay.Q<Toggle>("arSyncToggle");
            if (arSyncToggle != null)
            {
                arSyncToggle.focusable = true;
                arSyncToggle.RegisterValueChangedCallback(evt =>
                {
                    Debug.Log($"[LightingPanel] ARSyncToggle changed: {evt.newValue}");
                    isArSyncEnabled = evt.newValue;
                    ApplyArSync();
                });
            }

            Debug.Log($"[LightingPanel] Toggles: Shadow={shadowToggle != null}, ARSync={arSyncToggle != null}");
        }

        void SetupSoftnessButtons()
        {
            // ソフトネスボタン（シャドウパネル内）
            if (shadowPanelOverlay != null)
            {
                softHard = shadowPanelOverlay.Q<Button>("softHard");
                softMedium = shadowPanelOverlay.Q<Button>("softMedium");
                softSoft = shadowPanelOverlay.Q<Button>("softSoft");

                currentSoftnessButton = softMedium;

                // SetupButtonWithDebugを使用
                SetupButtonWithDebug(softHard, "Hard", () => SelectSoftness(LightShadows.Hard, softHard));
                SetupButtonWithDebug(softMedium, "Medium", () => SelectSoftness(LightShadows.Soft, softMedium));
                SetupButtonWithDebug(softSoft, "Soft", () => SelectSoftness(LightShadows.Soft, softSoft));

                Debug.Log($"[LightingPanel] Softness buttons: Hard={softHard != null}, Medium={softMedium != null}, Soft={softSoft != null}");
            }
        }

        void SetupLightDirectionControl()
        {
            lightDirectionBackground = lightingPanelOverlay.Q<VisualElement>("lightDirectionBackground");
            lightDirectionKnob = lightingPanelOverlay.Q<VisualElement>("lightDirectionKnob");

            Debug.Log($"[LightingPanel] LightDirectionBackground: {lightDirectionBackground != null}");
            Debug.Log($"[LightingPanel] LightDirectionKnob: {lightDirectionKnob != null}");

            if (lightDirectionBackground != null && lightDirectionKnob != null)
            {
                // focusableを有効にしてポインターイベントを受信できるようにする
                lightDirectionBackground.focusable = true;

                // TrickleDownフェーズでイベントを受信（子要素よりも先に処理）
                lightDirectionBackground.RegisterCallback<PointerDownEvent>(OnKnobPointerDown, TrickleDown.TrickleDown);
                lightDirectionBackground.RegisterCallback<PointerMoveEvent>(OnKnobPointerMove, TrickleDown.TrickleDown);
                lightDirectionBackground.RegisterCallback<PointerUpEvent>(OnKnobPointerUp, TrickleDown.TrickleDown);
                lightDirectionBackground.RegisterCallback<PointerLeaveEvent>(OnKnobPointerLeave, TrickleDown.TrickleDown);

                Debug.Log("[LightingPanel] Light direction control events registered with TrickleDown");
            }
        }

        void OnKnobPointerDown(PointerDownEvent evt)
        {
            Debug.Log($"[LightingPanel] OnKnobPointerDown: pos={evt.localPosition}, pointerId={evt.pointerId}");
            isDraggingKnob = true;
            lightDirectionBackground.CapturePointer(evt.pointerId);
            UpdateKnobPosition(evt.localPosition);
            evt.StopPropagation(); // イベントの伝播を止める
        }

        void OnKnobPointerMove(PointerMoveEvent evt)
        {
            if (isDraggingKnob)
            {
                Debug.Log($"[LightingPanel] OnKnobPointerMove: pos={evt.localPosition}");
                UpdateKnobPosition(evt.localPosition);
            }
        }

        void OnKnobPointerUp(PointerUpEvent evt)
        {
            Debug.Log($"[LightingPanel] OnKnobPointerUp");
            isDraggingKnob = false;
            lightDirectionBackground.ReleasePointer(evt.pointerId);
        }

        void OnKnobPointerLeave(PointerLeaveEvent evt)
        {
            Debug.Log($"[LightingPanel] OnKnobPointerLeave");
            if (isDraggingKnob)
            {
                isDraggingKnob = false;
                lightDirectionBackground.ReleasePointer(evt.pointerId);
            }
        }

        void UpdateKnobPosition(Vector2 localPosition)
        {
            if (lightDirectionBackground == null || lightDirectionKnob == null) return;

            // Issue #74: contentRectを使用（パディング/ボーダー内部の領域）
            // localPositionはボーダーを含む要素の左上からの座標なので、
            // ボーダー幅を引いてcontentRect内の座標に変換する
            var contentRect = lightDirectionBackground.contentRect;
            float contentWidth = contentRect.width;
            float contentHeight = contentRect.height;

            // レイアウトが完了していない場合はスキップ
            if (contentWidth <= 0 || contentHeight <= 0)
            {
                Debug.LogWarning("[LightingPanel] contentRect not ready yet");
                return;
            }

            // ボーダー幅を取得（USS: border-width: 1px）
            float borderLeft = lightDirectionBackground.resolvedStyle.borderLeftWidth;
            float borderTop = lightDirectionBackground.resolvedStyle.borderTopWidth;

            // localPositionからボーダー分を引いてcontentRect内座標に変換
            float contentX = localPosition.x - borderLeft;
            float contentY = localPosition.y - borderTop;

            // contentRectの中心
            float centerX = contentWidth / 2f;
            float centerY = contentHeight / 2f;

            // ノブサイズ（USS: width: 14px, height: 14px）
            float knobRadius = 7f;

            // 方向ラベル用のマージン（12px）を考慮した有効半径
            float effectiveRadius = Mathf.Min(centerX, centerY) - 12f;

            // 中心からのオフセット（contentRect内座標）
            float dx = contentX - centerX;
            float dy = contentY - centerY;

            // 半径内に制限
            float distance = Mathf.Sqrt(dx * dx + dy * dy);
            if (distance > effectiveRadius)
            {
                dx = dx / distance * effectiveRadius;
                dy = dy / distance * effectiveRadius;
            }

            // ノブ位置を更新（style.left/topはcontentRect基準）
            lightDirectionKnob.style.left = centerX + dx - knobRadius;
            lightDirectionKnob.style.top = centerY + dy - knobRadius;
            lightDirectionKnob.style.translate = StyleKeyword.None;

            // 角度を計算（北=0°、時計回りで増加）
            lightAzimuth = Mathf.Atan2(dx, -dy) * Mathf.Rad2Deg;

            Debug.Log($"[LightingPanel] Knob: content({contentX:F1},{contentY:F1}), center({centerX:F1},{centerY:F1}), dx={dx:F1}, dy={dy:F1}");

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
            Debug.Log($"[LightingPanel] Selected softness: {softness}");
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
            // メインライトを再検索（後から生成されている可能性）
            if (mainLight == null)
            {
                var lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
                foreach (var light in lights)
                {
                    if (light.type == LightType.Directional)
                    {
                        mainLight = light;
                        Debug.Log($"[LightingPanel] Found directional light: {light.name}");
                        break;
                    }
                }
            }

            // 色温度を色に変換
            Color lightColor = Mathf.CorrelatedColorTemperatureToRGB(colorTemperature);

            // Unityライトに適用
            if (mainLight != null)
            {
                mainLight.color = lightColor;
                mainLight.intensity = brightness;
                Debug.Log($"[LightingPanel] MainLight updated: color={lightColor}, intensity={brightness}");
            }
            else
            {
                Debug.LogWarning("[LightingPanel] No directional light found in scene!");
            }

            // lilToonグローバルシェーダープロパティに適用
            ApplyToGlobalShaderProperties(lightColor);

            Debug.Log($"[LightingPanel] Applied: ColorTemp={colorTemperature}K, Brightness={brightness}, lilToon={lilToonSupported}");
        }

        /// <summary>
        /// グローバルシェーダープロパティに適用（lilToon/MToon対応）
        /// </summary>
        void ApplyToGlobalShaderProperties(Color lightColor)
        {
            // 明るさを色に適用
            Color adjustedColor = lightColor * brightness;

            // lilToonグローバルプロパティ
            Shader.SetGlobalColor("_lil_MainLightColor", adjustedColor);
            Shader.SetGlobalFloat("_lil_MainLightIntensity", brightness);

            // 汎用グローバルプロパティ（他のシェーダー用）
            Shader.SetGlobalColor("_MainLightColor", adjustedColor);
            Shader.SetGlobalFloat("_MainLightIntensity", brightness);

            // アバターのマテリアルにも直接適用を試みる
            ApplyToAvatarMaterials(adjustedColor);
        }

        // 非対応シェーダー警告を一度だけ表示するためのフラグ
        private bool hasShownUnsupportedShaderWarning = false;
        private string lastUnsupportedShaderName = "";

        /// <summary>
        /// ロード済みアバターのマテリアルに直接適用
        /// Issue #433: sharedMaterialsとmaterialsの両方に適用
        /// </summary>
        void ApplyToAvatarMaterials(Color lightColor)
        {
            // シーン内のすべてのRendererを検索
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
            int totalMaterials = 0;
            int supportedMaterials = 0;
            string unsupportedShaderName = "";

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                // ARプレーンやUI要素は除外
                if (renderer.gameObject.layer == LayerMask.NameToLayer("UI")) continue;

                // Issue #433: インスタンス化されたマテリアル（materials）を使用
                // sharedMaterialsだとVRMローダーで生成されたマテリアルに反映されない
                var materials = renderer.materials;
                foreach (var mat in materials)
                {
                    if (mat == null) continue;

                    totalMaterials++;
                    bool wasSupported = ApplyLightingToMaterial(mat, lightColor);
                    if (wasSupported)
                    {
                        supportedMaterials++;
                    }
                    else
                    {
                        unsupportedShaderName = mat.shader?.name ?? "Unknown";
                    }
                }
            }

            // アバターがロードされているのに全てのマテリアルが非対応の場合、警告表示
            if (totalMaterials > 0 && supportedMaterials == 0)
            {
                if (!hasShownUnsupportedShaderWarning || lastUnsupportedShaderName != unsupportedShaderName)
                {
                    hasShownUnsupportedShaderWarning = true;
                    lastUnsupportedShaderName = unsupportedShaderName;
                    OnWarning?.Invoke("W433", $"このアバターのシェーダー({unsupportedShaderName})はライティング調整に対応していません");
                    Debug.LogWarning($"[LightingPanel] Unsupported shader: {unsupportedShaderName}");
                }
            }
            else if (supportedMaterials > 0)
            {
                // 対応マテリアルがあった場合、フラグをリセット
                hasShownUnsupportedShaderWarning = false;
            }

            Debug.Log($"[LightingPanel] Applied lighting to {supportedMaterials}/{totalMaterials} materials");
        }

        /// <summary>
        /// Issue #433: 個別マテリアルにライティング設定を適用
        /// lilToon, MToon, MToon10, Standard シェーダーに対応
        /// </summary>
        /// <returns>対応シェーダーの場合true</returns>
        bool ApplyLightingToMaterial(Material mat, Color lightColor)
        {
            string shaderName = mat.shader?.name ?? "Unknown";
            bool anyPropertySet = false;

            // lilToonのプロパティ
            if (mat.HasProperty("_LightMaxLimit"))
            {
                mat.SetFloat("_LightMaxLimit", brightness * 1.5f);
                anyPropertySet = true;
            }

            if (mat.HasProperty("_LightMinLimit"))
            {
                mat.SetFloat("_LightMinLimit", Mathf.Max(0.05f, brightness * 0.3f));
                anyPropertySet = true;
            }

            // lilToonのシェード色調整
            if (mat.HasProperty("_ShadowEnvStrength"))
            {
                mat.SetFloat("_ShadowEnvStrength", 1.0f);
                anyPropertySet = true;
            }

            // MToon/lilToonの環境光強度
            if (mat.HasProperty("_IndirectLightIntensity"))
            {
                mat.SetFloat("_IndirectLightIntensity", brightness);
                anyPropertySet = true;
            }

            // MToon (VRM 0.x) のプロパティ
            if (mat.HasProperty("_LightColorAttenuation"))
            {
                // ライト色の減衰量（0=減衰なし、1=完全減衰）
                mat.SetFloat("_LightColorAttenuation", 0f);
                anyPropertySet = true;
            }

            // MToon10 (VRM 1.0) のプロパティ
            if (mat.HasProperty("_ShadeColor"))
            {
                // シェード色を明るさに応じて調整
                Color shadeColor = lightColor * brightness * 0.5f;
                shadeColor.a = 1f;
                mat.SetColor("_ShadeColor", shadeColor);
                anyPropertySet = true;
            }

            // MToon/MToon10の明るさ調整
            if (mat.HasProperty("_LitFactor"))
            {
                mat.SetFloat("_LitFactor", brightness);
                anyPropertySet = true;
            }

            // MToon10 GI影響度
            if (mat.HasProperty("_GiEqualization"))
            {
                mat.SetFloat("_GiEqualization", brightness);
                anyPropertySet = true;
            }

            // Standardシェーダー用
            if (mat.HasProperty("_MainLightColor"))
            {
                mat.SetColor("_MainLightColor", lightColor * brightness);
                anyPropertySet = true;
            }

            // Standard/URP Litの色調整
            if (mat.HasProperty("_Color"))
            {
                // 元の色を取得して明るさを調整
                Color baseColor = mat.GetColor("_Color");
                // 色温度の影響を控えめに適用（元の色を保持しつつ）
                Color adjustedColor = Color.Lerp(baseColor, lightColor, 0.3f);
                adjustedColor.a = baseColor.a;
                // 明るさは別途適用
                mat.SetColor("_Color", adjustedColor);
                anyPropertySet = true;
            }

            // URP Litの BaseColor
            if (mat.HasProperty("_BaseColor"))
            {
                Color baseColor = mat.GetColor("_BaseColor");
                Color adjustedColor = Color.Lerp(baseColor, lightColor, 0.3f);
                adjustedColor.a = baseColor.a;
                mat.SetColor("_BaseColor", adjustedColor);
                anyPropertySet = true;
            }

            // デバッグログ（初回のみ）
            if (!anyPropertySet)
            {
                Debug.LogWarning($"[LightingPanel] No lighting properties found for material '{mat.name}' (shader: {shaderName})");
            }

            return anyPropertySet;
        }

        void ApplyLightDirection()
        {
            // メインライトを再検索（後から生成されている可能性）
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

            // 方位角と仰角からライトの向きを計算
            Quaternion rotation = Quaternion.Euler(lightElevation, lightAzimuth, 0f);
            Vector3 lightDirection = rotation * Vector3.forward;

            // Unityライトに適用
            if (mainLight != null)
            {
                mainLight.transform.rotation = rotation;
                Debug.Log($"[LightingPanel] MainLight rotation updated: {rotation.eulerAngles}");
            }

            // グローバルシェーダープロパティにライト方向を適用
            Shader.SetGlobalVector("_lil_MainLightDirection", lightDirection);
            Shader.SetGlobalVector("_MainLightDirection", lightDirection);

            Debug.Log($"[LightingPanel] Light direction: Azimuth={lightAzimuth:F0}°, Elevation={lightElevation:F0}°");
        }

        void ApplyShadow()
        {
            bool shadowEnabled = shadowToggle?.value ?? true;

            // Unityライトに適用
            if (mainLight != null)
            {
                if (shadowEnabled)
                {
                    mainLight.shadows = shadowSoftness;
                    mainLight.shadowStrength = shadowIntensity;
                }
                else
                {
                    mainLight.shadows = LightShadows.None;
                }
            }

            // Issue #75: AR平面シャドウレシーバーに適用
            if (arPlaneShadowReceiver == null)
            {
                arPlaneShadowReceiver = FindFirstObjectByType<ARPlaneShadowReceiver>();
            }
            if (arPlaneShadowReceiver != null)
            {
                arPlaneShadowReceiver.SetShadowEnabled(shadowEnabled);
                arPlaneShadowReceiver.SetShadowIntensity(shadowIntensity);
            }

            // マテリアルのシャドウプロパティを更新
            ApplyShadowToMaterials(shadowEnabled);

            Debug.Log($"[LightingPanel] Shadow: enabled={shadowEnabled}, intensity={shadowIntensity}, softness={shadowSoftness}");
        }

        /// <summary>
        /// マテリアルのシャドウプロパティを更新
        /// Issue #433: sharedMaterialsからmaterialsに変更
        /// </summary>
        void ApplyShadowToMaterials(bool shadowEnabled)
        {
            var renderers = FindObjectsByType<Renderer>(FindObjectsSortMode.None);

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;

                // Issue #433: インスタンス化されたマテリアルを使用
                var materials = renderer.materials;
                foreach (var mat in materials)
                {
                    if (mat == null) continue;

                    ApplyShadowToMaterial(mat, shadowEnabled);
                }
            }

            // グローバルシャドウプロパティ
            Shader.SetGlobalFloat("_lil_ShadowStrength", shadowEnabled ? shadowIntensity : 0f);
        }

        // シェード色のオリジナル値を保持（リセット用）
        private Dictionary<Material, Color> originalShadeColors = new Dictionary<Material, Color>();

        /// <summary>
        /// Issue #433: 個別マテリアルにシャドウ設定を適用
        /// シャドウ強度は影部分（シェード色）の暗さを調整
        /// </summary>
        void ApplyShadowToMaterial(Material mat, bool shadowEnabled)
        {
            string shaderName = mat.shader?.name ?? "Unknown";
            bool anyPropertySet = false;

            // lilToonのシャドウ強度
            if (mat.HasProperty("_ShadowStrength"))
            {
                mat.SetFloat("_ShadowStrength", shadowEnabled ? shadowIntensity : 0f);
                anyPropertySet = true;
            }

            // lilToonの1影強度
            if (mat.HasProperty("_Shadow1stStrength"))
            {
                mat.SetFloat("_Shadow1stStrength", shadowEnabled ? shadowIntensity : 0f);
                anyPropertySet = true;
            }

            // lilToonの2影強度
            if (mat.HasProperty("_Shadow2ndStrength"))
            {
                mat.SetFloat("_Shadow2ndStrength", shadowEnabled ? shadowIntensity * 0.5f : 0f);
                anyPropertySet = true;
            }

            // MToonのシェード強度 - シェードシフト（影の境界位置）
            if (mat.HasProperty("_ShadeShift"))
            {
                // -1 ~ 1: 負の値で影が増える（より強い効果）
                float shiftValue = shadowEnabled ? Mathf.Lerp(0f, -0.8f, shadowIntensity) : 0f;
                mat.SetFloat("_ShadeShift", shiftValue);
                anyPropertySet = true;
            }

            // MToonのシェードトゥーニー（影の境界の滑らかさ）
            if (mat.HasProperty("_ShadeToony"))
            {
                // 0 ~ 1: 高いほどくっきりした影
                float toonyValue = shadowEnabled ? Mathf.Lerp(0.3f, 0.95f, shadowIntensity) : 0.5f;
                mat.SetFloat("_ShadeToony", toonyValue);
                anyPropertySet = true;
            }

            // MToonのシェード色を暗くする（オリジナル値を保持）
            if (mat.HasProperty("_ShadeColor"))
            {
                // オリジナル値を保存
                if (!originalShadeColors.ContainsKey(mat))
                {
                    originalShadeColors[mat] = mat.GetColor("_ShadeColor");
                }

                Color originalShade = originalShadeColors[mat];

                if (shadowEnabled)
                {
                    // 強度に応じてシェード色を暗くする（より強い効果）
                    float darkenFactor = Mathf.Lerp(1f, 0.1f, shadowIntensity);
                    Color newShade = new Color(
                        originalShade.r * darkenFactor,
                        originalShade.g * darkenFactor,
                        originalShade.b * darkenFactor,
                        originalShade.a
                    );
                    mat.SetColor("_ShadeColor", newShade);
                }
                else
                {
                    // オリジナルに戻す
                    mat.SetColor("_ShadeColor", originalShade);
                }
                anyPropertySet = true;
            }

            // MToon10のシャドウ設定 - シェーディングシフトファクター
            if (mat.HasProperty("_ShadingShiftFactor"))
            {
                float shiftFactor = shadowEnabled ? Mathf.Lerp(0f, -0.5f, shadowIntensity) : 0f;
                mat.SetFloat("_ShadingShiftFactor", shiftFactor);
                anyPropertySet = true;
            }

            // MToon10のシェーディングトゥーニーファクター
            if (mat.HasProperty("_ShadingToonyFactor"))
            {
                float toonyFactor = shadowEnabled ? Mathf.Lerp(0.3f, 0.98f, shadowIntensity) : 0.5f;
                mat.SetFloat("_ShadingToonyFactor", toonyFactor);
                anyPropertySet = true;
            }

            // 受影設定
            if (mat.HasProperty("_ShadowReceive"))
            {
                mat.SetFloat("_ShadowReceive", shadowEnabled ? 1f : 0f);
                anyPropertySet = true;
            }

            // MToon10のシャドウ受け
            if (mat.HasProperty("_ReceiveShadowRate"))
            {
                mat.SetFloat("_ReceiveShadowRate", shadowEnabled ? shadowIntensity : 0f);
                anyPropertySet = true;
            }

            if (anyPropertySet)
            {
                Debug.Log($"[LightingPanel] Shadow applied to {mat.name} (shader: {shaderName}), enabled={shadowEnabled}, intensity={shadowIntensity}");
            }
        }

        void ApplyArSync()
        {
            var arLightEstimation = FindFirstObjectByType<ARLightEstimationController>();
            if (arLightEstimation != null)
            {
                arLightEstimation.enabled = isArSyncEnabled;

                if (isArSyncEnabled)
                {
                    // AR同期が有効の場合、手動設定を無効化するヒントを表示
                    Debug.Log($"[LightingPanel] AR Light Sync enabled - using AR Foundation light estimation");
                }
                else
                {
                    // AR同期が無効の場合、手動設定を適用
                    Debug.Log($"[LightingPanel] AR Light Sync disabled - using manual settings");
                    ApplyLighting();
                    ApplyLightDirection();
                }
            }
            else if (isArSyncEnabled)
            {
                // AR同期を有効にしようとした場合のみ警告を表示
                Debug.LogWarning("[LightingPanel] ARLightEstimationController not found - AR Sync unavailable");
                OnWarning?.Invoke("W121", "ARLightEstimationControllerが見つかりません。AR同期は利用できません。");

                // AR同期をOFFに戻す
                isArSyncEnabled = false;
                if (arSyncToggle != null) arSyncToggle.SetValueWithoutNotify(false);
            }
            // AR同期を無効にする場合は警告不要（手動設定を使用）
        }

        /// <summary>
        /// ライティングパネルを表示
        /// </summary>
        public void ShowLighting()
        {
            HideAll();
            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.AddToClassList("visible");
            }
            if (lightingPanelOverlay != null)
            {
                lightingPanelOverlay.AddToClassList("visible");
                TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
                Debug.Log("[LightingPanel] ShowLighting");

                // 現在の設定を適用
                ApplyLighting();
            }
        }

        /// <summary>
        /// シャドウパネルを表示
        /// </summary>
        public void ShowShadow()
        {
            HideAll();
            if (settingsPanelBackdrop != null)
            {
                settingsPanelBackdrop.AddToClassList("visible");
            }
            if (shadowPanelOverlay != null)
            {
                shadowPanelOverlay.AddToClassList("visible");
                TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
                Debug.Log("[LightingPanel] ShowShadow");

                // 現在の設定を適用
                ApplyShadow();
            }
        }

        /// <summary>
        /// 全パネルを非表示
        /// </summary>
        public void HideAll()
        {
            if (settingsPanelBackdrop != null)
            {
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
            Debug.Log("[LightingPanel] HideAll");
        }

        /// <summary>
        /// 互換性のためのエイリアス
        /// </summary>
        public void Show() => ShowLighting();
        public void Hide() => HideAll();

        /// <summary>
        /// 表示/非表示をトグル
        /// </summary>
        public void Toggle()
        {
            if (IsVisible)
            {
                HideAll();
            }
            else
            {
                ShowLighting();
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
