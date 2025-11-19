using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UniSIL.ShaderInference;
using UniSIL.ShaderInference.MaterialReconstruction;

namespace AICam.FBXLoader
{
    using System;
    using Cysharp.Threading.Tasks;

    public class RuntimeMaterialManager
    {
        // キャッシュされたマテリアル情報を保持するデータクラス
        public class MaterialData
        {
            public string materialName;  // マテリアルの名前
            public string texturePath;  // テクスチャのパス
            public string shaderName;   // シェーダー名
            public Color mainColor;     // マテリアルのメインカラー
        }

        private MaterialCacheDatabase materialCacheDatabase;
        private System.Text.StringBuilder materialSearchLog = new System.Text.StringBuilder();
        private System.Text.StringBuilder meshDiagnosticsLog = new System.Text.StringBuilder();

        // ログ記録が有効かどうか
        private static bool IsLoggingEnabled =>
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            true;
#else
            false;
#endif

        // 条件付きログ記録ヘルパー
        private void LogMaterialSearch(string message)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            materialSearchLog.AppendLine(message);
#endif
        }

        private void LogMeshDiagnostics(string message)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            meshDiagnosticsLog.AppendLine(message);
#endif
        }

        public string GetMaterialSearchLog() => materialSearchLog.ToString();
        public string GetMeshDiagnosticsLog() => meshDiagnosticsLog.ToString();
        public string GetCombinedLog()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            var log = new System.Text.StringBuilder();

            // 環境情報ヘッダーを追加
            log.AppendLine("=== ENVIRONMENT INFO ===");
#if UNITY_EDITOR
            log.AppendLine("Environment: UNITY EDITOR");
#elif DEVELOPMENT_BUILD
            log.AppendLine("Environment: DEVELOPMENT BUILD");
#else
            log.AppendLine("Environment: RELEASE BUILD");
#endif
            log.AppendLine($"Unity Version: {Application.unityVersion}");
            log.AppendLine($"Platform: {Application.platform}");
            log.AppendLine($"Device Model: {SystemInfo.deviceModel}");
            log.AppendLine($"OS: {SystemInfo.operatingSystem}");
            log.AppendLine($"CPU: {SystemInfo.processorType} ({SystemInfo.processorCount} cores)");
            log.AppendLine($"GPU: {SystemInfo.graphicsDeviceName}");
            log.AppendLine($"Memory: {SystemInfo.systemMemorySize} MB");
            log.AppendLine($"Timestamp: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            log.AppendLine();

            // マテリアル検索ログ
            log.Append(materialSearchLog.ToString());
            log.AppendLine();

            // メッシュ診断ログ
            log.Append(meshDiagnosticsLog.ToString());

            return log.ToString();
#else
            return string.Empty;
#endif
        }

        /// <summary>
        /// メッシュ診断を実行（外部から呼び出し可能）
        /// </summary>
        public void DiagnoseMeshStateWithLabel(GameObject gameObject, string label)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"[RuntimeMaterialManager] DiagnoseMeshStateWithLabel() 開始 - GameObject: {gameObject?.name ?? "null"}, Label: {label}");
            Debug.Log($"[RuntimeMaterialManager] meshDiagnosticsLog current length: {meshDiagnosticsLog.Length}");

            meshDiagnosticsLog.AppendLine($"\n=== {label} ===");
            meshDiagnosticsLog.AppendLine($"Time: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            meshDiagnosticsLog.AppendLine();

            Debug.Log($"[RuntimeMaterialManager] DiagnoseMeshStateInternal() を呼び出し");
            DiagnoseMeshStateInternal(gameObject);

            Debug.Log($"[RuntimeMaterialManager] DiagnoseMeshStateWithLabel() 完了 - meshDiagnosticsLog length: {meshDiagnosticsLog.Length}");
#endif
        }

        /// <summary>
        /// シーン上の全Humanoidを検査してログに記録
        /// </summary>
        public void AnalyzeSceneHumanoids()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log("[RuntimeMaterialManager] AnalyzeSceneHumanoids() 開始");

            // ログをクリア
            materialSearchLog.Clear();
            meshDiagnosticsLog.Clear();

            materialSearchLog.AppendLine("=== Scene Humanoid Analysis ===");
            materialSearchLog.AppendLine($"Time: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            materialSearchLog.AppendLine();

            // シーン上の全Animatorを検索
            Animator[] animators = UnityEngine.Object.FindObjectsOfType<Animator>();

            var humanoids = animators
                .Where(a => a.avatar != null && a.avatar.isHuman)
                .Select(a => a.gameObject)
                .ToList();

            materialSearchLog.AppendLine($"Found {humanoids.Count} Humanoid(s) in scene");
            materialSearchLog.AppendLine();

            if (humanoids.Count == 0)
            {
                materialSearchLog.AppendLine("⚠ No Humanoids found in scene.");
                materialSearchLog.AppendLine("Note: Make sure the GameObject has an Animator component with a Humanoid Avatar.");
                Debug.LogWarning("シーン上にHumanoidが見つかりませんでした。");
            }
            else
            {
                // 各Humanoidを診断
                for (int i = 0; i < humanoids.Count; i++)
                {
                    GameObject humanoid = humanoids[i];
                    materialSearchLog.AppendLine($"─────────────────────────────────────");
                    materialSearchLog.AppendLine($"Humanoid #{i + 1}: {humanoid.name}");
                    materialSearchLog.AppendLine($"Path: {GetGameObjectPath(humanoid)}");
                    materialSearchLog.AppendLine($"─────────────────────────────────────");
                    materialSearchLog.AppendLine();

                    // メッシュ診断
                    DiagnoseMeshStateWithLabel(humanoid, $"Humanoid #{i + 1}: {humanoid.name}");
                }
            }

            // ログを自動保存
            SaveLogToFile();

            Debug.Log($"[RuntimeMaterialManager] AnalyzeSceneHumanoids() 完了 - {humanoids.Count}体のHumanoidを分析");
#endif
        }

        /// <summary>
        /// GameObjectの階層パスを取得
        /// </summary>
        private string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform current = obj.transform.parent;
            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        public RuntimeMaterialManager()
        {
            // MaterialCacheDatabaseをロード (オプション)
            materialCacheDatabase = Resources.Load<MaterialCacheDatabase>("MaterialCacheDatabase");
            if (materialCacheDatabase == null)
            {
                Debug.LogWarning("[RuntimeMaterialManager] MaterialCacheDatabaseが見つかりません。Runtime検索モードで動作します。");
            }
        }

        /// <summary>
        /// 指定されたGameObjectにキャッシュされたMaterialを適用
        /// </summary>
        /// <param name="gameObject">対象のGameObject</param>
        /// <param name="extractedPath">FBXファイルのパス</param>
        /// <param name="meshNodeToMaterialNames">MeshNode名とMaterial名のマッピング</param>
        public async UniTask AssignMaterials(GameObject gameObject, string extractedPath, Dictionary<string, List<string>> meshNodeToMaterialNames = null)
        {
            Debug.Log($"[RuntimeMaterialManager] AssignMaterials() 開始 - GameObject: {gameObject?.name ?? "null"}");

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            materialSearchLog.Clear();
            materialSearchLog.AppendLine("=== Material Search Log ===");
            materialSearchLog.AppendLine($"GameObject: {gameObject.name}");
            materialSearchLog.AppendLine($"ExtractedPath: {extractedPath}");
            materialSearchLog.AppendLine($"Time: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            materialSearchLog.AppendLine();

            // 解凍フォルダの全ファイル構造をログに記録
            LogDirectoryStructure(extractedPath);
#endif

            // MaterialCacheDatabaseがnullの場合は警告のみ出して続行
            if (materialCacheDatabase == null)
            {
                Debug.LogWarning("[RuntimeMaterialManager] MaterialCacheDatabaseなしでRuntime検索モードで動作します。");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine("WARNING: MaterialCacheDatabaseがロードされていません。Runtime検索モードで動作します。");
#endif
            }

            var skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (skinnedMeshRenderers.Length == 0)
            {
                Debug.LogWarning("SkinnedMeshRendererが見つかりません。");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine("WARNING: SkinnedMeshRendererが見つかりません。");
#endif
                return;
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            materialSearchLog.AppendLine($"Total SkinnedMeshRenderers: {skinnedMeshRenderers.Length}");
            materialSearchLog.AppendLine();
#endif

            foreach (var renderer in skinnedMeshRenderers)
            {
                // MeshNode名を基にマテリアルを取得
                var meshNodeName = renderer.gameObject.name;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine($"--- Searching for: {meshNodeName} ---");
#endif

                var materials = await GetMaterialsForMeshNode(gameObject.name, meshNodeName, extractedPath, meshNodeToMaterialNames);

                if (materials != null && materials.Count > 0)
                {
                    renderer.materials = materials.ToArray();
                    Debug.Log($"Materialが適用されました: {meshNodeName} -> {materials.Count} 個");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    materialSearchLog.AppendLine($"✓ SUCCESS: {materials.Count} material(s) applied");
                    foreach (var mat in materials)
                    {
                        materialSearchLog.AppendLine($"  - {mat.name} (Shader: {mat.shader.name})");
                    }
#endif
                }
                else
                {
                    Debug.LogWarning($"Materialが見つかりません: {meshNodeName}");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    materialSearchLog.AppendLine($"✗ FAILED: No material found");
#endif
                }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine();
#endif

                await UniTask.Yield();
            }

            // メモリを解放
            System.GC.Collect();
            await Resources.UnloadUnusedAssets();

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            // メッシュ診断ログを生成
            DiagnoseMeshState(gameObject);

            // ログを自動的にファイルに保存（エディタ/ビルド版を区別）
            SaveLogToFile();

            Debug.Log("[RuntimeMaterialManager] AssignMaterials() 完了");
#else
            Debug.Log("[RuntimeMaterialManager] AssignMaterials() 完了 (ログ機能は無効)");
#endif
        }

        /// <summary>
        /// 指定されたFBX名とMeshNode名に対応するMaterialリストを取得
        /// </summary>
        /// <param name="fbxName">FBX名</param>
        /// <param name="meshNodeName">MeshNode名</param>
        /// <param name="extractedPath">FBXファイルのパス</param>
        /// <param name="meshNodeToMaterialNames">MeshNode名とMaterial名のマッピング</param>
        /// <returns>取得したMaterialのリスト</returns>
        private async UniTask<List<Material>> GetMaterialsForMeshNode(string fbxName, string meshNodeName, string extractedPath, Dictionary<string, List<string>> meshNodeToMaterialNames)
        {
            var materials = new List<Material>();

            // MaterialCacheDatabaseがない場合は直接テクスチャ検索へ
            if (materialCacheDatabase == null)
            {
                Debug.Log($"[RuntimeMaterialManager] CacheDatabaseなし。{meshNodeName}のテクスチャを直接検索します。");
                materialSearchLog.AppendLine($"[Runtime Mode] No cache database, searching textures directly for: {meshNodeName}");

                // 戦略3のみ実行: 親ディレクトリからテクスチャを直接検索
                var runtimeSearchNames = new List<string>();
                if (meshNodeToMaterialNames != null && meshNodeToMaterialNames.TryGetValue(meshNodeName, out List<string> runtimeMatNames))
                {
                    runtimeSearchNames.AddRange(runtimeMatNames);
                }
                runtimeSearchNames.Add(meshNodeName);

                materials = await SearchTexturesInParentDirectory(extractedPath, runtimeSearchNames);
                return materials;
            }

            // FBXエントリを検索
            var fbxEntry = materialCacheDatabase.mappings.Find(m => m.fbxName == fbxName);
            if (fbxEntry == null)
            {
                Debug.LogWarning($"FBXエントリが見つかりません: {fbxName}。Runtime検索に切り替えます。");
                materialSearchLog.AppendLine($"[Fallback] FBX entry not found: {fbxName}, using runtime search");

                // FBXエントリがない場合も戦略3にフォールバック
                var fallbackSearchNames = new List<string>();
                if (meshNodeToMaterialNames != null && meshNodeToMaterialNames.TryGetValue(meshNodeName, out List<string> fallbackMatNames))
                {
                    fallbackSearchNames.AddRange(fallbackMatNames);
                }
                fallbackSearchNames.Add(meshNodeName);

                materials = await SearchTexturesInParentDirectory(extractedPath, fallbackSearchNames);
                return materials;
            }

            // 検索戦略1: AssimpのMaterial名で検索
            if (meshNodeToMaterialNames != null && meshNodeToMaterialNames.TryGetValue(meshNodeName, out List<string> assimpMaterialNames))
            {
                Debug.Log($"[戦略1] AssimpのMaterial名で検索: {meshNodeName}");
                materialSearchLog.AppendLine($"[Strategy 1] Searching by Assimp Material Names:");
                materialSearchLog.AppendLine($"  Assimp Materials: {string.Join(", ", assimpMaterialNames)}");

                var foundMaterialNames = new HashSet<string>(); // 重複防止

                foreach (var assimpMatName in assimpMaterialNames)
                {
                    if (foundMaterialNames.Contains(assimpMatName))
                        continue; // 既に処理済み

                    bool found = false;
                    foreach (var entry in fbxEntry.materialEntries)
                    {
                        if (entry.materialName == assimpMatName && entry.meshNodeName == meshNodeName)
                        {
                            var material = await CreateMaterialFromEntry(entry, extractedPath);
                            if (material != null)
                            {
                                materials.Add(material);
                                foundMaterialNames.Add(assimpMatName);
                                Debug.Log($"  Material見つかりました: {assimpMatName}");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                                materialSearchLog.AppendLine($"  ✓ Found: {assimpMatName}");
#endif
                                found = true;
                            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                            else
                            {
                                materialSearchLog.AppendLine($"  ✗ Failed to create: {assimpMatName} (Shader: {entry.shaderName})");
                            }
#endif
                            break; // 同じMaterial名の最初のentryのみ使用
                        }
                    }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    if (!found)
                    {
                        materialSearchLog.AppendLine($"  ✗ Not found in cache: {assimpMatName}");
                    }
#endif
                }

                if (materials.Count > 0)
                {
                    return materials;
                }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine($"  Result: No materials found in strategy 1");
#endif
            }
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            else
            {
                materialSearchLog.AppendLine($"[Strategy 1] Skipped: No Assimp material mapping for this node");
            }
#endif

            // 検索戦略2: GameObject名(MeshNode名)で検索
            Debug.Log($"[戦略2] GameObject名で検索: {meshNodeName}");
            materialSearchLog.AppendLine($"[Strategy 2] Searching by GameObject Name:");
            materialSearchLog.AppendLine($"  MeshNode Name: {meshNodeName}");

            int foundCount = 0;
            foreach (var entry in fbxEntry.materialEntries)
            {
                if (entry.meshNodeName == meshNodeName)
                {
                    var material = await CreateMaterialFromEntry(entry, extractedPath);
                    if (material != null)
                    {
                        materials.Add(material);
                        Debug.Log($"  Material見つかりました: {entry.materialName}");
                        materialSearchLog.AppendLine($"  ✓ Found: {entry.materialName}");
                        foundCount++;
                    }
                    else
                    {
                        materialSearchLog.AppendLine($"  ✗ Failed to create: {entry.materialName} (Shader: {entry.shaderName})");
                    }
                }
            }

            if (foundCount == 0)
            {
                materialSearchLog.AppendLine($"  ✗ No materials found for this node name");
            }
            materialSearchLog.AppendLine($"  Result: {foundCount} material(s) found in strategy 2");

            if (materials.Count > 0)
            {
                return materials;
            }

            // 検索戦略3: 親ディレクトリからテクスチャを直接検索
            Debug.Log($"[戦略3] 親ディレクトリからテクスチャを検索: {meshNodeName}");
            materialSearchLog.AppendLine($"[Strategy 3] Searching in Parent Directory:");
            var searchNames = new List<string>();

            // AssimpのMaterial名がある場合は追加
            if (meshNodeToMaterialNames != null && meshNodeToMaterialNames.TryGetValue(meshNodeName, out List<string> matNames))
            {
                searchNames.AddRange(matNames);
            }

            // GameObject名も追加
            searchNames.Add(meshNodeName);

            materialSearchLog.AppendLine($"  Search Names: {string.Join(", ", searchNames)}");
            materialSearchLog.AppendLine($"  Search Path: {Path.GetDirectoryName(extractedPath)}");

            materials = await SearchTexturesInParentDirectory(extractedPath, searchNames);

            if (materials.Count > 0)
            {
                materialSearchLog.AppendLine($"  ✓ Found {materials.Count} material(s) from textures");
            }
            else
            {
                materialSearchLog.AppendLine($"  ✗ No textures found in parent directory");
            }

            return materials;
        }

        /// <summary>
        /// 親ディレクトリからテクスチャファイルを検索してMaterialを作成
        /// UniSIL統合: .matファイルがあればYAMLパースとShader推論を使用
        /// </summary>
        /// <param name="extractedPath">FBXファイルのパス</param>
        /// <param name="searchNames">検索するファイル名のリスト</param>
        /// <returns>作成されたMaterialのリスト</returns>
        private async UniTask<List<Material>> SearchTexturesInParentDirectory(string extractedPath, List<string> searchNames)
        {
            var materials = new List<Material>();

            // FBXの親ディレクトリを取得（例: /path/to/FBX/）
            string fbxDir = Path.GetDirectoryName(extractedPath);
            if (string.IsNullOrEmpty(fbxDir) || !Directory.Exists(fbxDir))
            {
                Debug.LogWarning($"FBXディレクトリが見つかりません: {extractedPath}");
                materialSearchLog.AppendLine($"    ERROR: FBX directory not found: {extractedPath}");
                return materials;
            }

            // FBXの親の親ディレクトリを取得（例: /path/to/Assets/Kyoko/）
            string assetDir = Path.GetDirectoryName(fbxDir);
            if (string.IsNullOrEmpty(assetDir) || !Directory.Exists(assetDir))
            {
                Debug.LogWarning($"アセットディレクトリが見つかりません: {fbxDir}");
                materialSearchLog.AppendLine($"    ERROR: Asset directory not found: {fbxDir}");
                return materials;
            }

            Debug.Log($"[RuntimeMaterialManager] FBX directory: {fbxDir}");
            Debug.Log($"[RuntimeMaterialManager] Asset directory: {assetDir}");
            materialSearchLog.AppendLine($"    FBX directory: {fbxDir}");
            materialSearchLog.AppendLine($"    Asset directory: {assetDir}");

            // 検索対象ディレクトリ（優先順位順）
            string[] searchDirectories = new string[]
            {
                Path.Combine(assetDir, "Material"),   // ../Material/
                Path.Combine(assetDir, "Materials"),  // ../Materials/
                fbxDir,                                // FBXと同じディレクトリ
                assetDir                               // アセットルート
            };

            foreach (var searchName in searchNames)
            {
                // 戦略1: .matファイルを複数のディレクトリから探してUniSILで再構築
                foreach (var searchDir in searchDirectories)
                {
                    if (!Directory.Exists(searchDir))
                        continue;

                    string matPath = Path.Combine(searchDir, searchName + ".mat");
                    if (File.Exists(matPath))
                    {
                        Debug.Log($"[UniSIL] .mat file found: {matPath}");
                        materialSearchLog.AppendLine($"    [UniSIL] Found .mat file: {searchName}.mat in {Path.GetFileName(searchDir)}/");

                        var material = await CreateMaterialFromMatFile(matPath, assetDir);
                        if (material != null)
                        {
                            materials.Add(material);
                            materialSearchLog.AppendLine($"    [UniSIL] Material reconstructed successfully");
                            return materials;
                        }
                        else
                        {
                            materialSearchLog.AppendLine($"    [UniSIL] Failed to reconstruct material from .mat");
                        }
                    }
                }
            }

            // 戦略2（フォールバック）: テクスチャファイルのみからStandardシェーダーで作成
            Debug.Log($"[RuntimeMaterialManager] No .mat files found, falling back to texture-only search");
            materialSearchLog.AppendLine($"    [Fallback] No .mat files found, searching for texture files only");

            // テクスチャ検索用ディレクトリ（Material/, Texture/, Textures/ なども探す）
            string[] textureSearchDirs = new string[]
            {
                Path.Combine(assetDir, "Texture"),
                Path.Combine(assetDir, "Textures"),
                Path.Combine(assetDir, "Material"),
                Path.Combine(assetDir, "Materials"),
                fbxDir,
                assetDir
            };

            string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp" };
            foreach (var searchName in searchNames)
            {
                foreach (var searchDir in textureSearchDirs)
                {
                    if (!Directory.Exists(searchDir))
                        continue;

                    foreach (var ext in imageExtensions)
                    {
                        string texturePath = Path.Combine(searchDir, searchName + ext);
                        if (File.Exists(texturePath))
                        {
                            Debug.Log($"  テクスチャ見つかりました: {texturePath}");
                            materialSearchLog.AppendLine($"    Found texture: {searchName}{ext} in {Path.GetFileName(searchDir)}/");
                            var material = await CreateMaterialFromTexturePath(searchName, texturePath);
                            if (material != null)
                            {
                                materials.Add(material);
                                materialSearchLog.AppendLine($"    Material created successfully from texture");
                                return materials;
                            }
                            else
                            {
                                materialSearchLog.AppendLine($"    Failed to create material from texture");
                            }
                        }
                    }
                }
            }

            return materials;
        }

        /// <summary>
        /// .matファイルからUniSILを使用してMaterialを再構築
        /// </summary>
        private async UniTask<Material> CreateMaterialFromMatFile(string matPath, string textureDirectory)
        {
            try
            {
                // .matファイルを読み込み
                string yamlText = await File.ReadAllTextAsync(matPath);

                // YAMLパースしてMaterialDataに変換
                var materialData = YAMLMaterialParser.Parse(yamlText);
                if (materialData == null || !materialData.IsValid())
                {
                    Debug.LogWarning($"[UniSIL] Failed to parse .mat file: {matPath}");
                    materialSearchLog.AppendLine($"      ERROR: Failed to parse .mat file");
                    return null;
                }

                Debug.Log($"[UniSIL] Parsed material: {materialData.name}");
                materialSearchLog.AppendLine($"      Parsed material: {materialData.name}");
                materialSearchLog.AppendLine($"      Shader GUID: {materialData.shaderGuid}");
                materialSearchLog.AppendLine($"      Keywords: {string.Join(", ", materialData.keywords)}");

                // ShaderDatabaseをロード
                var shaderDB = ShaderDBLoader.LoadDatabase();
                if (shaderDB == null)
                {
                    Debug.LogError("[UniSIL] Failed to load ShaderDatabase");
                    materialSearchLog.AppendLine($"      ERROR: ShaderDatabase not found");
                    return null;
                }

                // ShaderInferenceEngineで推論
                var inferenceEngine = new ShaderInferenceEngine(shaderDB);
                var inferenceResult = inferenceEngine.InferShader(materialData);

                Debug.Log($"[UniSIL] Inferred shader: {inferenceResult.inferredShader} (confidence: {inferenceResult.confidence:P2})");
                materialSearchLog.AppendLine($"      Inferred shader: {inferenceResult.inferredShader}");
                materialSearchLog.AppendLine($"      Confidence: {inferenceResult.confidence:P2}");

                // MaterialReconstructorで再構築
                var reconstructor = new MaterialReconstructor();
                var material = reconstructor.ReconstructMaterial(materialData, inferenceResult);

                if (material != null)
                {
                    Debug.Log($"[UniSIL] Material reconstructed: {material.name} with shader {material.shader.name}");
                    materialSearchLog.AppendLine($"      ✓ Material reconstructed successfully");
                    materialSearchLog.AppendLine($"      Final shader: {material.shader.name}");
                }

                await UniTask.Yield();
                return material;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UniSIL] Error reconstructing material from {matPath}: {ex.Message}");
                materialSearchLog.AppendLine($"      ERROR: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// テクスチャパスからMaterialを作成
        /// </summary>
        /// <param name="materialName">Material名</param>
        /// <param name="texturePath">テクスチャのパス</param>
        /// <returns>作成されたMaterial</returns>
        private async UniTask<Material> CreateMaterialFromTexturePath(string materialName, string texturePath)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                Debug.LogWarning("Standardシェーダーが見つかりません。");
                return null;
            }

            var material = new Material(shader)
            {
                name = materialName
            };

            if (File.Exists(texturePath))
            {
                byte[] fileData = null;
                try
                {
                    fileData = await File.ReadAllBytesAsync(texturePath);
                    var texture = new Texture2D(2, 2, TextureFormat.BGRA32, false);
                    if (texture.LoadImage(fileData))
                    {
                        texture.Compress(true);
                        material.mainTexture = texture;
                        Debug.Log($"  テクスチャをロードしました: {texturePath}");
                    }
                    else
                    {
                        Debug.LogWarning($"  テクスチャのロードに失敗しました: {texturePath}");
                        UnityEngine.Object.Destroy(texture);
                    }
                }
                finally
                {
                    fileData = null; // バイト配列の参照を解放
                }
            }

            return material;
        }

        private int matCount = 0;

        /// <summary>
        /// キャッシュデータを元にMaterialを作成
        /// </summary>
        /// <param name="entry">Materialデータのエントリ</param>
        /// <returns>生成されたMaterial</returns>
        private async UniTask<Material> CreateMaterialFromEntry(MaterialCacheDatabase.MaterialMapping.MaterialEntry entry, string extractedPath)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            materialSearchLog.AppendLine($"    [CreateMaterialFromEntry] Material: {entry.materialName}");
            materialSearchLog.AppendLine($"      Requested Shader: {entry.shaderName}");
#endif

            Shader shader = Shader.Find(entry.shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"Shaderが見つかりません: {entry.shaderName}。フォールバックマテリアルを試みます。");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine($"      ✗ Shader NOT FOUND: {entry.shaderName}");
                materialSearchLog.AppendLine($"      Reason: Shader.Find() returned null");
                materialSearchLog.AppendLine($"      Trying fallback material from Resources...");
#endif

                // Resourcesからフォールバックマテリアルをロード
                Material fallbackMaterial = LoadFallbackMaterial(entry.shaderName);
                if (fallbackMaterial != null)
                {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    materialSearchLog.AppendLine($"      ✓ Fallback material loaded: {fallbackMaterial.name}");
#endif
                    shader = fallbackMaterial.shader;
                }
                else
                {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    materialSearchLog.AppendLine($"      ✗ Fallback material not found");
#endif
                    return null;
                }
            }

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            materialSearchLog.AppendLine($"      ✓ Shader found: {shader.name}");
#endif

            var material = new Material(shader)
            {
                name = entry.materialName
            };

            // メインカラーを適用
            material.color = entry.mainColor;

            var texturePath = Path.Combine(extractedPath, entry.texturePath);

            Debug.Log($"TexturePath:{texturePath}");

#if DEVELOPMENT_BUILD || UNITY_EDITOR
            materialSearchLog.AppendLine($"      Main Color: {entry.mainColor}");
            materialSearchLog.AppendLine($"      Texture Path (cache): {entry.texturePath}");
            materialSearchLog.AppendLine($"      Texture Path (full): {texturePath}");
#endif

            // テクスチャを適用
            if (string.IsNullOrEmpty(entry.texturePath))
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine($"      Texture: None specified in cache");
#endif
            }
            else if (!File.Exists(texturePath))
            {
                Debug.LogWarning($"テクスチャファイルが存在しません: {texturePath}");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine($"      ✗ Texture file NOT FOUND: {texturePath}");
                materialSearchLog.AppendLine($"      ExtractedPath: {extractedPath}");
#endif
            }
            else
            {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine($"      ✓ Texture file exists: {Path.GetFileName(texturePath)}");
#endif
                byte[] fileData = null;
                try
                {
                    fileData = await File.ReadAllBytesAsync(texturePath);
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    materialSearchLog.AppendLine($"      File size: {fileData.Length} bytes");
#endif

                    var texture = new Texture2D(2, 2, TextureFormat.BGRA32, false);
                    if (texture.LoadImage(fileData))
                    {
                        texture.Compress(true);
                        material.mainTexture = texture;
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        materialSearchLog.AppendLine($"      ✓ Texture loaded successfully ({texture.width}x{texture.height})");
#endif
                    }
                    else
                    {
                        Debug.LogWarning($"テクスチャのロードに失敗しました: {texturePath}");
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                        materialSearchLog.AppendLine($"      ✗ Texture.LoadImage() failed");
#endif
                        UnityEngine.Object.Destroy(texture);
                    }
                }
                catch (Exception ex)
                {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                    materialSearchLog.AppendLine($"      ✗ Exception while loading texture: {ex.Message}");
#endif
                }
                finally
                {
                    fileData = null; // バイト配列の参照を解放
                }
            }

            matCount++;
            Debug.Log($"MaterialCount:{matCount}");
            return material;
        }

        /// <summary>
        /// Resourcesからフォールバックマテリアルをロード
        /// </summary>
        /// <param name="shaderName">シェーダー名</param>
        /// <returns>ロードされたマテリアル、見つからない場合はnull</returns>
        private Material LoadFallbackMaterial(string shaderName)
        {
            // シェーダー名をResourcesパスに変換
            // 例: "Hidden/lilToonOutline" → "GeneratedMaterials/Empty_Hidden_lilToonOutline"
            //     "lilToon" → "GeneratedMaterials/Empty_lilToon"

            string materialName = shaderName.Replace("/", "_");
            string resourcePath = $"GeneratedMaterials/Empty_{materialName}";

            Material fallbackMaterial = Resources.Load<Material>(resourcePath);

            if (fallbackMaterial == null)
            {
                Debug.LogWarning($"フォールバックマテリアルが見つかりません: {resourcePath}");
            }

            return fallbackMaterial;
        }

        /// <summary>
        /// 解凍フォルダの全ファイル構造をログに記録
        /// </summary>
        /// <param name="extractedPath">FBXファイルのパス</param>
        private void LogDirectoryStructure(string extractedPath)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            materialSearchLog.AppendLine("=== Directory Structure ===");

            // extractedPathの親ディレクトリを取得
            string parentDir = Path.GetDirectoryName(extractedPath);
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            {
                materialSearchLog.AppendLine($"Parent directory not found: {extractedPath}");
                materialSearchLog.AppendLine();
                return;
            }

            materialSearchLog.AppendLine($"Parent Directory: {parentDir}");
            materialSearchLog.AppendLine();

            // 画像拡張子リスト
            string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".psd", ".tiff", ".gif" };

            try
            {
                // 親ディレクトリ内の全ファイルを取得
                var allFiles = Directory.GetFiles(parentDir, "*", SearchOption.AllDirectories);

                materialSearchLog.AppendLine($"Total Files Found: {allFiles.Length}");
                materialSearchLog.AppendLine();

                // 画像ファイルのみをフィルタ
                var imageFiles = allFiles.Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return imageExtensions.Contains(ext);
                }).ToList();

                materialSearchLog.AppendLine($"Image Files ({imageFiles.Count}):");
                if (imageFiles.Count > 0)
                {
                    foreach (var file in imageFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        var relativePath = file.Replace(parentDir, "").TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        var sizeKB = fileInfo.Length / 1024.0;
                        materialSearchLog.AppendLine($"  📷 {relativePath} ({sizeKB:F1} KB)");
                    }
                }
                else
                {
                    materialSearchLog.AppendLine($"  (No image files found)");
                }
                materialSearchLog.AppendLine();

                // FBXファイルを探す
                var fbxFiles = allFiles.Where(f => Path.GetExtension(f).ToLowerInvariant() == ".fbx").ToList();
                materialSearchLog.AppendLine($"FBX Files ({fbxFiles.Count}):");
                if (fbxFiles.Count > 0)
                {
                    foreach (var file in fbxFiles)
                    {
                        var relativePath = file.Replace(parentDir, "").TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        materialSearchLog.AppendLine($"  🎨 {relativePath}");
                    }
                }
                materialSearchLog.AppendLine();

                // その他の主要ファイル（マテリアル関連）
                var matFiles = allFiles.Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    return ext == ".mat" || ext == ".shader" || ext == ".cginc";
                }).ToList();

                if (matFiles.Count > 0)
                {
                    materialSearchLog.AppendLine($"Material/Shader Files ({matFiles.Count}):");
                    foreach (var file in matFiles)
                    {
                        var relativePath = file.Replace(parentDir, "").TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                        materialSearchLog.AppendLine($"  🎭 {relativePath}");
                    }
                    materialSearchLog.AppendLine();
                }

                // ディレクトリ構造の概要
                var directories = Directory.GetDirectories(parentDir, "*", SearchOption.AllDirectories);
                materialSearchLog.AppendLine($"Directory Structure ({directories.Length} subdirectories):");

                // 最上位のディレクトリのみ表示
                var topLevelDirs = Directory.GetDirectories(parentDir);
                foreach (var dir in topLevelDirs)
                {
                    var dirName = Path.GetFileName(dir);
                    var filesInDir = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length;
                    materialSearchLog.AppendLine($"  📁 {dirName}/ ({filesInDir} files)");
                }
                materialSearchLog.AppendLine();
            }
            catch (Exception ex)
            {
                materialSearchLog.AppendLine($"Error reading directory structure: {ex.Message}");
                materialSearchLog.AppendLine();
            }
#endif
        }

        /// <summary>
        /// メッシュの状態を診断してログに記録（初回のみ）
        /// </summary>
        /// <param name="gameObject">診断対象のGameObject</param>
        private void DiagnoseMeshState(GameObject gameObject)
        {
            meshDiagnosticsLog.Clear();
            meshDiagnosticsLog.AppendLine("=== Mesh Diagnostics Log ===");
            meshDiagnosticsLog.AppendLine($"GameObject: {gameObject.name}");
            meshDiagnosticsLog.AppendLine();

            DiagnoseMeshStateInternal(gameObject);
        }

        /// <summary>
        /// メッシュの状態を診断してログに記録（内部実装）
        /// </summary>
        /// <param name="gameObject">診断対象のGameObject</param>
        private void DiagnoseMeshStateInternal(GameObject gameObject)
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log($"[RuntimeMaterialManager] DiagnoseMeshStateInternal() 開始 - GameObject: {gameObject?.name ?? "null"}");

            var skinnedMeshRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            Debug.Log($"[RuntimeMaterialManager] Found {skinnedMeshRenderers.Length} SkinnedMeshRenderers");

            meshDiagnosticsLog.AppendLine($"Total SkinnedMeshRenderers: {skinnedMeshRenderers.Length}");
            meshDiagnosticsLog.AppendLine();

            int rendererIndex = 0;
            foreach (var renderer in skinnedMeshRenderers)
            {
                rendererIndex++;
                meshDiagnosticsLog.AppendLine($"--- SkinnedMeshRenderer #{rendererIndex}: {renderer.gameObject.name} ---");

                // Mesh基本情報
                var mesh = renderer.sharedMesh;
                if (mesh == null)
                {
                    meshDiagnosticsLog.AppendLine("  ERROR: SharedMesh is null!");
                    meshDiagnosticsLog.AppendLine();
                    continue;
                }

                meshDiagnosticsLog.AppendLine($"  Mesh Name: {mesh.name}");
                meshDiagnosticsLog.AppendLine($"  Vertex Count: {mesh.vertexCount}");
                meshDiagnosticsLog.AppendLine($"  Triangle Count: {mesh.triangles.Length / 3}");
                meshDiagnosticsLog.AppendLine($"  SubMesh Count: {mesh.subMeshCount}");

                // Bones情報
                meshDiagnosticsLog.AppendLine($"  Bones: {renderer.bones?.Length ?? 0}");
                if (renderer.rootBone != null)
                {
                    meshDiagnosticsLog.AppendLine($"  Root Bone: {renderer.rootBone.name}");
                }
                else
                {
                    meshDiagnosticsLog.AppendLine($"  Root Bone: NULL (WARNING!)");
                }

                // BindPoses情報
                var bindposes = mesh.bindposes;
                meshDiagnosticsLog.AppendLine($"  BindPoses: {bindposes?.Length ?? 0}");
                if (renderer.bones != null && bindposes != null && renderer.bones.Length != bindposes.Length)
                {
                    meshDiagnosticsLog.AppendLine($"  ERROR: Bone count ({renderer.bones.Length}) != BindPose count ({bindposes.Length})");
                }

                // BoneWeights検証
                var boneWeights = mesh.boneWeights;
                meshDiagnosticsLog.AppendLine($"  BoneWeights: {boneWeights?.Length ?? 0}");

                if (boneWeights != null && boneWeights.Length > 0)
                {
                    int invalidWeightCount = 0;
                    int zeroWeightCount = 0;
                    int invalidIndexCount = 0;
                    float minWeightSum = float.MaxValue;
                    float maxWeightSum = float.MinValue;

                    // ボーンごとの影響を受ける頂点数
                    int maxBoneIndex = renderer.bones?.Length ?? 0;
                    int[] boneInfluenceCounts = new int[maxBoneIndex];
                    float[] boneTotalWeights = new float[maxBoneIndex];

                    // 頂点ごとのボーン数分布（1ボーン、2ボーン、3ボーン、4ボーン）
                    int[] boneCountDistribution = new int[5]; // 0, 1, 2, 3, 4 bones

                    // 問題のある頂点のサンプル（最初の5個まで）
                    System.Collections.Generic.List<string> problemVertexSamples = new System.Collections.Generic.List<string>();

                    for (int i = 0; i < boneWeights.Length; i++)
                    {
                        var bw = boneWeights[i];
                        float weightSum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;

                        // ウエイトサムの検証
                        if (weightSum < 0.0001f)
                        {
                            zeroWeightCount++;
                            if (problemVertexSamples.Count < 5)
                            {
                                problemVertexSamples.Add($"      Vertex[{i}]: Zero weights");
                            }
                        }
                        else if (Mathf.Abs(weightSum - 1.0f) > 0.01f)
                        {
                            invalidWeightCount++;
                            if (problemVertexSamples.Count < 5)
                            {
                                problemVertexSamples.Add($"      Vertex[{i}]: Invalid weight sum = {weightSum:F4}");
                            }
                        }

                        minWeightSum = Mathf.Min(minWeightSum, weightSum);
                        maxWeightSum = Mathf.Max(maxWeightSum, weightSum);

                        // 頂点が使用しているボーン数をカウント
                        int activeBoneCount = 0;
                        if (bw.weight0 > 0.0001f) activeBoneCount++;
                        if (bw.weight1 > 0.0001f) activeBoneCount++;
                        if (bw.weight2 > 0.0001f) activeBoneCount++;
                        if (bw.weight3 > 0.0001f) activeBoneCount++;
                        boneCountDistribution[activeBoneCount]++;

                        // ボーンインデックスの妥当性チェック & ボーン別統計
                        if (bw.weight0 > 0)
                        {
                            if (bw.boneIndex0 >= maxBoneIndex)
                            {
                                invalidIndexCount++;
                                if (problemVertexSamples.Count < 5)
                                {
                                    problemVertexSamples.Add($"      Vertex[{i}]: Invalid boneIndex0={bw.boneIndex0} (max={maxBoneIndex})");
                                }
                            }
                            else
                            {
                                boneInfluenceCounts[bw.boneIndex0]++;
                                boneTotalWeights[bw.boneIndex0] += bw.weight0;
                            }
                        }
                        if (bw.weight1 > 0)
                        {
                            if (bw.boneIndex1 >= maxBoneIndex)
                            {
                                invalidIndexCount++;
                            }
                            else
                            {
                                boneInfluenceCounts[bw.boneIndex1]++;
                                boneTotalWeights[bw.boneIndex1] += bw.weight1;
                            }
                        }
                        if (bw.weight2 > 0)
                        {
                            if (bw.boneIndex2 >= maxBoneIndex)
                            {
                                invalidIndexCount++;
                            }
                            else
                            {
                                boneInfluenceCounts[bw.boneIndex2]++;
                                boneTotalWeights[bw.boneIndex2] += bw.weight2;
                            }
                        }
                        if (bw.weight3 > 0)
                        {
                            if (bw.boneIndex3 >= maxBoneIndex)
                            {
                                invalidIndexCount++;
                            }
                            else
                            {
                                boneInfluenceCounts[bw.boneIndex3]++;
                                boneTotalWeights[bw.boneIndex3] += bw.weight3;
                            }
                        }
                    }

                    meshDiagnosticsLog.AppendLine($"  BoneWeight Validation:");
                    meshDiagnosticsLog.AppendLine($"    Weight Sum Range: {minWeightSum:F4} - {maxWeightSum:F4}");
                    meshDiagnosticsLog.AppendLine($"    Zero Weight Vertices: {zeroWeightCount} / {boneWeights.Length}");
                    meshDiagnosticsLog.AppendLine($"    Invalid Weight Sum (!= 1.0): {invalidWeightCount} / {boneWeights.Length}");
                    meshDiagnosticsLog.AppendLine($"    Invalid Bone Indices: {invalidIndexCount}");

                    // ボーン数分布
                    meshDiagnosticsLog.AppendLine($"    Bone Count Distribution:");
                    meshDiagnosticsLog.AppendLine($"      0 bones: {boneCountDistribution[0]} vertices ({(boneCountDistribution[0] * 100f / boneWeights.Length):F1}%)");
                    meshDiagnosticsLog.AppendLine($"      1 bone:  {boneCountDistribution[1]} vertices ({(boneCountDistribution[1] * 100f / boneWeights.Length):F1}%)");
                    meshDiagnosticsLog.AppendLine($"      2 bones: {boneCountDistribution[2]} vertices ({(boneCountDistribution[2] * 100f / boneWeights.Length):F1}%)");
                    meshDiagnosticsLog.AppendLine($"      3 bones: {boneCountDistribution[3]} vertices ({(boneCountDistribution[3] * 100f / boneWeights.Length):F1}%)");
                    meshDiagnosticsLog.AppendLine($"      4 bones: {boneCountDistribution[4]} vertices ({(boneCountDistribution[4] * 100f / boneWeights.Length):F1}%)");

                    // ボーン別の影響統計（上位10ボーン）
                    meshDiagnosticsLog.AppendLine($"    Bone Influence Statistics (Top 10):");
                    var boneStats = new System.Collections.Generic.List<(int index, int count, float avgWeight, string name)>();
                    for (int boneIdx = 0; boneIdx < maxBoneIndex; boneIdx++)
                    {
                        if (boneInfluenceCounts[boneIdx] > 0)
                        {
                            float avgWeight = boneTotalWeights[boneIdx] / boneInfluenceCounts[boneIdx];
                            string boneName = renderer.bones[boneIdx] != null ? renderer.bones[boneIdx].name : "NULL";
                            boneStats.Add((boneIdx, boneInfluenceCounts[boneIdx], avgWeight, boneName));
                        }
                    }
                    boneStats.Sort((a, b) => b.count.CompareTo(a.count)); // 影響頂点数でソート

                    int displayCount = Mathf.Min(10, boneStats.Count);
                    for (int i = 0; i < displayCount; i++)
                    {
                        var stat = boneStats[i];
                        meshDiagnosticsLog.AppendLine($"      [{stat.index}] {stat.name}: {stat.count} vertices (avg weight: {stat.avgWeight:F3})");
                    }

                    // 問題のある頂点のサンプル
                    if (problemVertexSamples.Count > 0)
                    {
                        meshDiagnosticsLog.AppendLine($"    Problem Vertex Samples (first {problemVertexSamples.Count}):");
                        foreach (var sample in problemVertexSamples)
                        {
                            meshDiagnosticsLog.AppendLine(sample);
                        }
                    }

                    // 警告メッセージ
                    if (zeroWeightCount > 0)
                    {
                        meshDiagnosticsLog.AppendLine($"    WARNING: {zeroWeightCount} vertices have zero weights!");
                    }
                    if (invalidWeightCount > boneWeights.Length * 0.1f)
                    {
                        meshDiagnosticsLog.AppendLine($"    WARNING: Over 10% of vertices have invalid weight sums!");
                    }
                    if (invalidIndexCount > 0)
                    {
                        meshDiagnosticsLog.AppendLine($"    ERROR: {invalidIndexCount} bone indices are out of range!");
                    }
                }
                else if (mesh.vertexCount > 0)
                {
                    meshDiagnosticsLog.AppendLine($"  ERROR: Mesh has vertices but no BoneWeights!");
                }

                // Materials情報
                var materials = renderer.sharedMaterials;
                meshDiagnosticsLog.AppendLine($"  Materials: {materials?.Length ?? 0}");
                if (materials != null)
                {
                    for (int i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != null)
                        {
                            var mat = materials[i];
                            meshDiagnosticsLog.AppendLine($"    [{i}] {mat.name}");
                            meshDiagnosticsLog.AppendLine($"        Shader: {mat.shader.name}");
                            meshDiagnosticsLog.AppendLine($"        RenderQueue: {mat.renderQueue}");

                            // メインテクスチャ
                            if (mat.mainTexture != null)
                            {
                                meshDiagnosticsLog.AppendLine($"        MainTexture: {mat.mainTexture.name} ({mat.mainTexture.width}x{mat.mainTexture.height})");
                            }
                            else
                            {
                                meshDiagnosticsLog.AppendLine($"        MainTexture: None");
                            }

                            // カラープロパティ
                            if (mat.HasProperty("_Color"))
                            {
                                Color color = mat.GetColor("_Color");
                                meshDiagnosticsLog.AppendLine($"        _Color: RGBA({color.r:F3}, {color.g:F3}, {color.b:F3}, {color.a:F3})");
                            }

                            // lilToon固有のプロパティ
                            if (mat.shader.name.Contains("lilToon"))
                            {
                                if (mat.HasProperty("_MainTex"))
                                {
                                    var mainTex = mat.GetTexture("_MainTex");
                                    if (mainTex != null && mainTex != mat.mainTexture)
                                    {
                                        meshDiagnosticsLog.AppendLine($"        _MainTex: {mainTex.name} ({mainTex.width}x{mainTex.height})");
                                    }
                                }

                                // 透明度
                                if (mat.HasProperty("_Cutoff"))
                                {
                                    float cutoff = mat.GetFloat("_Cutoff");
                                    meshDiagnosticsLog.AppendLine($"        _Cutoff: {cutoff:F3}");
                                }

                                // アウトライン
                                if (mat.HasProperty("_OutlineWidth"))
                                {
                                    float outlineWidth = mat.GetFloat("_OutlineWidth");
                                    meshDiagnosticsLog.AppendLine($"        _OutlineWidth: {outlineWidth:F3}");
                                }

                                if (mat.HasProperty("_OutlineColor"))
                                {
                                    Color outlineColor = mat.GetColor("_OutlineColor");
                                    meshDiagnosticsLog.AppendLine($"        _OutlineColor: RGBA({outlineColor.r:F3}, {outlineColor.g:F3}, {outlineColor.b:F3}, {outlineColor.a:F3})");
                                }
                            }

                            // Standard Shader固有のプロパティ
                            if (mat.shader.name == "Standard")
                            {
                                if (mat.HasProperty("_Metallic"))
                                {
                                    float metallic = mat.GetFloat("_Metallic");
                                    meshDiagnosticsLog.AppendLine($"        _Metallic: {metallic:F3}");
                                }

                                if (mat.HasProperty("_Glossiness"))
                                {
                                    float glossiness = mat.GetFloat("_Glossiness");
                                    meshDiagnosticsLog.AppendLine($"        _Glossiness: {glossiness:F3}");
                                }
                            }

                            // 全テクスチャプロパティを列挙
                            var shader = mat.shader;
                            int texCount = 0;
                            for (int texIdx = 0; texIdx < shader.GetPropertyCount(); texIdx++)
                            {
                                if (shader.GetPropertyType(texIdx) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                                {
                                    string propName = shader.GetPropertyName(texIdx);
                                    var tex = mat.GetTexture(propName);
                                    if (tex != null && propName != "_MainTex" && tex != mat.mainTexture)
                                    {
                                        if (texCount == 0)
                                        {
                                            meshDiagnosticsLog.AppendLine($"        Additional Textures:");
                                        }
                                        meshDiagnosticsLog.AppendLine($"          {propName}: {tex.name} ({tex.width}x{tex.height})");
                                        texCount++;
                                    }
                                }
                            }
                        }
                        else
                        {
                            meshDiagnosticsLog.AppendLine($"    [{i}] NULL MATERIAL (ERROR!)");
                        }
                    }
                }

                meshDiagnosticsLog.AppendLine();
            }

            // ログ出力
            Debug.Log(meshDiagnosticsLog.ToString());
#endif
        }

        /// <summary>
        /// ログを自動的にファイルに保存（エディタ/ビルド版を区別）
        /// </summary>
        private void SaveLogToFile()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            Debug.Log("[RuntimeMaterialManager] SaveLogToFile() 開始");
            try
            {
                string combinedLog = GetCombinedLog();
                if (string.IsNullOrEmpty(combinedLog))
                {
                    Debug.LogWarning("No log data to save");
                    return;
                }

                // タイムスタンプ付きファイル名
                string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

                // エディタ版とビルド版を識別するプレフィックスを追加
#if UNITY_EDITOR
                string environment = "EDITOR";
#else
                string environment = "BUILD";
#endif

                string fileName = $"FBX_Load_Log_{environment}_{timestamp}.txt";

                // プロジェクト直下に保存（Assetsの親ディレクトリ）
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string filePath = Path.Combine(projectRoot, fileName);

                // ファイルに書き込み
                File.WriteAllText(filePath, combinedLog);

                Debug.Log($"[{environment}] Log file auto-saved: {filePath}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to auto-save log: {e.Message}");
            }
#endif
        }
    }
}
