using UnityEngine;
using System.IO;
using Cysharp.Threading.Tasks;
using AICam.AvatarCache;
using AICam.FBXLoader;

namespace AICam.UI
{
    /// <summary>
    /// Phase 05: AvatarSlotManager のキャッシュとスロットデータを同期するサービス。
    /// FBX/VRM ロード完了後にスロット情報を永続化し、バイナリキャッシュ作成をトリガーする。
    /// </summary>
    public class AvatarCacheSyncService
    {
        private readonly BinaryCacheService binaryCacheService;
        private readonly RuntimeFBXLoaderBridge fbxLoaderBridge;

        public AvatarCacheSyncService(
            BinaryCacheService binaryCacheService,
            RuntimeFBXLoaderBridge fbxLoaderBridge)
        {
            this.binaryCacheService = binaryCacheService;
            this.fbxLoaderBridge = fbxLoaderBridge;
        }

        /// <summary>
        /// AvatarSlotManager のキャッシュと同期（FBX用）。
        /// Issue #458: エクスポート機能のために必要。
        /// </summary>
        public void SyncSlot(int slotIndex, string filePath, string avatarName, string iconFilePath = null)
        {
            if (slotIndex < 0)
            {
                Debug.LogWarning($"[AvatarCacheSyncService] Cannot sync - invalid slot index: {slotIndex}");
                return;
            }

            var slotManager = AvatarSlotManager.Instance;
            if (slotManager == null || slotManager.Cache == null)
            {
                Debug.LogWarning("[AvatarCacheSyncService] AvatarSlotManager not available for sync");
                return;
            }

            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            if (avatarSlotData == null)
            {
                avatarSlotData = new AvatarSlotData(slotIndex);
            }

            avatarSlotData.modelFilePath = filePath;
            avatarSlotData.avatarName = avatarName;
            avatarSlotData.isValid = true;
            avatarSlotData.fileType = AvatarFileType.FBX;

            if (!string.IsNullOrEmpty(iconFilePath))
            {
                avatarSlotData.iconFilePath = iconFilePath;
            }

            slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
            slotManager.Cache.SetLastActiveSlot(slotIndex);
            slotManager.Cache.SaveToFile();

            Debug.Log($"[AvatarCacheSyncService] Synced slot {slotIndex}: {avatarName}, icon={iconFilePath ?? "none"}, lastActive={slotIndex}");

            // バイナリキャッシュを作成（AvatarMemoryCacheが利用可能な場合）
            var memoryCache = Object.FindFirstObjectByType<AvatarMemoryCache>();
            if (memoryCache != null && fbxLoaderBridge != null && fbxLoaderBridge.CurrentModel != null)
            {
                binaryCacheService?.CreateBinaryCacheAsync(slotIndex, avatarSlotData, memoryCache).Forget();
            }
        }

        /// <summary>
        /// AvatarSlotManager のキャッシュと同期（VRM用）。
        /// Issue #458: エクスポート機能のために必要。
        /// </summary>
        public void SyncSlotForVRM(int slotIndex, string filePath, GameObject avatar, string iconFilePath = null)
        {
            if (slotIndex < 0)
            {
                Debug.LogWarning($"[AvatarCacheSyncService] Cannot sync VRM - invalid slot index: {slotIndex}");
                return;
            }

            var slotManager = AvatarSlotManager.Instance;
            if (slotManager == null || slotManager.Cache == null)
            {
                Debug.LogWarning("[AvatarCacheSyncService] AvatarSlotManager not available for VRM sync");
                return;
            }

            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            if (avatarSlotData == null)
            {
                avatarSlotData = new AvatarSlotData(slotIndex);
            }

            avatarSlotData.modelFilePath = filePath;
            avatarSlotData.avatarName = avatar != null ? avatar.name : Path.GetFileNameWithoutExtension(filePath);
            avatarSlotData.isValid = true;
            avatarSlotData.fileType = AvatarFileType.VRM;

            if (!string.IsNullOrEmpty(iconFilePath))
            {
                avatarSlotData.iconFilePath = iconFilePath;
            }

            slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
            slotManager.Cache.SetLastActiveSlot(slotIndex);
            slotManager.Cache.SaveToFile();

            Debug.Log($"[AvatarCacheSyncService] Synced VRM slot {slotIndex}: {avatarSlotData.avatarName}, icon={iconFilePath ?? "none"}, lastActive={slotIndex}");

            // VRM用バイナリキャッシュを作成
            if (avatar != null)
            {
                binaryCacheService?.CreateBinaryCacheForVRMAsync(slotIndex, avatarSlotData, avatar, iconFilePath).Forget();
            }
        }
    }
}
