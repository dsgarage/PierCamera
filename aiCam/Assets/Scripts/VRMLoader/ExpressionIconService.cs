#if BLENDSHAPE_CONTROLLER
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using DSGarage.BlendShape;
#if VRM10_AVAILABLE
using UniVRM10;
#endif

namespace AICam.VRM
{
    /// <summary>
    /// Issue #465: スロット単位の表情アイコン生成を一元管理するサービス
    /// </summary>
    public class ExpressionIconService : MonoBehaviour
    {
        private const string TAG = "[ExpressionIconService]";
        private const string EXPRESSION_ICONS_FOLDER = "expression_icons";

        private static ExpressionIconService _instance;
        public static ExpressionIconService Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("ExpressionIconService");
                    _instance = go.AddComponent<ExpressionIconService>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private VRMExpressionIconGenerator _generator;
        private bool _isGenerating;

        /// <summary>生成中かどうか</summary>
        public bool IsGenerating => _isGenerating;

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }

        /// <summary>
        /// スロット単位で表情アイコンを生成
        /// </summary>
        public void GenerateForSlot(
            GameObject avatar,
            int slotIndex,
            string avatarName,
            Action<string> onComplete,
            Action<string> onError)
        {
            if (avatar == null)
            {
                onError?.Invoke("Avatar is null");
                return;
            }

            if (_isGenerating)
            {
                Debug.LogWarning($"{TAG} Already generating icons, skipping request for slot {slotIndex}");
                return;
            }

            // GenerateFromGameObject / GenerateWithCustomExpressions は
            // 出力先に avatarName サブフォルダを自動作成する
            // → baseFolder を渡し、実際のアイコンは baseFolder/{avatarName}/ に出力される
            string baseFolder = GetBaseFolder();
            string actualIconFolder = Path.Combine(baseFolder, SanitizeFileName(avatar.name));

            // 既存アイコンチェック
            if (Directory.Exists(actualIconFolder) &&
                Directory.GetFiles(actualIconFolder, "*.png").Length > 0)
            {
                Debug.Log($"{TAG} Icons already exist for slot {slotIndex}: {actualIconFolder}");
                onComplete?.Invoke(actualIconFolder);
                return;
            }

            _isGenerating = true;

            // Generator を取得または作成
            EnsureGenerator();

            // Transform を保存（生成中に移動されるため）
            var savedPos = avatar.transform.position;
            var savedRot = avatar.transform.rotation;

            // イベントハンドラをセットアップ
            Action<string, List<string>> completeHandler = null;
            Action<string> errorHandler = null;

            completeHandler = (name, paths) =>
            {
                _generator.OnGenerationComplete -= completeHandler;
                _generator.OnError -= errorHandler;
                _isGenerating = false;

                // Transform を復元
                if (avatar != null)
                {
                    avatar.transform.position = savedPos;
                    avatar.transform.rotation = savedRot;
                }

                Debug.Log($"{TAG} Generated {paths.Count} icons for slot {slotIndex} at {actualIconFolder}");
                onComplete?.Invoke(actualIconFolder);
            };

            errorHandler = (errorMsg) =>
            {
                _generator.OnGenerationComplete -= completeHandler;
                _generator.OnError -= errorHandler;
                _isGenerating = false;

                // Transform を復元
                if (avatar != null)
                {
                    avatar.transform.position = savedPos;
                    avatar.transform.rotation = savedRot;
                }

                Debug.LogError($"{TAG} Error generating icons for slot {slotIndex}: {errorMsg}");
                onError?.Invoke(errorMsg);
            };

            _generator.OnGenerationComplete += completeHandler;
            _generator.OnError += errorHandler;

            // VRoidStudio 判定で生成方法を分岐
            bool isVRoid = VrmExpressionBridge.IsVRoidStudioAvatar(avatar);

            if (isVRoid)
            {
                // VRoid: StandardExpressions を使用（GenerateFromGameObject 内部で適用）
                Debug.Log($"{TAG} VRoid Studio avatar detected, using StandardExpressions for slot {slotIndex}");
                _generator.GenerateFromGameObject(avatar, baseFolder, false);
            }
            else
            {
                GenerateForNonVRoid(avatar, slotIndex, baseFolder);
            }
        }

        private void GenerateForNonVRoid(GameObject avatar, int slotIndex, string baseFolder)
        {
#if VRM10_AVAILABLE
            var vrm10 = avatar.GetComponent<Vrm10Instance>();
            if (vrm10 != null)
            {
                var expressionSet = VrmExpressionBridge.CreateExpressionSetFromVrm10(vrm10, avatar);
                if (expressionSet != null && expressionSet.Count > 0)
                {
                    // ExpressionSet → Dictionary<string, Dictionary<string, float>> に変換
                    var customExpressions = ExpressionSetToDict(expressionSet);
                    Debug.Log($"{TAG} Non-VRoid VRM 1.0, using {customExpressions.Count} expressions for slot {slotIndex}");
                    _generator.GenerateWithCustomExpressions(avatar, customExpressions, baseFolder, false);
                    return;
                }

                Debug.LogWarning($"{TAG} Failed to create ExpressionSet from VRM 1.0, falling back to StandardExpressions");
            }
#endif
            // フォールバック: StandardExpressions を使用
            Debug.Log($"{TAG} Using StandardExpressions as fallback for slot {slotIndex}");
            _generator.GenerateFromGameObject(avatar, baseFolder, false);
        }

        /// <summary>
        /// ExpressionSet を GenerateWithCustomExpressions 用の Dictionary に変換
        /// </summary>
        private static Dictionary<string, Dictionary<string, float>> ExpressionSetToDict(ExpressionSet set)
        {
            var dict = new Dictionary<string, Dictionary<string, float>>();
            if (set?.expressions == null) return dict;

            foreach (var entry in set.expressions)
            {
                string key = entry.name ?? $"entry_{entry.index}";
                dict[key] = new Dictionary<string, float>(entry.blendShapes);
            }
            return dict;
        }

        private void EnsureGenerator()
        {
            if (_generator == null)
            {
                _generator = gameObject.GetComponent<VRMExpressionIconGenerator>();
                if (_generator == null)
                {
                    _generator = gameObject.AddComponent<VRMExpressionIconGenerator>();
                }
            }
        }

        /// <summary>
        /// 表情アイコンのベースフォルダパスを取得
        /// </summary>
        public static string GetBaseFolder()
        {
            return Path.Combine(
                Application.persistentDataPath,
                "AvatarSlots",
                EXPRESSION_ICONS_FOLDER);
        }

        /// <summary>
        /// 出力先フォルダパスを取得
        /// </summary>
        public static string GetOutputFolder(string sanitizedAvatarName)
        {
            return Path.Combine(GetBaseFolder(), sanitizedAvatarName);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "unnamed";

            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name;
        }
    }
}
#endif
