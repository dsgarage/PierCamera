using System;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバターロード結果
    /// </summary>
    public class AvatarLoadResult
    {
        public bool Success { get; set; }
        public GameObject Avatar { get; set; }
        public string ErrorMessage { get; set; }
        public string VrmVersion { get; set; }

        public static AvatarLoadResult Succeeded(GameObject avatar, string vrmVersion = null)
        {
            return new AvatarLoadResult
            {
                Success = true,
                Avatar = avatar,
                VrmVersion = vrmVersion
            };
        }

        public static AvatarLoadResult Failed(string errorMessage)
        {
            return new AvatarLoadResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// スロット切り替え結果
    /// </summary>
    public class SlotSwitchResult
    {
        public bool Success { get; set; }
        public bool WasCacheHit { get; set; }
        public GameObject Avatar { get; set; }
        public int SlotIndex { get; set; }
        public string ErrorMessage { get; set; }

        public static SlotSwitchResult Succeeded(int slotIndex, GameObject avatar, bool wasCacheHit)
        {
            return new SlotSwitchResult
            {
                Success = true,
                SlotIndex = slotIndex,
                Avatar = avatar,
                WasCacheHit = wasCacheHit
            };
        }

        public static SlotSwitchResult Failed(int slotIndex, string errorMessage)
        {
            return new SlotSwitchResult
            {
                Success = false,
                SlotIndex = slotIndex,
                ErrorMessage = errorMessage
            };
        }
    }

    /// <summary>
    /// アバターローダーインターフェース
    /// VRM/FBX等の読み込みを抽象化し、キャッシュシステムから分離
    /// </summary>
    public interface IAvatarLoader
    {
        /// <summary>
        /// ファイルからアバターを非同期ロード
        /// </summary>
        /// <param name="filePath">VRM/FBXファイルのパス</param>
        /// <param name="parent">親Transform（配置用）</param>
        /// <param name="onProgress">進捗コールバック（0-100）</param>
        /// <returns>ロード結果</returns>
        UniTask<AvatarLoadResult> LoadAsync(
            string filePath,
            Transform parent,
            Action<float> onProgress = null
        );

        /// <summary>
        /// アバターを破棄
        /// VRM等のリソースを適切に解放
        /// </summary>
        /// <param name="avatar">破棄するアバター</param>
        void DisposeAvatar(GameObject avatar);

        /// <summary>
        /// サポートするファイル拡張子
        /// </summary>
        string[] SupportedExtensions { get; }

        /// <summary>
        /// 指定ファイルをロード可能か
        /// </summary>
        bool CanLoad(string filePath);
    }
}
