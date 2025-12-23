using UnityEngine;
using Assimp;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UniSIL.ShaderInference;
using UniSIL.ShaderInference.MaterialLoading;
using UniSIL.ShaderInference.MaterialReconstruction;

using arCam.FBXLoader;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Assimpを使用してFBXファイルからボーン階層のみをロードする
    /// メッシュ読み込みは後回し、まずAポーズ生成に専念
    /// </summary>
    public class RuntimeAssimpFBXLoader
    {
        private const string LOG_PREFIX = "[RuntimeAssimpFBXLoader]";

        // Shader キャッシュ（起動時間短縮）
        private static Shader _cachedStandardShader;
        private static Shader _cachedUnlitColorShader;
        private static Shader _cachedLilToonShader;

        // 座標系変換行列（FBXごとに自動検出）
        private UnityEngine.Matrix4x4 coordinateConversionMatrix = UnityEngine.Matrix4x4.identity;
        private FbxCoordProfile coordProfile;
        private bool shouldFlipTriangleWinding = false;

        // Assimp Scene
        private Scene currentScene;

        // FBXファイルのディレクトリパス（テクスチャ検索用）
        private string fbxDirectory;

        // ボーン名 → Transform のマップ（STEP 4: SkinnedMeshRenderer.bones構築用）
        private Dictionary<string, Transform> boneNameToTransform = new Dictionary<string, Transform>();

        // MeshNode名 → マテリアル名のマッピング（RuntimeMaterialManager用）
        private Dictionary<string, List<string>> meshNodeToMaterialNames = new Dictionary<string, List<string>>();

        // UniSIL統合: MaterialManifestキャッシュ
        private MaterialManifest cachedMaterialManifest = null;
        private Dictionary<string, MaterialManifest.MaterialEntry> materialNameToEntry = new Dictionary<string, MaterialManifest.MaterialEntry>();

        // AssimpSceneCacheキャッシュ
        private AssimpSceneCache assimpSceneCache = null;
        private string cachedFbxPath = null;

        /// <summary>
        /// MeshNode名とマテリアル名のマッピングを取得
        /// </summary>
        public Dictionary<string, List<string>> GetMeshNodeToMaterialNames() => meshNodeToMaterialNames;

        /// <summary>
        /// シェーダーをキャッシュから取得（起動時間短縮）
        /// </summary>
        private static Shader GetCachedShader(string shaderName)
        {
            switch (shaderName)
            {
                case "Standard":
                    if (_cachedStandardShader == null)
                        _cachedStandardShader = Shader.Find("Standard");
                    return _cachedStandardShader;
                case "Unlit/Color":
                    if (_cachedUnlitColorShader == null)
                        _cachedUnlitColorShader = Shader.Find("Unlit/Color");
                    return _cachedUnlitColorShader;
                case "lilToon":
                    if (_cachedLilToonShader == null)
                        _cachedLilToonShader = Shader.Find("lilToon");
                    return _cachedLilToonShader;
                default:
                    // キャッシュされていないシェーダーは直接検索
                    return Shader.Find(shaderName);
            }
        }

        /// <summary>
        /// FBXファイルからボーン階層をロードしてGameObjectツリーを構築（非同期）
        /// v0.4.0以降: メッシュも階層構築時に即座に処理（辞書ルックアップ不使用）
        /// </summary>
        /// <param name="fbxPath">FBXファイルのパス</param>
        /// <param name="rootName">ルートGameObjectの名前</param>
        /// <returns>ルートGameObject</returns>
        public async UniTask<GameObject> LoadBoneHierarchy(string fbxPath, string rootName = null)
        {
            if (!File.Exists(fbxPath))
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} FBX file not found: {fbxPath}");
                return null;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} Loading FBX: {fbxPath}");

            // Assimpでシーンをロード（三角形化を有効化）
            AssimpContext importer = new AssimpContext();
            Scene scene = importer.ImportFile(fbxPath, PostProcessSteps.Triangulate);

            if (scene == null || scene.RootNode == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to import FBX file");
                return null;
            }

            // シーンを保存（メッシュロード時に使用）
            currentScene = scene;

            // FBXディレクトリパスを保存（テクスチャ検索用）
            fbxDirectory = Path.GetDirectoryName(fbxPath);
            cachedFbxPath = fbxPath;

            // AssimpSceneCacheをロードまたは生成
            assimpSceneCache = AssimpSceneCacheBuilder.Load(fbxPath);
            if (assimpSceneCache == null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} Generating new AssimpSceneCache...");
                assimpSceneCache = AssimpSceneCacheBuilder.BuildAndSave(scene, fbxPath);
            }
            else
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} Using existing AssimpSceneCache");
            }

            // UniSIL統合: MaterialManifestをロード
            LoadMaterialManifests();

            UnityEngine.Debug.Log($"{LOG_PREFIX} Scene loaded successfully");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Meshes: {scene.MeshCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Materials: {scene.MaterialCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Animations: {scene.AnimationCount}");

            // 重い処理後にフレームを譲る
            await UniTask.Yield();

            // FBX Global Settings (座標系情報)
            UnityEngine.Debug.Log($"{LOG_PREFIX} === FBX Global Settings ===");

            // FBX座標系を自動検出
            coordProfile = FbxCoordinateSystemDetector.ExtractFbxCoordProfile(scene);
            coordinateConversionMatrix = FbxCoordinateSystemDetector.BuildConversionMatrix(coordProfile);

            // 行列の行列式を計算（負の場合は三角形の巻き順を反転する必要がある）
            float determinant = coordinateConversionMatrix.determinant;
            shouldFlipTriangleWinding = determinant < 0f;

            UnityEngine.Debug.Log($"{LOG_PREFIX} Detected Coordinate System:");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Profile: {coordProfile.profileName}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Up Axis: {coordProfile.up}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Front Axis: {coordProfile.front}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Right Axis: {coordProfile.right}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Handedness: {(coordProfile.isRightHanded ? "Right-handed" : "Left-handed")}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} Conversion to Unity (Left-handed, Y-up, Z-forward):");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Matrix Row 0: {coordinateConversionMatrix.GetRow(0)}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Matrix Row 1: {coordinateConversionMatrix.GetRow(1)}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Matrix Row 2: {coordinateConversionMatrix.GetRow(2)}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Determinant: {determinant}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Should Flip Triangle Winding: {shouldFlipTriangleWinding}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} === End Global Settings ===");

            // ボーン名辞書をクリア
            boneNameToTransform.Clear();

            // FBX名を決定
            string objName = string.IsNullOrEmpty(rootName) ? Path.GetFileNameWithoutExtension(fbxPath) : rootName;

            // ノード階層をGameObject階層に変換（親なしで開始し、メッシュも即座に処理）
            GameObject rootObject = await BuildBoneHierarchyAsyncRoot(scene.RootNode, scene);

            // ルートGameObjectの名前をFBX名に変更
            if (rootObject != null)
            {
                rootObject.name = objName;
                UnityEngine.Debug.Log($"{LOG_PREFIX} Root GameObject renamed to: {objName}");
            }

            // rootBoneを検索（Hips）- SkinnedMeshRenderer用
            cachedRootBone = FindRootBone(rootObject.transform);
            if (cachedRootBone != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} Root bone found: {cachedRootBone.name}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} Root bone (Hips) not found in hierarchy");
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} Bone hierarchy built successfully");
            LogHierarchy(rootObject.transform, 0);

            // 全メッシュ構築完了後、キャッシュを使用してマテリアルを一括割り当て
            await AssignMaterialsFromCache(rootObject);

            return rootObject;
        }

        /// <summary>
        /// ルートノードからGameObject階層を構築（親なし）
        /// </summary>
        private async UniTask<GameObject> BuildBoneHierarchyAsyncRoot(Node rootNode, Scene scene)
        {
            if (rootNode == null)
                return null;

            // ルートノード用のGameObjectを作成（親なし）
            GameObject rootObject = new GameObject(rootNode.Name);

            // Transform情報を設定
            SetTransformFromAssimpMatrix(rootObject.transform, rootNode.Transform);

            // ボーン名辞書に追加
            if (!boneNameToTransform.ContainsKey(rootNode.Name))
            {
                boneNameToTransform[rootNode.Name] = rootObject.transform;
            }

            // Hipsボーンを見つけたら即座にキャッシュ
            if (rootNode.Name.ToLower().Contains("hips") && cachedRootBone == null)
            {
                cachedRootBone = rootObject.transform;
                UnityEngine.Debug.Log($"{LOG_PREFIX} Root bone cached during hierarchy build: {rootNode.Name}");
            }

            // このノードがメッシュを持っている場合、即座に処理
            if (rootNode.MeshCount > 0)
            {
                string hierarchyPath = GetTransformPath(rootObject.transform);
                UnityEngine.Debug.Log($"{LOG_PREFIX} Processing mesh node: {rootNode.Name} with {rootNode.MeshCount} mesh(es)");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   → Attaching to GameObject: {hierarchyPath}");

                await LoadMeshesForNodeAsync(rootNode, rootObject.transform);
                await UniTask.Yield();
            }

            // 子ノードを再帰的に処理
            for (int i = 0; i < rootNode.ChildCount; i++)
            {
                await BuildBoneHierarchyAsync(rootNode.Children[i], rootObject.transform, scene, 1);
            }

            return rootObject;
        }

        /// <summary>
        /// Assimpノード階層をGameObject階層に再帰的に変換（非同期版）
        /// メッシュを持つノードは即座に処理（辞書ルックアップを使用しない）
        /// </summary>
        private async UniTask BuildBoneHierarchyAsync(Node node, Transform parentTransform, Scene scene, int depth = 0)
        {
            if (node == null)
                return;

            // 現在のノード用のGameObjectを作成
            GameObject nodeObject = new GameObject(node.Name);
            nodeObject.transform.SetParent(parentTransform, false);

            // Transform情報を設定（Assimpの行列をUnityに変換）
            SetTransformFromAssimpMatrix(nodeObject.transform, node.Transform);

            // ボーン名辞書に追加（STEP 4: bones[]構築時に使用）
            // 重複する名前の場合は最初に見つかったものを保持
            if (!boneNameToTransform.ContainsKey(node.Name))
            {
                boneNameToTransform[node.Name] = nodeObject.transform;
            }

            // Hipsボーンを見つけたら即座にキャッシュ（メッシュ処理で必要）
            if (node.Name.ToLower().Contains("hips") && cachedRootBone == null)
            {
                cachedRootBone = nodeObject.transform;
                UnityEngine.Debug.Log($"{LOG_PREFIX} Root bone cached during hierarchy build: {node.Name}");
            }

            // このノードがメッシュを持っている場合、即座に処理
            if (node.MeshCount > 0)
            {
                string hierarchyPath = GetTransformPath(nodeObject.transform);
                UnityEngine.Debug.Log($"{LOG_PREFIX} Processing mesh node: {node.Name} with {node.MeshCount} mesh(es)");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   → Attaching to GameObject: {hierarchyPath}");

                await LoadMeshesForNodeAsync(node, nodeObject.transform);
                await UniTask.Yield(); // メッシュ処理後にフレームを譲る
            }

            // 一定の深さごとにフレームを譲る（UIフリーズ防止）
            if (depth % 10 == 0)
            {
                await UniTask.Yield();
            }

            // 子ノードを再帰的に処理
            for (int i = 0; i < node.ChildCount; i++)
            {
                await BuildBoneHierarchyAsync(node.Children[i], nodeObject.transform, scene, depth + 1);
            }
        }

        /// <summary>
        /// AssimpのMatrix4x4からUnityのTransformに変換（座標系変換適用）
        /// Transform階層とMesh頂点の両方をUnity座標系に統一
        /// </summary>
        private void SetTransformFromAssimpMatrix(Transform t, Assimp.Matrix4x4 m)
        {
            m.Decompose(out var s, out var r, out var p);

            UnityEngine.Vector3 pos = FbxCoordinateSystemDetector.ConvertVector(p, coordinateConversionMatrix);
            UnityEngine.Quaternion rot = FbxCoordinateSystemDetector.ConvertQuaternion(r, coordinateConversionMatrix);
            UnityEngine.Vector3 scale = new UnityEngine.Vector3(s.X, s.Y, s.Z);

            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = scale;
        }

        /// <summary>
        /// 階層構造をログ出力
        /// </summary>
        private void LogHierarchy(Transform t, int depth)
        {
            string indent = new string(' ', depth * 2);
            Vector3 euler = t.localEulerAngles;
            UnityEngine.Debug.Log($"{LOG_PREFIX} {indent}├─ {t.name} Rot({euler.x:F1}, {euler.y:F1}, {euler.z:F1})");

            foreach (Transform child in t)
            {
                LogHierarchy(child, depth + 1);
            }
        }

        /// <summary>
        /// Transformの階層パスを取得（例: "kyoko/RootNode/Armature/Hair"）
        /// </summary>
        private string GetTransformPath(Transform transform)
        {
            if (transform == null)
                return "";

            string path = transform.name;
            Transform current = transform.parent;

            while (current != null)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }

            return path;
        }

        /// <summary>
        /// ボーン名からHumanBodyBonesを推測
        /// </summary>
        public Dictionary<HumanBodyBones, Transform> MapHumanoidBones(Transform root)
        {
            var boneMap = new Dictionary<HumanBodyBones, Transform>();

            // 再帰的に全Transformを検索
            MapBonesRecursive(root, boneMap);

            UnityEngine.Debug.Log($"{LOG_PREFIX} Humanoid bone mapping completed: {boneMap.Count} bones mapped");
            foreach (var kvp in boneMap)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX}   {kvp.Key,-25} → {kvp.Value.name}");
            }

            return boneMap;
        }

        private void MapBonesRecursive(Transform t, Dictionary<HumanBodyBones, Transform> boneMap)
        {
            // ボーン名からHumanBodyBonesを推測（簡易版）
            string boneName = t.name.ToLower();

            if (boneName.Contains("hips") || boneName == "pelvis")
                TryAddBone(boneMap, HumanBodyBones.Hips, t);
            else if (boneName.Contains("spine") && !boneName.Contains("chest"))
                TryAddBone(boneMap, HumanBodyBones.Spine, t);
            else if (boneName.Contains("chest") || (boneName.Contains("spine") && boneName.Contains("1")))
                TryAddBone(boneMap, HumanBodyBones.Chest, t);
            else if (boneName.Contains("neck"))
                TryAddBone(boneMap, HumanBodyBones.Neck, t);
            else if (boneName.Contains("head") && !boneName.Contains("top"))
                TryAddBone(boneMap, HumanBodyBones.Head, t);

            // 腕（左）
            else if ((boneName.Contains("left") || boneName.EndsWith(".l")) && boneName.Contains("shoulder"))
                TryAddBone(boneMap, HumanBodyBones.LeftShoulder, t);
            else if ((boneName.Contains("left") || boneName.EndsWith(".l")) && (boneName.Contains("upper") && boneName.Contains("arm")))
                TryAddBone(boneMap, HumanBodyBones.LeftUpperArm, t);
            else if ((boneName.Contains("left") || boneName.EndsWith(".l")) && (boneName.Contains("lower") && boneName.Contains("arm")))
                TryAddBone(boneMap, HumanBodyBones.LeftLowerArm, t);
            else if ((boneName.Contains("left") || boneName.EndsWith(".l")) && boneName.Contains("hand"))
                TryAddBone(boneMap, HumanBodyBones.LeftHand, t);

            // 腕（右）
            else if ((boneName.Contains("right") || boneName.EndsWith(".r")) && boneName.Contains("shoulder"))
                TryAddBone(boneMap, HumanBodyBones.RightShoulder, t);
            else if ((boneName.Contains("right") || boneName.EndsWith(".r")) && (boneName.Contains("upper") && boneName.Contains("arm")))
                TryAddBone(boneMap, HumanBodyBones.RightUpperArm, t);
            else if ((boneName.Contains("right") || boneName.EndsWith(".r")) && (boneName.Contains("lower") && boneName.Contains("arm")))
                TryAddBone(boneMap, HumanBodyBones.RightLowerArm, t);
            else if ((boneName.Contains("right") || boneName.EndsWith(".r")) && boneName.Contains("hand"))
                TryAddBone(boneMap, HumanBodyBones.RightHand, t);

            // 脚（左）
            else if ((boneName.Contains("left") || boneName.EndsWith(".l")) && (boneName.Contains("upper") && boneName.Contains("leg")))
                TryAddBone(boneMap, HumanBodyBones.LeftUpperLeg, t);
            else if ((boneName.Contains("left") || boneName.EndsWith(".l")) && (boneName.Contains("lower") && boneName.Contains("leg")))
                TryAddBone(boneMap, HumanBodyBones.LeftLowerLeg, t);
            else if ((boneName.Contains("left") || boneName.EndsWith(".l")) && boneName.Contains("foot"))
                TryAddBone(boneMap, HumanBodyBones.LeftFoot, t);
            else if ((boneName.Contains("left") || boneName.EndsWith(".l")) && boneName.Contains("toe"))
                TryAddBone(boneMap, HumanBodyBones.LeftToes, t);

            // 脚（右）
            else if ((boneName.Contains("right") || boneName.EndsWith(".r")) && (boneName.Contains("upper") && boneName.Contains("leg")))
                TryAddBone(boneMap, HumanBodyBones.RightUpperLeg, t);
            else if ((boneName.Contains("right") || boneName.EndsWith(".r")) && (boneName.Contains("lower") && boneName.Contains("leg")))
                TryAddBone(boneMap, HumanBodyBones.RightLowerLeg, t);
            else if ((boneName.Contains("right") || boneName.EndsWith(".r")) && boneName.Contains("foot"))
                TryAddBone(boneMap, HumanBodyBones.RightFoot, t);
            else if ((boneName.Contains("right") || boneName.EndsWith(".r")) && boneName.Contains("toe"))
                TryAddBone(boneMap, HumanBodyBones.RightToes, t);

            // 子ノードを再帰的に処理
            foreach (Transform child in t)
            {
                MapBonesRecursive(child, boneMap);
            }
        }

        private void TryAddBone(Dictionary<HumanBodyBones, Transform> boneMap, HumanBodyBones boneType, Transform transform)
        {
            if (!boneMap.ContainsKey(boneType))
            {
                boneMap[boneType] = transform;
            }
        }

        /// <summary>
        /// STEP 5: rootBoneを決定する（Humanoidの場合はHips）
        /// </summary>
        /// <param name="root">ルートTransform</param>
        /// <returns>rootBone（Hipsまたは最初のボーン）</returns>
        private Transform FindRootBone(Transform root)
        {
            // Hipsを検索（Humanoidの標準rootBone）
            Transform hips = FindTransformByName(root, "hips");
            if (hips != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} rootBone: {hips.name}");
                return hips;
            }

            // "pelvis"という名前も試す
            Transform pelvis = FindTransformByName(root, "pelvis");
            if (pelvis != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} rootBone: {pelvis.name}");
                return pelvis;
            }

            // 見つからない場合は警告して最初の子をrootBoneとする
            UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [STEP 5] Hips not found, using first child as rootBone");
            if (root.childCount > 0)
            {
                return root.GetChild(0);
            }

            // それでもない場合はrootそのものを返す
            UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [STEP 5] No children found, using root as rootBone");
            return root;
        }

        /// <summary>
        /// Transform階層から名前で検索（大文字小文字無視、再帰的）
        /// </summary>
        private Transform FindTransformByName(Transform parent, string name)
        {
            string nameLower = name.ToLower();

            // 現在のTransformをチェック
            if (parent.name.ToLower().Contains(nameLower))
            {
                return parent;
            }

            // 子を再帰的に検索
            foreach (Transform child in parent)
            {
                Transform found = FindTransformByName(child, name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        // メッシュロード用にrootBoneをキャッシュ（Avatar生成用）
        private Transform cachedRootBone;

        /// <summary>
        /// メッシュロード（後方互換性のため残す）
        /// v0.4.0以降: メッシュは BuildBoneHierarchyAsync() 内でインライン処理されるため、
        /// このメソッドは何もしない（rootBoneの更新のみ）
        /// </summary>
        /// <param name="rootObject">ルートGameObject</param>
        public async UniTask LoadMeshes(GameObject rootObject)
        {
            if (currentScene == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} No scene loaded. Call LoadBoneHierarchy first.");
                return;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} LoadMeshes() called - meshes already processed during hierarchy build");

            // STEP 5: rootBoneを決定（将来のスキニング実装用）
            cachedRootBone = FindRootBone(rootObject.transform);
            if (cachedRootBone != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} Root bone found: {cachedRootBone.name}");
            }

            // サニティチェック: モデルロード完了
            UnityEngine.Debug.Log($"{LOG_PREFIX} ========================================");
            UnityEngine.Debug.Log($"{LOG_PREFIX} [Check] モデルロード完了");
            UnityEngine.Debug.Log($"{LOG_PREFIX} [Check] Root Transform = {rootObject.transform.name}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} [Check] Bone Count in hierarchy = {boneNameToTransform.Count}");

            await UniTask.Yield();
        }

        /// <summary>
        /// 特定のノードに含まれる全メッシュをロードしてSkinnedMeshRendererを作成（非同期版）
        /// v0.5.0: リファクタリング - 4クラスアーキテクチャを使用
        /// </summary>
        private async UniTask LoadMeshesForNodeAsync(Node node, Transform nodeTransform)
        {
            try
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} ========================================");
                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMeshesForNode] START for node: {node.Name}");
                UnityEngine.Debug.Log($"{LOG_PREFIX} ========================================");

                // ============================================================
                // v0.5.0 REFACTORED: 4-Class Architecture
                // ============================================================
                // STEP 2: MeshDataCollector - メッシュデータ収集
                MeshDataCollector meshCollector = new MeshDataCollector(
                    currentScene, node, coordinateConversionMatrix, debugMode: false);
                MeshData meshData = meshCollector.Collect();
                await UniTask.Yield();

                // STEP 3: BoneDataCollector - ボーンデータ収集
                BoneDataCollector boneCollector = new BoneDataCollector(
                    currentScene, node, meshData.vertices.Count, debugMode: false);
                BoneData boneData = boneCollector.Collect();
                await UniTask.Yield();

                // ボーンがない場合は静的メッシュとして作成
                if (boneData.allUniqueBoneNames.Count == 0)
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX} Mesh has no bones, creating static mesh (MeshFilter + MeshRenderer)");
                    CreateStaticMeshRenderer(nodeTransform, meshData.unityMesh);
                    UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMeshesForNode] SUCCESS (Static Mesh) for node: {node.Name}");
                    return;
                }

                // STEP 4: SkinnedMeshBuilder - SkinnedMeshRenderer 構築
                SkinnedMeshBuilder skinnedMeshBuilder = new SkinnedMeshBuilder(
                    nodeTransform.gameObject, boneNameToTransform, coordinateConversionMatrix, debugMode: false);

                // rootBone名を決定（Hipsまたは最初のボーン）
                string rootBoneName = cachedRootBone != null ? cachedRootBone.name : (boneData.allUniqueBoneNames.Count > 0 ? boneData.allUniqueBoneNames[0] : null);

                // マテリアル作成（メッシュのマテリアルインデックスを取得）
                int materialIndex = -1;
                Assimp.Material assimpMaterial = null;
                List<string> materialNames = new List<string>();

                if (node.MeshCount > 0)
                {
                    // 全メッシュのマテリアル名を収集
                    foreach (int assimpMeshIndex in node.MeshIndices)
                    {
                        Assimp.Mesh assimpMesh = currentScene.Meshes[assimpMeshIndex];
                        int matIndex = assimpMesh.MaterialIndex;

                        if (matIndex >= 0 && matIndex < currentScene.MaterialCount)
                        {
                            Assimp.Material mat = currentScene.Materials[matIndex];
                            if (!materialNames.Contains(mat.Name))
                            {
                                materialNames.Add(mat.Name);
                            }
                        }
                    }

                    // 最初のメッシュのマテリアルを使用（テクスチャ抽出用）
                    int assimpMeshIndex0 = node.MeshIndices[0];
                    Assimp.Mesh assimpMesh0 = currentScene.Meshes[assimpMeshIndex0];
                    materialIndex = assimpMesh0.MaterialIndex;

                    if (materialIndex >= 0 && materialIndex < currentScene.MaterialCount)
                    {
                        assimpMaterial = currentScene.Materials[materialIndex];
                        UnityEngine.Debug.Log($"{LOG_PREFIX}   Mesh material index: {materialIndex}, Material name: {assimpMaterial.Name}");
                    }

                    // MeshNode名とマテリアル名のマッピングを保存
                    if (materialNames.Count > 0)
                    {
                        meshNodeToMaterialNames[node.Name] = materialNames;
                        UnityEngine.Debug.Log($"{LOG_PREFIX}   Stored material mapping: {node.Name} -> [{string.Join(", ", materialNames)}]");
                    }
                }

                // デフォルトマテリアルで一時的に構築（後でAssignMaterialsFromCacheで置き換える）
                UnityEngine.Material defaultMaterial = CreateDefaultMaterial(node.Name);
                UnityEngine.Material[] materials = new UnityEngine.Material[] { defaultMaterial };

                // SkinnedMeshRenderer 構築
                SkinnedMeshRenderer smr = skinnedMeshBuilder.Build(meshData, boneData, rootBoneName, materials);
                UnityEngine.Debug.Log($"{LOG_PREFIX}   SkinnedMeshRenderer created with default material (will be replaced later)");
                await UniTask.Yield();

                UnityEngine.Debug.Log($"{LOG_PREFIX} ========================================");
                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMeshesForNode] SUCCESS for node: {node.Name}");
                UnityEngine.Debug.Log($"{LOG_PREFIX} ========================================");
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} [LoadMeshesForNode] FAILED for node: {node.Name}");
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Exception: {ex.Message}");
                UnityEngine.Debug.LogError($"{LOG_PREFIX} StackTrace: {ex.StackTrace}");

                // エラーが発生してもフォールバック：最低限のレンダラーを作成
                try
                {
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} Creating fallback renderer for node: {node.Name}");

                    // 簡易メッシュを作成
                    UnityEngine.Mesh fallbackMesh = new UnityEngine.Mesh();
                    fallbackMesh.name = $"{node.Name}_Fallback";
                    fallbackMesh.vertices = new UnityEngine.Vector3[] { UnityEngine.Vector3.zero };
                    fallbackMesh.triangles = new int[] { };

                    CreateStaticMeshRenderer(nodeTransform, fallbackMesh);
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} Fallback renderer created for node: {node.Name}");
                }
                catch (System.Exception fallbackEx)
                {
                    UnityEngine.Debug.LogError($"{LOG_PREFIX} Even fallback renderer failed: {fallbackEx.Message}");
                }
            }
        }

        /// <summary>
        /// STEP 3: Assimp AnimMesh から BlendShape を Unity Mesh に追加
        /// </summary>
        private async UniTask AddBlendShapesToMesh(Node node, UnityEngine.Mesh mesh)
        {
            if (node.MeshCount == 0)
                return;

            int totalBlendShapes = 0;

            // このノードの全メッシュを処理
            for (int meshIdx = 0; meshIdx < node.MeshCount; meshIdx++)
            {
                int assimpMeshIndex = node.MeshIndices[meshIdx];
                Assimp.Mesh assimpMesh = currentScene.Meshes[assimpMeshIndex];

                // AnimMesh（BlendShape）がない場合はスキップ
                if (!assimpMesh.HasMeshAnimationAttachments || assimpMesh.MeshAnimationAttachmentCount == 0)
                    continue;

                UnityEngine.Debug.Log($"{LOG_PREFIX} === Processing BlendShapes for mesh: {assimpMesh.Name} ===");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   AnimMesh count: {assimpMesh.MeshAnimationAttachmentCount}");

                // 各 AnimMesh（BlendShape）を処理
                for (int animIdx = 0; animIdx < assimpMesh.MeshAnimationAttachmentCount; animIdx++)
                {
                    Assimp.MeshAnimationAttachment animMesh = assimpMesh.MeshAnimationAttachments[animIdx];

                    // BlendShape名を生成（MeshAnimationAttachmentにはName プロパティがないためインデックスを使用）
                    string blendShapeName = $"{assimpMesh.Name}_BlendShape_{animIdx}";

                    UnityEngine.Debug.Log($"{LOG_PREFIX}   BlendShape: {blendShapeName}");

                    // デルタ頂点配列を作成
                    UnityEngine.Vector3[] deltaVertices = new UnityEngine.Vector3[mesh.vertexCount];
                    UnityEngine.Vector3[] deltaNormals = new UnityEngine.Vector3[mesh.vertexCount];
                    UnityEngine.Vector3[] deltaTangents = new UnityEngine.Vector3[mesh.vertexCount];

                    // AnimMeshの頂点データを変換
                    if (animMesh.HasVertices)
                    {
                        for (int i = 0; i < animMesh.VertexCount && i < mesh.vertexCount; i++)
                        {
                            // ベース頂点との差分を計算
                            Assimp.Vector3D baseVertex = assimpMesh.Vertices[i];
                            Assimp.Vector3D animVertex = animMesh.Vertices[i];
                            Assimp.Vector3D delta = animVertex - baseVertex;

                            // 座標系変換を適用
                            deltaVertices[i] = FbxCoordinateSystemDetector.ConvertVector(delta, coordinateConversionMatrix);
                        }
                    }

                    // 法線デルタを変換
                    if (animMesh.HasNormals)
                    {
                        for (int i = 0; i < animMesh.VertexCount && i < mesh.vertexCount; i++)
                        {
                            Assimp.Vector3D baseNormal = assimpMesh.Normals[i];
                            Assimp.Vector3D animNormal = animMesh.Normals[i];
                            Assimp.Vector3D delta = animNormal - baseNormal;

                            // 座標系変換を適用して正規化
                            deltaNormals[i] = FbxCoordinateSystemDetector.ConvertVector(delta, coordinateConversionMatrix).normalized;
                        }
                    }

                    // Tangentデルタを変換（AssimpにTangentがある場合）
                    if (animMesh.HasTangentBasis && assimpMesh.HasTangentBasis)
                    {
                        for (int i = 0; i < animMesh.VertexCount && i < mesh.vertexCount; i++)
                        {
                            Assimp.Vector3D baseTangent = assimpMesh.Tangents[i];
                            Assimp.Vector3D animTangent = animMesh.Tangents[i];
                            Assimp.Vector3D delta = animTangent - baseTangent;

                            // 座標系変換を適用
                            deltaTangents[i] = FbxCoordinateSystemDetector.ConvertVector(delta, coordinateConversionMatrix);
                        }
                    }

                    // BlendShapeフレームを追加（weight = 100）
                    mesh.AddBlendShapeFrame(blendShapeName, 100f, deltaVertices, deltaNormals, deltaTangents);
                    totalBlendShapes++;

                    UnityEngine.Debug.Log($"{LOG_PREFIX}     ✓ Added BlendShape: {blendShapeName} (vertices: {animMesh.VertexCount})");
                }

                // 各メッシュ処理後にフレームを譲る
                await UniTask.Yield();
            }

            if (totalBlendShapes > 0)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} ✓ Total BlendShapes added: {totalBlendShapes}");
            }
            else
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} No BlendShapes found in this mesh");
            }
        }

        /// <summary>
        /// STEP 4-7: SkinnedMeshRenderer のセットアップ
        /// </summary>
        private async UniTask SetupSkinnedMeshRenderer(
            Node node,
            Transform nodeTransform,
            UnityEngine.Mesh mesh,
            List<UnityEngine.BoneWeight> boneWeights,
            List<string> allUniqueBoneNames,
            Dictionary<string, Assimp.Matrix4x4> boneNameToOffsetMatrix)
        {
            UnityEngine.Debug.Log($"{LOG_PREFIX} === Setting up SkinnedMeshRenderer ===");

            // 最初のメッシュからボーン情報を取得
            if (node.MeshCount == 0)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} No meshes in node");
                return;
            }

            int assimpMeshIndex = node.MeshIndices[0];
            Assimp.Mesh assimpMesh = currentScene.Meshes[assimpMeshIndex];

            // ボーンがない場合は静的メッシュとして作成
            if (!assimpMesh.HasBones)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} Mesh has no bones, creating static mesh (MeshFilter + MeshRenderer)");
                CreateStaticMeshRenderer(nodeTransform, mesh);
                return;
            }

            // STEP 4: BoneTransforms（bones[]）の構築（全ユニークボーンから）
            Transform[] bones = BuildBonesArray(allUniqueBoneNames);
            if (bones == null || bones.Length == 0)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to build bones array");
                return;
            }
            UnityEngine.Debug.Log($"{LOG_PREFIX} STEP 4: Bones array built: {bones.Length} bones");

            // STEP 5: BoneWeights の設定
            UnityEngine.Debug.Log($"{LOG_PREFIX} === STEP 5: Assigning BoneWeights to Mesh ===");
            if (boneWeights.Count > 0)
            {
                UnityEngine.BoneWeight[] boneWeightsArray = boneWeights.ToArray();
                mesh.boneWeights = boneWeightsArray;

                // ウェイト統計
                int verticesWithWeights = 0;
                int totalWeights = 0;
                for (int i = 0; i < boneWeightsArray.Length; i++)
                {
                    var bw = boneWeightsArray[i];
                    int weightsForVertex = 0;
                    if (bw.weight0 > 0) weightsForVertex++;
                    if (bw.weight1 > 0) weightsForVertex++;
                    if (bw.weight2 > 0) weightsForVertex++;
                    if (bw.weight3 > 0) weightsForVertex++;

                    if (weightsForVertex > 0)
                    {
                        verticesWithWeights++;
                        totalWeights += weightsForVertex;
                    }
                }

                float avgWeightsPerVertex = verticesWithWeights > 0 ? (float)totalWeights / verticesWithWeights : 0;

                UnityEngine.Debug.Log($"{LOG_PREFIX}   Assigned to mesh.boneWeights");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   Total vertices: {boneWeightsArray.Length}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   Vertices with weights: {verticesWithWeights}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   Average weights per vertex: {avgWeightsPerVertex:F2}");
                UnityEngine.Debug.Log($"{LOG_PREFIX} === STEP 5 Complete ===");
            }
            else
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} STEP 5 FAILED: No bone weights found!");
            }

            // STEP 6: BindPose（bindposes）の生成（全ユニークボーンから）
            UnityEngine.Matrix4x4[] bindposes = BuildBindPoses(allUniqueBoneNames, bones, boneNameToOffsetMatrix);
            if (bindposes == null || bindposes.Length == 0)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} STEP 6 FAILED: Could not build bindposes");
                return;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} === STEP 6: Assigning BindPoses to Mesh ===");
            mesh.bindposes = bindposes;
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Assigned to mesh.bindposes");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Total bindposes: {bindposes.Length}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} === STEP 6 Complete ===");

            // STEP 7: SkinnedMeshRenderer の構築
            UnityEngine.Debug.Log($"{LOG_PREFIX} STEP 7: Creating SkinnedMeshRenderer");

            // SkinnedMeshRenderer を追加
            SkinnedMeshRenderer smr = nodeTransform.gameObject.AddComponent<SkinnedMeshRenderer>();

            if (smr == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to add SkinnedMeshRenderer component!");
                return;
            }

            // 正しい適用順序
            smr.bones = bones;                          // 1. bones設定
            smr.sharedMesh = mesh;                      // 2. mesh設定（bindposes, boneWeights含む）
            smr.rootBone = cachedRootBone;              // 3. rootBone設定（Hips）
            smr.updateWhenOffscreen = true;             // 4. 画面外でも更新

            // マテリアル設定
            UnityEngine.Material material = CreateLilToonMaterial(node.Name);
            smr.sharedMaterial = material;

            // ログ出力
            UnityEngine.Debug.Log($"{LOG_PREFIX} === SkinnedMeshRenderer Setup Complete ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   GameObject: {nodeTransform.name}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Component: SkinnedMeshRenderer (verified: {smr != null})");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Mesh: {mesh.name}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Vertices: {mesh.vertexCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Triangles: {mesh.triangles.Length / 3}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Bones: {bones.Length}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   BindPoses: {bindposes.Length}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   BoneWeights: {boneWeights.Count}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   BlendShapes: {mesh.blendShapeCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   RootBone: {(cachedRootBone != null ? cachedRootBone.name : "null")}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Material: {material.name} (Shader: {material.shader.name})");

            await UniTask.Yield();
        }

        /// <summary>
        /// ボーンを持たないメッシュ用に静的レンダラーを作成
        /// </summary>
        private void CreateStaticMeshRenderer(Transform nodeTransform, UnityEngine.Mesh mesh)
        {
            UnityEngine.Debug.Log($"{LOG_PREFIX} === Creating Static Mesh Renderer ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   GameObject: {nodeTransform.name}");

            // MeshFilterを追加
            MeshFilter meshFilter = nodeTransform.gameObject.AddComponent<MeshFilter>();
            if (meshFilter == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to add MeshFilter component!");
                return;
            }
            meshFilter.sharedMesh = mesh;

            // MeshRendererを追加
            MeshRenderer meshRenderer = nodeTransform.gameObject.AddComponent<MeshRenderer>();
            if (meshRenderer == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to add MeshRenderer component!");
                return;
            }

            // マテリアル設定
            UnityEngine.Material material = CreateLilToonMaterial(nodeTransform.name);
            meshRenderer.sharedMaterial = material;

            // ログ出力
            UnityEngine.Debug.Log($"{LOG_PREFIX} === Static Mesh Renderer Complete ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Component: MeshFilter (verified: {meshFilter != null})");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Component: MeshRenderer (verified: {meshRenderer != null})");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Mesh: {mesh.name}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Vertices: {mesh.vertexCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Triangles: {mesh.triangles.Length / 3}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   BlendShapes: {mesh.blendShapeCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Material: {material.name}");
        }

        /// <summary>
        /// STEP 4: ボーン名リストから Transform[] bones を構築（マルチメッシュ対応）
        /// </summary>
        private Transform[] BuildBonesArray(List<string> boneNames)
        {
            if (boneNames == null || boneNames.Count == 0)
                return null;

            UnityEngine.Debug.Log($"{LOG_PREFIX} === STEP 4: Building Bones Array (All Unique Bones) ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Total unique bones: {boneNames.Count}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Cached bone names: {boneNameToTransform.Count}");

            Transform[] bones = new Transform[boneNames.Count];
            int foundCount = 0;
            int notFoundCount = 0;

            for (int i = 0; i < boneNames.Count; i++)
            {
                string boneName = boneNames[i];

                // ボーン名辞書から Transform を取得
                if (boneNameToTransform.TryGetValue(boneName, out Transform boneTransform))
                {
                    bones[i] = boneTransform;
                    foundCount++;
                    UnityEngine.Debug.Log($"{LOG_PREFIX}   Bone[{i}]: {boneName}");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}     → Path: {GetTransformPath(boneTransform)}");
                }
                else
                {
                    bones[i] = null;
                    notFoundCount++;
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}   Bone[{i}]: {boneName} NOT FOUND in hierarchy!");
                }
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} === STEP 4 Complete ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Found: {foundCount}/{boneNames.Count}");
            if (notFoundCount > 0)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX}   NOT FOUND: {notFoundCount} bones missing!");
            }

            return bones;
        }

        /// <summary>
        /// STEP 6: BindPose を Assimp OffsetMatrix から計算（マルチメッシュ対応）
        /// Assimpのbone.OffsetMatrixは逆バインド行列なので、これを座標系変換して使用
        /// </summary>
        private UnityEngine.Matrix4x4[] BuildBindPoses(
            List<string> boneNames,
            Transform[] bones,
            Dictionary<string, Assimp.Matrix4x4> boneNameToOffsetMatrix)
        {
            if (bones == null || bones.Length == 0)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} BuildBindPoses: bones array is null or empty");
                return null;
            }

            if (boneNames == null || boneNames.Count == 0)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} BuildBindPoses: boneNames is null or empty");
                return null;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} === STEP 6: Building BindPoses (Assimp OffsetMatrix, Multi-Mesh) ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Total bones: {bones.Length}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Source: Assimp bone.OffsetMatrix with coordinate conversion");

            UnityEngine.Matrix4x4[] bindposes = new UnityEngine.Matrix4x4[bones.Length];
            int validCount = 0;

            for (int i = 0; i < boneNames.Count; i++)
            {
                string boneName = boneNames[i];

                if (bones[i] != null && boneNameToOffsetMatrix.TryGetValue(boneName, out Assimp.Matrix4x4 offsetMatrix))
                {
                    // Assimp OffsetMatrixを座標系変換してBindPoseに使用
                    bindposes[i] = FbxCoordinateSystemDetector.ConvertAssimpMatrix(offsetMatrix, coordinateConversionMatrix);
                    validCount++;

                    UnityEngine.Debug.Log($"{LOG_PREFIX}   BindPose[{i}]: {bones[i].name}");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}     → Bone Path: {GetTransformPath(bones[i])}");

                    // 行列の一部を表示（デバッグ用）
                    UnityEngine.Vector3 pos = bindposes[i].GetPosition();
                    UnityEngine.Quaternion rot = bindposes[i].rotation;
                    UnityEngine.Debug.Log($"{LOG_PREFIX}     → BindPose Matrix: pos={pos}, rot={rot.eulerAngles}");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}     → BindPose Full Matrix:\n{bindposes[i]}");
                }
                else
                {
                    bindposes[i] = UnityEngine.Matrix4x4.identity;
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}   BindPose[{i}]: {boneName} - NULL BONE or NO OFFSET MATRIX - using identity matrix");
                }
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} === STEP 6 Complete ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Valid bindposes: {validCount}/{bones.Length}");

            return bindposes;
        }

        /// <summary>
        /// AssimpのMaterial情報からテクスチャを抽出（FBX埋め込みテクスチャ対応）
        /// </summary>
        private UnityEngine.Texture2D ExtractTextureFromAssimpMaterial(Assimp.Material assimpMaterial, Assimp.TextureType textureType)
        {
            UnityEngine.Debug.Log($"{LOG_PREFIX}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} ╔══════════════════════════════════════════════════════════");
            UnityEngine.Debug.Log($"{LOG_PREFIX} ║ TEXTURE EXTRACTION from Material: {assimpMaterial?.Name ?? "null"}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} ╚══════════════════════════════════════════════════════════");

            if (assimpMaterial == null || currentScene == null)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   ✗ Material or Scene is null - aborting");
                return null;
            }

            // マテリアルのテクスチャ情報をデバッグ出力
            UnityEngine.Debug.Log($"{LOG_PREFIX}   📊 Material Texture Info:");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      HasTextureDiffuse: {assimpMaterial.HasTextureDiffuse}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      Diffuse Count: {assimpMaterial.GetMaterialTextureCount(Assimp.TextureType.Diffuse)}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      Emissive Count: {assimpMaterial.GetMaterialTextureCount(Assimp.TextureType.Emissive)}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      Unknown Count: {assimpMaterial.GetMaterialTextureCount(Assimp.TextureType.Unknown)}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      Scene TextureCount: {currentScene.TextureCount}");

            // テクスチャパスを取得
            Assimp.TextureSlot textureSlot;
            bool hasTexture = assimpMaterial.GetMaterialTexture(textureType, 0, out textureSlot);

            if (!hasTexture)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX}   ℹ No texture found in texture slot");
                return null;
            }

            string texturePath = textureSlot.FilePath;
            UnityEngine.Debug.Log($"{LOG_PREFIX}   📄 Texture Path: {texturePath}");

            // FBX埋め込みテクスチャの場合、パスは "*0", "*1" などの形式
            if (texturePath.StartsWith("*"))
            {
                // 埋め込みテクスチャのインデックスを取得
                if (int.TryParse(texturePath.Substring(1), out int textureIndex))
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX}   🔍 EMBEDDED TEXTURE DETECTED");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      Index: {textureIndex}");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      Available textures in scene: {currentScene.TextureCount}");

                    // Assimp Scene から埋め込みテクスチャを取得
                    if (textureIndex >= 0 && textureIndex < currentScene.TextureCount)
                    {
                        Assimp.EmbeddedTexture embeddedTexture = currentScene.Textures[textureIndex];

                        if (embeddedTexture != null)
                        {
                            return LoadEmbeddedTexture(embeddedTexture, textureIndex);
                        }
                        else
                        {
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}   ✗ EmbeddedTexture at index {textureIndex} is null!");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}   ✗ TEXTURE INDEX OUT OF RANGE!");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      Requested: {textureIndex}");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      Available: 0-{currentScene.TextureCount - 1}");
                    }
                }
            }
            else
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX}   ℹ External texture file (not embedded): {texturePath}");
                return LoadExternalTexture(texturePath);
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX}   ✗ Texture extraction failed");
            return null;
        }

        /// <summary>
        /// Assimpの埋め込みテクスチャをUnity Texture2Dに変換
        /// </summary>
        private UnityEngine.Texture2D LoadEmbeddedTexture(Assimp.EmbeddedTexture embeddedTexture, int textureIndex)
        {
            UnityEngine.Debug.Log($"{LOG_PREFIX}      ┌────────────────────────────────────────────────");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      │ 🖼 LOADING EMBEDDED TEXTURE [{textureIndex}]");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      ├────────────────────────────────────────────────");

            try
            {
                // テクスチャ基本情報を表示
                UnityEngine.Debug.Log($"{LOG_PREFIX}      │ ℹ Basic Information:");
                UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Width:  {embeddedTexture.Width}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Height: {embeddedTexture.Height}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Format: {embeddedTexture.CompressedFormatHint}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Compressed: {embeddedTexture.IsCompressed}");

                // 圧縮されたテクスチャ（PNG, JPGなど）の場合
                if (embeddedTexture.IsCompressed)
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      ├────────────────────────────────────────────────");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │ 🔄 Processing COMPRESSED texture");

                    // 圧縮データを取得
                    byte[] compressedData = embeddedTexture.CompressedData;

                    if (compressedData != null && compressedData.Length > 0)
                    {
                        UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Data size: {compressedData.Length} bytes ({compressedData.Length / 1024.0:F2} KB)");

                        // Unity Texture2D を作成
                        UnityEngine.Texture2D texture = new UnityEngine.Texture2D(2, 2);

                        // LoadImage でバイトデータから画像をロード
                        UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Calling Texture2D.LoadImage()...");
                        bool loadSuccess = texture.LoadImage(compressedData);

                        if (loadSuccess)
                        {
                            texture.name = $"EmbeddedTexture_{textureIndex}";
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │ ✓ SUCCESS - Compressed texture loaded!");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Name: {texture.name}");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Final dimensions: {texture.width}x{texture.height}");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Format: {texture.format}");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                            return texture;
                        }
                        else
                        {
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      │");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      │ ✗ FAILED - Texture2D.LoadImage() returned false");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Data size was: {compressedData.Length} bytes");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Format hint: {embeddedTexture.CompressedFormatHint}");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │ ✗ FAILED - Compressed data is null or empty");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   CompressedData: {(compressedData == null ? "null" : $"{compressedData.Length} bytes")}");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                    }
                }
                else
                {
                    // 非圧縮テクスチャ（RAWデータ）の場合
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      ├────────────────────────────────────────────────");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │ 🔄 Processing UNCOMPRESSED (RAW) texture");

                    if (embeddedTexture.HasNonCompressedData)
                    {
                        int width = (int)embeddedTexture.Width;
                        int height = (int)embeddedTexture.Height;
                        int expectedPixels = width * height;

                        UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Dimensions: {width}x{height}");
                        UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Expected pixels: {expectedPixels}");

                        UnityEngine.Texture2D texture = new UnityEngine.Texture2D(width, height, UnityEngine.TextureFormat.RGBA32, false);

                        // Assimpのテクセルデータを取得
                        var texels = embeddedTexture.NonCompressedData;

                        if (texels != null && texels.Length == expectedPixels)
                        {
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Texel array length: {texels.Length} (✓ matches expected)");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Converting texels to Unity Color32...");

                            UnityEngine.Color32[] pixels = new UnityEngine.Color32[expectedPixels];

                            for (int i = 0; i < texels.Length; i++)
                            {
                                var texel = texels[i];
                                pixels[i] = new UnityEngine.Color32(texel.R, texel.G, texel.B, texel.A);
                            }

                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Applying pixels to texture...");
                            texture.SetPixels32(pixels);
                            texture.Apply();
                            texture.name = $"EmbeddedTexture_{textureIndex}";

                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │ ✓ SUCCESS - Uncompressed texture loaded!");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Name: {texture.name}");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Dimensions: {width}x{height}");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Format: {texture.format}");
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                            return texture;
                        }
                        else
                        {
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      │");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      │ ✗ FAILED - Invalid texel data");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Texels: {(texels == null ? "null" : $"{texels.Length} elements")}");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Expected: {expectedPixels} elements");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │ ✗ FAILED - No uncompressed data available");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   HasNonCompressedData: {embeddedTexture.HasNonCompressedData}");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      │");
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      │ ✗ EXCEPTION occurred during texture loading");
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Message: {ex.Message}");
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Type: {ex.GetType().Name}");
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      └────────────────────────────────────────────────");
            }

            UnityEngine.Debug.LogError($"{LOG_PREFIX}      ✗ Texture loading FAILED - returning null");
            return null;
        }

        /// <summary>
        /// 外部テクスチャファイルをロード（FBXファイルと同じディレクトリから検索）
        /// </summary>
        private UnityEngine.Texture2D LoadExternalTexture(string texturePath)
        {
            UnityEngine.Debug.Log($"{LOG_PREFIX}      ┌────────────────────────────────────────────────");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      │ 🔍 LOADING EXTERNAL TEXTURE");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      ├────────────────────────────────────────────────");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      │ Original path: {texturePath}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}      │ FBX directory: {fbxDirectory}");

            try
            {
                // テクスチャパスを解析（相対パス、絶対パス、ファイル名のみに対応）
                string fullPath = null;

                // パターン1: 絶対パスとして存在確認
                if (File.Exists(texturePath))
                {
                    fullPath = texturePath;
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │ Found (absolute path): {fullPath}");
                }
                // パターン2: FBXディレクトリ + 相対パス
                else if (!string.IsNullOrEmpty(fbxDirectory))
                {
                    string relativePath = Path.Combine(fbxDirectory, texturePath);
                    if (File.Exists(relativePath))
                    {
                        fullPath = relativePath;
                        UnityEngine.Debug.Log($"{LOG_PREFIX}      │ Found (relative to FBX): {fullPath}");
                    }
                    else
                    {
                        // パターン3: ファイル名のみで検索（パスの最後の部分を使用）
                        string fileName = Path.GetFileName(texturePath);
                        string fileNamePath = Path.Combine(fbxDirectory, fileName);
                        if (File.Exists(fileNamePath))
                        {
                            fullPath = fileNamePath;
                            UnityEngine.Debug.Log($"{LOG_PREFIX}      │ Found (filename only): {fullPath}");
                        }
                        else
                        {
                            // パターン4: Texturesサブディレクトリも検索
                            string texturesDir = Path.Combine(fbxDirectory, "Textures");
                            if (Directory.Exists(texturesDir))
                            {
                                string texturesDirPath = Path.Combine(texturesDir, fileName);
                                if (File.Exists(texturesDirPath))
                                {
                                    fullPath = texturesDirPath;
                                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │ Found (in Textures/): {fullPath}");
                                }
                            }
                        }
                    }
                }

                if (fullPath == null || !File.Exists(fullPath))
                {
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      │ ✗ FAILED - Texture file not found");
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Searched paths:");
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   - {texturePath}");
                    if (!string.IsNullOrEmpty(fbxDirectory))
                    {
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   - {Path.Combine(fbxDirectory, texturePath)}");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   - {Path.Combine(fbxDirectory, Path.GetFileName(texturePath))}");
                        UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   - {Path.Combine(fbxDirectory, "Textures", Path.GetFileName(texturePath))}");
                    }
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                    return null;
                }

                // ファイルを読み込み
                byte[] imageData = File.ReadAllBytes(fullPath);
                UnityEngine.Debug.Log($"{LOG_PREFIX}      │   File size: {imageData.Length} bytes ({imageData.Length / 1024.0:F2} KB)");

                // Texture2Dを作成
                UnityEngine.Texture2D texture = new UnityEngine.Texture2D(2, 2);

                // LoadImageでバイトデータから画像をロード
                UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Calling Texture2D.LoadImage()...");
                bool loadSuccess = texture.LoadImage(imageData);

                if (loadSuccess)
                {
                    texture.name = Path.GetFileNameWithoutExtension(fullPath);
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │ ✓ SUCCESS - External texture loaded!");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Name: {texture.name}");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Dimensions: {texture.width}x{texture.height}");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      │   Format: {texture.format}");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                    return texture;
                }
                else
                {
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      │");
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      │ ✗ FAILED - Texture2D.LoadImage() returned false");
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   File: {fullPath}");
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Size: {imageData.Length} bytes");
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}      └────────────────────────────────────────────────");
                    UnityEngine.Object.Destroy(texture);
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      │");
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      │ ✗ EXCEPTION - {ex.GetType().Name}");
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      │   Message: {ex.Message}");
                UnityEngine.Debug.LogError($"{LOG_PREFIX}      └────────────────────────────────────────────────");
            }

            return null;
        }

        /// <summary>
        /// デフォルトマテリアルを作成（一時的なプレースホルダー）
        /// </summary>
        private UnityEngine.Material CreateDefaultMaterial(string materialName)
        {
            UnityEngine.Shader shader = GetCachedShader("Standard");
            if (shader == null)
            {
                shader = GetCachedShader("Unlit/Color");
            }

            var material = new UnityEngine.Material(shader);
            material.name = $"{materialName}_Default";
            material.color = UnityEngine.Color.gray; // グレーで識別しやすく

            return material;
        }

        /// <summary>
        /// AssimpSceneCacheを使用してマテリアルを一括割り当て
        /// RuntimeMaterialManagerを使用してUniSIL統合マテリアル再構築を行う
        /// </summary>
        private async UniTask AssignMaterialsFromCache(GameObject rootObject)
        {
            if (rootObject == null)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [AssignMaterials] rootObject is null");
                return;
            }

            if (assimpSceneCache == null)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [AssignMaterials] assimpSceneCache is null, skipping material assignment");
                return;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} [AssignMaterials] === START ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX} [AssignMaterials] Using AssimpSceneCache with {assimpSceneCache.materials.Count} materials");

            // RuntimeMaterialManagerを作成
            var materialManager = new RuntimeMaterialManager();

            // 解凍先パス（FBXディレクトリまたはUnityPackage解凍先）
            string extractedPath = fbxDirectory;

            // MaterialManifestが見つかっている場合、そのディレクトリを優先
            string extractRootDir = FindExtractRootDirectory(fbxDirectory);
            if (!string.IsNullOrEmpty(extractRootDir))
            {
                extractedPath = extractRootDir;
                UnityEngine.Debug.Log($"{LOG_PREFIX} [AssignMaterials] Using extract root: {extractedPath}");
            }

            // RuntimeMaterialManager.AssignMaterials()を使用して一括割り当て
            await materialManager.AssignMaterials(rootObject, extractedPath, meshNodeToMaterialNames);

            UnityEngine.Debug.Log($"{LOG_PREFIX} [AssignMaterials] === END ===");

            // ログを出力（デバッグ用）
            string log = materialManager.GetCombinedLog();
            if (!string.IsNullOrEmpty(log))
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} [AssignMaterials] Combined Log:\n{log}");
            }
        }

        /// <summary>
        /// MaterialManifestをロード（UniSIL統合）
        /// </summary>
        private void LoadMaterialManifests()
        {
            UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] === START ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] FBX directory: {fbxDirectory}");

            // 解凍先ルートディレクトリを検索（最大5階層上まで遡る）
            string extractRootDir = FindExtractRootDirectory(fbxDirectory);

            if (string.IsNullOrEmpty(extractRootDir))
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [LoadMaterialManifests] Could not find extract root directory from: {fbxDirectory}");
                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] MaterialManifest not found, using fallback material creation");
                return;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] Extract root directory: {extractRootDir}");

            // 検索対象ディレクトリ（解凍ルートを優先）
            List<string> searchDirs = new List<string>
            {
                extractRootDir,                                    // 解凍ルート（最優先）
                Path.Combine(extractRootDir, "Material"),         // ルート/Material
                Path.Combine(extractRootDir, "Materials"),        // ルート/Materials
                fbxDirectory                                       // FBXと同じディレクトリ
            };

            UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] Searching {searchDirs.Count} directories for MaterialManifest.json");

            foreach (string searchDir in searchDirs)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests]   Checking: {searchDir}");

                if (!Directory.Exists(searchDir))
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests]     Directory does not exist");
                    continue;
                }

                string manifestPath = Path.Combine(searchDir, "MaterialManifest.json");
                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests]     Looking for: {manifestPath}");

                if (File.Exists(manifestPath))
                {
                    try
                    {
                        string json = File.ReadAllText(manifestPath);
                        MaterialManifest manifest = JsonUtility.FromJson<MaterialManifest>(json);

                        if (manifest != null)
                        {
                            cachedMaterialManifest = manifest;
                            UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] ✓ Loaded MaterialManifest: {manifest.materialCount} materials from {searchDir}");

                            // マテリアル名→エントリのマッピングを構築
                            materialNameToEntry.Clear();
                            if (manifest.materials != null)
                            {
                                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] Building material name dictionary from {manifest.materials.Count} entries");
                                foreach (var entry in manifest.materials)
                                {
                                    if (entry != null && !string.IsNullOrEmpty(entry.name))
                                    {
                                        materialNameToEntry[entry.name] = entry;
                                        UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests]   Registered: {entry.name}");
                                    }
                                }
                                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] ✓ Dictionary built with {materialNameToEntry.Count} materials");
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [LoadMaterialManifests] manifest.materials is null!");
                            }

                            UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] === END (SUCCESS) ===");
                            return; // 最初に見つかったManifestを使用
                        }
                    }
                    catch (System.Exception ex)
                    {
                        UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [UniSIL] Failed to load MaterialManifest: {ex.Message}");
                    }
                }
                else
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests]     File does not exist");
                }
            }

            UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [LoadMaterialManifests] MaterialManifest not found in any search directory");
            UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMaterialManifests] === END (NOT FOUND) ===");
        }

        /// <summary>
        /// 指定ディレクトリから最大5階層上まで遡り、MaterialManifest.jsonまたはTextureManifest.jsonが存在するディレクトリを返す
        /// </summary>
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
                    UnityEngine.Debug.Log($"{LOG_PREFIX} [FindExtractRoot] Found manifest at level {level}: {currentDir}");
                    return currentDir;
                }

                // 親ディレクトリへ移動
                string parentDir = Directory.GetParent(currentDir)?.FullName;
                if (string.IsNullOrEmpty(parentDir) || parentDir == currentDir)
                {
                    // これ以上親ディレクトリがない
                    break;
                }

                currentDir = parentDir;
            }

            UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [FindExtractRoot] No manifest found within {MAX_LEVELS} levels from {startDirectory}");
            return null;
        }

        /// <summary>
        /// マテリアルを作成（UniSIL統合版）
        /// 戦略0: MaterialManifestから.matファイルを検索→UniSILで再構築
        /// 戦略1: Assimpの埋め込みテクスチャを使用
        /// 戦略2: フォールバック（lilToon/Standard）
        /// </summary>
        /// <param name="nodeName">ノード名</param>
        /// <param name="assimpMaterial">Assimpマテリアル</param>
        /// <param name="assimpMaterialIndex">Assimpマテリアルインデックス</param>
        private UnityEngine.Material CreateMaterialWithShaderDB(string nodeName, Assimp.Material assimpMaterial, int assimpMaterialIndex)
        {
            UnityEngine.Material material = null;
            string materialName = assimpMaterial?.Name ?? nodeName;

            UnityEngine.Debug.Log($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] === START ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] Creating material for: {materialName}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] cachedMaterialManifest: {(cachedMaterialManifest != null ? "EXISTS" : "NULL")}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] assimpMaterial: {(assimpMaterial != null ? "EXISTS" : "NULL")}");

            if (cachedMaterialManifest != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] materialNameToEntry.Count: {materialNameToEntry.Count}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] materialNameToEntry keys: {string.Join(", ", materialNameToEntry.Keys)}");
            }

            // 戦略0: MaterialManifestを使用してUniSILで再構築
            if (cachedMaterialManifest != null && assimpMaterial != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] Attempting Strategy 0 (UniSIL MaterialManifest)");
                UnityEngine.Debug.Log($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] Looking for material: '{assimpMaterial.Name}'");

                if (materialNameToEntry.TryGetValue(assimpMaterial.Name, out MaterialManifest.MaterialEntry entry))
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX}   [UniSIL Strategy 0] ✓ Found in MaterialManifest: {entry.name}");
                    UnityEngine.Debug.Log($"{LOG_PREFIX}     Shader: {entry.shaderName}");

                    material = CreateMaterialFromManifestEntry(entry);

                    if (material != null)
                    {
                        UnityEngine.Debug.Log($"{LOG_PREFIX}     ✓ Material reconstructed with UniSIL");
                        return material;
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"{LOG_PREFIX}     ✗ CreateMaterialFromManifestEntry returned null");
                    }
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   [UniSIL Strategy 0] ✗ Material '{assimpMaterial.Name}' not found in materialNameToEntry");
                }
            }
            else
            {
                if (cachedMaterialManifest == null)
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] Skipping Strategy 0: cachedMaterialManifest is null");
                if (assimpMaterial == null)
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   [CreateMaterialWithShaderDB] Skipping Strategy 0: assimpMaterial is null");
            }

            // 戦略1: Assimpの埋め込みテクスチャを使用
            if (assimpMaterial != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX}   [Strategy 1] Using Assimp embedded textures");

                // lilToonまたはStandardシェーダーを使用
                UnityEngine.Shader shader = GetCachedShader("lilToon");
                if (shader == null)
                {
                    shader = GetCachedShader("Standard");
                }

                if (shader != null)
                {
                    material = new UnityEngine.Material(shader);
                    material.name = materialName;
                    UnityEngine.Debug.Log($"{LOG_PREFIX}     ✓ Material created with shader: {shader.name}");

                    // 埋め込みテクスチャを適用
                    ApplyEmbeddedTextures(material, assimpMaterial);
                    return material;
                }
            }

            // 戦略2: フォールバック
            UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   [Strategy 2] Fallback - creating default material");
            material = CreateLilToonMaterial(nodeName, assimpMaterialIndex);

            return material;
        }

        /// <summary>
        /// MaterialManifestのエントリから.matファイルを読み込んでUniSILで再構築
        /// </summary>
        private UnityEngine.Material CreateMaterialFromManifestEntry(MaterialManifest.MaterialEntry entry)
        {
            try
            {
                // 解凍先ルートディレクトリを検索（最大5階層上まで遡る）
                string extractRootDir = FindExtractRootDirectory(fbxDirectory);

                if (string.IsNullOrEmpty(extractRootDir))
                {
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX}     [UniSIL] Extract root directory not found, using fbxDirectory");
                    extractRootDir = fbxDirectory;
                }

                // 検索対象ディレクトリ（解凍ルートを優先）
                List<string> searchDirs = new List<string>
                {
                    Path.Combine(extractRootDir, "Material"),
                    Path.Combine(extractRootDir, "Materials"),
                    fbxDirectory,
                    extractRootDir
                };

                foreach (string searchDir in searchDirs)
                {
                    if (!Directory.Exists(searchDir))
                        continue;

                    string matPath = Path.Combine(searchDir, entry.name + ".mat");
                    if (File.Exists(matPath))
                    {
                        UnityEngine.Debug.Log($"{LOG_PREFIX}     [UniSIL] Found .mat file: {matPath}");

                        // YAMLパース
                        string yamlText = File.ReadAllText(matPath);
                        MaterialData materialData = YAMLMaterialParser.Parse(yamlText);

                        if (materialData == null || !materialData.IsValid())
                        {
                            UnityEngine.Debug.LogWarning($"{LOG_PREFIX}     [UniSIL] Failed to parse .mat file");
                            continue;
                        }

                        UnityEngine.Debug.Log($"{LOG_PREFIX}     [UniSIL] Parsed material: {materialData.name}");

                        // テクスチャパスを絶対パスに変換（MaterialDataのテクスチャGUIDからパスを解決）
                        ConvertTexturePathsToAbsolute(materialData, searchDir);

                        // ShaderDatabaseをロード
                        UnityEngine.Debug.Log($"{LOG_PREFIX}     [UniSIL] Loading ShaderDatabase...");
                        var shaderDB = ShaderDBLoader.LoadDatabase();

                        if (shaderDB == null)
                        {
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}     [UniSIL] ShaderDatabase not found - LoadDatabase() returned null");
                            continue;
                        }

                        if (shaderDB.shaders == null)
                        {
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}     [UniSIL] ShaderDatabase.shaders is null - asset may be corrupted");
                            UnityEngine.Debug.LogError($"{LOG_PREFIX}     Please regenerate ShaderDB.asset");
                            continue;
                        }

                        UnityEngine.Debug.Log($"{LOG_PREFIX}     [UniSIL] ShaderDatabase loaded: {shaderDB.shaders.Count} shaders");

                        // シェーダー取得の3層戦略
                        UnityEngine.Shader shader = null;
                        string shaderSource = null;

                        // 戦略1: GUID直接lookup (ShaderGuidDictionary)
                        if (!string.IsNullOrEmpty(materialData.shaderGuid))
                        {
                            UnityEngine.Debug.Log($"{LOG_PREFIX}     [Strategy 1] Attempting GUID lookup: {materialData.shaderGuid}");
                            var shaderGuidDict = ShaderGuidDictionaryLoader.LoadDictionary();

                            if (shaderGuidDict != null)
                            {
                                string shaderName = shaderGuidDict.GetShaderNameByGuid(materialData.shaderGuid);
                                if (!string.IsNullOrEmpty(shaderName))
                                {
                                    shader = UnityEngine.Shader.Find(shaderName);
                                    if (shader != null)
                                    {
                                        UnityEngine.Debug.Log($"{LOG_PREFIX}       ✓ Found shader by GUID: {shaderName}");
                                        shaderSource = "GUID Lookup";
                                    }
                                    else
                                    {
                                        UnityEngine.Debug.LogWarning($"{LOG_PREFIX}       ✗ Shader name found but Shader.Find() failed: {shaderName}");
                                    }
                                }
                            }
                        }

                        // 戦略2: ShaderInferenceEngineでの推論
                        if (shader == null)
                        {
                            UnityEngine.Debug.Log($"{LOG_PREFIX}     [Strategy 2] Falling back to shader inference");
                            var config = new InferenceConfig();
                            var inferenceEngine = new ShaderInferenceEngine(shaderDB, config);
                            ShaderInferenceResult inferenceResult;

                            if (!string.IsNullOrEmpty(materialData.shaderGuid))
                            {
                                inferenceResult = inferenceEngine.InferShaderWithGuid(materialData, materialData.shaderGuid);
                            }
                            else
                            {
                                inferenceResult = inferenceEngine.InferShader(materialData);
                            }

                            UnityEngine.Debug.Log($"{LOG_PREFIX}       Inferred: {inferenceResult.inferredShader} (confidence: {inferenceResult.confidence:P2})");
                            shader = UnityEngine.Shader.Find(inferenceResult.inferredShader);

                            if (shader != null)
                            {
                                shaderSource = $"Inference ({inferenceResult.confidence:P2})";
                                UnityEngine.Debug.Log($"{LOG_PREFIX}       ✓ Inference succeeded");
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning($"{LOG_PREFIX}       ✗ Inferred shader not found: {inferenceResult.inferredShader}");
                            }
                        }

                        // 戦略3: フォールバック (lilToon → Standard)
                        if (shader == null)
                        {
                            UnityEngine.Debug.LogWarning($"{LOG_PREFIX}     [Strategy 3] All shader lookup failed, using fallback");
                            shader = GetCachedShader("lilToon");

                            if (shader != null)
                            {
                                shaderSource = "Fallback (lilToon)";
                                UnityEngine.Debug.Log($"{LOG_PREFIX}       Using lilToon fallback");
                            }
                            else
                            {
                                shader = GetCachedShader("Standard");
                                shaderSource = "Fallback (Standard)";
                                UnityEngine.Debug.Log($"{LOG_PREFIX}       Using Standard fallback");
                            }
                        }

                        // Materialを作成
                        var material = new UnityEngine.Material(shader);
                        material.name = materialData.name;

                        UnityEngine.Debug.Log($"{LOG_PREFIX}     ✓ Material created: {material.name} with shader: {shader.name}");
                        UnityEngine.Debug.Log($"{LOG_PREFIX}       Shader source: {shaderSource}");

                        // MaterialReconstructorでプロパティとテクスチャを適用
                        UnityEngine.Debug.Log($"{LOG_PREFIX}     Applying properties and textures...");
                        var reconstructor = new MaterialReconstructor();

                        try
                        {
                            // Note: ReconstructMaterialは新しいマテリアルを返すので、
                            // 既存のマテリアルにプロパティをコピーする必要がある
                            var tempMaterial = reconstructor.ReconstructMaterial(materialData, new ShaderInferenceResult { inferredShader = shader.name, confidence = 1.0f });

                            if (tempMaterial != null)
                            {
                                // プロパティとテクスチャをコピー
                                material.CopyPropertiesFromMaterial(tempMaterial);
                                UnityEngine.Debug.Log($"{LOG_PREFIX}     ✓ Properties and textures applied");
                            }
                            else
                            {
                                UnityEngine.Debug.LogWarning($"{LOG_PREFIX}     ⚠ ReconstructMaterial returned null, using material with shader only");
                            }
                        }
                        catch (System.Exception propEx)
                        {
                            UnityEngine.Debug.LogWarning($"{LOG_PREFIX}     ⚠ Failed to apply properties: {propEx.Message}");
                            UnityEngine.Debug.LogWarning($"{LOG_PREFIX}       Using material with shader only");
                        }

                        return material;
                    }
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX}     [UniSIL] Error reconstructing material: {ex.Message}");
                UnityEngine.Debug.LogError($"{LOG_PREFIX}     Stack trace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}     Inner exception: {ex.InnerException.Message}");
                    UnityEngine.Debug.LogError($"{LOG_PREFIX}     Inner stack trace: {ex.InnerException.StackTrace}");
                }
            }

            return null;
        }

        /// <summary>
        /// MaterialDataのテクスチャGUIDをパスに解決（TextureManifest使用）
        /// 注: UniSILのTexturePropertyにはpathフィールドがなくguidのみ
        /// MaterialReconstructorが内部でTextureLoaderを使ってGUIDから自動的にロードする
        /// </summary>
        private void ConvertTexturePathsToAbsolute(MaterialData materialData, string baseDirectory)
        {
            // MaterialReconstructorが内部でTextureLoaderを使用するため、
            // ここでは何もする必要がない
            // TextureLoaderがTextureManifest.jsonを自動的に読み込んでGUID→パス解決を行う

            UnityEngine.Debug.Log($"{LOG_PREFIX}     [UniSIL] MaterialReconstructor will handle texture loading via TextureLoader");
        }

        /// <summary>
        /// 埋め込みテクスチャをマテリアルに適用
        /// </summary>
        /// <param name="material">Unity Material</param>
        /// <param name="assimpMaterial">Assimp Material</param>
        private void ApplyEmbeddedTextures(UnityEngine.Material material, Assimp.Material assimpMaterial)
        {
            UnityEngine.Debug.Log($"{LOG_PREFIX}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} ┌══════════════════════════════════════════════════════════");
            UnityEngine.Debug.Log($"{LOG_PREFIX} │ 🎨 APPLYING EMBEDDED TEXTURES TO MATERIAL");
            UnityEngine.Debug.Log($"{LOG_PREFIX} ├══════════════════════════════════════════════════════════");
            UnityEngine.Debug.Log($"{LOG_PREFIX} │ ℹ Material: {material.name}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} │   Shader: {material.shader.name}");
            UnityEngine.Debug.Log($"{LOG_PREFIX} │");

            // Diffuseテクスチャを抽出して適用
            UnityEngine.Texture2D diffuseTexture = ExtractTextureFromAssimpMaterial(assimpMaterial, Assimp.TextureType.Diffuse);

            if (diffuseTexture != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} ├──────────────────────────────────────────────────────────");
                UnityEngine.Debug.Log($"{LOG_PREFIX} │ ✓ Diffuse texture extracted successfully");
                UnityEngine.Debug.Log($"{LOG_PREFIX} │   Name: {diffuseTexture.name}");
                UnityEngine.Debug.Log($"{LOG_PREFIX} │   Dimensions: {diffuseTexture.width}x{diffuseTexture.height}");
                UnityEngine.Debug.Log($"{LOG_PREFIX} │   Format: {diffuseTexture.format}");
                UnityEngine.Debug.Log($"{LOG_PREFIX} │");
                UnityEngine.Debug.Log($"{LOG_PREFIX} │ 🔍 Checking material properties...");

                // _MainTexプロパティに設定（lilToon, Standard, URP Litなど）
                if (material.HasProperty("_MainTex"))
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │   ✓ Material has '_MainTex' property");
                    material.SetTexture("_MainTex", diffuseTexture);
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │   ✓ Texture assigned to '_MainTex'");
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │");
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │ ✅ SUCCESS - Texture applied to material!");
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │   Property: _MainTex");
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │   Texture: {diffuseTexture.name} ({diffuseTexture.width}x{diffuseTexture.height})");
                }
                // _BaseMapプロパティに設定（URP）
                else if (material.HasProperty("_BaseMap"))
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │   ✓ Material has '_BaseMap' property (URP)");
                    material.SetTexture("_BaseMap", diffuseTexture);
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │   ✓ Texture assigned to '_BaseMap'");
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │");
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │ ✅ SUCCESS - Texture applied to material!");
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │   Property: _BaseMap");
                    UnityEngine.Debug.Log($"{LOG_PREFIX} │   Texture: {diffuseTexture.name} ({diffuseTexture.width}x{diffuseTexture.height})");
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} │");
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} │ ⚠ WARNING - No compatible texture property found!");
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} │   Shader: {material.shader.name}");
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} │   Missing: '_MainTex' and '_BaseMap' properties");
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} │   Texture extracted but could not be applied");
                }
                UnityEngine.Debug.Log($"{LOG_PREFIX} └══════════════════════════════════════════════════════════");
            }
            else
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} ├──────────────────────────────────────────────────────────");
                UnityEngine.Debug.Log($"{LOG_PREFIX} │ ℹ No diffuse texture found in material");
                UnityEngine.Debug.Log($"{LOG_PREFIX} │   Material will use shader's default color/texture");
                UnityEngine.Debug.Log($"{LOG_PREFIX} └══════════════════════════════════════════════════════════");
            }
        }

        /// <summary>
        /// lilToonシェーダーを使用したマテリアルを作成（フォールバック用）
        /// </summary>
        /// <param name="nodeName">ノード名</param>
        /// <param name="assimpMaterialIndex">Assimpマテリアルインデックス（-1の場合はマテリアル情報なし）</param>
        private UnityEngine.Material CreateLilToonMaterial(string nodeName, int assimpMaterialIndex = -1)
        {
            // lilToonシェーダーを検索（キャッシュ使用）
            UnityEngine.Shader lilToonShader = GetCachedShader("lilToon");

            if (lilToonShader == null)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} lilToon shader not found, using Standard shader instead");
                lilToonShader = GetCachedShader("Standard");
            }

            UnityEngine.Material material = new UnityEngine.Material(lilToonShader);
            material.name = $"{nodeName}_Material";

            // Assimpマテリアルからテクスチャを抽出
            if (assimpMaterialIndex >= 0 && currentScene != null && assimpMaterialIndex < currentScene.MaterialCount)
            {
                Assimp.Material assimpMaterial = currentScene.Materials[assimpMaterialIndex];

                UnityEngine.Debug.Log($"{LOG_PREFIX}   Processing Assimp material[{assimpMaterialIndex}]: {assimpMaterial.Name}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}     HasTextureDiffuse: {assimpMaterial.HasTextureDiffuse}");
                UnityEngine.Debug.Log($"{LOG_PREFIX}     TextureDiffuse count: {assimpMaterial.GetMaterialTextureCount(Assimp.TextureType.Diffuse)}");

                // Diffuseテクスチャを抽出
                UnityEngine.Texture2D diffuseTexture = ExtractTextureFromAssimpMaterial(assimpMaterial, Assimp.TextureType.Diffuse);

                if (diffuseTexture != null)
                {
                    // lilToonの場合は_MainTexにテクスチャを設定
                    if (material.HasProperty("_MainTex"))
                    {
                        material.SetTexture("_MainTex", diffuseTexture);
                        UnityEngine.Debug.Log($"{LOG_PREFIX}     ✓ Applied diffuse texture to _MainTex: {diffuseTexture.name} ({diffuseTexture.width}x{diffuseTexture.height})");
                    }
                    else if (material.HasProperty("_BaseMap"))
                    {
                        material.SetTexture("_BaseMap", diffuseTexture);
                        UnityEngine.Debug.Log($"{LOG_PREFIX}     ✓ Applied diffuse texture to _BaseMap: {diffuseTexture.name} ({diffuseTexture.width}x{diffuseTexture.height})");
                    }
                }
                else
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX}     No diffuse texture extracted, using default white color");
                }

                // マテリアルのカラー情報も取得
                if (assimpMaterial.HasColorDiffuse)
                {
                    var diffuseColor = assimpMaterial.ColorDiffuse;
                    UnityEngine.Color color = new UnityEngine.Color(diffuseColor.R, diffuseColor.G, diffuseColor.B, diffuseColor.A);

                    if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", color);
                    }
                    else if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", color);
                    }

                    UnityEngine.Debug.Log($"{LOG_PREFIX}     Applied diffuse color: {color}");
                }
            }
            else
            {
                // デフォルトカラーを設定（白）
                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", UnityEngine.Color.white);
                }
                else if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", UnityEngine.Color.white);
                }

                UnityEngine.Debug.Log($"{LOG_PREFIX}   No Assimp material, using default white color");
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX}   Created material: {material.name} with shader: {lilToonShader.name}");

            return material;
        }
    }
}
