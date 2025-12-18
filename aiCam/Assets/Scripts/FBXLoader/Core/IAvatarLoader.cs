using UnityEngine;
using System;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
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

        public static AvatarLoadResult Succeeded(GameObject avatar)
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
        public GameObject Avatar { get; set; }
        public string ErrorMessage { get; set; }
    }
}
