using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using AICam.AvatarCache.IO;
using AICam.AvatarCache.Serializers;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AICam.AvatarCache
{
    /// <summary>
    /// アバターキャッシュマネージャー
    /// キャッシュの作成・ロード・管理を担当
    /// </summary>
    public class AvatarCacheManager
    {
        public const int CURRENT_CACHE_FORMAT_VERSION = 1;
        private const string CACHE_SUBDIRECTORY = "AvatarCache";
        private const string MANIFEST_FILENAME = "manifest.json";

        private readonly string _cacheRootPath;

        public AvatarCacheManager(string cacheRootPath)
        {
            _cacheRootPath = cacheRootPath;
        }

        /// <summary>
        /// ファイルのSHA256ハッシュを計算
        /// </summary>
        public static string CalculateFileHash(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"File not found: {filePath}");

            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// キャッシュディレクトリパスを取得
        /// </summary>
        public string GetCacheDirectoryPath(string cacheId)
        {
            if (string.IsNullOrEmpty(cacheId))
                throw new ArgumentNullException(nameof(cacheId));

            return Path.Combine(_cacheRootPath, CACHE_SUBDIRECTORY, cacheId);
        }

        /// <summary>
        /// キャッシュが存在するかチェック
        /// </summary>
        public bool CacheExists(string cacheId)
        {
            if (string.IsNullOrEmpty(cacheId))
                return false;

            var cacheDir = GetCacheDirectoryPath(cacheId);
            var manifestPath = Path.Combine(cacheDir, MANIFEST_FILENAME);

            return Directory.Exists(cacheDir) && File.Exists(manifestPath);
        }

        /// <summary>
        /// キャッシュが有効かチェック
        /// </summary>
        public bool IsCacheValid(string cacheId)
        {
            if (!CacheExists(cacheId))
                return false;

            try
            {
                var cacheDir = GetCacheDirectoryPath(cacheId);
                var manifestPath = Path.Combine(cacheDir, MANIFEST_FILENAME);
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonUtility.FromJson<AvatarCacheManifest>(json);

                // バージョンチェック
                if (manifest.cacheFormatVersion != CURRENT_CACHE_FORMAT_VERSION)
                    return false;

                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[AvatarCacheManager] Failed to validate cache: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// VRMからキャッシュを作成
        /// 全てのデータ（ボーン、Humanoid、メッシュ、BlendShape、テクスチャ、マテリアル）を保存
        /// </summary>
        public async UniTask CreateCacheAsync(string vrmPath, GameObject avatar)
        {
            if (string.IsNullOrEmpty(vrmPath))
                throw new ArgumentNullException(nameof(vrmPath));

            if (avatar == null)
                throw new ArgumentNullException(nameof(avatar));

            var hash = CalculateFileHash(vrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var coreDir = Path.Combine(cacheDir, "core");
            var texturesDir = Path.Combine(cacheDir, "textures");
            var iconsDir = Path.Combine(cacheDir, "icons");

            // ディレクトリ構造を作成
            Directory.CreateDirectory(cacheDir);
            Directory.CreateDirectory(coreDir);
            Directory.CreateDirectory(texturesDir);
            Directory.CreateDirectory(iconsDir);

            // Step 1: ボーン階層を保存
            var boneCache = BoneHierarchyCacheSerializer.ExtractFromAvatar(avatar);
            var bonesJson = BoneHierarchyCacheSerializer.SerializeToJson(boneCache);
            await File.WriteAllTextAsync(Path.Combine(coreDir, "bones.json"), bonesJson);

            // Step 2: Humanoidマッピングを保存
            var animator = avatar.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                var humanoidCache = HumanoidCacheSerializer.ExtractFromAnimator(animator);
                var humanoidJson = HumanoidCacheSerializer.SerializeToJson(humanoidCache);
                await File.WriteAllTextAsync(Path.Combine(coreDir, "humanoid.json"), humanoidJson);
            }

            // Step 3: メッシュとBlendShapeを保存
            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var meshes = new List<Mesh>();
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh != null)
                {
                    meshes.Add(smr.sharedMesh);
                }
            }

            if (meshes.Count > 0)
            {
                MeshCacheSerializer.SerializeToBinary(meshes.ToArray(), Path.Combine(coreDir, "meshes.bin"));
                BlendShapeCacheSerializer.SerializeToBinary(smrs, Path.Combine(coreDir, "blendshapes.bin"));
            }

            // Step 4: テクスチャを保存
            var textureCacheManager = new TextureCacheManager(texturesDir);
            var renderers = avatar.GetComponentsInChildren<Renderer>();
            await textureCacheManager.ExtractAndSaveTexturesAsync(GetMaterialsFromRenderers(renderers));

            // Step 5: マテリアルを保存
            var materialCache = MaterialCacheSerializer.ExtractFromRenderers(renderers);
            var materialsJson = MaterialCacheSerializer.SerializeToJson(materialCache);
            await File.WriteAllTextAsync(Path.Combine(coreDir, "materials.json"), materialsJson);

            // Step 6: マニフェストを作成
            var manifest = new AvatarCacheManifest
            {
                cacheFormatVersion = CURRENT_CACHE_FORMAT_VERSION,
                cacheId = hash,
                originalFileName = Path.GetFileName(vrmPath),
                createdAt = DateTime.UtcNow.ToString("o"),
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString()
            };

            var manifestPath = Path.Combine(cacheDir, MANIFEST_FILENAME);
            var json = JsonUtility.ToJson(manifest, true);
            await File.WriteAllTextAsync(manifestPath, json);

            Debug.Log($"[AvatarCacheManager] Cache created: {hash}");
        }

        /// <summary>
        /// レンダラーからマテリアル配列を取得
        /// </summary>
        private static Material[] GetMaterialsFromRenderers(Renderer[] renderers)
        {
            var materials = new List<Material>();
            var processed = new HashSet<Material>();

            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat != null && !processed.Contains(mat))
                    {
                        processed.Add(mat);
                        materials.Add(mat);
                    }
                }
            }

            return materials.ToArray();
        }

        /// <summary>
        /// キャッシュからアバターをロード
        /// </summary>
        public async UniTask<GameObject> LoadFromCacheAsync(string cacheId)
        {
            if (string.IsNullOrEmpty(cacheId))
                throw new ArgumentNullException(nameof(cacheId));

            if (!CacheExists(cacheId))
                throw new FileNotFoundException($"Cache not found: {cacheId}");

            if (!IsCacheValid(cacheId))
                throw new InvalidOperationException($"Cache is invalid or version mismatch: {cacheId}");

            var cacheDir = GetCacheDirectoryPath(cacheId);
            var coreDir = Path.Combine(cacheDir, "core");
            var texturesDir = Path.Combine(cacheDir, "textures");

            // Step 1: ボーン階層を復元
            var bonesPath = Path.Combine(coreDir, "bones.json");
            if (!File.Exists(bonesPath))
                throw new FileNotFoundException($"Bones cache not found: {bonesPath}");

            var bonesJson = await File.ReadAllTextAsync(bonesPath);
            var boneCache = BoneHierarchyCacheSerializer.DeserializeFromJson(bonesJson);
            var avatar = BoneHierarchyCacheSerializer.Reconstruct(boneCache);

            // Step 2: Humanoidマッピングを復元してAvatarを作成
            var humanoidPath = Path.Combine(coreDir, "humanoid.json");
            if (File.Exists(humanoidPath))
            {
                var humanoidJson = await File.ReadAllTextAsync(humanoidPath);
                var humanoidCache = HumanoidCacheSerializer.DeserializeFromJson(humanoidJson);
                var humanAvatar = HumanoidCacheSerializer.CreateAvatar(humanoidCache, avatar);

                if (humanAvatar != null)
                {
                    var animator = avatar.GetComponent<Animator>();
                    if (animator == null)
                    {
                        animator = avatar.AddComponent<Animator>();
                    }
                    animator.avatar = humanAvatar;
                }
            }

            // Step 3: メッシュを復元
            var meshesPath = Path.Combine(coreDir, "meshes.bin");
            Mesh[] meshes = null;
            if (File.Exists(meshesPath))
            {
                meshes = MeshCacheSerializer.DeserializeFromBinary(meshesPath);

                // Step 4: BlendShapeを適用
                var blendShapesPath = Path.Combine(coreDir, "blendshapes.bin");
                if (File.Exists(blendShapesPath))
                {
                    BlendShapeCacheSerializer.DeserializeAndApply(blendShapesPath, meshes);
                }
            }

            // Step 5: テクスチャをロード
            var textureCacheManager = new TextureCacheManager(texturesDir);
            var textures = new List<Texture2D>();
            if (Directory.Exists(texturesDir))
            {
                var textureFiles = Directory.GetFiles(texturesDir, "*.png");
                foreach (var textureFile in textureFiles)
                {
                    var textureId = Path.GetFileNameWithoutExtension(textureFile);
                    try
                    {
                        var texture = await textureCacheManager.LoadTextureAsync(textureId);
                        textures.Add(texture);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[AvatarCacheManager] Failed to load texture {textureId}: {e.Message}");
                    }
                }
            }

            // Step 6: マテリアルを復元
            var materialsPath = Path.Combine(coreDir, "materials.json");
            Material[] materials = null;
            if (File.Exists(materialsPath))
            {
                var materialsJson = await File.ReadAllTextAsync(materialsPath);
                var materialCache = MaterialCacheSerializer.DeserializeFromJson(materialsJson);
                materials = MaterialCacheSerializer.Reconstruct(materialCache, textures.ToArray());
            }

            // Step 7: SkinnedMeshRendererをセットアップ
            if (meshes != null && meshes.Length > 0)
            {
                SetupSkinnedMeshRenderers(avatar, meshes, materials, boneCache);
            }

            Debug.Log($"[AvatarCacheManager] Avatar loaded from cache: {cacheId}");
            return avatar;
        }

        /// <summary>
        /// SkinnedMeshRendererをセットアップ
        /// </summary>
        private void SetupSkinnedMeshRenderers(GameObject avatar, Mesh[] meshes, Material[] materials, BoneHierarchyCache boneCache)
        {
            // ボーンTransform配列を構築
            var transforms = avatar.GetComponentsInChildren<Transform>();
            var boneTransforms = new Transform[transforms.Length];
            var transformByPath = new Dictionary<string, Transform>();

            foreach (var t in transforms)
            {
                var path = GetTransformPath(t, avatar.transform);
                transformByPath[path] = t;
            }

            for (int i = 0; i < transforms.Length && i < boneCache.bones.Length; i++)
            {
                boneTransforms[i] = transforms[i];
            }

            // メッシュ名からマテリアルへのマップを作成
            var materialByName = new Dictionary<string, Material>();
            if (materials != null)
            {
                foreach (var mat in materials)
                {
                    if (mat != null && !string.IsNullOrEmpty(mat.name))
                    {
                        materialByName[mat.name] = mat;
                    }
                }
            }

            // 各メッシュに対してSkinnedMeshRendererを作成
            for (int i = 0; i < meshes.Length; i++)
            {
                var mesh = meshes[i];
                if (mesh == null) continue;

                // メッシュ用のGameObjectを作成
                var meshGO = new GameObject(mesh.name);
                meshGO.transform.SetParent(avatar.transform, false);

                var smr = meshGO.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;

                // ボーンを設定
                smr.bones = boneTransforms;
                smr.rootBone = avatar.transform;

                // マテリアルを設定
                if (materials != null && i < materials.Length && materials[i] != null)
                {
                    smr.sharedMaterial = materials[i];
                }
                else if (materialByName.TryGetValue(mesh.name, out var material))
                {
                    smr.sharedMaterial = material;
                }
                else
                {
                    // デフォルトマテリアルを作成
                    var defaultMat = new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit"));
                    defaultMat.name = mesh.name + "_Material";
                    smr.sharedMaterial = defaultMat;
                }
            }
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
        /// キャッシュを削除
        /// </summary>
        public void DeleteCache(string cacheId)
        {
            if (string.IsNullOrEmpty(cacheId))
                return;

            var cacheDir = GetCacheDirectoryPath(cacheId);

            if (Directory.Exists(cacheDir))
            {
                try
                {
                    Directory.Delete(cacheDir, true);
                    Debug.Log($"[AvatarCacheManager] Cache deleted: {cacheId}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AvatarCacheManager] Failed to delete cache {cacheId}: {e.Message}");
                }
            }
        }
    }
}
