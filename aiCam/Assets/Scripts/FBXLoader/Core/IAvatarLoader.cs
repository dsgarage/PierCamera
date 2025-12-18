using UnityEngine;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバターローダーインターフェースのスタブ
    /// </summary>
    public interface IAvatarLoader
    {
        UniTask<AvatarLoadResult> LoadAvatarAsync(string path);
        void ClearAvatar();
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
