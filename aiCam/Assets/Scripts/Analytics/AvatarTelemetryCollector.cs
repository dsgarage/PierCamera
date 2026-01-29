using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using AICam.Analytics.DTOs;
using AICam.Core;
using PierCamera.Analytics;
using VRM;

namespace AICam.Analytics
{
    /// <summary>
    /// アバターロード時のテレメトリデータを収集するヘルパークラス
    /// </summary>
    public static class AvatarTelemetryCollector
    {
        /// <summary>
        /// VRM 0.x からテレメトリを収集
        /// </summary>
        public static AvatarLoadTelemetryDTO CollectFromVrm0x(
            GameObject model,
            string filePath,
            long fileSizeBytes,
            float loadTimeSeconds,
            bool success,
            string errorMessage = null,
            int slotIndex = -1)
        {
            AICamLogger.Log(AICamLogger.Category.Telemetry, "========================================");
            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Telemetry Collection Start (VRM 0.x) ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  File: {Path.GetFileName(filePath)}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Size: {fileSizeBytes / 1024.0 / 1024.0:F2} MB");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  LoadTime: {loadTimeSeconds:F3} sec");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Success: {success}");

            var dto = CreateBaseDTO(filePath, fileSizeBytes, loadTimeSeconds, success, errorMessage, slotIndex);
            dto.vrmVersion = "VRM_0_x";

            if (model != null && success)
            {
                dto.vrmMeta0x = CollectVRM0xMetadata(model);
                dto.meshStats = CollectMeshStatistics(model);
                dto.textureStats = CollectTextureStatistics(model);
                dto.avatarInfo = CollectAvatarInfo(model);
                dto.initFlags = CollectInitializationFlags(model, isVrm0x: true);
            }

            // デバイス情報をログ出力
            LogDeviceInfo(dto.device);

            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Telemetry Collection Complete ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, "========================================");

            return dto;
        }

        /// <summary>
        /// VRM 1.0 からテレメトリを収集
        /// </summary>
        public static AvatarLoadTelemetryDTO CollectFromVrm10(
            GameObject model,
            string filePath,
            long fileSizeBytes,
            float loadTimeSeconds,
            bool success,
            string errorMessage = null,
            int slotIndex = -1)
        {
            AICamLogger.Log(AICamLogger.Category.Telemetry, "========================================");
            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Telemetry Collection Start (VRM 1.0) ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  File: {Path.GetFileName(filePath)}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Size: {fileSizeBytes / 1024.0 / 1024.0:F2} MB");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  LoadTime: {loadTimeSeconds:F3} sec");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Success: {success}");

            var dto = CreateBaseDTO(filePath, fileSizeBytes, loadTimeSeconds, success, errorMessage, slotIndex);
            dto.vrmVersion = "VRM_1_0";

            if (model != null && success)
            {
                dto.vrmMeta10 = CollectVRM10Metadata(model);
                dto.meshStats = CollectMeshStatistics(model);
                dto.textureStats = CollectTextureStatistics(model);
                dto.avatarInfo = CollectAvatarInfo(model);
                dto.initFlags = CollectInitializationFlags(model, isVrm0x: false);
            }

            // デバイス情報をログ出力
            LogDeviceInfo(dto.device);

            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Telemetry Collection Complete ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, "========================================");

            return dto;
        }

        /// <summary>
        /// FBX からテレメトリを収集
        /// </summary>
        public static AvatarLoadTelemetryDTO CollectFromFBX(
            GameObject model,
            string filePath,
            long fileSizeBytes,
            float loadTimeSeconds,
            bool success,
            string errorMessage = null,
            int slotIndex = -1)
        {
            AICamLogger.Log(AICamLogger.Category.Telemetry, "========================================");
            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Telemetry Collection Start (FBX) ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  File: {Path.GetFileName(filePath)}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Size: {fileSizeBytes / 1024.0 / 1024.0:F2} MB");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  LoadTime: {loadTimeSeconds:F3} sec");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Success: {success}");

            var dto = CreateBaseDTO(filePath, fileSizeBytes, loadTimeSeconds, success, errorMessage, slotIndex);
            dto.vrmVersion = "FBX";

            if (model != null && success)
            {
                dto.meshStats = CollectMeshStatistics(model);
                dto.textureStats = CollectTextureStatistics(model);
                dto.avatarInfo = CollectAvatarInfo(model);
                dto.initFlags = CollectInitializationFlags(model, isVrm0x: false);
            }

            // デバイス情報をログ出力
            LogDeviceInfo(dto.device);

            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Telemetry Collection Complete ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, "========================================");

            return dto;
        }

        /// <summary>
        /// ベースDTOを作成
        /// </summary>
        private static AvatarLoadTelemetryDTO CreateBaseDTO(
            string filePath,
            long fileSizeBytes,
            float loadTimeSeconds,
            bool success,
            string errorMessage,
            int slotIndex)
        {
            return new AvatarLoadTelemetryDTO
            {
                sessionId = Guid.NewGuid().ToString(),
                timestamp = DateTime.UtcNow.ToString("o"),
                fileName = Path.GetFileName(filePath),
                fileExtension = Path.GetExtension(filePath)?.TrimStart('.').ToLower(),
                fileSizeBytes = fileSizeBytes,
                fileHash = null, // MD5は重いのでスキップ
                success = success,
                errorMessage = errorMessage,
                slotIndex = slotIndex,
                performance = new PerformanceMetrics
                {
                    loadTimeSeconds = loadTimeSeconds
                },
                device = CollectDeviceInfo()
            };
        }

        /// <summary>
        /// VRM 0.x メタデータを収集
        /// </summary>
        private static VrmMetadata0x CollectVRM0xMetadata(GameObject model)
        {
            try
            {
                var vrmMeta = model.GetComponent<VRMMeta>();
                if (vrmMeta?.Meta == null) return null;

                var meta = vrmMeta.Meta;
                var metadata = new VrmMetadata0x
                {
                    title = meta.Title,
                    version = meta.Version,
                    author = meta.Author,
                    contactInformation = meta.ContactInformation,
                    reference = meta.Reference,
                    allowedUser = meta.AllowedUser.ToString(),
                    violentUsage = meta.ViolentUssage.ToString(),
                    sexualUsage = meta.SexualUssage.ToString(),
                    commercialUsage = meta.CommercialUssage.ToString(),
                    otherPermissionUrl = meta.OtherPermissionUrl,
                    licenseType = meta.LicenseType.ToString()
                };

                // 詳細ログ出力
                AICamLogger.Log(AICamLogger.Category.Telemetry, "=== VRM 0.x Metadata ===");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Title: {metadata.title}");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Version: {metadata.version}");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Author: {metadata.author}");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  AllowedUser: {metadata.allowedUser}");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  License: {metadata.licenseType}");

                return metadata;
            }
            catch (Exception e)
            {
                AICamLogger.LogWarning(AICamLogger.Category.Telemetry, $"Failed to collect VRM 0.x metadata: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// VRM 1.0 メタデータを収集
        /// </summary>
        private static VrmMetadata10 CollectVRM10Metadata(GameObject model)
        {
            try
            {
                var vrm10 = model.GetComponent<UniVRM10.Vrm10Instance>();
                if (vrm10?.Vrm?.Meta == null) return null;

                var meta = vrm10.Vrm.Meta;
                return new VrmMetadata10
                {
                    name = meta.Name,
                    version = meta.Version,
                    authors = meta.Authors?.ToArray(),
                    copyrightInformation = meta.CopyrightInformation,
                    contactInformation = meta.ContactInformation,
                    references = meta.References?.ToArray(),
                    thirdPartyLicenses = meta.ThirdPartyLicenses,
                    avatarPermission = meta.AvatarPermission.ToString(),
                    violentUsage = meta.ViolentUsage.ToString(),
                    sexualUsage = meta.SexualUsage.ToString(),
                    commercialUsage = meta.CommercialUsage.ToString(),
                    allowPoliticalOrReligiousUsage = meta.PoliticalOrReligiousUsage,
                    allowAntisocialOrHateUsage = meta.AntisocialOrHateUsage,
                    creditNotation = meta.CreditNotation.ToString(),
                    allowRedistribution = meta.Redistribution,
                    modification = meta.Modification.ToString(),
                    otherLicenseUrl = meta.OtherLicenseUrl
                };
            }
            catch (Exception e)
            {
                AICamLogger.LogWarning(AICamLogger.Category.Telemetry, $"Failed to collect VRM 1.0 metadata: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// メッシュ統計を収集
        /// </summary>
        private static MeshStatistics CollectMeshStatistics(GameObject model)
        {
            try
            {
                var skinnedMeshRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>();
                var meshRenderers = model.GetComponentsInChildren<MeshRenderer>();

                int totalVertices = 0;
                int totalTriangles = 0;
                int totalBlendShapes = 0;
                int totalBones = 0;
                var materialNames = new List<string>();
                var shaderNames = new List<string>();
                var shaderDetails = new List<ShaderInfo>();

                foreach (var smr in skinnedMeshRenderers)
                {
                    if (smr.sharedMesh != null)
                    {
                        totalVertices += smr.sharedMesh.vertexCount;
                        totalTriangles += smr.sharedMesh.triangles.Length / 3;
                        totalBlendShapes += smr.sharedMesh.blendShapeCount;
                    }
                    if (smr.bones != null)
                    {
                        totalBones = Mathf.Max(totalBones, smr.bones.Length);
                    }
                    CollectMaterialInfo(smr.sharedMaterials, materialNames, shaderNames, shaderDetails);
                }

                foreach (var mr in meshRenderers)
                {
                    var filter = mr.GetComponent<MeshFilter>();
                    if (filter?.sharedMesh != null)
                    {
                        totalVertices += filter.sharedMesh.vertexCount;
                        totalTriangles += filter.sharedMesh.triangles.Length / 3;
                    }
                    CollectMaterialInfo(mr.sharedMaterials, materialNames, shaderNames, shaderDetails);
                }

                // SpringBone count
                int springBoneCount = 0;
                var springBones = model.GetComponentsInChildren<VRMSpringBone>();
                springBoneCount = springBones?.Length ?? 0;

                var stats = new MeshStatistics
                {
                    skinnedMeshRendererCount = skinnedMeshRenderers.Length,
                    meshRendererCount = meshRenderers.Length,
                    totalVertexCount = totalVertices,
                    totalTriangleCount = totalTriangles,
                    totalBlendShapeCount = totalBlendShapes,
                    totalMaterialCount = materialNames.Distinct().Count(),
                    totalBoneCount = totalBones,
                    springBoneCount = springBoneCount,
                    materialNames = materialNames.Distinct().ToArray(),
                    shaderNames = shaderNames.Distinct().ToArray(),
                    shaderDetails = shaderDetails.ToArray()
                };

                // 詳細ログ出力
                LogMeshStatistics(stats);

                return stats;
            }
            catch (Exception e)
            {
                AICamLogger.LogWarning(AICamLogger.Category.Telemetry, $"Failed to collect mesh statistics: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// メッシュ統計をログ出力
        /// </summary>
        private static void LogMeshStatistics(MeshStatistics stats)
        {
            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Mesh Statistics ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  SkinnedMeshRenderer: {stats.skinnedMeshRendererCount}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  MeshRenderer: {stats.meshRendererCount}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Vertices: {stats.totalVertexCount:N0}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Triangles: {stats.totalTriangleCount:N0}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  BlendShapes: {stats.totalBlendShapeCount}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Materials: {stats.totalMaterialCount}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Bones: {stats.totalBoneCount}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  SpringBones: {stats.springBoneCount}");

            // シェーダー詳細をログ出力
            if (stats.shaderDetails != null && stats.shaderDetails.Length > 0)
            {
                AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Shader Details ===");
                foreach (var shader in stats.shaderDetails)
                {
                    var keywords = shader.keywords != null && shader.keywords.Length > 0
                        ? string.Join(", ", shader.keywords)
                        : "(none)";
                    AICamLogger.Log(AICamLogger.Category.Telemetry,
                        $"  [{shader.materialName}] {shader.name} (RenderQueue={shader.renderQueue}, Pass={shader.passCount}, Supported={shader.isSupported})");
                    AICamLogger.Log(AICamLogger.Category.Telemetry, $"    Keywords: {keywords}");
                }
            }
        }

        /// <summary>
        /// テクスチャ統計を収集
        /// </summary>
        private static TextureStatistics CollectTextureStatistics(GameObject model)
        {
            try
            {
                var renderers = model.GetComponentsInChildren<Renderer>();
                var textures = new HashSet<Texture>();
                var textureFormats = new List<string>();
                var textureWidths = new List<int>();
                var textureHeights = new List<int>();
                var textureNames = new List<string>();
                long totalMemory = 0;
                int maxWidth = 0;
                int maxHeight = 0;

                foreach (var renderer in renderers)
                {
                    if (renderer.sharedMaterials == null) continue;

                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat == null) continue;

                        // 主要なテクスチャプロパティを収集
                        var texturePropertyNames = new[]
                        {
                            "_MainTex", "_BumpMap", "_MetallicGlossMap", "_OcclusionMap",
                            "_EmissionMap", "_ShadeTexture", "_ShadeTex", "_MatCapTexture",
                            "_RimTexture", "_OutlineColorMask"
                        };

                        foreach (var propName in texturePropertyNames)
                        {
                            if (!mat.HasProperty(propName)) continue;
                            var tex = mat.GetTexture(propName);
                            if (tex == null || textures.Contains(tex)) continue;

                            textures.Add(tex);

                            if (tex is Texture2D tex2D)
                            {
                                textureFormats.Add(tex2D.format.ToString());
                                textureWidths.Add(tex2D.width);
                                textureHeights.Add(tex2D.height);
                                textureNames.Add(tex2D.name);

                                maxWidth = Mathf.Max(maxWidth, tex2D.width);
                                maxHeight = Mathf.Max(maxHeight, tex2D.height);

                                totalMemory += Profiler.GetRuntimeMemorySizeLong(tex2D);
                            }
                        }
                    }
                }

                var stats = new TextureStatistics
                {
                    textureCount = textures.Count,
                    totalTextureMemoryBytes = totalMemory,
                    maxTextureWidth = maxWidth,
                    maxTextureHeight = maxHeight,
                    textureFormats = textureFormats.Distinct().ToArray(),
                    textureWidths = textureWidths.ToArray(),
                    textureHeights = textureHeights.ToArray(),
                    textureNames = textureNames.ToArray()
                };

                // 詳細ログ出力
                LogTextureStatistics(stats);

                return stats;
            }
            catch (Exception e)
            {
                AICamLogger.LogWarning(AICamLogger.Category.Telemetry, $"Failed to collect texture statistics: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// テクスチャ統計をログ出力
        /// </summary>
        private static void LogTextureStatistics(TextureStatistics stats)
        {
            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Texture Statistics ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Count: {stats.textureCount}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Total Memory: {stats.totalTextureMemoryBytes / 1024.0 / 1024.0:F2} MB");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Max Size: {stats.maxTextureWidth}x{stats.maxTextureHeight}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Formats: {string.Join(", ", stats.textureFormats ?? Array.Empty<string>())}");

            if (stats.textureNames != null)
            {
                for (int i = 0; i < stats.textureNames.Length; i++)
                {
                    var name = stats.textureNames[i];
                    var width = i < stats.textureWidths.Length ? stats.textureWidths[i] : 0;
                    var height = i < stats.textureHeights.Length ? stats.textureHeights[i] : 0;
                    AICamLogger.Log(AICamLogger.Category.Telemetry, $"  [{i}] {name} ({width}x{height})");
                }
            }
        }

        /// <summary>
        /// Unity Avatar情報を収集
        /// </summary>
        private static AvatarInfo CollectAvatarInfo(GameObject model)
        {
            try
            {
                var animator = model.GetComponent<Animator>();
                if (animator == null || animator.avatar == null)
                {
                    return new AvatarInfo
                    {
                        isValid = false,
                        isHuman = false
                    };
                }

                var avatar = animator.avatar;
                var info = new AvatarInfo
                {
                    isValid = avatar.isValid,
                    isHuman = avatar.isHuman
                };

                // HumanDescriptionの情報を取得（Editorでのみ完全に取得可能）
                if (avatar.isHuman)
                {
                    // ボーン数はHumanTraitから推測
                    info.humanBoneCount = HumanTrait.BoneCount;
                    info.skeletonBoneCount = avatar.isValid ? CountSkeletonBones(model.transform) : 0;

                    // HumanDescriptionのパラメータは直接取得できないためデフォルト値
                    info.armStretch = 0.05f;
                    info.legStretch = 0.05f;
                    info.upperArmTwist = 0.5f;
                    info.lowerArmTwist = 0.5f;
                    info.upperLegTwist = 0.5f;
                    info.lowerLegTwist = 0.5f;
                    info.feetSpacing = 0f;
                    info.hasTranslationDoF = false;
                }

                // 詳細ログ出力
                AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Avatar Info ===");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Valid: {info.isValid}");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Human: {info.isHuman}");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  HumanBoneCount: {info.humanBoneCount}");
                AICamLogger.Log(AICamLogger.Category.Telemetry, $"  SkeletonBoneCount: {info.skeletonBoneCount}");

                return info;
            }
            catch (Exception e)
            {
                AICamLogger.LogWarning(AICamLogger.Category.Telemetry, $"Failed to collect avatar info: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// スケルトンボーン数をカウント
        /// </summary>
        private static int CountSkeletonBones(Transform root)
        {
            int count = 0;
            CountBonesRecursive(root, ref count);
            return count;
        }

        private static void CountBonesRecursive(Transform t, ref int count)
        {
            count++;
            foreach (Transform child in t)
            {
                CountBonesRecursive(child, ref count);
            }
        }

        /// <summary>
        /// 初期化状態フラグを収集
        /// </summary>
        private static InitializationFlags CollectInitializationFlags(GameObject model, bool isVrm0x)
        {
            try
            {
                var animator = model.GetComponent<Animator>();
                var renderers = model.GetComponentsInChildren<Renderer>();

                var flags = new InitializationFlags
                {
                    // Animatorコンポーネントが存在し、Avatarがバインドされているかで判定
                    // runtimeAnimatorControllerは後からApplyDefaultAOCで設定されるため、ここでは判定しない
                    animatorInitialized = animator != null && animator.avatar != null,
                    avatarValid = animator?.avatar?.isValid ?? false,
                    meshRendererEnabled = renderers.Any(r => r.enabled),
                    rootMotionEnabled = animator?.applyRootMotion ?? false,
                    materialInitialized = renderers.Any(r => r.sharedMaterials != null && r.sharedMaterials.Length > 0)
                };

                // VRM 0.x の場合
                if (isVrm0x)
                {
                    var springBones = model.GetComponentsInChildren<VRMSpringBone>();
                    flags.springBoneInitialized = springBones != null && springBones.Length > 0;

                    var blendShapeProxy = model.GetComponent<VRMBlendShapeProxy>();
                    flags.expressionInitialized = blendShapeProxy != null;

                    var lookAt = model.GetComponent<VRMLookAtHead>();
                    flags.lookAtInitialized = lookAt != null;
                }
                else
                {
                    // VRM 1.0 の場合
                    var vrm10 = model.GetComponent<UniVRM10.Vrm10Instance>();
                    if (vrm10 != null)
                    {
                        flags.springBoneInitialized = vrm10.SpringBone != null;
                        flags.expressionInitialized = vrm10.Runtime?.Expression != null;
                        flags.lookAtInitialized = vrm10.Runtime?.LookAt != null;
                    }
                }

                // 詳細ログ出力
                LogInitializationFlags(flags, isVrm0x);

                return flags;
            }
            catch (Exception e)
            {
                AICamLogger.LogWarning(AICamLogger.Category.Telemetry, $"Failed to collect initialization flags: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// 初期化フラグをログ出力
        /// </summary>
        private static void LogInitializationFlags(InitializationFlags flags, bool isVrm0x)
        {
            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Initialization Flags ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Animator: {(flags.animatorInitialized ? "✓" : "✗")}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Avatar Valid: {(flags.avatarValid ? "✓" : "✗")}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  MeshRenderer: {(flags.meshRendererEnabled ? "✓" : "✗")}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Material: {(flags.materialInitialized ? "✓" : "✗")}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  RootMotion: {(flags.rootMotionEnabled ? "✓" : "✗")}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  SpringBone: {(flags.springBoneInitialized ? "✓" : "✗")}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Expression ({(isVrm0x ? "VRM0.x" : "VRM1.0")}): {(flags.expressionInitialized ? "✓" : "✗")}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  LookAt: {(flags.lookAtInitialized ? "✓" : "✗")}");
        }

        /// <summary>
        /// マテリアル情報を収集
        /// </summary>
        private static void CollectMaterialInfo(
            Material[] materials,
            List<string> materialNames,
            List<string> shaderNames,
            List<ShaderInfo> shaderDetails = null)
        {
            if (materials == null) return;

            foreach (var mat in materials)
            {
                if (mat != null)
                {
                    materialNames.Add(mat.name);
                    if (mat.shader != null)
                    {
                        shaderNames.Add(mat.shader.name);

                        // 詳細なシェーダー情報を収集
                        if (shaderDetails != null)
                        {
                            var info = new ShaderInfo
                            {
                                name = mat.shader.name,
                                materialName = mat.name,
                                renderQueue = mat.renderQueue,
                                keywords = mat.shaderKeywords ?? Array.Empty<string>(),
                                passCount = mat.passCount,
                                isSupported = mat.shader.isSupported
                            };
                            shaderDetails.Add(info);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// デバイス情報を収集
        /// </summary>
        private static DeviceInfo CollectDeviceInfo()
        {
            return new DeviceInfo
            {
                deviceModel = SystemInfo.deviceModel,
                deviceName = DeviceAnalytics.GetFriendlyDeviceName(),
                osVersion = SystemInfo.operatingSystem,
                unityVersion = Application.unityVersion,
                appVersion = Application.version,
                buildVersion = Application.buildGUID,
                bundleVersion = GetBundleVersion(),
                hasLiDAR = DeviceAnalytics.HasLiDAR(),
                deviceCategory = DeviceAnalytics.GetDeviceCategory().ToString(),
                systemMemoryMB = SystemInfo.systemMemorySize,
                graphicsMemoryMB = SystemInfo.graphicsMemorySize,
                graphicsDeviceName = SystemInfo.graphicsDeviceName
            };
        }

        /// <summary>
        /// デバイス情報をログ出力
        /// </summary>
        private static void LogDeviceInfo(DeviceInfo device)
        {
            if (device == null) return;

            AICamLogger.Log(AICamLogger.Category.Telemetry, "=== Device Info ===");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Model: {device.deviceModel}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Name: {device.deviceName}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  OS: {device.osVersion}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Unity: {device.unityVersion}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  App: {device.appVersion}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  LiDAR: {device.hasLiDAR}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  Category: {device.deviceCategory}");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  RAM: {device.systemMemoryMB} MB");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  VRAM: {device.graphicsMemoryMB} MB");
            AICamLogger.Log(AICamLogger.Category.Telemetry, $"  GPU: {device.graphicsDeviceName}");
        }

        /// <summary>
        /// バンドルバージョンを取得
        /// </summary>
        private static string GetBundleVersion()
        {
#if UNITY_IOS
            // iOSの場合、CFBundleVersionを取得
            return UnityEngine.iOS.Device.generation.ToString();
#elif UNITY_ANDROID
            // Androidの場合、versionCodeを取得（直接取得は難しいのでApplication.versionを使用）
            return Application.version;
#else
            return Application.version;
#endif
        }
    }
}
