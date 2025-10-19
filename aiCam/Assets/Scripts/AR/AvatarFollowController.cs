// Assets/Scripts/AR/AvatarFollowController.cs
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace AR
{
    /// <summary>
    /// アバターの追従モードを管理
    /// ModeA: 平面にロックして距離を一定に保つ（平面追従）
    /// ModeB: カメラとの相対位置（距離）を固定して追従（カメラ追従）
    /// ModeOff: 追従なし
    /// </summary>
    public class AvatarFollowController : MonoBehaviour
    {
        public enum FollowMode
        {
            Off = 0,           // 追従なし
            PlaneLocked = 1,   // 平面追従（距離固定、平面上を滑る）
            CameraLocked = 2   // カメラ追従（相対位置固定）
        }

        [Header("Follow Settings")]
        [SerializeField] private FollowMode mode = FollowMode.Off;

        [Tooltip("維持する距離（メートル）")]
        [SerializeField] private float desiredDistance = 1.5f;

        [Tooltip("位置の補間速度（0-1）")]
        [SerializeField] private float posLerp = 0.15f;

        [Tooltip("回転の補間速度（0-1）")]
        [SerializeField] private float rotLerp = 0.15f;

        [Header("References")]
        [SerializeField] private ARRaycastManager raycaster;
        [SerializeField] private ARPlaneManager planeManager;
        [SerializeField] private Camera arCamera;

        // Runtime state
        private ARPlane boundPlane;                    // ModeA で参照する平面
        private Vector3 cameraLocalOffset;             // ModeB: カメラ相対オフセット
        private List<ARRaycastHit> raycastHits = new List<ARRaycastHit>();

        // Public API
        public FollowMode Mode => mode;

        void Start()
        {
            if (arCamera == null)
                arCamera = Camera.main;
        }

        void Update()
        {
            if (mode == FollowMode.Off)
                return;

            if (arCamera == null)
                return;

            if (mode == FollowMode.PlaneLocked)
            {
                UpdatePlaneLocked();
            }
            else if (mode == FollowMode.CameraLocked)
            {
                UpdateCameraLocked();
            }
        }

        /// <summary>
        /// 追従モードを設定
        /// </summary>
        public void SetMode(FollowMode nextMode, ARPlane planeHint = null)
        {
            if (mode == nextMode)
                return;

            Debug.Log($"[AvatarFollowController] Mode changed: {mode} → {nextMode}");

            mode = nextMode;

            if (mode == FollowMode.PlaneLocked)
            {
                if (planeHint != null)
                {
                    BindToPlane(planeHint);
                }
                else
                {
                    // 現在位置直下の平面を検索
                    TryFindPlaneBelow();
                }
            }
            else if (mode == FollowMode.CameraLocked)
            {
                BindToCamera(arCamera);
            }
        }

        /// <summary>
        /// ModeA: 平面に紐付け
        /// </summary>
        public void BindToPlane(ARPlane plane)
        {
            if (plane == null)
            {
                Debug.LogWarning("[AvatarFollowController] BindToPlane: plane is null");
                return;
            }

            boundPlane = GetRootPlane(plane);
            desiredDistance = Vector3.Distance(
                new Vector3(arCamera.transform.position.x, 0, arCamera.transform.position.z),
                new Vector3(transform.position.x, 0, transform.position.z)
            );

            Debug.Log($"[AvatarFollowController] Bound to plane {boundPlane.trackableId}, distance={desiredDistance:F2}m");
        }

        /// <summary>
        /// ModeB: カメラ相対位置を計算して記憶
        /// </summary>
        public void BindToCamera(Camera cam)
        {
            if (cam == null)
            {
                Debug.LogWarning("[AvatarFollowController] BindToCamera: camera is null");
                return;
            }

            Vector3 camPos = cam.transform.position;
            Vector3 avatarPos = transform.position;

            // カメラのY軸回転のみ考慮した相対オフセットを計算
            float camYaw = cam.transform.eulerAngles.y;
            Quaternion invCamRot = Quaternion.Inverse(Quaternion.Euler(0, camYaw, 0));
            cameraLocalOffset = invCamRot * (avatarPos - camPos);
            desiredDistance = cameraLocalOffset.magnitude;

            Debug.Log($"[AvatarFollowController] Bound to camera, offset={cameraLocalOffset}, distance={desiredDistance:F2}m");
        }

        /// <summary>
        /// ModeA: 平面上を滑りながら距離を維持
        /// </summary>
        private void UpdatePlaneLocked()
        {
            if (boundPlane == null || raycaster == null)
            {
                Debug.LogWarning("[AvatarFollowController] PlaneLocked mode but no bound plane or raycaster");
                mode = FollowMode.Off;
                return;
            }

            // Subsume対策: 最終的な親平面を取得
            boundPlane = GetRootPlane(boundPlane);

            if (boundPlane == null || boundPlane.trackingState == TrackingState.None)
            {
                Debug.LogWarning("[AvatarFollowController] Bound plane lost. Switching to Off mode.");
                mode = FollowMode.Off;
                return;
            }

            // カメラ位置から水平方向に desiredDistance 離れた位置を計算
            Vector3 camPos = arCamera.transform.position;
            Vector3 camForwardFlat = Vector3.ProjectOnPlane(arCamera.transform.forward, Vector3.up).normalized;

            if (camForwardFlat.sqrMagnitude < 0.01f)
            {
                camForwardFlat = Vector3.ProjectOnPlane(arCamera.transform.up, Vector3.up).normalized;
            }

            Vector3 targetPosFlat = camPos + camForwardFlat * desiredDistance;

            // 平面上に投影
            Vector3 targetPos = ProjectPointOnPlane(targetPosFlat, boundPlane);

            // 補間して移動
            transform.position = Vector3.Lerp(transform.position, targetPos, posLerp);

            // 回転: カメラを向く（Y軸のみ）
            Vector3 lookDir = camPos - transform.position;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotLerp);
            }
        }

        /// <summary>
        /// ModeB: カメラ相対位置を維持
        /// </summary>
        private void UpdateCameraLocked()
        {
            if (arCamera == null)
            {
                mode = FollowMode.Off;
                return;
            }

            Vector3 camPos = arCamera.transform.position;
            float camYaw = arCamera.transform.eulerAngles.y;
            Quaternion camRot = Quaternion.Euler(0, camYaw, 0);

            // オフセットを適用
            Vector3 targetPos = camPos + (camRot * cameraLocalOffset.normalized) * desiredDistance;

            // 補間して移動
            transform.position = Vector3.Lerp(transform.position, targetPos, posLerp);

            // 回転: カメラを向く（Y軸のみ）
            Vector3 lookDir = camPos - transform.position;
            lookDir.y = 0;
            if (lookDir.sqrMagnitude > 0.01f)
            {
                Quaternion targetRot = Quaternion.LookRotation(lookDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotLerp);
            }
        }

        /// <summary>
        /// 点を平面上に投影
        /// </summary>
        private Vector3 ProjectPointOnPlane(Vector3 point, ARPlane plane)
        {
            if (plane == null)
                return point;

            Vector3 planeCenter = plane.center;
            Vector3 planeNormal = plane.normal;

            // 点から平面への垂線の足を求める
            float distance = Vector3.Dot(planeNormal, point - planeCenter);
            return point - planeNormal * distance;
        }

        /// <summary>
        /// SubsumedBy を辿って最終的な親平面を取得
        /// </summary>
        private ARPlane GetRootPlane(ARPlane plane)
        {
            if (plane == null)
                return null;

            ARPlane current = plane;
            int maxDepth = 10; // 無限ループ対策
            int depth = 0;

            while (current.subsumedBy != null && depth < maxDepth)
            {
                current = current.subsumedBy;
                depth++;
            }

            return current;
        }

        /// <summary>
        /// 現在位置直下の平面を検索
        /// </summary>
        private void TryFindPlaneBelow()
        {
            if (raycaster == null)
                return;

            Vector3 avatarPos = transform.position;
            Vector3 origin = avatarPos + Vector3.up * 2f; // 上から下へレイキャスト
            Vector3 direction = Vector3.down;

            raycastHits.Clear();

            if (raycaster.Raycast(new Ray(origin, direction), raycastHits, TrackableType.PlaneWithinPolygon))
            {
                foreach (var hit in raycastHits)
                {
                    ARPlane plane = planeManager.GetPlane(hit.trackableId);
                    if (plane != null && plane.alignment == PlaneAlignment.HorizontalUp)
                    {
                        BindToPlane(plane);
                        return;
                    }
                }
            }

            Debug.LogWarning("[AvatarFollowController] No plane found below avatar. Mode set to Off.");
            mode = FollowMode.Off;
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying)
                return;

            if (mode == FollowMode.PlaneLocked && boundPlane != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(transform.position, boundPlane.center);
                Gizmos.DrawWireSphere(boundPlane.center, 0.1f);
            }
            else if (mode == FollowMode.CameraLocked && arCamera != null)
            {
                Gizmos.color = Color.blue;
                Gizmos.DrawLine(transform.position, arCamera.transform.position);
            }
        }
#endif
    }
}
