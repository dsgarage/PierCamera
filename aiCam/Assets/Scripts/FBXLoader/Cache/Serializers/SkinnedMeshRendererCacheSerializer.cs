using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace AICam.AvatarCache.Serializers
{
    /// <summary>
    /// SkinnedMeshRendererのキャッシュ情報
    /// </summary>
    [Serializable]
    public class SkinnedMeshRendererCache
    {
        public int version = 1;
        public SkinnedMeshRendererInfo[] renderers;
    }

    /// <summary>
    /// 個別のSkinnedMeshRenderer情報
    /// </summary>
    [Serializable]
    public class SkinnedMeshRendererInfo
    {
        /// <summary>SMRが付いているGameObjectのパス</summary>
        public string gameObjectPath;
        /// <summary>メッシュ名</summary>
        public string meshName;
        /// <summary>ルートボーンのパス</summary>
        public string rootBonePath;
        /// <summary>ボーン配列のパス（bindposeと同じ順序）</summary>
        public string[] bonePaths;
        /// <summary>マテリアル名の配列</summary>
        public string[] materialNames;
    }

    /// <summary>
    /// SkinnedMeshRendererのシリアライザー
    /// ボーン参照情報を保存して、復元時に正しいボーン順序を再現する
    /// </summary>
    public static class SkinnedMeshRendererCacheSerializer
    {
        private const int CURRENT_VERSION = 1;

        /// <summary>
        /// アバターからSkinnedMeshRenderer情報を抽出
        /// </summary>
        public static SkinnedMeshRendererCache ExtractFromAvatar(GameObject avatar)
        {
            if (avatar == null)
                throw new ArgumentNullException(nameof(avatar));

            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var rendererInfoList = new List<SkinnedMeshRendererInfo>();

            foreach (var smr in smrs)
            {
                if (smr.sharedMesh == null) continue;

                var info = new SkinnedMeshRendererInfo
                {
                    gameObjectPath = GetTransformPath(smr.transform, avatar.transform),
                    meshName = smr.sharedMesh.name,
                    rootBonePath = smr.rootBone != null ? GetTransformPath(smr.rootBone, avatar.transform) : "",
                    bonePaths = ExtractBonePaths(smr, avatar.transform),
                    materialNames = ExtractMaterialNames(smr)
                };

                rendererInfoList.Add(info);
            }

            return new SkinnedMeshRendererCache
            {
                version = CURRENT_VERSION,
                renderers = rendererInfoList.ToArray()
            };
        }

        /// <summary>
        /// SMRのボーンパス配列を抽出
        /// </summary>
        private static string[] ExtractBonePaths(SkinnedMeshRenderer smr, Transform root)
        {
            var bones = smr.bones;
            if (bones == null || bones.Length == 0)
                return Array.Empty<string>();

            var paths = new string[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                paths[i] = bones[i] != null ? GetTransformPath(bones[i], root) : "";
            }
            return paths;
        }

        /// <summary>
        /// SMRのマテリアル名を抽出
        /// </summary>
        private static string[] ExtractMaterialNames(SkinnedMeshRenderer smr)
        {
            var materials = smr.sharedMaterials;
            if (materials == null || materials.Length == 0)
                return Array.Empty<string>();

            var names = new string[materials.Length];
            for (int i = 0; i < materials.Length; i++)
            {
                names[i] = materials[i] != null ? materials[i].name : "";
            }
            return names;
        }

        /// <summary>
        /// JSONにシリアライズ
        /// </summary>
        public static string SerializeToJson(SkinnedMeshRendererCache cache)
        {
            if (cache == null)
                throw new ArgumentNullException(nameof(cache));

            return JsonUtility.ToJson(cache, true);
        }

        /// <summary>
        /// JSONからデシリアライズ
        /// </summary>
        public static SkinnedMeshRendererCache DeserializeFromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
                throw new ArgumentNullException(nameof(json));

            return JsonUtility.FromJson<SkinnedMeshRendererCache>(json);
        }

        /// <summary>
        /// Transformのパスを取得
        /// </summary>
        private static string GetTransformPath(Transform target, Transform root)
        {
            if (target == null || target == root)
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
        /// パスからTransformを検索
        /// </summary>
        public static Transform FindTransformByPath(GameObject root, string path)
        {
            if (string.IsNullOrEmpty(path))
                return root.transform;

            return root.transform.Find(path);
        }

        /// <summary>
        /// ボーンパス配列からTransform配列を構築
        /// </summary>
        public static Transform[] BuildBoneArray(GameObject root, string[] bonePaths)
        {
            if (bonePaths == null || bonePaths.Length == 0)
                return Array.Empty<Transform>();

            var bones = new Transform[bonePaths.Length];
            int foundCount = 0;
            int missingCount = 0;

            for (int i = 0; i < bonePaths.Length; i++)
            {
                bones[i] = FindTransformByPath(root, bonePaths[i]);
                if (bones[i] == null)
                {
                    missingCount++;
                    if (missingCount <= 5) // 最初の5件のみログ出力
                    {
                        Debug.LogWarning($"[SMRCache] Bone not found at path: '{bonePaths[i]}' (root: {root.name})");
                    }
                }
                else
                {
                    foundCount++;
                }
            }

            Debug.Log($"[SMRCache] BuildBoneArray: {foundCount}/{bonePaths.Length} bones found, {missingCount} missing (root: {root.name})");
            return bones;
        }
    }
}
