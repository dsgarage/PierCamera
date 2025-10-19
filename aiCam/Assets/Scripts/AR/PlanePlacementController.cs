// Assets/Scripts/AR/PlanePlacementController.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AR
{
    /// <summary>
    /// 平面レイキャストしてアバターを配置/再配置
    /// </summary>
    public class PlanePlacementController : MonoBehaviour
    {
        [Header("Prefab")]
        [Tooltip("配置するアバターのPrefab")]
        [SerializeField] private GameObject placedPrefab;

        [Header("References")]
        [SerializeField] private ARRaycastManager raycastManager;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Camera arCamera;

        // 状態
        private GameObject currentAvatar;
        private ARPlane lastHitPlane;
        private List<ARRaycastHit> raycastHits = new List<ARRaycastHit>();

        // Public API
        public bool IsPlaneFound { get; private set; }
        public GameObject CurrentAvatar => currentAvatar;
        public ARPlane LastHitPlane => lastHitPlane;

        void Start()
        {
            if (arCamera == null)
                arCamera = Camera.main;

            if (raycastManager == null)
                raycastManager = FindObjectOfType<ARRaycastManager>();

            if (planeManager == null)
                planeManager = FindObjectOfType<ARPlaneManager>();
        }

        /// <summary>
        /// 指定されたスクリーン座標にアバターを配置/再配置
        /// </summary>
        public void TryPlaceAvatarAtScreenPos(Vector2 screenPos)
        {
            if (raycastManager == null)
            {
                Debug.LogWarning("[PlanePlacementController] ARRaycastManager is null");
                return;
            }

            raycastHits.Clear();

            // 平面へのレイキャスト
            if (raycastManager.Raycast(screenPos, raycastHits, TrackableType.PlaneWithinPolygon))
            {
                if (raycastHits.Count > 0)
                {
                    ARRaycastHit hit = raycastHits[0];
                    Pose hitPose = hit.pose;

                    // 平面情報を取得
                    if (planeManager != null)
                    {
                        lastHitPlane = planeManager.GetPlane(hit.trackableId);
                    }

                    IsPlaneFound = true;

                    if (currentAvatar == null)
                    {
                        // 初回配置
                        PlaceAvatar(hitPose);
                    }
                    else
                    {
                        // 再配置
                        RepositionAvatar(hitPose);
                    }
                }
            }
            else
            {
                Debug.Log("[PlanePlacementController] No plane hit at screen position");
            }
        }

        /// <summary>
        /// アバターを初回配置
        /// </summary>
        private void PlaceAvatar(Pose pose)
        {
            if (placedPrefab == null)
            {
                Debug.LogError("[PlanePlacementController] placedPrefab is not assigned!");
                return;
            }

            currentAvatar = Instantiate(placedPrefab, pose.position, pose.rotation);
            Debug.Log($"[PlanePlacementController] Avatar placed at {pose.position}");

            // AvatarFollowController がアタッチされていれば初期化
            var followController = currentAvatar.GetComponent<AvatarFollowController>();
            if (followController != null && lastHitPlane != null)
            {
                followController.BindToPlane(lastHitPlane);
            }
        }

        /// <summary>
        /// アバターを再配置
        /// </summary>
        private void RepositionAvatar(Pose pose)
        {
            if (currentAvatar == null)
                return;

            // 位置を更新（Y軸回転は現在の向きを維持）
            currentAvatar.transform.position = pose.position;

            // カメラ方向を向くようにY軸回転のみ更新
            Vector3 lookDir = arCamera.transform.position - currentAvatar.transform.position;
            lookDir.y = 0;

            if (lookDir.sqrMagnitude > 0.01f)
            {
                currentAvatar.transform.rotation = Quaternion.LookRotation(lookDir);
            }

            Debug.Log($"[PlanePlacementController] Avatar repositioned at {pose.position}");

            // AvatarFollowController の追従モードをOffに戻す
            var followController = currentAvatar.GetComponent<AvatarFollowController>();
            if (followController != null)
            {
                followController.SetMode(AvatarFollowController.FollowMode.Off);

                // 平面情報を更新
                if (lastHitPlane != null)
                {
                    followController.BindToPlane(lastHitPlane);
                }
            }
        }

        /// <summary>
        /// 現在のアバターを削除
        /// </summary>
        public void ClearAvatar()
        {
            if (currentAvatar != null)
            {
                Destroy(currentAvatar);
                currentAvatar = null;
                Debug.Log("[PlanePlacementController] Avatar cleared");
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (currentAvatar != null && lastHitPlane != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(currentAvatar.transform.position, lastHitPlane.center);
                Gizmos.DrawWireSphere(lastHitPlane.center, 0.1f);
            }
        }
#endif
    }
}
