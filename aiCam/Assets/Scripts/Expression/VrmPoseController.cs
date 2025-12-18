using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

namespace AICam.Expression
{
    /// <summary>
    /// Issue #146: VRMアバターのポーズを制御するコントローラー
    /// BlendShapeベースのポーズアニメーションを制御
    /// ランタイムではTransformを含むAnimationClipは読み込めないため、BlendShapeに限定
    /// </summary>
    public class VrmPoseController : MonoBehaviour
    {
        /// <summary>
        /// ポーズデータ（BlendShapeベース）
        /// </summary>
        [Serializable]
        public class PoseData
        {
            public string name;
            public string description;
            public List<BlendShapeWeight> blendShapeWeights = new List<BlendShapeWeight>();
        }

        /// <summary>
        /// BlendShapeウェイト情報
        /// </summary>
        [Serializable]
        public class BlendShapeWeight
        {
            public string meshPath;       // SkinnedMeshRendererのパス
            public int blendShapeIndex;   // BlendShapeのインデックス
            public float weight;          // 0-100
        }

        [Header("Target")]
        [SerializeField] private Vrm10Instance vrmInstance;

        [Header("Pose Settings")]
        [Tooltip("ポーズの遷移速度")]
        [Range(0.1f, 20f)]
        [SerializeField] private float transitionSpeed = 5f;

        [Header("Preset Poses")]
        [SerializeField] private List<PoseData> presetPoses = new List<PoseData>();

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        // 現在のポーズインデックス（-1 = デフォルト/Idle）
        private int _currentPoseIndex = -1;

        // BlendShape補間用
        private Dictionary<(string, int), float> _currentWeights = new Dictionary<(string, int), float>();
        private Dictionary<(string, int), float> _targetWeights = new Dictionary<(string, int), float>();

        // SkinnedMeshRenderer参照キャッシュ
        private Dictionary<string, SkinnedMeshRenderer> _smrCache = new Dictionary<string, SkinnedMeshRenderer>();

        /// <summary>
        /// 利用可能なポーズの一覧
        /// </summary>
        public IReadOnlyList<PoseData> AvailablePoses => presetPoses;

        /// <summary>
        /// 現在のポーズインデックス
        /// </summary>
        public int CurrentPoseIndex => _currentPoseIndex;

        /// <summary>
        /// 現在のポーズ名
        /// </summary>
        public string CurrentPoseName
        {
            get
            {
                if (_currentPoseIndex < 0 || _currentPoseIndex >= presetPoses.Count)
                    return "Idle";
                return presetPoses[_currentPoseIndex].name;
            }
        }

        /// <summary>
        /// ポーズ変更イベント
        /// </summary>
        public event Action<int, string> OnPoseChanged;

        private void Awake()
        {
            if (vrmInstance == null)
            {
                vrmInstance = GetComponent<Vrm10Instance>();
            }
        }

        private void Start()
        {
            Initialize();
        }

        /// <summary>
        /// VRMインスタンスを設定して初期化
        /// </summary>
        public void SetVrmInstance(Vrm10Instance instance)
        {
            vrmInstance = instance;
            Initialize();
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public void Initialize()
        {
            if (vrmInstance == null)
            {
                Debug.LogWarning("[VrmPoseController] Vrm10Instance is null");
                return;
            }

            BuildSmrCache();
            _currentWeights.Clear();
            _targetWeights.Clear();
            _currentPoseIndex = -1;

            if (debugLog)
            {
                Debug.Log($"[VrmPoseController] Initialized with {presetPoses.Count} poses, {_smrCache.Count} SMRs cached");
            }
        }

        /// <summary>
        /// SkinnedMeshRendererのキャッシュを構築
        /// </summary>
        private void BuildSmrCache()
        {
            _smrCache.Clear();

            if (vrmInstance == null) return;

            var renderers = vrmInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var renderer in renderers)
            {
                string path = GetHierarchyPath(renderer.transform);
                _smrCache[path] = renderer;

                if (debugLog)
                {
                    Debug.Log($"[VrmPoseController] Cached SMR: {path}");
                }
            }
        }

        /// <summary>
        /// Transformのヒエラルキーパスを取得
        /// </summary>
        private string GetHierarchyPath(Transform t)
        {
            var path = new List<string>();
            while (t != null && t != vrmInstance.transform)
            {
                path.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", path);
        }

        private void Update()
        {
            if (vrmInstance == null) return;

            UpdateBlendShapeWeights();
        }

        /// <summary>
        /// BlendShapeウェイトの補間更新
        /// </summary>
        private void UpdateBlendShapeWeights()
        {
            float t = Time.deltaTime * transitionSpeed;

            foreach (var kvp in _targetWeights)
            {
                var key = kvp.Key;
                float target = kvp.Value;

                if (!_currentWeights.TryGetValue(key, out float current))
                {
                    current = 0f;
                }

                if (Mathf.Abs(current - target) > 0.01f)
                {
                    current = Mathf.Lerp(current, target, t);
                    _currentWeights[key] = current;

                    // SMRに適用
                    if (_smrCache.TryGetValue(key.Item1, out var smr) && smr != null)
                    {
                        if (key.Item2 >= 0 && key.Item2 < smr.sharedMesh.blendShapeCount)
                        {
                            smr.SetBlendShapeWeight(key.Item2, current);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 次のポーズに切り替え
        /// </summary>
        public void NextPose()
        {
            if (presetPoses.Count == 0) return;

            // 現在のポーズをリセット
            ResetCurrentPoseWeights();

            // 次のインデックスへ
            _currentPoseIndex++;
            if (_currentPoseIndex >= presetPoses.Count)
            {
                _currentPoseIndex = -1; // Idleに戻る
            }

            // 新しいポーズを適用
            ApplyCurrentPose();

            OnPoseChanged?.Invoke(_currentPoseIndex, CurrentPoseName);

            if (debugLog)
            {
                Debug.Log($"[VrmPoseController] Pose changed to: {CurrentPoseName} (index: {_currentPoseIndex})");
            }
        }

        /// <summary>
        /// 前のポーズに切り替え
        /// </summary>
        public void PreviousPose()
        {
            if (presetPoses.Count == 0) return;

            // 現在のポーズをリセット
            ResetCurrentPoseWeights();

            // 前のインデックスへ
            _currentPoseIndex--;
            if (_currentPoseIndex < -1)
            {
                _currentPoseIndex = presetPoses.Count - 1;
            }

            // 新しいポーズを適用
            ApplyCurrentPose();

            OnPoseChanged?.Invoke(_currentPoseIndex, CurrentPoseName);

            if (debugLog)
            {
                Debug.Log($"[VrmPoseController] Pose changed to: {CurrentPoseName} (index: {_currentPoseIndex})");
            }
        }

        /// <summary>
        /// インデックスでポーズを設定
        /// </summary>
        public void SetPoseByIndex(int index)
        {
            if (index < -1 || index >= presetPoses.Count) return;
            if (index == _currentPoseIndex) return;

            ResetCurrentPoseWeights();
            _currentPoseIndex = index;
            ApplyCurrentPose();

            OnPoseChanged?.Invoke(_currentPoseIndex, CurrentPoseName);
        }

        /// <summary>
        /// 名前でポーズを設定
        /// </summary>
        public void SetPoseByName(string poseName)
        {
            if (string.IsNullOrEmpty(poseName))
            {
                ResetToIdle();
                return;
            }

            for (int i = 0; i < presetPoses.Count; i++)
            {
                if (presetPoses[i].name.Equals(poseName, StringComparison.OrdinalIgnoreCase))
                {
                    SetPoseByIndex(i);
                    return;
                }
            }

            Debug.LogWarning($"[VrmPoseController] Pose not found: {poseName}");
        }

        /// <summary>
        /// 現在のポーズのウェイトをリセット
        /// </summary>
        private void ResetCurrentPoseWeights()
        {
            if (_currentPoseIndex >= 0 && _currentPoseIndex < presetPoses.Count)
            {
                var pose = presetPoses[_currentPoseIndex];
                foreach (var weight in pose.blendShapeWeights)
                {
                    var key = (weight.meshPath, weight.blendShapeIndex);
                    _targetWeights[key] = 0f;
                }
            }
        }

        /// <summary>
        /// 現在のポーズを適用
        /// </summary>
        private void ApplyCurrentPose()
        {
            if (_currentPoseIndex < 0 || _currentPoseIndex >= presetPoses.Count)
            {
                // Idle状態
                return;
            }

            var pose = presetPoses[_currentPoseIndex];
            foreach (var weight in pose.blendShapeWeights)
            {
                var key = (weight.meshPath, weight.blendShapeIndex);
                _targetWeights[key] = weight.weight;
            }
        }

        /// <summary>
        /// Idle状態に戻す
        /// </summary>
        public void ResetToIdle()
        {
            ResetCurrentPoseWeights();
            _currentPoseIndex = -1;

            OnPoseChanged?.Invoke(-1, "Idle");

            if (debugLog)
            {
                Debug.Log("[VrmPoseController] Reset to Idle");
            }
        }

        /// <summary>
        /// 即座にポーズを適用（補間なし）
        /// </summary>
        public void ApplyImmediate()
        {
            foreach (var kvp in _targetWeights)
            {
                _currentWeights[kvp.Key] = kvp.Value;

                if (_smrCache.TryGetValue(kvp.Key.Item1, out var smr) && smr != null)
                {
                    if (kvp.Key.Item2 >= 0 && kvp.Key.Item2 < smr.sharedMesh.blendShapeCount)
                    {
                        smr.SetBlendShapeWeight(kvp.Key.Item2, kvp.Value);
                    }
                }
            }
        }

        /// <summary>
        /// ポーズ名の一覧を取得
        /// </summary>
        public List<string> GetPoseNames()
        {
            var names = new List<string> { "Idle" };
            foreach (var pose in presetPoses)
            {
                names.Add(pose.name);
            }
            return names;
        }

        /// <summary>
        /// 新しいポーズを追加
        /// </summary>
        public void AddPose(PoseData pose)
        {
            if (pose == null) return;
            presetPoses.Add(pose);

            if (debugLog)
            {
                Debug.Log($"[VrmPoseController] Added pose: {pose.name}");
            }
        }

        /// <summary>
        /// 現在のBlendShape状態からポーズを作成
        /// </summary>
        public PoseData CreatePoseFromCurrentState(string poseName)
        {
            var pose = new PoseData
            {
                name = poseName,
                description = $"Created at {DateTime.Now}",
                blendShapeWeights = new List<BlendShapeWeight>()
            };

            foreach (var kvp in _smrCache)
            {
                var smr = kvp.Value;
                if (smr == null || smr.sharedMesh == null) continue;

                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    float weight = smr.GetBlendShapeWeight(i);
                    if (weight > 0.1f) // 0より大きいものだけ保存
                    {
                        pose.blendShapeWeights.Add(new BlendShapeWeight
                        {
                            meshPath = kvp.Key,
                            blendShapeIndex = i,
                            weight = weight
                        });
                    }
                }
            }

            return pose;
        }
    }
}
