using UnityEngine;
using UnityEngine.UIElements;
using Cysharp.Threading.Tasks;
using System;
using System.IO;
#if BLENDSHAPE_CONTROLLER
using DSGarage.BlendShape;
#endif

namespace AICam.UI
{
    /// <summary>
    /// 表情切り替えUI制御（シングルタップ→次の表情、ダブルタップ→リセット）を管理するコントローラー。
    /// VRM 1.0 / VRM 0.x / BlendshapeController SDK の3系統に対応。
    /// </summary>
    public class ExpressionUIController
    {
        private readonly bool enableDebugLogging;
        private const float DOUBLE_TAP_THRESHOLD = 0.3f;

        // 表情コントローラー（排他使用: いずれか1つのみ有効）
        private AICam.Expression.VrmExpressionSetup expressionSetup;
        private AICam.Expression.Vrm0ExpressionController vrm0ExpressionController;

#if BLENDSHAPE_CONTROLLER
        private ExpressionSetManager blendShapeExpressionManager;
#endif

        // 現在のアバター参照（動的セットアップ用）
        private GameObject currentAvatar;
        private int currentSlotIndex = -1;

        // ダブルタップ検出
        private int expressionTapCount = 0;
        private System.Threading.CancellationTokenSource expressionTapCts;

        public ExpressionUIController(
            VisualElement root,
            AICam.Expression.VrmExpressionSetup expressionSetup,
            bool enableDebugLogging)
        {
            this.expressionSetup = expressionSetup;
            this.enableDebugLogging = enableDebugLogging;

            var topButton3 = root.Q<Button>("topButton3");
            if (enableDebugLogging) Debug.Log($"🔘 topButton3: {(topButton3 != null ? "✅ found" : "❌ NOT FOUND")}");
            if (topButton3 != null)
            {
                topButton3.RegisterCallback<ClickEvent>(evt => OnTopButton3Click());
                if (enableDebugLogging) Debug.Log("✅ Top button 3 (Expression) click event registered");
            }
        }

        /// <summary>
        /// VRM表情システムをセットアップ。
        /// VRM 1.0 / VRM 0.x / BlendshapeController SDK の順に検出。
        /// </summary>
        public void SetupExpressionSystem(GameObject avatar, int slotIndex = -1)
        {
            if (avatar == null) return;

            Debug.Log($"[Expression] SetupExpressionSystem: Starting for {avatar.name}, slotIndex={slotIndex}");

            // 現在のアバター参照を保存
            currentAvatar = avatar;
            currentSlotIndex = slotIndex;

            // VRM 1.0を確認
            var vrm10Instance = avatar.GetComponent<UniVRM10.Vrm10Instance>();
            if (vrm10Instance != null)
            {
                Debug.Log($"[Expression] VRM 1.0 detected");
                SetupVrm10ExpressionSystem(avatar, vrm10Instance);
                return;
            }

            // VRM 0.xを確認
            var blendShapeProxy = avatar.GetComponent<global::VRM.VRMBlendShapeProxy>();
            if (blendShapeProxy != null)
            {
                Debug.Log($"[Expression] VRM 0.x detected");
                SetupVrm0ExpressionSystem(avatar, blendShapeProxy);
                return;
            }

            // Issue #471: キャッシュロード時のフォールバック（BlendshapeController SDK）
            Debug.Log($"[Expression] No VRM component, trying BlendShapeController fallback...");
#if BLENDSHAPE_CONTROLLER
            if (TrySetupBlendShapeExpressionSystem(avatar, slotIndex))
            {
                Debug.Log($"[Expression] ✅ BlendShapeController setup successful, manager={blendShapeExpressionManager != null}");
                return;
            }
            Debug.LogWarning($"[Expression] BlendShapeController setup failed");
#else
            Debug.LogWarning($"[Expression] BLENDSHAPE_CONTROLLER not defined");
#endif

            Debug.LogWarning($"[Expression] {avatar.name} - no expression support");
        }

        /// <summary>
        /// 表情アイコン生成をトリガー（Fire-and-forget）。
        /// SetupExpressionSystem の後に呼び出す。
        /// </summary>
        public void TriggerExpressionIconGeneration(GameObject avatar, int slotIndex)
        {
            if (avatar == null || slotIndex < 0) return;

            var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
            if (slotManager?.Cache == null) return;

            var avatarSlotData = slotManager.Cache.GetSlot(slotIndex);
            string avatarName = avatarSlotData?.avatarName ?? avatar.name;

            // 既にアイコンがある場合はスキップ
            if (avatarSlotData != null && avatarSlotData.HasExpressionIcons) return;

            Debug.Log($"🎨 TriggerExpressionIconGeneration: Starting for slot {slotIndex}, avatar={avatarName}");

            AICam.VRM.ExpressionIconService.Instance.GenerateForSlot(
                avatar,
                slotIndex,
                avatarName,
                onComplete: (folderPath) =>
                {
                    Debug.Log($"🎨 Expression icons generated for slot {slotIndex}: {folderPath}");

                    // AvatarSlotData を更新・永続化
                    var mgr = AICam.FBXLoader.AvatarSlotManager.Instance;
                    if (mgr?.Cache != null)
                    {
                        var slot = mgr.Cache.GetSlot(slotIndex);
                        if (slot != null)
                        {
                            slot.expressionIconFolderPath = folderPath;
                            mgr.Cache.UpdateSlot(slotIndex, slot);
                            mgr.Cache.SaveToFile();
                            Debug.Log($"🎨 Persisted expressionIconFolderPath for slot {slotIndex}");
                        }
                    }
                },
                onError: (error) =>
                {
                    Debug.LogWarning($"🎨 Expression icon generation failed for slot {slotIndex}: {error}");
                }
            );
        }

#if BLENDSHAPE_CONTROLLER
        /// <summary>
        /// VRM 表情メタデータをキャッシュに保存。
        /// VRM 1.0 / VRM 0.x 両方に対応。
        /// </summary>
        public void SaveExpressionDataToCache(GameObject avatar, string cacheId)
        {
            try
            {
                ExpressionSet expressionSet = null;

                // VRM 1.0 を確認
                var vrm10 = avatar.GetComponent<UniVRM10.Vrm10Instance>();
                if (vrm10 != null)
                {
                    Debug.Log($"[Expression] SaveExpressionDataToCache: VRM 1.0 detected");
                    if (AICam.VRM.VrmExpressionBridge.IsVRoidStudioAvatar(avatar))
                    {
                        expressionSet = AICam.VRM.VrmExpressionBridge.GetStandardExpressionSet();
                    }
                    else
                    {
                        expressionSet = AICam.VRM.VrmExpressionBridge.CreateExpressionSetFromVrm10(vrm10, avatar);
                    }
                }
                else
                {
                    // VRM 0.x を確認
                    var blendShapeProxy = avatar.GetComponent<global::VRM.VRMBlendShapeProxy>();
                    if (blendShapeProxy != null)
                    {
                        Debug.Log($"[Expression] SaveExpressionDataToCache: VRM 0.x detected");
                        expressionSet = AICam.VRM.VrmExpressionBridge.CreateExpressionSetFromVrm0(blendShapeProxy, avatar);
                    }
                    else
                    {
                        // VRoidStudio アバターの場合は標準表情セットを使用
                        if (AICam.VRM.VrmExpressionBridge.IsVRoidStudioAvatar(avatar))
                        {
                            Debug.Log($"[Expression] SaveExpressionDataToCache: VRoidStudio avatar detected");
                            expressionSet = AICam.VRM.VrmExpressionBridge.GetStandardExpressionSet();
                        }
                        else
                        {
                            Debug.LogWarning($"[Expression] SaveExpressionDataToCache: No VRM component found");
                            return;
                        }
                    }
                }

                if (expressionSet == null || expressionSet.Count == 0)
                {
                    Debug.LogWarning($"[Expression] SaveExpressionDataToCache: No expressions found");
                    return;
                }

                var collection = new ExpressionSetCollection
                {
                    collectionName = "VRM Expressions",
                    avatarName = avatar.name
                };
                collection.AddSet(expressionSet);

                string cacheDir = Path.Combine(Application.persistentDataPath, "AvatarCache", cacheId);
                string jsonPath = Path.Combine(cacheDir, "expressions.json");
                ExpressionSetSerializer.SaveCollection(collection, jsonPath);

                Debug.Log($"[Expression] Saved expression data to cache: {jsonPath} ({expressionSet.Count} expressions)");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Expression] Failed to save expression data: {e.Message}");
            }
        }
#endif

        /// <summary>
        /// スロット切り替え時の表情状態復元。
        /// ActivateSlotAvatar から呼ばれる。
        /// </summary>
        public void OnSlotActivated(GameObject avatar, int slotIndex = -1)
        {
            // 現在のアバター参照を更新
            currentAvatar = avatar;
            currentSlotIndex = slotIndex;

#if BLENDSHAPE_CONTROLLER
            // BlendshapeController をリセット
            blendShapeExpressionManager = null;

            // 切り替え先アバターの ExpressionSetManager を復元
            if (avatar != null)
            {
                var manager = avatar.GetComponent<ExpressionSetManager>();
                if (manager != null)
                {
                    blendShapeExpressionManager = manager;
                    expressionSetup = null;
                    vrm0ExpressionController = null;
                    Debug.Log($"[Expression] Restored BlendShape expression manager: {manager.Collection?.CurrentSet?.Count ?? 0} expressions");
                    return;
                }
            }
#endif
            // 表情マネージャーが見つからない場合はセットアップを試行
            if (avatar != null)
            {
                Debug.Log($"[Expression] OnSlotActivated: No existing manager, calling SetupExpressionSystem for slot {slotIndex}");
                SetupExpressionSystem(avatar, slotIndex);
            }
        }

        // ----- Private methods -----

        private void OnTopButton3Click()
        {
            expressionTapCount++;
            Debug.Log($"🔘 topButton3 clicked - expressionTapCount: {expressionTapCount}");

            if (expressionTapCount == 1)
            {
                // 1回目のタップ - 遅延処理を開始
                expressionTapCts?.Cancel();
                expressionTapCts = new System.Threading.CancellationTokenSource();
                HandleExpressionTapAsync(expressionTapCts.Token).Forget();
            }
            // 2回目以降のタップはexpressionTapCountが増えるだけ（HandleExpressionTapAsyncで処理）
        }

        private async UniTaskVoid HandleExpressionTapAsync(System.Threading.CancellationToken ct)
        {
            try
            {
                // ダブルタップ待機
                await UniTask.Delay((int)(DOUBLE_TAP_THRESHOLD * 1000), cancellationToken: ct);

                // 待機完了後、タップ数に応じて処理
                int finalTapCount = expressionTapCount;
                expressionTapCount = 0;  // リセット

                if (finalTapCount >= 2)
                {
                    // ダブルタップ → 表情リセット（Neutral）
                    Debug.Log("🔘 Double tap detected! Resetting expression to neutral...");
                    TapticEngine.Impact(TapticEngine.ImpactStyle.Medium);
                    ResetExpression();
                }
                else
                {
                    // シングルタップ → 次の表情
                    Debug.Log("🔘 Single tap confirmed - Switching expression...");
                    TapticEngine.Selection();
                    SwitchToNextExpression();
                }
            }
            catch (System.OperationCanceledException)
            {
                // キャンセルされた場合は何もしない
            }
        }

        private void SwitchToNextExpression()
        {
            Debug.Log($"😊 SwitchToNextExpression called - vrm0: {(vrm0ExpressionController != null ? "✅" : "❌")}, vrm10: {(expressionSetup != null ? "✅" : "❌")}");
#if BLENDSHAPE_CONTROLLER
            Debug.Log($"😊 BlendShapeManager: {(blendShapeExpressionManager != null ? "✅" : "❌")}");
#endif

            // VRM 0.xを優先チェック（より一般的）
            if (vrm0ExpressionController != null)
            {
                int indexBefore = vrm0ExpressionController.CurrentExpressionIndex;
                vrm0ExpressionController.NextExpression();
                int indexAfter = vrm0ExpressionController.CurrentExpressionIndex;
                Debug.Log($"😊 VRM 0.x Expression switched: {indexBefore} → {indexAfter}, Name: {vrm0ExpressionController.CurrentExpressionName}");
                return;
            }

            // VRM 1.0をチェック（SetupExpressionSystemで設定されたもののみ使用）
            if (expressionSetup != null)
            {
                var controller = expressionSetup.CurrentExpressionController;
                if (controller != null)
                {
                    int indexBefore = controller.CurrentExpressionIndex;
                    expressionSetup.NextExpression();
                    int indexAfter = controller.CurrentExpressionIndex;
                    Debug.Log($"😊 VRM 1.0 Expression switched: {indexBefore} → {indexAfter}, Name: {controller.CurrentExpressionName}");
                    return;
                }
                // コントローラーがnullの場合、expressionSetupをクリアして次へ
                Debug.Log($"😊 VRM 1.0 expressionSetup exists but controller is null, clearing");
                expressionSetup = null;
            }

            // Issue #471: BlendshapeController SDK フォールバック
#if BLENDSHAPE_CONTROLLER
            if (blendShapeExpressionManager != null)
            {
                int indexBefore = blendShapeExpressionManager.CurrentExpressionIndex;
                blendShapeExpressionManager.NextExpression();
                int indexAfter = blendShapeExpressionManager.CurrentExpressionIndex;
                var current = blendShapeExpressionManager.CurrentExpression;
                Debug.Log($"😊 BlendShape Expression switched: {indexBefore} -> {indexAfter}, Name: {current?.name}");
                return;
            }

            // 動的セットアップを試行
            if (currentAvatar != null)
            {
                Debug.Log($"😊 No controller available, attempting dynamic setup for {currentAvatar.name}");
                if (TrySetupBlendShapeExpressionSystem(currentAvatar, currentSlotIndex))
                {
                    if (blendShapeExpressionManager != null)
                    {
                        blendShapeExpressionManager.NextExpression();
                        var current = blendShapeExpressionManager.CurrentExpression;
                        Debug.Log($"😊 BlendShape Expression switched after dynamic setup: {current?.name}");
                        return;
                    }
                }
            }
#endif

            Debug.LogWarning("No expression controller available - load a VRM avatar first");
        }

        private void ResetExpression()
        {
            Debug.Log("😊 ResetExpression called");

            // VRM 0.xを優先チェック
            if (vrm0ExpressionController != null)
            {
                vrm0ExpressionController.ResetToNeutral();
                Debug.Log("😊 VRM 0.x Expression reset to neutral");
                return;
            }

            // VRM 1.0をチェック（SetupExpressionSystemで設定されたもののみ使用）
            if (expressionSetup != null)
            {
                var controller = expressionSetup.CurrentExpressionController;
                if (controller != null)
                {
                    expressionSetup.ResetExpression();
                    Debug.Log("😊 VRM 1.0 Expression reset to neutral");
                    return;
                }
                // コントローラーがnullの場合、expressionSetupをクリアして次へ
                expressionSetup = null;
            }

            // Issue #471: BlendshapeController SDK フォールバック
#if BLENDSHAPE_CONTROLLER
            if (blendShapeExpressionManager != null)
            {
                blendShapeExpressionManager.ResetAllBlendShapes();
                Debug.Log("😊 BlendShape Expression reset to neutral");
                return;
            }
#endif

            Debug.LogWarning("No expression controller available - load a VRM avatar first");
        }

        private void SetupVrm10ExpressionSystem(GameObject avatar, UniVRM10.Vrm10Instance vrmInstance)
        {
            // VrmExpressionSetupを検索、なければ作成
            if (expressionSetup == null)
            {
                expressionSetup = UnityEngine.Object.FindFirstObjectByType<AICam.Expression.VrmExpressionSetup>();

                if (expressionSetup == null)
                {
                    var setupObj = new GameObject("VrmExpressionSetup");
                    expressionSetup = setupObj.AddComponent<AICam.Expression.VrmExpressionSetup>();
                    Debug.Log($"🎭 SetupExpressionSystem: Created new VrmExpressionSetup for VRM 1.0");
                }
            }

            if (expressionSetup != null)
            {
                expressionSetup.OnVrmLoaded(avatar);

                var controller = expressionSetup.CurrentExpressionController;
                if (controller != null)
                {
                    Debug.Log($"🎭 SetupExpressionSystem: VRM 1.0 expression system ready, Available: {controller.AvailableExpressions.Count}");
                }
                else
                {
                    Debug.LogWarning($"🎭 SetupExpressionSystem: VRM 1.0 CurrentExpressionController is null");
                }
            }

            // VRM 0.xコントローラーをクリア
            vrm0ExpressionController = null;
        }

        private void SetupVrm0ExpressionSystem(GameObject avatar, global::VRM.VRMBlendShapeProxy blendShapeProxy)
        {
            // 既存のコントローラーを取得または追加
            vrm0ExpressionController = avatar.GetComponent<AICam.Expression.Vrm0ExpressionController>();
            if (vrm0ExpressionController == null)
            {
                vrm0ExpressionController = avatar.AddComponent<AICam.Expression.Vrm0ExpressionController>();
            }

            vrm0ExpressionController.SetBlendShapeProxy(blendShapeProxy);

            Debug.Log($"🎭 SetupExpressionSystem: VRM 0.x expression system ready, Available: {vrm0ExpressionController.AvailableExpressions.Count}");

            // VRM 1.0セットアップをクリア
            expressionSetup = null;
        }

#if BLENDSHAPE_CONTROLLER
        /// <summary>
        /// キャッシュロードされたアバター用の BlendshapeController SDK フォールバック。
        /// 1. expressions.json があればそれを使用
        /// 2. なければアバターのブレンドシェイプから直接構築（VRoidStudio アバターの場合）
        /// </summary>
        private bool TrySetupBlendShapeExpressionSystem(GameObject avatar, int slotIndex)
        {
            Debug.Log($"[Expression] TrySetupBlendShapeExpressionSystem: avatar={avatar.name}, slotIndex={slotIndex}");

            // expressions.json からの読み込みを試行
            string jsonPath = null;
            string cacheDir = null;
            if (slotIndex >= 0)
            {
                var slotManager = AICam.FBXLoader.AvatarSlotManager.Instance;
                Debug.Log($"[Expression] SlotManager: {(slotManager != null ? "exists" : "null")}, Cache: {(slotManager?.Cache != null ? "exists" : "null")}");

                if (slotManager?.Cache != null)
                {
                    var slotData = slotManager.Cache.GetSlot(slotIndex);
                    Debug.Log($"[Expression] SlotData for {slotIndex}: {(slotData != null ? "exists" : "null")}, binaryCacheId: {slotData?.binaryCacheId ?? "null"}");

                    if (slotData != null && !string.IsNullOrEmpty(slotData.binaryCacheId))
                    {
                        cacheDir = Path.Combine(Application.persistentDataPath, "AvatarCache", slotData.binaryCacheId);
                        jsonPath = Path.Combine(cacheDir, "expressions.json");
                        Debug.Log($"[Expression] jsonPath: {jsonPath}, exists: {File.Exists(jsonPath)}");
                    }
                }
            }
            else
            {
                Debug.Log($"[Expression] slotIndex is negative ({slotIndex}), cannot look up expressions.json");
            }

            // パス1: expressions.json が存在する場合
            if (jsonPath != null && File.Exists(jsonPath))
            {
                try
                {
                    string json = File.ReadAllText(jsonPath);
                    if (SetupBlendShapeManager(avatar, json))
                    {
                        Debug.Log($"[Expression] BlendShape setup from expressions.json: {blendShapeExpressionManager.Collection?.CurrentSet?.Count ?? 0} expressions");
                        return true;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Expression] Failed to load expressions.json: {e.Message}");
                }
            }

            // パス2: expressions.json がない場合、アバターから直接構築
            Debug.Log("[Expression] No expressions.json found, attempting direct blendshape scan...");
            ExpressionSet expressionSet = null;

            if (AICam.VRM.VrmExpressionBridge.IsVRoidStudioAvatar(avatar))
            {
                expressionSet = AICam.VRM.VrmExpressionBridge.GetStandardExpressionSet();
                Debug.Log("[Expression] VRoidStudio avatar detected, using standard expression set");
            }

            if (expressionSet == null || expressionSet.Count == 0)
            {
                Debug.Log("[Expression] Cannot build expression set from avatar blendshapes");
                return false;
            }

            // ExpressionSetCollection を構築
            var collection = new ExpressionSetCollection
            {
                collectionName = "VRM Expressions",
                avatarName = avatar.name
            };
            collection.AddSet(expressionSet);

            // JSON にシリアライズしてマネージャーにロード
            string collectionJson = ExpressionSetSerializer.ToJson(collection);
            if (!SetupBlendShapeManager(avatar, collectionJson))
            {
                return false;
            }

            // 次回のために expressions.json を保存
            if (cacheDir != null)
            {
                try
                {
                    string savePath = Path.Combine(cacheDir, "expressions.json");
                    ExpressionSetSerializer.SaveCollection(collection, savePath);
                    Debug.Log($"[Expression] Saved generated expressions.json to cache: {savePath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Expression] Failed to save expressions.json: {e.Message}");
                }
            }

            Debug.Log($"[Expression] BlendShape setup from avatar scan: {expressionSet.Count} expressions");
            return true;
        }

        private bool SetupBlendShapeManager(GameObject avatar, string json)
        {
            try
            {
                var manager = avatar.GetComponent<ExpressionSetManager>();
                if (manager == null)
                {
                    manager = avatar.AddComponent<ExpressionSetManager>();
                }

                manager.SetTargetAvatar(avatar);
                manager.LoadCollectionFromJson(json);

                if (manager.Collection != null && manager.Collection.SetCount > 0)
                {
                    manager.SwitchSet(0);
                }

                blendShapeExpressionManager = manager;
                expressionSetup = null;
                vrm0ExpressionController = null;

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Expression] Failed to setup BlendShape manager: {e.Message}");
                return false;
            }
        }
#endif
    }
}
