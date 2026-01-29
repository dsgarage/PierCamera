namespace AICam.UI
{
    /// <summary>
    /// アラート通知サービスのインターフェース。
    /// ShowInfo/ShowWarning/ShowError は外部 API として署名を維持する。
    /// </summary>
    public interface IAlertService
    {
        void ShowInfo(string code, string message, float autoDismissSeconds = 3f);
        void ShowWarning(string code, string message, float autoDismissSeconds = 5f);
        void ShowError(string code, string message, float autoDismissSeconds = 0f);
        void HideAlert();

        /// <summary>
        /// アラートバーが表示中かどうか
        /// </summary>
        bool IsAlertVisible { get; }

        /// <summary>
        /// アラートバーのワールド座標バウンド（IsPointOverUIPanel 用）
        /// </summary>
        UnityEngine.Rect AlertWorldBound { get; }
    }
}
