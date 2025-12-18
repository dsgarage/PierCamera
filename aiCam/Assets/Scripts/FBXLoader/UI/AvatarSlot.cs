using UnityEngine;
using UnityEngine.UIElements;
using System;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバタースロットのスタブクラス
    /// </summary>
    public class AvatarSlot : MonoBehaviour
    {
        public int SlotIndex { get; set; }
        public bool IsLoaded { get; set; }
        public GameObject LoadedAvatar { get; set; }

        public event Action<int> OnSlotSelected;
        public event Action<int> OnSlotCleared;
        public event Action<int> OnSlotClicked;
        public event Action<int> OnSlotLongPressed;

        public void SetAvatar(GameObject avatar)
        {
            LoadedAvatar = avatar;
            IsLoaded = avatar != null;
        }

        public void Clear()
        {
            LoadedAvatar = null;
            IsLoaded = false;
            OnSlotCleared?.Invoke(SlotIndex);
        }

        public void Initialize(int slotIndex)
        {
            SlotIndex = slotIndex;
        }

        public void Initialize(int slotIndex, AvatarSlotData data)
        {
            SlotIndex = slotIndex;
            if (data != null)
            {
                SetSlotData(data);
            }
        }

        public void SetSlotData(AvatarSlotData data)
        {
            // スタブ実装
        }

        public void SetSelected(bool selected)
        {
            // スタブ実装
        }

        public void StartLoading()
        {
            // スタブ実装
        }

        public void SetProgress(float progress)
        {
            // スタブ実装
        }

        public void CompleteLoading()
        {
            // スタブ実装
        }

        public void CancelLoading()
        {
            // スタブ実装
        }
    }
}
