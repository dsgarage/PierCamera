using System;
using System.Collections.Generic;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// ボーン階層のシリアライザー
    /// </summary>
    public static class BoneHierarchyCacheSerializer
    {
        private const int CURRENT_VERSION = 1;

        /// <summary>
        /// アバターからボーン階層を抽出
        /// </summary>
        public static BoneHierarchyCache ExtractFromAvatar(GameObject avatar)
        {
            if (avatar == null)
                throw new ArgumentNullException(nameof(avatar));

            var transforms = avatar.GetComponentsInChildren<Transform>();
            var boneInfoList = new List<BoneInfo>();
            var transformToIndex = new Dictionary<Transform, int>();

            // インデックスマップを作成
            for (int i = 0; i < transforms.Length; i++)
            {
                transformToIndex[transforms[i]] = i;
            }

            // ボーン情報を抽出
            foreach (var t in transforms)
            {
                var parentIndex = -1;
                if (t.parent != null && transformToIndex.TryGetValue(t.parent, out var idx))
                {
                    parentIndex = idx;
                }

                var boneInfo = new BoneInfo
                {
                    name = t.name,
                    path = GetTransformPath(t, avatar.transform),
                    parentIndex = parentIndex,
                    localPosition = new float[] { t.localPosition.x, t.localPosition.y, t.localPosition.z },
                    localRotation = new float[] { t.localRotation.x, t.localRotation.y, t.localRotation.z, t.localRotation.w },
                    localScale = new float[] { t.localScale.x, t.localScale.y, t.localScale.z }
                };

                boneInfoList.Add(boneInfo);
            }

            return new BoneHierarchyCache
            {
                version = CURRENT_VERSION,
                bones = boneInfoList.ToArray()
            };
        }

        /// <summary>
        /// ボーン階層をJSONにシリアライズ
        /// </summary>
        public static string SerializeToJson(BoneHierarchyCache cache)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            return JsonUtility.ToJson(cache, true);
        }

        /// <summary>
        /// JSONからボーン階層をデシリアライズ
        /// </summary>
        public static BoneHierarchyCache DeserializeFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            return JsonUtility.FromJson<BoneHierarchyCache>(json);
        }

        /// <summary>
        /// ボーン階層からGameObjectを再構築
        /// bindposesとの整合性を保つため、保存時のTransformをそのまま復元する
        /// （SMRセットアップ後に位置調整が必要な場合はNormalizeRootTransformを使用）
        /// </summary>
        public static GameObject Reconstruct(BoneHierarchyCache cache)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            if (cache.bones == null || cache.bones.Length == 0)
                throw new InvalidOperationException("No bones in cache");

            var gameObjects = new GameObject[cache.bones.Length];

            // 全てのGameObjectを作成（保存時のTransformをそのまま復元）
            for (int i = 0; i < cache.bones.Length; i++)
            {
                var bone = cache.bones[i];
                gameObjects[i] = new GameObject(bone.name);

                var t = gameObjects[i].transform;
                t.localPosition = new Vector3(bone.localPosition[0], bone.localPosition[1], bone.localPosition[2]);
                t.localRotation = new Quaternion(bone.localRotation[0], bone.localRotation[1], bone.localRotation[2], bone.localRotation[3]);
                t.localScale = new Vector3(bone.localScale[0], bone.localScale[1], bone.localScale[2]);

                if (bone.parentIndex == -1)
                {
                    Debug.Log($"[BoneHierarchy] Root '{bone.name}' restored with original transform (pos: {bone.localPosition[0]:F2}, {bone.localPosition[1]:F2}, {bone.localPosition[2]:F2})");
                }
            }

            // 親子関係を設定
            for (int i = 0; i < cache.bones.Length; i++)
            {
                var bone = cache.bones[i];
                if (bone.parentIndex >= 0 && bone.parentIndex < gameObjects.Length)
                {
                    gameObjects[i].transform.SetParent(gameObjects[bone.parentIndex].transform, false);
                }
            }

            // ルートを返す（最初のボーンはルートであるべき）
            return gameObjects[0];
        }

        /// <summary>
        /// 警告: この関数はスキニングを破壊する可能性がある
        /// キャッシュ作成時にアバターが原点にあった場合のみ使用可能
        /// 通常はキャッシュ作成前にアバターを原点に移動すべき
        /// </summary>
        public static void NormalizeRootTransform(GameObject root)
        {
            if (root == null) return;

            Debug.LogWarning($"[BoneHierarchy] NormalizeRootTransform called - this may break skinning! " +
                           $"Original pos: {root.transform.localPosition}, rot: {root.transform.localRotation.eulerAngles}");
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
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
    }
}
