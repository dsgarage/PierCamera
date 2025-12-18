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
    }
}
