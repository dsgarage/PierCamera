using System.Collections.Generic;
using UnityEngine;

namespace arCam.FBXLoader
{
    /// <summary>
    /// メッシュデータを保持するクラス
    /// STEP 2: MeshDataCollector で生成され、STEP 4: SkinnedMeshBuilder で使用される
    /// </summary>
    public class MeshData
    {
        /// <summary>
        /// 結合された頂点（座標変換済み）
        /// </summary>
        public List<Vector3> vertices;

        /// <summary>
        /// 結合されたUV座標
        /// </summary>
        public List<Vector2> uvs;

        /// <summary>
        /// 結合された法線（座標変換済み）
        /// </summary>
        public List<Vector3> normals;

        /// <summary>
        /// 結合された三角形インデックス
        /// </summary>
        public List<int> triangles;

        /// <summary>
        /// 作成されたUnity Mesh（BlendShape含む）
        /// ⚠️ 重要: BlendShapeは sharedMesh 設定前に追加すること
        /// </summary>
        public Mesh unityMesh;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public MeshData(int estimatedVertexCount = 0)
        {
            vertices = new List<Vector3>(estimatedVertexCount);
            uvs = new List<Vector2>(estimatedVertexCount);
            normals = new List<Vector3>(estimatedVertexCount);
            triangles = new List<int>(estimatedVertexCount * 3);
            unityMesh = null;
        }
    }

    /// <summary>
    /// ボーンデータを保持するクラス
    /// STEP 3: BoneDataCollector で生成され、STEP 4: SkinnedMeshBuilder で使用される
    /// </summary>
    public class BoneData
    {
        /// <summary>
        /// ボーン名 → Assimp OffsetMatrix のマッピング
        /// ⚠️ 重要: これは生データのまま保持し、座標変換は適用しない
        ///         BindPose計算時に SkinnedMeshBuilder で座標変換を適用する
        /// </summary>
        public Dictionary<string, Assimp.Matrix4x4> boneNameToOffsetMatrix;

        /// <summary>
        /// ボーン名 → グローバルインデックスのマッピング
        /// 全メッシュから収集したユニークなボーンのインデックス
        /// </summary>
        public Dictionary<string, int> boneNameToIndex;

        /// <summary>
        /// 全頂点のBoneWeight（正規化済み）
        /// - 各頂点は最大4つのボーンに影響される
        /// - weight0 + weight1 + weight2 + weight3 = 1.0 に正規化済み
        /// - tiny weight も丸めずに保持（float精度）
        /// </summary>
        public BoneWeight[] boneWeights;

        /// <summary>
        /// 全ユニークボーン名のリスト（順序保証）
        /// bones配列の構築に使用される
        /// </summary>
        public List<string> allUniqueBoneNames;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        public BoneData(int estimatedBoneCount = 0, int estimatedVertexCount = 0)
        {
            boneNameToOffsetMatrix = new Dictionary<string, Assimp.Matrix4x4>(estimatedBoneCount);
            boneNameToIndex = new Dictionary<string, int>(estimatedBoneCount);
            boneWeights = new BoneWeight[estimatedVertexCount];
            allUniqueBoneNames = new List<string>(estimatedBoneCount);
        }
    }
}
