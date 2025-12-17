using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

/// <summary>
/// デバイスの画面仕様を検出し、UIを自動的に最適化するコンポーネント
/// iPhone 17 Pro Max, iPhone 16 Pro Max等の各デバイスに対応
/// </summary>
public class DeviceScreenAdapter : MonoBehaviour
{
    [System.Serializable]
    public class DeviceProfile
    {
        public string deviceName;
        public Vector2Int logicalResolution;  // ポイント単位
        public Vector2Int physicalResolution; // ピクセル単位
        public int scaleFactor;
        public float safeAreaTop;    // ポイント単位
        public float safeAreaBottom; // ポイント単位
        public float aspectRatio;
    }

    // 既知のデバイスプロファイル
    public static readonly DeviceProfile[] KnownDevices = new DeviceProfile[]
    {
        // iPhone 17 Pro Max
        new DeviceProfile {
            deviceName = "iPhone 17 Pro Max",
            logicalResolution = new Vector2Int(440, 956),
            physicalResolution = new Vector2Int(1320, 2868),
            scaleFactor = 3,
            safeAreaTop = 62,
            safeAreaBottom = 34,
            aspectRatio = 19.5f / 9f
        },
        // iPhone 16 Pro Max
        new DeviceProfile {
            deviceName = "iPhone 16 Pro Max",
            logicalResolution = new Vector2Int(440, 956),
            physicalResolution = new Vector2Int(1320, 2868),
            scaleFactor = 3,
            safeAreaTop = 62,
            safeAreaBottom = 34,
            aspectRatio = 19.5f / 9f
        },
        // iPhone 15 Pro Max
        new DeviceProfile {
            deviceName = "iPhone 15 Pro Max",
            logicalResolution = new Vector2Int(430, 932),
            physicalResolution = new Vector2Int(1290, 2796),
            scaleFactor = 3,
            safeAreaTop = 59,
            safeAreaBottom = 34,
            aspectRatio = 19.5f / 9f
        },
        // iPhone 14 Pro Max
        new DeviceProfile {
            deviceName = "iPhone 14 Pro Max",
            logicalResolution = new Vector2Int(430, 932),
            physicalResolution = new Vector2Int(1290, 2796),
            scaleFactor = 3,
            safeAreaTop = 59,
            safeAreaBottom = 34,
            aspectRatio = 19.5f / 9f
        },
        // iPhone SE (3rd gen)
        new DeviceProfile {
            deviceName = "iPhone SE",
            logicalResolution = new Vector2Int(375, 667),
            physicalResolution = new Vector2Int(750, 1334),
            scaleFactor = 2,
            safeAreaTop = 20,
            safeAreaBottom = 0,
            aspectRatio = 16f / 9f
        },
    };

    [Header("Settings")]
    [SerializeField] private bool autoDetectOnStart = true;
    [SerializeField] private bool applyToCanvasScalers = true;
    [SerializeField] private bool applyToPanelSettings = true;

    [Header("Debug")]
    [SerializeField] private bool showDebugInfo = true;

    // 検出されたデバイス情報
    public static DeviceProfile CurrentDevice { get; private set; }
    public static Vector2 OptimalReferenceResolution { get; private set; }

    private void Awake()
    {
        if (autoDetectOnStart)
        {
            DetectAndApply();
        }
    }

    /// <summary>
    /// デバイスを検出して設定を適用
    /// </summary>
    public void DetectAndApply()
    {
        DetectDevice();
        ApplySettings();
    }

    /// <summary>
    /// 現在のデバイスを検出
    /// </summary>
    public void DetectDevice()
    {
        int screenWidth = Screen.width;
        int screenHeight = Screen.height;
        float dpi = Screen.dpi;
        Rect safeArea = Screen.safeArea;

        // 縦向きに正規化
        if (screenWidth > screenHeight)
        {
            int temp = screenWidth;
            screenWidth = screenHeight;
            screenHeight = temp;
        }

        float aspectRatio = (float)screenHeight / screenWidth;

        if (showDebugInfo)
        {
            Debug.Log($"[DeviceScreenAdapter] Screen: {screenWidth}x{screenHeight}, DPI: {dpi}, AspectRatio: {aspectRatio:F2}");
            Debug.Log($"[DeviceScreenAdapter] SafeArea: {safeArea}");
        }

        // 既知のデバイスと照合
        CurrentDevice = null;
        float bestMatch = float.MaxValue;

        foreach (var device in KnownDevices)
        {
            // 物理解像度で比較
            float widthDiff = Mathf.Abs(device.physicalResolution.x - screenWidth);
            float heightDiff = Mathf.Abs(device.physicalResolution.y - screenHeight);
            float totalDiff = widthDiff + heightDiff;

            if (totalDiff < bestMatch && totalDiff < 100) // 100ピクセル以内なら一致とみなす
            {
                bestMatch = totalDiff;
                CurrentDevice = device;
            }
        }

        // 既知のデバイスに一致しない場合は動的に生成
        if (CurrentDevice == null)
        {
            CurrentDevice = new DeviceProfile
            {
                deviceName = "Unknown Device",
                logicalResolution = new Vector2Int(
                    Mathf.RoundToInt(screenWidth / (dpi > 0 ? dpi / 163f : 2f)),
                    Mathf.RoundToInt(screenHeight / (dpi > 0 ? dpi / 163f : 2f))
                ),
                physicalResolution = new Vector2Int(screenWidth, screenHeight),
                scaleFactor = dpi > 0 ? Mathf.RoundToInt(dpi / 163f) : 2,
                safeAreaTop = screenHeight - safeArea.yMax,
                safeAreaBottom = safeArea.y,
                aspectRatio = aspectRatio
            };
        }

        // 最適なリファレンス解像度を計算（論理解像度の2倍）
        OptimalReferenceResolution = new Vector2(
            CurrentDevice.logicalResolution.x * 2,
            CurrentDevice.logicalResolution.y * 2
        );

        if (showDebugInfo)
        {
            Debug.Log($"[DeviceScreenAdapter] Detected: {CurrentDevice.deviceName}");
            Debug.Log($"[DeviceScreenAdapter] Logical: {CurrentDevice.logicalResolution}");
            Debug.Log($"[DeviceScreenAdapter] Optimal Reference: {OptimalReferenceResolution}");
        }
    }

    /// <summary>
    /// 検出したデバイスに基づいて設定を適用
    /// </summary>
    public void ApplySettings()
    {
        if (CurrentDevice == null)
        {
            Debug.LogWarning("[DeviceScreenAdapter] No device detected, skipping apply");
            return;
        }

        if (applyToCanvasScalers)
        {
            ApplyToAllCanvasScalers();
        }

        if (applyToPanelSettings)
        {
            ApplyToAllPanelSettings();
        }
    }

    /// <summary>
    /// 全てのCanvasScalerに設定を適用
    /// </summary>
    private void ApplyToAllCanvasScalers()
    {
        CanvasScaler[] scalers = FindObjectsByType<CanvasScaler>(FindObjectsSortMode.None);
        int count = 0;

        foreach (var scaler in scalers)
        {
            if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ScaleWithScreenSize)
            {
                scaler.referenceResolution = OptimalReferenceResolution;
                scaler.matchWidthOrHeight = 0.5f;
                count++;

                if (showDebugInfo)
                {
                    Debug.Log($"[DeviceScreenAdapter] Applied to CanvasScaler: {scaler.gameObject.name}");
                }
            }
        }

        Debug.Log($"[DeviceScreenAdapter] Applied settings to {count} CanvasScalers");
    }

    /// <summary>
    /// 全てのPanelSettingsに設定を適用
    /// </summary>
    private void ApplyToAllPanelSettings()
    {
        UIDocument[] uiDocs = FindObjectsByType<UIDocument>(FindObjectsSortMode.None);
        int count = 0;

        foreach (var uiDoc in uiDocs)
        {
            if (uiDoc.panelSettings != null)
            {
                var settings = uiDoc.panelSettings;

                // PanelSettingsはScriptableObjectなので、実行時に変更すると永続化される可能性がある
                // RuntimeだけなのでInstanceを変更しても問題ない
                if (settings.scaleMode == PanelScaleMode.ScaleWithScreenSize)
                {
                    settings.referenceResolution = new Vector2Int(
                        Mathf.RoundToInt(OptimalReferenceResolution.x),
                        Mathf.RoundToInt(OptimalReferenceResolution.y)
                    );
                    settings.match = 0.5f;
                    count++;

                    if (showDebugInfo)
                    {
                        Debug.Log($"[DeviceScreenAdapter] Applied to PanelSettings: {uiDoc.gameObject.name}");
                    }
                }
            }
        }

        Debug.Log($"[DeviceScreenAdapter] Applied settings to {count} PanelSettings");
    }

    /// <summary>
    /// 現在のデバイス情報を取得（静的メソッド）
    /// </summary>
    public static string GetDeviceInfo()
    {
        if (CurrentDevice == null)
        {
            return "Device not detected";
        }

        return $"{CurrentDevice.deviceName}\n" +
               $"Logical: {CurrentDevice.logicalResolution.x}x{CurrentDevice.logicalResolution.y} points\n" +
               $"Physical: {CurrentDevice.physicalResolution.x}x{CurrentDevice.physicalResolution.y} pixels\n" +
               $"Scale: {CurrentDevice.scaleFactor}x\n" +
               $"SafeArea: top={CurrentDevice.safeAreaTop}pt, bottom={CurrentDevice.safeAreaBottom}pt\n" +
               $"Reference: {OptimalReferenceResolution.x}x{OptimalReferenceResolution.y}";
    }

#if UNITY_EDITOR
    [ContextMenu("Simulate iPhone 17 Pro Max")]
    private void SimulateiPhone17ProMax()
    {
        CurrentDevice = KnownDevices[0]; // iPhone 17 Pro Max
        OptimalReferenceResolution = new Vector2(880, 1912);
        ApplySettings();
    }

    [ContextMenu("Simulate iPhone 15 Pro Max")]
    private void SimulateiPhone15ProMax()
    {
        CurrentDevice = KnownDevices[2]; // iPhone 15 Pro Max
        OptimalReferenceResolution = new Vector2(860, 1864);
        ApplySettings();
    }
#endif
}
