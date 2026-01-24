using System;
using System.Collections.Generic;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// Humanoidマッピングのシリアライザー
    /// </summary>
    public static class HumanoidCacheSerializer
    {
        private const int CURRENT_VERSION = 1;

        /// <summary>
        /// AnimatorからHumanoidマッピングを抽出
        /// </summary>
        public static HumanoidCache ExtractFromAnimator(Animator animator)
        {
            if (animator == null)
                throw new ArgumentNullException(nameof(animator));

            if (animator.avatar == null)
                throw new InvalidOperationException("Animator has no avatar");

            if (!animator.avatar.isHuman)
                throw new InvalidOperationException("Avatar is not humanoid");

            var mappings = new List<HumanBoneMapping>();
            var root = animator.transform;

            // 全てのHumanBodyBonesを走査
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                    continue;

                var boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform != null)
                {
                    var mapping = new HumanBoneMapping
                    {
                        humanBoneName = bone.ToString(),
                        bonePath = GetTransformPath(boneTransform, root)
                    };
                    mappings.Add(mapping);
                }
            }

            return new HumanoidCache
            {
                version = CURRENT_VERSION,
                mappings = mappings.ToArray()
            };
        }

        /// <summary>
        /// HumanoidマッピングをJSONにシリアライズ
        /// </summary>
        public static string SerializeToJson(HumanoidCache cache)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            return JsonUtility.ToJson(cache, true);
        }

        /// <summary>
        /// JSONからHumanoidマッピングをデシリアライズ
        /// </summary>
        public static HumanoidCache DeserializeFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            return JsonUtility.FromJson<HumanoidCache>(json);
        }

        /// <summary>
        /// HumanoidマッピングからAvatarを作成
        /// </summary>
        public static Avatar CreateAvatar(HumanoidCache cache, GameObject root)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            if (root == null)
                throw new ArgumentNullException(nameof(root));

            if (cache.mappings == null || cache.mappings.Length == 0)
                throw new InvalidOperationException("No humanoid mappings in cache");

            // HumanDescriptionを構築
            var humanDescription = new HumanDescription();
            var humanBones = new List<HumanBone>();
            var skeletonBones = new List<SkeletonBone>();

            // 全てのTransformをSkeletonBoneとして追加
            var transforms = root.GetComponentsInChildren<Transform>();
            foreach (var t in transforms)
            {
                var skeletonBone = new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                };
                skeletonBones.Add(skeletonBone);
            }

            // HumanBoneマッピングを構築
            foreach (var mapping in cache.mappings)
            {
                // パスからボーン名を取得
                var boneName = GetBoneNameFromPath(mapping.bonePath);

                // パスからTransformを検索して名前を確認
                var boneTransform = root.transform.Find(mapping.bonePath);
                if (boneTransform != null)
                {
                    boneName = boneTransform.name;
                }

                var humanBone = new HumanBone
                {
                    humanName = ConvertToHumanName(mapping.humanBoneName),
                    boneName = boneName,
                    limit = new HumanLimit { useDefaultValues = true }
                };
                humanBones.Add(humanBone);
            }

            humanDescription.human = humanBones.ToArray();
            humanDescription.skeleton = skeletonBones.ToArray();

            // デフォルト設定
            humanDescription.upperArmTwist = 0.5f;
            humanDescription.lowerArmTwist = 0.5f;
            humanDescription.upperLegTwist = 0.5f;
            humanDescription.lowerLegTwist = 0.5f;
            humanDescription.armStretch = 0.05f;
            humanDescription.legStretch = 0.05f;
            humanDescription.feetSpacing = 0f;
            humanDescription.hasTranslationDoF = false;

            // Avatarを構築
            var avatar = AvatarBuilder.BuildHumanAvatar(root, humanDescription);

            if (avatar == null)
            {
                Debug.LogError("[HumanoidCacheSerializer] Failed to build avatar");
                return null;
            }

            if (!avatar.isValid)
            {
                Debug.LogWarning("[HumanoidCacheSerializer] Built avatar is not valid");
            }

            avatar.name = root.name + "_Avatar";
            return avatar;
        }

        /// <summary>
        /// Transformのパスを取得
        /// </summary>
        private static string GetTransformPath(Transform target, Transform root)
        {
            if (target == root)
                return "";

            var path = target.name;
            var current = target.parent;

            while (current != null && current != root)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>
        /// パスからボーン名を取得
        /// </summary>
        private static string GetBoneNameFromPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            var lastSlash = path.LastIndexOf('/');
            return lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        }

        /// <summary>
        /// HumanBodyBones名をHumanTrait名に変換
        /// </summary>
        private static string ConvertToHumanName(string humanBodyBoneName)
        {
            // HumanBodyBonesの名前をHumanTraitの名前に変換
            // 例: "LeftUpperArm" -> "LeftUpperArm" (ほとんどは同じ)
            // ただし、一部の名前は変換が必要
            switch (humanBodyBoneName)
            {
                case "Hips": return "Hips";
                case "LeftUpperLeg": return "LeftUpperLeg";
                case "RightUpperLeg": return "RightUpperLeg";
                case "LeftLowerLeg": return "LeftLowerLeg";
                case "RightLowerLeg": return "RightLowerLeg";
                case "LeftFoot": return "LeftFoot";
                case "RightFoot": return "RightFoot";
                case "Spine": return "Spine";
                case "Chest": return "Chest";
                case "UpperChest": return "UpperChest";
                case "Neck": return "Neck";
                case "Head": return "Head";
                case "LeftShoulder": return "LeftShoulder";
                case "RightShoulder": return "RightShoulder";
                case "LeftUpperArm": return "LeftUpperArm";
                case "RightUpperArm": return "RightUpperArm";
                case "LeftLowerArm": return "LeftLowerArm";
                case "RightLowerArm": return "RightLowerArm";
                case "LeftHand": return "LeftHand";
                case "RightHand": return "RightHand";
                case "LeftToes": return "LeftToes";
                case "RightToes": return "RightToes";
                case "LeftEye": return "LeftEye";
                case "RightEye": return "RightEye";
                case "Jaw": return "Jaw";
                // Finger bones
                case "LeftThumbProximal": return "Left Thumb Proximal";
                case "LeftThumbIntermediate": return "Left Thumb Intermediate";
                case "LeftThumbDistal": return "Left Thumb Distal";
                case "LeftIndexProximal": return "Left Index Proximal";
                case "LeftIndexIntermediate": return "Left Index Intermediate";
                case "LeftIndexDistal": return "Left Index Distal";
                case "LeftMiddleProximal": return "Left Middle Proximal";
                case "LeftMiddleIntermediate": return "Left Middle Intermediate";
                case "LeftMiddleDistal": return "Left Middle Distal";
                case "LeftRingProximal": return "Left Ring Proximal";
                case "LeftRingIntermediate": return "Left Ring Intermediate";
                case "LeftRingDistal": return "Left Ring Distal";
                case "LeftLittleProximal": return "Left Little Proximal";
                case "LeftLittleIntermediate": return "Left Little Intermediate";
                case "LeftLittleDistal": return "Left Little Distal";
                case "RightThumbProximal": return "Right Thumb Proximal";
                case "RightThumbIntermediate": return "Right Thumb Intermediate";
                case "RightThumbDistal": return "Right Thumb Distal";
                case "RightIndexProximal": return "Right Index Proximal";
                case "RightIndexIntermediate": return "Right Index Intermediate";
                case "RightIndexDistal": return "Right Index Distal";
                case "RightMiddleProximal": return "Right Middle Proximal";
                case "RightMiddleIntermediate": return "Right Middle Intermediate";
                case "RightMiddleDistal": return "Right Middle Distal";
                case "RightRingProximal": return "Right Ring Proximal";
                case "RightRingIntermediate": return "Right Ring Intermediate";
                case "RightRingDistal": return "Right Ring Distal";
                case "RightLittleProximal": return "Right Little Proximal";
                case "RightLittleIntermediate": return "Right Little Intermediate";
                case "RightLittleDistal": return "Right Little Distal";
                default: return humanBodyBoneName;
            }
        }
    }
}
