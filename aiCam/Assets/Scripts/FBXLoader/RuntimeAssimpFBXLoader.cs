using UnityEngine;
using Assimp;
using System.Collections.Generic;
using System.IO;

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

        // ノード名→Transform のマップ
        private Dictionary<string, Transform> nodeNameToTransform = new Dictionary<string, Transform>();

        // Assimp Scene
        private Scene currentScene;

        /// <summary>
        /// FBXファイルからボーン階層のみをロードしてGameObjectツリーを構築
        /// </summary>
        /// <param name="fbxPath">FBXファイルのパス</param>
        /// <param name="rootName">ルートGameObjectの名前</param>
        /// <returns>ルートGameObject</returns>
        public GameObject LoadBoneHierarchy(string fbxPath, string rootName = null)
        {
            if (!File.Exists(fbxPath))
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} FBX file not found: {fbxPath}");
                return null;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} Loading FBX: {fbxPath}");

            // Assimpでシーンをロード
            AssimpContext importer = new AssimpContext();
            Scene scene = importer.ImportFile(fbxPath, PostProcessSteps.None);

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

            // ノード名マップをクリア
            nodeNameToTransform.Clear();

            // ルートGameObjectを作成
            string objName = string.IsNullOrEmpty(rootName) ? Path.GetFileNameWithoutExtension(fbxPath) : rootName;
            GameObject rootObject = new GameObject(objName);

            // ノード階層をGameObject階層に変換（ボーンのみ）
            BuildBoneHierarchy(scene.RootNode, rootObject.transform, scene);

            UnityEngine.Debug.Log($"{LOG_PREFIX} Bone hierarchy built successfully");
            LogHierarchy(rootObject.transform, 0);

            return rootObject;
        }

        /// <summary>
        /// Assimpノード階層をGameObject階層に再帰的に変換
        /// </summary>
        private void BuildBoneHierarchy(Node node, Transform parentTransform, Scene scene)
        {
            if (node == null)
                return;

            // 現在のノード用のGameObjectを作成
            GameObject nodeObject = new GameObject(node.Name);
            nodeObject.transform.SetParent(parentTransform, false);

            // Transform情報を設定（Assimpの行列をUnityに変換）
            SetTransformFromAssimpMatrix(nodeObject.transform, node.Transform);

            // ノード名→Transformマップに追加（メッシュロード時に使用）
            if (!nodeNameToTransform.ContainsKey(node.Name))
            {
                nodeNameToTransform[node.Name] = nodeObject.transform;
            }

            // 子ノードを再帰的に処理
            for (int i = 0; i < node.ChildCount; i++)
            {
                BuildBoneHierarchy(node.Children[i], nodeObject.transform, scene);
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
        /// Assimpシーンからメッシュデータをロードして、対応するノードにSkinnedMeshRendererを追加
        /// </summary>
        /// <param name="rootObject">ルートGameObject</param>
        public void LoadMeshes(GameObject rootObject)
        {
            if (currentScene == null)
            {
                UnityEngine.Debug.LogError($"{LOG_PREFIX} No scene loaded. Call LoadBoneHierarchy first.");
                return;
            }

            if (currentScene.MeshCount == 0)
            {
                UnityEngine.Debug.LogWarning($"{LOG_PREFIX} No meshes found in scene.");
                return;
            }

            UnityEngine.Debug.Log($"{LOG_PREFIX} === Loading Meshes ===");
            UnityEngine.Debug.Log($"{LOG_PREFIX} Total meshes in scene: {currentScene.MeshCount}");

            // STEP 5: rootBoneを決定（Hips）
            cachedRootBone = FindRootBone(rootObject.transform);

            // シーンのノード階層を再帰的に探索してメッシュを持つノードを処理
            ProcessMeshNodes(currentScene.RootNode, rootObject.transform);

            UnityEngine.Debug.Log($"{LOG_PREFIX} === Mesh Loading Complete ===");
        }


        /// <summary>
        /// ノード階層を再帰的に探索してメッシュを持つノードを処理
        /// </summary>
        private void ProcessMeshNodes(Node node, Transform rootTransform)
        {
            if (node == null)
                return;

            // このノードがメッシュを持っている場合
            if (node.MeshCount > 0)
            {
                // ノード名に対応するTransformを取得
                if (nodeNameToTransform.TryGetValue(node.Name, out Transform nodeTransform))
                {
                    UnityEngine.Debug.Log($"{LOG_PREFIX} Processing mesh node: {node.Name} with {node.MeshCount} mesh(es)");

                    // このノードに含まれる全メッシュをロード（サブメッシュとして）
                    LoadMeshesForNode(node, nodeTransform);
                }
                else
                {
                    UnityEngine.Debug.LogWarning($"{LOG_PREFIX} Transform not found for mesh node: {node.Name}");
                }
            }

            // 子ノードを再帰的に処理
            for (int i = 0; i < node.ChildCount; i++)
            {
                ProcessMeshNodes(node.Children[i], rootTransform);
            }
        }

        /// <summary>
        /// 特定のノードに含まれる全メッシュをロードしてSkinnedMeshRendererを作成
        /// </summary>
        private void LoadMeshesForNode(Node node, Transform nodeTransform)
        {
            // 複数のメッシュがある場合はサブメッシュとして結合
            List<UnityEngine.Vector3> combinedVertices = new List<UnityEngine.Vector3>();
            List<UnityEngine.Vector3> combinedNormals = new List<UnityEngine.Vector3>();
            List<UnityEngine.Vector2> combinedUVs = new List<UnityEngine.Vector2>();
            List<int> combinedTriangles = new List<int>();
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

            UnityEngine.Debug.Log($"{LOG_PREFIX}   Mesh: {unityMesh.name} (V:{unityMesh.vertexCount}, T:{unityMesh.triangles.Length / 3})");

            // STEP 7: SkinnedMeshRendererの完全セットアップ（bones[], rootBone, bindposes）
            SkinnedMeshRenderer renderer = nodeTransform.gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = unityMesh;

            // ボーン情報を収集（最初のメッシュからのみ - 簡易実装）
            if (node.MeshCount > 0)
            {
                int assimpMeshIndex = node.MeshIndices[0];
                Assimp.Mesh assimpMesh = currentScene.Meshes[assimpMeshIndex];

                if (assimpMesh.HasBones)
                {
                    // ボーン名からTransformへのマッピング
                    List<Transform> boneTransforms = new List<Transform>();
                    for (int i = 0; i < assimpMesh.BoneCount; i++)
                    {
                        Assimp.Bone bone = assimpMesh.Bones[i];
                        if (nodeNameToTransform.TryGetValue(bone.Name, out Transform boneTransform))
                        {
                            boneTransforms.Add(boneTransform);
                        }
                        else
                        {
                            UnityEngine.Debug.LogWarning($"{LOG_PREFIX} [STEP 7] Bone not found in hierarchy: {bone.Name}");
                            boneTransforms.Add(null); // nullは後でidentity matrixに
                        }
                    }

                    // STEP 6: bindpose計算
                    Transform[] bones = boneTransforms.ToArray();
                    UnityEngine.Matrix4x4[] bindposes = CalculateBindPoses(bones, cachedRootBone);

                    // STEP 7: SkinnedMeshRendererにセット
                    renderer.bones = bones;
                    renderer.rootBone = cachedRootBone;
                    unityMesh.bindposes = bindposes;

                    UnityEngine.Debug.Log($"{LOG_PREFIX}   SMR: {bones.Length} bones, rootBone={cachedRootBone.name}");
                }
            }

            // lilToonシェーダーを使用したマテリアルを作成
            UnityEngine.Material material = CreateLilToonMaterial(node.Name);
            renderer.sharedMaterial = material;

            UnityEngine.Debug.Log($"{LOG_PREFIX}   Added SkinnedMeshRenderer to: {nodeTransform.name}");
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
