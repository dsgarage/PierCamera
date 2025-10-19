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
    [SerializeField] AROcclusionManager occlusionManager;
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

    // スワイプ操作用
    bool isSwipeActive = false;
    Vector2 swipeStartPosition;
    float avatarRotationY = 0f;  // アバターのY軸回転（手動調整分）

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
        // 起動確認ログ（常に出力）
        Debug.Log($"[PlaceAvatarOnPlaneOnly] Awake - Debug logging enabled: {enableDebugLog}");

        rcMgr = GetComponent<ARRaycastManager>();
        if (!planeManager) planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        if (!anchorManager) anchorManager = FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
        if (!occlusionManager) occlusionManager = FindFirstObjectByType<AROcclusionManager>(FindObjectsInactive.Include);
        if (!faceUIManager) faceUIManager = FindFirstObjectByType<FaceUIManager>(FindObjectsInactive.Include);
        if (!poseGridLayout) poseGridLayout = FindFirstObjectByType<PoseGridLayout>(FindObjectsInactive.Include);
        // 床寄りにしたい場合は検出を水平に絞る（※壁検出を抑制）
        if (planeManager) planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

        // ARカメラ未指定なら自動取得
        if (!arCamera) arCamera = Camera.main;

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
        }

        Debug.Log($"[PlaceAvatarOnPlaneOnly] Initialized - FollowMode: {enableFollowMode}, Distance: {followDistance}m");
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

        // スワイプ操作の処理（固定モード時のみ）
        if (currentFollowMode != FollowMode.Off && avatar && Input.touchCount > 0)
        {
            HandleSwipeInteraction();
        }

        if (Input.touchCount == 0)
        {
            isSwipeActive = false;
            return;
        }

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
                // スワイプ開始
                isSwipeActive = true;
                swipeStartPosition = touch.position;
                break;

            case TouchPhase.Moved:
                if (!isSwipeActive) return;

                Vector2 delta = touch.position - swipeStartPosition;

                // 上下スワイプ: 距離調整
                if (enableSwipeDistance && Mathf.Abs(delta.y) > Mathf.Abs(delta.x))
                {
                    // 上にスワイプ(+Y) = 遠くに、下にスワイプ(-Y) = 近くに
                    float distanceDelta = delta.y / swipeDistanceSensitivity;
                    followDistance = Mathf.Clamp(followDistance + distanceDelta, minDistance, maxDistance);

                    Debug.Log($"[PlaceAvatarOnPlaneOnly] Swipe distance adjust: {followDistance:F2}m (delta: {distanceDelta:F2}m)");
                }
                // 左右スワイプ: 回転
                else if (enableSwipeRotation && Mathf.Abs(delta.x) > Mathf.Abs(delta.y))
                {
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
}