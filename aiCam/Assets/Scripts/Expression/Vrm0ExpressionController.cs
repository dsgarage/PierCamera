using System;
using System.Collections.Generic;
using UnityEngine;
using VRM;

namespace AICam.Expression
{
    /// <summary>
    /// Issue #145/#411: VRM 0.x用の表情コントローラー
    /// VRMBlendShapeProxyを使用してBlendShapeベースの表情を制御
    /// </summary>
    public class Vrm0ExpressionController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private VRMBlendShapeProxy blendShapeProxy;

        [Header("Debug")]
        [SerializeField] private bool debugLog = true;

        // 現在の表情インデックス
        private int _currentExpressionIndex = -1;
        private List<BlendShapeKey> _availableExpressions = new List<BlendShapeKey>();

        /// <summary>
        /// 利用可能な表情の一覧
        /// </summary>
        public IReadOnlyList<BlendShapeKey> AvailableExpressions => _availableExpressions;

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
                return _availableExpressions[_currentExpressionIndex].Name;
            }
        }

        /// <summary>
        /// 表情変更イベント
        /// </summary>
        public event Action<int, string> OnExpressionChanged;

        private void Awake()
        {
            if (blendShapeProxy == null)
            {
                blendShapeProxy = GetComponent<VRMBlendShapeProxy>();
            }
        }

        private void Start()
        {
            Initialize();
        }

        /// <summary>
        /// BlendShapeProxyを設定して初期化
        /// </summary>
        public void SetBlendShapeProxy(VRMBlendShapeProxy proxy)
        {
            blendShapeProxy = proxy;
            Initialize();
        }

        /// <summary>
        /// 初期化処理
        /// </summary>
        public void Initialize()
        {
            if (blendShapeProxy == null)
            {
                Debug.LogWarning("[Vrm0ExpressionController] VRMBlendShapeProxy is null");
                return;
            }

            BuildExpressionList();
            _currentExpressionIndex = -1;

            Debug.Log($"[Vrm0ExpressionController] Initialized with {_availableExpressions.Count} expressions");
        }

        /// <summary>
        /// 利用可能な表情リストを構築
        /// </summary>
        private void BuildExpressionList()
        {
            _availableExpressions.Clear();

            if (blendShapeProxy == null) return;

            // プリセット表情を追加
            var presets = new[]
            {
                BlendShapePreset.Joy,
                BlendShapePreset.Angry,
                BlendShapePreset.Sorrow,
                BlendShapePreset.Fun,
                BlendShapePreset.A,
                BlendShapePreset.I,
                BlendShapePreset.U,
                BlendShapePreset.E,
                BlendShapePreset.O,
                BlendShapePreset.Blink,
                BlendShapePreset.Blink_L,
                BlendShapePreset.Blink_R
            };

            foreach (var preset in presets)
            {
                var key = BlendShapeKey.CreateFromPreset(preset);
                // BlendShapeがあるか確認
                try
                {
                    float currentValue = blendShapeProxy.GetValue(key);
                    _availableExpressions.Add(key);
                    if (debugLog)
                    {
                        Debug.Log($"[Vrm0ExpressionController] Added preset: {preset}");
                    }
                }
                catch
                {
                    // 存在しないプリセットはスキップ
                }
            }

            // カスタム表情も追加（BlendShapeAvatarから取得）
            var avatar = blendShapeProxy.BlendShapeAvatar;
            if (avatar != null && avatar.Clips != null)
            {
                foreach (var clip in avatar.Clips)
                {
                    if (clip != null && clip.Preset == BlendShapePreset.Unknown)
                    {
                        var key = BlendShapeKey.CreateUnknown(clip.BlendShapeName);
                        if (!_availableExpressions.Contains(key))
                        {
                            _availableExpressions.Add(key);
                            if (debugLog)
                            {
                                Debug.Log($"[Vrm0ExpressionController] Added custom: {clip.BlendShapeName}");
                            }
                        }
                    }
                }
            }

            Debug.Log($"[Vrm0ExpressionController] Total expressions: {_availableExpressions.Count}");
        }

        /// <summary>
        /// 次の表情に切り替え
        /// </summary>
        public void NextExpression()
        {
            if (_availableExpressions.Count == 0)
            {
                Debug.LogWarning("[Vrm0ExpressionController] No expressions available");
                return;
            }

            // 現在の表情をリセット
            if (_currentExpressionIndex >= 0 && _currentExpressionIndex < _availableExpressions.Count)
            {
                blendShapeProxy.ImmediatelySetValue(_availableExpressions[_currentExpressionIndex], 0f);
            }

            // 次のインデックスへ
            _currentExpressionIndex++;
            if (_currentExpressionIndex >= _availableExpressions.Count)
            {
                _currentExpressionIndex = -1; // Neutralに戻る
            }

            // 新しい表情を設定
            if (_currentExpressionIndex >= 0)
            {
                var key = _availableExpressions[_currentExpressionIndex];
                blendShapeProxy.ImmediatelySetValue(key, 1f);
            }

            OnExpressionChanged?.Invoke(_currentExpressionIndex, CurrentExpressionName);

            Debug.Log($"[Vrm0ExpressionController] Expression changed to: {CurrentExpressionName} (index: {_currentExpressionIndex})");
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
                blendShapeProxy.ImmediatelySetValue(_availableExpressions[_currentExpressionIndex], 0f);
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
                blendShapeProxy.ImmediatelySetValue(key, 1f);
            }

            OnExpressionChanged?.Invoke(_currentExpressionIndex, CurrentExpressionName);

            Debug.Log($"[Vrm0ExpressionController] Expression changed to: {CurrentExpressionName} (index: {_currentExpressionIndex})");
        }

        /// <summary>
        /// ニュートラル（無表情）に戻す
        /// </summary>
        public void ResetToNeutral()
        {
            // 全表情をリセット
            foreach (var key in _availableExpressions)
            {
                blendShapeProxy.ImmediatelySetValue(key, 0f);
            }

            _currentExpressionIndex = -1;

            OnExpressionChanged?.Invoke(-1, "Neutral");

            Debug.Log("[Vrm0ExpressionController] Reset to Neutral");
        }

        /// <summary>
        /// 表情名の一覧を取得
        /// </summary>
        public List<string> GetExpressionNames()
        {
            var names = new List<string> { "Neutral" };
            foreach (var key in _availableExpressions)
            {
                names.Add(key.Name);
            }
            return names;
        }
    }
}
