using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.IO;
using Cysharp.Threading.Tasks;
using AICam.AvatarCache;
using AICam.Core;
using AICam.FBXLoader;
using NativeFilePickerNamespace;

namespace AICam.UI
{
    /// <summary>
    /// Phase 06: アバターロード（VRM/FBX）・ファイルピッカー・バイナリキャッシュ復元・スロット切替を管理するコントローラー。
    /// </summary>
    public class AvatarLoadOrchestrator
    {
        // Loading state
        private bool isSlotLoading = false;
        private Button currentLoadingSlot = null;

        public bool IsSlotLoading => isSlotLoading;
        public Button CurrentLoadingSlot => currentLoadingSlot;

        // Dependencies
        private readonly AICam.VRM.RuntimeAvatarLoader avatarLoader;
        private AICam.FBXLoader.RuntimeFBXLoaderBridge fbxLoaderBridge;
        private readonly AvatarCacheSyncService avatarCacheSyncService;
        private readonly ISlotProgressUI slotProgressUI;
        private readonly PoseUIController poseUIController;
        private readonly ExpressionUIController expressionUIController;
        private readonly AvatarSlotUIController slotUIController;
        private readonly Func<IAvatarPlacer> findAvatarPlacer;
        private readonly Action<GameObject> placeAvatarAheadOfCamera;
        private readonly Action reapplyLightingSettings;
        private readonly Action<UnityEngine.Object> destroy;
        private readonly bool enableDebugLogging;

        public AvatarLoadOrchestrator(
            AICam.VRM.RuntimeAvatarLoader avatarLoader,
            AICam.FBXLoader.RuntimeFBXLoaderBridge fbxLoaderBridge,
            AvatarCacheSyncService avatarCacheSyncService,
            ISlotProgressUI slotProgressUI,
            PoseUIController poseUIController,
            ExpressionUIController expressionUIController,
            AvatarSlotUIController slotUIController,
            Func<IAvatarPlacer> findAvatarPlacer,
            Action<GameObject> placeAvatarAheadOfCamera,
            Action reapplyLightingSettings,
            Action<UnityEngine.Object> destroy,
            bool enableDebugLogging)
        {
            this.avatarLoader = avatarLoader;
            this.fbxLoaderBridge = fbxLoaderBridge;
            this.avatarCacheSyncService = avatarCacheSyncService;
            this.slotProgressUI = slotProgressUI;
            this.poseUIController = poseUIController;
            this.expressionUIController = expressionUIController;
            this.slotUIController = slotUIController;
            this.findAvatarPlacer = findAvatarPlacer;
            this.placeAvatarAheadOfCamera = placeAvatarAheadOfCamera;
            this.reapplyLightingSettings = reapplyLightingSettings;
            this.destroy = destroy;
            this.enableDebugLogging = enableDebugLogging;
        }

        /// <summary>
        /// スロットクリック時: 設定済みならアバター切替、空ならファイルピッカー
        /// </summary>
        public void OnSlotClicked(Button button)
        {
            if (isSlotLoading)
            {
                Debug.Log($"🔄 A slot is already loading (current: {currentLoadingSlot?.name}), ignoring click on {button.name}");
                return;
            }

            var slotData = slotUIController.GetSlotData(button);

            if (slotData != null && slotData.IsConfigured)
            {
                Debug.Log($"🔄 Switching to avatar in slot: {button.name}");
                isSlotLoading = true;
                currentLoadingSlot = button;
                SwitchToSlotAvatar(button, slotData);
            }
            else
            {
                Debug.Log($"📂 Empty slot, opening file picker: {button.name}");
                OpenFilePicker(button);
            }
        }

        /// <summary>
        /// ファイルピッカーを開く（複数形式対応）
        /// </summary>
        public async void OpenFilePicker(Button targetButton)
        {
            Debug.Log($"📂 Opening file picker for button: {targetButton.name}");

            try
            {
#if UNITY_EDITOR
                Debug.Log($"💻 Opening Unity Editor file panel for VRM");

                string path = UnityEditor.EditorUtility.OpenFilePanel("Select VRM File", "", "vrm");

                if (string.IsNullOrEmpty(path))
                {
                    Debug.Log("❌ File picker cancelled");
                    return;
                }

                Debug.Log($"✅ File selected: {path}");
                TapticEngine.Impact(TapticEngine.ImpactStyle.Light);

                await LoadFileAsync(path, targetButton);
#elif UNITY_IOS || UNITY_ANDROID
                Debug.Log($"📱 Opening NativeFilePicker...");

                var tcs = new System.Threading.Tasks.TaskCompletionSource<string>();

                string[] allowedFileTypes;

#if UNITY_IOS
                allowedFileTypes = new string[] { "public.data", "public.content", "public.item" };
                Debug.Log("[FilePicker] iOS: Using UTI types for file picker");
#elif UNITY_ANDROID
                allowedFileTypes = new string[] { "*/*" };
                Debug.Log("[FilePicker] Android: Using MIME type for file picker");
#endif

                Debug.Log($"🔍 Calling NativeFilePicker.PickFile...");

                NativeFilePicker.PickFile((path) =>
                {
                    Debug.Log($"[FilePicker] File picker callback: {path}");
                    tcs.SetResult(path);
                }, allowedFileTypes);

                Debug.Log("[FilePicker] Waiting for file selection...");
                string selectedPath = await tcs.Task;

                if (string.IsNullOrEmpty(selectedPath))
                {
                    Debug.Log("❌ File selection cancelled");
                    return;
                }

                Debug.Log($"✅ File selected: {selectedPath}");

                if (!selectedPath.ToLower().EndsWith(".vrm"))
                {
                    Debug.LogWarning($"⚠️ Selected file may not be a VRM file: {selectedPath}");
                    Debug.LogWarning("[FilePicker] Attempting to load anyway...");
                }

                TapticEngine.Impact(TapticEngine.ImpactStyle.Light);

                await LoadFileAsync(selectedPath, targetButton);
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error opening file picker: {e.Message}");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// 拡張子に基づいてファイルをロード
        /// </summary>
        public async UniTask LoadFileAsync(string filePath, Button targetButton)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("❌ File path is null or empty");
                return;
            }

            if (!File.Exists(filePath))
            {
                Debug.LogError($"❌ File not found: {filePath}");
                return;
            }

            string extension = Path.GetExtension(filePath).ToLower();
            Debug.Log($"📄 File extension: {extension}");

            try
            {
                switch (extension)
                {
                    case ".vrm":
                    case ".glb":
                        await LoadVRMFileAsync(filePath, targetButton);
                        break;

                    case ".fbx":
                        await LoadFBXFileAsync(filePath, targetButton);
                        break;

                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                    case ".gif":
                        Debug.LogWarning("⚠️ Image format is not yet supported");
                        break;

                    default:
                        Debug.LogError($"❌ Unsupported file format: {extension}");
                        break;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Failed to load file: {e.Message}");
                Debug.LogException(e);
            }
        }

        /// <summary>
        /// VRMファイルをロード
        /// </summary>
        public async UniTask LoadVRMFileAsync(string filePath, Button targetButton)
        {
            if (avatarLoader == null)
            {
                Debug.LogError("❌ RuntimeAvatarLoader is not assigned!");
                return;
            }

            Debug.Log($"🎭 Loading VRM file: {filePath}");

            slotProgressUI?.StartSlotLoading(targetButton);
            slotProgressUI?.UpdateSlotProgress(targetButton, 0.1f);

            try
            {
                Debug.Log("🗑️ Clearing existing avatar before loading new VRM...");
                avatarLoader.ClearCurrentAvatar();

                var placer = findAvatarPlacer?.Invoke();
                if (placer != null)
                {
                    var existingAvatar = placer.PlacedAvatar;
                    if (existingAvatar != null)
                    {
                        Debug.Log($"🗑️ Destroying existing avatar in IAvatarPlacer: {existingAvatar.name}");
                        destroy?.Invoke(existingAvatar);
                        placer.PlacedAvatar = null;
                    }
                }

                slotProgressUI?.UpdateSlotProgress(targetButton, 0.3f);

                var avatar = await avatarLoader.LoadVRMFromPathAsync(filePath);

                if (avatar == null)
                {
                    Debug.LogError("❌ Failed to load VRM avatar");
                    slotProgressUI?.CancelSlotLoading(targetButton);
                    return;
                }

                Debug.Log($"✅ VRM avatar loaded successfully: {avatar.name}");

                poseUIController?.ApplyDefaultAOC(avatar);

                int slotIndex = slotUIController.GetSlotIndexFromButton(targetButton);
                expressionUIController?.SetupExpressionSystem(avatar, slotIndex);
                expressionUIController?.TriggerExpressionIconGeneration(avatar, slotIndex);

                placeAvatarAheadOfCamera?.Invoke(avatar);
                reapplyLightingSettings?.Invoke();

                slotProgressUI?.UpdateSlotProgress(targetButton, 0.7f);

                await UniTask.DelayFrame(3);

                slotProgressUI?.UpdateSlotProgress(targetButton, 0.85f);

                Debug.Log($"🖼 Starting thumbnail capture for: {avatar.name}");
                var thumbnail = await AvatarIconCapture.Instance.CaptureAsTextureAsync(avatar);
                Debug.Log($"🖼 Thumbnail capture result: {(thumbnail != null ? $"{thumbnail.width}x{thumbnail.height}" : "NULL")}");

                slotProgressUI?.CompleteSlotLoading(targetButton);

                var slotData = slotUIController.EnsureSlotData(targetButton);
                slotData.filePath = filePath;
                slotData.fileType = SlotFileType.VRM;
                slotData.thumbnail = thumbnail;
                slotData.loadedAvatar = avatar;

                Debug.Log($"💾 Slot data saved for {targetButton.name}: {filePath} (VRM)");

                string iconPath = slotUIController.SaveThumbnailToFile(targetButton, thumbnail);

                avatarCacheSyncService?.SyncSlotForVRM(slotIndex, filePath, avatar, iconPath);

                if (thumbnail != null)
                {
                    slotUIController.UpdateButtonIcon(targetButton, thumbnail);
                    Debug.Log($"🖼 Thumbnail generated and applied to button: {targetButton.name}");
                }

                slotUIController.UpdateSlotSelection(targetButton);

                TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error loading VRM: {e.Message}");
                Debug.LogException(e);
                slotProgressUI?.CancelSlotLoading(targetButton);
            }
        }

        /// <summary>
        /// FBXファイルをロード
        /// </summary>
        public async UniTask LoadFBXFileAsync(string filePath, Button targetButton)
        {
            if (fbxLoaderBridge == null)
            {
                fbxLoaderBridge = UnityEngine.Object.FindFirstObjectByType<RuntimeFBXLoaderBridge>();

                if (fbxLoaderBridge == null)
                {
                    Debug.LogError("❌ RuntimeFBXLoaderBridge is not found!");
                    return;
                }
            }

            Debug.Log($"📦 Loading FBX file: {filePath}");

            slotProgressUI?.StartSlotLoading(targetButton);
            slotProgressUI?.UpdateSlotProgress(targetButton, 0.1f);

            bool loadSuccess = false;
            var tcs = new UniTaskCompletionSource();

            try
            {
                fbxLoaderBridge.StartRuntimeLoadFromPath(
                    filePath,
                    -1,
                    null,
                    progress =>
                    {
                        Debug.Log($"📦 FBX load progress: {progress}%");
                        slotProgressUI?.UpdateSlotProgress(targetButton, 0.1f + (progress / 100f) * 0.8f);
                    },
                    success =>
                    {
                        loadSuccess = success;
                        tcs.TrySetResult();
                    }
                );

                await tcs.Task;

                if (!loadSuccess)
                {
                    Debug.LogError("❌ Failed to load FBX");
                    slotProgressUI?.CancelSlotLoading(targetButton);
                    return;
                }

                var loadedModel = fbxLoaderBridge.CurrentModel;
                if (loadedModel == null)
                {
                    Debug.LogError("❌ FBX model is null after loading");
                    slotProgressUI?.CancelSlotLoading(targetButton);
                    return;
                }

                Debug.Log($"✅ FBX loaded successfully: {loadedModel.name}");

                poseUIController?.ApplyDefaultAOC(loadedModel);

                int slotIndex = slotUIController.GetSlotIndexFromButton(targetButton);
                expressionUIController?.SetupExpressionSystem(loadedModel, slotIndex);
                expressionUIController?.TriggerExpressionIconGeneration(loadedModel, slotIndex);

                placeAvatarAheadOfCamera?.Invoke(loadedModel);
                reapplyLightingSettings?.Invoke();

                slotProgressUI?.UpdateSlotProgress(targetButton, 0.9f);

                await UniTask.DelayFrame(3);

                Debug.Log($"🖼 Starting thumbnail capture for: {loadedModel.name}");
                Texture2D thumbnail = await AvatarIconCapture.Instance.CaptureAsTextureAsync(loadedModel);
                Debug.Log($"🖼 Thumbnail capture result: {(thumbnail != null ? $"{thumbnail.width}x{thumbnail.height}" : "NULL")}");

                slotProgressUI?.CompleteSlotLoading(targetButton);

                var slotData = slotUIController.EnsureSlotData(targetButton);
                slotData.filePath = filePath;
                slotData.fileType = SlotFileType.FBX;
                slotData.thumbnail = thumbnail;
                slotData.loadedAvatar = loadedModel;

                Debug.Log($"💾 Slot data saved for {targetButton.name}: {filePath} (FBX)");

                string iconPath = slotUIController.SaveThumbnailToFile(targetButton, thumbnail);

                avatarCacheSyncService?.SyncSlot(slotIndex, filePath, loadedModel.name, iconPath);

                if (thumbnail != null)
                {
                    slotUIController.UpdateButtonIcon(targetButton, thumbnail);
                    Debug.Log($"🖼 Thumbnail generated and applied to button: {targetButton.name}");
                }

                slotUIController.UpdateSlotSelection(targetButton);

                TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
            }
            catch (Exception e)
            {
                Debug.LogError($"❌ Error loading FBX: {e.Message}");
                Debug.LogException(e);
                slotProgressUI?.CancelSlotLoading(targetButton);
            }
        }

        /// <summary>
        /// スロットのアバターに切り替え
        /// </summary>
        public async void SwitchToSlotAvatar(Button button, SlotData slotData)
        {
            if (slotData == null || !slotData.IsConfigured)
            {
                isSlotLoading = false;
                currentLoadingSlot = null;
                return;
            }

            if (slotUIController.IsCurrentSelectedSlot(button))
            {
                Debug.Log($"🔄 Already selected slot: {button.name}");
                isSlotLoading = false;
                currentLoadingSlot = null;
                return;
            }

            slotUIController.UpdateSlotSelection(button);

            if (slotData.loadedAvatar == null)
            {
                Debug.Log($"🔄 Avatar not loaded, loading from: {slotData.filePath}");

                try
                {
                    bool loadedFromCache = await TryLoadFromBinaryCacheAsync(button, slotData);

                    if (!loadedFromCache)
                    {
                        Debug.Log($"🔄 Binary cache not available, loading from original file: {slotData.filePath}");

                        if (slotData.fileType == SlotFileType.VRM)
                        {
                            await LoadVRMFileAsync(slotData.filePath, button);
                        }
                        else if (slotData.fileType == SlotFileType.FBX)
                        {
                            await LoadFBXFileAsync(slotData.filePath, button);
                        }
                    }
                }
                finally
                {
                    isSlotLoading = false;
                    currentLoadingSlot = null;
                }
            }
            else
            {
                Debug.Log($"🔄 Activating avatar: {slotData.loadedAvatar.name}");
                slotUIController.ActivateSlotAvatar(slotData);

                isSlotLoading = false;
                currentLoadingSlot = null;
            }

            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        /// <summary>
        /// バイナリキャッシュからアバターを復元する
        /// </summary>
        public async UniTask<bool> TryLoadFromBinaryCacheAsync(Button button, SlotData slotData)
        {
            int slotIndex = slotUIController.GetSlotIndexFromButton(button);
            if (slotIndex < 0)
            {
                Debug.Log($"[BinaryCache] Invalid slot index for {button.name}");
                return false;
            }

            var slotManager = AvatarSlotManager.Instance;
            if (slotManager?.Cache == null)
            {
                Debug.Log("[BinaryCache] AvatarSlotManager not available");
                return false;
            }

            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            if (avatarSlotData == null || string.IsNullOrEmpty(avatarSlotData.binaryCacheId))
            {
                Debug.Log($"[BinaryCache] No binaryCacheId for slot {slotIndex}");
                return false;
            }

            string cacheId = avatarSlotData.binaryCacheId;
            Debug.Log($"🚀 Attempting to load from binary cache: {cacheId}");

            slotProgressUI?.StartSlotLoading(button);
            slotProgressUI?.UpdateSlotProgress(button, 0.1f);

            try
            {
                var cacheIntegrator = new AvatarCacheIntegrator(Application.persistentDataPath);

                if (!cacheIntegrator.HasBinaryCache(cacheId))
                {
                    Debug.Log($"[BinaryCache] Cache not found: {cacheId}");
                    slotProgressUI?.CancelSlotLoading(button);
                    return false;
                }

                slotProgressUI?.UpdateSlotProgress(button, 0.3f);

                var avatar = await cacheIntegrator.LoadFromBinaryCacheAsync(cacheId, progress =>
                {
                    slotProgressUI?.UpdateSlotProgress(button, 0.3f + (progress / 100f) * 0.4f);
                }, slotIndex);

                if (avatar == null)
                {
                    Debug.LogWarning($"[BinaryCache] Failed to load from cache: {cacheId}");
                    slotProgressUI?.CancelSlotLoading(button);
                    return false;
                }

                Debug.Log($"✅ Avatar loaded from binary cache: {avatar.name}");

                slotProgressUI?.UpdateSlotProgress(button, 0.7f);

                if (avatarLoader != null)
                {
                    avatarLoader.ClearCurrentAvatar();
                }

                var placer = findAvatarPlacer?.Invoke();
                if (placer != null)
                {
                    var existingAvatar = placer.PlacedAvatar;
                    if (existingAvatar != null)
                    {
                        Debug.Log($"🗑️ Destroying existing avatar in IAvatarPlacer: {existingAvatar.name}");
                        destroy?.Invoke(existingAvatar);
                        placer.PlacedAvatar = null;
                    }
                }

                poseUIController?.ApplyDefaultAOC(avatar);

                expressionUIController?.SetupExpressionSystem(avatar, slotIndex);
                expressionUIController?.TriggerExpressionIconGeneration(avatar, slotIndex);

                placeAvatarAheadOfCamera?.Invoke(avatar);
                reapplyLightingSettings?.Invoke();

                slotProgressUI?.UpdateSlotProgress(button, 0.85f);

                await UniTask.DelayFrame(3);

                slotProgressUI?.CompleteSlotLoading(button);

                slotData.loadedAvatar = avatar;
                Debug.Log($"💾 Slot data updated from binary cache for {button.name}");

                string iconPath = AvatarSlotCache.GetIconPath(slotIndex);
                bool iconRestored = false;

                if (File.Exists(iconPath))
                {
                    try
                    {
                        byte[] iconData = File.ReadAllBytes(iconPath);
                        var texture = new Texture2D(2, 2);
                        if (texture.LoadImage(iconData))
                        {
                            slotData.thumbnail = texture;
                            slotUIController.UpdateButtonIcon(button, texture);
                            iconRestored = true;
                            Debug.Log($"[BinaryCache] Icon restored from cache for slot {slotIndex}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[BinaryCache] Failed to load restored icon: {ex.Message}");
                    }
                }

                if (!iconRestored && avatar != null)
                {
                    await UniTask.DelayFrame(3);
                    var thumbnail = await AvatarIconCapture.Instance.CaptureAsTextureAsync(avatar);
                    if (thumbnail != null)
                    {
                        slotData.thumbnail = thumbnail;
                        slotUIController.UpdateButtonIcon(button, thumbnail);
                        iconPath = slotUIController.SaveThumbnailToFile(button, thumbnail);
                        Debug.Log($"[BinaryCache] New icon captured for slot {slotIndex}");
                    }
                }

                if (File.Exists(iconPath) && avatarSlotData.iconFilePath != iconPath)
                {
                    avatarSlotData.iconFilePath = iconPath;
                    slotManager.Cache.UpdateSlot(slotIndex, avatarSlotData);
                    slotManager.Cache.SaveToFile();
                }

                slotUIController.UpdateSlotSelection(button);

                poseUIController?.SetCachedAvatar(avatar);

                TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[BinaryCache] Error loading from cache: {e.Message}");
                slotProgressUI?.CancelSlotLoading(button);
                return false;
            }
        }
    }
}
