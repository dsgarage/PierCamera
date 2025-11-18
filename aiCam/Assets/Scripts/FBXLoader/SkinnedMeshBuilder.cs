using System.Collections.Generic;
using UnityEngine;
using AICam.FBXLoader;

namespace arCam.FBXLoader
{
    /// <summary>
    /// STEP 4: SkinnedMeshRenderer を構築するクラス
    ///
    /// 責務:
    /// - MeshData と BoneData から SkinnedMeshRenderer を作成
    /// - bones 配列を構築
    /// - BindPose を計算（OffsetMatrix に座標変換を適用）
    /// - SkinnedMeshRenderer の各プロパティを正しい順序で設定
    ///
    /// 重要制約:
    /// - SkinnedMeshRenderer の設定順序を厳守:
    ///   1. bones
    ///   2. sharedMesh（BlendShape は既に登録済み）
    ///   3. rootBone
    ///   4. sharedMaterial
    ///   5. updateWhenOffscreen
    /// - bones.Length == bindposes.Length を検証
    /// - boneWeights.Length == mesh.vertexCount を検証
    /// </summary>
    public class SkinnedMeshBuilder
    {
        private const string LOG_PREFIX = "[SkinnedMeshBuilder]";

        private readonly GameObject targetGameObject;
        private readonly Dictionary<string, Transform> boneNameToTransform;
        private readonly UnityEngine.Matrix4x4 coordinateConversionMatrix;
        private readonly bool debugMode;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="gameObject">SkinnedMeshRenderer を追加する GameObject</param>
        /// <param name="boneDict">ボーン名 → Transform の辞書</param>
        /// <param name="conversionMatrix">座標変換行列</param>
        /// <param name="debugMode">デバッグログを有効化</param>
        public SkinnedMeshBuilder(
            GameObject gameObject,
            Dictionary<string, Transform> boneDict,
            UnityEngine.Matrix4x4 conversionMatrix,
            bool debugMode = false)
        {
            targetGameObject = gameObject;
            boneNameToTransform = boneDict;
            coordinateConversionMatrix = conversionMatrix;
            this.debugMode = debugMode;
        }

        /// <summary>
        /// SkinnedMeshRenderer を構築
        /// </summary>
        /// <param name="meshData">STEP 2 で作成された MeshData</param>
        /// <param name="boneData">STEP 3 で作成された BoneData</param>
        /// <param name="rootBoneName">ルートボーンの名前（通常は "Hips"）</param>
        /// <param name="materials">マテリアル配列</param>
        /// <returns>作成された SkinnedMeshRenderer</returns>
        public SkinnedMeshRenderer Build(
            MeshData meshData,
            BoneData boneData,
            string rootBoneName,
            Material[] materials = null)
        {
            Debug.Log($"{LOG_PREFIX} === STEP 4: Building SkinnedMeshRenderer ===");
            Debug.Log($"{LOG_PREFIX}   GameObject: {targetGameObject.name}");
            Debug.Log($"{LOG_PREFIX}   Root bone: {rootBoneName}");

            // STEP 4-1: bones 配列を構築
            Transform[] bones = BuildBonesArray(boneData);

            // STEP 4-2: BindPose を計算
            UnityEngine.Matrix4x4[] bindposes = BuildBindPoses(boneData, bones);

            // STEP 4-3: Mesh に BoneWeight と BindPose を設定
            SetupMeshBoneData(meshData, boneData, bindposes);

            // STEP 4-4: SkinnedMeshRenderer を作成・設定
            SkinnedMeshRenderer smr = CreateSkinnedMeshRenderer(
                meshData,
                bones,
                rootBoneName,
                materials);

            Debug.Log($"{LOG_PREFIX} === STEP 4 Complete ===");

            return smr;
        }

        /// <summary>
        /// STEP 4-1: bones 配列を構築
        /// </summary>
        private Transform[] BuildBonesArray(BoneData boneData)
        {
            Debug.Log($"{LOG_PREFIX} [STEP 4-1] Building bones array");
            Debug.Log($"{LOG_PREFIX}   Total unique bones: {boneData.allUniqueBoneNames.Count}");
            Debug.Log($"{LOG_PREFIX}   Cached bone transforms: {boneNameToTransform.Count}");

            Transform[] bones = new Transform[boneData.allUniqueBoneNames.Count];
            int foundCount = 0;
            int notFoundCount = 0;

            for (int i = 0; i < boneData.allUniqueBoneNames.Count; i++)
            {
                string boneName = boneData.allUniqueBoneNames[i];

                if (boneNameToTransform.TryGetValue(boneName, out Transform boneTransform))
                {
                    bones[i] = boneTransform;
                    foundCount++;

                    if (debugMode)
                    {
                        Debug.Log($"{LOG_PREFIX}   Bone[{i}]: {boneName}");
                        Debug.Log($"{LOG_PREFIX}     → Path: {GetTransformPath(boneTransform)}");
                    }
                }
                else
                {
                    bones[i] = null;
                    notFoundCount++;
                    Debug.LogError($"{LOG_PREFIX}   [ERROR] Bone[{i}]: {boneName} NOT FOUND in hierarchy!");
                }
            }

            Debug.Log($"{LOG_PREFIX} [STEP 4-1 Complete]");
            Debug.Log($"{LOG_PREFIX}   Found: {foundCount}/{boneData.allUniqueBoneNames.Count}");

            if (notFoundCount > 0)
            {
                Debug.LogError($"{LOG_PREFIX}   [ERROR] NOT FOUND: {notFoundCount} bones missing!");
            }

            return bones;
        }

        /// <summary>
        /// STEP 4-2: BindPose を計算
        /// ⚠️ ここで初めて OffsetMatrix に座標変換を適用
        /// </summary>
        private UnityEngine.Matrix4x4[] BuildBindPoses(BoneData boneData, Transform[] bones)
        {
            Debug.Log($"{LOG_PREFIX} [STEP 4-2] Building BindPoses");
            Debug.Log($"{LOG_PREFIX}   Total bones: {bones.Length}");
            Debug.Log($"{LOG_PREFIX}   Source: Assimp OffsetMatrix with coordinate conversion");

            UnityEngine.Matrix4x4[] bindposes = new UnityEngine.Matrix4x4[bones.Length];
            int validCount = 0;

            for (int i = 0; i < boneData.allUniqueBoneNames.Count; i++)
            {
                string boneName = boneData.allUniqueBoneNames[i];

                if (bones[i] != null && boneData.boneNameToOffsetMatrix.TryGetValue(boneName, out Assimp.Matrix4x4 offsetMatrix))
                {
                    // OffsetMatrix に座標変換を適用して BindPose を計算
                    bindposes[i] = FbxCoordinateSystemDetector.ConvertAssimpMatrix(offsetMatrix, coordinateConversionMatrix);
                    validCount++;

                    if (debugMode)
                    {
                        Debug.Log($"{LOG_PREFIX}   BindPose[{i}]: {bones[i].name}");
                        Debug.Log($"{LOG_PREFIX}     → Bone Path: {GetTransformPath(bones[i])}");

                        UnityEngine.Vector3 pos = bindposes[i].GetPosition();
                        UnityEngine.Quaternion rot = bindposes[i].rotation;
                        Debug.Log($"{LOG_PREFIX}     → BindPose Matrix: pos={pos}, rot={rot.eulerAngles}");
                    }
                }
                else
                {
                    bindposes[i] = UnityEngine.Matrix4x4.identity;
                    Debug.LogError($"{LOG_PREFIX}   [ERROR] BindPose[{i}]: {boneName} - NULL BONE or NO OFFSET MATRIX - using identity");
                }
            }

            Debug.Log($"{LOG_PREFIX} [STEP 4-2 Complete]");
            Debug.Log($"{LOG_PREFIX}   Valid bindposes: {validCount}/{bones.Length}");

            return bindposes;
        }

        /// <summary>
        /// STEP 4-3: Mesh に BoneWeight と BindPose を設定
        /// </summary>
        private void SetupMeshBoneData(MeshData meshData, BoneData boneData, UnityEngine.Matrix4x4[] bindposes)
        {
            Debug.Log($"{LOG_PREFIX} [STEP 4-3] Setting up Mesh bone data");

            // 検証: boneWeights.Length == mesh.vertexCount
            if (boneData.boneWeights.Length != meshData.unityMesh.vertexCount)
            {
                Debug.LogError($"{LOG_PREFIX} [ERROR] BoneWeight count mismatch!");
                Debug.LogError($"{LOG_PREFIX}   boneWeights.Length: {boneData.boneWeights.Length}");
                Debug.LogError($"{LOG_PREFIX}   mesh.vertexCount: {meshData.unityMesh.vertexCount}");
            }
            else
            {
                Debug.Log($"{LOG_PREFIX}   BoneWeights: {boneData.boneWeights.Length} (matches vertexCount)");
            }

            // 検証: bindposes.Length == bones.Length
            Debug.Log($"{LOG_PREFIX}   BindPoses: {bindposes.Length}");

            // Mesh に設定
            meshData.unityMesh.boneWeights = boneData.boneWeights;
            meshData.unityMesh.bindposes = bindposes;

            Debug.Log($"{LOG_PREFIX} [STEP 4-3 Complete]");
        }

        /// <summary>
        /// STEP 4-4: SkinnedMeshRenderer を作成・設定
        /// ⚠️ 設定順序を厳守
        /// </summary>
        private SkinnedMeshRenderer CreateSkinnedMeshRenderer(
            MeshData meshData,
            Transform[] bones,
            string rootBoneName,
            Material[] materials)
        {
            Debug.Log($"{LOG_PREFIX} [STEP 4-4] Creating SkinnedMeshRenderer");
            Debug.Log($"{LOG_PREFIX}   Setting order: bones → sharedMesh → rootBone → sharedMaterial → updateWhenOffscreen");

            // SkinnedMeshRenderer を追加
            SkinnedMeshRenderer smr = targetGameObject.AddComponent<SkinnedMeshRenderer>();

            // 1. bones を設定
            smr.bones = bones;
            Debug.Log($"{LOG_PREFIX}   [1/5] bones: {bones.Length} transforms");

            // 2. sharedMesh を設定（BlendShape は既に登録済み）
            smr.sharedMesh = meshData.unityMesh;
            Debug.Log($"{LOG_PREFIX}   [2/5] sharedMesh: {meshData.unityMesh.name}");
            Debug.Log($"{LOG_PREFIX}     → vertices: {meshData.unityMesh.vertexCount}");
            Debug.Log($"{LOG_PREFIX}     → triangles: {meshData.unityMesh.triangles.Length / 3}");
            Debug.Log($"{LOG_PREFIX}     → blendShapeCount: {meshData.unityMesh.blendShapeCount}");

            // 3. rootBone を設定
            Transform rootBone = null;
            if (!string.IsNullOrEmpty(rootBoneName) && boneNameToTransform.TryGetValue(rootBoneName, out rootBone))
            {
                smr.rootBone = rootBone;
                Debug.Log($"{LOG_PREFIX}   [3/5] rootBone: {rootBoneName} → {GetTransformPath(rootBone)}");
            }
            else
            {
                // rootBone が見つからない場合は最初のボーンを使用
                if (bones.Length > 0 && bones[0] != null)
                {
                    smr.rootBone = bones[0];
                    Debug.LogWarning($"{LOG_PREFIX}   [3/5] rootBone '{rootBoneName}' not found, using first bone: {bones[0].name}");
                }
                else
                {
                    Debug.LogError($"{LOG_PREFIX}   [3/5] [ERROR] No valid rootBone found!");
                }
            }

            // 4. sharedMaterial を設定
            if (materials != null && materials.Length > 0)
            {
                smr.sharedMaterials = materials;
                Debug.Log($"{LOG_PREFIX}   [4/5] sharedMaterials: {materials.Length} materials");
                for (int i = 0; i < materials.Length; i++)
                {
                    Debug.Log($"{LOG_PREFIX}     [{i}] {materials[i]?.name ?? "null"}");
                }
            }
            else
            {
                Debug.LogWarning($"{LOG_PREFIX}   [4/5] No materials provided");
            }

            // 5. updateWhenOffscreen を設定
            smr.updateWhenOffscreen = true;
            Debug.Log($"{LOG_PREFIX}   [5/5] updateWhenOffscreen: true");

            Debug.Log($"{LOG_PREFIX} [STEP 4-4 Complete]");

            // 最終検証
            ValidateSkinnedMeshRenderer(smr);

            return smr;
        }

        /// <summary>
        /// SkinnedMeshRenderer の最終検証
        /// </summary>
        private void ValidateSkinnedMeshRenderer(SkinnedMeshRenderer smr)
        {
            Debug.Log($"{LOG_PREFIX} [Validation]");

            bool isValid = true;

            // bones.Length == bindposes.Length
            if (smr.bones.Length != smr.sharedMesh.bindposes.Length)
            {
                Debug.LogError($"{LOG_PREFIX}   [ERROR] bones.Length ({smr.bones.Length}) != bindposes.Length ({smr.sharedMesh.bindposes.Length})");
                isValid = false;
            }
            else
            {
                Debug.Log($"{LOG_PREFIX}   ✓ bones.Length == bindposes.Length: {smr.bones.Length}");
            }

            // boneWeights.Length == vertexCount
            if (smr.sharedMesh.boneWeights.Length != smr.sharedMesh.vertexCount)
            {
                Debug.LogError($"{LOG_PREFIX}   [ERROR] boneWeights.Length ({smr.sharedMesh.boneWeights.Length}) != vertexCount ({smr.sharedMesh.vertexCount})");
                isValid = false;
            }
            else
            {
                Debug.Log($"{LOG_PREFIX}   ✓ boneWeights.Length == vertexCount: {smr.sharedMesh.vertexCount}");
            }

            // rootBone exists
            if (smr.rootBone == null)
            {
                Debug.LogError($"{LOG_PREFIX}   [ERROR] rootBone is null");
                isValid = false;
            }
            else
            {
                Debug.Log($"{LOG_PREFIX}   ✓ rootBone: {smr.rootBone.name}");
            }

            // null bones check
            int nullBoneCount = 0;
            for (int i = 0; i < smr.bones.Length; i++)
            {
                if (smr.bones[i] == null)
                {
                    nullBoneCount++;
                }
            }

            if (nullBoneCount > 0)
            {
                Debug.LogError($"{LOG_PREFIX}   [ERROR] {nullBoneCount} null bones found in bones array!");
                isValid = false;
            }
            else
            {
                Debug.Log($"{LOG_PREFIX}   ✓ No null bones");
            }

            if (isValid)
            {
                Debug.Log($"{LOG_PREFIX} [Validation] ✓ All checks passed!");
            }
            else
            {
                Debug.LogError($"{LOG_PREFIX} [Validation] ✗ Validation FAILED!");
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
