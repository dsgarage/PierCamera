#if BLENDSHAPE_CONTROLLER
using System.Collections.Generic;
using UnityEngine;
using DSGarage.BlendShape;
#if VRM10_AVAILABLE
using UniVRM10;
#endif

namespace AICam.VRM
{
    /// <summary>
    /// Issue #464: VRM 1.0 の Expression を BlendshapeController SDK の ExpressionSet に変換するブリッジ
    /// </summary>
    public static class VrmExpressionBridge
    {
        private const string TAG = "[VrmExpressionBridge]";

        /// <summary>
        /// VRoidStudio アバター判定（Fcl_ prefix のブレンドシェイプが10個以上）
        /// </summary>
        public static bool IsVRoidStudioAvatar(GameObject avatar)
        {
            if (avatar == null) return false;

            int fclCount = 0;
            var skinnedMeshes = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (var smr in skinnedMeshes)
            {
                if (smr.sharedMesh == null) continue;

                for (int i = 0; i < smr.sharedMesh.blendShapeCount; i++)
                {
                    string name = smr.sharedMesh.GetBlendShapeName(i);
                    if (name.StartsWith("Fcl_"))
                    {
                        fclCount++;
                        if (fclCount >= 10) return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// StandardExpressions を ExpressionSet に変換
        /// </summary>
        public static ExpressionSet GetStandardExpressionSet()
        {
            var set = new ExpressionSet("StandardExpressions")
            {
                description = "VRoid Studio standard expressions"
            };

            int index = 0;
            foreach (var kvp in VRMExpressionIconGenerator.StandardExpressions)
            {
                var entry = new ExpressionEntry(kvp.Key, index);
                foreach (var bs in kvp.Value)
                {
                    entry.blendShapes[bs.Key] = bs.Value;
                }
                set.AddExpression(entry);
                index++;
            }

            return set;
        }

#if VRM10_AVAILABLE
        /// <summary>
        /// 除外する視線表情プリセット
        /// </summary>
        private static readonly HashSet<ExpressionPreset> ExcludedPresets = new HashSet<ExpressionPreset>
        {
            ExpressionPreset.lookUp,
            ExpressionPreset.lookDown,
            ExpressionPreset.lookLeft,
            ExpressionPreset.lookRight
        };

        /// <summary>
        /// VRM 1.0 Clips → ExpressionSet 変換
        /// </summary>
        public static ExpressionSet CreateExpressionSetFromVrm10(Vrm10Instance vrm10, GameObject avatar)
        {
            if (vrm10 == null || vrm10.Vrm == null)
            {
                Debug.LogWarning($"{TAG} Vrm10Instance or Vrm data is null");
                return null;
            }

            var expressionData = vrm10.Vrm.Expression;
            if (expressionData == null)
            {
                Debug.LogWarning($"{TAG} Expression data is null");
                return null;
            }

            var set = new ExpressionSet(avatar != null ? avatar.name : "VRM10Expressions")
            {
                description = "Generated from VRM 1.0 expression clips"
            };

            int entryIndex = 0;
            foreach (var (preset, clip) in expressionData.Clips)
            {
                if (clip == null) continue;

                // 視線表情をスキップ
                if (ExcludedPresets.Contains(preset)) continue;

                // ExpressionEntry を構築
                string expressionName = preset == ExpressionPreset.custom
                    ? clip.name
                    : preset.ToString();

                var entry = new ExpressionEntry(expressionName, entryIndex);

                // MorphTargetBindings を解決
                if (clip.MorphTargetBindings != null && clip.MorphTargetBindings.Length > 0)
                {
                    foreach (var binding in clip.MorphTargetBindings)
                    {
                        // RelativePath からメッシュを検索
                        Transform meshTransform = avatar != null
                            ? avatar.transform.Find(binding.RelativePath)
                            : null;

                        if (meshTransform == null)
                        {
                            Debug.LogWarning($"{TAG} Mesh not found: {binding.RelativePath} for expression {expressionName}");
                            continue;
                        }

                        var smr = meshTransform.GetComponent<SkinnedMeshRenderer>();
                        if (smr == null || smr.sharedMesh == null)
                        {
                            Debug.LogWarning($"{TAG} SkinnedMeshRenderer not found at: {binding.RelativePath}");
                            continue;
                        }

                        // Index からブレンドシェイプ名を取得
                        if (binding.Index < 0 || binding.Index >= smr.sharedMesh.blendShapeCount)
                        {
                            Debug.LogWarning($"{TAG} BlendShape index {binding.Index} out of range for {binding.RelativePath}");
                            continue;
                        }

                        string blendShapeName = smr.sharedMesh.GetBlendShapeName(binding.Index);

                        // Weight: VRM は 0-1、Unity BlendShape は 0-100
                        float weight = binding.Weight * 100f;

                        // 同名のブレンドシェイプが既にある場合はスキップ（最初の値を優先）
                        if (!entry.blendShapes.ContainsKey(blendShapeName))
                        {
                            entry.blendShapes[blendShapeName] = weight;
                        }
                    }
                }

                // AddExpression は MaxExpressions=12 制限があるため直接追加
                entry.index = entryIndex;
                set.expressions.Add(entry);
                entryIndex++;

                Debug.Log($"{TAG} Expression '{expressionName}': {entry.blendShapes.Count} blendshapes");
            }

            Debug.Log($"{TAG} Created ExpressionSet with {set.Count} expressions from VRM 1.0");
            return set;
        }
#endif
    }
}
#endif
