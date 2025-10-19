using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[RequireComponent(typeof(ARRaycastManager))]
public sealed class PlaceAvatarOnPlaneOnly : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] GameObject avatarPrefab;

    [Header("Managers")]
    [SerializeField] ARPlaneManager planeManager;
    [SerializeField] ARAnchorManager anchorManager;   // 任意（安定化用）
    [SerializeField] FaceUIManager faceUIManager;
    [SerializeField] ExpressionGridLayout expressionGridLayout;
    [SerializeField] PoseGridLayout poseGridLayout;

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

    static readonly List<ARRaycastHit> s_Hits = new();
    ARRaycastManager rcMgr;
    GameObject avatar;
    ARPlane avatarPlane; // アバターが配置された平面
    FaceController avatarFaceController;
    Animator avatarAnimator;

    // 追従モード
    enum FollowMode { Off, PlaneLocked, CameraLocked }
    FollowMode currentFollowMode = FollowMode.Off;
    Vector3 cameraLocalOffset; // CameraLocked用のオフセット
    float lastTapTime = -1f;
    Vector2 lastTapPosition;

    void Awake()
    {
        // 起動確認ログ（常に出力）
        Debug.Log($"[PlaceAvatarOnPlaneOnly] Awake - Debug logging enabled: {enableDebugLog}");

        rcMgr = GetComponent<ARRaycastManager>();
        if (!planeManager) planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        if (!anchorManager) anchorManager = FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
        if (!faceUIManager) faceUIManager = FindFirstObjectByType<FaceUIManager>(FindObjectsInactive.Include);
        if (!poseGridLayout) poseGridLayout = FindFirstObjectByType<PoseGridLayout>(FindObjectsInactive.Include);
        // 床寄りにしたい場合は検出を水平に絞る（※壁検出を抑制）
        if (planeManager) planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

        // ARカメラ未指定なら自動取得
        if (!arCamera) arCamera = Camera.main;

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Initialized - FollowMode: {enableFollowMode}, Distance: {followDistance}m");
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
        }
        else
        {
            // 追従モード更新
            UpdateFollowMode();
        }

        if (Input.touchCount == 0) return;
        var touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began) return;

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
            avatar = Instantiate(avatarPrefab, pose.position, rot, parent);
            avatarPlane = plane; // 配置した平面を記憶
            currentFollowMode = FollowMode.Off; // 初期はOff

            BindAvatarFaceController();

            // HUDを起動
            faceUIManager?.InitializeWithAvatar(avatar);

            Debug.Log($"[PlaceAvatarOnPlaneOnly] Avatar placed at {pose.position}. Tap twice to toggle follow mode.");
        }
        else
        {
            avatar.transform.SetPositionAndRotation(pose.position, rot);
            avatarPlane = plane; // 再配置時も平面を更新
            currentFollowMode = FollowMode.Off; // 再配置時はOff
            if (!avatarFaceController || !avatarAnimator)
                BindAvatarFaceController();

            Debug.Log($"[PlaceAvatarOnPlaneOnly] Avatar repositioned to {pose.position}. Follow mode reset to Off.");
        }
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

    // UIヒット判定（EventSystem + 指定Rect）
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
        // Off → PlaneLocked → CameraLocked → Off
        switch (currentFollowMode)
        {
            case FollowMode.Off:
                currentFollowMode = FollowMode.PlaneLocked;
                float distance = arCamera && avatar ? Vector3.Distance(arCamera.transform.position, avatar.transform.position) : 0f;
                Debug.Log($"[PlaceAvatarOnPlaneOnly] Follow Mode: PlaneLocked (平面追従) - Current distance: {distance:F2}m");
                break;
            case FollowMode.PlaneLocked:
                currentFollowMode = FollowMode.CameraLocked;
                // カメラ相対オフセットを計算
                if (arCamera && avatar)
                {
                    Vector3 camPos = arCamera.transform.position;
                    Vector3 avatarPos = avatar.transform.position;
                    float camYaw = arCamera.transform.eulerAngles.y;
                    Quaternion invCamRot = Quaternion.Inverse(Quaternion.Euler(0, camYaw, 0));
                    cameraLocalOffset = invCamRot * (avatarPos - camPos);

                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Follow Mode: CameraLocked (カメラ追従) - Offset: {cameraLocalOffset}, CamYaw: {camYaw:F1}°");
                }
                break;
            case FollowMode.CameraLocked:
                currentFollowMode = FollowMode.Off;
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
            Debug.Log($"[PlaceAvatarOnPlaneOnly] PlaneLocked: Distance={currentDistance:F2}m (target={followDistance:F2}m), Horizontal={horizontalDistance:F2}m");
        }

        // カメラを向く
        Vector3 lookDir = camPos - avatar.transform.position;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, targetRot, followSmoothness);
        }
    }

    void UpdateCameraLocked()
    {
        if (!arCamera || !avatar)
            return;

        Vector3 camPos = arCamera.transform.position;
        float camYaw = arCamera.transform.eulerAngles.y;
        Quaternion camRot = Quaternion.Euler(0, camYaw, 0);

        // カメラ相対位置を維持
        Vector3 targetPos = camPos + (camRot * cameraLocalOffset.normalized) * followDistance;

        // 滑らかに移動
        avatar.transform.position = Vector3.Lerp(avatar.transform.position, targetPos, followSmoothness);

        if (enableDebugLog && Time.frameCount % 30 == 0) // 30フレームごとにログ
        {
            float currentDistance = Vector3.Distance(camPos, avatar.transform.position);
            Debug.Log($"[PlaceAvatarOnPlaneOnly] CameraLocked: Distance={currentDistance:F2}m (target={followDistance:F2}m), CamYaw={camYaw:F1}°");
        }

        // カメラを向く
        Vector3 lookDir = camPos - avatar.transform.position;
        lookDir.y = 0;
        if (lookDir.sqrMagnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            avatar.transform.rotation = Quaternion.Slerp(avatar.transform.rotation, targetRot, followSmoothness);
        }
    }
}