using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using AICam.AvatarCache;
using AICam.FBXLoader;

namespace AICam.UI
{
    /// <summary>
    /// Phase 05: スロットの永続化・復元を担当するコントローラー。
    /// 起動時のスロットデータ読み込み、バイナリキャッシュからのアバター自動ロード、
    /// スロット数の保存を行う。UIを持たず、ISlotPersistenceHost 経由でUI操作を委譲する。
    ///
    /// ## v0.8.0 修正履歴
    /// - Issue #471: アクティブスロットのみ SetupExpressionSystem を呼び出すように変更
    /// - 非アクティブスロットが表情システムを上書きする問題を修正
    /// - AutoLoadSlotFromCacheAsync に isActiveSlot パラメータを追加
    /// </summary>
    public class SlotPersistenceController
    {
        private readonly ISlotPersistenceHost host;
        private readonly ISlotProgressUI slotProgressUI;
        private readonly PoseUIController poseUIController;
        private readonly ExpressionUIController expressionUIController;
        private readonly bool enableDebugLogging;

        public SlotPersistenceController(
            ISlotPersistenceHost host,
            ISlotProgressUI slotProgressUI,
            PoseUIController poseUIController,
            ExpressionUIController expressionUIController,
            bool enableDebugLogging)
        {
            this.host = host;
            this.slotProgressUI = slotProgressUI;
            this.poseUIController = poseUIController;
            this.expressionUIController = expressionUIController;
            this.enableDebugLogging = enableDebugLogging;
        }

        /// <summary>
        /// Issue #462: 現在のスロットボタン数をキャッシュに保存。
        /// </summary>
        public void SaveSlotCount(int count)
        {
            var slotManager = AvatarSlotManager.Instance;
            if (slotManager?.Cache != null)
            {
                slotManager.Cache.lastCreatedSlotCount = count;
                slotManager.Cache.SaveToFile();
                Debug.Log($"[📦 PERSIST] Saved lastCreatedSlotCount={count}");
            }
        }

        /// <summary>
        /// Issue #416: 永続化されたスロットデータを読み込み（非同期版）。
        /// AvatarSlotManager の初期化完了を待ってからアイコンを読み込み、
        /// バイナリキャッシュからアバターを自動ロードする。
        /// </summary>
        public async UniTaskVoid LoadPersistedSlotDataAsync()
        {
            Debug.Log("[📦 PERSIST] LoadPersistedSlotDataAsync called - waiting for AvatarSlotManager...");

            // AvatarSlotManagerの初期化完了を待機（最大3秒）
            var slotManager = AvatarSlotManager.Instance;
            int maxWait = 30;
            while ((slotManager == null || !slotManager.IsInitialized) && maxWait > 0)
            {
                await UniTask.Delay(100);
                slotManager = AvatarSlotManager.Instance;
                maxWait--;
            }

            if (slotManager == null)
            {
                Debug.LogWarning("[📦 PERSIST] AvatarSlotManager.Instance is null after waiting");
                return;
            }
            if (!slotManager.IsInitialized)
            {
                Debug.LogWarning("[📦 PERSIST] AvatarSlotManager not initialized after waiting");
                return;
            }
            if (slotManager.Cache == null)
            {
                Debug.LogWarning("[📦 PERSIST] AvatarSlotManager.Cache is null");
                return;
            }

            Debug.Log("[📦 PERSIST] AvatarSlotManager ready, loading persisted data...");

            var cache = slotManager.Cache;
            Debug.Log($"[📦 PERSIST] Cache available: {cache.GetConfiguredSlotCount()} configured slots, lastActive={cache.lastActiveSlotIndex}");

            if (host.BottomButtonContainer == null)
            {
                Debug.LogWarning("[📦 PERSIST] bottomButtonContainer is null");
                return;
            }

            // Issue #462: 既存ボタン数を正確にカウントし、bottomButtonCountを補正
            var existingButtons = host.BottomButtonContainer.Query<Button>().ToList();
            int existingSlotCount = 0;
            foreach (var btn in existingButtons)
            {
                if (btn != host.BottomButtonAdd) existingSlotCount++;
            }
            host.BottomButtonCount = existingSlotCount;
            Debug.Log($"[📦 PERSIST] Existing slot buttons: {existingSlotCount}, corrected bottomButtonCount={host.BottomButtonCount}");

            // 孤立スロットデータのクリーンアップ
            if (cache.lastCreatedSlotCount >= 0)
            {
                bool cleaned = false;
                for (int i = cache.lastCreatedSlotCount; i < cache.slots.Count; i++)
                {
                    var orphan = cache.GetSlot(i);
                    if (orphan != null && orphan.IsConfigured)
                    {
                        Debug.Log($"[📦 PERSIST] Cleaning orphaned slot {i}: {orphan.avatarName}");
                        cache.ClearSlot(i);
                        cleaned = true;
                    }
                }
                if (cleaned)
                {
                    cache.SaveToFile();
                    Debug.Log("[📦 PERSIST] Orphaned slot data cleaned up");
                }
            }

            // Issue #462: 設定済みスロットの最大インデックスを特定
            int maxConfiguredIndex = -1;
            var configuredSlotIndices = new List<int>();
            for (int i = 0; i < cache.slots.Count; i++)
            {
                var slot = cache.GetSlot(i);
                if (slot != null && slot.IsConfigured)
                {
                    configuredSlotIndices.Add(i);
                    maxConfiguredIndex = i;
                }
            }

            if (configuredSlotIndices.Count == 0)
            {
                Debug.Log("[📦 PERSIST] No configured slots found");
                return;
            }

            Debug.Log($"[📦 PERSIST] Configured slots: [{string.Join(", ", configuredSlotIndices)}], maxIndex={maxConfiguredIndex}");

            // Issue #462: 設定済みスロットのみボタン生成（空スロットは復元しない）
            Debug.Log($"[📦 PERSIST] Creating buttons for {configuredSlotIndices.Count} configured slots");
            foreach (int slotIdx in configuredSlotIndices)
            {
                // slot 0 → bottomButton1 は UXML に既存
                if (slotIdx == 0) continue;
                host.AddBottomPanelButtonForSlot(slotIdx);
            }

            // Phase 1: 全設定済みスロットのメタデータ・アイコンを復元
            var allButtons = host.BottomButtonContainer.Query<Button>().ToList();
            Debug.Log($"[📦 PERSIST] Total buttons after creation: {allButtons.Count}");
            int loadedCount = 0;
            Button lastActiveButton = null;
            var configuredButtons = new List<Button>();

            foreach (var button in allButtons)
            {
                if (button == host.BottomButtonAdd) continue;

                int slotIndex = host.GetSlotIndexFromButton(button);
                if (slotIndex < 0) continue;

                var avatarSlotData = cache.GetSlot(slotIndex);
                if (avatarSlotData == null || !avatarSlotData.IsConfigured) continue;

                Debug.Log($"[📦 PERSIST] Processing slot {slotIndex}: {avatarSlotData.avatarName}, hasIcon={avatarSlotData.HasIcon}");

                // slotDataMapを更新
                var slotData = host.EnsureSlotData(button);
                slotData.filePath = avatarSlotData.modelFilePath;
                slotData.fileType = avatarSlotData.fileType == AvatarFileType.VRM
                    ? SlotFileType.VRM
                    : SlotFileType.FBX;

                // アイコンを読み込んでUIを更新
                if (avatarSlotData.HasIcon && System.IO.File.Exists(avatarSlotData.iconFilePath))
                {
                    try
                    {
                        byte[] iconData = System.IO.File.ReadAllBytes(avatarSlotData.iconFilePath);
                        var texture = new Texture2D(2, 2);
                        if (texture.LoadImage(iconData))
                        {
                            slotData.thumbnail = texture;
                            host.UpdateButtonIcon(button, texture);
                            Debug.Log($"[📦 PERSIST] ✅ Icon loaded for slot {slotIndex}");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[📦 PERSIST] Failed to load icon for slot {slotIndex}: {e.Message}");
                    }
                }

                // lastActiveSlotIndexのボタンを記録
                if (slotIndex == cache.lastActiveSlotIndex)
                {
                    lastActiveButton = button;
                }

                configuredButtons.Add(button);
                loadedCount++;
            }

            Debug.Log($"[📦 PERSIST] Restored metadata for {loadedCount} slots");

            // Phase 2: Issue #462 バイナリキャッシュからアバターを自動ロード
            if (lastActiveButton == null && configuredButtons.Count > 0)
            {
                lastActiveButton = configuredButtons[0];
                Debug.Log($"[📦 PERSIST] lastActiveSlot not found, using first configured: {lastActiveButton.name}");
            }

            if (lastActiveButton != null)
            {
                Debug.Log($"[📦 PERSIST] Auto-loading lastActive slot: {lastActiveButton.name}");
                await AutoLoadSlotFromCacheAsync(lastActiveButton, slotManager, isActiveSlot: true);
            }

            // 他の設定済みスロットをロード
            foreach (var button in configuredButtons)
            {
                if (button == lastActiveButton) continue;
                Debug.Log($"[📦 PERSIST] Auto-loading additional slot: {button.name}");
                await AutoLoadSlotFromCacheAsync(button, slotManager, isActiveSlot: false);
            }

            Debug.Log($"[📦 PERSIST] ✅ All {loadedCount} persisted slots loaded");
        }

        /// <summary>
        /// Issue #462: 起動時にバイナリキャッシュからアバターを自動ロード。
        /// TryLoadFromBinaryCacheAsync と異なり、既存アバターを破棄しない。
        /// </summary>
        private async UniTask AutoLoadSlotFromCacheAsync(Button button, AvatarSlotManager slotManager, bool isActiveSlot)
        {
            var slotData = host.GetSlotData(button);
            if (slotData == null || !slotData.IsConfigured)
                return;

            int slotIndex = host.GetSlotIndexFromButton(button);
            if (slotIndex < 0) return;

            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            if (avatarSlotData == null || string.IsNullOrEmpty(avatarSlotData.binaryCacheId))
            {
                Debug.Log($"[📦 AUTO-LOAD] No binaryCacheId for slot {slotIndex}, skipping");
                return;
            }

            string cacheId = avatarSlotData.binaryCacheId;

            try
            {
                var cacheIntegrator = new AvatarCacheIntegrator(Application.persistentDataPath);

                if (!cacheIntegrator.HasBinaryCache(cacheId))
                {
                    Debug.Log($"[📦 AUTO-LOAD] Cache not found for slot {slotIndex}: {cacheId}");
                    return;
                }

                // プログレス表示
                slotProgressUI?.StartSlotLoading(button);
                slotProgressUI?.UpdateSlotProgress(button, 0.1f);

                var avatar = await cacheIntegrator.LoadFromBinaryCacheAsync(cacheId, progress =>
                {
                    slotProgressUI?.UpdateSlotProgress(button, 0.1f + (progress / 100f) * 0.6f);
                }, slotIndex);

                if (avatar == null)
                {
                    Debug.LogWarning($"[📦 AUTO-LOAD] Failed to load avatar for slot {slotIndex}");
                    slotProgressUI?.CancelSlotLoading(button);
                    return;
                }

                // 非アクティブスロットは即座に非表示化（描画を防ぐ）
                if (!isActiveSlot)
                {
                    avatar.SetActive(false);
                }

                Debug.Log($"[📦 AUTO-LOAD] Avatar loaded for slot {slotIndex}: {avatar.name}, active={isActiveSlot}");

                slotProgressUI?.UpdateSlotProgress(button, 0.7f);

                // v0.8.0: AOC・表情セットアップ（アクティブスロットのみ）
                // 非アクティブスロットで SetupExpressionSystem を呼ぶと、
                // アクティブスロットの expressionSetup/blendShapeExpressionManager を上書きしてしまうため、
                // アクティブスロットのみで呼び出す
                poseUIController?.ApplyDefaultAOC(avatar);
                if (isActiveSlot)
                {
                    expressionUIController?.SetupExpressionSystem(avatar, slotIndex);
                    expressionUIController?.TriggerExpressionIconGeneration(avatar, slotIndex);
                }

                if (isActiveSlot)
                {
                    // アクティブスロット: カメラ前方に配置して表示
                    host.PlaceAvatarAheadOfCamera(avatar);
                    host.ReapplyLightingSettings();

                    slotProgressUI?.UpdateSlotProgress(button, 0.85f);
                    await UniTask.DelayFrame(3);
                    slotProgressUI?.CompleteSlotLoading(button);

                    slotData.loadedAvatar = avatar;
                    host.UpdateSlotSelection(button);
                    poseUIController?.SetCachedAvatar(avatar);
                    avatar.SetActive(true);
                    Debug.Log($"[📦 AUTO-LOAD] ✅ Active slot {slotIndex} loaded and visible");
                }
                else
                {
                    // 非アクティブスロット: 配置せずロードだけして非表示
                    avatar.SetActive(false);

                    slotProgressUI?.UpdateSlotProgress(button, 0.85f);
                    await UniTask.DelayFrame(3);
                    slotProgressUI?.CompleteSlotLoading(button);

                    slotData.loadedAvatar = avatar;
                    Debug.Log($"[📦 AUTO-LOAD] ✅ Slot {slotIndex} loaded (hidden)");
                }

                // アイコン復元（キャッシュから復元されていない場合）
                string iconPath = AvatarSlotCache.GetIconPath(slotIndex);
                if (!System.IO.File.Exists(iconPath) && avatar != null)
                {
                    await UniTask.DelayFrame(3);
                    var thumbnail = await AvatarIconCapture.Instance.CaptureAsTextureAsync(avatar);
                    if (thumbnail != null)
                    {
                        slotData.thumbnail = thumbnail;
                        host.UpdateButtonIcon(button, thumbnail);
                        host.SaveThumbnailToFile(button, thumbnail);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[📦 AUTO-LOAD] Error loading slot {slotIndex}: {e.Message}");
                slotProgressUI?.CancelSlotLoading(button);
            }
        }
    }
}
