using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using Assimp;
using Assimp.Configs;

namespace AICam.FBXLoader
{
    /// <summary>
    /// シンプルなFBXローダー（Assimp使用）
    /// avatownMobileのRuntimeFBXLoader3を参考に簡略化
    /// </summary>
    public class SimpleFBXLoader
    {
        private Dictionary<string, Transform> nodeNameToTransform = new Dictionary<string, Transform>();

        /// <summary>
        /// FBXファイルをロードしてGameObjectを返す
        /// </summary>
        public async UniTask<GameObject> LoadFBXAsync(string fbxPath, Action<float> onProgress = null)
        {
            if (string.IsNullOrEmpty(fbxPath))
                throw new ArgumentNullException(nameof(fbxPath));

            if (!File.Exists(fbxPath))
                throw new FileNotFoundException($"FBX not found: {fbxPath}");

            Debug.Log($"[SimpleFBXLoader] Loading FBX: {fbxPath}");
            onProgress?.Invoke(10f);

            // Assimp でインポート
            Scene scene;
            using (var importer = new AssimpContext())
            {
                scene = importer.ImportFile(
                    fbxPath,
                    PostProcessSteps.Triangulate |
                    PostProcessSteps.CalculateTangentSpace |
                    PostProcessSteps.JoinIdenticalVertices |
                    PostProcessSteps.SortByPrimitiveType |
                    PostProcessSteps.LimitBoneWeights
                );
            }

            if (scene == null || scene.RootNode == null)
            {
                throw new Exception("Assimp import failed");
            }

            Debug.Log($"[SimpleFBXLoader] Assimp scene loaded. Meshes: {scene.MeshCount}, Materials: {scene.MaterialCount}");
            onProgress?.Invoke(30f);

            // ルートGameObjectを作成
            GameObject rootObject = new GameObject(Path.GetFileNameWithoutExtension(fbxPath));

            // ノード階層を構築
            BuildNodeHierarchy(scene.RootNode, rootObject.transform, null);
            onProgress?.Invoke(50f);

            // メッシュとマテリアルを作成
            await CreateMeshesAndMaterials(scene, rootObject);
            onProgress?.Invoke(80f);

            Debug.Log($"[SimpleFBXLoader] FBX loaded successfully: {rootObject.name}");
            onProgress?.Invoke(100f);

            return rootObject;
        }

        private void BuildNodeHierarchy(Node assimpNode, Transform parentTransform, Transform parentOfParent)
        {
            if (assimpNode == null) return;

            // Unityノードを作成
            GameObject nodeObject = new GameObject(assimpNode.Name);
            nodeObject.transform.SetParent(parentTransform);

            // ローカルトランスフォームを設定
            SetLocalTransform(assimpNode, nodeObject.transform);

            // ノード名とTransformを登録
            nodeNameToTransform[assimpNode.Name] = nodeObject.transform;

            // 子ノードを再帰的に構築
            foreach (var childNode in assimpNode.Children)
            {
                BuildNodeHierarchy(childNode, nodeObject.transform, parentTransform);
            }
        }

        private void SetLocalTransform(Node assimpNode, Transform unityTransform)
        {
            // Assimpのローカル行列をUnity形式に変換
            var assimpMatrix = assimpNode.Transform;

            // Assimpの行列はRow-major、UnityはColumn-major
            // Assimp: 右手座標系Y-up、Unity: 左手座標系Y-up
            // X軸を反転することで座標系を変換
            Matrix4x4 unityMatrix = new Matrix4x4();
            unityMatrix.m00 = -assimpMatrix.A1; unityMatrix.m01 = assimpMatrix.A2; unityMatrix.m02 = assimpMatrix.A3; unityMatrix.m03 = assimpMatrix.A4;
            unityMatrix.m10 =  assimpMatrix.B1; unityMatrix.m11 = assimpMatrix.B2; unityMatrix.m12 = assimpMatrix.B3; unityMatrix.m13 = assimpMatrix.B4;
            unityMatrix.m20 =  assimpMatrix.C1; unityMatrix.m21 = assimpMatrix.C2; unityMatrix.m22 = assimpMatrix.C3; unityMatrix.m23 = assimpMatrix.C4;
            unityMatrix.m30 = -assimpMatrix.D1; unityMatrix.m31 = assimpMatrix.D2; unityMatrix.m32 = assimpMatrix.D3; unityMatrix.m33 = assimpMatrix.D4;

            // TRS分解
            Vector3 position = unityMatrix.GetPosition();
            Quaternion rotation = unityMatrix.rotation;
            Vector3 scale = unityMatrix.lossyScale;

            unityTransform.localPosition = position;
            unityTransform.localRotation = rotation;
            unityTransform.localScale = scale;
        }

        private async UniTask CreateMeshesAndMaterials(Scene scene, GameObject rootObject)
        {
            // マテリアルを作成
            UnityEngine.Material[] materials = new UnityEngine.Material[scene.MaterialCount];
            for (int i = 0; i < scene.MaterialCount; i++)
            {
                materials[i] = CreateMaterial(scene.Materials[i], i);
            }

            // メッシュを作成
            for (int i = 0; i < scene.MeshCount; i++)
            {
                var assimpMesh = scene.Meshes[i];
                CreateMeshRenderer(assimpMesh, materials, rootObject, i);

                // 非同期処理を挿入
                if (i % 10 == 0)
                {
                    await UniTask.Yield();
                }
            }
        }

        private UnityEngine.Material CreateMaterial(Assimp.Material assimpMaterial, int index)
        {
            // シンプルなStandardマテリアルを作成
            UnityEngine.Material material = new UnityEngine.Material(Shader.Find("Standard"));
            material.name = assimpMaterial.Name ?? $"Material_{index}";

            // ベースカラーを設定
            if (assimpMaterial.HasColorDiffuse)
            {
                var color = assimpMaterial.ColorDiffuse;
                material.color = new Color(color.R, color.G, color.B, color.A);
            }

            return material;
        }

        private void CreateMeshRenderer(Assimp.Mesh assimpMesh, UnityEngine.Material[] materials, GameObject rootObject, int meshIndex)
        {
            // メッシュノードを探す
            string nodeName = assimpMesh.Name ?? $"Mesh_{meshIndex}";
            Transform nodeTransform;

            if (!nodeNameToTransform.TryGetValue(nodeName, out nodeTransform))
            {
                // ノードが見つからない場合は新規作成
                GameObject meshObject = new GameObject(nodeName);
                meshObject.transform.SetParent(rootObject.transform);
                nodeTransform = meshObject.transform;
            }

            // Unity Meshを作成
            UnityEngine.Mesh unityMesh = ConvertToUnityMesh(assimpMesh);
            unityMesh.name = nodeName;

            // MeshFilterとMeshRendererを追加
            var meshFilter = nodeTransform.gameObject.AddComponent<MeshFilter>();
            meshFilter.mesh = unityMesh;

            var meshRenderer = nodeTransform.gameObject.AddComponent<MeshRenderer>();

            // マテリアルを割り当て
            if (assimpMesh.MaterialIndex >= 0 && assimpMesh.MaterialIndex < materials.Length)
            {
                meshRenderer.material = materials[assimpMesh.MaterialIndex];
            }
            else
            {
                meshRenderer.material = new UnityEngine.Material(Shader.Find("Standard"));
            }
        }

        private UnityEngine.Mesh ConvertToUnityMesh(Assimp.Mesh assimpMesh)
        {
            UnityEngine.Mesh unityMesh = new UnityEngine.Mesh();

            // 頂点座標（右手→左手座標系変換：X軸を反転）
            Vector3[] vertices = new Vector3[assimpMesh.VertexCount];
            for (int i = 0; i < assimpMesh.VertexCount; i++)
            {
                var v = assimpMesh.Vertices[i];
                vertices[i] = new Vector3(-v.X, v.Y, v.Z);
            }
            unityMesh.vertices = vertices;

            // 法線
            if (assimpMesh.HasNormals)
            {
                Vector3[] normals = new Vector3[assimpMesh.VertexCount];
                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    var n = assimpMesh.Normals[i];
                    normals[i] = new Vector3(-n.X, n.Y, n.Z);
                }
                unityMesh.normals = normals;
            }

            // UV
            if (assimpMesh.HasTextureCoords(0))
            {
                Vector2[] uvs = new Vector2[assimpMesh.VertexCount];
                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    var uv = assimpMesh.TextureCoordinateChannels[0][i];
                    uvs[i] = new Vector2(uv.X, uv.Y);
                }
                unityMesh.uv = uvs;
            }

            // 三角形インデックス（ワインディング順序を反転）
            List<int> indices = new List<int>();
            foreach (var face in assimpMesh.Faces)
            {
                if (face.IndexCount == 3)
                {
                    // 左手座標系に変換するため順序を反転
                    indices.Add(face.Indices[2]);
                    indices.Add(face.Indices[1]);
                    indices.Add(face.Indices[0]);
                }
            }
            unityMesh.triangles = indices.ToArray();

            unityMesh.RecalculateBounds();
            unityMesh.RecalculateTangents();

            return unityMesh;
        }
    }
}
