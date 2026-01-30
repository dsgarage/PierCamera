using UnityEngine;
using UnityEngine.UIElements;
using System;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using AICam.AvatarCache;
using AICam.FBXLoader;

namespace AICam.UI
{
    /// <summary>
    /// Phase 06: スロットUIの管理（ボタン作成・選択・データマップ・長押し検出・ダブルタップ）を担当するコントローラー。
    /// </summary>
    public class AvatarSlotUIController
    {
        // UI elements
        private readonly VisualElement root;
        private readonly VisualElement bottomButtonContainer;
        private readonly Button bottomButtonAdd;
        private int bottomButtonCount = 1;

        // Slot data
        private readonly Dictionary<Button, SlotData> slotDataMap = new Dictionary<Button, SlotData>();
        private Button currentSelectedSlot;

        // Double tap detection
        private const float DOUBLE_TAP_THRESHOLD = 0.3f;
        private Button lastClickedSlotButton;
        private float lastSlotClickTime;

        // Long press detection
        private Button currentLongPressButton;
        private float longPressTime = 0f;
        private const float longPressThresholdForDelete = 0.5f;
        private bool isLongPressing = false;
        private bool suppressNextClick = false;

        // Dependencies
        private readonly SlotPersistenceController slotPersistenceController;
        private readonly BinaryCacheService binaryCacheService;
        private readonly PoseUIController poseUIController;
        private readonly ExpressionUIController expressionUIController;
        private readonly Action<GameObject> placeAvatarAheadOfCamera;
        private readonly Action<string, string, float> showInfo;
        private readonly bool enableDebugLogging;

        // Set after construction (two-phase init to break circular deps)
        private AvatarLoadOrchestrator loadOrchestrator;
        private SlotDeleteController deleteController;

        // Public properties
        public VisualElement BottomButtonContainer => bottomButtonContainer;
        public Button BottomButtonAdd => bottomButtonAdd;
        public int BottomButtonCount
        {
            get => bottomButtonCount;
            set => bottomButtonCount = value;
        }
        public Button CurrentLongPressButton => currentLongPressButton;

        public AvatarSlotUIController(
            VisualElement root,
            SlotPersistenceController slotPersistenceController,
            BinaryCacheService binaryCacheService,
            PoseUIController poseUIController,
            ExpressionUIController expressionUIController,
            Action<GameObject> placeAvatarAheadOfCamera,
            Action<string, string, float> showInfo,
            bool enableDebugLogging)
        {
            this.root = root;
            this.slotPersistenceController = slotPersistenceController;
            this.binaryCacheService = binaryCacheService;
            this.poseUIController = poseUIController;
            this.expressionUIController = expressionUIController;
            this.placeAvatarAheadOfCamera = placeAvatarAheadOfCamera;
            this.showInfo = showInfo;
            this.enableDebugLogging = enableDebugLogging;

            // Query UI elements
            bottomButtonContainer = root.Q<VisualElement>("bottomButtonContainer");
            bottomButtonAdd = root.Q<Button>("bottomButtonAdd");

            // ScrollView setup
            SetupScrollView();

            // +button click event
            if (bottomButtonAdd != null)
            {
                bottomButtonAdd.RegisterCallback<ClickEvent>(evt =>
                {
                    if (suppressNextClick)
                    {
                        Debug.Log($"Click suppressed after long press on {bottomButtonAdd.name}");
                        suppressNextClick = false;
                        return;
                    }
                    AddBottomPanelButton();
                });
                RegisterLongPressForButton(bottomButtonAdd);
                if (enableDebugLogging) Debug.Log("✅ Add button events registered (including long press)");
            }

            // Register existing buttons
            RegisterLongPressForExistingButtons();
        }

        public void SetLoadOrchestrator(AvatarLoadOrchestrator orchestrator)
        {
            loadOrchestrator = orchestrator;
        }

        public void SetDeleteController(SlotDeleteController controller)
        {
            deleteController = controller;
        }

        private void SetupScrollView()
        {
            var bottomScrollView = root.Q<ScrollView>("bottomScrollView");
            if (bottomScrollView != null)
            {
                bottomScrollView.mode = ScrollViewMode.Horizontal;

#if UNITY_EDITOR
                bottomScrollView.horizontalScrollerVisibility = ScrollerVisibility.Auto;
#else
                bottomScrollView.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
#endif
                bottomScrollView.verticalScrollerVisibility = ScrollerVisibility.Hidden;

                bottomScrollView.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
                bottomScrollView.elasticity = 0.1f;
                bottomScrollView.scrollDecelerationRate = 0.135f;

                bottomScrollView.horizontalPageSize = 0;
                bottomScrollView.verticalPageSize = 0;
                bottomScrollView.nestedInteractionKind = ScrollView.NestedInteractionKind.Default;

                bottomScrollView.contentContainer.style.flexDirection = FlexDirection.Row;
                bottomScrollView.contentContainer.style.flexWrap = Wrap.NoWrap;

                bottomScrollView.mouseWheelScrollSize = 30f;

                if (enableDebugLogging) Debug.Log($"✅ ScrollView configured: mode={bottomScrollView.mode}, touchBehavior={bottomScrollView.touchScrollBehavior}");
            }
        }

        /// <summary>
        /// Update loop for long press detection. Called from CCC.Update().
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (isLongPressing && currentLongPressButton != null)
            {
                longPressTime += deltaTime;

                if (Mathf.FloorToInt(longPressTime * 10) != Mathf.FloorToInt((longPressTime - deltaTime) * 10))
                {
                    Debug.Log($"⏱ Long press time: {longPressTime:F2}s / {longPressThresholdForDelete}s");
                }

                if (longPressTime >= longPressThresholdForDelete)
                {
                    Debug.Log($"✅ Long press threshold reached! Showing popup for {currentLongPressButton.name}");

                    if (currentLongPressButton == bottomButtonAdd)
                    {
                        deleteController?.ShowClearCachePopup();
                    }
                    else
                    {
                        deleteController?.ShowDeletePopup(currentLongPressButton);
                    }

                    isLongPressing = false;
                    longPressTime = 0f;
                }
            }
        }

        #region Button Management

        public void AddBottomPanelButton(bool persistSlotCount = true)
        {
            if (bottomButtonContainer == null)
            {
                Debug.LogWarning("⚠️ bottomButtonContainer is null");
                return;
            }

            bottomButtonCount++;
            Debug.Log($"➕ Adding bottom panel button #{bottomButtonCount}");

            var newButton = new Button();
            newButton.name = $"bottomButton{bottomButtonCount}";
            newButton.AddToClassList("bottom-panel-button");

            int addButtonIndex = bottomButtonContainer.IndexOf(bottomButtonAdd);
            bottomButtonContainer.Insert(addButtonIndex, newButton);

            RegisterLongPressForButton(newButton);

            newButton.RegisterCallback<ClickEvent>(evt =>
            {
                if (suppressNextClick)
                {
                    Debug.Log($"🚫 Click suppressed after long press on {newButton.name}");
                    suppressNextClick = false;
                    return;
                }

                Debug.Log($"🔘 Bottom button #{newButton.name} clicked");
                TapticEngine.Selection();

                loadOrchestrator?.OnSlotClicked(newButton);
            });

            if (persistSlotCount)
            {
                slotPersistenceController?.SaveSlotCount(bottomButtonCount);
            }

            TapticEngine.Impact(TapticEngine.ImpactStyle.Light);
        }

        public Button AddBottomPanelButtonForSlot(int slotIndex)
        {
            if (bottomButtonContainer == null)
            {
                Debug.LogWarning("⚠️ bottomButtonContainer is null");
                return null;
            }

            int buttonNumber = slotIndex + 1;

            var newButton = new Button();
            newButton.name = $"bottomButton{buttonNumber}";
            newButton.AddToClassList("bottom-panel-button");

            int addButtonIndex = bottomButtonContainer.IndexOf(bottomButtonAdd);
            bottomButtonContainer.Insert(addButtonIndex, newButton);

            RegisterLongPressForButton(newButton);

            newButton.RegisterCallback<ClickEvent>(evt =>
            {
                if (suppressNextClick)
                {
                    Debug.Log($"🚫 Click suppressed after long press on {newButton.name}");
                    suppressNextClick = false;
                    return;
                }

                Debug.Log($"🔘 Bottom button #{newButton.name} clicked");
                TapticEngine.Selection();
                loadOrchestrator?.OnSlotClicked(newButton);
            });

            if (buttonNumber > bottomButtonCount)
                bottomButtonCount = buttonNumber;

            Debug.Log($"➕ Added button for slot {slotIndex}: {newButton.name}");
            return newButton;
        }

        #endregion

        #region Long Press & Click Registration

        private void RegisterLongPressForExistingButtons()
        {
            if (bottomButtonContainer == null) return;

            var buttons = bottomButtonContainer.Query<Button>().ToList();
            foreach (var button in buttons)
            {
                if (button == bottomButtonAdd) continue;

                RegisterLongPressForButton(button);

                button.RegisterCallback<ClickEvent>(evt =>
                {
                    if (suppressNextClick)
                    {
                        Debug.Log($"🚫 Click suppressed after long press on {button.name}");
                        suppressNextClick = false;
                        return;
                    }

                    Debug.Log($"🔘 Bottom button #{button.name} clicked");
                    TapticEngine.Selection();

                    // Double tap detection
                    float currentTime = Time.time;
                    bool isConfiguredSlot = slotDataMap.TryGetValue(button, out var slotData) && slotData != null && slotData.IsConfigured;

                    if (isConfiguredSlot && lastClickedSlotButton == button && (currentTime - lastSlotClickTime) <= DOUBLE_TAP_THRESHOLD)
                    {
                        Debug.Log($"👆👆 Double tap detected on slot: {button.name}");
                        lastClickedSlotButton = null;
                        lastSlotClickTime = 0f;
                        OnSlotDoubleTapped(button, slotData);
                        return;
                    }

                    lastClickedSlotButton = isConfiguredSlot ? button : null;
                    lastSlotClickTime = currentTime;

                    loadOrchestrator?.OnSlotClicked(button);
                });
            }

            Debug.Log($"✅ Long press and click registered for {buttons.Count - 1} buttons");
        }

        private void RegisterLongPressForButton(Button button)
        {
            button.RegisterCallback<PointerDownEvent>(evt =>
            {
                isLongPressing = true;
                currentLongPressButton = button;
                longPressTime = 0f;
                Debug.Log($"👇 Long press started on {button.name}");
            }, TrickleDown.TrickleDown);

            button.RegisterCallback<PointerUpEvent>(evt =>
            {
                Debug.Log($"👆 Long press released on {button.name} (time: {longPressTime:F2}s, isLongPressing: {isLongPressing})");

                if (longPressTime < longPressThresholdForDelete)
                {
                    Debug.Log($"📌 Short press detected, allowing click event");
                }
                else
                {
                    Debug.Log($"⏱ Long press detected, suppressing click event");
                    evt.StopPropagation();
                    suppressNextClick = true;
                }

                isLongPressing = false;
                longPressTime = 0f;
            }, TrickleDown.TrickleDown);

            button.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                if (isLongPressing)
                {
                    Debug.Log($"↖️ Pointer left {button.name}, cancelling long press");
                    isLongPressing = false;
                    longPressTime = 0f;
                }
            });
        }

        #endregion

        #region Slot Double Tap / Export

        private void OnSlotDoubleTapped(Button button, SlotData slotData)
        {
            if (slotData == null || !slotData.IsConfigured)
            {
                Debug.LogWarning($"[AvatarSlotUIController] Cannot export unconfigured slot: {button.name}");
                return;
            }

            Debug.Log($"📤 Double tap on configured slot, showing export popup: {button.name}");

            int slotIndex = GetSlotIndexFromButton(button);

            AvatarSlotData avatarSlotData = null;
            var slotManager = AvatarSlotManager.Instance;

            if (slotManager?.Cache != null)
            {
                avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            }

            if (avatarSlotData == null || !avatarSlotData.IsConfigured)
            {
                Debug.Log($"[AvatarSlotUIController] Creating AvatarSlotData from local slotData for slot {slotIndex}");
                avatarSlotData = new AvatarSlotData(slotIndex)
                {
                    modelFilePath = slotData.filePath,
                    avatarName = slotData.loadedAvatar != null ? slotData.loadedAvatar.name : Path.GetFileNameWithoutExtension(slotData.filePath)
                };

                if (slotData.loadedAvatar != null)
                {
                    binaryCacheService?.CreateBinaryCacheAndShowPopupAsync(slotIndex, avatarSlotData, slotData.loadedAvatar);
                }
                else
                {
                    Debug.LogWarning($"[AvatarSlotUIController] No loaded avatar for slot {slotIndex}");
                    showInfo?.Invoke("Error", "アバターがロードされていません", 2f);
                }
            }
            else
            {
                ShowExportPopupDirect(slotIndex, avatarSlotData);
            }

            TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
        }

        public void ShowExportPopupDirect(int slotIndex, AvatarSlotData avatarSlotData)
        {
            var popup = ExportPopup.Instance;
            if (popup == null)
            {
                Debug.Log("[AvatarSlotUIController] Creating ExportPopup instance");
                var popupObj = new GameObject("ExportPopup");
                popup = popupObj.AddComponent<ExportPopup>();
            }

            Debug.Log($"[AvatarSlotUIController] Showing export popup for slot {slotIndex}, binaryCacheId: {avatarSlotData.binaryCacheId}");
            popup.Show(slotIndex, avatarSlotData, root, (success, path) =>
            {
                if (success)
                {
                    Debug.Log($"📤 Export completed: {path}");
                    showInfo?.Invoke("Export", "エクスポート完了", 2f);
                }
            });
        }

        #endregion

        #region Slot Data Access

        public int GetSlotIndexFromButton(Button button)
        {
            if (button == null) return -1;

            string name = button.name;
            if (name.StartsWith("bottomButton"))
            {
                string numStr = name.Replace("bottomButton", "");
                if (int.TryParse(numStr, out int num))
                {
                    return num - 1;
                }
            }
            return -1;
        }

        public SlotData EnsureSlotData(Button button)
        {
            if (!slotDataMap.ContainsKey(button))
                slotDataMap[button] = new SlotData();
            return slotDataMap[button];
        }

        public SlotData GetSlotData(Button button)
        {
            return slotDataMap.TryGetValue(button, out var data) ? data : null;
        }

        public bool IsCurrentSelectedSlot(Button button)
        {
            return currentSelectedSlot == button;
        }

        #endregion

        #region Selection & Activation

        public void UpdateSlotSelection(Button selectedButton)
        {
            if (currentSelectedSlot != null)
            {
                currentSelectedSlot.RemoveFromClassList("selected");
            }

            currentSelectedSlot = selectedButton;
            if (currentSelectedSlot != null)
            {
                currentSelectedSlot.AddToClassList("selected");
            }
        }

        public void ActivateSlotAvatar(SlotData slotData)
        {
            foreach (var kvp in slotDataMap)
            {
                if (kvp.Value?.loadedAvatar != null)
                {
                    kvp.Value.loadedAvatar.SetActive(false);
                }
            }

            if (slotData?.loadedAvatar != null)
            {
                placeAvatarAheadOfCamera?.Invoke(slotData.loadedAvatar);

                poseUIController?.SetCachedAvatar(slotData.loadedAvatar);
                Debug.Log($"Updated cachedCurrentAvatar: {slotData.loadedAvatar.name}");
            }

            // スロットインデックスを取得
            int slotIndex = -1;
            foreach (var kvp in slotDataMap)
            {
                if (kvp.Value == slotData)
                {
                    slotIndex = GetSlotIndexFromButton(kvp.Key);
                    break;
                }
            }

            expressionUIController?.OnSlotActivated(slotData?.loadedAvatar, slotIndex);
        }

        #endregion

        #region Icon Management

        public void UpdateButtonIcon(Button button, Texture2D texture)
        {
            if (button == null)
            {
                Debug.LogWarning("⚠️ UpdateButtonIcon: button is null");
                return;
            }

            if (texture == null)
            {
                Debug.LogWarning($"⚠️ UpdateButtonIcon: texture is null for {button.name}");
                return;
            }

            Debug.Log($"🖼 UpdateButtonIcon: Setting texture {texture.width}x{texture.height} to {button.name}");

            button.style.backgroundImage = new StyleBackground(texture);
            button.AddToClassList("has-icon");

            Debug.Log($"✅ Button icon updated for {button.name}");
        }

        public string SaveThumbnailToFile(Button button, Texture2D thumbnail)
        {
            if (thumbnail == null) return null;

            int slotIndex = GetSlotIndexFromButton(button);
            if (slotIndex < 0) return null;

            try
            {
                string iconPath = AvatarSlotCache.GetIconPath(slotIndex);
                string iconDir = Path.GetDirectoryName(iconPath);
                if (!Directory.Exists(iconDir))
                    Directory.CreateDirectory(iconDir);

                byte[] pngData = thumbnail.EncodeToPNG();
                File.WriteAllBytes(iconPath, pngData);
                Debug.Log($"[ICON] Saved icon to {iconPath} ({pngData.Length} bytes)");
                return iconPath;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ICON] Failed to save icon for slot {slotIndex}: {e.Message}");
                return null;
            }
        }

        public void RefreshAllSlotIcons()
        {
            if (bottomButtonContainer == null) return;

            var buttons = bottomButtonContainer.Query<Button>().ToList();
            foreach (var button in buttons)
            {
                if (button == bottomButtonAdd) continue;

                button.style.backgroundImage = StyleKeyword.None;

                if (slotDataMap.ContainsKey(button))
                {
                    slotDataMap[button] = new SlotData();
                }
            }

            Debug.Log("✅ All slot icons refreshed");
        }

        #endregion

        #region Delete Support

        public void ClearLongPressButton()
        {
            currentLongPressButton = null;
        }

        /// <summary>
        /// キャッシュクリア時に全スロットのアバターを破棄し、UIボタンを削除する。
        /// bottomButton1 は残すがアイコンはリセットする。
        /// </summary>
        public void ClearAllSlotsAndAvatars()
        {
            Debug.Log("🗑 ClearAllSlotsAndAvatars: Starting...");

            // 1. ロード済みアバターを破棄
            foreach (var kvp in slotDataMap)
            {
                var slotData = kvp.Value;
                if (slotData?.loadedAvatar != null)
                {
                    Debug.Log($"🗑 Destroying avatar: {slotData.loadedAvatar.name}");
                    UnityEngine.Object.Destroy(slotData.loadedAvatar);
                    slotData.loadedAvatar = null;
                }
            }

            // 2. bottomButton1 以外のボタンを削除
            if (bottomButtonContainer != null)
            {
                var buttonsToRemove = new List<Button>();
                foreach (var child in bottomButtonContainer.Children())
                {
                    if (child is Button btn && btn != bottomButtonAdd && btn.name != "bottomButton1")
                    {
                        buttonsToRemove.Add(btn);
                    }
                }

                foreach (var btn in buttonsToRemove)
                {
                    Debug.Log($"🗑 Removing button: {btn.name}");
                    slotDataMap.Remove(btn);
                    bottomButtonContainer.Remove(btn);
                }
            }

            // 3. bottomButton1 のアイコンをリセット
            var bottomButton1 = root.Q<Button>("bottomButton1");
            if (bottomButton1 != null)
            {
                bottomButton1.style.backgroundImage = StyleKeyword.None;
                bottomButton1.RemoveFromClassList("has-icon");
                bottomButton1.RemoveFromClassList("selected");

                if (slotDataMap.ContainsKey(bottomButton1))
                {
                    slotDataMap[bottomButton1] = new SlotData();
                }
            }

            // 4. カウントをリセット
            bottomButtonCount = 1;
            currentSelectedSlot = null;

            Debug.Log("✅ ClearAllSlotsAndAvatars: Complete");
        }

        public void RemoveSlot(Button button)
        {
            // ロード済みアバターを破棄
            if (slotDataMap.TryGetValue(button, out var slotData))
            {
                if (slotData?.loadedAvatar != null)
                {
                    Debug.Log($"🗑 RemoveSlot: Destroying avatar {slotData.loadedAvatar.name}");
                    UnityEngine.Object.Destroy(slotData.loadedAvatar);
                    slotData.loadedAvatar = null;
                }
                slotDataMap.Remove(button);
            }

            if (bottomButtonContainer != null)
            {
                bottomButtonContainer.Remove(button);
            }

            // Recount buttons
            bottomButtonCount = 0;
            if (bottomButtonContainer != null)
            {
                foreach (var child in bottomButtonContainer.Children())
                {
                    if (child is Button btn && btn != bottomButtonAdd)
                        bottomButtonCount++;
                }
            }
        }

        #endregion
    }
}
