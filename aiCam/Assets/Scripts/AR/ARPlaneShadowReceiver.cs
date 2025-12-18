using UnityEngine;

namespace AICam.AR
{
    /// <summary>
    /// AR平面でシャドウを受けるためのスタブクラス
    /// </summary>
    public class ARPlaneShadowReceiver : MonoBehaviour
    {
        public bool EnableShadow { get; set; } = true;
        public float ShadowIntensity { get; set; } = 1.0f;

        public void SetShadowEnabled(bool enabled)
        {
            EnableShadow = enabled;
        }

        public void SetShadowIntensity(float intensity)
        {
            ShadowIntensity = intensity;
        }
    }
}
