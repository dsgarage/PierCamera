// Assets/Scripts/AR/AvatarTapHandler.cs
using UnityEngine;
using AR.Input;

namespace AR
{
    /// <summary>
    /// ダブルタップ地点からアバターColliderをレイキャストし、
    /// 該当アバターのFollowModeをトグル
    /// </summary>
    [RequireComponent(typeof(TouchRouter))]
    public class AvatarTapHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera arCamera;
        [SerializeField] private PlanePlacementController placementController;

        [Header("Raycast Settings")]
        [Tooltip("アバター検出用のレイヤー")]
        [SerializeField] private LayerMask avatarLayerMask = ~0; // デフォルトは全レイヤー

        [Tooltip("レイキャストの最大距離")]
        [SerializeField] private float maxRaycastDistance = 100f;

        private TouchRouter touchRouter;

        void Awake()
        {
            touchRouter = GetComponent<TouchRouter>();
        }

        void Start()
        {
            if (arCamera == null)
                arCamera = Camera.main;

            if (placementController == null)
                placementController = FindObjectOfType<PlanePlacementController>();
        }

        void OnEnable()
        {
            if (touchRouter != null)
            {
                touchRouter.OnSingleTap += HandleSingleTap;
                touchRouter.OnDoubleTap += HandleDoubleTap;
            }
        }

        void OnDisable()
        {
            if (touchRouter != null)
            {
                touchRouter.OnSingleTap -= HandleSingleTap;
                touchRouter.OnDoubleTap -= HandleDoubleTap;
            }
        }

        /// <summary>
        /// シングルタップ処理: アバター配置
        /// </summary>
        private void HandleSingleTap(Vector2 screenPos)
        {
            if (placementController != null)
            {
                placementController.TryPlaceAvatarAtScreenPos(screenPos);
            }
        }

        /// <summary>
        /// ダブルタップ処理: 追従モード切替
        /// </summary>
        private void HandleDoubleTap(Vector2 screenPos)
        {
            if (arCamera == null)
                return;

            Ray ray = arCamera.ScreenPointToRay(screenPos);
            RaycastHit hit;

            // Physics Raycast でアバターを検出
            if (Physics.Raycast(ray, out hit, maxRaycastDistance, avatarLayerMask))
            {
                // AvatarFollowController を取得
                var followController = hit.collider.GetComponentInParent<AvatarFollowController>();

                if (followController != null)
                {
                    ToggleFollowMode(followController);
                }
                else
                {
                    Debug.Log("[AvatarTapHandler] Double tapped object does not have AvatarFollowController");
                }
            }
            else
            {
                Debug.Log("[AvatarTapHandler] Double tap did not hit any avatar");
            }
        }

        /// <summary>
        /// 追従モードを切り替え: PlaneLocked → CameraLocked → Off → PlaneLocked ...
        /// </summary>
        private void ToggleFollowMode(AvatarFollowController followController)
        {
            AvatarFollowController.FollowMode currentMode = followController.Mode;
            AvatarFollowController.FollowMode nextMode;

            switch (currentMode)
            {
                case AvatarFollowController.FollowMode.Off:
                    nextMode = AvatarFollowController.FollowMode.PlaneLocked;
                    break;

                case AvatarFollowController.FollowMode.PlaneLocked:
                    nextMode = AvatarFollowController.FollowMode.CameraLocked;
                    break;

                case AvatarFollowController.FollowMode.CameraLocked:
                    nextMode = AvatarFollowController.FollowMode.Off;
                    break;

                default:
                    nextMode = AvatarFollowController.FollowMode.Off;
                    break;
            }

            // モード切替時に平面情報を渡す
            if (nextMode == AvatarFollowController.FollowMode.PlaneLocked &&
                placementController != null &&
                placementController.LastHitPlane != null)
            {
                followController.SetMode(nextMode, placementController.LastHitPlane);
            }
            else
            {
                followController.SetMode(nextMode);
            }

            Debug.Log($"[AvatarTapHandler] Follow mode toggled to: {nextMode}");

            // UI フィードバック（オプション）
            ShowModeToast(nextMode);
        }

        /// <summary>
        /// モード切替のフィードバック表示（オプション）
        /// </summary>
        private void ShowModeToast(AvatarFollowController.FollowMode mode)
        {
            string modeText = mode switch
            {
                AvatarFollowController.FollowMode.Off => "追従モード: OFF",
                AvatarFollowController.FollowMode.PlaneLocked => "追従モード: 平面固定",
                AvatarFollowController.FollowMode.CameraLocked => "追従モード: カメラ固定",
                _ => "追従モード: 不明"
            };

            Debug.Log($"[AvatarTapHandler] {modeText}");

            // TODO: UI Toast を表示する場合はここに実装
            // 例: ToastManager.Instance.Show(modeText);
        }
    }
}
