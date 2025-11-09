using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// AR平面へのタップ検出とアバター配置の制御
/// 責任: タップ検出・平面選択・各マネージャーへの委譲のみ
/// インスタンス生成、追従、スワイプ操作は各専用マネージャーが担当
/// </summary>
[RequireComponent(typeof(ARRaycastManager))]
public sealed class PlaceAvatarOnPlaneOnly : MonoBehaviour
{
    [Header("Avatar Managers")]
    [SerializeField] private AICam.VRM.AvatarInstanceManager avatarInstanceManager;
    [SerializeField] private AICam.VRM.AvatarFollowController avatarFollowController;
    [SerializeField] private AICam.VRM.AvatarSwipeController avatarSwipeController;

    [Header("AR Managers")]
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private AROcclusionManager occlusionManager;

    [Header("UI Managers")]
    [SerializeField] private FaceUIManager faceUIManager;
    [SerializeField] private ExpressionGridLayout expressionGridLayout;
    [SerializeField] private PoseGridLayout poseGridLayout;
    [SerializeField] private AICam.UI.CameraCaptureController cameraCaptureController;

    [Header("Filters")]
    [Tooltip("水平面（床・テーブルなど）に限定")]
    [SerializeField] private bool onlyHorizontal = true;
    [Tooltip("対応端末では床分類の平面に限定（未対応端末では無視）")]
    [SerializeField] private bool onlyFloorIfAvailable = false;

    [Header("UI touch block")]
    [Tooltip("この Canvas 上の UI（例: Capture ボタン）をタップしたときは配置を無効化する")]
    [SerializeField] private Canvas uiCanvas;
    [SerializeField] private List<RectTransform> touchBlockAreas = new();

    [Header("Double Tap Settings")]
    [Tooltip("アバターをダブルタップで追従モードを切り替える")]
    [SerializeField] private bool enableFollowMode = true;
    [Tooltip("ダブルタップの最大間隔（秒）")]
    [SerializeField] private float doubleTapInterval = 0.3f;

    [Header("Visual Feedback")]
    [SerializeField] private Color defaultPlaneColor = new Color(0.0f, 0.8f, 1.0f, 0.2f);
    [SerializeField] private Color planeLockedColor = new Color(1f, 0.6f, 0.2f, 0.3f);
    [SerializeField] private Color cameraLockedColor = new Color(0.6f, 0.4f, 1f, 0.3f);

    [Header("Occlusion Settings")]
    [SerializeField] private bool desiredOcclusionOn = true;
    [SerializeField] private EnvironmentDepthMode envDepthModeOn = EnvironmentDepthMode.Best;
    [SerializeField] private int occlusionWarmupFrames = 2;

    private static readonly List<ARRaycastHit> s_Hits = new();
    private ARRaycastManager rcMgr;
    private Camera arCamera;

    // ダブルタップ検出用
    private float lastTapTime = -1f;
    private Vector2 lastTapPosition;

    // オクルージョン制御用
    private Coroutine occlusionApplyCoroutine;

    void Awake()
    {
        Debug.Log("[PlaceAvatarOnPlaneOnly] Awake - Initializing...");

        rcMgr = GetComponent<ARRaycastManager>();
        arCamera = Camera.main;

        // 自動参照取得
        if (!avatarInstanceManager) avatarInstanceManager = FindFirstObjectByType<AICam.VRM.AvatarInstanceManager>(FindObjectsInactive.Include);
        if (!avatarFollowController) avatarFollowController = FindFirstObjectByType<AICam.VRM.AvatarFollowController>(FindObjectsInactive.Include);
        if (!avatarSwipeController) avatarSwipeController = FindFirstObjectByType<AICam.VRM.AvatarSwipeController>(FindObjectsInactive.Include);
        if (!planeManager) planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        if (!occlusionManager) occlusionManager = FindFirstObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
        if (!faceUIManager) faceUIManager = FindFirstObjectByType<FaceUIManager>(FindObjectsInactive.Include);
        if (!expressionGridLayout) expressionGridLayout = FindFirstObjectByType<ExpressionGridLayout>(FindObjectsInactive.Include);
        if (!poseGridLayout) poseGridLayout = FindFirstObjectByType<PoseGridLayout>(FindObjectsInactive.Include);
        if (!cameraCaptureController) cameraCaptureController = FindFirstObjectByType<AICam.UI.CameraCaptureController>(FindObjectsInactive.Include);

        // 床寄りにしたい場合は検出を水平に絞る
        if (planeManager) planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

        // オクルージョンの初期設定
        if (occlusionManager)
        {
            SetOcclusionModesImmediate(false);
            Debug.Log("[PlaceAvatarOnPlaneOnly] Occlusion manager initialized");
        }

        // 平面の追加/更新イベントを購読
        if (planeManager)
        {
            planeManager.planesChanged += OnPlanesChanged;
        }

        Debug.Log("[PlaceAvatarOnPlaneOnly] Initialized successfully");
    }

    void OnEnable()
    {
        // オクルージョン状態をサブシステム起動後に適用
        if (occlusionManager)
        {
            occlusionApplyCoroutine = StartCoroutine(ApplyOcclusionWhenReady());
        }
    }

    void Update()
    {
        // タッチ入力がない場合は早期リターン
        if (Input.touchCount == 0) return;

        var touch = Input.GetTouch(0);

        // スワイプ処理（追従モード中のみ）
        if (avatarSwipeController != null && avatarFollowController != null &&
            avatarFollowController.CurrentMode != AICam.VRM.AvatarFollowController.FollowMode.Off)
        {
            avatarSwipeController.HandleSwipe(touch.position, touch.phase);
        }

        // タップ開始以外は無視
        if (touch.phase != TouchPhase.Began) return;

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Touch detected at {touch.position}");

        // Main画面以外は無視
        if (UIMgr.instance != null && UIMgr.instance.State != UIMgr.UIState.Home)
        {
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Touch ignored - not in Home state");
            return;
        }

        // UI上のタップは無視
        if (IsTouchOverUI(touch))
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Touch ignored - over UI");
            return;
        }

        // ダブルタップ検出（追従モード切替）
        if (enableFollowMode && avatarInstanceManager != null && avatarInstanceManager.CurrentInstance != null)
        {
            if (CheckDoubleTap(touch.position))
            {
                Debug.Log("[PlaceAvatarOnPlaneOnly] Double tap detected! Toggling follow mode...");
                ToggleFollowMode();
                return;
            }
        }

        // 追従モード中はシングルタップでの配置をキャンセル
        if (avatarFollowController != null && avatarFollowController.CurrentMode != AICam.VRM.AvatarFollowController.FollowMode.Off)
        {
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Single tap ignored - Follow mode is active");
            return;
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Single tap - checking for plane");

        // 平面へのRaycast
        if (!rcMgr.Raycast(touch.position, s_Hits, TrackableType.PlaneWithinPolygon))
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] No plane hit detected");
            return;
        }

        var hit = s_Hits[0];
        var plane = planeManager ? planeManager.GetPlane(hit.trackableId) : hit.trackable as ARPlane;
        if (!plane)
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Plane reference not found");
            return;
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Plane hit: {plane.trackableId}, alignment: {plane.alignment}");

        // 平面フィルタリング
        if (!IsPlaneValid(plane))
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Plane filtered out");
            return;
        }

        // アバターをインスタンス化して配置
        InstantiateAvatarAt(hit.pose, plane);
    }

    void OnDisable()
    {
        // イベント購読を解除
        if (planeManager)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }

        // オクルージョン適用コルーチンを停止
        if (occlusionApplyCoroutine != null)
        {
            StopCoroutine(occlusionApplyCoroutine);
            occlusionApplyCoroutine = null;
        }
    }

    void OnDestroy()
    {
        // イベント購読を解除（念のため）
        if (planeManager)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    void OnApplicationPause(bool pauseStatus)
    {
        // アプリ復帰時にオクルージョン状態を再適用
        if (!pauseStatus && occlusionManager && isActiveAndEnabled)
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] App resumed, reapplying occlusion state");
            if (occlusionApplyCoroutine != null)
            {
                StopCoroutine(occlusionApplyCoroutine);
            }
            occlusionApplyCoroutine = StartCoroutine(ApplyOcclusionWhenReady());
        }
    }

    /// <summary>
    /// 平面が有効かどうかチェック
    /// </summary>
    private bool IsPlaneValid(ARPlane plane)
    {
        // 水平面フィルター
        if (onlyHorizontal && !(plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown))
        {
            return false;
        }

        // 床分類フィルター
        if (onlyFloorIfAvailable)
        {
            bool supportsClass = planeManager && planeManager.descriptor != null && planeManager.descriptor.supportsClassification;
            if (supportsClass)
            {
                var labels = plane.classifications;
                if ((labels & PlaneClassifications.Floor) == 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// アバターを指定位置にインスタンス化して配置
    /// </summary>
    private void InstantiateAvatarAt(Pose pose, ARPlane plane)
    {
        if (avatarInstanceManager == null)
        {
            Debug.LogWarning("[PlaceAvatarOnPlaneOnly] AvatarInstanceManager not assigned!");
            return;
        }

        // AvatarInstanceManagerでインスタンス生成
        var avatar = avatarInstanceManager.InstantiateAt(pose, plane);

        if (avatar == null)
        {
            Debug.LogWarning("[PlaceAvatarOnPlaneOnly] Failed to instantiate avatar - no template set?");
            return;
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Avatar instantiated: {avatar.name}");

        // 追従コントローラーに対象を設定
        if (avatarFollowController != null)
        {
            avatarFollowController.SetTarget(avatar, plane);
        }

        // スワイプコントローラーに対象を設定
        if (avatarSwipeController != null)
        {
            avatarSwipeController.SetTarget(avatar);
        }

        // UI初期化
        InitializeUI(avatar);

        // オクルージョンを有効化
        SetOcclusion(true);
    }

    /// <summary>
    /// UIを初期化
    /// </summary>
    private void InitializeUI(GameObject avatar)
    {
        if (faceUIManager != null)
        {
            faceUIManager.InitializeWithAvatar(avatar);
        }

        if (expressionGridLayout != null)
        {
            var faceController = avatarInstanceManager?.CurrentFaceController;
            expressionGridLayout.SetTargetController(faceController);
        }

        if (poseGridLayout != null)
        {
            var animator = avatarInstanceManager?.CurrentAnimator;
            poseGridLayout.SetTargetAnimator(animator);
        }
    }

    /// <summary>
    /// ダブルタップを検出
    /// </summary>
    private bool CheckDoubleTap(Vector2 position)
    {
        float currentTime = Time.time;

        if (currentTime - lastTapTime <= doubleTapInterval &&
            Vector2.Distance(lastTapPosition, position) < 50f)
        {
            lastTapTime = -1f; // リセット
            return true;
        }

        lastTapTime = currentTime;
        lastTapPosition = position;
        return false;
    }

    /// <summary>
    /// 追従モードを切り替え
    /// </summary>
    private void ToggleFollowMode()
    {
        if (avatarFollowController == null) return;

        avatarFollowController.ToggleMode();

        // 視覚フィードバック: 平面の色を変更
        Color color = GetCurrentModeColor();
        SetPlaneColor(color);

        // オクルージョン制御
        bool occlusionOn = avatarFollowController.CurrentMode != AICam.VRM.AvatarFollowController.FollowMode.CameraLocked;
        SetOcclusion(occlusionOn);
    }

    /// <summary>
    /// 現在のモードに応じた平面の色を取得
    /// </summary>
    private Color GetCurrentModeColor()
    {
        if (avatarFollowController == null)
        {
            return defaultPlaneColor;
        }

        switch (avatarFollowController.CurrentMode)
        {
            case AICam.VRM.AvatarFollowController.FollowMode.Off:
                return defaultPlaneColor;
            case AICam.VRM.AvatarFollowController.FollowMode.PlaneLocked:
                return planeLockedColor;
            case AICam.VRM.AvatarFollowController.FollowMode.CameraLocked:
                return cameraLockedColor;
            default:
                return defaultPlaneColor;
        }
    }

    /// <summary>
    /// タッチがUI上にあるかチェック
    /// </summary>
    private bool IsTouchOverUI(Touch touch)
    {
        // EventSystemチェック
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
            return true;

        // 明示登録したRect
        var cam = uiCanvas ? uiCanvas.worldCamera : null;
        foreach (var rt in touchBlockAreas)
        {
            if (!rt) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, touch.position, cam))
                return true;
        }

        // UI Toolkit パネル
        if (cameraCaptureController != null && cameraCaptureController.IsPointOverUIPanel(touch.position))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// 平面の追加/更新イベントハンドラー
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        Color currentColor = GetCurrentModeColor();

        // 新しく追加された平面に色を適用
        foreach (var plane in args.added)
        {
            ApplyColorToPlane(plane, currentColor);
        }

        // 更新された平面にも色を再適用
        foreach (var plane in args.updated)
        {
            ApplyColorToPlane(plane, currentColor);
        }
    }

    /// <summary>
    /// 平面に色を適用
    /// </summary>
    private void ApplyColorToPlane(ARPlane plane, Color color)
    {
        var meshRenderer = plane.GetComponent<MeshRenderer>();
        if (meshRenderer && meshRenderer.material)
        {
            meshRenderer.material.color = color;
        }
    }

    /// <summary>
    /// 全ての平面に色を設定
    /// </summary>
    private void SetPlaneColor(Color color)
    {
        if (!planeManager) return;

        foreach (var plane in planeManager.trackables)
        {
            ApplyColorToPlane(plane, color);
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Plane color set to: {color}");
    }

    /// <summary>
    /// オクルージョンを設定
    /// </summary>
    private void SetOcclusion(bool enabled)
    {
        if (!occlusionManager)
        {
            Debug.LogWarning("[PlaceAvatarOnPlaneOnly] OcclusionManager is null");
            return;
        }

        desiredOcclusionOn = enabled;
        Debug.Log($"[PlaceAvatarOnPlaneOnly] SetOcclusion({enabled})");

        if (occlusionApplyCoroutine != null)
        {
            StopCoroutine(occlusionApplyCoroutine);
        }
        occlusionApplyCoroutine = StartCoroutine(ApplyOcclusionWhenReady());
    }

    /// <summary>
    /// オクルージョンをサブシステム起動後に適用
    /// </summary>
    private System.Collections.IEnumerator ApplyOcclusionWhenReady()
    {
        // warmupフレーム待機
        for (int i = 0; i < occlusionWarmupFrames; i++)
        {
            yield return null;
        }

        if (!occlusionManager)
        {
            yield break;
        }

        // 希望状態を適用
        SetOcclusionModesImmediate(desiredOcclusionOn);

        // 適用確認
        yield return null;
        yield return null;

        // リトライ（1回のみ）
        if (desiredOcclusionOn)
        {
            var currentMode = occlusionManager.currentEnvironmentDepthMode;
            if (currentMode == EnvironmentDepthMode.Disabled && envDepthModeOn != EnvironmentDepthMode.Disabled)
            {
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Occlusion not applied, retrying...");
                SetOcclusionModesImmediate(true);

                yield return null;
                yield return null;
                currentMode = occlusionManager.currentEnvironmentDepthMode;
                Debug.Log($"[PlaceAvatarOnPlaneOnly] After retry: {currentMode}");
            }
        }

        occlusionApplyCoroutine = null;
    }

    /// <summary>
    /// オクルージョンモードを即座に設定
    /// </summary>
    private void SetOcclusionModesImmediate(bool enabled)
    {
        if (!occlusionManager) return;

        if (enabled)
        {
            occlusionManager.requestedEnvironmentDepthMode = envDepthModeOn;
            occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
            occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Occlusion ON: {envDepthModeOn}");
        }
        else
        {
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
            occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
            occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
            Debug.Log("[PlaceAvatarOnPlaneOnly] Occlusion OFF");
        }
    }
}
