using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UniGLTF;
using VRM;
using UniVRM10;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバターマニフェスト - VRM/FBXファイルのメタデータ
    /// </summary>
    [Serializable]
    public class AvatarManifest
    {
        public const string MANIFEST_FILE_NAME = "avatar_manifest.json";
        public const int CURRENT_VERSION = 1;

        // 基本情報
        public string avatarName;
        public string modelFileName;
        public string fileType;         // VRM / FBX
        public string vrmVersion;       // VRM 0.x / 1.0
        public int manifestVersion = CURRENT_VERSION;
        public string createdAt;
        public string modifiedAt;

        // VRMメタ情報
        public VrmMetaInfo meta;

        // テクスチャ情報
        public List<TextureEntry> textures = new List<TextureEntry>();

        // マテリアル情報
        public List<MaterialEntry> materials = new List<MaterialEntry>();

        // Humanoidボーン情報
        public HumanoidBoneInfo humanoidBones;

        [Serializable]
        public class VrmMetaInfo
        {
            public string title;
            public string author;
            public string version;
            public string contactInformation;
            public string reference;
            public string licenseType;
            public string otherLicenseUrl;
        }

        [Serializable]
        public class TextureEntry
        {
            public string name;
            public string path;
            public int width;
            public int height;
            public string format;
        }

        [Serializable]
        public class MaterialEntry
        {
            public string name;
            public string shaderName;
            public List<string> textureNames = new List<string>();
        }

        [Serializable]
        public class HumanoidBoneInfo
        {
            public bool isValid;
            public int boneCount;
            public List<string> mappedBones = new List<string>();
            public List<string> missingBones = new List<string>();
        }

        /// <summary>
        /// マニフェストを初期化
        /// </summary>
        public AvatarManifest()
        {
            meta = new VrmMetaInfo();
            humanoidBones = new HumanoidBoneInfo();
            createdAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            modifiedAt = createdAt;
        }

        /// <summary>
        /// VRMファイルからマニフェストを生成
        /// </summary>
        public static AvatarManifest CreateFromVRM(string vrmFilePath, GameObject loadedModel, string detectedVrmVersion)
        {
            try
            {
                var manifest = new AvatarManifest();
                manifest.modelFileName = Path.GetFileName(vrmFilePath);
                manifest.avatarName = Path.GetFileNameWithoutExtension(vrmFilePath);
                manifest.fileType = "VRM";
                manifest.vrmVersion = detectedVrmVersion;

                // Humanoidボーン情報を取得
                manifest.humanoidBones = ExtractHumanoidBoneInfo(loadedModel);

                // マテリアル情報を取得
                manifest.materials = ExtractMaterialInfo(loadedModel);

                Debug.Log($"[AvatarManifest] Created manifest for VRM: {manifest.avatarName}");
                return manifest;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarManifest] Failed to create manifest from VRM: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// VRM 0.xからメタ情報を取得
        /// </summary>
        public void ExtractVrm0Meta(VRMMeta vrmMeta)
        {
            if (vrmMeta == null) return;

            meta.title = vrmMeta.Meta?.Title ?? avatarName;
            meta.author = vrmMeta.Meta?.Author ?? "";
            meta.version = vrmMeta.Meta?.Version ?? "";
            meta.contactInformation = vrmMeta.Meta?.ContactInformation ?? "";
            meta.reference = vrmMeta.Meta?.Reference ?? "";

            Debug.Log($"[AvatarManifest] Extracted VRM 0.x meta: {meta.title} by {meta.author}");
        }

        /// <summary>
        /// VRM 1.0からメタ情報を取得
        /// </summary>
        public void ExtractVrm10Meta(Vrm10Instance vrm10Instance)
        {
            if (vrm10Instance == null || vrm10Instance.Vrm == null) return;

            var vrm10Meta = vrm10Instance.Vrm.Meta;
            if (vrm10Meta == null) return;

            meta.title = vrm10Meta.Name ?? avatarName;
            meta.author = string.Join(", ", vrm10Meta.Authors ?? new List<string>());
            meta.version = vrm10Meta.Version ?? "";
            meta.contactInformation = vrm10Meta.ContactInformation ?? "";
            meta.reference = string.Join(", ", vrm10Meta.References ?? new List<string>());

            Debug.Log($"[AvatarManifest] Extracted VRM 1.0 meta: {meta.title} by {meta.author}");
        }

        /// <summary>
        /// FBXファイルからマニフェストを生成
        /// </summary>
        public static AvatarManifest CreateFromFBX(string fbxFilePath, GameObject loadedModel)
        {
            try
            {
                var manifest = new AvatarManifest();
                manifest.modelFileName = Path.GetFileName(fbxFilePath);
                manifest.avatarName = Path.GetFileNameWithoutExtension(fbxFilePath);
                manifest.fileType = "FBX";
                manifest.vrmVersion = "";

                // Humanoidボーン情報を取得
                manifest.humanoidBones = ExtractHumanoidBoneInfo(loadedModel);

                // マテリアル情報を取得
                manifest.materials = ExtractMaterialInfo(loadedModel);

                // FBXと同じディレクトリからテクスチャを検索
                manifest.textures = SearchTexturesInDirectory(Path.GetDirectoryName(fbxFilePath));

                Debug.Log($"[AvatarManifest] Created manifest for FBX: {manifest.avatarName}");
                return manifest;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarManifest] Failed to create manifest from FBX: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Humanoidボーン情報を抽出
        /// </summary>
        private static HumanoidBoneInfo ExtractHumanoidBoneInfo(GameObject model)
        {
            var info = new HumanoidBoneInfo();

            var animator = model?.GetComponent<Animator>();
            if (animator == null || animator.avatar == null)
            {
                info.isValid = false;
                Debug.LogWarning("[AvatarManifest] No Animator or Avatar found");
                return info;
            }

            info.isValid = animator.avatar.isValid && animator.avatar.isHuman;

            // Humanoidボーンをチェック
            var humanBones = (HumanBodyBones[])Enum.GetValues(typeof(HumanBodyBones));
            foreach (var bone in humanBones)
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform != null)
                {
                    info.mappedBones.Add(bone.ToString());
                }
                else
                {
                    // 必須ボーンのみ記録
                    if (IsRequiredBone(bone))
                    {
                        info.missingBones.Add(bone.ToString());
                    }
                }
            }

            info.boneCount = info.mappedBones.Count;

            Debug.Log($"[AvatarManifest] Humanoid bones: {info.boneCount} mapped, {info.missingBones.Count} missing required");
            return info;
        }

        /// <summary>
        /// 必須ボーンかどうか
        /// </summary>
        private static bool IsRequiredBone(HumanBodyBones bone)
        {
            return bone switch
            {
                HumanBodyBones.Hips => true,
                HumanBodyBones.Spine => true,
                HumanBodyBones.Head => true,
                HumanBodyBones.LeftUpperArm => true,
                HumanBodyBones.LeftLowerArm => true,
                HumanBodyBones.LeftHand => true,
                HumanBodyBones.RightUpperArm => true,
                HumanBodyBones.RightLowerArm => true,
                HumanBodyBones.RightHand => true,
                HumanBodyBones.LeftUpperLeg => true,
                HumanBodyBones.LeftLowerLeg => true,
                HumanBodyBones.LeftFoot => true,
                HumanBodyBones.RightUpperLeg => true,
                HumanBodyBones.RightLowerLeg => true,
                HumanBodyBones.RightFoot => true,
                _ => false
            };
        }

        /// <summary>
        /// マテリアル情報を抽出
        /// </summary>
        private static List<MaterialEntry> ExtractMaterialInfo(GameObject model)
        {
            var materials = new List<MaterialEntry>();
            var renderers = model?.GetComponentsInChildren<Renderer>();

            if (renderers == null) return materials;

            var processedMaterials = new HashSet<string>();

            foreach (var renderer in renderers)
            {
                foreach (var mat in renderer.sharedMaterials)
                {
                    if (mat == null) continue;
                    if (processedMaterials.Contains(mat.name)) continue;

                    processedMaterials.Add(mat.name);

                    var entry = new MaterialEntry
                    {
                        name = mat.name,
                        shaderName = mat.shader?.name ?? "Unknown"
                    };

                    // テクスチャプロパティを取得
                    var textureNames = mat.GetTexturePropertyNames();
                    foreach (var texName in textureNames)
                    {
                        var tex = mat.GetTexture(texName);
                        if (tex != null)
                        {
                            entry.textureNames.Add(tex.name);
                        }
                    }

                    materials.Add(entry);
                }
            }

            Debug.Log($"[AvatarManifest] Extracted {materials.Count} materials");
            return materials;
        }

        /// <summary>
        /// ディレクトリ内のテクスチャを検索
        /// </summary>
        private static List<TextureEntry> SearchTexturesInDirectory(string directory)
        {
            var textures = new List<TextureEntry>();

            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return textures;

            string[] extensions = { "*.png", "*.jpg", "*.jpeg", "*.tga", "*.bmp" };

            foreach (var ext in extensions)
            {
                try
                {
                    var files = Directory.GetFiles(directory, ext, SearchOption.AllDirectories);
                    foreach (var file in files)
                    {
                        var entry = new TextureEntry
                        {
                            name = Path.GetFileNameWithoutExtension(file),
                            path = file,
                            format = Path.GetExtension(file).TrimStart('.')
                        };
                        textures.Add(entry);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AvatarManifest] Error searching textures: {e.Message}");
                }
            }

            Debug.Log($"[AvatarManifest] Found {textures.Count} textures in directory");
            return textures;
        }

        /// <summary>
        /// マニフェストが有効かどうか
        /// </summary>
        public bool IsValid()
        {
            if (string.IsNullOrEmpty(avatarName)) return false;
            if (string.IsNullOrEmpty(modelFileName)) return false;
            if (string.IsNullOrEmpty(fileType)) return false;

            // Humanoidボーンが有効か
            if (humanoidBones == null || !humanoidBones.isValid)
            {
                Debug.LogWarning("[AvatarManifest] Invalid: Humanoid bones not valid");
                return false;
            }

            return true;
        }

        /// <summary>
        /// マニフェストが空かどうか
        /// </summary>
        public bool IsEmpty()
        {
            return string.IsNullOrEmpty(avatarName) &&
                   string.IsNullOrEmpty(modelFileName) &&
                   (humanoidBones == null || humanoidBones.boneCount == 0);
        }

        /// <summary>
        /// マニフェストをファイルに保存
        /// </summary>
        public bool SaveToFile(string filePath)
        {
            try
            {
                modifiedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                string json = JsonUtility.ToJson(this, true);
                File.WriteAllText(filePath, json);

                Debug.Log($"[AvatarManifest] Saved manifest to: {filePath}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarManifest] Failed to save manifest: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// マニフェストをファイルから読み込み
        /// </summary>
        public static AvatarManifest LoadFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogWarning($"[AvatarManifest] Manifest file not found: {filePath}");
                    return null;
                }

                string json = File.ReadAllText(filePath);
                var manifest = JsonUtility.FromJson<AvatarManifest>(json);

                if (manifest == null)
                {
                    Debug.LogError($"[AvatarManifest] Failed to parse manifest: {filePath}");
                    return null;
                }

                Debug.Log($"[AvatarManifest] Loaded manifest: {manifest.avatarName}");
                return manifest;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarManifest] Failed to load manifest: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// マニフェストファイルのパスを取得（モデルファイルと同じディレクトリ）
        /// </summary>
        public static string GetManifestPath(string modelFilePath)
        {
            string directory = Path.GetDirectoryName(modelFilePath);
            string fileName = Path.GetFileNameWithoutExtension(modelFilePath);
            return Path.Combine(directory, $"{fileName}_manifest.json");
        }

        /// <summary>
        /// マニフェストファイルが存在するか
        /// </summary>
        public static bool ManifestExists(string modelFilePath)
        {
            string manifestPath = GetManifestPath(modelFilePath);
            return File.Exists(manifestPath);
        }
    }
}
