using UnityEngine;

namespace AICam.AvatarBuilder
{
    /// <summary>
    /// Editor-importedしたFBXのAvatar定義を保存するテンプレート
    /// RuntimeでこのテンプレートをTriLibスケルトンに適用することで、
    /// Editorと同じ正確なAvatar定義を使用できる
    /// </summary>
    [CreateAssetMenu(fileName = "AvatarTemplate", menuName = "AICam/Avatar Template", order = 1)]
    public class AvatarTemplate : ScriptableObject
    {
        [Header("Avatar Definition")]
        public HumanBone[] humanBones;
        public SkeletonBone[] skeletonBones;

        [Header("Avatar Parameters")]
        public float upperArmTwist = 0.5f;
        public float lowerArmTwist = 0.5f;
        public float upperLegTwist = 0.5f;
        public float lowerLegTwist = 0.5f;
        public float armStretch = 0.05f;
        public float legStretch = 0.05f;
        public float feetSpacing = 0f;
        public bool hasTranslationDoF = false;

        [Header("Metadata")]
        public string sourceFBXName;
        public string extractedDate;

        /// <summary>
        /// このテンプレートからHumanDescriptionを構築
        /// ボーン名はruntimeSkeletonから取得（名前でマッピング）
        /// </summary>
        public HumanDescription BuildHumanDescription(Transform root)
        {
            // 1. Runtime階層から全Transformを収集
            var allTransforms = root.GetComponentsInChildren<Transform>();
            var transformDict = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in allTransforms)
            {
                transformDict[t.name] = t;
            }

            // 2. HumanBoneをマッピング
            var runtimeHumanBones = new System.Collections.Generic.List<HumanBone>();
            foreach (var templateBone in humanBones)
            {
                if (transformDict.TryGetValue(templateBone.boneName, out Transform runtimeTransform))
                {
                    runtimeHumanBones.Add(new HumanBone
                    {
                        humanName = templateBone.humanName,
                        boneName = runtimeTransform.name,
                        limit = templateBone.limit
                    });
                }
                else
                {
                    Debug.LogWarning($"[AvatarTemplate] Bone not found in runtime hierarchy: {templateBone.boneName}");
                }
            }

            // 3. SkeletonBoneをマッピング
            var runtimeSkeletonBones = new System.Collections.Generic.List<SkeletonBone>();
            foreach (var templateBone in skeletonBones)
            {
                if (transformDict.TryGetValue(templateBone.name, out Transform runtimeTransform))
                {
                    runtimeSkeletonBones.Add(new SkeletonBone
                    {
                        name = runtimeTransform.name,
                        position = templateBone.position,
                        rotation = templateBone.rotation,
                        scale = templateBone.scale
                    });
                }
            }

            // 4. HumanDescription構築
            return new HumanDescription
            {
                human = runtimeHumanBones.ToArray(),
                skeleton = runtimeSkeletonBones.ToArray(),
                upperArmTwist = upperArmTwist,
                lowerArmTwist = lowerArmTwist,
                upperLegTwist = upperLegTwist,
                lowerLegTwist = lowerLegTwist,
                armStretch = armStretch,
                legStretch = legStretch,
                feetSpacing = feetSpacing,
                hasTranslationDoF = hasTranslationDoF
            };
        }

        /// <summary>
        /// 必須ボーンがすべてマッピングできるか検証
        /// </summary>
        public bool ValidateBoneMapping(Transform root)
        {
            var allTransforms = root.GetComponentsInChildren<Transform>();
            var transformDict = new System.Collections.Generic.Dictionary<string, Transform>();
            foreach (var t in allTransforms)
            {
                transformDict[t.name] = t;
            }

            int foundCount = 0;
            int totalRequired = 0;

            foreach (var bone in humanBones)
            {
                totalRequired++;
                if (transformDict.ContainsKey(bone.boneName))
                {
                    foundCount++;
                }
                else
                {
                    Debug.LogError($"[AvatarTemplate] Required bone missing: {bone.boneName}");
                }
            }

            Debug.Log($"[AvatarTemplate] Bone mapping: {foundCount}/{totalRequired} bones found");
            return foundCount == totalRequired;
        }
    }
}
