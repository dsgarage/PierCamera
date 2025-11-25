using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Assimp;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Assimp SceneからAssimpSceneCacheを生成するビルダー
    /// メッシュ/ウェイトデータを除く、マテリアル・ノード階層・マッピング情報を抽出
    /// </summary>
    public static class AssimpSceneCacheBuilder
    {
        private const string LOG_PREFIX = "[AssimpSceneCacheBuilder]";

        /// <summary>
        /// Assimp SceneからキャッシュをビルドしてJSON保存
        /// </summary>
        /// <param name="scene">Assimp Scene</param>
        /// <param name="fbxPath">FBXファイルパス</param>
        /// <param name="outputDirectory">出力ディレクトリ（nullの場合はFBXと同じディレクトリ）</param>
        /// <returns>生成されたAssimpSceneCache</returns>
        public static AssimpSceneCache BuildAndSave(Scene scene, string fbxPath, string outputDirectory = null)
        {
            if (scene == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Scene is null");
                return null;
            }

            if (string.IsNullOrEmpty(fbxPath))
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} FBX path is null or empty");
                return null;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} Building cache from scene...");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   FBX: {Path.GetFileName(fbxPath)}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Materials: {scene.MaterialCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Meshes: {scene.MeshCount}");

            var cache = new AssimpSceneCache();

            // 基本情報
            cache.fbxFileName = Path.GetFileName(fbxPath);
            cache.fbxLastModified = File.GetLastWriteTime(fbxPath).ToString("yyyy-MM-dd HH:mm:ss");
            cache.generatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // マテリアル情報を抽出
            ExtractMaterials(scene, cache);

            // ノード階層情報を抽出
            ExtractNodes(scene, cache);

            // MeshNode→Materialマッピングを構築
            BuildMeshNodeToMaterialMapping(scene, cache);

            // JSON保存
            string saveDirectory = outputDirectory ?? Path.GetDirectoryName(fbxPath);
            string cachePath = Path.Combine(saveDirectory, "AssimpSceneCache.json");

            try
            {
                string json = JsonUtility.ToJson(cache, prettyPrint: true);
                File.WriteAllText(cachePath, json);

                UnityEngine.Debug.Log($"{LOG_PREFIX} ✓ Cache saved: {cachePath}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   - Materials: {cache.materials.Count}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   - Nodes: {cache.nodes.Count}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   - MeshNode mappings: {cache.meshNodeToMaterialIndices.Count}");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to save cache: {ex.Message}");
                return null;
            }

            return cache;
        }

        /// <summary>
        /// マテリアル情報を抽出
        /// </summary>
        private static void ExtractMaterials(Scene scene, AssimpSceneCache cache)
        {
            for (int i = 0; i < scene.MaterialCount; i++)
            {
                var assimpMat = scene.Materials[i];
                var matInfo = new AssimpSceneCache.MaterialInfo
                {
                    name = assimpMat.Name,
                    materialIndex = i
                };

                // カラープロパティ
                if (assimpMat.HasColorDiffuse)
                {
                    var c = assimpMat.ColorDiffuse;
                    matInfo.diffuseColor = new AssimpSceneCache.SerializableColor(c.R, c.G, c.B, c.A);
                }

                if (assimpMat.HasColorSpecular)
                {
                    var c = assimpMat.ColorSpecular;
                    matInfo.specularColor = new AssimpSceneCache.SerializableColor(c.R, c.G, c.B, c.A);
                }

                if (assimpMat.HasColorAmbient)
                {
                    var c = assimpMat.ColorAmbient;
                    matInfo.ambientColor = new AssimpSceneCache.SerializableColor(c.R, c.G, c.B, c.A);
                }

                if (assimpMat.HasColorEmissive)
                {
                    var c = assimpMat.ColorEmissive;
                    matInfo.emissiveColor = new AssimpSceneCache.SerializableColor(c.R, c.G, c.B, c.A);
                }

                // スカラープロパティ
                if (assimpMat.HasShininess)
                    matInfo.shininess = assimpMat.Shininess;

                if (assimpMat.HasOpacity)
                    matInfo.opacity = assimpMat.Opacity;

                if (assimpMat.HasReflectivity)
                    matInfo.reflectivity = assimpMat.Reflectivity;

                // テクスチャ情報を抽出
                ExtractTextures(assimpMat, matInfo);

                cache.materials.Add(matInfo);
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX}   Extracted {cache.materials.Count} materials");
        }

        /// <summary>
        /// テクスチャ情報を抽出
        /// </summary>
        private static void ExtractTextures(Assimp.Material assimpMat, AssimpSceneCache.MaterialInfo matInfo)
        {
            // 主要なテクスチャタイプを抽出
            var textureTypes = new[]
            {
                TextureType.Diffuse,
                TextureType.Normals,
                TextureType.Specular,
                TextureType.Emissive,
                TextureType.Height,
                TextureType.Opacity,
                TextureType.Metalness,
                TextureType.Roughness
            };

            foreach (var texType in textureTypes)
            {
                if (assimpMat.HasTextureDiffuse && texType == TextureType.Diffuse)
                {
                    var texSlot = assimpMat.TextureDiffuse;
                    AddTextureInfo(matInfo, texSlot, "Diffuse");
                }
                else if (assimpMat.HasTextureNormal && texType == TextureType.Normals)
                {
                    var texSlot = assimpMat.TextureNormal;
                    AddTextureInfo(matInfo, texSlot, "Normal");
                }
                else if (assimpMat.HasTextureSpecular && texType == TextureType.Specular)
                {
                    var texSlot = assimpMat.TextureSpecular;
                    AddTextureInfo(matInfo, texSlot, "Specular");
                }
                else if (assimpMat.HasTextureEmissive && texType == TextureType.Emissive)
                {
                    var texSlot = assimpMat.TextureEmissive;
                    AddTextureInfo(matInfo, texSlot, "Emissive");
                }
                else if (assimpMat.HasTextureHeight && texType == TextureType.Height)
                {
                    var texSlot = assimpMat.TextureHeight;
                    AddTextureInfo(matInfo, texSlot, "Height");
                }
            }
        }

        /// <summary>
        /// TextureSlotから情報を抽出してリストに追加
        /// </summary>
        private static void AddTextureInfo(AssimpSceneCache.MaterialInfo matInfo, TextureSlot texSlot, string typeName)
        {
            var texInfo = new AssimpSceneCache.TextureInfo
            {
                textureType = typeName,
                filePath = texSlot.FilePath,
                fileName = Path.GetFileName(texSlot.FilePath),
                isEmbedded = texSlot.FilePath.StartsWith("*"), // 埋め込みテクスチャは "*0", "*1" のような形式
                embeddedIndex = -1
            };

            // 埋め込みテクスチャのインデックスを抽出
            if (texInfo.isEmbedded && texSlot.FilePath.Length > 1)
            {
                if (int.TryParse(texSlot.FilePath.Substring(1), out int index))
                {
                    texInfo.embeddedIndex = index;
                }
            }

            matInfo.textures.Add(texInfo);
        }

        /// <summary>
        /// ノード階層情報を抽出（再帰的）
        /// </summary>
        private static void ExtractNodes(Scene scene, AssimpSceneCache cache)
        {
            if (scene.RootNode != null)
            {
                ExtractNodeRecursive(scene.RootNode, null, cache);
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX}   Extracted {cache.nodes.Count} nodes");
        }

        /// <summary>
        /// ノード情報を再帰的に抽出
        /// </summary>
        private static void ExtractNodeRecursive(Node node, string parentName, AssimpSceneCache cache)
        {
            var nodeInfo = new AssimpSceneCache.NodeInfo
            {
                name = node.Name,
                parentName = parentName,
                hasMesh = node.MeshCount > 0,
                meshIndices = new int[node.MeshCount]
            };

            // メッシュインデックスをコピー
            for (int i = 0; i < node.MeshCount; i++)
            {
                nodeInfo.meshIndices[i] = node.MeshIndices[i];
            }

            cache.nodes.Add(nodeInfo);

            // 子ノードを再帰的に処理
            foreach (var childNode in node.Children)
            {
                ExtractNodeRecursive(childNode, node.Name, cache);
            }
        }

        /// <summary>
        /// MeshNode→Materialマッピングを構築
        /// </summary>
        private static void BuildMeshNodeToMaterialMapping(Scene scene, AssimpSceneCache cache)
        {
            foreach (var nodeInfo in cache.nodes)
            {
                if (!nodeInfo.hasMesh || nodeInfo.meshIndices.Length == 0)
                    continue;

                // このノードが使用するマテリアルインデックスを収集
                var materialIndices = new HashSet<int>();

                foreach (int meshIndex in nodeInfo.meshIndices)
                {
                    if (meshIndex >= 0 && meshIndex < scene.MeshCount)
                    {
                        var mesh = scene.Meshes[meshIndex];
                        materialIndices.Add(mesh.MaterialIndex);
                    }
                }

                // Dictionary に追加（JSON シリアライズ用にキーバリューペアのリストとして保存）
                cache.meshNodeToMaterialIndices[nodeInfo.name] = new List<int>(materialIndices).ToArray();
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX}   Built {cache.meshNodeToMaterialIndices.Count} mesh→material mappings");
        }

        /// <summary>
        /// キャッシュをロード
        /// </summary>
        /// <param name="fbxPath">FBXファイルパス</param>
        /// <param name="searchDirectory">検索ディレクトリ（nullの場合はFBXと同じディレクトリ）</param>
        /// <returns>ロードされたキャッシュ、または null</returns>
        public static AssimpSceneCache Load(string fbxPath, string searchDirectory = null)
        {
            string directory = searchDirectory ?? Path.GetDirectoryName(fbxPath);
            string cachePath = Path.Combine(directory, "AssimpSceneCache.json");

            if (!File.Exists(cachePath))
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} Cache not found: {cachePath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(cachePath);
                var cache = JsonUtility.FromJson<AssimpSceneCache>(json);

                // キャッシュの有効性をチェック（FBXファイルの更新日時）
                if (File.Exists(fbxPath))
                {
                    string currentModified = File.GetLastWriteTime(fbxPath).ToString("yyyy-MM-dd HH:mm:ss");
                    if (cache.fbxLastModified != currentModified)
                    {
                        UnityEngine.Debug.LogWarning($"{LOG_PREFIX} Cache is outdated (FBX modified)");
                        UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   Cache: {cache.fbxLastModified}");
                        UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   Current: {currentModified}");
                        return null;
                    }
                }

                UnityEngine.Debug.Log($"{LOG_PREFIX} ✓ Cache loaded: {cachePath}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   - Materials: {cache.materials.Count}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   - Nodes: {cache.nodes.Count}");

                return cache;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to load cache: {ex.Message}");
                return null;
            }
        }
    }
}
