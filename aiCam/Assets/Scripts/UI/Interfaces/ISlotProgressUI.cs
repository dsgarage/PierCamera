using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// スロットロード進捗 UI のインターフェース。
    /// </summary>
    public interface ISlotProgressUI
    {
        void StartSlotLoading(Button slotButton);
        void UpdateSlotProgress(Button slotButton, float progress01);
        void CompleteSlotLoading(Button slotButton);
        void CancelSlotLoading(Button slotButton);
    }
}
