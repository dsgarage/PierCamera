using System.Collections.Generic;
using UnityEngine;
using Assimp;

namespace arCam.FBXLoader
{
    /// <summary>
    /// STEP 3: ボーンデータ収集を担当するクラス
    ///
    /// 責務:
    /// - 全サブメッシュから全ユニークボーンを収集
    /// - ボーン名 → OffsetMatrix のマッピング辞書を作成
    /// - ボーン名 → グローバルインデックスのマッピング辞書を作成
    /// - BoneWeight を収集・正規化
    /// - 4つ以上のボーンを持つ頂点の処理
    ///
    /// 重要制約:
    /// - OffsetMatrix は生データのまま保持（座標変換を適用しない）
    /// - 座標変換は STEP 4 の SkinnedMeshBuilder で BindPose 計算時に適用
    /// - BoneWeight は float で保持（tiny weight を丸めない）
    /// </summary>
    public class BoneDataCollector
    {
        private const string LOG_PREFIX = "[BoneDataCollector]";

        private readonly Assimp.Scene assimpScene;
        private readonly Assimp.Node targetNode;
        private readonly int totalVertexCount;
        private readonly bool debugMode;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="scene">Assimp Scene</param>
        /// <param name="node">メッシュを持つノード</param>
        /// <param name="vertexCount">結合後の総頂点数</param>
        /// <param name="debugMode">デバッグログを有効化</param>
        public BoneDataCollector(
            Assimp.Scene scene,
            Assimp.Node node,
            int vertexCount,
            bool debugMode = false)
        {
            assimpScene = scene;
            targetNode = node;
            totalVertexCount = vertexCount;
            this.debugMode = debugMode;
        }

        /// <summary>
        /// ボーンデータを収集して BoneData を返す
        /// </summary>
        public BoneData Collect()
        {
            Debug.Log($"{LOG_PREFIX} === STEP 3: Collecting Bone Data ===");
            Debug.Log($"{LOG_PREFIX}   Node: {targetNode.Name}");
            Debug.Log($"{LOG_PREFIX}   Mesh count: {targetNode.MeshCount}");
            Debug.Log($"{LOG_PREFIX}   Total vertex count: {totalVertexCount}");

            BoneData boneData = new BoneData(
                estimatedBoneCount: 100,
                estimatedVertexCount: totalVertexCount);

            // STEP 3-1: 全メッシュから全ユニークボーンを収集
            CollectAllUniqueBones(boneData);

            // STEP 3-2: BoneWeight を収集
            CollectBoneWeights(boneData);

            // STEP 3-3: BoneWeight を正規化
            NormalizeBoneWeights(boneData);

            Debug.Log($"{LOG_PREFIX} === STEP 3 Complete ===");
            Debug.Log($"{LOG_PREFIX}   Total unique bones: {boneData.allUniqueBoneNames.Count}");
            Debug.Log($"{LOG_PREFIX}   Total vertices: {boneData.boneWeights.Length}");

            return boneData;
        }

        /// <summary>
        /// STEP 3-1: 全メッシュから全ユニークボーンを収集
        /// </summary>
        private void CollectAllUniqueBones(BoneData boneData)
        {
            Debug.Log($"{LOG_PREFIX} [STEP 3-1] Collecting all unique bones from all sub-meshes");

            for (int meshIdx = 0; meshIdx < targetNode.MeshCount; meshIdx++)
            {
                int assimpMeshIdx = targetNode.MeshIndices[meshIdx];
                Assimp.Mesh assimpMesh = assimpScene.Meshes[assimpMeshIdx];

                if (assimpMesh.HasBones)
                {
                    Debug.Log($"{LOG_PREFIX}   SubMesh[{meshIdx}]: {assimpMesh.BoneCount} bones");

                    for (int boneIdx = 0; boneIdx < assimpMesh.BoneCount; boneIdx++)
                    {
                        Assimp.Bone bone = assimpMesh.Bones[boneIdx];
                        string boneName = bone.Name;

                        // ユニークボーンリストに追加
                        if (!boneData.allUniqueBoneNames.Contains(boneName))
                        {
                            int globalIndex = boneData.allUniqueBoneNames.Count;
                            boneData.allUniqueBoneNames.Add(boneName);
                            boneData.boneNameToIndex[boneName] = globalIndex;

                            // OffsetMatrix を保存（生データのまま、座標変換を適用しない）
                            boneData.boneNameToOffsetMatrix[boneName] = bone.OffsetMatrix;

                            if (debugMode)
                            {
                                Debug.Log($"{LOG_PREFIX}     [Bone {globalIndex}] {boneName}");
                                Debug.Log($"{LOG_PREFIX}       OffsetMatrix (raw): {bone.OffsetMatrix}");
                            }
                        }
                    }
                }
                else
                {
                    Debug.LogWarning($"{LOG_PREFIX}   SubMesh[{meshIdx}]: No bones");
                }
            }

            Debug.Log($"{LOG_PREFIX} [STEP 3-1 Complete] Total unique bones: {boneData.allUniqueBoneNames.Count}");
        }

        /// <summary>
        /// STEP 3-2: BoneWeight を収集
        /// </summary>
        private void CollectBoneWeights(BoneData boneData)
        {
            Debug.Log($"{LOG_PREFIX} [STEP 3-2] Collecting BoneWeights");

            // 一時的なウェイトデータ（頂点ごとに複数のボーン影響を保持）
            List<List<BoneWeightEntry>> tempWeights = new List<List<BoneWeightEntry>>(totalVertexCount);
            for (int i = 0; i < totalVertexCount; i++)
            {
                tempWeights.Add(new List<BoneWeightEntry>());
            }

            int vertexOffset = 0;
            int totalWeightCount = 0;
            int over4BoneCount = 0;

            for (int meshIdx = 0; meshIdx < targetNode.MeshCount; meshIdx++)
            {
                int assimpMeshIdx = targetNode.MeshIndices[meshIdx];
                Assimp.Mesh assimpMesh = assimpScene.Meshes[assimpMeshIdx];

                if (assimpMesh.HasBones)
                {
                    for (int boneIdx = 0; boneIdx < assimpMesh.BoneCount; boneIdx++)
                    {
                        Assimp.Bone bone = assimpMesh.Bones[boneIdx];
                        string boneName = bone.Name;

                        // ボーン名が辞書に存在することを確認
                        if (!boneData.boneNameToIndex.TryGetValue(boneName, out int globalBoneIndex))
                        {
                            Debug.LogError($"{LOG_PREFIX} [ERROR] Bone '{boneName}' not found in global bone mapping!");
                            continue;
                        }

                        // このボーンの影響を受ける頂点のウェイトを収集
                        for (int weightIdx = 0; weightIdx < bone.VertexWeightCount; weightIdx++)
                        {
                            Assimp.VertexWeight vw = bone.VertexWeights[weightIdx];
                            int globalVertexIndex = vertexOffset + vw.VertexID;

                            if (globalVertexIndex >= totalVertexCount)
                            {
                                Debug.LogError($"{LOG_PREFIX} [ERROR] Vertex index out of range: {globalVertexIndex} >= {totalVertexCount}");
                                continue;
                            }

                            tempWeights[globalVertexIndex].Add(new BoneWeightEntry
                            {
                                boneIndex = globalBoneIndex,
                                weight = vw.Weight
                            });

                            totalWeightCount++;
                        }
                    }
                }

                vertexOffset += assimpMesh.VertexCount;
            }

            Debug.Log($"{LOG_PREFIX}   Total weight entries collected: {totalWeightCount}");

            // STEP 3-2-2: 一時ウェイトから BoneWeight 配列を構築（4ボーン制限を適用）
            for (int i = 0; i < totalVertexCount; i++)
            {
                List<BoneWeightEntry> weights = tempWeights[i];

                if (weights.Count > 4)
                {
                    over4BoneCount++;

                    // Weight降順でソート
                    weights.Sort((a, b) => b.weight.CompareTo(a.weight));

                    // 上位4個だけを選択
                    List<BoneWeightEntry> topWeights = weights.GetRange(0, 4);

                    if (debugMode || over4BoneCount <= 10) // 最初の10個だけログ出力
                    {
                        Debug.LogWarning($"{LOG_PREFIX} [WARN] 頂点[{i}] - ボーン制限処理:");
                        Debug.LogWarning($"{LOG_PREFIX}   ├─ 元のボーン数: {weights.Count}個");
                        Debug.LogWarning($"{LOG_PREFIX}   ├─ Unity制限: 最大4個");
                        Debug.LogWarning($"{LOG_PREFIX}   ├─ 処理方法: Weight降順ソート → 上位4個選択");
                        Debug.LogWarning($"{LOG_PREFIX}   ├─ 選択されたボーン:");
                        for (int j = 0; j < topWeights.Count; j++)
                        {
                            string boneName = boneData.allUniqueBoneNames[topWeights[j].boneIndex];
                            Debug.LogWarning($"{LOG_PREFIX}   │   ├─ Bone[{topWeights[j].boneIndex}] \"{boneName}\": Weight {topWeights[j].weight:F3}");
                        }
                        Debug.LogWarning($"{LOG_PREFIX}   └─ 除外されたボーン:");
                        for (int j = 4; j < weights.Count; j++)
                        {
                            string boneName = boneData.allUniqueBoneNames[weights[j].boneIndex];
                            Debug.LogWarning($"{LOG_PREFIX}       ├─ Bone[{weights[j].boneIndex}] \"{boneName}\": Weight {weights[j].weight:F3} (除外理由: {j + 1}番目以降)");
                        }
                    }

                    weights = topWeights;
                }

                // BoneWeight に変換
                boneData.boneWeights[i] = ConvertToBoneWeight(weights);
            }

            if (over4BoneCount > 0)
            {
                Debug.LogWarning($"{LOG_PREFIX} [WARN] 4つ以上のボーンを持つ頂点: {over4BoneCount}/{totalVertexCount}");
            }

            Debug.Log($"{LOG_PREFIX} [STEP 3-2 Complete]");
        }

        /// <summary>
        /// ウェイトエントリーリストを BoneWeight に変換
        /// </summary>
        private BoneWeight ConvertToBoneWeight(List<BoneWeightEntry> weights)
        {
            BoneWeight bw = new BoneWeight();

            for (int i = 0; i < weights.Count && i < 4; i++)
            {
                switch (i)
                {
                    case 0:
                        bw.boneIndex0 = weights[i].boneIndex;
                        bw.weight0 = weights[i].weight;
                        break;
                    case 1:
                        bw.boneIndex1 = weights[i].boneIndex;
                        bw.weight1 = weights[i].weight;
                        break;
                    case 2:
                        bw.boneIndex2 = weights[i].boneIndex;
                        bw.weight2 = weights[i].weight;
                        break;
                    case 3:
                        bw.boneIndex3 = weights[i].boneIndex;
                        bw.weight3 = weights[i].weight;
                        break;
                }
            }

            return bw;
        }

        /// <summary>
        /// STEP 3-3: BoneWeight を正規化（合計が1.0になるように）
        /// </summary>
        private void NormalizeBoneWeights(BoneData boneData)
        {
            Debug.Log($"{LOG_PREFIX} [STEP 3-3] Normalizing BoneWeights");

            int normalizedCount = 0;
            int zeroWeightCount = 0;

            for (int i = 0; i < boneData.boneWeights.Length; i++)
            {
                BoneWeight bw = boneData.boneWeights[i];
                float sum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;

                if (sum > 0.0001f)
                {
                    // 合計が1.0になるように正規化
                    float inv = 1.0f / sum;
                    bw.weight0 *= inv;
                    bw.weight1 *= inv;
                    bw.weight2 *= inv;
                    bw.weight3 *= inv;
                    normalizedCount++;
                }
                else
                {
                    // ウェイトが0の頂点は強制的にweight0=1に設定
                    Debug.LogWarning($"{LOG_PREFIX} [WARN] 頂点[{i}] - ゼロウェイト検出:");
                    Debug.LogWarning($"{LOG_PREFIX}   ├─ Weight合計: {sum}");
                    Debug.LogWarning($"{LOG_PREFIX}   ├─ 処理: weight0=1.0, boneIndex0=0 に強制設定");
                    Debug.LogWarning($"{LOG_PREFIX}   └─ 理由: スキニングにはウェイトが必須");

                    bw.weight0 = 1f;
                    bw.boneIndex0 = 0;
                    zeroWeightCount++;
                }

                boneData.boneWeights[i] = bw;
            }

            Debug.Log($"{LOG_PREFIX} [STEP 3-3 Complete]");
            Debug.Log($"{LOG_PREFIX}   Normalized vertices: {normalizedCount}");

            if (zeroWeightCount > 0)
            {
                Debug.LogWarning($"{LOG_PREFIX}   [WARN] Zero-weight vertices: {zeroWeightCount}");
            }
        }

        /// <summary>
        /// 一時的なウェイトデータを保持する構造体
        /// </summary>
        private struct BoneWeightEntry
        {
            public int boneIndex;
            public float weight;
        }
    }
}
