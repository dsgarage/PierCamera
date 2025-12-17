using System;
using System.Collections.Generic;
using UnityEngine;
using UniVRM10;

namespace AICam.Expression
{
    /// <summary>
    /// Issue #145: VRMアバターの表情（Expression）を制御するコントローラー
    /// VRM 1.0のExpression APIを使用してBlendShapeベースの表情を制御
    /// </summary>
    public class VrmExpressionController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Vrm10Instance vrmInstance;

        [Header("Expression Settings")]
        [Tooltip("表情の遷移速度")]
        [Range(0.1f, 20f)]
        [SerializeField] private float transitionSpeed = 10f;

        [Tooltip("表情の保持時間（0で永続）")]
        [SerializeField] private float holdDuration = 0f;

        [Tooltip("自動的にニュートラルに戻る")]
        [SerializeField] private bool autoResetToNeutral = false;

        [Header("Custom Expressions (Optional)")]
        [Tooltip("追加のカスタム表情名（VRMに含まれる表情は自動検出されます）")]
        [SerializeField] private List<string> customExpressionNames = new List<string>();

        [Header("Debug")]
        [SerializeField] private bool debugLog = false;

        // 現在の表情インデックス（プリセット+カスタム）
        private int _currentExpressionIndex = -1;
        private List<ExpressionKey> _availableExpressions = new List<ExpressionKey>();

        // 空の表情（BlendShapeバインディングがない）を記録
        private HashSet<ExpressionKey> _emptyExpressions = new HashSet<ExpressionKey>();

        // BlendShapeが見つからない表情（バインディングはあるがメッシュに存在しない）
        private Dictionary<ExpressionKey, List<string>> _missingBlendShapeExpressions = new Dictionary<ExpressionKey, List<string>>();

        // 表情の補間用
        private Dictionary<ExpressionKey, float> _currentWeights = new Dictionary<ExpressionKey, float>();
        private Dictionary<ExpressionKey, float> _targetWeights = new Dictionary<ExpressionKey, float>();

        // 保持タイマー
        private float _holdTimer = 0f;
        private bool _isHolding = false;

        // 除外する視線表情プリセット（ボーン制御が多く、BlendShapeで動作しないことが多い）
        private static readonly HashSet<ExpressionPreset> ExcludedPresets = new HashSet<ExpressionPreset>
        {
            ExpressionPreset.lookUp,
            ExpressionPreset.lookDown,
            ExpressionPreset.lookLeft,
            ExpressionPreset.lookRight
        };

        /// <summary>
        /// 利用可能な表情の一覧
        /// </summary>
        public IReadOnlyList<ExpressionKey> AvailableExpressions => _availableExpressions;

        /// <summary>
        /// 現在の表情インデックス
        /// </summary>
        public int CurrentExpressionIndex => _currentExpressionIndex;

        /// <summary>
        /// 現在の表情名
        /// </summary>
        public string CurrentExpressionName
        {
            get
            {
                if (_currentExpressionIndex < 0 || _currentExpressionIndex >= _availableExpressions.Count)
                    return "Neutral";
                return _availableExpressions[_currentExpressionIndex].ToString();
            }
        }

        /// <summary>
        /// 表情変更イベント
        /// </summary>
        public event Action<int, string> OnExpressionChanged;

        /// <summary>
        /// 空の表情（BlendShapeバインディングがない）に切り替わったときのイベント
        /// </summary>
        public event Action<string> OnEmptyExpressionSelected;

        /// <summary>
        /// BlendShapeが見つからない表情に切り替わったときのイベント
        /// (expressionName, missingDetails)
        /// </summary>
        public event Action<string, string> OnMissingBlendShapeExpressionSelected;

        /// <summary>
        /// 空の表情リスト（初期化時に検出）
        /// </summary>
        public IReadOnlyCollection<string> EmptyExpressionNames => GetEmptyExpressionNames();

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
                Debug.LogWarning("[VrmExpressionController] Vrm10Instance is null");
                return;
            }

            BuildExpressionList();

            // ウェイト辞書の初期化
            _currentWeights.Clear();
            _targetWeights.Clear();
            foreach (var key in _availableExpressions)
            {
                _currentWeights[key] = 0f;
                _targetWeights[key] = 0f;
            }

            _currentExpressionIndex = -1;

            if (debugLog)
            {
                Debug.Log($"[VrmExpressionController] Initialized with {_availableExpressions.Count} expressions");
                if (_emptyExpressions.Count > 0)
                {
                    Debug.Log($"[VrmExpressionController] Empty expressions (no BlendShape bindings): {string.Join(", ", GetEmptyExpressionNames())}");
                }
            }
        }

        /// <summary>
        /// 利用可能な表情リストを構築
        /// </summary>
        private void BuildExpressionList()
        {
            _availableExpressions.Clear();
            _emptyExpressions.Clear();
            _missingBlendShapeExpressions.Clear();

            if (vrmInstance == null || vrmInstance.Vrm == null) return;

            var expressionData = vrmInstance.Vrm.Expression;
            if (expressionData == null) return;

            // VRM10ObjectExpressionのClipsプロパティからすべての表情を取得
            int index = 0;
            foreach (var (preset, clip) in expressionData.Clips)
            {
                if (clip == null)
                {
                    Debug.LogWarning($"[VrmExpressionController] Index {index}: clip is null (preset: {preset})");
                    index++;
                    continue;
                }

                // 視線表情は除外（ボーン制御が多く、バグに見えるため）
                if (ExcludedPresets.Contains(preset))
                {
                    if (debugLog)
                    {
                        Debug.Log($"[VrmExpressionController] Index {index}: {preset} skipped (look direction expression)");
                    }
                    index++;
                    continue;
                }

                ExpressionKey key;
                if (preset == ExpressionPreset.custom)
                {
                    key = ExpressionKey.CreateCustom(clip.name);
                }
                else
                {
                    key = ExpressionKey.CreateFromPreset(preset);
                }

                _availableExpressions.Add(key);

                // BlendShapeバインディングが空かチェック
                int morphCount = clip.MorphTargetBindings?.Length ?? 0;
                int materialCount = clip.MaterialColorBindings?.Length ?? 0;
                int uvCount = clip.MaterialUVBindings?.Length ?? 0;
                bool hasBindings = morphCount > 0;
                bool hasMaterialBindings = materialCount > 0;
                bool hasUVBindings = uvCount > 0;

                // バインディングがあっても、実際のメッシュにBlendShapeが存在するか検証
                bool hasValidBindings = false;
                List<string> missingBindings = new List<string>();

                if (hasBindings)
                {
                    foreach (var binding in clip.MorphTargetBindings)
                    {
                        // メッシュを検索
                        var meshTransform = vrmInstance.transform.Find(binding.RelativePath);
                        if (meshTransform != null)
                        {
                            var smr = meshTransform.GetComponent<SkinnedMeshRenderer>();
                            if (smr != null && smr.sharedMesh != null)
                            {
                                int blendShapeCount = smr.sharedMesh.blendShapeCount;
                                if (binding.Index >= 0 && binding.Index < blendShapeCount)
                                {
                                    hasValidBindings = true;
                                }
                                else
                                {
                                    string blendShapeName = $"Index {binding.Index}";
                                    missingBindings.Add($"{binding.RelativePath}:{blendShapeName}");
                                }
                            }
                            else
                            {
                                missingBindings.Add($"{binding.RelativePath}: SkinnedMeshRenderer not found");
                            }
                        }
                        else
                        {
                            missingBindings.Add($"{binding.RelativePath}: mesh not found");
                        }
                    }
                }

                if (!hasBindings && !hasMaterialBindings && !hasUVBindings)
                {
                    _emptyExpressions.Add(key);
                }
                else if (hasBindings && !hasValidBindings)
                {
                    // バインディングはあるがメッシュ上のBlendShapeが見つからない
                    _emptyExpressions.Add(key);
                    _missingBlendShapeExpressions[key] = missingBindings;
                }

                // 常にログ出力（デバッグ中）
                string status;
                if (!hasBindings && !hasMaterialBindings && !hasUVBindings)
                {
                    status = "⚠️ EMPTY (no bindings)";
                }
                else if (hasBindings && !hasValidBindings)
                {
                    status = $"⚠️ INVALID (BlendShape not found: {string.Join(", ", missingBindings)})";
                }
                else
                {
                    status = "✓";
                }
                Debug.Log($"[VrmExpressionController] Index {index}: {key} (preset: {preset}, name: {clip.name}, morph: {morphCount}, material: {materialCount}, uv: {uvCount}) {status}");

                index++;
            }

            Debug.Log($"[VrmExpressionController] Total expressions: {_availableExpressions.Count}, Empty: {_emptyExpressions.Count}");
        }

        private void Update()
        {
            if (vrmInstance == null) return;

            // 表情の補間
            UpdateExpressionWeights();

            // 保持タイマー
            if (_isHolding && holdDuration > 0)
            {
                _holdTimer += Time.deltaTime;
                if (_holdTimer >= holdDuration)
                {
                    _isHolding = false;
                    if (autoResetToNeutral)
                    {
                        ResetToNeutral();
                    }
                }
            }
        }

        /// <summary>
        /// 表情ウェイトの補間更新
        /// </summary>
        private void UpdateExpressionWeights()
        {
            if (vrmInstance.Runtime?.Expression == null) return;

            float t = Time.deltaTime * transitionSpeed;

            foreach (var key in _availableExpressions)
            {
                if (!_currentWeights.ContainsKey(key)) continue;

                float current = _currentWeights[key];
                float target = _targetWeights.GetValueOrDefault(key, 0f);

                if (Mathf.Abs(current - target) > 0.001f)
                {
                    current = Mathf.Lerp(current, target, t);
                    _currentWeights[key] = current;

                    // VRMに適用
                    vrmInstance.Runtime.Expression.SetWeight(key, current);
                }
            }
        }

        /// <summary>
        /// 次の表情に切り替え
        /// </summary>
        public void NextExpression()
        {
            if (_availableExpressions.Count == 0) return;

            // 現在の表情をリセット
            if (_currentExpressionIndex >= 0 && _currentExpressionIndex < _availableExpressions.Count)
            {
                _targetWeights[_availableExpressions[_currentExpressionIndex]] = 0f;
            }

            // 次のインデックスへ
            _currentExpressionIndex++;
            if (_currentExpressionIndex >= _availableExpressions.Count)
            {
                _currentExpressionIndex = -1; // Neutralに戻る
            }

            // 新しい表情を設定
            ExpressionKey currentKey = default;
            if (_currentExpressionIndex >= 0)
            {
                currentKey = _availableExpressions[_currentExpressionIndex];
                _targetWeights[currentKey] = 1f;
            }

            _holdTimer = 0f;
            _isHolding = holdDuration > 0;

            OnExpressionChanged?.Invoke(_currentExpressionIndex, CurrentExpressionName);

            // 空の表情またはBlendShape不一致の場合はイベント発火
            if (_currentExpressionIndex >= 0 && _emptyExpressions.Contains(currentKey))
            {
                if (_missingBlendShapeExpressions.TryGetValue(currentKey, out var missingList))
                {
                    // BlendShapeが見つからない
                    OnMissingBlendShapeExpressionSelected?.Invoke(CurrentExpressionName, string.Join(", ", missingList));
                }
                else
                {
                    // バインディング自体がない
                    OnEmptyExpressionSelected?.Invoke(CurrentExpressionName);
                }
            }

            if (debugLog)
            {
                Debug.Log($"[VrmExpressionController] Expression changed to: {CurrentExpressionName} (index: {_currentExpressionIndex})");
            }
        }

        /// <summary>
        /// 前の表情に切り替え
        /// </summary>
        public void PreviousExpression()
        {
            if (_availableExpressions.Count == 0) return;

            // 現在の表情をリセット
            if (_currentExpressionIndex >= 0 && _currentExpressionIndex < _availableExpressions.Count)
            {
                _targetWeights[_availableExpressions[_currentExpressionIndex]] = 0f;
            }

            // 前のインデックスへ
            _currentExpressionIndex--;
            if (_currentExpressionIndex < -1)
            {
                _currentExpressionIndex = _availableExpressions.Count - 1;
            }

            // 新しい表情を設定
            if (_currentExpressionIndex >= 0)
            {
                var key = _availableExpressions[_currentExpressionIndex];
                _targetWeights[key] = 1f;
            }

            _holdTimer = 0f;
            _isHolding = holdDuration > 0;

            OnExpressionChanged?.Invoke(_currentExpressionIndex, CurrentExpressionName);

            if (debugLog)
            {
                Debug.Log($"[VrmExpressionController] Expression changed to: {CurrentExpressionName} (index: {_currentExpressionIndex})");
            }
        }

        /// <summary>
        /// インデックスで表情を設定
        /// </summary>
        public void SetExpressionByIndex(int index)
        {
            if (index < -1 || index >= _availableExpressions.Count) return;
            if (index == _currentExpressionIndex) return;

            // 現在の表情をリセット
            if (_currentExpressionIndex >= 0 && _currentExpressionIndex < _availableExpressions.Count)
            {
                _targetWeights[_availableExpressions[_currentExpressionIndex]] = 0f;
            }

            _currentExpressionIndex = index;

            // 新しい表情を設定
            if (_currentExpressionIndex >= 0)
            {
                var key = _availableExpressions[_currentExpressionIndex];
                _targetWeights[key] = 1f;
            }

            _holdTimer = 0f;
            _isHolding = holdDuration > 0;

            OnExpressionChanged?.Invoke(_currentExpressionIndex, CurrentExpressionName);
        }

        /// <summary>
        /// 名前で表情を設定
        /// </summary>
        public void SetExpressionByName(string expressionName)
        {
            if (string.IsNullOrEmpty(expressionName))
            {
                ResetToNeutral();
                return;
            }

            for (int i = 0; i < _availableExpressions.Count; i++)
            {
                if (_availableExpressions[i].ToString().Equals(expressionName, StringComparison.OrdinalIgnoreCase))
                {
                    SetExpressionByIndex(i);
                    return;
                }
            }

            Debug.LogWarning($"[VrmExpressionController] Expression not found: {expressionName}");
        }

        /// <summary>
        /// 表情のウェイトを直接設定（ブレンド用）
        /// </summary>
        public void SetExpressionWeight(ExpressionKey key, float weight)
        {
            weight = Mathf.Clamp01(weight);

            if (_targetWeights.ContainsKey(key))
            {
                _targetWeights[key] = weight;
            }
        }

        /// <summary>
        /// ニュートラル（無表情）に戻す
        /// </summary>
        public void ResetToNeutral()
        {
            // 全表情をリセット
            foreach (var key in _availableExpressions)
            {
                _targetWeights[key] = 0f;
            }

            _currentExpressionIndex = -1;
            _isHolding = false;
            _holdTimer = 0f;

            OnExpressionChanged?.Invoke(-1, "Neutral");

            if (debugLog)
            {
                Debug.Log("[VrmExpressionController] Reset to Neutral");
            }
        }

        /// <summary>
        /// 即座に表情を適用（補間なし）
        /// </summary>
        public void ApplyImmediate()
        {
            if (vrmInstance?.Runtime?.Expression == null) return;

            foreach (var key in _availableExpressions)
            {
                float target = _targetWeights.GetValueOrDefault(key, 0f);
                _currentWeights[key] = target;
                vrmInstance.Runtime.Expression.SetWeight(key, target);
            }
        }

        /// <summary>
        /// 表情名の一覧を取得
        /// </summary>
        public List<string> GetExpressionNames()
        {
            var names = new List<string> { "Neutral" };
            foreach (var key in _availableExpressions)
            {
                names.Add(key.ToString());
            }
            return names;
        }

        /// <summary>
        /// 空の表情名リストを取得
        /// </summary>
        private List<string> GetEmptyExpressionNames()
        {
            var names = new List<string>();
            foreach (var key in _emptyExpressions)
            {
                names.Add(key.ToString());
            }
            return names;
        }

        /// <summary>
        /// 指定した表情が空（BlendShapeバインディングがない）かどうか
        /// </summary>
        public bool IsExpressionEmpty(string expressionName)
        {
            foreach (var key in _emptyExpressions)
            {
                if (key.ToString().Equals(expressionName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 空の表情の数
        /// </summary>
        public int EmptyExpressionCount => _emptyExpressions.Count;

        /// <summary>
        /// 有効な（空でない）表情の数
        /// </summary>
        public int ValidExpressionCount => _availableExpressions.Count - _emptyExpressions.Count;
    }
}
