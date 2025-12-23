using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UniSIL.ShaderInference;
using UniSIL.ShaderInference.MaterialReconstruction;
using UniSIL.ShaderInference.MaterialLoading;
using UniSIL.ShaderInference.TextureLoading;

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
#if DEVELOPMENT_BUILD || UNITY_EDITOR
        private System.Text.StringBuilder materialSearchLog = new System.Text.StringBuilder();
        private System.Text.StringBuilder meshDiagnosticsLog = new System.Text.StringBuilder();
#endif

        // UniSIL Manifest support
        private Dictionary<string, MaterialManifest> loadedMaterialManifests = new Dictionary<string, MaterialManifest>();
        private Dictionary<string, TextureManifest> loadedTextureManifests = new Dictionary<string, TextureManifest>();

        // シェーダー固定モード: 全マテリアルにlilToonシェーダーを使用
        public bool UseLilToonShaderOnly { get; set; } = false;

        // 固定シェーダーのキャッシュ
        private Shader _fixedLilToonShader;

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

        public string GetMaterialSearchLog()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            return materialSearchLog.ToString();
#else
            return string.Empty;
#endif
        }

        public string GetMeshDiagnosticsLog()
        {
#if DEVELOPMENT_BUILD || UNITY_EDITOR
            return meshDiagnosticsLog.ToString();
#else
            return string.Empty;
#endif
        }
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
        /// lilToonシェーダーを取得（キャッシュ付き）
        /// </summary>
        private Shader GetLilToonShader()
        {
            if (_fixedLilToonShader != null) return _fixedLilToonShader;

            // lilToon公開シェーダーのみを使用（Hidden系は使用しない）
            _fixedLilToonShader = Shader.Find("lilToon");
            if (_fixedLilToonShader == null)
            {
                _fixedLilToonShader = Shader.Find("Universal Render Pipeline/Lit");
                Debug.LogWarning("[RuntimeMaterialManager] lilToonシェーダーが見つかりません。URP/Litを使用します。");
            }

            return _fixedLilToonShader;
        }

#if UNITY_EDITOR
        // ロードしたマテリアルを保存するためのリスト
        private List<(string meshName, Material material)> loadedMaterialsForSave = new List<(string, Material)>();

        /// <summary>
        /// マテリアルを記録（後でアセットとして保存用）
        /// </summary>
        private void RecordMaterialForSave(string meshName, Material material, string texturePath)
        {
            if (material == null) return;
            loadedMaterialsForSave.Add((meshName, material));
        }

        /// <summary>
        /// 記録したマテリアルをアセットとして保存
        /// </summary>
        public void SaveLoadedMaterialsToStreamingAssets(string modelName)
        {
            if (loadedMaterialsForSave.Count == 0)
            {
                Debug.LogWarning("[RuntimeMaterialManager] 保存するマテリアルがありません。");
                return;
            }

            // 保存先ディレクトリ（Assets/StreamingAssets/LoadedMaterials/モデル名/）
            string relativePath = $"Assets/StreamingAssets/LoadedMaterials/{modelName}";
            string fullPath = Path.Combine(Application.dataPath, "StreamingAssets", "LoadedMaterials", modelName);

            // ディレクトリを作成
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }

            // 正しいlilToonシェーダーを取得（GUIDベース）
            // lilToon の正しいGUID: df12117ecd77c31469c224178886498e
            string lilToonShaderPath = "Packages/jp.lilxyzw.liltoon/Shader/lts.shader";
            Shader correctLilToonShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(lilToonShaderPath);
            if (correctLilToonShader == null)
            {
                // フォールバック: Shader.Findを使用
                correctLilToonShader = Shader.Find("lilToon");
            }
            Debug.Log($"[RuntimeMaterialManager] 保存用シェーダー: {correctLilToonShader?.name ?? "null"} (path: {lilToonShaderPath})");

            int savedCount = 0;
            foreach (var (meshName, material) in loadedMaterialsForSave)
            {
                if (material == null) continue;

                // マテリアル名をファイル名に使用（不正な文字を除去）
                string safeName = string.Join("_", material.name.Split(Path.GetInvalidFileNameChars()));
                string assetPath = $"{relativePath}/{safeName}.mat";

                // 既存のアセットがあれば削除
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(assetPath) != null)
                {
                    UnityEditor.AssetDatabase.DeleteAsset(assetPath);
                }

                // マテリアルをコピーして保存（元のマテリアルを変更しない）
                Material materialCopy = new Material(material);

                // シェーダーを正しいlilToonに置き換え（Hidden系を回避）
                if (correctLilToonShader != null && UseLilToonShaderOnly)
                {
                    materialCopy.shader = correctLilToonShader;
                }

                UnityEditor.AssetDatabase.CreateAsset(materialCopy, assetPath);
                savedCount++;

                Debug.Log($"[RuntimeMaterialManager] マテリアル保存: {assetPath} (Shader: {materialCopy.shader?.name})");
            }

            UnityEditor.AssetDatabase.SaveAssets();
            UnityEditor.AssetDatabase.Refresh();

            Debug.Log($"[RuntimeMaterialManager] マテリアルを保存しました: {relativePath} ({savedCount}件)");

            // リストをクリア
            loadedMaterialsForSave.Clear();
        }

        /// <summary>
        /// マテリアル記録リストをクリア
        /// </summary>
        public void ClearMaterialSaveList()
        {
            loadedMaterialsForSave.Clear();
        }
#elif DEVELOPMENT_BUILD
        // Development Buildではダミー実装
        private void RecordMaterialForSave(string meshName, Material material, string texturePath) { }
        public void SaveLoadedMaterialsToStreamingAssets(string modelName) { }
        public void ClearMaterialSaveList() { }
#endif

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
#if UNITY_EDITOR
                        // マテリアルをアセットとして保存用に記録（エディタのみ）
                        RecordMaterialForSave(meshNodeName, mat, null);
#endif
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
#endif

#if UNITY_EDITOR
            // マテリアルをアセットとして保存（エディタのみ）
            SaveLoadedMaterialsToStreamingAssets(gameObject.name);
#endif

#if DEVELOPMENT_BUILD || UNITY_EDITOR
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
        /// MaterialManifestをロード
        /// </summary>
        /// <param name="directory">マニフェストが保存されているディレクトリ</param>
        /// <returns>ロードされたManifest、失敗時はnull</returns>
        private MaterialManifest LoadMaterialManifest(string directory)
        {
            if (loadedMaterialManifests.TryGetValue(directory, out MaterialManifest cached))
            {
                return cached;
            }

            string manifestPath = Path.Combine(directory, "MaterialManifest.json");
            if (!File.Exists(manifestPath))
            {
                materialSearchLog.AppendLine($"    [Manifest] MaterialManifest not found at: {Path.GetFileName(directory)}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                MaterialManifest manifest = JsonUtility.FromJson<MaterialManifest>(json);

                if (manifest != null)
                {
                    // IsValid()チェックを外す（JsonUtilityの制限でListが正しくデシリアライズされない可能性）
                    loadedMaterialManifests[directory] = manifest;
                    string msg = $"[Manifest] ✓ Loaded MaterialManifest: {manifest.materialCount} materials";
                    Debug.Log(msg);
                    materialSearchLog.AppendLine($"    {msg}");
                    return manifest;
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"[Manifest] ✗ Failed to load MaterialManifest: {ex.Message}";
                Debug.LogWarning(errorMsg);
                materialSearchLog.AppendLine($"    {errorMsg}");
            }

            return null;
        }

        /// <summary>
        /// TextureManifestをロード
        /// </summary>
        /// <param name="directory">マニフェストが保存されているディレクトリ</param>
        /// <returns>ロードされたManifest、失敗時はnull</returns>
        private TextureManifest LoadTextureManifest(string directory)
        {
            if (loadedTextureManifests.TryGetValue(directory, out TextureManifest cached))
            {
                return cached;
            }

            string manifestPath = Path.Combine(directory, "TextureManifest.json");
            if (!File.Exists(manifestPath))
            {
                Debug.Log($"[RuntimeMaterialManager] TextureManifest not found: {manifestPath}");
                return null;
            }

            try
            {
                string json = File.ReadAllText(manifestPath);
                TextureManifest manifest = JsonUtility.FromJson<TextureManifest>(json);

                if (manifest != null)
                {
                    // IsValid()チェックを外す（JsonUtilityの制限でListが正しくデシリアライズされない可能性）
                    loadedTextureManifests[directory] = manifest;
                    Debug.Log($"[RuntimeMaterialManager] ✓ Loaded TextureManifest: {manifest.textureCount} textures from {directory}");
                    return manifest;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[RuntimeMaterialManager] Failed to load TextureManifest: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// 親ディレクトリからテクスチャファイルを検索してMaterialを作成
        /// UniSIL統合: .matファイルがあればYAMLパースとShader推論を使用
        /// Manifest優先: MaterialManifestがあれば優先的に使用
        /// </summary>
        /// <param name="extractedPath">FBXファイルのパス</param>
        /// <param name="searchNames">検索するファイル名のリスト</param>
        /// <returns>作成されたMaterialのリスト</returns>
        private async UniTask<List<Material>> SearchTexturesInParentDirectory(string extractedPath, List<string> searchNames)
        {
            var materials = new List<Material>();

            // extractedPathがディレクトリかファイルか判定
            string fbxDir;
            if (Directory.Exists(extractedPath))
            {
                // extractedPathがディレクトリの場合、そのまま使用
                fbxDir = extractedPath;
            }
            else if (File.Exists(extractedPath))
            {
                // extractedPathがファイルの場合、親ディレクトリを取得
                fbxDir = Path.GetDirectoryName(extractedPath);
            }
            else
            {
                // どちらでもない場合、親ディレクトリを試す
                fbxDir = Path.GetDirectoryName(extractedPath);
            }

            if (string.IsNullOrEmpty(fbxDir) || !Directory.Exists(fbxDir))
            {
                Debug.LogWarning($"FBXディレクトリが見つかりません: {extractedPath}");
                materialSearchLog.AppendLine($"    ERROR: FBX directory not found: {extractedPath}");
                return materials;
            }

            // 解凍先のルートディレクトリを取得
            // UnityPackageExtractorは extractedFolderPath 配下に全ファイルを展開する
            // MaterialManifest.json と TextureManifest.json はルート直下に保存される
            //
            // ルート検出方法: fbxDirから最大5階層上まで遡り、Manifestファイルを探す
            materialSearchLog.AppendLine($"    [Manifest] Searching for extract root...");
            string extractRootDir = FindExtractRootDirectory(fbxDir);
            if (string.IsNullOrEmpty(extractRootDir))
            {
                Debug.LogError($"[RuntimeMaterialManager] Could not find extract root (no Manifest found within 5 levels up from {fbxDir})");
                materialSearchLog.AppendLine($"    ERROR: Could not find extract root directory");
                materialSearchLog.AppendLine($"    No MaterialManifest.json or TextureManifest.json found within 5 levels");
                return materials;
            }

            Debug.Log($"[RuntimeMaterialManager] FBX directory: {fbxDir}");
            Debug.Log($"[RuntimeMaterialManager] Extract root directory: {extractRootDir}");
            materialSearchLog.AppendLine($"    FBX directory: {fbxDir}");
            materialSearchLog.AppendLine($"    [Manifest] ✓ Extract root found: {extractRootDir}");

            // 検索対象ディレクトリ（優先順位順）
            // シェーダー優先順位: lilToon > Poiyomi > UnityChan > Default (Standard)
            string[] searchDirectories = BuildMaterialSearchDirectories(extractRootDir, fbxDir);

            materialSearchLog.AppendLine($"    [Material Search] Shader priority: lilToon > Poiyomi > UnityChan > Default");
            materialSearchLog.AppendLine($"    [Material Search] Searching {searchDirectories.Length} directories in priority order");

            // 戦略0（新規）: MaterialManifestを使用
            foreach (var searchDir in searchDirectories)
            {
                if (!Directory.Exists(searchDir))
                    continue;

                MaterialManifest materialManifest = LoadMaterialManifest(searchDir);
                if (materialManifest != null)
                {
                    Debug.Log($"[Manifest] Using MaterialManifest from: {searchDir}");
                    materialSearchLog.AppendLine($"    [Manifest] Using MaterialManifest: {materialManifest.materialCount} materials");

                    // searchNamesに一致するマテリアルを検索（複数マテリアル対応）
                    foreach (var searchName in searchNames)
                    {
                        var entry = materialManifest.FindByName(searchName);
                        if (entry != null)
                        {
                            Debug.Log($"[Manifest] Found material in manifest: {searchName}");
                            materialSearchLog.AppendLine($"    [Manifest] Found: {searchName} (shader: {entry.shaderName})");

                            // .matファイルから再構築 (manifestに記録された正しいパスを使用)
                            string matPath = entry.assetPath;
                            materialSearchLog.AppendLine($"    [Manifest] Checking .mat file: {matPath}");

                            if (File.Exists(matPath))
                            {
                                var material = await CreateMaterialFromMatFile(matPath, extractRootDir);
                                if (material != null)
                                {
                                    // 重複チェック: 同じ名前のマテリアルがすでに存在する場合はスキップ
                                    bool isDuplicate = materials.Any(m => m.name == material.name);
                                    if (!isDuplicate)
                                    {
                                        materials.Add(material);
                                        materialSearchLog.AppendLine($"    [Manifest] ✓ Material reconstructed successfully");
                                    }
                                    else
                                    {
                                        materialSearchLog.AppendLine($"    [Manifest] ⚠ Duplicate material skipped: {material.name}");
                                        // 重複マテリアルは即座に破棄（Destroyは次フレームまで遅延するため参照エラーの原因になる）
                                        UnityEngine.Object.DestroyImmediate(material);
                                    }
                                    // ✓ 複数マテリアル対応: 見つかってもすぐにreturnせず、全searchNamesをチェック
                                }
                            }
                            else
                            {
                                materialSearchLog.AppendLine($"    [Manifest] ✗ .mat file not found at: {matPath}");
                            }
                        }
                    }

                    // Manifest検索で1つ以上見つかっていれば、それを返す
                    if (materials.Count > 0)
                    {
                        materialSearchLog.AppendLine($"    [Manifest] ✓ Total {materials.Count} material(s) reconstructed from manifest");
                        return materials;
                    }
                }
            }

            // 戦略1: .matファイルを複数のディレクトリから探してUniSILで再構築（複数マテリアル対応）
            foreach (var searchName in searchNames)
            {
                bool foundForThisName = false;
                foreach (var searchDir in searchDirectories)
                {
                    if (!Directory.Exists(searchDir))
                        continue;

                    string matPath = Path.Combine(searchDir, searchName + ".mat");
                    if (File.Exists(matPath))
                    {
                        Debug.Log($"[UniSIL] .mat file found: {matPath}");
                        materialSearchLog.AppendLine($"    [UniSIL] Found .mat file: {searchName}.mat in {Path.GetFileName(searchDir)}/");

                        var material = await CreateMaterialFromMatFile(matPath, extractRootDir);
                        if (material != null)
                        {
                            // 重複チェック: 同じ名前のマテリアルがすでに存在する場合はスキップ
                            bool isDuplicate = materials.Any(m => m.name == material.name);
                            if (!isDuplicate)
                            {
                                materials.Add(material);
                                materialSearchLog.AppendLine($"    [UniSIL] Material reconstructed successfully");
                                foundForThisName = true;
                                break; // このsearchNameに対して見つかったので次のsearchNameへ
                            }
                            else
                            {
                                materialSearchLog.AppendLine($"    [UniSIL] ⚠ Duplicate material skipped: {material.name}");
                                // 重複マテリアルは即座に破棄（Destroyは次フレームまで遅延するため参照エラーの原因になる）
                                UnityEngine.Object.DestroyImmediate(material);
                                foundForThisName = true;
                                break; // 重複でも次のsearchNameへ
                            }
                        }
                        else
                        {
                            materialSearchLog.AppendLine($"    [UniSIL] Failed to reconstruct material from .mat");
                        }
                    }
                }
            }

            // 戦略1で1つ以上見つかっていれば、それを返す
            if (materials.Count > 0)
            {
                materialSearchLog.AppendLine($"    [UniSIL] ✓ Total {materials.Count} material(s) reconstructed");
                return materials;
            }

            // 戦略2（フォールバック）: テクスチャファイルのみからStandardシェーダーで作成
            Debug.Log($"[RuntimeMaterialManager] No .mat files found, falling back to texture-only search");
            materialSearchLog.AppendLine($"    [Fallback] No .mat files found, searching for texture files only");

            // テクスチャ検索用ディレクトリ（Material/, Texture/, Textures/ なども探す）
            string[] textureSearchDirs = new string[]
            {
                Path.Combine(extractRootDir, "Texture"),
                Path.Combine(extractRootDir, "Textures"),
                Path.Combine(extractRootDir, "Material"),
                Path.Combine(extractRootDir, "Materials"),
                fbxDir,
                extractRootDir
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
                                // 重複チェック: 同じ名前のマテリアルがすでに存在する場合はスキップ
                                bool isDuplicate = materials.Any(m => m.name == material.name);
                                if (!isDuplicate)
                                {
                                    materials.Add(material);
                                    materialSearchLog.AppendLine($"    Material created successfully from texture");
                                }
                                else
                                {
                                    materialSearchLog.AppendLine($"    ⚠ Duplicate material skipped: {material.name}");
                                    // 重複マテリアルは即座に破棄（Destroyは次フレームまで遅延するため参照エラーの原因になる）
                                    UnityEngine.Object.DestroyImmediate(material);
                                }
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
        /// MaterialReconstructorは使わず、ShaderInferenceのみを使用してシェーダーを推論
        /// テクスチャは手動でTextureManifestから読み込み
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
                Debug.Log("[UniSIL] Loading ShaderDatabase...");
                var shaderDB = ShaderDBLoader.LoadDatabase();

                if (shaderDB == null)
                {
                    Debug.LogError("[UniSIL] Failed to load ShaderDatabase - ShaderDBLoader.LoadDatabase() returned null");
                    Debug.LogError("[UniSIL] Please check if Assets/Resources/ShaderDB.asset exists");
                    materialSearchLog.AppendLine($"      ERROR: ShaderDatabase not found");
                    return null;
                }

                if (shaderDB.shaders == null)
                {
                    Debug.LogError("[UniSIL] ShaderDatabase.shaders is null - ShaderDB.asset may be corrupted");
                    Debug.LogError("[UniSIL] Please regenerate ShaderDB.asset using Tools > UniSIL > Generate ShaderDB");
                    materialSearchLog.AppendLine($"      ERROR: ShaderDatabase.shaders is null (corrupted asset)");
                    return null;
                }

                Debug.Log($"[UniSIL] ShaderDatabase loaded successfully: {shaderDB.shaders.Count} shaders");

                // シェーダー取得の優先順位:
                // 0. UseLilToonShaderOnly が true の場合、lilToon固定
                // 1. GUID直接lookup (ShaderGuidDictionary)
                // 2. ShaderInferenceEngineでの推論
                // 3. フォールバック (lilToon → Standard)

                Shader shader = null;
                string shaderSource = null;

                // 戦略0: lilToonシェーダー固定モード
                if (UseLilToonShaderOnly)
                {
                    shader = GetLilToonShader();
                    shaderSource = "Fixed (lilToon)";
                    Debug.Log($"[UniSIL] Using fixed lilToon shader: {shader?.name}");
                    materialSearchLog.AppendLine($"      [Fixed Mode] Using lilToon shader: {shader?.name}");
                }

                // 戦略1: GUIDがあれば直接lookupを試みる
                if (shader == null && !string.IsNullOrEmpty(materialData.shaderGuid))
                {
                    Debug.Log($"[UniSIL] Attempting GUID lookup: {materialData.shaderGuid}");
                    materialSearchLog.AppendLine($"      [Strategy 1] Direct GUID lookup: {materialData.shaderGuid}");

                    // ShaderGuidDictionaryLoaderを使用してGUIDから直接シェーダー名を取得
                    var shaderGuidDict = ShaderGuidDictionaryLoader.LoadDictionary();
                    if (shaderGuidDict != null)
                    {
                        string rawShaderName = shaderGuidDict.GetShaderNameByGuid(materialData.shaderGuid);

                        // Hidden/lilToon* を公開lilToonシェーダーに正規化
                        string shaderName = NormalizeLilToonShaderName(rawShaderName);

                        // 正規化が行われた場合はログに記録
                        if (!string.IsNullOrEmpty(shaderName) && !string.IsNullOrEmpty(rawShaderName) && shaderName != rawShaderName)
                        {
                            Debug.Log($"[UniSIL] Hidden lilToon shader normalized: '{rawShaderName}' → '{shaderName}'");
                            materialSearchLog.AppendLine($"      Normalized: {rawShaderName} → {shaderName}");
                        }

                        if (!string.IsNullOrEmpty(shaderName))
                        {
                            shader = Shader.Find(shaderName);
                            if (shader != null)
                            {
                                Debug.Log($"[UniSIL] ✓ Found shader by GUID: {shaderName}");
                                materialSearchLog.AppendLine($"      ✓ GUID lookup succeeded: {shaderName}");
                                shaderSource = "GUID Lookup";
                            }
                            else
                            {
                                Debug.LogWarning($"[UniSIL] ✗ Shader name found by GUID but Shader.Find() failed: {shaderName}");
                                materialSearchLog.AppendLine($"      ✗ Shader name found but not loaded: {shaderName}");
                            }
                        }
                        else
                        {
                            Debug.Log($"[UniSIL] GUID not found in ShaderGuidDictionary");
                            materialSearchLog.AppendLine($"      GUID not found in dictionary");
                        }
                    }
                    else
                    {
                        Debug.LogWarning("[UniSIL] ShaderGuidDictionary could not be loaded");
                        materialSearchLog.AppendLine($"      ✗ ShaderGuidDictionary not available");
                    }
                }

                // 戦略2: GUID lookupで見つからなければ推論を使用
                if (shader == null)
                {
                    Debug.Log($"[UniSIL] Falling back to shader inference");
                    materialSearchLog.AppendLine($"      [Strategy 2] Shader inference");

                    var config = new InferenceConfig();
                    var inferenceEngine = new ShaderInferenceEngine(shaderDB, config);
                    ShaderInferenceResult inferenceResult;

                    if (!string.IsNullOrEmpty(materialData.shaderGuid))
                    {
                        Debug.Log($"[UniSIL] Using shader GUID for inference: {materialData.shaderGuid}");
                        materialSearchLog.AppendLine($"      Using GUID-based inference");
                        inferenceResult = inferenceEngine.InferShaderWithGuid(materialData, materialData.shaderGuid);
                    }
                    else
                    {
                        Debug.Log($"[UniSIL] No shader GUID, using property-based inference");
                        materialSearchLog.AppendLine($"      Using property-based inference");
                        inferenceResult = inferenceEngine.InferShader(materialData);
                    }

                    Debug.Log($"[UniSIL] Inferred shader: {inferenceResult.inferredShader} (confidence: {inferenceResult.confidence:P2})");
                    materialSearchLog.AppendLine($"      Inferred: {inferenceResult.inferredShader} (confidence: {inferenceResult.confidence:P2})");

                    // Hidden/lilToon* シェーダーを公開シェーダーに正規化
                    string targetShaderName = inferenceResult.inferredShader;
                    if (LilToonShaderNormalizer.TryNormalize(targetShaderName, out string normalizedName))
                    {
                        Debug.Log($"[UniSIL] Normalized Hidden lilToon shader: '{targetShaderName}' → '{normalizedName}'");
                        materialSearchLog.AppendLine($"      Normalized: {targetShaderName} → {normalizedName}");
                        targetShaderName = normalizedName;
                    }

                    shader = Shader.Find(targetShaderName);
                    if (shader != null)
                    {
                        shaderSource = $"Inference ({inferenceResult.confidence:P2})";
                        materialSearchLog.AppendLine($"      ✓ Inference succeeded");
                    }
                    else
                    {
                        Debug.LogWarning($"[UniSIL] ✗ Inferred shader not found: {targetShaderName}");
                        materialSearchLog.AppendLine($"      ✗ Inferred shader not found in project");
                    }
                }

                // 戦略3: 最終フォールバック
                if (shader == null)
                {
                    Debug.LogWarning($"[UniSIL] All shader lookup strategies failed, using fallback");
                    materialSearchLog.AppendLine($"      [Strategy 3] Fallback shaders");

                    shader = Shader.Find("lilToon");
                    if (shader != null)
                    {
                        shaderSource = "Fallback (lilToon)";
                        materialSearchLog.AppendLine($"      Using lilToon fallback");
                    }
                    else
                    {
                        Debug.LogError("[UniSIL] Even lilToon shader not found, using Standard");
                        shader = Shader.Find("Standard");
                        shaderSource = "Fallback (Standard)";
                        materialSearchLog.AppendLine($"      Using Standard fallback");
                    }
                }

                // Materialを作成
                var material = new Material(shader);
                material.name = materialData.name;

                Debug.Log($"[UniSIL] Material created with shader: {shader.name} (source: {shaderSource})");
                materialSearchLog.AppendLine($"      ✓ Material created with shader: {shader.name}");
                materialSearchLog.AppendLine($"      Shader source: {shaderSource}");

                // プロパティを適用
                int appliedProps = 0;

                // Float/Range properties
                if (materialData.floats != null)
                {
                    foreach (var kvp in materialData.floats)
                    {
                        if (material.HasProperty(kvp.Key))
                        {
                            material.SetFloat(kvp.Key, kvp.Value);
                            appliedProps++;
                        }
                    }
                }

                // Color properties
                if (materialData.colors != null)
                {
                    foreach (var kvp in materialData.colors)
                    {
                        if (material.HasProperty(kvp.Key))
                        {
                            material.SetColor(kvp.Key, kvp.Value);
                            appliedProps++;
                        }
                    }
                }

                Debug.Log($"[UniSIL] Applied {appliedProps} properties");
                materialSearchLog.AppendLine($"      Applied {appliedProps} properties");

                // lilToonシェーダーの場合、キーワードとレンダーステートを復元
                // materialData is UniSIL.ShaderInference.MaterialData from YAMLMaterialParser.Parse()
                if (shader.name.Contains("lilToon"))
                {
                    var setupResult = LilToonMaterialSetup.SetupMaterial(material, materialData, enableLogging: true);
                    if (setupResult.Success)
                    {
                        Debug.Log($"[UniSIL] lilToon setup: Mode={setupResult.DetectedRenderingMode}, Keywords={setupResult.AppliedKeywords}, RenderStates={setupResult.AppliedRenderStates}");
                        materialSearchLog.AppendLine($"      lilToon setup: Mode={setupResult.DetectedRenderingMode}, Keywords={setupResult.AppliedKeywords}, RenderStates={setupResult.AppliedRenderStates}");
                    }
                    else
                    {
                        Debug.LogWarning($"[UniSIL] lilToon setup failed: {setupResult.ErrorMessage}");
                        materialSearchLog.AppendLine($"      lilToon setup failed: {setupResult.ErrorMessage}");
                    }
                }

                // テクスチャを手動で読み込み（TextureManifestから）
                // .matファイルのディレクトリを取得
                string matDirectory = Path.GetDirectoryName(matPath);

                // 解凍先ルートディレクトリを取得（Manifestがある場所）
                // matDirectoryから最大5階層上まで遡り、TextureManifest.jsonを探す
                string extractRootDir = FindExtractRootDirectory(matDirectory);
                if (string.IsNullOrEmpty(extractRootDir))
                {
                    Debug.LogWarning($"[UniSIL] Could not find extract root (no TextureManifest.json found within 5 levels up from {matDirectory})");
                    extractRootDir = matDirectory; // フォールバック: .matと同じディレクトリ
                }

                Debug.Log($"[UniSIL] Material directory: {matDirectory}");
                Debug.Log($"[UniSIL] Extract root directory: {extractRootDir}");

                // まず解凍先ルートのManifestを優先的に読み込み
                var textureManifest = LoadTextureManifest(extractRootDir);
                if (textureManifest == null)
                {
                    Debug.Log($"[UniSIL] TextureManifest not found at extract root: {extractRootDir}");
                    // 見つからなければ.matファイルと同じディレクトリを試す
                    textureManifest = LoadTextureManifest(matDirectory);
                    if (textureManifest == null)
                    {
                        Debug.LogWarning($"[UniSIL] TextureManifest not found at material directory: {matDirectory}");
                    }
                }
                else
                {
                    Debug.Log($"[UniSIL] TextureManifest loaded: {textureManifest.textureCount} textures");
                }

                int appliedTextures = 0;
                if (materialData.textures == null)
                {
                    Debug.LogWarning($"[UniSIL] materialData.textures is NULL");
                    materialSearchLog.AppendLine($"      WARNING: materialData.textures is NULL");
                }
                else if (materialData.textures.Count == 0)
                {
                    Debug.LogWarning($"[UniSIL] materialData.textures is EMPTY (Count=0)");
                    materialSearchLog.AppendLine($"      WARNING: materialData.textures is EMPTY");
                }
                else
                {
                    Debug.Log($"[UniSIL] Material has {materialData.textures.Count} texture properties");
                    materialSearchLog.AppendLine($"      Material has {materialData.textures.Count} texture properties");

                    foreach (var texProp in materialData.textures)
                    {
                        Debug.Log($"[UniSIL]   Texture property: {texProp.name} (GUID: {texProp.guid})");

                        if (!material.HasProperty(texProp.name))
                        {
                            Debug.LogWarning($"[UniSIL]   Property {texProp.name} not found in shader {material.shader.name}");
                            continue;
                        }

                        Texture2D loadedTexture = null;

                        // 戦略1: TextureManifestからマテリアル名ベースで検索（最優先）
                        // GUIDは解凍時に変わるため、ファイル名ベースの検索を優先
                        if (textureManifest != null && loadedTexture == null)
                        {
                            // マテリアル名（例: "Cloth"）を使ってテクスチャを検索
                            string materialBaseName = materialData.name; // 例: "Cloth"
                            Debug.Log($"[UniSIL] Searching texture for material '{materialBaseName}', property '{texProp.name}'");

                            foreach (var texEntry in textureManifest.textures)
                            {
                                string fileName = Path.GetFileNameWithoutExtension(texEntry.relativePath);

                                // ファイル名とマテリアル名の双方向マッチング
                                // 例: "Cloth" <-> "Clothes.png", "Lace" <-> "Lace2", "lace1" <-> "Lace"
                                bool nameMatches = fileName.Contains(materialBaseName, StringComparison.OrdinalIgnoreCase) ||
                                                  materialBaseName.Contains(fileName, StringComparison.OrdinalIgnoreCase);

                                if (nameMatches)
                                {
                                    // プロパティタイプに応じたサフィックスチェック
                                    bool isMatch = false;

                                    if (texProp.name.Contains("Base", StringComparison.OrdinalIgnoreCase) ||
                                        texProp.name.Contains("Main", StringComparison.OrdinalIgnoreCase) ||
                                        texProp.name.Contains("Albedo", StringComparison.OrdinalIgnoreCase) ||
                                        texProp.name.Contains("Color", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // ベースカラー: サフィックスなし、または _Main/_Color
                                        isMatch = !fileName.Contains("Normal", StringComparison.OrdinalIgnoreCase) &&
                                                 !fileName.Contains("Bump", StringComparison.OrdinalIgnoreCase) &&
                                                 !fileName.Contains("Metallic", StringComparison.OrdinalIgnoreCase) &&
                                                 !fileName.Contains("Smoothness", StringComparison.OrdinalIgnoreCase);
                                        Debug.Log($"[UniSIL]   Checking base color texture: {fileName} -> isMatch={isMatch}");
                                    }
                                    else if (texProp.name.Contains("Normal", StringComparison.OrdinalIgnoreCase) ||
                                            texProp.name.Contains("Bump", StringComparison.OrdinalIgnoreCase))
                                    {
                                        // ノーマルマップ: _Normal または _Bump
                                        isMatch = fileName.Contains("Normal", StringComparison.OrdinalIgnoreCase) ||
                                                 fileName.Contains("Bump", StringComparison.OrdinalIgnoreCase);
                                        Debug.Log($"[UniSIL]   Checking normal map: {fileName} -> isMatch={isMatch}");
                                    }
                                    else
                                    {
                                        // その他のプロパティ: マテリアル名に一致するファイルの最初のものを使用
                                        // 特殊マップ(Normal/Bump/Metallic/Smoothness)を除外
                                        isMatch = !fileName.Contains("Normal", StringComparison.OrdinalIgnoreCase) &&
                                                 !fileName.Contains("Bump", StringComparison.OrdinalIgnoreCase) &&
                                                 !fileName.Contains("Metallic", StringComparison.OrdinalIgnoreCase) &&
                                                 !fileName.Contains("Smoothness", StringComparison.OrdinalIgnoreCase);
                                        Debug.Log($"[UniSIL]   Checking other property '{texProp.name}': {fileName} -> isMatch={isMatch}");
                                    }

                                    if (isMatch)
                                    {
                                        string texPath = Path.Combine(extractRootDir, texEntry.relativePath);
                                        loadedTexture = await LoadTextureFromFile(texPath);

                                        if (loadedTexture != null)
                                        {
                                            Debug.Log($"[UniSIL] ✓ Loaded texture by material name: {texProp.name} -> {texEntry.relativePath}");
                                            materialSearchLog.AppendLine($"        ✓ Loaded: {Path.GetFileName(texEntry.relativePath)}");
                                            break;
                                        }
                                    }
                                }
                            }

                            if (loadedTexture == null)
                            {
                                Debug.LogWarning($"[UniSIL] No texture found for material '{materialBaseName}', property '{texProp.name}'");
                            }
                        }

                        // 戦略2（非推奨・フォールバック）: GUIDで検索
                        // UnityPackage解凍時にGUIDが変わるため、通常は失敗する
                        if (textureManifest != null && loadedTexture == null && !string.IsNullOrEmpty(texProp.guid))
                        {
                            var texEntry = textureManifest.FindByGuid(texProp.guid);
                            if (texEntry != null)
                            {
                                string texPath = Path.Combine(extractRootDir, texEntry.relativePath);
                                loadedTexture = await LoadTextureFromFile(texPath);

                                if (loadedTexture != null)
                                {
                                    Debug.Log($"[UniSIL] Loaded texture by GUID: {texProp.name} -> {texEntry.relativePath}");
                                }
                            }
                        }

                        // Manifestで見つからない場合は直接ファイル検索
                        if (loadedTexture == null && !string.IsNullOrEmpty(texProp.name))
                        {
                            // 一般的な拡張子で検索
                            string[] extensions = { ".png", ".jpg", ".jpeg", ".tga" };

                            // 検索対象ディレクトリ
                            string[] searchDirs = new string[]
                            {
                                matDirectory,                                      // .matと同じディレクトリ
                                Path.Combine(extractRootDir, "Textures"),         // ルート/Textures
                                Path.Combine(extractRootDir, "Materials"),        // ルート/Materials
                                extractRootDir                                     // ルート直下
                            };

                            foreach (string searchDir in searchDirs)
                            {
                                if (!Directory.Exists(searchDir))
                                    continue;

                                foreach (string ext in extensions)
                                {
                                    // プロパティ名からテクスチャ名を推測
                                    string texName = texProp.name.Replace("_MainTex", "").Replace("_", "");
                                    string texPath = Path.Combine(searchDir, texName + ext);

                                    if (File.Exists(texPath))
                                    {
                                        loadedTexture = await LoadTextureFromFile(texPath);
                                        if (loadedTexture != null)
                                        {
                                            Debug.Log($"[UniSIL] Loaded texture by name: {texProp.name} -> {searchDir}/{texName}{ext}");
                                            break;
                                        }
                                    }
                                }

                                if (loadedTexture != null)
                                    break;
                            }
                        }

                        if (loadedTexture != null)
                        {
                            material.SetTexture(texProp.name, loadedTexture);

                            // .matファイルから読み取ったUV scale/offsetを適用
                            // TexturePropertyにはオリジナルのUV設定が保存されている
                            if (texProp.scale != Vector2.one || texProp.offset != Vector2.zero)
                            {
                                material.SetTextureScale(texProp.name, texProp.scale);
                                material.SetTextureOffset(texProp.name, texProp.offset);
                                Debug.Log($"[UniSIL]   Applied UV transform: scale={texProp.scale}, offset={texProp.offset} for {texProp.name}");
                            }

                            appliedTextures++;
                        }
                        else
                        {
                            Debug.LogWarning($"[UniSIL]   Failed to load texture for property: {texProp.name}");
                        }
                    }
                } // end of else (materialData.textures is not null/empty)

                // lilToon shaders require _MainTex to be set for rendering
                // If _MainTex is not set but we have base color textures, copy one to _MainTex
                if (material.shader.name.Contains("lilToon", StringComparison.OrdinalIgnoreCase))
                {
                    if (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") == null)
                    {
                        // Try to find a base color texture to use as _MainTex
                        Texture2D baseTexture = null;

                        // Check common base color properties in priority order
                        string[] baseColorProperties = { "_BaseMap", "_BaseColorMap", "_MainTexture", "_ColorMap" };
                        foreach (string propName in baseColorProperties)
                        {
                            if (material.HasProperty(propName))
                            {
                                baseTexture = material.GetTexture(propName) as Texture2D;
                                if (baseTexture != null)
                                {
                                    material.SetTexture("_MainTex", baseTexture);
                                    Debug.Log($"[UniSIL] Set _MainTex from {propName} for lilToon shader");
                                    materialSearchLog.AppendLine($"      ✓ Set _MainTex from {propName}");
                                    break;
                                }
                            }
                        }

                        if (baseTexture == null)
                        {
                            Debug.LogWarning($"[UniSIL] lilToon shader detected but no base color texture found to set _MainTex");
                        }
                    }
                }

                Debug.Log($"[UniSIL] Applied {appliedTextures} textures");
                materialSearchLog.AppendLine($"      Applied {appliedTextures} textures");

                // Keywords適用
                if (materialData.keywords != null && materialData.keywords.Count > 0)
                {
                    foreach (var keyword in materialData.keywords)
                    {
                        material.EnableKeyword(keyword);
                    }
                    Debug.Log($"[UniSIL] Applied {materialData.keywords.Count} keywords");
                    materialSearchLog.AppendLine($"      Applied {materialData.keywords.Count} keywords");
                }

                materialSearchLog.AppendLine($"      ✓ Material reconstructed successfully");
                materialSearchLog.AppendLine($"      Final shader: {material.shader.name}");

                await UniTask.Yield();
                return material;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[UniSIL] Error reconstructing material from {matPath}: {ex.Message}");
                Debug.LogError($"[UniSIL] Stack trace: {ex.StackTrace}");
                materialSearchLog.AppendLine($"      ERROR: {ex.Message}");
                materialSearchLog.AppendLine($"      Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        /// <summary>
        /// ファイルパスからテクスチャを読み込み
        /// </summary>
        private async UniTask<Texture2D> LoadTextureFromFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return null;

                byte[] fileData = await File.ReadAllBytesAsync(filePath);

                // URP + Linearカラースペース対応:
                // テクスチャはsRGBカラースペースで保存されているため、linear=false（sRGB）で作成
                // これによりUnityが自動的にLinear空間に変換してレンダリングします
                Texture2D texture = new Texture2D(2, 2, TextureFormat.BGRA32, mipChain: false, linear: false);

                if (texture.LoadImage(fileData))
                {
                    // テクスチャの基本設定を適用
                    // Unity標準のテクスチャインポート設定に合わせる
                    texture.wrapMode = TextureWrapMode.Repeat;  // デフォルトはRepeat
                    texture.filterMode = FilterMode.Bilinear;   // デフォルトはBilinear
                    texture.anisoLevel = 1;                     // 異方性フィルタリングレベル

                    // 注: LoadImage()はPNG/JPGの向きを自動的に正しく処理します
                    // FBXのUV座標は変更不要（メッシュ側で正しく設定されている）

                    texture.Compress(true);
                    return texture;
                }
                else
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UniSIL] Failed to load texture from {filePath}: {ex.Message}");
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
            Shader shader;
            if (UseLilToonShaderOnly)
            {
                shader = GetLilToonShader();
            }
            else
            {
                shader = Shader.Find("Standard");
            }

            if (shader == null)
            {
                Debug.LogWarning("シェーダーが見つかりません。");
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

            Shader shader;

            // シェーダー固定モードの場合はlilToonを使用
            if (UseLilToonShaderOnly)
            {
                shader = GetLilToonShader();
#if DEVELOPMENT_BUILD || UNITY_EDITOR
                materialSearchLog.AppendLine($"      [Fixed Mode] Using lilToon shader: {shader?.name}");
#endif
            }
            else
            {
                shader = Shader.Find(entry.shaderName);
            }

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

                string fileName = $"MaterialManager_Log_{environment}_{timestamp}.txt";

                // FBXImportLogsディレクトリに保存
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                string logsDirectory = Path.Combine(projectRoot, "FBXImportLogs");

                // ディレクトリが存在しない場合は作成
                if (!Directory.Exists(logsDirectory))
                {
                    Directory.CreateDirectory(logsDirectory);
                    Debug.Log($"Created logs directory: {logsDirectory}");
                }

                string filePath = Path.Combine(logsDirectory, fileName);

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

        /// <summary>
        /// シェーダー優先順位に基づいてマテリアル検索ディレクトリを構築
        /// 優先順位: lilToon > Poiyomi > UnityChan > Default (Standard)
        /// </summary>
        /// <param name="extractRootDir">解凍先ルートディレクトリ</param>
        /// <param name="fbxDir">FBXディレクトリ</param>
        /// <returns>優先順位順のディレクトリ配列</returns>
        private string[] BuildMaterialSearchDirectories(string extractRootDir, string fbxDir)
        {
            var directories = new List<string>();

            // シェーダー優先順位定義
            // 優先順位: lilToon > Poiyomi > UnityChan > Default (Standard)
            string[] shaderPriorityKeywords = new string[]
            {
                "lilToon",
                "liltoon",
                "Poiyomi",
                "poiyomi",
                "UnityChan",
                "unitychan",
                "UTS",  // Unity Toon Shader
                "Standard",
                "standard",
                "Default",
                "default"
            };

            // 1. 優先順位の高いシェーダーフォルダを先に追加
            foreach (var keyword in shaderPriorityKeywords)
            {
                // extractRootDir 直下の Material/Materials フォルダをチェック
                string[] materialFolderNames = new string[]
                {
                    $"Materials_{keyword}",
                    $"Material_{keyword}",
                    $"{keyword}_Materials",
                    $"{keyword}_Material",
                    $"Mat_{keyword}",
                    $"{keyword}"
                };

                foreach (var folderName in materialFolderNames)
                {
                    // extractRootDir直下
                    string candidate = Path.Combine(extractRootDir, folderName);
                    if (Directory.Exists(candidate) && !directories.Contains(candidate))
                    {
                        directories.Add(candidate);
                        Debug.Log($"[Material Priority] Added: {folderName} (Priority: {Array.IndexOf(shaderPriorityKeywords, keyword) + 1})");
                    }

                    // extractRootDir/Assets 配下も探す
                    string assetsCandidate = Path.Combine(extractRootDir, "Assets", folderName);
                    if (Directory.Exists(assetsCandidate) && !directories.Contains(assetsCandidate))
                    {
                        directories.Add(assetsCandidate);
                        Debug.Log($"[Material Priority] Added: Assets/{folderName} (Priority: {Array.IndexOf(shaderPriorityKeywords, keyword) + 1})");
                    }

                    // FBXファイルと同じ階層の兄弟フォルダもチェック
                    string fbxParent = Path.GetDirectoryName(fbxDir);
                    if (!string.IsNullOrEmpty(fbxParent))
                    {
                        string siblingCandidate = Path.Combine(fbxParent, folderName);
                        if (Directory.Exists(siblingCandidate) && !directories.Contains(siblingCandidate))
                        {
                            directories.Add(siblingCandidate);
                            Debug.Log($"[Material Priority] Added: ../{folderName} (Priority: {Array.IndexOf(shaderPriorityKeywords, keyword) + 1})");
                        }
                    }
                }
            }

            // 2. 汎用マテリアルフォルダを追加（優先順位キーワードなし）
            string[] genericMaterialFolders = new string[]
            {
                Path.Combine(extractRootDir, "Materials"),
                Path.Combine(extractRootDir, "Material"),
                Path.Combine(extractRootDir, "Assets", "Materials"),
                Path.Combine(extractRootDir, "Assets", "Material"),
                fbxDir  // FBXと同じディレクトリ（最後）
            };

            foreach (var folder in genericMaterialFolders)
            {
                if (Directory.Exists(folder) && !directories.Contains(folder))
                {
                    directories.Add(folder);
                }
            }

            // 3. 解凍先ルート自体も最後に追加（Manifestがある場所）
            if (!directories.Contains(extractRootDir))
            {
                directories.Add(extractRootDir);
            }

            Debug.Log($"[Material Priority] Total {directories.Count} directories in search order");
            for (int i = 0; i < directories.Count; i++)
            {
                Debug.Log($"[Material Priority]   {i + 1}. {Path.GetFileName(directories[i])}");
            }

            return directories.ToArray();
        }

        /// <summary>
        /// 指定ディレクトリから最大5階層上まで遡り、MaterialManifest.jsonまたはTextureManifest.jsonが存在するディレクトリを返す
        /// </summary>
        /// <param name="startDirectory">開始ディレクトリ</param>
        /// <returns>Manifestが見つかったディレクトリ。見つからない場合はnull</returns>
        private string FindExtractRootDirectory(string startDirectory)
        {
            const int MAX_LEVELS = 5;
            string currentDir = startDirectory;

            for (int level = 0; level < MAX_LEVELS; level++)
            {
                if (string.IsNullOrEmpty(currentDir))
                    break;

                // MaterialManifest.json または TextureManifest.json が存在するか確認
                string materialManifestPath = Path.Combine(currentDir, "MaterialManifest.json");
                string textureManifestPath = Path.Combine(currentDir, "TextureManifest.json");

                if (File.Exists(materialManifestPath) || File.Exists(textureManifestPath))
                {
                    Debug.Log($"[FindExtractRoot] Found manifest at level {level}: {currentDir}");
                    return currentDir;
                }

                // 親ディレクトリへ移動
                string parentDir = Path.GetDirectoryName(currentDir);
                if (string.IsNullOrEmpty(parentDir) || parentDir == currentDir)
                {
                    // これ以上親ディレクトリがない
                    break;
                }

                currentDir = parentDir;
            }

            Debug.LogWarning($"[FindExtractRoot] No manifest found within {MAX_LEVELS} levels from {startDirectory}");
            return null;
        }

        /// <summary>
        /// Hidden/lilToon* シェーダー名を公開用 lilToon シェーダー名に正規化する
        /// </summary>
        /// <param name="shaderName">元のシェーダー名</param>
        /// <returns>正規化されたシェーダー名（Hidden/lilToon*の場合はlilToonに変換）</returns>
        private string NormalizeLilToonShaderName(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName))
                return shaderName;

            // Hidden/lilToon* シェーダーを公開用 lilToon に正規化
            if (shaderName.StartsWith("Hidden/lilToon", StringComparison.OrdinalIgnoreCase))
            {
                // Hidden/lilToonLite* → lilToonLite
                if (shaderName.StartsWith("Hidden/lilToonLite", StringComparison.OrdinalIgnoreCase))
                {
                    return "lilToonLite";
                }
                // Hidden/lilToonMulti* → lilToonMulti
                else if (shaderName.StartsWith("Hidden/lilToonMulti", StringComparison.OrdinalIgnoreCase))
                {
                    return "lilToonMulti";
                }
                // Hidden/lilToon* (その他) → lilToon
                else
                {
                    return "lilToon";
                }
            }

            return shaderName;
        }
    }
}
