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

    static readonly List<ARRaycastHit> s_Hits = new();
    ARRaycastManager rcMgr;
    GameObject avatar;
    FaceController avatarFaceController;
    Animator avatarAnimator;

    void Awake()
    {
        rcMgr = GetComponent<ARRaycastManager>();
        if (!planeManager) planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        if (!anchorManager) anchorManager = FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
        if (!faceUIManager) faceUIManager = FindFirstObjectByType<FaceUIManager>(FindObjectsInactive.Include);
        if (!poseGridLayout) poseGridLayout = FindFirstObjectByType<PoseGridLayout>(FindObjectsInactive.Include);
        // 床寄りにしたい場合は検出を水平に絞る（※壁検出を抑制）
        if (planeManager) planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;

        // ARカメラ未指定なら自動取得
        if (!arCamera) arCamera = Camera.main;
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
        }

        if (Input.touchCount == 0) return;
        var touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began) return;

        // Main画面以外は無視
        if (UIMgr.instance.State != UIMgr.UIState.Home) return;

        // UI 上のタップは必ず無視（EventSystem か、明示登録したRectに入っていたら弾く）
        if (IsTouchOverUI(touch)) return;

        // UI上のタップは無視
        if (EventSystem.current && EventSystem.current.IsPointerOverGameObject(touch.fingerId)) return;

        // 1) 平面ポリゴン内だけにRaycast
        if (!rcMgr.Raycast(touch.position, s_Hits, TrackableType.PlaneWithinPolygon))
            return; // ← 平面外をタップ → 何もしない

        var hit = s_Hits[0];
        var plane = planeManager ? planeManager.GetPlane(hit.trackableId) : hit.trackable as ARPlane;
        if (!plane) return;

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

            // 動的にロードされたアバターの場合は自動セットアップ
            if (avatar.GetComponent<AR.AvatarAutoSetup>() == null)
            {
                // AvatarAutoSetupコンポーネントがない場合は静的メソッドでセットアップ
                AR.AvatarAutoSetup.Setup(avatar);
            }

            BindAvatarFaceController();

            // HUDを起動
            faceUIManager?.InitializeWithAvatar(avatar);
        }
        else
        {
            avatar.transform.SetPositionAndRotation(pose.position, rot);
            if (!avatarFaceController || !avatarAnimator)
                BindAvatarFaceController();
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
}