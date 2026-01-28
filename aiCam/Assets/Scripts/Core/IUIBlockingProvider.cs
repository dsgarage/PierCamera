using UnityEngine;

namespace AICam.Core
{
    /// <summary>
    /// UI要素によるタッチブロック判定を抽象化するインターフェース。
    /// PlaceAvatarOnPlaneOnly（Assembly-CSharp）からCameraCaptureController（AICam.UI）への
    /// 型依存を将来的に解消するために使用。
    /// </summary>
    public interface IUIBlockingProvider
    {
        /// <summary>
        /// 指定されたスクリーン座標がUIパネル上にあるかどうかを判定する
        /// </summary>
        /// <param name="screenPosition">スクリーン座標</param>
        /// <returns>UIパネル上にあればtrue</returns>
        bool IsPointOverUIPanel(Vector2 screenPosition);
    }
}
