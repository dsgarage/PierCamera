using System.Collections.Generic;
using UnityEngine;
using Assimp;
using AICam.FBXLoader;

namespace arCam.FBXLoader
{
    /// <summary>
    /// STEP 1: Transform階層構築と座標変換を担当するクラス
    ///
    /// 責務:
    /// - Assimp Scene から Transform階層を構築
    /// - 座標系変換（右手系Y-up → 左手系Y-up）を適用
    /// - ボーン名 → Transform のマッピング辞書を作成
    ///
    /// 重要原則:
    /// 「スキニング問題の80%はTransformの破綻が原因」
    /// → このクラスが最も重要
    /// </summary>
    public class TransformBuilder
    {
        private const string LOG_PREFIX = "[TransformBuilder]";

        private readonly Assimp.Scene assimpScene;
        private readonly Transform rootTransform;
        private readonly UnityEngine.Matrix4x4 coordinateConversionMatrix;
        private readonly bool debugMode;

        /// <summary>
        /// ボーン名 → Transform のマッピング辞書
        /// STEP 4 で bones配列を構築する際に使用される
        /// </summary>
        public Dictionary<string, Transform> BoneNameToTransform { get; private set; }

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="scene">Assimp Scene</param>
        /// <param name="root">Unity Transform のルート</param>
        /// <param name="conversionMatrix">座標変換行列</param>
        /// <param name="debugMode">デバッグログを有効化</param>
        public TransformBuilder(
            Assimp.Scene scene,
            Transform root,
            UnityEngine.Matrix4x4 conversionMatrix,
            bool debugMode = false)
        {
            assimpScene = scene;
            rootTransform = root;
            coordinateConversionMatrix = conversionMatrix;
            this.debugMode = debugMode;
            BoneNameToTransform = new Dictionary<string, Transform>();
        }

        /// <summary>
        /// Transform階層を構築し、辞書を作成
        /// </summary>
        public void Build()
        {
            Debug.Log($"{LOG_PREFIX} === STEP 1: Building Transform Hierarchy ===");
            Debug.Log($"{LOG_PREFIX}   Root: {rootTransform.name}");
            Debug.Log($"{LOG_PREFIX}   Coordinate Conversion: {coordinateConversionMatrix}");

            if (assimpScene.RootNode == null)
            {
                Debug.LogError($"{LOG_PREFIX} Assimp Scene RootNode is null!");
                return;
            }

            // 再帰的にノード階層を構築
            int nodeCount = BuildNodeRecursive(assimpScene.RootNode, rootTransform);

            Debug.Log($"{LOG_PREFIX} === STEP 1 Complete ===");
            Debug.Log($"{LOG_PREFIX}   Total nodes processed: {nodeCount}");
            Debug.Log($"{LOG_PREFIX}   Bone dictionary entries: {BoneNameToTransform.Count}");

            // デバッグモードで全ボーンをリスト表示
            if (debugMode)
            {
                Debug.Log($"{LOG_PREFIX} [DEBUG] Bone Dictionary:");
                foreach (var kvp in BoneNameToTransform)
                {
                    Debug.Log($"{LOG_PREFIX}   [{kvp.Key}] → {GetTransformPath(kvp.Value)}");
                }
            }
        }

        /// <summary>
        /// ノードを再帰的に構築
        /// </summary>
        private int BuildNodeRecursive(Assimp.Node assimpNode, Transform parentTransform)
        {
            if (assimpNode == null)
                return 0;

            int processedCount = 0;

            // GameObjectを作成
            GameObject nodeObj = new GameObject(assimpNode.Name);
            Transform nodeTransform = nodeObj.transform;
            nodeTransform.SetParent(parentTransform, false);

            // Transformを設定（座標変換を適用）
            SetTransformFromAssimpMatrix(nodeTransform, assimpNode.Transform);

            // 辞書に登録
            if (!BoneNameToTransform.ContainsKey(assimpNode.Name))
            {
                BoneNameToTransform[assimpNode.Name] = nodeTransform;
                processedCount++;

                if (debugMode)
                {
                    Debug.Log($"{LOG_PREFIX} [Node] {assimpNode.Name}");
                    Debug.Log($"{LOG_PREFIX}   ├─ Path: {GetTransformPath(nodeTransform)}");
                    Debug.Log($"{LOG_PREFIX}   ├─ localPosition: {nodeTransform.localPosition}");
                    Debug.Log($"{LOG_PREFIX}   ├─ localRotation: {nodeTransform.localRotation.eulerAngles}");
                    Debug.Log($"{LOG_PREFIX}   └─ localScale: {nodeTransform.localScale}");
                }
            }
            else
            {
                Debug.LogWarning($"{LOG_PREFIX} [WARN] Duplicate node name: {assimpNode.Name}");
            }

            // 子ノードを再帰的に構築
            for (int i = 0; i < assimpNode.ChildCount; i++)
            {
                processedCount += BuildNodeRecursive(assimpNode.Children[i], nodeTransform);
            }

            return processedCount;
        }

        /// <summary>
        /// Assimp.Matrix4x4 から Unity Transform を設定（座標変換を適用）
        /// </summary>
        private void SetTransformFromAssimpMatrix(Transform t, Assimp.Matrix4x4 m)
        {
            // Assimp 行列を分解
            m.Decompose(out var s, out var r, out var p);

            // 座標変換を適用
            UnityEngine.Vector3 pos = FbxCoordinateSystemDetector.ConvertVector(p, coordinateConversionMatrix);
            UnityEngine.Quaternion rot = FbxCoordinateSystemDetector.ConvertQuaternion(r, coordinateConversionMatrix);
            UnityEngine.Vector3 scale = new UnityEngine.Vector3(s.X, s.Y, s.Z);

            // Unity Transform に設定
            t.localPosition = pos;
            t.localRotation = rot;
            t.localScale = scale;

            if (debugMode)
            {
                Debug.Log($"{LOG_PREFIX} [DEBUG] Transform Conversion:");
                Debug.Log($"{LOG_PREFIX}   Assimp Pos: ({p.X}, {p.Y}, {p.Z})");
                Debug.Log($"{LOG_PREFIX}   Unity Pos:  {pos}");
                Debug.Log($"{LOG_PREFIX}   Assimp Rot: ({r.X}, {r.Y}, {r.Z}, {r.W})");
                Debug.Log($"{LOG_PREFIX}   Unity Rot:  {rot.eulerAngles}");
            }
        }

        /// <summary>
        /// Transform のフルパスを取得（デバッグ用）
        /// </summary>
        private string GetTransformPath(Transform t)
        {
            if (t == null)
                return "null";

            List<string> parts = new List<string>();
            Transform current = t;

            while (current != null)
            {
                parts.Insert(0, current.name);
                current = current.parent;
            }

            return string.Join("/", parts);
        }
    }
}
