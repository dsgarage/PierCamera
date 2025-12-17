using UnityEngine;
using UniVRM10;

namespace AICam.Expression
{
    /// <summary>
    /// Issue #145: VRM読み込み時に表情/ポーズシステムを自動セットアップ
    /// RuntimeAvatarLoaderから呼び出されて動作
    /// </summary>
    public class VrmExpressionSetup : MonoBehaviour
    {
        [Header("Auto Setup")]
        [Tooltip("VRM読み込み時に自動的に表情コントローラーを追加")]
        [SerializeField] private bool autoSetupExpression = true;

        [Tooltip("VRM読み込み時に自動的にポーズコントローラーを追加")]
        [SerializeField] private bool autoSetupPose = true;

        [Tooltip("VRM読み込み時に自動的にヒット検出を追加")]
        [SerializeField] private bool autoSetupHitDetector = true;

        [Header("Expression Settings")]
        [Tooltip("表情の遷移速度")]
        [SerializeField] private float transitionSpeed = 10f;

        [Tooltip("デバッグログを出力")]
        [SerializeField] private bool debugLog = false;

        private VrmExpressionController _currentExpressionController;
        private VrmPoseController _currentPoseController;
        private VrmHitDetector _currentHitDetector;

        /// <summary>
        /// 現在のExpressionController
        /// </summary>
        public VrmExpressionController CurrentExpressionController => _currentExpressionController;

        /// <summary>
        /// 現在のPoseController
        /// </summary>
        public VrmPoseController CurrentPoseController => _currentPoseController;

        /// <summary>
        /// 現在のHitDetector
        /// </summary>
        public VrmHitDetector CurrentHitDetector => _currentHitDetector;

        /// <summary>
        /// VRMがロードされた後に呼び出す
        /// </summary>
        public void OnVrmLoaded(GameObject avatarRoot)
        {
            if (avatarRoot == null)
            {
                Debug.LogWarning("[VrmExpressionSetup] Avatar root is null");
                return;
            }

            var vrmInstance = avatarRoot.GetComponent<Vrm10Instance>();
            if (vrmInstance == null)
            {
                Debug.LogWarning("[VrmExpressionSetup] Vrm10Instance not found on avatar");
                return;
            }

            SetupExpressionSystem(vrmInstance);
        }

        /// <summary>
        /// 表情システムをセットアップ
        /// </summary>
        public void SetupExpressionSystem(Vrm10Instance vrmInstance)
        {
            if (vrmInstance == null) return;

            // 既存のコントローラーをクリーンアップ
            CleanupCurrentControllers();

            // ExpressionController
            if (autoSetupExpression)
            {
                _currentExpressionController = vrmInstance.gameObject.GetComponent<VrmExpressionController>();
                if (_currentExpressionController == null)
                {
                    _currentExpressionController = vrmInstance.gameObject.AddComponent<VrmExpressionController>();
                }

                _currentExpressionController.SetVrmInstance(vrmInstance);

                if (debugLog)
                {
                    Debug.Log($"[VrmExpressionSetup] ExpressionController setup complete - {_currentExpressionController.AvailableExpressions.Count} expressions available");
                }
            }

            // PoseController
            if (autoSetupPose)
            {
                _currentPoseController = vrmInstance.gameObject.GetComponent<VrmPoseController>();
                if (_currentPoseController == null)
                {
                    _currentPoseController = vrmInstance.gameObject.AddComponent<VrmPoseController>();
                }

                _currentPoseController.SetVrmInstance(vrmInstance);

                if (debugLog)
                {
                    Debug.Log($"[VrmExpressionSetup] PoseController setup complete - {_currentPoseController.AvailablePoses.Count} poses available");
                }
            }

            // HitDetector
            if (autoSetupHitDetector)
            {
                _currentHitDetector = vrmInstance.gameObject.GetComponent<VrmHitDetector>();
                if (_currentHitDetector == null)
                {
                    _currentHitDetector = vrmInstance.gameObject.AddComponent<VrmHitDetector>();
                }

                _currentHitDetector.SetVrmInstance(vrmInstance);
                _currentHitDetector.SetExpressionController(_currentExpressionController);
                _currentHitDetector.SetPoseController(_currentPoseController);

                if (debugLog)
                {
                    Debug.Log("[VrmExpressionSetup] HitDetector setup complete");
                }
            }

            // イベント登録
            if (_currentExpressionController != null)
            {
                _currentExpressionController.OnExpressionChanged += OnExpressionChanged;
            }

            if (_currentPoseController != null)
            {
                _currentPoseController.OnPoseChanged += OnPoseChanged;
            }

            Debug.Log("[VrmExpressionSetup] VRM expression/pose system setup complete");
        }

        /// <summary>
        /// 既存のコントローラーをクリーンアップ
        /// </summary>
        private void CleanupCurrentControllers()
        {
            if (_currentExpressionController != null)
            {
                _currentExpressionController.OnExpressionChanged -= OnExpressionChanged;
            }

            if (_currentPoseController != null)
            {
                _currentPoseController.OnPoseChanged -= OnPoseChanged;
            }

            _currentExpressionController = null;
            _currentPoseController = null;
            _currentHitDetector = null;
        }

        /// <summary>
        /// ポーズ変更時のコールバック
        /// </summary>
        private void OnPoseChanged(int index, string name)
        {
            if (debugLog)
            {
                Debug.Log($"[VrmExpressionSetup] Pose changed: {name} (index: {index})");
            }
        }

        /// <summary>
        /// 表情変更時のコールバック
        /// </summary>
        private void OnExpressionChanged(int index, string name)
        {
            if (debugLog)
            {
                Debug.Log($"[VrmExpressionSetup] Expression changed: {name} (index: {index})");
            }
        }

        /// <summary>
        /// 次の表情に切り替え（外部からのトリガー用）
        /// </summary>
        public void NextExpression()
        {
            _currentExpressionController?.NextExpression();
        }

        /// <summary>
        /// 前の表情に切り替え（外部からのトリガー用）
        /// </summary>
        public void PreviousExpression()
        {
            _currentExpressionController?.PreviousExpression();
        }

        /// <summary>
        /// 表情をリセット（外部からのトリガー用）
        /// </summary>
        public void ResetExpression()
        {
            _currentExpressionController?.ResetToNeutral();
        }

        /// <summary>
        /// 名前で表情を設定
        /// </summary>
        public void SetExpression(string expressionName)
        {
            _currentExpressionController?.SetExpressionByName(expressionName);
        }

        /// <summary>
        /// インデックスで表情を設定
        /// </summary>
        public void SetExpression(int index)
        {
            _currentExpressionController?.SetExpressionByIndex(index);
        }

        /// <summary>
        /// 次のポーズに切り替え（外部からのトリガー用）
        /// </summary>
        public void NextPose()
        {
            _currentPoseController?.NextPose();
        }

        /// <summary>
        /// 前のポーズに切り替え（外部からのトリガー用）
        /// </summary>
        public void PreviousPose()
        {
            _currentPoseController?.PreviousPose();
        }

        /// <summary>
        /// ポーズをリセット（外部からのトリガー用）
        /// </summary>
        public void ResetPose()
        {
            _currentPoseController?.ResetToIdle();
        }

        /// <summary>
        /// 名前でポーズを設定
        /// </summary>
        public void SetPose(string poseName)
        {
            _currentPoseController?.SetPoseByName(poseName);
        }

        /// <summary>
        /// インデックスでポーズを設定
        /// </summary>
        public void SetPose(int index)
        {
            _currentPoseController?.SetPoseByIndex(index);
        }

        private void OnDestroy()
        {
            CleanupCurrentControllers();
        }
    }
}
