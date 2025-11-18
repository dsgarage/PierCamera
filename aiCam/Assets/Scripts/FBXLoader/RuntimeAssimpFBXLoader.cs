using UnityEngine;
using Assimp;
using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;

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
        /// v0.3.2の成功ロジック: Transform階層に座標変換を適用してAポーズを維持
        /// </summary>
        private void SetTransformFromAssimpMatrix(Transform transform, Assimp.Matrix4x4 assimpMatrix)
        {
            // Assimpの行列を分解
            Assimp.Vector3D scale;
            Assimp.Quaternion rotation;
            Assimp.Vector3D position;
            assimpMatrix.Decompose(out scale, out rotation, out position);

            // 座標系変換を適用してUnityのTransformに設定（v0.3.2と同じ）
            transform.localPosition = FbxCoordinateSystemDetector.ConvertVector(position, coordinateConversionMatrix);
            transform.localRotation = FbxCoordinateSystemDetector.ConvertQuaternion(rotation, coordinateConversionMatrix);
            transform.localScale = new UnityEngine.Vector3(scale.X, scale.Y, scale.Z);
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

        /// <summary>
        /// STEP 6: bindposeをUnity Transformから計算する
        /// </summary>
        /// <param name="bones">ボーンTransform配列</param>
        /// <param name="rootBone">rootBone（Hips）</param>
        /// <returns>bindpose配列</returns>
        private UnityEngine.Matrix4x4[] CalculateBindPoses(Transform[] bones, Transform rootBone)
        {
            if (bones == null || bones.Length == 0)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [STEP 6] No bones provided for bindpose calculation");
                return new UnityEngine.Matrix4x4[0];
            }

            if (rootBone == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} [STEP 6] rootBone is null!");
                return new UnityEngine.Matrix4x4[0];
            }

            UnityEngine.Matrix4x4[] bindposes = new UnityEngine.Matrix4x4[bones.Length];

            // Unity期待値の公式: bindpose[i] = bones[i].worldToLocalMatrix * rootBone.localToWorldMatrix
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null)
                {
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [STEP 6] bones[{i}] is null, using identity matrix");
                    bindposes[i] = UnityEngine.Matrix4x4.identity;
                    continue;
                }

                bindposes[i] = bones[i].worldToLocalMatrix * rootBone.localToWorldMatrix;
            }

            return bindposes;
        }

        // メッシュロード用にrootBoneをキャッシュ（STEP 5）
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

            await UniTask.Yield();
        }

        /// <summary>
        /// 特定のノードに含まれる全メッシュをロードしてSkinnedMeshRendererを作成（非同期版）
        /// 必ずレンダラーコンポーネントを作成することを保証
        /// </summary>
        private async UniTask LoadMeshesForNodeAsync(Node node, Transform nodeTransform)
        {
            try
            {
                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMeshesForNode] START for node: {node.Name}");

                // 複数のメッシュがある場合はサブメッシュとして結合
                List<UnityEngine.Vector3> combinedVertices = new List<UnityEngine.Vector3>();
                List<UnityEngine.Vector3> combinedNormals = new List<UnityEngine.Vector3>();
                List<UnityEngine.Vector2> combinedUVs = new List<UnityEngine.Vector2>();
                List<int> combinedTriangles = new List<int>();
                List<UnityEngine.BoneWeight> combinedBoneWeights = new List<UnityEngine.BoneWeight>();
                int vertexOffset = 0;
                bool hasNormals = false;

            for (int meshIndex = 0; meshIndex < node.MeshCount; meshIndex++)
            {
                int assimpMeshIndex = node.MeshIndices[meshIndex];
                Assimp.Mesh assimpMesh = currentScene.Meshes[assimpMeshIndex];

                // 頂点データをロード（v0.3.2: 座標系変換を適用）
                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    Assimp.Vector3D vertex = assimpMesh.Vertices[i];
                    // Transform階層と同じ座標変換を適用（基準を一致させる）
                    UnityEngine.Vector3 unityVertex = FbxCoordinateSystemDetector.ConvertVector(vertex, coordinateConversionMatrix);
                    combinedVertices.Add(unityVertex);
                }

                // 法線データをロード（v0.3.2: 座標系変換を適用）
                if (assimpMesh.HasNormals)
                {
                    hasNormals = true;
                    for (int i = 0; i < assimpMesh.VertexCount; i++)
                    {
                        Assimp.Vector3D normal = assimpMesh.Normals[i];
                        // 法線も座標系変換を適用して正規化
                        UnityEngine.Vector3 unityNormal = FbxCoordinateSystemDetector.ConvertVector(normal, coordinateConversionMatrix);
                        combinedNormals.Add(unityNormal.normalized);
                    }
                }
                else
                {
                    // 法線がない場合はダミーを追加（後でRecalculateNormals）
                    for (int i = 0; i < assimpMesh.VertexCount; i++)
                    {
                        combinedNormals.Add(UnityEngine.Vector3.up);
                    }
                }

                // UVデータをロード（チャンネル0のみ）
                if (assimpMesh.HasTextureCoords(0))
                {
                    for (int i = 0; i < assimpMesh.VertexCount; i++)
                    {
                        Assimp.Vector3D uv = assimpMesh.TextureCoordinateChannels[0][i];
                        combinedUVs.Add(new UnityEngine.Vector2(uv.X, uv.Y));
                    }
                }
                else
                {
                    // UVがない場合はダミーを追加
                    for (int i = 0; i < assimpMesh.VertexCount; i++)
                    {
                        combinedUVs.Add(UnityEngine.Vector2.zero);
                    }
                }

                // 三角形インデックスをロード
                for (int i = 0; i < assimpMesh.FaceCount; i++)
                {
                    Assimp.Face face = assimpMesh.Faces[i];
                    if (face.IndexCount == 3)
                    {
                        // 頂点オフセットを加算してインデックスを調整
                        int idx0 = vertexOffset + face.Indices[0];
                        int idx1 = vertexOffset + face.Indices[1];
                        int idx2 = vertexOffset + face.Indices[2];

                        // 座標系変換で左手系・右手系が変わる場合、三角形の巻き順を反転
                        if (shouldFlipTriangleWinding)
                        {
                            combinedTriangles.Add(idx0);
                            combinedTriangles.Add(idx2); // 反転: idx1とidx2を入れ替え
                            combinedTriangles.Add(idx1);
                        }
                        else
                        {
                            combinedTriangles.Add(idx0);
                            combinedTriangles.Add(idx1);
                            combinedTriangles.Add(idx2);
                        }
                    }
                    else
                    {
                        UnityEngine.Debug.LogWarning($"{LOG_PREFIX}     Face {i} is not a triangle (indices: {face.IndexCount})");
                    }
                }

                vertexOffset += assimpMesh.VertexCount;

                // 各メッシュ処理後にフレームを譲る（重い処理のため）
                await UniTask.Yield();
            }

            // BoneWeightsをロード（最初のメッシュのボーン構造を使用）
            if (node.MeshCount > 0)
            {
                int assimpMeshIndex = node.MeshIndices[0];
                Assimp.Mesh assimpMesh = currentScene.Meshes[assimpMeshIndex];

                if (assimpMesh.HasBones)
                {
                    // 頂点数分のBoneWeight配列を初期化
                    UnityEngine.BoneWeight[] boneWeights = new UnityEngine.BoneWeight[combinedVertices.Count];

                    // 各ボーンのウェイトデータを処理
                    for (int boneIndex = 0; boneIndex < assimpMesh.BoneCount; boneIndex++)
                    {
                        Assimp.Bone bone = assimpMesh.Bones[boneIndex];

                        // このボーンが影響する全頂点を処理
                        foreach (var vertexWeight in bone.VertexWeights)
                        {
                            int vertexIndex = vertexWeight.VertexID;
                            float weight = vertexWeight.Weight;

                            // 頂点範囲チェック
                            if (vertexIndex >= boneWeights.Length)
                                continue;

                            // BoneWeightに追加（最大4つまで）- async methodsではrefが使えないので直接アクセス
                            UnityEngine.BoneWeight bw = boneWeights[vertexIndex];

                            if (bw.weight0 == 0f)
                            {
                                bw.boneIndex0 = boneIndex;
                                bw.weight0 = weight;
                            }
                            else if (bw.weight1 == 0f)
                            {
                                bw.boneIndex1 = boneIndex;
                                bw.weight1 = weight;
                            }
                            else if (bw.weight2 == 0f)
                            {
                                bw.boneIndex2 = boneIndex;
                                bw.weight2 = weight;
                            }
                            else if (bw.weight3 == 0f)
                            {
                                bw.boneIndex3 = boneIndex;
                                bw.weight3 = weight;
                            }

                            // 変更を配列に書き戻す
                            boneWeights[vertexIndex] = bw;
                        }
                    }

                    combinedBoneWeights.AddRange(boneWeights);
                    UnityEngine.Debug.Log($"{LOG_PREFIX}   BoneWeights: {boneWeights.Length} vertices");
                }
            }

            // Unity Meshを作成
            UnityEngine.Mesh unityMesh = new UnityEngine.Mesh();
            unityMesh.name = $"{node.Name}_Mesh";
            unityMesh.vertices = combinedVertices.ToArray();
            unityMesh.uv = combinedUVs.ToArray();
            unityMesh.triangles = combinedTriangles.ToArray();

            // 法線を設定
            if (hasNormals)
            {
                unityMesh.normals = combinedNormals.ToArray();
            }
            else
            {
                unityMesh.RecalculateNormals();
            }

            // バウンディングボックスを再計算
            unityMesh.RecalculateBounds();

                // STEP 3: BlendShapeを追加（SMR.sharedMeshをセットする前に必須）
                await AddBlendShapesToMesh(node, unityMesh);

                // STEP 4-7: SkinnedMeshRenderer のセットアップ
                await SetupSkinnedMeshRenderer(node, nodeTransform, unityMesh, combinedBoneWeights);

                UnityEngine.Debug.Log($"{LOG_PREFIX} [LoadMeshesForNode] SUCCESS for node: {node.Name}");
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
            List<UnityEngine.BoneWeight> boneWeights)
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

            // STEP 4: BoneTransforms（bones[]）の構築
            Transform[] bones = BuildBonesArray(assimpMesh);
            if (bones == null || bones.Length == 0)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to build bones array");
                return;
            }
            UnityEngine.Debug.Log($"{LOG_PREFIX} STEP 4: Bones array built: {bones.Length} bones");

            // STEP 5: BoneWeights の設定
            if (boneWeights.Count > 0)
            {
                mesh.boneWeights = boneWeights.ToArray();
                UnityEngine.Debug.Log($"{LOG_PREFIX} STEP 5: BoneWeights assigned: {boneWeights.Count}");
            }
            else
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} No bone weights found");
            }

            // STEP 6: BindPose（bindposes）の生成
            UnityEngine.Matrix4x4[] bindposes = BuildBindPoses(bones);
            if (bindposes == null || bindposes.Length == 0)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} Failed to build bindposes");
                return;
            }
            mesh.bindposes = bindposes;
            UnityEngine.Debug.Log($"{LOG_PREFIX} STEP 6: BindPoses built: {bindposes.Length}");

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
        /// STEP 4: aiMesh.Bones から Transform[] bones を構築
        /// </summary>
        private Transform[] BuildBonesArray(Assimp.Mesh assimpMesh)
        {
            if (!assimpMesh.HasBones)
                return null;

            Transform[] bones = new Transform[assimpMesh.BoneCount];

            for (int i = 0; i < assimpMesh.BoneCount; i++)
            {
                Assimp.Bone bone = assimpMesh.Bones[i];
                string boneName = bone.Name;

                // ボーン名辞書から Transform を取得
                if (boneNameToTransform.TryGetValue(boneName, out Transform boneTransform))
                {
                    bones[i] = boneTransform;
                    UnityEngine.Debug.Log($"{LOG_PREFIX}   Bone[{i}]: {boneName} → {GetTransformPath(boneTransform)}");
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   Bone[{i}]: {boneName} NOT FOUND in hierarchy");
                    // 見つからない場合は null（SkinnedMeshRenderer がエラーを出す可能性あり）
                    bones[i] = null;
                }
            }

            return bones;
        }

        /// <summary>
        /// STEP 6: BindPose を Unity 基準で計算
        /// </summary>
        private UnityEngine.Matrix4x4[] BuildBindPoses(Transform[] bones)
        {
            if (bones == null || bones.Length == 0)
                return null;

            if (cachedRootBone == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} cachedRootBone is null, cannot build bindposes");
                return null;
            }

            UnityEngine.Matrix4x4[] bindposes = new UnityEngine.Matrix4x4[bones.Length];

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    // Unity基準の正しいBindPose計算式
                    bindposes[i] = bones[i].worldToLocalMatrix * cachedRootBone.localToWorldMatrix;
                    UnityEngine.Debug.Log($"{LOG_PREFIX}   BindPose[{i}]: {bones[i].name}");
                }
                else
                {
                    // ボーンが見つからなかった場合は単位行列
                    bindposes[i] = UnityEngine.Matrix4x4.identity;
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX}   BindPose[{i}]: null bone, using identity matrix");
                }
            }

            return bindposes;
        }

        /// <summary>
        /// lilToonシェーダーを使用したマテリアルを作成
        /// </summary>
        private UnityEngine.Material CreateLilToonMaterial(string nodeName)
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

            // デフォルトカラーを設定（白）
            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", UnityEngine.Color.white);
            }
            else if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", UnityEngine.Color.white);
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX}   Created material: {material.name} with shader: {lilToonShader.name}");

            return material;
        }
    }
}
