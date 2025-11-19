using UnityEngine;
using Assimp;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;

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
                if (node.MeshCount > 0)
                {
                    int assimpMeshIndex = node.MeshIndices[0];
                    Assimp.Mesh assimpMesh = currentScene.Meshes[assimpMeshIndex];
                    materialIndex = assimpMesh.MaterialIndex;

                    if (materialIndex >= 0 && materialIndex < currentScene.MaterialCount)
                    {
                        assimpMaterial = currentScene.Materials[materialIndex];
                        UnityEngine.Debug.Log($"{LOG_PREFIX}   Mesh material index: {materialIndex}, Material name: {assimpMaterial.Name}");
                    }
                }

                // RuntimeMaterialManagerを使用してマテリアルを作成（ShaderDB対応）
                UnityEngine.Material material = CreateMaterialWithShaderDB(node.Name, assimpMaterial, materialIndex);
                UnityEngine.Material[] materials = new UnityEngine.Material[] { material };

                // SkinnedMeshRenderer 構築
                SkinnedMeshRenderer smr = skinnedMeshBuilder.Build(meshData, boneData, rootBoneName, materials);
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
        /// マテリアルを作成し、埋め込みテクスチャを適用
        /// </summary>
        /// <param name="nodeName">ノード名</param>
        /// <param name="assimpMaterial">Assimpマテリアル</param>
        /// <param name="assimpMaterialIndex">Assimpマテリアルインデックス</param>
        private UnityEngine.Material CreateMaterialWithShaderDB(string nodeName, Assimp.Material assimpMaterial, int assimpMaterialIndex)
        {
            UnityEngine.Material material = null;

            // Assimpマテリアルからシェーダーを作成
            if (assimpMaterial != null)
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX}   Creating material for: {assimpMaterial.Name}");

                // lilToonまたはStandardシェーダーを使用
                UnityEngine.Shader shader = UnityEngine.Shader.Find("lilToon");
                if (shader == null)
                {
                    shader = UnityEngine.Shader.Find("Standard");
                }

                if (shader != null)
                {
                    material = new UnityEngine.Material(shader);
                    material.name = $"{nodeName}_Material";
                    UnityEngine.Debug.Log($"{LOG_PREFIX}     ✓ Material created with shader: {shader.name}");
                }
            }

            // マテリアルが作成されなかった場合はフォールバック
            if (material == null)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   Material creation failed, creating fallback lilToon material");
                material = CreateLilToonMaterial(nodeName, assimpMaterialIndex);
            }

            // 埋め込みテクスチャを適用
            if (assimpMaterial != null && material != null)
            {
                ApplyEmbeddedTextures(material, assimpMaterial);
            }

            return material;
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
            // lilToonシェーダーを検索
            UnityEngine.Shader lilToonShader = UnityEngine.Shader.Find("lilToon");

            if (lilToonShader == null)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} lilToon shader not found, using Standard shader instead");
                lilToonShader = UnityEngine.Shader.Find("Standard");
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
