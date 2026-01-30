using UnityEngine;
using UnityEngine.UIElements;
using System;
using AICam.AvatarCache;
using AICam.FBXLoader;

namespace AICam.UI
{
    /// <summary>
    /// Phase 06: スロット削除ポップアップ・キャッシュクリアポップアップを管理するコントローラー。
    /// </summary>
    public class SlotDeleteController
    {
        // Delete popup
        private VisualElement deletePopup;
        private Button deleteButton;
        private Button cancelButton;

        // Clear cache popup
        private VisualElement clearCachePopup;
        private Button clearCacheButton;
        private Button clearCacheCancelButton;

        // Dependencies
        private readonly AvatarSlotUIController slotUIController;
        private readonly SlotPersistenceController slotPersistenceController;
        private readonly Action<string, string, float> showInfo;
        private readonly bool enableDebugLogging;

        public SlotDeleteController(
            VisualElement root,
            AvatarSlotUIController slotUIController,
            SlotPersistenceController slotPersistenceController,
            Action<string, string, float> showInfo,
            bool enableDebugLogging)
        {
            this.slotUIController = slotUIController;
            this.slotPersistenceController = slotPersistenceController;
            this.showInfo = showInfo;
            this.enableDebugLogging = enableDebugLogging;

            CreateDeletePopup(root);
            if (enableDebugLogging) Debug.Log($"🔧 Delete popup created: {(deletePopup != null ? "✅" : "❌")}");

            CreateClearCachePopup(root);
            if (enableDebugLogging) Debug.Log($"🔧 Clear cache popup created: {(clearCachePopup != null ? "✅" : "❌")}");
        }

        public bool IsDeletePopupVisible => deletePopup != null && deletePopup.style.display == DisplayStyle.Flex;
        public bool IsClearCachePopupVisible => clearCachePopup != null && clearCachePopup.style.display == DisplayStyle.Flex;

        private void CreateDeletePopup(VisualElement root)
        {
            deletePopup = new VisualElement();
            deletePopup.name = "deletePopup";
            deletePopup.AddToClassList("delete-popup");

            deletePopup.style.position = Position.Absolute;
            deletePopup.pickingMode = PickingMode.Position;

            deleteButton = new Button();
            deleteButton.text = "削除";
            deleteButton.AddToClassList("delete-popup-button");
            deleteButton.AddToClassList("delete");
            deleteButton.RegisterCallback<ClickEvent>(evt => OnDeleteButtonClicked());

            cancelButton = new Button();
            cancelButton.text = "キャンセル";
            cancelButton.AddToClassList("delete-popup-button");
            cancelButton.RegisterCallback<ClickEvent>(evt => HideDeletePopup());

            deletePopup.Add(deleteButton);
            deletePopup.Add(cancelButton);
            root.Add(deletePopup);

            Debug.Log("✅ Delete popup created");
        }

        private void CreateClearCachePopup(VisualElement root)
        {
            clearCachePopup = new VisualElement();
            clearCachePopup.name = "clearCachePopup";
            clearCachePopup.AddToClassList("delete-popup");

            clearCachePopup.style.position = Position.Absolute;
            clearCachePopup.pickingMode = PickingMode.Position;

            clearCacheButton = new Button();
            clearCacheButton.text = "キャッシュクリア";
            clearCacheButton.AddToClassList("delete-popup-button");
            clearCacheButton.AddToClassList("delete");
            clearCacheButton.RegisterCallback<ClickEvent>(evt => OnClearCacheButtonClicked());

            clearCacheCancelButton = new Button();
            clearCacheCancelButton.text = "キャンセル";
            clearCacheCancelButton.AddToClassList("delete-popup-button");
            clearCacheCancelButton.RegisterCallback<ClickEvent>(evt => HideClearCachePopup());

            clearCachePopup.Add(clearCacheButton);
            clearCachePopup.Add(clearCacheCancelButton);
            root.Add(clearCachePopup);

            Debug.Log("✅ Clear cache popup created");
        }

        public void ShowDeletePopup(Button targetButton)
        {
            if (deletePopup == null)
            {
                Debug.LogError("❌ deletePopup is null!");
                return;
            }

            if (targetButton == null)
            {
                Debug.LogError("❌ targetButton is null!");
                return;
            }

            var buttonBounds = targetButton.worldBound;
            Debug.Log($"📍 Button bounds: x={buttonBounds.x}, y={buttonBounds.y}, width={buttonBounds.width}, height={buttonBounds.height}");

            float popupWidth = 120f;
            float popupHeight = 90f;

            float popupLeft = buttonBounds.x + (buttonBounds.width / 2) - (popupWidth / 2);
            float popupTop = buttonBounds.y - popupHeight - 10;

            Debug.Log($"📍 Popup position: left={popupLeft}, top={popupTop}");

            deletePopup.style.left = popupLeft;
            deletePopup.style.top = popupTop;
            deletePopup.style.display = DisplayStyle.Flex;

            Debug.Log($"📋 Delete popup shown for {targetButton.name}");
            Debug.Log($"📋 Popup display style: {deletePopup.style.display}");
            Debug.Log($"📋 Popup position type: {deletePopup.style.position}");

            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        public void HideDeletePopup()
        {
            if (deletePopup == null) return;

            deletePopup.style.display = DisplayStyle.None;
            slotUIController.ClearLongPressButton();
            Debug.Log("❌ Delete popup hidden");

            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        public void ShowClearCachePopup()
        {
            if (clearCachePopup == null)
            {
                Debug.LogError("❌ clearCachePopup is null!");
                return;
            }

            var addButton = slotUIController.BottomButtonAdd;
            if (addButton == null)
            {
                Debug.LogError("❌ bottomButtonAdd is null!");
                return;
            }

            var buttonBounds = addButton.worldBound;
            Debug.Log($"📍 Add button bounds: x={buttonBounds.x}, y={buttonBounds.y}, width={buttonBounds.width}, height={buttonBounds.height}");

            float popupWidth = 140f;
            float popupHeight = 90f;

            float popupLeft = buttonBounds.x + (buttonBounds.width / 2) - (popupWidth / 2);
            float popupTop = buttonBounds.y - popupHeight - 10;

            Debug.Log($"📍 Clear cache popup position: left={popupLeft}, top={popupTop}");

            clearCachePopup.style.left = popupLeft;
            clearCachePopup.style.top = popupTop;
            clearCachePopup.style.display = DisplayStyle.Flex;

            Debug.Log("🗑 Clear cache popup shown");

            TapticEngine.Impact(TapticEngine.ImpactStyle.Heavy);
        }

        public void HideClearCachePopup()
        {
            if (clearCachePopup == null) return;

            clearCachePopup.style.display = DisplayStyle.None;
            Debug.Log("❌ Clear cache popup hidden");

            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        private void OnClearCacheButtonClicked()
        {
            Debug.Log("🗑 Clear cache button clicked");

            ClearAllAvatarCache();
            HideClearCachePopup();

            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        private void ClearAllAvatarCache()
        {
            Debug.Log("🗑 Clearing all avatar cache...");

            // 1. ロード済みアバターを破棄し、UIボタンを削除
            slotUIController.ClearAllSlotsAndAvatars();

            // 2. AvatarSlotManager のキャッシュをクリア
            var slotManager = AvatarSlotManager.Instance;
            if (slotManager != null)
            {
                var cache = slotManager.Cache;
                if (cache != null)
                {
                    for (int i = 0; i < cache.maxSlots; i++)
                    {
                        slotManager.ClearSlot(i);
                    }
                    // lastCreatedSlotCount をリセット
                    cache.lastCreatedSlotCount = 1;
                    cache.lastActiveSlotIndex = 0;
                    cache.SaveToFile();
                }
            }

            // 3. メモリキャッシュをクリア
            var memoryCache = AvatarMemoryCache.Instance;
            if (memoryCache != null)
            {
                memoryCache.ClearAll();
            }

            // 4. スロット数を保存
            slotPersistenceController?.SaveSlotCount(slotUIController.BottomButtonCount);

            showInfo?.Invoke("Cache", "キャッシュをクリアしました", 2f);
            Debug.Log("✅ All avatar cache cleared");
        }

        private void OnDeleteButtonClicked()
        {
            var longPressButton = slotUIController.CurrentLongPressButton;
            if (longPressButton == null || slotUIController.BottomButtonContainer == null)
            {
                HideDeletePopup();
                return;
            }

            Debug.Log($"🗑 Deleting button: {longPressButton.name}");

            int slotIndex = slotUIController.GetSlotIndexFromButton(longPressButton);
            if (slotIndex >= 0)
            {
                var slotManager = AvatarSlotManager.Instance;
                if (slotManager != null)
                {
                    slotManager.ClearSlot(slotIndex);
                    Debug.Log($"🗑 Cleared slot data for index {slotIndex}");
                }
            }

            slotUIController.RemoveSlot(longPressButton);
            HideDeletePopup();

            slotPersistenceController?.SaveSlotCount(slotUIController.BottomButtonCount);

            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }
    }
}
