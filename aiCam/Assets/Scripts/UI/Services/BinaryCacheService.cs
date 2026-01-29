using UnityEngine;
using System;
using Cysharp.Threading.Tasks;
using AICam.AvatarCache;
using AICam.FBXLoader;
#if BLENDSHAPE_CONTROLLER
using DSGarage.BlendShape;
#endif

namespace AICam.UI
{
    /// <summary>
    /// Phase 05: バイナリキャッシュの作成・管理を担当するサービス。
    /// AvatarCacheIntegrator を使用してアバターのバイナリキャッシュを作成し、
    /// AvatarSlotManager のキャッシュを更新する。
    /// </summary>
    public class BinaryCacheService
    {
        private readonly ExpressionUIController expressionUIController;
        private readonly RuntimeFBXLoaderBridge fbxLoaderBridge;
        private readonly Action<string, string, float> showInfo;
        private readonly Action<string, string, float> showWarning;
        private readonly Action<string, string> showError;
        private readonly Action<int, AvatarSlotData> showExportPopup;

        public BinaryCacheService(
            ExpressionUIController expressionUIController,
            RuntimeFBXLoaderBridge fbxLoaderBridge,
            Action<string, string, float> showInfo,
            Action<string, string, float> showWarning,
            Action<string, string> showError,
            Action<int, AvatarSlotData> showExportPopup)
        {
            this.expressionUIController = expressionUIController;
            this.fbxLoaderBridge = fbxLoaderBridge;
            this.showInfo = showInfo;
            this.showWarning = showWarning;
            this.showError = showError;
            this.showExportPopup = showExportPopup;
        }

        /// <summary>
        /// バイナリキャッシュを作成してからエクスポートポップアップを表示。
        /// ダブルタップ時にキャッシュが未作成の場合に使用。
        /// </summary>
        public async void CreateBinaryCacheAndShowPopupAsync(int slotIndex, AvatarSlotData avatarSlotData, GameObject avatar)
        {
            try
            {
                showInfo?.Invoke("Cache", "キャッシュを作成中...", 3f);

                var cacheIntegrator = new AvatarCacheIntegrator(Application.persistentDataPath);
                string cacheId = await cacheIntegrator.CreateBinaryCacheAsync(avatar, avatarSlotData.modelFilePath);

                if (!string.IsNullOrEmpty(cacheId))
                {
                    avatarSlotData.binaryCacheId = cacheId;

                    var slotManager = AvatarSlotManager.Instance;
                    if (slotManager?.Cache != null)
                    {
                        slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
                        slotManager.Cache.SaveToFile();
                    }

                    Debug.Log($"[BinaryCacheService] Binary cache created: {cacheId}");

#if BLENDSHAPE_CONTROLLER
                    expressionUIController?.SaveExpressionDataToCache(avatar, cacheId);
#endif

                    showExportPopup?.Invoke(slotIndex, avatarSlotData);
                }
                else
                {
                    Debug.LogWarning($"[BinaryCacheService] Failed to create binary cache for slot {slotIndex}");
                    showWarning?.Invoke("CACHE_ERROR", "キャッシュの作成に失敗しました", 5f);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BinaryCacheService] Error creating binary cache: {e.Message}");
                showError?.Invoke("CACHE_ERROR", $"キャッシュエラー: {e.Message}");
            }
        }

        /// <summary>
        /// VRM用バイナリキャッシュを非同期で作成。
        /// VRMロード完了後にAvatarSlotManagerと同期する際に呼ばれる。
        /// </summary>
        public async UniTaskVoid CreateBinaryCacheForVRMAsync(int slotIndex, AvatarSlotData avatarSlotData, GameObject avatar, string iconSourcePath = null)
        {
            try
            {
                var cacheIntegrator = new AvatarCacheIntegrator(Application.persistentDataPath);
                string cacheId = await cacheIntegrator.CreateBinaryCacheAsync(avatar, avatarSlotData.modelFilePath, iconSourcePath);

                if (!string.IsNullOrEmpty(cacheId))
                {
                    avatarSlotData.binaryCacheId = cacheId;
                    var slotManager = AvatarSlotManager.Instance;
                    if (slotManager?.Cache != null)
                    {
                        slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
                        slotManager.Cache.SaveToFile();
                    }
                    Debug.Log($"[BinaryCacheService] VRM binary cache created for slot {slotIndex}: {cacheId}");

#if BLENDSHAPE_CONTROLLER
                    expressionUIController?.SaveExpressionDataToCache(avatar, cacheId);
#endif
                }
                else
                {
                    Debug.LogWarning($"[BinaryCacheService] Failed to create VRM binary cache for slot {slotIndex}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BinaryCacheService] Error creating VRM binary cache: {e.Message}");
            }
        }

        /// <summary>
        /// FBX用バイナリキャッシュを非同期で作成。
        /// FBXロード完了後にAvatarSlotManagerと同期する際に呼ばれる。
        /// </summary>
        public async UniTaskVoid CreateBinaryCacheAsync(int slotIndex, AvatarSlotData avatarSlotData, AvatarMemoryCache memoryCache)
        {
            try
            {
                GameObject currentModel = fbxLoaderBridge?.CurrentModel;
                if (currentModel == null)
                {
                    Debug.LogWarning($"[BinaryCacheService] No current model available for binary cache");
                    return;
                }

                var cacheIntegrator = new AvatarCacheIntegrator(Application.persistentDataPath);
                string cacheId = await cacheIntegrator.CreateBinaryCacheAsync(currentModel, avatarSlotData.modelFilePath);

                if (!string.IsNullOrEmpty(cacheId))
                {
                    avatarSlotData.binaryCacheId = cacheId;
                    var slotManager = AvatarSlotManager.Instance;
                    if (slotManager?.Cache != null)
                    {
                        slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
                        slotManager.Cache.SaveToFile();
                    }
                    Debug.Log($"[BinaryCacheService] Binary cache created for slot {slotIndex}: {cacheId}");

#if BLENDSHAPE_CONTROLLER
                    expressionUIController?.SaveExpressionDataToCache(currentModel, cacheId);
#endif
                }
                else
                {
                    Debug.LogWarning($"[BinaryCacheService] Failed to create binary cache for slot {slotIndex}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[BinaryCacheService] Error creating binary cache: {e.Message}");
            }
        }
    }
}
