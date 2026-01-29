using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            // 全ボーンのTransformを保存（復元用）
            // BuildHumanAvatar後やAnimator適用後の状態を壊さないよう、最後に必ず復元する
            var allTransforms = avatar.GetComponentsInChildren<Transform>();
            var savedPositions = new Vector3[allTransforms.Length];
            var savedRotations = new Quaternion[allTransforms.Length];
            var savedScales = new Vector3[allTransforms.Length];
            for (int i = 0; i < allTransforms.Length; i++)
            {
                savedPositions[i] = allTransforms[i].localPosition;
                savedRotations[i] = allTransforms[i].localRotation;
                savedScales[i] = allTransforms[i].localScale;
            }

            // Animatorを無効化（ボーンの回転を変更されないようにする）
            var animator = avatar.GetComponent<Animator>();
            bool animatorWasEnabled = false;
            if (animator != null)
            {
                animatorWasEnabled = animator.enabled;
                animator.enabled = false;
                Debug.Log($"[AvatarCacheManager] Disabled Animator for cache creation");
            }

            // アバターを原点に正規化（bindposesはメッシュ作成時の原点基準で計算されている）
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;
            avatar.transform.localScale = Vector3.one;

            // 重要: ボーンをbind-time状態にリセット（bindposesから逆算）
            // VRMロード後にBuildHumanAvatarがボーンTransformを変更している可能性があるため、
            // bindposesが作成された時点のTransformに戻してから保存する。
            // これにより、ロード時にbind-time状態のボーンを復元でき、
            // BuildHumanAvatarを1回だけ呼べばオリジナルVRMと同じパイプラインを再現できる。
            ResetBonesToBindTime(avatar);

            try
            {
                await CreateCacheInternalAsync(vrmPath, avatar);
            }
            finally
            {
                // 全ボーンのTransformを復元（元のゲームオブジェクトを壊さない）
                for (int i = 0; i < allTransforms.Length; i++)
                {
                    if (allTransforms[i] != null)
                    {
                        allTransforms[i].localPosition = savedPositions[i];
                        allTransforms[i].localRotation = savedRotations[i];
                        allTransforms[i].localScale = savedScales[i];
                    }
                }

                // Animatorを復元
                if (animator != null)
                {
                    animator.enabled = animatorWasEnabled;
                }

                Debug.Log($"[AvatarCacheManager] Restored all bone transforms and Animator state");
            }
        }

        private async UniTask CreateCacheInternalAsync(string vrmPath, GameObject avatar)
        {
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

            // Step 3.5: SkinnedMeshRenderer情報を保存（ボーン参照を含む）
            var smrCache = SkinnedMeshRendererCacheSerializer.ExtractFromAvatar(avatar);
            var smrJson = SkinnedMeshRendererCacheSerializer.SerializeToJson(smrCache);
            await File.WriteAllTextAsync(Path.Combine(coreDir, "smr.json"), smrJson);

            // Step 3.6: キャッシュ整合性検証 - bindposesとボーンのworld transformが一致するか確認
            VerifyBindposeBoneConsistency(smrs);

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
            Debug.Log($"🔧 [AvatarCacheManager] LoadFromCacheAsync START - cacheId: {cacheId}");

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

            // Step 2: Humanoidデータを事前読み込み（適用はSMRセットアップ後）
            // Animatorを先にセットアップするとボーンのretargetが発生し、
            // bindposesとボーン位置が不一致になりスキニングが崩れるため
            var humanoidPath = Path.Combine(coreDir, "humanoid.json");
            HumanoidCache humanoidCache = null;
            if (File.Exists(humanoidPath))
            {
                var humanoidJson = await File.ReadAllTextAsync(humanoidPath);
                humanoidCache = HumanoidCacheSerializer.DeserializeFromJson(humanoidJson);
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

            // Step 6.5: SMR情報をロード
            var smrPath = Path.Combine(coreDir, "smr.json");
            SkinnedMeshRendererCache smrCache = null;
            if (File.Exists(smrPath))
            {
                var smrJson = await File.ReadAllTextAsync(smrPath);
                smrCache = SkinnedMeshRendererCacheSerializer.DeserializeFromJson(smrJson);
            }

            // Step 7: ルートTransformを原点にリセット（SMRセットアップ前に必須）
            // bindposesはアバターが原点にある状態で計算されているため、
            // ボーンを原点に配置しないとスキニングが崩れる
            // 注意: この後でPlaceAvatarAheadOfCameraを呼び出してアバター全体を移動可能
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.identity;
            avatar.transform.localScale = Vector3.one;
            Debug.Log($"[AvatarCacheManager] Root transform reset to identity for correct skinning");

            // Step 8: SkinnedMeshRendererをセットアップ（Animator設定前に実行）
            // bindposesは原点でのボーン位置と一致している必要があるため、
            // Animatorがボーンを変更する前にSMRをセットアップする
            if (meshes != null && meshes.Length > 0)
            {
                SetupSkinnedMeshRenderers(avatar, meshes, materials, smrCache);
            }

            // Step 9: Humanoid Avatarを作成してAnimatorに設定（SMRセットアップ後）
            // 重要: キャッシュにはbind-time状態のボーンTransformが保存されている。
            // BuildHumanAvatarはこのbind-time状態を受け取り、オリジナルVRMロードと
            // 同じパイプラインでAvatarを構築する。
            // BuildHumanAvatarがボーンを変更しても復元しない（オリジナルと同じ動作）。
            if (humanoidCache != null)
            {
                var humanAvatar = HumanoidCacheSerializer.CreateAvatar(humanoidCache, avatar);

                if (humanAvatar != null)
                {
                    var animator = avatar.GetComponent<Animator>();
                    if (animator == null)
                    {
                        animator = avatar.AddComponent<Animator>();
                    }
                    animator.avatar = humanAvatar;
                    Debug.Log($"[AvatarCacheManager] Humanoid Avatar set up (bind-time pipeline, matching original VRM load)");
                }
            }

            Debug.Log($"[AvatarCacheManager] Avatar loaded from cache: {cacheId}");
            return avatar;
        }

        /// <summary>
        /// SkinnedMeshRendererをセットアップ
        /// </summary>
        private void SetupSkinnedMeshRenderers(GameObject avatar, Mesh[] meshes, Material[] materials, SkinnedMeshRendererCache smrCache)
        {
            Debug.Log($"🔧 [AvatarCacheManager] SetupSkinnedMeshRenderers START - avatar: {avatar?.name}, meshes: {meshes?.Length ?? 0}, smrCache: {smrCache != null}");

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

            // メッシュ名からメッシュへのマップを作成
            var meshByName = new Dictionary<string, Mesh>();
            foreach (var mesh in meshes)
            {
                if (mesh != null && !string.IsNullOrEmpty(mesh.name))
                {
                    meshByName[mesh.name] = mesh;
                }
            }

            // SMRキャッシュがある場合は正確なボーン情報を使用
            if (smrCache != null && smrCache.renderers != null && smrCache.renderers.Length > 0)
            {
                // デバッグ: アバターの直下の子を列挙
                Debug.Log($"[AvatarCacheManager] Avatar root: '{avatar.name}', children: {avatar.transform.childCount}");
                for (int i = 0; i < Mathf.Min(avatar.transform.childCount, 10); i++)
                {
                    Debug.Log($"[AvatarCacheManager]   Child[{i}]: '{avatar.transform.GetChild(i).name}'");
                }

                foreach (var smrInfo in smrCache.renderers)
                {
                    if (!meshByName.TryGetValue(smrInfo.meshName, out var mesh))
                    {
                        Debug.LogWarning($"[AvatarCacheManager] Mesh not found: {smrInfo.meshName}");
                        continue;
                    }

                    // SMRを取り付けるGameObjectを検索または作成
                    Transform targetTransform;
                    if (string.IsNullOrEmpty(smrInfo.gameObjectPath))
                    {
                        targetTransform = avatar.transform;
                    }
                    else
                    {
                        targetTransform = avatar.transform.Find(smrInfo.gameObjectPath);
                        if (targetTransform == null)
                        {
                            // パスが見つからない場合は新しいGameObjectを作成
                            var meshGO = new GameObject(mesh.name);
                            meshGO.transform.SetParent(avatar.transform, false);
                            targetTransform = meshGO.transform;
                        }
                    }

                    // 既存のSMRを使用するか、新規作成
                    var smr = targetTransform.GetComponent<SkinnedMeshRenderer>();
                    if (smr == null)
                    {
                        smr = targetTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
                    }

                    smr.sharedMesh = mesh;

                    // ボーンを正確な順序で設定（bindposeの順序と一致させる）
                    if (smrInfo.bonePaths != null && smrInfo.bonePaths.Length > 0)
                    {
                        var boneArray = SkinnedMeshRendererCacheSerializer.BuildBoneArray(avatar, smrInfo.bonePaths);
                        smr.bones = boneArray;

                        var bindposeCount = mesh.bindposes?.Length ?? 0;
                        Debug.Log($"[AvatarCacheManager] Mesh '{smrInfo.meshName}': bindposes={bindposeCount}, bones={boneArray.Length}, null bones={boneArray.Count(b => b == null)}");
                    }

                    // ルートボーンを設定
                    if (!string.IsNullOrEmpty(smrInfo.rootBonePath))
                    {
                        smr.rootBone = SkinnedMeshRendererCacheSerializer.FindTransformByPath(avatar, smrInfo.rootBonePath);
                    }
                    else
                    {
                        smr.rootBone = avatar.transform;
                    }

                    // マテリアルを設定
                    if (smrInfo.materialNames != null && smrInfo.materialNames.Length > 0)
                    {
                        var mats = new Material[smrInfo.materialNames.Length];
                        for (int i = 0; i < smrInfo.materialNames.Length; i++)
                        {
                            if (materialByName.TryGetValue(smrInfo.materialNames[i], out var mat))
                            {
                                mats[i] = mat;
                            }
                            else
                            {
                                // デフォルトマテリアルを作成
                                var defaultMat = new Material(Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit"));
                                defaultMat.name = smrInfo.materialNames[i];
                                mats[i] = defaultMat;
                            }
                        }
                        smr.sharedMaterials = mats;
                    }

                    Debug.Log($"[AvatarCacheManager] SMR setup: {smrInfo.meshName}, bones: {smr.bones?.Length ?? 0}");
                }
            }
            else
            {
                // フォールバック: SMRキャッシュがない場合は古い方法を使用
                Debug.LogWarning("[AvatarCacheManager] No SMR cache found, using fallback method");
                SetupSkinnedMeshRenderersFallback(avatar, meshes, materials);
            }
        }

        /// <summary>
        /// フォールバック: SMRキャッシュがない場合の古い方法
        /// </summary>
        private void SetupSkinnedMeshRenderersFallback(GameObject avatar, Mesh[] meshes, Material[] materials)
        {
            var transforms = avatar.GetComponentsInChildren<Transform>();

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

            for (int i = 0; i < meshes.Length; i++)
            {
                var mesh = meshes[i];
                if (mesh == null) continue;

                var meshGO = new GameObject(mesh.name);
                meshGO.transform.SetParent(avatar.transform, false);

                var smr = meshGO.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
                smr.bones = transforms;
                smr.rootBone = avatar.transform;

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

        /// <summary>
        /// ボーンをbind-time状態にリセット
        /// bindpose[i] = bones[i].worldToLocalMatrix * meshRenderer.localToWorldMatrix なので、
        /// bones[i].localToWorldMatrix = meshRenderer.localToWorldMatrix * bindpose[i].inverse
        /// この式でbind-time時のworld matrixを逆算し、local transformに変換する。
        /// 重要: meshRenderer.localToWorldMatrixを考慮しないと、メッシュが回転している場合
        /// （VRM/Blenderモデルの-90°X回転など）にボーン位置が不正になる。
        /// GetComponentsInChildren の depth-first 順序（親→子）で処理することで、
        /// 親のworld matrixが先に確定し、子のlocal transformを正しく計算できる。
        /// </summary>
        private static void ResetBonesToBindTime(GameObject avatar)
        {
            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            // 全SMRから、各ボーン → bind-time world matrix のマッピングを構築
            var bindTimeWorldMatrices = new Dictionary<Transform, Matrix4x4>();
            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;
                var bindposes = mesh.bindposes;
                var bones = smr.bones;
                if (bindposes == null || bones == null) continue;

                // bindpose[i] = bones[i].worldToLocalMatrix * meshRenderer.localToWorldMatrix
                // よって: bones[i].localToWorldMatrix = meshRenderer.localToWorldMatrix * bindpose[i].inverse
                var meshWorldMatrix = smr.transform.localToWorldMatrix;

                int count = Mathf.Min(bindposes.Length, bones.Length);
                for (int i = 0; i < count; i++)
                {
                    if (bones[i] != null && !bindTimeWorldMatrices.ContainsKey(bones[i]))
                    {
                        bindTimeWorldMatrices[bones[i]] = meshWorldMatrix * bindposes[i].inverse;
                    }
                }
            }

            if (bindTimeWorldMatrices.Count == 0)
            {
                Debug.LogWarning("[AvatarCacheManager] No bindpose data found for ResetBonesToBindTime");
                return;
            }

            // 親→子の順序で処理（GetComponentsInChildrenはdepth-first）
            var allTransforms = avatar.GetComponentsInChildren<Transform>();
            int resetCount = 0;

            foreach (var t in allTransforms)
            {
                if (!bindTimeWorldMatrices.TryGetValue(t, out var bindTimeWorld))
                    continue;

                // bind-time world matrix → local transform に変換
                Matrix4x4 localMatrix;
                if (t.parent != null)
                {
                    // 親のworld matrixは既に更新済み（親→子の順序で処理しているため）
                    localMatrix = t.parent.localToWorldMatrix.inverse * bindTimeWorld;
                }
                else
                {
                    localMatrix = bindTimeWorld;
                }

                t.localPosition = new Vector3(localMatrix.m03, localMatrix.m13, localMatrix.m23);
                t.localRotation = localMatrix.rotation;
                // localScaleは変更しない（humanoidボーンは通常(1,1,1)）
                resetCount++;
            }

            Debug.Log($"[AvatarCacheManager] Reset {resetCount}/{bindTimeWorldMatrices.Count} bones to bind-time positions");
        }

        /// <summary>
        /// キャッシュ作成時にbindposesとボーンのworld transformが一致するか検証
        /// 不一致があればログに出力（キャッシュデータの整合性確認用）
        /// </summary>
        private static void VerifyBindposeBoneConsistency(SkinnedMeshRenderer[] smrs)
        {
            Debug.Log("[AvatarCacheManager] === Bindpose-Bone Consistency Verification ===");
            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;

                var bindposes = mesh.bindposes;
                var bones = smr.bones;
                if (bindposes == null || bones == null) continue;

                // bindpose[i] = bones[i].worldToLocalMatrix * meshRenderer.localToWorldMatrix
                // よって: bones[i].localToWorldMatrix = meshRenderer.localToWorldMatrix * bindpose[i].inverse
                var meshWorldMatrix = smr.transform.localToWorldMatrix;

                int count = Mathf.Min(bindposes.Length, bones.Length);
                int matchCount = 0;
                int mismatchCount = 0;

                for (int i = 0; i < count; i++)
                {
                    if (bones[i] == null) continue;

                    // メッシュレンダラーのTransformを考慮してbind-time world matrixを算出
                    var bindTimeWorld = meshWorldMatrix * bindposes[i].inverse;
                    var bpPos = new Vector3(bindTimeWorld.m03, bindTimeWorld.m13, bindTimeWorld.m23);
                    var bonePos = bones[i].position;
                    float posError = Vector3.Distance(bpPos, bonePos);

                    var bpRot = bindTimeWorld.rotation;
                    var boneRot = bones[i].rotation;
                    float rotError = Quaternion.Angle(bpRot, boneRot);

                    if (posError > 0.01f || rotError > 1f)
                    {
                        mismatchCount++;
                        string boneName = bones[i].name.ToLower();
                        if (boneName.Contains("shoulder") || boneName.Contains("arm") || boneName.Contains("upper") ||
                            boneName.Contains("hips") || boneName.Contains("spine") || boneName.Contains("chest"))
                        {
                            Debug.LogWarning($"[CacheVerify] MISMATCH [{smr.name}] bone '{bones[i].name}': " +
                                $"posErr={posError:F4}m, rotErr={rotError:F2}deg");
                        }
                    }
                    else
                    {
                        matchCount++;
                    }
                }

                Debug.Log($"[CacheVerify] {smr.name}: {matchCount}/{count} OK, {mismatchCount} mismatches" +
                    (mismatchCount > 0 ? " ⚠️ BINDPOSE MISMATCH DETECTED" : " ✓ ALL CONSISTENT"));
            }
            Debug.Log("[AvatarCacheManager] === Verification Complete ===");
        }
    }
}
