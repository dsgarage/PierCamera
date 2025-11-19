using System.Collections.Generic;
using UnityEngine;
using Assimp;
using AICam.FBXLoader;

namespace arCam.FBXLoader
{
    /// <summary>
    /// STEP 2: メッシュデータ収集とBlendShape登録を担当するクラス
    ///
    /// 責務:
    /// - 複数のサブメッシュからメッシュデータを収集・結合
    /// - 頂点・UV・法線を座標変換
    /// - BlendShape（MorphTarget）を登録
    /// - Unity Mesh を作成
    ///
    /// 重要制約:
    /// - BlendShape は sharedMesh 設定前に追加すること
    /// - 座標変換は vertices と normals に適用（UV は不要）
    /// </summary>
    public class MeshDataCollector
    {
        private const string LOG_PREFIX = "[MeshDataCollector]";

        private readonly Assimp.Scene assimpScene;
        private readonly Assimp.Node targetNode;
        private readonly UnityEngine.Matrix4x4 coordinateConversionMatrix;
        private readonly bool debugMode;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="scene">Assimp Scene</param>
        /// <param name="node">メッシュを持つノード</param>
        /// <param name="conversionMatrix">座標変換行列</param>
        /// <param name="debugMode">デバッグログを有効化</param>
        public MeshDataCollector(
            Assimp.Scene scene,
            Assimp.Node node,
            UnityEngine.Matrix4x4 conversionMatrix,
            bool debugMode = false)
        {
            assimpScene = scene;
            targetNode = node;
            coordinateConversionMatrix = conversionMatrix;
            this.debugMode = debugMode;
        }

        /// <summary>
        /// メッシュデータを収集して MeshData を返す
        /// </summary>
        public MeshData Collect()
        {
            Debug.Log($"{LOG_PREFIX} === STEP 2: Collecting Mesh Data ===");
            Debug.Log($"{LOG_PREFIX}   Node: {targetNode.Name}");
            Debug.Log($"{LOG_PREFIX}   Mesh count: {targetNode.MeshCount}");

            MeshData meshData = new MeshData(estimatedVertexCount: 10000);

            int totalVertexOffset = 0;

            // 全サブメッシュを結合
            for (int meshIdx = 0; meshIdx < targetNode.MeshCount; meshIdx++)
            {
                int assimpMeshIdx = targetNode.MeshIndices[meshIdx];
                Assimp.Mesh assimpMesh = assimpScene.Meshes[assimpMeshIdx];

                Debug.Log($"{LOG_PREFIX} [SubMesh {meshIdx}] Name: {assimpMesh.Name ?? "Unnamed"}");
                Debug.Log($"{LOG_PREFIX}   ├─ Vertices: {assimpMesh.VertexCount}");
                Debug.Log($"{LOG_PREFIX}   ├─ Faces: {assimpMesh.FaceCount}");
                Debug.Log($"{LOG_PREFIX}   ├─ Has UVs: {assimpMesh.HasTextureCoords(0)}");
                Debug.Log($"{LOG_PREFIX}   ├─ Has Normals: {assimpMesh.HasNormals}");
                Debug.Log($"{LOG_PREFIX}   ├─ Has Bones: {assimpMesh.HasBones} ({assimpMesh.BoneCount} bones)");
                Debug.Log($"{LOG_PREFIX}   └─ Material Index: {assimpMesh.MaterialIndex}");

                // 頂点データを収集
                CollectVertices(assimpMesh, meshData);

                // UVを収集
                CollectUVs(assimpMesh, meshData);

                // 法線を収集
                CollectNormals(assimpMesh, meshData);

                // 三角形インデックスを収集（オフセット適用）
                CollectTriangles(assimpMesh, meshData, totalVertexOffset);

                totalVertexOffset += assimpMesh.VertexCount;
            }

            Debug.Log($"{LOG_PREFIX} [Combined Mesh]");
            Debug.Log($"{LOG_PREFIX}   ├─ Total Vertices: {meshData.vertices.Count}");
            Debug.Log($"{LOG_PREFIX}   ├─ Total UVs: {meshData.uvs.Count}");
            Debug.Log($"{LOG_PREFIX}   ├─ Total Normals: {meshData.normals.Count}");
            Debug.Log($"{LOG_PREFIX}   └─ Total Triangles: {meshData.triangles.Count / 3}");

            // Unity Mesh を作成
            CreateUnityMesh(meshData);

            // BlendShape を登録
            RegisterBlendShapes(meshData);

            Debug.Log($"{LOG_PREFIX} === STEP 2 Complete ===");

            return meshData;
        }

        /// <summary>
        /// 頂点を収集（座標変換を適用）
        /// </summary>
        private void CollectVertices(Assimp.Mesh assimpMesh, MeshData meshData)
        {
            for (int i = 0; i < assimpMesh.VertexCount; i++)
            {
                Assimp.Vector3D v = assimpMesh.Vertices[i];
                UnityEngine.Vector3 vertex = FbxCoordinateSystemDetector.ConvertVector(v, coordinateConversionMatrix);
                meshData.vertices.Add(vertex);
            }

            if (debugMode)
            {
                Debug.Log($"{LOG_PREFIX} [DEBUG] First vertex: {meshData.vertices[meshData.vertices.Count - assimpMesh.VertexCount]}");
            }
        }

        /// <summary>
        /// UVを収集（座標変換不要）
        /// </summary>
        private void CollectUVs(Assimp.Mesh assimpMesh, MeshData meshData)
        {
            if (assimpMesh.HasTextureCoords(0))
            {
                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    Assimp.Vector3D uv = assimpMesh.TextureCoordinateChannels[0][i];
                    meshData.uvs.Add(new UnityEngine.Vector2(uv.X, uv.Y));
                }
            }
            else
            {
                // UVがない場合はゼロで埋める
                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    meshData.uvs.Add(UnityEngine.Vector2.zero);
                }

                Debug.LogWarning($"{LOG_PREFIX} [WARN] No UVs found, filling with zeros");
            }
        }

        /// <summary>
        /// 法線を収集（座標変換を適用）
        /// </summary>
        private void CollectNormals(Assimp.Mesh assimpMesh, MeshData meshData)
        {
            if (assimpMesh.HasNormals)
            {
                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    Assimp.Vector3D n = assimpMesh.Normals[i];
                    UnityEngine.Vector3 normal = FbxCoordinateSystemDetector.ConvertVector(n, coordinateConversionMatrix);
                    meshData.normals.Add(normal.normalized);
                }
            }
            else
            {
                // 法線がない場合はゼロで埋める（後で再計算される）
                for (int i = 0; i < assimpMesh.VertexCount; i++)
                {
                    meshData.normals.Add(UnityEngine.Vector3.zero);
                }

                Debug.LogWarning($"{LOG_PREFIX} [WARN] No normals found, filling with zeros");
            }
        }

        /// <summary>
        /// 三角形インデックスを収集（オフセット適用）
        /// </summary>
        private void CollectTriangles(Assimp.Mesh assimpMesh, MeshData meshData, int vertexOffset)
        {
            for (int i = 0; i < assimpMesh.FaceCount; i++)
            {
                Assimp.Face face = assimpMesh.Faces[i];

                if (face.IndexCount == 3)
                {
                    // 三角形の頂点順序を反転（右手系→左手系）
                    meshData.triangles.Add(vertexOffset + face.Indices[0]);
                    meshData.triangles.Add(vertexOffset + face.Indices[2]);
                    meshData.triangles.Add(vertexOffset + face.Indices[1]);
                }
                else
                {
                    Debug.LogWarning($"{LOG_PREFIX} [WARN] Non-triangle face detected (IndexCount={face.IndexCount}), skipping");
                }
            }
        }

        /// <summary>
        /// Unity Mesh を作成
        /// </summary>
        private void CreateUnityMesh(MeshData meshData)
        {
            Debug.Log($"{LOG_PREFIX} [Creating Unity Mesh]");

            UnityEngine.Mesh mesh = new UnityEngine.Mesh();
            mesh.name = $"{targetNode.Name}_Mesh";

            // 頂点数が65535を超える場合は32bitインデックスを使用
            if (meshData.vertices.Count > 65535)
            {
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                Debug.Log($"{LOG_PREFIX}   Using 32-bit index format (vertices > 65535)");
            }

            mesh.SetVertices(meshData.vertices);
            mesh.SetUVs(0, meshData.uvs);
            mesh.SetNormals(meshData.normals);
            mesh.SetTriangles(meshData.triangles, 0);

            // バウンディングボックスを再計算
            mesh.RecalculateBounds();

            Debug.Log($"{LOG_PREFIX}   Mesh bounds: {mesh.bounds}");

            meshData.unityMesh = mesh;
        }

        /// <summary>
        /// BlendShape（MorphTarget）を登録
        /// ⚠️ 重要: sharedMesh 設定前に追加すること
        /// </summary>
        private void RegisterBlendShapes(MeshData meshData)
        {
            Debug.Log($"{LOG_PREFIX} [Registering BlendShapes]");

            int totalBlendShapeCount = 0;

            for (int meshIdx = 0; meshIdx < targetNode.MeshCount; meshIdx++)
            {
                int assimpMeshIdx = targetNode.MeshIndices[meshIdx];
                Assimp.Mesh assimpMesh = assimpScene.Meshes[assimpMeshIdx];

                if (assimpMesh.HasMeshAnimationAttachments)
                {
                    Debug.Log($"{LOG_PREFIX}   SubMesh[{meshIdx}] has {assimpMesh.MeshAnimationAttachmentCount} BlendShapes");

                    for (int i = 0; i < assimpMesh.MeshAnimationAttachmentCount; i++)
                    {
                        Assimp.MeshAnimationAttachment blendShape = assimpMesh.MeshAnimationAttachments[i];
                        string blendShapeName = $"{assimpMesh.Name}_BlendShape_{i}";

                        // BlendShape の頂点を変換
                        UnityEngine.Vector3[] deltaVertices = new UnityEngine.Vector3[blendShape.VertexCount];
                        UnityEngine.Vector3[] deltaNormals = new UnityEngine.Vector3[blendShape.VertexCount];
                        UnityEngine.Vector3[] deltaTangents = new UnityEngine.Vector3[blendShape.VertexCount];

                        for (int v = 0; v < blendShape.VertexCount; v++)
                        {
                            if (blendShape.HasVertices)
                            {
                                deltaVertices[v] = FbxCoordinateSystemDetector.ConvertVector(
                                    blendShape.Vertices[v], coordinateConversionMatrix);
                            }

                            if (blendShape.HasNormals)
                            {
                                deltaNormals[v] = FbxCoordinateSystemDetector.ConvertVector(
                                    blendShape.Normals[v], coordinateConversionMatrix).normalized;
                            }

                            // Tangent はサポートしない（通常は使用されない）
                            deltaTangents[v] = UnityEngine.Vector3.zero;
                        }

                        // BlendShape フレームを追加（weight = 100）
                        meshData.unityMesh.AddBlendShapeFrame(blendShapeName, 100f, deltaVertices, deltaNormals, deltaTangents);

                        Debug.Log($"{LOG_PREFIX}     [{i}] {blendShapeName}: {blendShape.VertexCount} vertices");
                        totalBlendShapeCount++;
                    }
                }
            }

            Debug.Log($"{LOG_PREFIX}   Total BlendShapes registered: {totalBlendShapeCount}");
        }
    }
}
