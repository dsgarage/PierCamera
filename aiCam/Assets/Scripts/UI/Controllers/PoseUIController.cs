using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using AICam.AvatarCache;
using DSGarage.PoseSlot;

namespace AICam.UI
{
    /// <summary>
    /// ポーズ切り替えUI制御（シングルタップ→次のポーズ、ダブルタップ→OverrideController切り替え）を管理するコントローラー。
    /// </summary>
    public class PoseUIController
    {
        private readonly AnimatorOverrideController[] poseOverrideControllers;
        private readonly PoseSlotController poseSlotController;
        private readonly bool enableDebugLogging;
        private readonly System.Action<string, string, float> showInfo;
        private AICam.FBXLoader.RuntimeFBXLoaderBridge fbxLoaderBridge;

        private const float DOUBLE_TAP_THRESHOLD = 0.3f;
        private const int POSE_COUNT = 12;

        private int currentPoseIndex = 0;
        private int currentOverrideIndex = 0;
        private GameObject cachedCurrentAvatar;
        private List<string> cachedStateNames;

        // ダブルタップ検出
        private int tapCount = 0;
        private System.Threading.CancellationTokenSource tapCts;

        public PoseUIController(
            VisualElement root,
            AnimatorOverrideController[] poseOverrideControllers,
            PoseSlotController poseSlotController,
            AICam.FBXLoader.RuntimeFBXLoaderBridge fbxLoaderBridge,
            bool enableDebugLogging,
            System.Action<string, string, float> showInfo)
        {
            this.poseOverrideControllers = poseOverrideControllers;
            this.poseSlotController = poseSlotController;
            this.fbxLoaderBridge = fbxLoaderBridge;
            this.enableDebugLogging = enableDebugLogging;
            this.showInfo = showInfo;

            var topButton4 = root.Q<Button>("topButton4");
            if (enableDebugLogging) Debug.Log($"🔘 topButton4: {(topButton4 != null ? "✅ found" : "❌ NOT FOUND")}");
            if (topButton4 != null)
            {
                topButton4.RegisterCallback<ClickEvent>(evt => OnTopButton4Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 4 (Pose) click event registered");
            }
        }

        /// <summary>
        /// 外部からキャッシュ済みアバターを設定。
        /// </summary>
        public void SetCachedAvatar(GameObject avatar)
        {
            cachedCurrentAvatar = avatar;
        }

        /// <summary>
        /// 現在のアバターを取得するヘルパー。
        /// </summary>
        public GameObject GetCurrentAvatar()
        {
            // キャッシュされたアバターを優先
            if (cachedCurrentAvatar != null && cachedCurrentAvatar.activeInHierarchy)
            {
                return cachedCurrentAvatar;
            }

            // AvatarSlotManager + AvatarMemoryCacheから取得
            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            var memoryCache = AvatarMemoryCache.Instance;

            if (slotManager != null && memoryCache != null)
            {
                int currentSlot = slotManager.CurrentSlotIndex;
                if (currentSlot >= 0)
                {
                    var avatar = memoryCache.GetCachedAvatar(currentSlot);
                    if (avatar != null)
                    {
                        cachedCurrentAvatar = avatar;
                        return avatar;
                    }
                }
            }

            // RuntimeFBXLoaderBridgeから取得
            if (fbxLoaderBridge != null && fbxLoaderBridge.CurrentModel != null)
            {
                cachedCurrentAvatar = fbxLoaderBridge.CurrentModel;
                return cachedCurrentAvatar;
            }

            // シーン内検索
            var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
            foreach (var anim in animators)
            {
                if (anim.avatar != null && anim.avatar.isHuman && anim.gameObject.activeInHierarchy)
                {
                    cachedCurrentAvatar = anim.gameObject;
                    return cachedCurrentAvatar;
                }
            }

            return null;
        }

        /// <summary>
        /// アバターにデフォルトのAOCを適用（Pose00を再生）。
        /// </summary>
        public void ApplyDefaultAOC(GameObject avatar)
        {
            if (avatar == null)
            {
                Debug.LogWarning("⚠️ ApplyDefaultAOC: avatar is null");
                return;
            }

            if (poseOverrideControllers == null || poseOverrideControllers.Length == 0)
            {
                Debug.LogWarning("⚠️ ApplyDefaultAOC: No OverrideControllers configured");
                return;
            }

            var animator = avatar.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning($"⚠️ ApplyDefaultAOC: Avatar {avatar.name} has no Animator component");
                return;
            }

            // 最初のAOC（p012 = デフォルト）を適用
            var defaultAOC = poseOverrideControllers[0];
            if (defaultAOC == null)
            {
                Debug.LogWarning("⚠️ ApplyDefaultAOC: First OverrideController is null");
                return;
            }

            animator.runtimeAnimatorController = defaultAOC;
            currentOverrideIndex = 0;
            currentPoseIndex = 0;
            cachedCurrentAvatar = avatar;

            // 初期ポーズを再生
            animator.Play("Pose00", 0, 0f);

            Debug.Log($"🎭 ApplyDefaultAOC: Applied {defaultAOC.name} to {avatar.name}");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // Issue #439: デバッグビルドのみAOC適用メッセージを表示
            showInfo?.Invoke("AOC", defaultAOC.name, 1.5f);
#endif
        }

        /// <summary>
        /// アバタースロットロード完了時のハンドラ。
        /// CCC の OnAvatarSlotLoadComplete から呼ばれる。
        /// </summary>
        public void OnSlotLoadComplete(int slotIndex)
        {
            var memoryCache = AvatarMemoryCache.Instance;
            if (memoryCache != null)
            {
                cachedCurrentAvatar = memoryCache.GetCachedAvatar(slotIndex);
                currentPoseIndex = 0;
                cachedStateNames = null;
                Debug.Log($"🎭 Avatar cached from slot {slotIndex}: {(cachedCurrentAvatar != null ? cachedCurrentAvatar.name : "null")}");

                if (cachedCurrentAvatar != null)
                {
                    ApplyDefaultAOC(cachedCurrentAvatar);
                }
            }
        }

        /// <summary>
        /// アバタースロットクリア時のハンドラ。
        /// CCC の OnAvatarSlotCleared から呼ばれる。
        /// </summary>
        public void OnSlotCleared(int slotIndex)
        {
            cachedCurrentAvatar = null;
            cachedStateNames = null;
            currentPoseIndex = 0;
            Debug.Log($"🎭 Avatar cache cleared (slot {slotIndex} was cleared)");
        }

        /// <summary>
        /// AvatarLoadHandler 経由のロード完了時のハンドラ。
        /// CCC の OnAvatarLoadHandlerComplete から呼ばれる。
        /// </summary>
        public void OnLoadHandlerComplete(GameObject avatar)
        {
            cachedCurrentAvatar = avatar;
            currentPoseIndex = 0;
            cachedStateNames = null;
            ApplyDefaultAOC(avatar);
        }

        // ----- Private methods -----

        private void OnTopButton4Click()
        {
            tapCount++;
            Debug.Log($"🔘 topButton4 clicked - tapCount: {tapCount}");

            if (tapCount == 1)
            {
                // 1回目のタップ - 遅延処理を開始
                tapCts?.Cancel();
                tapCts = new System.Threading.CancellationTokenSource();
                HandleTapAsync(tapCts.Token).Forget();
            }
            // 2回目以降のタップはtapCountが増えるだけ（HandleTapAsyncで処理）
        }

        private async UniTaskVoid HandleTapAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                // ダブルタップ待機
                await UniTask.Delay((int)(DOUBLE_TAP_THRESHOLD * 1000), cancellationToken: ct);

                // 待機完了後、タップ数に応じて処理
                int finalTapCount = tapCount;
                tapCount = 0;  // リセット

                if (finalTapCount >= 2)
                {
                    // ダブルタップ
                    Debug.Log("🔘 Double tap detected! Switching OverrideController...");
                    TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
                    SwitchToNextOverrideController();
                }
                else
                {
                    // シングルタップ
                    Debug.Log("🔘 Single tap confirmed - Switching pose...");
                    TapticEngine.Selection();
                    SwitchToNextPose();
                }
            }
            catch (System.OperationCanceledException)
            {
                // キャンセルされた場合は何もしない
            }
        }

        private void SwitchToNextPose()
        {
            Debug.Log("🎭 SwitchToNextPose called");

            GameObject avatar = null;

            // 方法0: キャッシュされたアバターを使用（最優先）
            if (cachedCurrentAvatar != null && cachedCurrentAvatar.activeInHierarchy)
            {
                avatar = cachedCurrentAvatar;
                Debug.Log($"🎭 Using cached avatar: {avatar.name}");
            }

            // 方法1: AvatarSlotManager + AvatarMemoryCacheから取得
            if (avatar == null)
            {
                var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                var memoryCache = AvatarMemoryCache.Instance;

                if (slotManager != null && memoryCache != null)
                {
                    int currentSlot = slotManager.CurrentSlotIndex;
                    Debug.Log($"🎭 CurrentSlotIndex: {currentSlot}");

                    if (currentSlot >= 0)
                    {
                        avatar = memoryCache.GetCachedAvatar(currentSlot);
                        if (avatar != null)
                        {
                            cachedCurrentAvatar = avatar;  // キャッシュを更新
                        }
                        Debug.Log($"🎭 From MemoryCache: {(avatar != null ? avatar.name : "null")}");
                    }
                }
            }

            // 方法2: RuntimeFBXLoaderBridgeから取得（フォールバック）
            if (avatar == null)
            {
                if (fbxLoaderBridge == null)
                {
                    fbxLoaderBridge = Object.FindFirstObjectByType<AICam.FBXLoader.RuntimeFBXLoaderBridge>();
                }
                if (fbxLoaderBridge != null)
                {
                    avatar = fbxLoaderBridge.CurrentModel;
                    if (avatar != null)
                    {
                        cachedCurrentAvatar = avatar;  // キャッシュを更新
                    }
                    Debug.Log($"🎭 From RuntimeFBXLoaderBridge: {(avatar != null ? avatar.name : "null")}");
                }
            }

            // 方法3: シーン内のAnimatorを持つアクティブなアバターを検索（最終フォールバック）
            if (avatar == null)
            {
                var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
                foreach (var anim in animators)
                {
                    // Humanoidアバターを探す
                    if (anim.avatar != null && anim.avatar.isHuman && anim.gameObject.activeInHierarchy)
                    {
                        avatar = anim.gameObject;
                        cachedCurrentAvatar = avatar;  // キャッシュを更新
                        Debug.Log($"🎭 Found Humanoid avatar in scene: {avatar.name}");
                        break;
                    }
                }
            }

            Animator animator = null;
            if (avatar != null)
            {
                animator = avatar.GetComponent<Animator>();
            }
            Debug.Log($"🎭 avatar: {(avatar != null ? avatar.name : "null")}, animator: {animator != null}");

            if (avatar == null)
            {
                Debug.LogWarning("⚠️ No avatar placed");
                return;
            }

            if (animator == null)
            {
                animator = avatar.GetComponent<Animator>();
                if (animator == null)
                {
                    Debug.LogWarning("⚠️ Avatar has no Animator component");
                    return;
                }
            }

            // AnimatorControllerのClip一覧を取得
            var controller = animator.runtimeAnimatorController;
            Debug.Log($"🎭 runtimeAnimatorController: {(controller != null ? controller.name : "null")}");

            // OverrideControllerが設定されていない場合、b010を自動設定
            if (poseOverrideControllers != null && poseOverrideControllers.Length > 0 && poseOverrideControllers[0] != null)
            {
                bool isOverrideController = controller is AnimatorOverrideController;
                if (!isOverrideController)
                {
                    animator.runtimeAnimatorController = poseOverrideControllers[0];
                    controller = animator.runtimeAnimatorController;
                    currentOverrideIndex = 0;
                    currentPoseIndex = 0;
                    Debug.Log($"🎭 Auto-assigned OverrideController: {poseOverrideControllers[0].name}");
                }
            }

            if (controller == null)
            {
                Debug.LogWarning("⚠️ Animator has no RuntimeAnimatorController");
                return;
            }

            // PoseAnimatorControllerのState名は固定（Pose00〜Pose11）
            // ランタイムではAnimatorControllerのState名を直接取得できないため、固定配列を使用

            // 次のポーズインデックスに進む
            int previousIndex = currentPoseIndex;
            currentPoseIndex = (currentPoseIndex + 1) % POSE_COUNT;
            var targetState = $"Pose{currentPoseIndex:D2}";

            Debug.Log($"🎭 Pose: {targetState} ({currentPoseIndex + 1}/{POSE_COUNT})");

            // Pose11からPose00に戻った場合はアラートバーを表示
            if (previousIndex == POSE_COUNT - 1 && currentPoseIndex == 0)
            {
                showInfo?.Invoke("Pose", "Loop - Back to Pose00", 1.5f);
            }

            // State名で再生
            animator.Play(targetState, 0, 0f);
        }

        private void SwitchToNextOverrideController()
        {
            Debug.Log($"🎭 SwitchToNextOverrideController called");

            // PoseSlotControllerを使用
            if (poseSlotController != null)
            {
                // アバターが変わっている場合、PoseSlotControllerを更新
                EnsurePoseSlotControllerSetup();

                poseSlotController.NextOverride();
                currentOverrideIndex = poseSlotController.CurrentOverrideIndex;
                currentPoseIndex = 0;  // ポーズインデックスをリセット

                Debug.Log($"🎭 PoseSlotController.NextOverride() - index: {currentOverrideIndex}, name: {poseSlotController.CurrentOverrideName}");
                showInfo?.Invoke("Change", poseSlotController.CurrentOverrideName, 2f);
                return;
            }

            // フォールバック: PoseSlotControllerがない場合は従来の実装
            Debug.Log($"🎭 Fallback: poseOverrideControllers: {(poseOverrideControllers != null ? poseOverrideControllers.Length.ToString() : "null")}");

            if (poseOverrideControllers == null || poseOverrideControllers.Length == 0)
            {
                Debug.LogWarning("⚠️ No OverrideControllers configured - please set poseOverrideControllers in Inspector");
                return;
            }

            // アバター取得
            GameObject avatar = cachedCurrentAvatar;
            if (avatar == null || !avatar.activeInHierarchy)
            {
                // アバターを検索
                var animators = Object.FindObjectsByType<Animator>(FindObjectsSortMode.None);
                foreach (var anim in animators)
                {
                    if (anim.avatar != null && anim.avatar.isHuman && anim.gameObject.activeInHierarchy)
                    {
                        avatar = anim.gameObject;
                        cachedCurrentAvatar = avatar;
                        break;
                    }
                }
            }

            if (avatar == null)
            {
                Debug.LogWarning("⚠️ No avatar found for OverrideController switch");
                return;
            }

            var animator = avatar.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogWarning("⚠️ Avatar has no Animator component");
                return;
            }

            // 次のOverrideControllerに進む
            currentOverrideIndex = (currentOverrideIndex + 1) % poseOverrideControllers.Length;
            var nextOverride = poseOverrideControllers[currentOverrideIndex];

            if (nextOverride == null)
            {
                Debug.LogWarning($"⚠️ OverrideController at index {currentOverrideIndex} is null");
                return;
            }

            // OverrideControllerを適用
            var previousController = animator.runtimeAnimatorController;
            Debug.Log($"🎭 Before switch - current controller: {(previousController != null ? previousController.name : "null")}");

            animator.runtimeAnimatorController = nextOverride;

            Debug.Log($"🎭 After switch - new controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "null")}");

            // ポーズインデックスをリセットしてPose00を再生
            currentPoseIndex = 0;
            animator.Play("Pose00", 0, 0f);

            // State名キャッシュをクリア（新しいコントローラー用に再取得）
            cachedStateNames = null;

            Debug.Log($"🎭 Switched to OverrideController: {nextOverride.name} ({currentOverrideIndex + 1}/{poseOverrideControllers.Length})");

            // 水色のアラートバーで表示
            showInfo?.Invoke("Change", nextOverride.name, 2f);
        }

        private void EnsurePoseSlotControllerSetup()
        {
            if (poseSlotController == null) return;

            // 現在のアバターを取得
            GameObject avatar = GetCurrentAvatar();
            if (avatar == null) return;

            var animator = avatar.GetComponent<Animator>();
            if (animator == null) return;

            // TargetAnimatorが異なる場合は更新
            if (poseSlotController.TargetAnimator != animator)
            {
                Debug.Log($"🎭 Updating PoseSlotController.TargetAnimator to: {avatar.name}");
                poseSlotController.TargetAnimator = animator;

                // OverrideControllersを設定（未設定の場合）
                if (poseSlotController.OverrideCount == 0 && poseOverrideControllers != null)
                {
                    poseSlotController.SetOverrideControllers(poseOverrideControllers);
                    Debug.Log($"🎭 Set {poseOverrideControllers.Length} override controllers to PoseSlotController");
                }
            }
        }
    }
}
