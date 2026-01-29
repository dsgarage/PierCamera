using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// Phase 05: SlotPersistenceController が CCC の UI 操作にアクセスするためのインターフェース。
    /// CameraCaptureController がこのインターフェースを実装する。
    /// </summary>
    public interface ISlotPersistenceHost
    {
        VisualElement BottomButtonContainer { get; }
        Button BottomButtonAdd { get; }
        int BottomButtonCount { get; set; }

        /// <summary>slotDataMap からスロットデータを取得。存在しない場合は新規作成して返す。</summary>
        SlotData EnsureSlotData(Button button);

        /// <summary>slotDataMap からスロットデータを取得。存在しない場合は null。</summary>
        SlotData GetSlotData(Button button);

        void AddBottomPanelButtonForSlot(int slotIndex);
        int GetSlotIndexFromButton(Button button);
        void PlaceAvatarAheadOfCamera(GameObject avatar);
        void ReapplyLightingSettings();
        void UpdateSlotSelection(Button button);
        void UpdateButtonIcon(Button button, Texture2D texture);
        string SaveThumbnailToFile(Button button, Texture2D texture);
    }
}
