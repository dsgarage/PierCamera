using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace AICam.AR
{
    /// <summary>
    /// Issue #74: ARFoundationのLight Estimation機能を制御
    /// ARカメラからの光源情報をシーンのライティングに反映
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class ARLightEstimationController : MonoBehaviour
    {
        [Header("AR Camera")]
        [SerializeField] private ARCameraManager arCameraManager;

        /// <summary>
        /// Issue #443: ライト値が変更された時に発火するイベント
        /// パラメータ: (Color lightColor, float intensity)
        /// </summary>
        public event System.Action<Color, float> OnLightValuesChanged;

        [Header("Light Estimation Settings")]
        [Tooltip("色温度を適用する")]
        [SerializeField] private bool applyColorTemperature = true;

        [Tooltip("メインライトの方向を適用する")]
        [SerializeField] private bool applyMainLightDirection = true;

        [Tooltip("メインライトの強度を適用する")]
        [SerializeField] private bool applyMainLightIntensity = true;

        [Tooltip("環境光（Spherical Harmonics）を適用する")]
        [SerializeField] private bool applyAmbientLight = true;

        [Header("Smoothing")]
        [Tooltip("ライト変化の補間速度")]
        [Range(0.1f, 10f)]
        [SerializeField] private float smoothSpeed = 5f;

        private Light _mainLight;

        // 補間用の現在値
        private Color _currentColor = Color.white;
        private float _currentIntensity = 1f;
        private Quaternion _currentRotation = Quaternion.identity;

        // ターゲット値
        private Color _targetColor = Color.white;
        private float _targetIntensity = 1f;
        private Quaternion _targetRotation = Quaternion.identity;

        void Awake()
        {
            _mainLight = GetComponent<Light>();
            if (_mainLight == null)
            {
                Debug.LogError("[ARLightEstimation] Light component not found!");
                enabled = false;
                return;
            }

            // 初期値を設定
            _currentColor = _mainLight.color;
            _currentIntensity = _mainLight.intensity;
            _currentRotation = transform.rotation;
            _targetColor = _currentColor;
            _targetIntensity = _currentIntensity;
            _targetRotation = _currentRotation;
        }

        void OnEnable()
        {
            if (arCameraManager == null)
            {
                arCameraManager = FindFirstObjectByType<ARCameraManager>();
            }

            if (arCameraManager != null)
            {
                arCameraManager.frameReceived += OnCameraFrameReceived;
                Debug.Log("[ARLightEstimation] Subscribed to frameReceived");
            }
            else
            {
                Debug.LogWarning("[ARLightEstimation] ARCameraManager not found!");
            }
        }

        void OnDisable()
        {
            if (arCameraManager != null)
            {
                arCameraManager.frameReceived -= OnCameraFrameReceived;
                Debug.Log("[ARLightEstimation] Unsubscribed from frameReceived");
            }
        }

        void Update()
        {
            // 値が十分近い場合は補間をスキップ（パフォーマンス最適化）
            if (!NeedsUpdate())
                return;

            // 値を滑らかに補間
            float t = Time.deltaTime * smoothSpeed;
            _currentColor = Color.Lerp(_currentColor, _targetColor, t);
            _currentIntensity = Mathf.Lerp(_currentIntensity, _targetIntensity, t);
            _currentRotation = Quaternion.Slerp(_currentRotation, _targetRotation, t);

            // ライトに適用
            if (_mainLight != null)
            {
                _mainLight.color = _currentColor;
                _mainLight.intensity = _currentIntensity;
                transform.rotation = _currentRotation;

                // Issue #443: ライト値変更イベントを発火
                OnLightValuesChanged?.Invoke(_currentColor, _currentIntensity);
            }
        }

        /// <summary>
        /// 補間が必要かどうかを判定（デルタチェック）
        /// </summary>
        private bool NeedsUpdate()
        {
            const float colorThreshold = 0.001f;
            const float intensityThreshold = 0.001f;
            const float rotationThreshold = 0.001f;

            // 色の差分チェック
            if (Mathf.Abs(_currentColor.r - _targetColor.r) > colorThreshold ||
                Mathf.Abs(_currentColor.g - _targetColor.g) > colorThreshold ||
                Mathf.Abs(_currentColor.b - _targetColor.b) > colorThreshold)
                return true;

            // 強度の差分チェック
            if (Mathf.Abs(_currentIntensity - _targetIntensity) > intensityThreshold)
                return true;

            // 回転の差分チェック
            if (Quaternion.Angle(_currentRotation, _targetRotation) > rotationThreshold)
                return true;

            return false;
        }

        void OnCameraFrameReceived(ARCameraFrameEventArgs args)
        {
            var lightEstimation = args.lightEstimation;

            // 色温度を適用
            if (applyColorTemperature && lightEstimation.averageColorTemperature.HasValue)
            {
                float kelvin = lightEstimation.averageColorTemperature.Value;
                _targetColor = Mathf.CorrelatedColorTemperatureToRGB(kelvin);
                // Debug.Log($"[ARLightEstimation] ColorTemp: {kelvin}K -> {_targetColor}");
            }

            // メインライトの強度を適用
            if (applyMainLightIntensity)
            {
                if (lightEstimation.mainLightIntensityLumens.HasValue)
                {
                    // ルーメンからUnityの強度に変換（簡易的な変換）
                    float lumens = lightEstimation.mainLightIntensityLumens.Value;
                    _targetIntensity = Mathf.Clamp(lumens / 1000f, 0.1f, 3f);
                    // Debug.Log($"[ARLightEstimation] Intensity: {lumens} lumens -> {_targetIntensity}");
                }
                else if (lightEstimation.averageBrightness.HasValue)
                {
                    _targetIntensity = lightEstimation.averageBrightness.Value;
                }
            }

            // メインライトの方向を適用
            if (applyMainLightDirection && lightEstimation.mainLightDirection.HasValue)
            {
                Vector3 direction = lightEstimation.mainLightDirection.Value;
                _targetRotation = Quaternion.LookRotation(direction);
                // Debug.Log($"[ARLightEstimation] Direction: {direction}");
            }

            // 環境光（Spherical Harmonics）を適用
            if (applyAmbientLight && lightEstimation.ambientSphericalHarmonics.HasValue)
            {
                var sh = lightEstimation.ambientSphericalHarmonics.Value;
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
                RenderSettings.ambientProbe = sh;
                // Debug.Log("[ARLightEstimation] Applied Spherical Harmonics");
            }
        }

        /// <summary>
        /// Light Estimationをリセット（デフォルト値に戻す）
        /// </summary>
        public void ResetToDefault()
        {
            _targetColor = Color.white;
            _targetIntensity = 1f;
            _targetRotation = Quaternion.Euler(50f, -30f, 0f); // デフォルトの太陽光角度
        }
    }
}
