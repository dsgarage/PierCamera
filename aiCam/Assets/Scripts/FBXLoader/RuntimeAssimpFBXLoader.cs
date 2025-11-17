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

            UnityEngine.Debug.Log($"{LOG_PREFIX} Scene loaded successfully");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Meshes: {scene.MeshCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Materials: {scene.MaterialCount}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}   Animations: {scene.AnimationCount}");

            // FBX Global Settings (座標系情報)
            UnityEngine.Debug.Log($"{LOG_PREFIX} === FBX Global Settings ===");

            // FBX座標系を自動検出
            coordProfile = FbxCoordinateSystemDetector.ExtractFbxCoordProfile(scene);
            coordinateConversionMatrix = FbxCoordinateSystemDetector.BuildConversionMatrix(coordProfile);

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
            UnityEngine.Debug.Log($"{LOG_PREFIX} === End Global Settings ===");

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

            UnityEngine.Debug.Log($"{LOG_PREFIX}   Node: {node.Name}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}     LocalPosition: {nodeObject.transform.localPosition}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}     LocalRotation: {nodeObject.transform.localRotation.eulerAngles}");
            UnityEngine.Debug.Log($"{LOG_PREFIX}     LocalScale: {nodeObject.transform.localScale}");

            // 子ノードを再帰的に処理
            for (int i = 0; i < node.ChildCount; i++)
            {
                BuildBoneHierarchy(node.Children[i], nodeObject.transform, scene);
            }
        }

        /// <summary>
        /// AssimpのMatrix4x4からUnityのTransformに変換（座標系変換適用）
        /// </summary>
        private void SetTransformFromAssimpMatrix(Transform transform, Assimp.Matrix4x4 assimpMatrix)
        {
            // Assimpの行列を分解
            Assimp.Vector3D scale;
            Assimp.Quaternion rotation;
            Assimp.Vector3D position;
            assimpMatrix.Decompose(out scale, out rotation, out position);

            // 座標系変換を適用してUnityのTransformに設定
            transform.localPosition = FbxCoordinateSystemDetector.ConvertVector(position, coordinateConversionMatrix);
            transform.localRotation = FbxCoordinateSystemDetector.ConvertQuaternion(rotation, coordinateConversionMatrix);
            transform.localScale = new UnityEngine.Vector3(scale.X, scale.Y, scale.Z); // スケールは変換不要
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
    }
}
