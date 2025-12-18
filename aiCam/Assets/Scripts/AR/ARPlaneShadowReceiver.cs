using UnityEngine;

namespace AICam.AR
{
    /// <summary>
    /// AR平面でシャドウを受けるためのスタブクラス
    /// </summary>
    public class ARPlaneShadowReceiver : MonoBehaviour
    {
        public bool EnableShadow { get; set; } = true;

        public void SetShadowEnabled(bool enabled)
        {
            EnableShadow = enabled;
        }
    }
}
