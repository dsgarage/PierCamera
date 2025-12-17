using System;
using UnityEngine;
using UnityEngine.UIElements;
using UniVRM10;

namespace AICam.Expression
{
    /// <summary>
    /// Issue #145/#146: VRMアバターのヒット領域を検出
    /// 顔タップで表情変更、体タップでポーズ変更
    /// </summary>
    public class VrmHitDetector : MonoBehaviour
    {
        /// <summary>
        /// ヒット領域の種類
        /// </summary>
        public enum VrmHitRegion
        {
            None,
            Face,   // 頭・顔（表情変更用）
            Body    // 胴体・腕・脚（ポーズ変更用）
        }

        [Header("Target")]
        [SerializeField] private Vrm10Instance vrmInstance;

        [Header("Controllers")]
        [SerializeField] private VrmExpressionController expressionController;
        [SerializeField] private VrmPoseController poseController;

        [Header("Detection Settings")]
        [Tooltip("UIをブロックするカメラ（UIToolkit用）")]
        [SerializeField] private Camera mainCamera;

        [Tooltip("タップ検出のレイヤーマスク")]
        [SerializeField] private LayerMask avatarLayerMask = ~0;

        [Tooltip("ダブルタップ判定の時間閾値（秒）")]
        [SerializeField] private float doubleTapThreshold = 0.3f;

        [Tooltip("タップと判定する最大移動距離（ピクセル）")]
        [SerializeField] private float tapDistanceThreshold = 20f;

        [Header("UIToolkit")]
        [Tooltip("UIToolkitのUIDocument（UI貫通防止用、未設定時は自動検索）")]
        [SerializeField] private UIDocument uiDocument;

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;
        [SerializeField] private bool drawDebugRay = false;

        // Humanoidボーン参照（Face領域判定用）
        private Transform _headBone;
        private Transform _neckBone;
        private Transform _spineBone;

        // タップ検出用
        private float _lastTapTime = 0f;
        private Vector2 _touchStartPosition;
        private bool _isTouching = false;

        // UIToolkit用キャッシュ
        private VisualElement _uiRoot;

        /// <summary>
        /// ヒット検出イベント
        /// </summary>
        public event Action<VrmHitRegion, Vector3> OnHitDetected;

        /// <summary>
        /// 顔タップイベント
        /// </summary>
        public event Action OnFaceTapped;

        /// <summary>
        /// 体タップイベント
        /// </summary>
        public event Action OnBodyTapped;

        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            // UIDocumentを自動検索
            if (uiDocument == null)
            {
                uiDocument = FindFirstObjectByType<UIDocument>();
            }
            if (uiDocument != null)
            {
                _uiRoot = uiDocument.rootVisualElement;
            }
        }

        private void Start()
        {
            Initialize();
        }

        /// <summary>
        /// VRMインスタンスを設定
        /// </summary>
        public void SetVrmInstance(Vrm10Instance instance)
        {
            vrmInstance = instance;
            Initialize();
        }

        /// <summary>
        /// ExpressionControllerを設定
        /// </summary>
        public void SetExpressionController(VrmExpressionController controller)
        {
            expressionController = controller;
        }

        /// <summary>
        /// 初期化
        /// </summary>
        public void Initialize()
        {
            if (vrmInstance == null)
            {
                vrmInstance = GetComponent<Vrm10Instance>();
            }

            if (vrmInstance == null)
            {
                Debug.LogWarning("[VrmHitDetector] Vrm10Instance not found");
                return;
            }

            // Humanoidボーンを取得
            var humanoid = vrmInstance.Humanoid;
            if (humanoid != null)
            {
                _headBone = humanoid.Head;
                _neckBone = humanoid.Neck;
                _spineBone = humanoid.Spine;

                if (debugLog)
                {
                    Debug.Log($"[VrmHitDetector] Humanoid bones - Head: {_headBone?.name}, Neck: {_neckBone?.name}, Spine: {_spineBone?.name}");
                }
            }

            // ExpressionControllerを自動検索
            if (expressionController == null)
            {
                expressionController = vrmInstance.GetComponent<VrmExpressionController>();
                if (expressionController == null)
                {
                    expressionController = vrmInstance.gameObject.AddComponent<VrmExpressionController>();
                    expressionController.SetVrmInstance(vrmInstance);
                }
            }

            // PoseControllerを自動検索
            if (poseController == null)
            {
                poseController = vrmInstance.GetComponent<VrmPoseController>();
                if (poseController == null)
                {
                    poseController = vrmInstance.gameObject.AddComponent<VrmPoseController>();
                    poseController.SetVrmInstance(vrmInstance);
                }
            }

            Debug.Log("[VrmHitDetector] Initialized");
        }

        private void Update()
        {
            HandleInput();
        }

        /// <summary>
        /// 入力処理
        /// </summary>
        private void HandleInput()
        {
            // タッチまたはマウス入力
            if (Input.GetMouseButtonDown(0))
            {
                _touchStartPosition = Input.mousePosition;
                _isTouching = true;
            }
            else if (Input.GetMouseButtonUp(0) && _isTouching)
            {
                _isTouching = false;

                Vector2 currentPos = Input.mousePosition;
                float distance = Vector2.Distance(_touchStartPosition, currentPos);

                // 移動が少ない場合はタップと判定
                if (distance <= tapDistanceThreshold)
                {
                    ProcessTap(currentPos);
                }
            }
        }

        /// <summary>
        /// タップ処理
        /// </summary>
        private void ProcessTap(Vector2 screenPosition)
        {
            // UI上のタップかチェック
            // 第1層: UIToolkit判定
            if (IsScreenPositionOverUIToolkit(screenPosition))
            {
                if (debugLog) Debug.Log("[VrmHitDetector] Tap on UIToolkit - ignored");
                return;
            }

            // 第2層: 従来型UI判定（EventSystem）
            if (UnityEngine.EventSystems.EventSystem.current != null &&
                UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject())
            {
                if (debugLog) Debug.Log("[VrmHitDetector] Tap on UI (EventSystem) - ignored");
                return;
            }

            // タップかダブルタップか判定
            float timeSinceLastTap = Time.time - _lastTapTime;
            bool isDoubleTap = timeSinceLastTap < doubleTapThreshold;
            _lastTapTime = Time.time;

            // シングルタップのみ処理（ダブルタップは別機能に使用可能）
            if (!isDoubleTap)
            {
                if (debugLog) Debug.Log("[VrmHitDetector] Single tap - waiting for potential double tap");
                return;
            }

            // ダブルタップ → レイキャストで領域判定
            if (debugLog) Debug.Log("[VrmHitDetector] Double tap detected!");

            VrmHitRegion region = DetectHitRegion(screenPosition, out Vector3 hitPoint);

            if (region != VrmHitRegion.None)
            {
                OnHitDetected?.Invoke(region, hitPoint);

                switch (region)
                {
                    case VrmHitRegion.Face:
                        OnFaceTapped?.Invoke();
                        HandleFaceTap();
                        break;
                    case VrmHitRegion.Body:
                        OnBodyTapped?.Invoke();
                        HandleBodyTap();
                        break;
                }
            }
        }

        /// <summary>
        /// レイキャストでヒット領域を判定
        /// </summary>
        private VrmHitRegion DetectHitRegion(Vector2 screenPosition, out Vector3 hitPoint)
        {
            hitPoint = Vector3.zero;

            if (mainCamera == null || vrmInstance == null) return VrmHitRegion.None;

            Ray ray = mainCamera.ScreenPointToRay(screenPosition);

            if (drawDebugRay)
            {
                Debug.DrawRay(ray.origin, ray.direction * 10f, Color.yellow, 1f);
            }

            if (Physics.Raycast(ray, out RaycastHit hit, 100f, avatarLayerMask))
            {
                // VRMの子オブジェクトか確認
                if (!hit.transform.IsChildOf(vrmInstance.transform) && hit.transform != vrmInstance.transform)
                {
                    if (debugLog) Debug.Log($"[VrmHitDetector] Hit object is not part of VRM: {hit.transform.name}");
                    return VrmHitRegion.None;
                }

                hitPoint = hit.point;

                // ヒット位置から領域を判定
                VrmHitRegion region = DetermineRegionFromHitPoint(hit.point, hit.transform);

                if (debugLog)
                {
                    Debug.Log($"[VrmHitDetector] Hit: {hit.transform.name} at {hit.point}, Region: {region}");
                }

                return region;
            }

            if (debugLog) Debug.Log("[VrmHitDetector] No hit detected");
            return VrmHitRegion.None;
        }

        /// <summary>
        /// ヒット位置から領域を判定
        /// </summary>
        private VrmHitRegion DetermineRegionFromHitPoint(Vector3 hitPoint, Transform hitTransform)
        {
            // 方法1: Humanoidボーンとの距離で判定
            if (_headBone != null)
            {
                float distanceToHead = Vector3.Distance(hitPoint, _headBone.position);

                // 頭の半径を推定（スケールに応じて調整）
                float headRadius = vrmInstance.transform.lossyScale.y * 0.15f;

                if (distanceToHead < headRadius)
                {
                    return VrmHitRegion.Face;
                }
            }

            // 方法2: Y座標による判定（Neckより上は顔）
            if (_neckBone != null)
            {
                if (hitPoint.y > _neckBone.position.y)
                {
                    return VrmHitRegion.Face;
                }
            }

            // 方法3: ヒットしたオブジェクト名で判定
            string hitName = hitTransform.name.ToLower();
            if (hitName.Contains("head") || hitName.Contains("face") ||
                hitName.Contains("hair") || hitName.Contains("eye") ||
                hitName.Contains("mouth") || hitName.Contains("ear"))
            {
                return VrmHitRegion.Face;
            }

            // それ以外は体
            return VrmHitRegion.Body;
        }

        /// <summary>
        /// 顔タップ時の処理
        /// </summary>
        private void HandleFaceTap()
        {
            if (expressionController != null)
            {
                expressionController.NextExpression();
                Debug.Log($"[VrmHitDetector] Face tapped → Expression: {expressionController.CurrentExpressionName}");
            }
        }

        /// <summary>
        /// 体タップ時の処理
        /// </summary>
        private void HandleBodyTap()
        {
            if (poseController != null)
            {
                poseController.NextPose();
                Debug.Log($"[VrmHitDetector] Body tapped → Pose: {poseController.CurrentPoseName}");
            }
            else
            {
                Debug.Log("[VrmHitDetector] Body tapped but PoseController not available");
            }
        }

        /// <summary>
        /// PoseControllerを設定
        /// </summary>
        public void SetPoseController(VrmPoseController controller)
        {
            poseController = controller;
        }

        /// <summary>
        /// 手動で顔タップをトリガー（UI等から呼び出し用）
        /// </summary>
        public void TriggerFaceTap()
        {
            OnFaceTapped?.Invoke();
            HandleFaceTap();
        }

        /// <summary>
        /// 手動で体タップをトリガー（UI等から呼び出し用）
        /// </summary>
        public void TriggerBodyTap()
        {
            OnBodyTapped?.Invoke();
            HandleBodyTap();
        }

        /// <summary>
        /// 指定されたスクリーン座標がUIToolkit上にあるかチェック
        /// RuntimePanelUtils.ScreenToPanelを使用して正確な座標変換を行う
        /// </summary>
        private bool IsScreenPositionOverUIToolkit(Vector2 screenPosition)
        {
            // UIDocumentが未設定または無効な場合は再取得を試みる
            if (uiDocument == null)
            {
                uiDocument = FindFirstObjectByType<UIDocument>();
                if (uiDocument == null) return false;
            }

            // rootが未取得の場合は取得
            if (_uiRoot == null)
            {
                _uiRoot = uiDocument.rootVisualElement;
                if (_uiRoot == null) return false;
            }

            var panel = _uiRoot.panel;
            if (panel == null) return false;

            // スクリーン座標をパネル座標に変換
            // UIToolkit: Y軸が上から下
            // Unity Screen: Y軸が下から上
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
                panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y)
            );

            // パネル座標でヒットテスト
            var pickedElement = panel.Pick(panelPosition);

            if (pickedElement != null && pickedElement.pickingMode == PickingMode.Position)
            {
                if (debugLog)
                {
                    Debug.Log($"[VrmHitDetector] UIToolkit hit: {pickedElement.name} at panel({panelPosition})");
                }
                return true;
            }

            return false;
        }
    }
}
