using UnityEngine;

namespace AICam.Core
{
    /// <summary>
    /// AR空間でのアバター配置機能を抽象化するインターフェース。
    /// CameraCaptureController（AICam.UI）からPlaceAvatarOnPlaneOnly（Assembly-CSharp）への
    /// 直接参照を避けるために使用。
    /// </summary>
    public interface IAvatarPlacer
    {
        /// <summary>
        /// 現在配置されているアバターのGameObject（null可）
        /// </summary>
        GameObject PlacedAvatar { get; set; }

        /// <summary>
        /// カメラの前方にアバターを配置する
        /// </summary>
        /// <param name="avatar">配置するアバター</param>
        /// <param name="distance">カメラからの距離（メートル）</param>
        /// <returns>配置に成功したらtrue</returns>
        bool PlaceAvatarAhead(GameObject avatar, float distance);
    }
}
