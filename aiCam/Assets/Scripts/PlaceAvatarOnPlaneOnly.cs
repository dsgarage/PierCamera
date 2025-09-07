// PlaceAvatarOnPlaneOnly.cs
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

    [Header("Filters")]
    [Tooltip("水平面（床・テーブルなど）に限定")]
    [SerializeField] bool onlyHorizontal = true;
    [Tooltip("対応端末では“床”分類の平面に限定（未対応端末では無視）")]
    [SerializeField] bool onlyFloorIfAvailable = false;

    [Header("UI touch block")]
    [Tooltip("この Canvas 上の UI（例: Capture ボタン）をタップしたときは配置を無効化する")]
    [SerializeField] Canvas uiCanvas;                                  // ScreenPoint判定用
    [SerializeField] List<RectTransform> touchBlockAreas = new();      // Capture ボタンなどの RectTransform を登録

    static readonly List<ARRaycastHit> s_Hits = new();
    ARRaycastManager rcMgr;
    GameObject avatar;

    void Awake()
    {
        rcMgr = GetComponent<ARRaycastManager>();
        if (!planeManager) planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
        if (!anchorManager) anchorManager = FindFirstObjectByType<ARAnchorManager>(FindObjectsInactive.Include);
        if (!faceUIManager) faceUIManager = FindFirstObjectByType<FaceUIManager>(FindObjectsInactive.Include);
        // 床寄りにしたい場合は検出を水平に絞る（※壁検出を抑制）
        if (planeManager) planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal;
    }

    void Update()
    {
        if (Input.touchCount == 0) return;
        var touch = Input.GetTouch(0);
        if (touch.phase != TouchPhase.Began) return;

        // ★ 追加：UI 上のタップは必ず無視（EventSystem か、明示登録したRectに入っていたら弾く）
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

        // 4) 生成 or 位置更新
        if (!avatar)
        {
            avatar = Instantiate(avatarPrefab, pose.position, pose.rotation, parent);

            // HUDを起動
            faceUIManager?.InitializeWithAvatar(avatar);
        }
        else
        {
            avatar.transform.SetPositionAndRotation(pose.position, pose.rotation);
        }
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
}