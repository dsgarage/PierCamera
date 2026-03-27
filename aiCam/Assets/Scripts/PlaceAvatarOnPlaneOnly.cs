using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using AICam.Core;
using Cysharp.Threading.Tasks;
using PierCamera.Analytics;

/// <summary>
/// ARプレーン上にアバターを配置し、追従モードやジェスチャー操作を管理するコンポーネント。
///
/// ## v0.8.0 変更履歴
/// - Issue #477: ジェスチャー状態管理を追加
///   - GestureState enum でピンチ/スワイプ/長押し状態を明示的に管理
///   - ピンチ終了後のクールダウン期間 (POST_PINCH_COOLDOWN) を追加
///   - タップ判定に移動距離閾値 (TAP_DISTANCE_THRESHOLD) を追加
/// </summary>
[RequireComponent(typeof(ARRaycastManager))]
public sealed class PlaceAvatarOnPlaneOnly : MonoBehaviour, IAvatarPlacer
{
    [Header("Prefab")]
    [SerializeField] GameObject avatarPrefab;

    [Header("Runtime Avatar Loader")]
    [SerializeField] AICam.VRM.RuntimeAvatarLoader avatarLoader;

    [Header("Managers")]
    [SerializeField] ARPlaneManager planeManager;
    [SerializeField] ARAnchorManager anchorManager;   // 任意（安定化用）
    [SerializeField] AROcclusionManager occlusionManager;
    [SerializeField] FaceUIManager faceUIManager;
    [SerializeField] ExpressionGridLayout expressionGridLayout;
    [SerializeField] PoseGridLayout poseGridLayout;
    [SerializeField] AICam.UI.CameraCaptureController cameraCaptureController;  // UI Toolkit パネルのタッチブロック用

    [Header("Filters")]
    [Tooltip("水平面（床・テーブルなど）に限定")]
    [SerializeField] bool onlyHorizontal = true;
    [Tooltip("対応端末では“床”分類の平面に限定（未対応端末では無視）")]
    [SerializeField] bool onlyFloorIfAvailable = false;

    [Header("Camera / Facing")]
    [Tooltip("AR カメラ（未指定なら Camera.main を使用）")]
    [SerializeField] Camera arCamera;
    [Tooltip("配置時にアバターをカメラの方向に向ける")]
    [SerializeField] bool faceCameraOnPlace = true;

    [Header("UI touch block")]
    [Tooltip("この Canvas 上の UI（例: Capture ボタン）をタップしたときは配置を無効化する")]
    [SerializeField] Canvas uiCanvas;                                  // ScreenPoint判定用
    [SerializeField] List<RectTransform> touchBlockAreas = new();      // Capture ボタンなどの RectTransform を登録

    [Header("Avatar Follow (追従機能)")]
    [Tooltip("アバターをダブルタップで追従モードを切り替える")]
    [SerializeField] bool enableFollowMode = true;
    [Tooltip("ダブルタップの最大間隔（秒）")]
    [SerializeField] float doubleTapInterval = 0.3f;
    [Tooltip("維持する距離（メートル）")]
    [SerializeField] float followDistance = 1.5f;
    [Tooltip("追従の滑らかさ（0-1）")]
    [SerializeField] float followSmoothness = 0.15f;
    [Tooltip("デバッグログを表示")]
    [SerializeField] bool enableDebugLog = true;

    [Header("Avatar Interaction (アバター操作)")]
    [Tooltip("スワイプで距離調整を有効化")]
    [SerializeField] bool enableSwipeDistance = true;
    [Tooltip("スワイプで回転を有効化")]
    [SerializeField] bool enableSwipeRotation = true;
    [Tooltip("上下スワイプの距離感度（ピクセル/メートル）")]
    [SerializeField] float swipeDistanceSensitivity = 200f;  // 200ピクセルで1m
    [Tooltip("左右スワイプの回転感度（度/ピクセル）")]
    [SerializeField] float swipeRotationSensitivity = 0.3f;  // 1ピクセルで0.3度
    [Tooltip("距離の最小値（メートル）")]
    [SerializeField] float minDistance = 0.5f;
    [Tooltip("距離の最大値（メートル）")]
    [SerializeField] float maxDistance = 5.0f;

    [Header("Avatar Scale (スケール調整) - Issue #395")]
    [Tooltip("ピンチでスケール調整を有効化")]
    [SerializeField] bool enablePinchScale = true;
    [Tooltip("スケールの最小値")]
    [SerializeField] float minScale = 0.1f;
    [Tooltip("スケールの最大値")]
    [SerializeField] float maxScale = 3.0f;
    [Tooltip("ピンチ感度")]
    [SerializeField] float pinchScaleSensitivity = 1.0f;

    [Header("Avatar Position Drag (位置ドラッグ) - Issue #395")]
    [Tooltip("[紫]モードで長押し+ドラッグで位置調整を有効化")]
    [SerializeField] bool enableLongPressDrag = true;
    [Tooltip("長押し判定時間（秒）")]
    [SerializeField] float longPressThreshold = 0.3f;
    [Tooltip("ドラッグ感度")]
    [SerializeField] float dragPositionSensitivity = 0.003f;

    static readonly List<ARRaycastHit> s_Hits = new();
    ARRaycastManager rcMgr;
    GameObject avatar;

    /// <summary>IAvatarPlacer実装: 配置済みアバターへのアクセス</summary>
    public GameObject PlacedAvatar { get => avatar; set => avatar = value; }

    ARPlane avatarPlane; // アバターが配置された平面
    FaceController avatarFaceController;
    Animator avatarAnimator;

    // 追従モード
    enum FollowMode { Off, PlaneLocked, CameraLocked }
    FollowMode currentFollowMode = FollowMode.CameraLocked; // Issue #422: 紫モードをデフォルトに

    // Issue #477: ジェスチャー状態管理
    enum GestureState { None, Tapping, Swiping, Pinching, LongPressing }
    GestureState currentGestureState = GestureState.None;
    float gestureStateChangedTime = 0f;
    const float POST_PINCH_COOLDOWN = 0.15f;  // ピンチ終了後のクールダウン（秒）
    const float TAP_DISTANCE_THRESHOLD = 20f; // タップ判定の最大移動距離（ピクセル）
    Vector2 touchStartPosition;               // タッチ開始位置（タップ判定用）
    Vector3 cameraLocalOffset; // CameraLocked用のオフセット
    float lastTapTime = -1f;
    Vector2 lastTapPosition;

    // スワイプ操作用
    bool isSwipeActive = false;
    Vector2 swipeStartPosition;
    float avatarRotationY = 0f;  // アバターのY軸回転（手動調整分）

    // Issue #395: ピンチスケール用
    float previousPinchDistance = 0f;
    float currentAvatarScale = 1.0f;

    // Issue #395: 長押しドラッグ用
    bool isLongPressActive = false;
    float touchStartTime = 0f;
    Vector2 longPressStartPosition;

    // 視覚フィードバック用
    Color defaultPlaneColor = new Color(0.0f, 0.8f, 1.0f, 0.2f);  // 薄い水色（シアン）
    Color planeLockedColor = new Color(1f, 0.6f, 0.2f, 0.3f);  // 薄いオレンジ
    Color cameraLockedColor = new Color(0.6f, 0.4f, 1f, 0.3f);  // 薄い紫

    // オクルージョン制御用（ARFoundation 6.3 仕様準拠）
    bool desiredOcclusionOn = true;  // 希望するオクルージョン状態
    EnvironmentDepthMode envDepthModeOn = EnvironmentDepthMode.Best;  // ON時の環境深度モード
    int occlusionWarmupFrames = 2;  // サブシステム初期化待ちフレーム数
    Coroutine occlusionApplyCoroutine;

    void Awake()
    {
        // 起動確認ログ（enableDebugLogがtrueの場合のみ）
        if (enableDebugLog) Debug.Log($"[PlaceAvatarOnPlaneOnly] Awake - Debug logging enabled: {enableDebugLog}");

        rcMgr = GetComponent<ARRaycastManager>();
        // Issue #427: 以下のFindFirstObjectByType呼び出しは起動時間に影響するため、
        // Inspectorでの参照設定を推奨。未設定時のフォールバックとして残す。
        if (!planeManager) planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        if (!anchorManager) anchorManager = FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
        if (!occlusionManager) occlusionManager = FindFirstObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
        if (!faceUIManager) faceUIManager = FindFirstObjectByType<FaceUIManager>(FindObjectsInactive.Include);
        if (!poseGridLayout) poseGridLayout = FindFirstObjectByType<PoseGridLayout>(FindObjectsInactive.Include);
        if (!cameraCaptureController) cameraCaptureController = FindFirstObjectByType<AICam.UI.CameraCaptureController>(FindObjectsInactive.Include);
        // 床寄りにしたい場合は検出を水平に絞る（※壁検出を抑制）
        if (planeManager) planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

        // ARカメラ未指定なら自動取得
        if (!arCamera) arCamera = Camera.main;

        // Issue #473: LiDARなし端末ではオクルージョンを無効化
        bool hasLiDAR = DeviceAnalytics.HasLiDAR();
        if (!hasLiDAR)
        {
            desiredOcclusionOn = false;
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Device does NOT have LiDAR - disabling occlusion features");
        }
        else
        {
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Device has LiDAR - occlusion features enabled");
        }

        // オクルージョンの初期設定（ARFoundation 6.3 仕様: enabled は触らず requested のみ制御）
        if (occlusionManager)
        {
            // 起動時は一旦全て Disabled に（サブシステム起動後に OnEnable で適用）
            SetOcclusionModesImmediate(false);
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Occlusion manager found, initial modes set to Disabled");
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Occlusion manager enabled: {occlusionManager.enabled}");
        }
        else
        {
            Debug.LogWarning("[PlaceAvatarOnPlaneOnly] AROcclusionManager not found - occlusion control will not work");
        }

        // 平面の追加/更新イベントを購読
        if (planeManager)
        {
            planeManager.planesChanged += OnPlanesChanged;
            Debug.Log($"[PlaceAvatarOnPlaneOnly] ARPlaneManager found - enabled: {planeManager.enabled}, detectionMode: {planeManager.requestedDetectionMode}");
        }
        else
        {
            Debug.LogError("[PlaceAvatarOnPlaneOnly] ARPlaneManager NOT FOUND! Plane detection will not work.");
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Initialized - FollowMode: {enableFollowMode}, Distance: {followDistance}m");
    }

    void Start()
    {
        // Issue #473: 平面検知の状態を定期的にログ出力
        StartCoroutine(LogPlaneDetectionStatus());
    }

    System.Collections.IEnumerator LogPlaneDetectionStatus()
    {
        // ARSession起動を待つ
        yield return new WaitForSeconds(1.0f);

        // 初回ログ
        LogPlaneManagerState("Initial check (1s after start)");

        // 3秒後に再度チェック
        yield return new WaitForSeconds(2.0f);
        LogPlaneManagerState("Second check (3s after start)");

        // 5秒後に再度チェック
        yield return new WaitForSeconds(2.0f);
        LogPlaneManagerState("Third check (5s after start)");
    }

    void LogPlaneManagerState(string context)
    {
        // ARSessionの状態を確認
        var arSession = FindFirstObjectByType<ARSession>();
        string sessionState = arSession != null ? ARSession.state.ToString() : "ARSession not found";

        if (!planeManager)
        {
            Debug.LogError($"[PlaceAvatarOnPlaneOnly] {context}: ARPlaneManager is NULL! ARSession.state={sessionState}");
            return;
        }

        int planeCount = 0;
        foreach (var _ in planeManager.trackables)
        {
            planeCount++;
        }

        // ARPlaneManagerのサブシステム状態も確認
        bool hasSubsystem = planeManager.subsystem != null;
        bool subsystemRunning = hasSubsystem && planeManager.subsystem.running;

        Debug.Log($"[PlaceAvatarOnPlaneOnly] {context}: " +
                  $"ARSession.state={sessionState}, " +
                  $"ARPlaneManager.enabled={planeManager.enabled}, " +
                  $"subsystem={hasSubsystem}, running={subsystemRunning}, " +
                  $"detectionMode={planeManager.requestedDetectionMode}, " +
                  $"trackedPlanes={planeCount}");
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
        if (!avatar)
        {
            if (avatarFaceController)
            {
                avatarFaceController = null;
                expressionGridLayout?.SetTargetController(null);
            }

            if (avatarAnimator)
            {
                avatarAnimator = null;
                poseGridLayout?.SetTargetAnimator(null);
            }
            currentFollowMode = FollowMode.Off; // アバターがないときはOff
            isSwipeActive = false; // スワイプもリセット
        }
        else
        {
            // 追従モード更新
            UpdateFollowMode();
        }

        // Issue #477: ジェスチャー状態の更新
        UpdateGestureState();

        // Issue #395, #429: ピンチスケール（[青]Off + [橙]PlaneLocked + [紫]CameraLockedモード、全モードで有効）
        if (enablePinchScale && avatar && Input.touchCount == 2)
        {
            currentGestureState = GestureState.Pinching;
            HandlePinchScale();
        }
        else
        {
            previousPinchDistance = 0f; // ピンチ解除時にリセット
        }

        // Issue #395: 長押しドラッグ（[紫]CameraLockedモード）
        if (enableLongPressDrag && avatar && currentFollowMode == FollowMode.CameraLocked && Input.touchCount == 1)
        {
            HandleLongPressDrag();
        }
        else if (Input.touchCount != 1)
        {
            isLongPressActive = false; // タッチ解除時にリセット
        }

        // Issue #395: [青]Offモードでの回転操作
        if (currentFollowMode == FollowMode.Off && avatar && Input.touchCount == 1)
        {
            HandleOffModeRotation();
        }

        // スワイプ操作の処理（[橙]PlaneLocked + [紫]CameraLockedモード時のみ）
        if ((currentFollowMode == FollowMode.PlaneLocked || currentFollowMode == FollowMode.CameraLocked)
            && avatar && Input.touchCount == 1)
        {
            HandleSwipeInteraction();
        }

        if (Input.touchCount == 0)
        {
            isSwipeActive = false;
            // Issue #477: タッチがなくなったらジェスチャー状態をリセット
            if (currentGestureState != GestureState.None)
            {
                gestureStateChangedTime = Time.time;
                currentGestureState = GestureState.None;
            }
            return;
        }

        var touch = Input.GetTouch(0);

        // Issue #477: タッチ開始位置を記録
        if (touch.phase == TouchPhase.Began)
        {
            touchStartPosition = touch.position;
        }

        // Issue #477: ピンチ終了直後はタップ/スワイプを無視
        if (currentGestureState == GestureState.None &&
            Time.time - gestureStateChangedTime < POST_PINCH_COOLDOWN)
        {
            if (enableDebugLog)
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Touch ignored - post-pinch cooldown ({Time.time - gestureStateChangedTime:F2}s < {POST_PINCH_COOLDOWN}s)");
            return;
        }

        if (touch.phase != TouchPhase.Began) return;

        // Issue #477: タップ判定には移動距離チェックを追加
        float touchMovement = Vector2.Distance(touch.position, touchStartPosition);
        if (touchMovement > TAP_DISTANCE_THRESHOLD)
        {
            if (enableDebugLog)
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Touch ignored - movement too large ({touchMovement:F0}px > {TAP_DISTANCE_THRESHOLD}px)");
            return;
        }

        // タップ検出ログ（常に出力）
        Debug.Log($"[PlaceAvatarOnPlaneOnly] Touch detected at {touch.position}, phase: {touch.phase}");

        // Main画面以外は無視
        if (UIMgr.instance.State != UIMgr.UIState.Home)
        {
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Touch ignored - not in Home state (current: {UIMgr.instance.State})");
            return;
        }

        // UI 上のタップは必ず無視（EventSystem か、明示登録したRectに入っていたら弾く）
        if (IsTouchOverUI(touch))
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Touch ignored - over UI");
            return;
        }

        // UI上のタップは無視
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Touch ignored - EventSystem detected UI");
            return;
        }

        // ダブルタップ検出（追従モード切替）
        if (enableFollowMode && avatar && CheckDoubleTap(touch.position))
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Double tap detected! Toggling follow mode...");
            ToggleFollowMode();
            return; // ダブルタップ時は配置しない
        }

        // PlaneLocked/CameraLockedモード中はワンタップでの配置をキャンセル
        if (currentFollowMode != FollowMode.Off)
        {
            Debug.Log($"[PlaceAvatarOnPlaneOnly] Single tap ignored - Follow mode is active ({currentFollowMode})");
            return;
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Single tap - checking for plane at {touch.position}");


        // 1) 平面ポリゴン内だけにRaycast
        if (!rcMgr.Raycast(touch.position, s_Hits, TrackableType.PlaneWithinPolygon))
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] No plane hit detected at tap position");
            return; // ← 平面外をタップ → 何もしない
        }

        var hit = s_Hits[0];
        var plane = planeManager ? planeManager.GetPlane(hit.trackableId) : hit.trackable as ARPlane;
        if (!plane)
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Plane reference not found");
            return;
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Plane hit detected: {plane.trackableId}, alignment: {plane.alignment}");

        // 2) 追加フィルタ（任意）
        if (onlyHorizontal && !(plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown))
            return; // 水平以外（壁や斜面）は拒否

        bool supportsClass = planeManager && planeManager.descriptor != null
                     && planeManager.descriptor.supportsClassification;

        if (onlyFloorIfAvailable)
        {
            if (supportsClass)
            {
                // Floor フラグが含まれていなければ不許可
                var labels = plane.classifications;
                if ((labels & PlaneClassifications.Floor) == 0)
                    return;
            }
            // 分類非対応端末はスキップ（＝従来どおり置く）
        }

        var pose = hit.pose;

        // 3) （任意）アンカーで固定してブレ低減
        Transform parent = null;
        if (anchorManager && plane)
        {
            var anchor = anchorManager.AttachAnchor(plane, pose);
            if (anchor) parent = anchor.transform;
        }

        // 4) 生成 or 位置更新（ここでカメラ方向を向かせる）
        var rot = faceCameraOnPlace ? GetFaceCameraRotation(pose.position, plane.alignment) : pose.rotation;

        if (!avatar)
        {
            // RuntimeAvatarLoaderにロード済みアバターがあればそれを使用、なければPrefabを使用
            GameObject avatarToPlace = GetAvatarToPlace();

            if (avatarToPlace != null)
            {
                // VRMアバターの場合は既に存在するGameObjectを配置
                if (avatarLoader != null && avatarLoader.CurrentAvatar == avatarToPlace)
                {
                    avatar = avatarToPlace;
                    avatar.SetActive(true); // VRMを表示
                    avatar.transform.SetParent(parent);
                    avatar.transform.SetPositionAndRotation(pose.position, rot);
                    Debug.Log($"[PlaceAvatarOnPlaneOnly] VRM Avatar placed at {pose.position}");
                }
                else
                {
                    // Prefabの場合はインスタンス化
                    avatar = Instantiate(avatarToPlace, pose.position, rot, parent);
                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Prefab Avatar placed at {pose.position}");
                }

                avatarPlane = plane; // 配置した平面を記憶
                // Issue #422: 紫モード（CameraLocked）をデフォルトに
                currentFollowMode = FollowMode.CameraLocked;

                // CameraLocked初期化: カメラ相対オフセットを計算
                if (arCamera && avatar)
                {
                    Vector3 offset = avatar.transform.position - arCamera.transform.position;
                    followDistance = offset.magnitude;
                    cameraLocalOffset = Quaternion.Inverse(arCamera.transform.rotation) * offset;
                }
                SetPlaneColor(cameraLockedColor);
                SetOcclusion(false);

                BindAvatarFaceController();

                // HUDを起動
                faceUIManager?.InitializeWithAvatar(avatar);

                Debug.Log($"[PlaceAvatarOnPlaneOnly] Avatar placed. Tap twice to toggle follow mode.");
            }
            else
            {
                Debug.LogWarning("[PlaceAvatarOnPlaneOnly] No avatar to place (avatarPrefab and VRM avatar are both null)");
            }
        }
        else
        {
            avatar.transform.SetPositionAndRotation(pose.position, rot);
            avatar.transform.SetParent(parent);
            avatarPlane = plane; // 再配置時も平面を更新
            // Issue #422: 紫モード（CameraLocked）をデフォルトに
            currentFollowMode = FollowMode.CameraLocked;
            if (arCamera && avatar)
            {
                Vector3 offset = avatar.transform.position - arCamera.transform.position;
                followDistance = offset.magnitude;
                cameraLocalOffset = Quaternion.Inverse(arCamera.transform.rotation) * offset;
            }
            SetPlaneColor(cameraLockedColor);
            SetOcclusion(false);
            if (!avatarFaceController || !avatarAnimator)
                BindAvatarFaceController();

            Debug.Log($"[PlaceAvatarOnPlaneOnly] Avatar repositioned to {pose.position}. Follow mode reset to Off.");
        }
    }

    // RuntimeAvatarLoaderにロード済みアバターがあればそれを返し、なければPrefabを返す
    GameObject GetAvatarToPlace()
    {
        // Priority 1: RuntimeAvatarLoader has loaded avatar
        if (avatarLoader != null && avatarLoader.CurrentAvatar != null)
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Using avatar from RuntimeAvatarLoader");
            return avatarLoader.CurrentAvatar;
        }

        // Priority 2: Fallback to prefab
        if (avatarPrefab != null)
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Using avatarPrefab");
        }
        return avatarPrefab;
    }

    // アバターを「カメラの方向」に向ける回転を作る
    // ・水平面上に投影して自然な向きを維持（Y軸でスピンしない）
    // ・下向き水平面（天井）に誤って置くケースも一応考慮
    Quaternion GetFaceCameraRotation(Vector3 placePos, PlaneAlignment alignment)
    {
        var cam = arCamera ? arCamera.transform : null;
        if (!cam) return Quaternion.identity;

        // 平面のUpベクトル（通常は世界のUpを使う）
        Vector3 up = Vector3.up;
        if (alignment == PlaneAlignment.HorizontalDown) up = -Vector3.up;

        // カメラ方向ベクトル（水平面に投影）
        Vector3 toCam = cam.position - placePos;
        // 「上下の傾き成分」を落として、水平面上の向きだけを採用
        toCam -= Vector3.Dot(toCam, up) * up;

        if (toCam.sqrMagnitude < 1e-6f)
        {
            // ほぼ同一点/真上真下などで向きが出せない時のフォールバック
            // カメラの前方を同様に投影して使う
            Vector3 fwd = cam.forward - Vector3.Dot(cam.forward, up) * up;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            toCam = fwd.normalized;
        }
        else
        {
            toCam.Normalize();
        }

        return Quaternion.LookRotation(toCam, up);
    }

    // UIヒット判定（EventSystem + 指定Rect + UI Toolkit）
    bool IsTouchOverUI(Touch touch)
    {
        // 1) 標準の UI ヒット（GraphicRaycaster 必須）
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId))
            return true;

        // 2) 明示登録した Rect（Capture ボタンなど）にヒットしているかを矩形で判定
        //    Screen Space - Overlay なら camera は null でOK
        var cam = uiCanvas ? uiCanvas.worldCamera : null;
        for (int i = 0; i < touchBlockAreas.Count; i++)
        {
            var rt = touchBlockAreas[i];
            if (!rt) continue;
            if (RectTransformUtility.RectangleContainsScreenPoint(rt, touch.position, cam))
                return true;
        }

        // 3) UI Toolkit のパネル判定
        if (cameraCaptureController != null && cameraCaptureController.IsPointOverUIPanel(touch.position))
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] Touch ignored - over UI Toolkit panel");
            return true;
        }

        return false;
    }

    void BindAvatarFaceController()
    {
        avatarFaceController = avatar ? avatar.GetComponent<FaceController>() : null;
        avatarAnimator = avatar ? avatar.GetComponent<Animator>() : null;
        expressionGridLayout?.SetTargetController(avatarFaceController);
        poseGridLayout?.SetTargetAnimator(avatarAnimator);
    }

    void OnDisable()
    {
        avatarFaceController = null;
        expressionGridLayout?.SetTargetController(null);
        avatarAnimator = null;
        poseGridLayout?.SetTargetAnimator(null);

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

    // ========== Issue #477: ジェスチャー状態管理 ==========

    /// <summary>
    /// Issue #477: ジェスチャー状態を更新
    /// ピンチ終了後のクールダウンや状態遷移を管理
    /// </summary>
    void UpdateGestureState()
    {
        int touchCount = Input.touchCount;

        // ピンチ中 → 1指以下になったらクールダウン開始
        if (currentGestureState == GestureState.Pinching && touchCount < 2)
        {
            gestureStateChangedTime = Time.time;
            currentGestureState = GestureState.None;
            if (enableDebugLog)
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Gesture: Pinching → None (cooldown started)");
        }

        // スワイプ中 → タッチがなくなったらリセット
        if (currentGestureState == GestureState.Swiping && touchCount == 0)
        {
            gestureStateChangedTime = Time.time;
            currentGestureState = GestureState.None;
            if (enableDebugLog)
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Gesture: Swiping → None");
        }

        // 長押し中 → タッチがなくなったらリセット
        if (currentGestureState == GestureState.LongPressing && touchCount == 0)
        {
            gestureStateChangedTime = Time.time;
            currentGestureState = GestureState.None;
            if (enableDebugLog)
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Gesture: LongPressing → None");
        }
    }

    // ========== 追従機能 ==========

    bool CheckDoubleTap(Vector2 position)
    {
        float currentTime = Time.time;

        if (currentTime - lastTapTime <= doubleTapInterval &&
            Vector2.Distance(lastTapPosition, position) < 50f) // 50ピクセル以内
        {
            lastTapTime = -1f; // リセット
            return true; // ダブルタップ検出
        }

        lastTapTime = currentTime;
        lastTapPosition = position;
        return false;
    }

    void ToggleFollowMode()
    {
        // Issue #429: Off → CameraLocked → PlaneLocked → Off（紫モードの利用頻度が高いため順序変更）
        switch (currentFollowMode)
        {
            case FollowMode.Off:
                currentFollowMode = FollowMode.CameraLocked;
                avatarRotationY = 0f;  // 回転リセット

                // カメラ相対オフセットを計算（距離と角度を保持）
                if (arCamera && avatar)
                {
                    Vector3 camPos = arCamera.transform.position;
                    Vector3 avatarPos = avatar.transform.position;
                    Vector3 offset = avatarPos - camPos;

                    // 現在の3D距離を保存
                    followDistance = offset.magnitude;

                    // カメラのローカル座標系でのオフセットを保存（正規化しない）
                    Quaternion invCamRot = Quaternion.Inverse(arCamera.transform.rotation);
                    cameraLocalOffset = invCamRot * offset;

                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Follow Mode: CameraLocked (カメラ追従) - Distance: {followDistance:F2}m, Offset: {cameraLocalOffset}");
                }

                // 視覚フィードバック: 平面を紫に、オクルージョンOFF
                SetPlaneColor(cameraLockedColor);
                SetOcclusion(false);

                break;
            case FollowMode.CameraLocked:
                currentFollowMode = FollowMode.PlaneLocked;
                avatarRotationY = 0f;  // 回転リセット

                // 現在の水平距離を保存
                if (arCamera && avatar)
                {
                    Vector3 camPos = arCamera.transform.position;
                    Vector3 avatarPos = avatar.transform.position;
                    float horizontalDist = Vector3.Distance(
                        new Vector3(camPos.x, 0, camPos.z),
                        new Vector3(avatarPos.x, 0, avatarPos.z)
                    );
                    followDistance = Mathf.Max(minDistance, horizontalDist);
                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Follow Mode: PlaneLocked (平面追従) - Locked horizontal distance: {followDistance:F2}m");
                }

                // 視覚フィードバック: 平面をオレンジに、オクルージョンON
                SetPlaneColor(planeLockedColor);
                SetOcclusion(true);

                break;
            case FollowMode.PlaneLocked:
                currentFollowMode = FollowMode.Off;

                // 視覚フィードバック: 平面をデフォルトに、オクルージョンを元に戻す
                SetPlaneColor(defaultPlaneColor);
                SetOcclusion(true);

                Debug.Log("[PlaceAvatarOnPlaneOnly] Follow Mode: Off (固定)");
                break;
        }
    }

    void UpdateFollowMode()
    {
        if (currentFollowMode == FollowMode.Off || !avatar || !arCamera)
            return;

        if (currentFollowMode == FollowMode.PlaneLocked)
        {
            UpdatePlaneLocked();
        }
        else if (currentFollowMode == FollowMode.CameraLocked)
        {
            UpdateCameraLocked();
        }
    }

    void UpdatePlaneLocked()
    {
        if (!avatarPlane)
            return;

        Vector3 camPos = arCamera.transform.position;
        Vector3 camForwardFlat = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up).normalized;

        if (camForwardFlat.sqrMagnitude < 0.01f)
            camForwardFlat = Vector3.forward;

        // カメラから followDistance 離れた位置
        Vector3 targetPosFlat = camPos + camForwardFlat * followDistance;

        // 平面上に投影
        Vector3 planeCenter = avatarPlane.center;
        Vector3 planeNormal = avatarPlane.normal;
        float distance = Vector3.Dot(planeNormal, targetPosFlat - planeCenter);
        Vector3 targetPos = targetPosFlat - planeNormal * distance;

        // 滑らかに移動
        avatar.transform.position = Vector3.Lerp(avatar.transform.position, targetPos, followSmoothness);

        if (enableDebugLog && Time.frameCount % 30 == 0) // 30フレームごとにログ
        {
            float currentDistance = Vector3.Distance(camPos, avatar.transform.position);
            float horizontalDistance = Vector3.Distance(
                new Vector3(camPos.x, 0, camPos.z),
                new Vector3(avatar.transform.position.x, 0, avatar.transform.position.z)
            );

            // オクルージョン状態も確認
            string occlusionStatus = "N/A";
            if (occlusionManager)
            {
                occlusionStatus = $"Requested={occlusionManager.requestedEnvironmentDepthMode}, Current={occlusionManager.currentEnvironmentDepthMode}";
            }

            Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaneLocked: Distance={currentDistance:F2}m (target={followDistance:F2}m), Horizontal={horizontalDistance:F2}m, Occlusion={occlusionStatus}");
        }

        // カメラを向く（手動回転を考慮）
        Vector3 lookDir = camPos - avatar.transform.position;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion baseLookRot = Quaternion.LookRotation(lookDir);
            if (Mathf.Abs(avatarRotationY) > 0.1f)
            {
                // 手動回転が設定されている場合はそれを適用
                Quaternion manualRot = Quaternion.Euler(0, avatarRotationY, 0);
                avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot * manualRot, followSmoothness);
            }
            else
            {
                avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot, followSmoothness);
            }
        }
    }

    void UpdateCameraLocked()
    {
        if (!arCamera || !avatar)
            return;

        Vector3 camPos = arCamera.transform.position;
        // カメラの完全な回転を使用（pitch/yaw/roll全て対応）
        Quaternion camRot = arCamera.transform.rotation;

        // カメラ相対オフセットをそのまま使用（角度を保持）
        // スワイプで距離調整された場合は、オフセットをスケーリング
        float currentOffsetLength = cameraLocalOffset.magnitude;
        Vector3 offset = cameraLocalOffset;
        if (currentOffsetLength > 0.01f && Mathf.Abs(followDistance - currentOffsetLength) > 0.01f)
        {
            // スワイプで距離が変更された場合は、方向を保ったままスケーリング
            offset = cameraLocalOffset.normalized * followDistance;
        }

        Vector3 targetPos = camPos + (camRot * offset);

        // CameraLockedモードではより強くカメラに追従（smoothnessを高く）
        float cameraLockSmoothness = Mathf.Min(followSmoothness * 3f, 0.8f);
        avatar.transform.position = Vector3.Lerp(avatar.transform.position, targetPos, cameraLockSmoothness);

        if (enableDebugLog && Time.frameCount % 30 == 0) // 30フレームごとにログ
        {
            float currentDistance = Vector3.Distance(camPos, avatar.transform.position);
            Vector3 camEuler = arCamera.transform.eulerAngles;
            Debug.Log($"[PlaceAvatarOnPlaneOnly] CameraLocked: Distance={currentDistance:F2}m (target={followDistance:F2}m), CamRot=({camEuler.x:F1}°, {camEuler.y:F1}°, {camEuler.z:F1}°), Offset={offset.magnitude:F2}m");
        }

        // カメラを向く（手動回転を考慮）
        Vector3 lookDir = camPos - avatar.transform.position;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion baseLookRot = Quaternion.LookRotation(lookDir);
            if (Mathf.Abs(avatarRotationY) > 0.1f)
            {
                // 手動回転が設定されている場合はそれを適用
                Quaternion manualRot = Quaternion.Euler(0, avatarRotationY, 0);
                avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot * manualRot, cameraLockSmoothness);
            }
            else
            {
                avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, baseLookRot, cameraLockSmoothness);
            }
        }
    }

    // ========== スワイプインタラクション ==========

    void HandleSwipeInteraction()
    {
        if (Input.touchCount == 0) return;

        Touch touch = Input.GetTouch(0);

        // UI上のタッチは無視
        if (IsTouchOverUI(touch)) return;
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return;

        switch (touch.phase)
        {
            case TouchPhase.Began:
                // Issue #477: ピンチ直後はスワイプを開始しない
                if (Time.time - gestureStateChangedTime < POST_PINCH_COOLDOWN)
                {
                    isSwipeActive = false;
                    return;
                }
                // スワイプ開始
                isSwipeActive = true;
                swipeStartPosition = touch.position;
                break;

            case TouchPhase.Moved:
                if (!isSwipeActive) return;

                // [紫]CameraLockedモードで長押しドラッグ中は回転処理をスキップ（位置調整中）
                if (currentFollowMode == FollowMode.CameraLocked && isLongPressActive)
                {
                    swipeStartPosition = touch.position; // 位置だけ更新
                    return;
                }

                Vector2 delta = touch.position - swipeStartPosition;

                // Issue #429: 上下スワイプ: 距離調整（[橙]PlaneLockedモードのみ有効、[紫]CameraLockedモードでは無効）
                if (enableSwipeDistance && currentFollowMode == FollowMode.PlaneLocked && Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
                {
                    // Issue #477: スワイプ状態に設定
                    currentGestureState = GestureState.Swiping;

                    // 上にスワイプ(+Y) = 遠くに、下にスワイプ(-Y) = 近くに
                    float distanceDelta = delta.y / swipeDistanceSensitivity;
                    followDistance = Mathf.Clamp(followDistance + distanceDelta, minDistance, maxDistance);

                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Swipe distance adjust: {followDistance:F2}m (delta: {distanceDelta:F2}m)");
                }
                // 左右スワイプ: 回転（[橙][紫]両方で有効、ただし[紫]長押し中は除く）
                else if (enableSwipeRotation && Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                {
                    // Issue #477: スワイプ状態に設定
                    currentGestureState = GestureState.Swiping;

                    float rotationDelta = -delta.x * swipeRotationSensitivity;
                    avatarRotationY += rotationDelta;

                    // -180〜180度に正規化
                    while (avatarRotationY > 180f) avatarRotationY -= 360f;
                    while (avatarRotationY < -180f) avatarRotationY += 360f;

                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Swipe rotation adjust: {avatarRotationY:F1}° (delta: {rotationDelta:F1}°)");
                }

                // 次のフレーム用に更新
                swipeStartPosition = touch.position;
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                isSwipeActive = false;
                break;
        }
    }

    // ========== Issue #395, #429: ピンチスケール ==========

    /// <summary>
    /// 2本指ピンチでアバターのスケールを調整
    /// [青]Off + [橙]PlaneLocked + [紫]CameraLockedモード全てで有効
    /// </summary>
    void HandlePinchScale()
    {
        if (Input.touchCount != 2 || !avatar) return;

        Touch touch0 = Input.GetTouch(0);
        Touch touch1 = Input.GetTouch(1);

        // UI上のタッチは無視
        if (IsTouchOverUI(touch0) || IsTouchOverUI(touch1)) return;

        // 現在の2点間の距離
        float currentPinchDistance = Vector2.Distance(touch0.position, touch1.position);

        if (previousPinchDistance > 0f)
        {
            // ピンチの変化量を計算
            float pinchDelta = currentPinchDistance - previousPinchDistance;
            float scaleDelta = pinchDelta * 0.001f * pinchScaleSensitivity;

            // スケールを更新
            currentAvatarScale = Mathf.Clamp(currentAvatarScale + scaleDelta, minScale, maxScale);
            avatar.transform.localScale = Vector3.one * currentAvatarScale;

            if (enableDebugLog && Mathf.Abs(scaleDelta) > 0.001f)
            {
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Pinch scale: {currentAvatarScale:F2} (delta: {scaleDelta:F3})");
            }
        }

        previousPinchDistance = currentPinchDistance;
    }

    // ========== Issue #395, #429: 長押しドラッグ ==========

    /// <summary>
    /// 長押し(0.3秒)+ドラッグでアバターの画面内位置を調整
    /// [紫]CameraLockedモードで有効
    /// </summary>
    void HandleLongPressDrag()
    {
        if (Input.touchCount != 1 || !avatar || !arCamera) return;

        Touch touch = Input.GetTouch(0);

        // UI上のタッチは無視
        if (IsTouchOverUI(touch)) return;

        switch (touch.phase)
        {
            case TouchPhase.Began:
                touchStartTime = Time.time;
                longPressStartPosition = touch.position;
                isLongPressActive = false;
                break;

            case TouchPhase.Stationary:
                // 長押し判定
                if (!isLongPressActive && Time.time - touchStartTime >= longPressThreshold)
                {
                    isLongPressActive = true;
                    // Issue #477: 長押し状態に設定
                    currentGestureState = GestureState.LongPressing;
                    Debug.Log("[PlaceAvatarOnPlaneOnly] Long press detected - drag to adjust position");
                }
                break;

            case TouchPhase.Moved:
                if (isLongPressActive)
                {
                    // ドラッグ量を計算（デルタを直接使用）
                    Vector2 dragDelta = touch.deltaPosition;

                    // スクリーン座標をカメラローカル座標に変換
                    // X: カメラの右方向（水平のみ）
                    // Y: カメラの前方向（水平、画面上方向=前進）
                    Vector3 camForward = arCamera.transform.forward;
                    camForward.y = 0;
                    camForward.Normalize();

                    Vector3 camRight = arCamera.transform.right;
                    camRight.y = 0;
                    camRight.Normalize();

                    // スクリーン座標のY（上下）を前後移動に、X（左右）を左右移動に
                    Vector3 worldDelta = (camRight * dragDelta.x + camForward * dragDelta.y) * dragPositionSensitivity;

                    // cameraLocalOffsetに加算（カメラローカル空間に変換）
                    Vector3 localDelta = Quaternion.Inverse(arCamera.transform.rotation) * worldDelta;
                    cameraLocalOffset += localDelta;

                    // オフセットを制限（暴走防止）
                    float maxOffset = 5f; // 最大5m
                    if (cameraLocalOffset.magnitude > maxOffset)
                    {
                        cameraLocalOffset = cameraLocalOffset.normalized * maxOffset;
                    }

                    if (enableDebugLog && worldDelta.magnitude > 0.001f)
                    {
                        Debug.Log($"[PlaceAvatarOnPlaneOnly] Drag position: offset={cameraLocalOffset}, worldDelta={worldDelta.magnitude:F3}");
                    }
                }
                else if (Time.time - touchStartTime >= longPressThreshold)
                {
                    // 移動中に長押し時間が経過した場合も有効化
                    isLongPressActive = true;
                    Debug.Log("[PlaceAvatarOnPlaneOnly] Long press activated during move");
                }
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                isLongPressActive = false;
                break;
        }
    }

    // ========== Issue #395: Offモード回転 ==========

    /// <summary>
    /// [青]Offモードでも左右スワイプでアバターを回転
    /// アンカー位置でその場回転
    /// </summary>
    void HandleOffModeRotation()
    {
        if (Input.touchCount != 1 || !avatar) return;
        if (!enableSwipeRotation) return;

        // Issue #477: ピンチ直後は無視
        if (Time.time - gestureStateChangedTime < POST_PINCH_COOLDOWN) return;

        Touch touch = Input.GetTouch(0);

        // UI上のタッチは無視
        if (IsTouchOverUI(touch)) return;
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return;

        switch (touch.phase)
        {
            case TouchPhase.Began:
                swipeStartPosition = touch.position;
                break;

            case TouchPhase.Moved:
                Vector2 delta = touch.position - swipeStartPosition;

                // 左右スワイプで回転
                if (Mathf.Abs(delta.x) > 5f) // 最小閾値
                {
                    float rotationDelta = -delta.x * swipeRotationSensitivity;

                    // アバターを直接回転
                    avatar.transform.Rotate(0, rotationDelta, 0, Space.World);

                    if (enableDebugLog && Mathf.Abs(rotationDelta) > 0.5f)
                    {
                        Debug.Log($"[PlaceAvatarOnPlaneOnly] Off mode rotation: {avatar.transform.eulerAngles.y:F1}° (delta: {rotationDelta:F1}°)");
                    }

                    swipeStartPosition = touch.position;
                }
                break;
        }
    }

    // ========== 視覚フィードバック ==========

    void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // 新しく追加された平面に現在のモードの色を適用
        Color currentColor = GetCurrentModeColor();

        foreach (var plane in args.added)
        {
            var meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer)
            {
                Material mat = meshRenderer.material;
                if (mat)
                {
                    mat.color = currentColor;
                    Debug.Log($"[PlaceAvatarOnPlaneOnly] New plane {plane.trackableId} color set to: {currentColor}");
                }
            }
        }

        // 更新された平面にも色を再適用（念のため）
        foreach (var plane in args.updated)
        {
            var meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer)
            {
                Material mat = meshRenderer.material;
                if (mat && mat.color != currentColor)
                {
                    mat.color = currentColor;
                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Updated plane {plane.trackableId} color set to: {currentColor}");
                }
            }
        }
    }

    Color GetCurrentModeColor()
    {
        switch (currentFollowMode)
        {
            case FollowMode.Off:
                return defaultPlaneColor;
            case FollowMode.PlaneLocked:
                return planeLockedColor;
            case FollowMode.CameraLocked:
                return cameraLockedColor;
            default:
                return defaultPlaneColor;
        }
    }

    void SetPlaneColor(Color color)
    {
        if (!planeManager) return;

        // 全ての検出済み平面の色を変更
        foreach (var plane in planeManager.trackables)
        {
            var meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer)
            {
                // マテリアルのインスタンスを取得して色を変更
                // .materialを使用することで各平面ごとのマテリアルインスタンスが作成される
                Material mat = meshRenderer.material;
                if (mat)
                {
                    mat.color = color;
                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Plane {plane.trackableId} color set to: {color}");
                }
            }
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] All plane colors changed to: {color}");
    }

    void SetOcclusion(bool enabled)
    {
        if (!occlusionManager)
        {
            Debug.LogWarning("[PlaceAvatarOnPlaneOnly] OcclusionManager is null - cannot change occlusion");
            return;
        }

        // 希望状態を記録
        desiredOcclusionOn = enabled;
        Debug.Log($"[PlaceAvatarOnPlaneOnly] SetOcclusion({enabled}) - Desired state recorded");

        // コルーチンでサブシステム起動を待って適用
        if (occlusionApplyCoroutine != null)
        {
            StopCoroutine(occlusionApplyCoroutine);
        }
        occlusionApplyCoroutine = StartCoroutine(ApplyOcclusionWhenReady());
    }

    System.Collections.IEnumerator ApplyOcclusionWhenReady()
    {
        // warmupフレーム待機（サブシステム初期化待ち）
        for (int i = 0; i < occlusionWarmupFrames; i++)
        {
            yield return null;
        }

        if (!occlusionManager)
        {
            Debug.LogWarning("[PlaceAvatarOnPlaneOnly] OcclusionManager is null after warmup");
            yield break;
        }

        // サブシステムの起動確認
        var subsystem = occlusionManager.subsystem;
        if (subsystem == null || !subsystem.running)
        {
            Debug.LogWarning($"[PlaceAvatarOnPlaneOnly] Occlusion subsystem not running (subsystem: {subsystem != null}, running: {subsystem?.running})");
            // サブシステムが起動していない場合でも、requested を設定（起動後に反映される）
        }

        // 希望状態を適用
        SetOcclusionModesImmediate(desiredOcclusionOn);

        // 適用確認のため2フレーム待機
        yield return null;
        yield return null;

        // 適用確認とリトライ（1回のみ）
        if (desiredOcclusionOn)
        {
            var currentMode = occlusionManager.currentEnvironmentDepthMode;
            if (currentMode == EnvironmentDepthMode.Disabled && envDepthModeOn != EnvironmentDepthMode.Disabled)
            {
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Occlusion not applied (current: {currentMode}), retrying once...");
                SetOcclusionModesImmediate(true);

                // 再確認
                yield return null;
                yield return null;
                currentMode = occlusionManager.currentEnvironmentDepthMode;
                Debug.Log($"[PlaceAvatarOnPlaneOnly] After retry, current mode: {currentMode}");
            }
            else
            {
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Occlusion applied successfully (current: {currentMode})");
            }
        }

        occlusionApplyCoroutine = null;
    }

    void SetOcclusionModesImmediate(bool enabled)
    {
        if (!occlusionManager) return;

        EnvironmentDepthMode previousMode = occlusionManager.requestedEnvironmentDepthMode;

        if (enabled)
        {
            // オクルージョンを有効化
            occlusionManager.requestedEnvironmentDepthMode = envDepthModeOn;
            occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
            occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;

            Debug.Log($"[PlaceAvatarOnPlaneOnly] Occlusion ON: {previousMode} → {envDepthModeOn}");
        }
        else
        {
            // オクルージョンを無効化
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
            occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
            occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;

            Debug.Log($"[PlaceAvatarOnPlaneOnly] Occlusion OFF: {previousMode} → Disabled");
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Requested: {occlusionManager.requestedEnvironmentDepthMode}, Current: {occlusionManager.currentEnvironmentDepthMode}");
    }

    // ========== Issue #425: アバター初期配置 ==========

    /// <summary>
    /// Issue #425: ロードされたアバターをカメラの1m前方に配置
    /// 平面が検出されていればその上に、なければカメラ高さで配置
    /// </summary>
    /// <param name="loadedAvatar">ロード済みアバター</param>
    /// <param name="distanceAhead">カメラからの距離（メートル）</param>
    /// <returns>配置成功した場合true</returns>
    public bool PlaceAvatarAhead(GameObject loadedAvatar, float distanceAhead = 1.0f)
    {
        if (loadedAvatar == null)
        {
            Debug.LogWarning("[PlaceAvatarOnPlaneOnly] PlaceAvatarAhead: avatar is null");
            return false;
        }

        if (arCamera == null)
        {
            arCamera = Camera.main;
            if (arCamera == null)
            {
                Debug.LogWarning("[PlaceAvatarOnPlaneOnly] PlaceAvatarAhead: No camera found");
                return false;
            }
        }

        // カメラの前方方向（水平のみ）
        Vector3 camPos = arCamera.transform.position;
        Vector3 camForward = arCamera.transform.forward;
        camForward.y = 0;
        camForward.Normalize();

        if (camForward.sqrMagnitude < 0.01f)
        {
            camForward = Vector3.forward;
        }

        // 1m前方の位置を計算
        Vector3 targetPosition = camPos + camForward * distanceAhead;

        // 平面を検索してY座標を調整
        bool foundPlane = false;
        ARPlane hitPlane = null;

        if (rcMgr != null)
        {
            // カメラ位置から下方向にレイキャスト
            Vector3 rayOrigin = new Vector3(targetPosition.x, camPos.y + 1f, targetPosition.z);
            Ray ray = new Ray(rayOrigin, Vector3.down);

            // スクリーン座標に変換してレイキャスト
            Vector3 screenPoint = arCamera.WorldToScreenPoint(targetPosition);
            if (rcMgr.Raycast(screenPoint, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                var hit = s_Hits[0];
                hitPlane = planeManager?.GetPlane(hit.trackableId) ?? hit.trackable as ARPlane;

                if (hitPlane != null)
                {
                    // 水平面フィルター
                    if (!onlyHorizontal || hitPlane.alignment == PlaneAlignment.HorizontalUp || hitPlane.alignment == PlaneAlignment.HorizontalDown)
                    {
                        targetPosition = hit.pose.position;
                        foundPlane = true;
                        Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAhead: Found plane at {targetPosition}, alignment: {hitPlane.alignment}");
                    }
                }
            }
        }

        // 平面が見つからなかった場合、検出済み平面から最も近いものを使用
        if (!foundPlane && planeManager != null)
        {
            float closestDist = float.MaxValue;
            ARPlane closestPlane = null;

            foreach (var plane in planeManager.trackables)
            {
                if (onlyHorizontal && plane.alignment != PlaneAlignment.HorizontalUp && plane.alignment != PlaneAlignment.HorizontalDown)
                    continue;

                // 平面の中心からターゲット位置までの水平距離
                Vector3 planeCenter = plane.center;
                float dist = Vector3.Distance(
                    new Vector3(targetPosition.x, 0, targetPosition.z),
                    new Vector3(planeCenter.x, 0, planeCenter.z)
                );

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestPlane = plane;
                }
            }

            // 最も近い平面が3m以内なら使用
            if (closestPlane != null && closestDist < 3f)
            {
                hitPlane = closestPlane;
                // 平面上に投影
                Vector3 planeNormal = closestPlane.normal;
                float d = Vector3.Dot(planeNormal, targetPosition - closestPlane.center);
                targetPosition = targetPosition - planeNormal * d;
                foundPlane = true;
                Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAhead: Using closest plane at distance {closestDist:F2}m");
            }
        }

        // 平面が見つからなかった場合の高さ推定
        if (!foundPlane)
        {
            if (camPos.y < 0.5f)
            {
                // Editor mode: カメラが原点付近→アバター中心がカメラ高さに来るように配置
                targetPosition.y = -0.8f;
                Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAhead: Editor mode, centering avatar in view (y={targetPosition.y:F1})");
            }
            else
            {
                // AR mode: カメラが頭の高さ→床面を推定
                targetPosition.y = camPos.y - 1.5f;
                Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAhead: AR mode, estimated floor height (y={targetPosition.y:F1})");
            }
        }

        // アバターを配置
        Quaternion rotation = GetFaceCameraRotation(targetPosition, hitPlane?.alignment ?? PlaneAlignment.HorizontalUp);
        Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAhead: GetFaceCameraRotation returned {rotation.eulerAngles}, arCamera={arCamera?.name ?? "null"}");
        loadedAvatar.transform.SetPositionAndRotation(targetPosition, rotation);
        loadedAvatar.SetActive(true);

        // 内部状態を更新
        avatar = loadedAvatar;
        avatarPlane = hitPlane;
        // Issue #422: 紫モード（CameraLocked）をデフォルトに
        currentFollowMode = FollowMode.CameraLocked;
        if (arCamera && avatar)
        {
            Vector3 offset = avatar.transform.position - arCamera.transform.position;
            followDistance = offset.magnitude;
            cameraLocalOffset = Quaternion.Inverse(arCamera.transform.rotation) * offset;
        }
        SetPlaneColor(cameraLockedColor);
        SetOcclusion(false);
        currentAvatarScale = loadedAvatar.transform.localScale.x;

        // FaceControllerとAnimatorをバインド
        BindAvatarFaceController();

        Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAhead: Avatar placed at {targetPosition}, foundPlane: {foundPlane}");
        return true;
    }

    /// <summary>
    /// Issue #474: アバターをカメラ前方に配置（平面検知待機版）
    /// 平面が見つからない場合は最大で指定秒数待機し、平面上に配置する
    /// </summary>
    /// <param name="loadedAvatar">配置するアバター</param>
    /// <param name="distanceAhead">カメラからの距離（メートル）</param>
    /// <param name="maxWaitSeconds">平面検知の最大待機時間（秒）</param>
    /// <returns>配置成功した場合はtrue</returns>
    public async UniTask<bool> PlaceAvatarAheadAsync(GameObject loadedAvatar, float distanceAhead = 1.5f, float maxWaitSeconds = 3.0f)
    {
        if (loadedAvatar == null)
        {
            Debug.LogWarning("[PlaceAvatarOnPlaneOnly] PlaceAvatarAheadAsync: avatar is null");
            return false;
        }

        if (arCamera == null)
        {
            arCamera = Camera.main;
            if (arCamera == null)
            {
                Debug.LogWarning("[PlaceAvatarOnPlaneOnly] PlaceAvatarAheadAsync: No camera found");
                return false;
            }
        }

        // まず同期版を試す
        bool hasPlane = HasAnyDetectedPlane();

        if (hasPlane)
        {
            Debug.Log("[PlaceAvatarOnPlaneOnly] PlaceAvatarAheadAsync: Plane already detected, using sync placement");
            return PlaceAvatarAhead(loadedAvatar, distanceAhead);
        }

        // 平面検知を待機
        Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAheadAsync: Waiting for plane detection (max {maxWaitSeconds}s)...");

        float waitTime = 0f;
        float checkInterval = 0.2f; // 200ms間隔でチェック

        while (waitTime < maxWaitSeconds)
        {
            await UniTask.Delay((int)(checkInterval * 1000));
            waitTime += checkInterval;

            if (HasAnyDetectedPlane())
            {
                Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAheadAsync: Plane detected after {waitTime:F1}s");
                return PlaceAvatarAhead(loadedAvatar, distanceAhead);
            }
        }

        // タイムアウト - 平面が見つからなかったが、推定位置で配置する
        Debug.LogWarning($"[PlaceAvatarOnPlaneOnly] PlaceAvatarAheadAsync: Timeout after {maxWaitSeconds}s, using estimated position");
        return PlaceAvatarAhead(loadedAvatar, distanceAhead);
    }

    /// <summary>
    /// 検知済みの平面があるかどうかをチェック
    /// </summary>
    /// <returns>水平面が1つ以上検知されている場合はtrue</returns>
    public bool HasAnyDetectedPlane()
    {
        if (planeManager == null) return false;

        foreach (var plane in planeManager.trackables)
        {
            if (!onlyHorizontal)
                return true;

            if (plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown)
                return true;
        }

        return false;
    }
}