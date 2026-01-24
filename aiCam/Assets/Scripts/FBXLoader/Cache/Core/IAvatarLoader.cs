using UnityEngine;
using System;
using Cysharp.Threading.Tasks;

namespace AICam.AvatarCache
{
    /// <summary>
    /// アバターローダーインターフェース
    /// </summary>
    public interface IAvatarLoader
    {
        UniTask<AvatarLoadResult> LoadAsync(string filePath, Transform parent, Action<float> onProgress = null);
        void ClearCurrentModel();
    }

    /// <summary>
    /// アバターロード結果
    /// </summary>
    public class AvatarLoadResult
    {
        public bool Success { get; set; }
        public GameObject Avatar { get; set; }
        public string ErrorMessage { get; set; }

        public static AvatarLoadResult Succeeded(GameObject avatar, string message = null)
        {
            return new AvatarLoadResult { Success = true, Avatar = avatar };
        }

        public static AvatarLoadResult Failed(string error)
        {
            return new AvatarLoadResult { Success = false, ErrorMessage = error };
        }
    }

    /// <summary>
    /// スロット切り替え結果
    /// </summary>
    public class SlotSwitchResult
    {
        public bool Success { get; set; }
        public int SlotIndex { get; set; }
        public GameObject Avatar { get; set; }
        public string ErrorMessage { get; set; }
        public bool WasCacheHit { get; set; }

        public static SlotSwitchResult Succeeded(int slotIndex, GameObject avatar = null, bool wasCacheHit = false)
        {
            return new SlotSwitchResult
            {
                Success = true,
                SlotIndex = slotIndex,
                Avatar = avatar,
                WasCacheHit = wasCacheHit
            };
        }

        public static SlotSwitchResult Failed(int slotIndex, string error)
        {
            return new SlotSwitchResult
            {
                Success = false,
                SlotIndex = slotIndex,
                ErrorMessage = error
            };
        }
    }
}
