using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.ARFoundation;

namespace ARCam
{
    /// <summary>
    /// ARカメラの色温度とホワイトバランスを調整
    /// URPのVolumeシステムを使用して外光に合わせた色補正を行います
    /// </summary>
    [RequireComponent(typeof(ARCameraManager))]
    public class ARCameraColorCorrection : MonoBehaviour
    {
        [Header("Color Correction Settings")]
        [Tooltip("色温度調整（-100〜100）。正の値で暖色、負の値で寒色")]
        [Range(-100f, 100f)]
        public float colorTemperature = 0f;

        [Tooltip("ティント調整（-100〜100）。正の値で緑寄り、負の値でマゼンタ寄り")]
        [Range(-100f, 100f)]
        public float tint = 0f;

        [Header("Exposure Settings")]
        [Tooltip("露出補正（-5〜5）。正の値で明るく、負の値で暗く")]
        [Range(-5f, 5f)]
        public float postExposure = 0f;

        [Header("Saturation Settings")]
        [Tooltip("彩度調整（-100〜100）。0が標準、正の値で鮮やか、負の値でモノクロに近づく")]
        [Range(-100f, 100f)]
        public float saturation = 0f;

        [Header("Contrast Settings")]
        [Tooltip("コントラスト調整（-100〜100）。0が標準")]
        [Range(-100f, 100f)]
        public float contrast = 0f;

        [Header("Auto White Balance")]
        [Tooltip("自動ホワイトバランスを有効化（実験的機能）")]
        public bool enableAutoWhiteBalance = false;

        [Tooltip("自動調整の速度（0.1〜5.0）")]
        [Range(0.1f, 5f)]
        public float autoAdjustSpeed = 1f;

        private Volume _globalVolume;
        private ARCameraManager _arCameraManager;

        // Volume Componentへの参照（動的に取得）
        private VolumeProfile _profile;

        void Start()
        {
            _arCameraManager = GetComponent<ARCameraManager>();

            // GlobalなVolumeを探す、なければ作成
            SetupGlobalVolume();
        }

        void Update()
        {
            if (_profile == null)
                return;

            // 自動ホワイトバランス
            if (enableAutoWhiteBalance)
            {
                UpdateAutoWhiteBalance();
            }

            // 色補正を適用
            ApplyColorCorrection();
        }

        /// <summary>
        /// GlobalなVolumeを設定
        /// </summary>
        private void SetupGlobalVolume()
        {
            // シーン内のGlobal Volumeを探す
            Volume[] volumes = FindObjectsByType<Volume>(FindObjectsSortMode.None);
            foreach (var volume in volumes)
            {
                if (volume.isGlobal)
                {
                    _globalVolume = volume;
                    _profile = volume.profile;
                    Debug.Log($"[ARCameraColorCorrection] Found global volume: {volume.name}");
                    return;
                }
            }

            // Volumeが見つからない場合は新規作成
            CreateGlobalVolume();
        }

        /// <summary>
        /// Global Volumeを新規作成
        /// </summary>
        private void CreateGlobalVolume()
        {
            GameObject volumeObject = new GameObject("AR Color Correction Volume");
            _globalVolume = volumeObject.AddComponent<Volume>();
            _globalVolume.isGlobal = true;
            _globalVolume.priority = 1;

            // VolumeProfileを作成
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _globalVolume.profile = _profile;

            Debug.Log("[ARCameraColorCorrection] Created new global volume");
        }

        /// <summary>
        /// 自動ホワイトバランス調整
        /// </summary>
        private void UpdateAutoWhiteBalance()
        {
            // TODO: ARカメラからの色温度情報を取得
            // ARFoundationのカメラテクスチャから平均色を計算し、
            // それに基づいて色温度を自動調整する実装を追加

            // 現在は手動調整のみ実装
        }

        /// <summary>
        /// 色補正をVolumeに適用
        /// </summary>
        private void ApplyColorCorrection()
        {
            if (_profile == null)
                return;

            // WhiteBalanceを適用
            ApplyWhiteBalance();

            // ColorAdjustmentsを適用
            ApplyColorAdjustments();
        }

        /// <summary>
        /// ホワイトバランスを適用
        /// </summary>
        private void ApplyWhiteBalance()
        {
            // WhiteBalance Componentを取得または追加
            if (!_profile.TryGet<UnityEngine.Rendering.Universal.WhiteBalance>(out var whiteBalance))
            {
                whiteBalance = _profile.Add<UnityEngine.Rendering.Universal.WhiteBalance>(true);
            }

            // 値を設定
            whiteBalance.temperature.value = colorTemperature;
            whiteBalance.tint.value = tint;
            whiteBalance.active = true;
        }

        /// <summary>
        /// 色調整を適用
        /// </summary>
        private void ApplyColorAdjustments()
        {
            // ColorAdjustments Componentを取得または追加
            if (!_profile.TryGet<UnityEngine.Rendering.Universal.ColorAdjustments>(out var colorAdjustments))
            {
                colorAdjustments = _profile.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            }

            // 値を設定
            colorAdjustments.postExposure.value = postExposure;
            colorAdjustments.saturation.value = saturation;
            colorAdjustments.contrast.value = contrast;
            colorAdjustments.active = true;
        }

        /// <summary>
        /// 色補正をリセット
        /// </summary>
        public void ResetColorCorrection()
        {
            colorTemperature = 0f;
            tint = 0f;
            postExposure = 0f;
            saturation = 0f;
            contrast = 0f;
        }

        /// <summary>
        /// プリセット: 暖色（屋内/白熱灯）
        /// </summary>
        public void ApplyWarmPreset()
        {
            colorTemperature = 30f;
            tint = 5f;
            postExposure = 0.2f;
            saturation = 10f;
            Debug.Log("[ARCameraColorCorrection] Applied Warm preset");
        }

        /// <summary>
        /// プリセット: 寒色（屋外/曇天）
        /// </summary>
        public void ApplyCoolPreset()
        {
            colorTemperature = -30f;
            tint = -5f;
            postExposure = -0.1f;
            saturation = -5f;
            Debug.Log("[ARCameraColorCorrection] Applied Cool preset");
        }

        /// <summary>
        /// プリセット: ニュートラル（標準）
        /// </summary>
        public void ApplyNeutralPreset()
        {
            ResetColorCorrection();
            Debug.Log("[ARCameraColorCorrection] Applied Neutral preset");
        }

        void OnDestroy()
        {
            // 動的に作成したVolumeProfileをクリーンアップ
            if (_profile != null && _globalVolume != null)
            {
                Destroy(_profile);
            }
        }
    }
}
