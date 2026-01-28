namespace AICam.Core
{
    /// <summary>
    /// ライティング設定の再適用機能を抽象化するインターフェース。
    /// RuntimeFBXLoaderBridge（AICam.FBXLoader）からCameraCaptureController（AICam.UI）への
    /// 直接参照による循環依存を解消するために使用。
    /// </summary>
    public interface ILightingSettingsProvider
    {
        /// <summary>
        /// ライティング・シャドウ設定を新しいマテリアルに再適用する
        /// </summary>
        void ReapplyLightingSettings();
    }
}
