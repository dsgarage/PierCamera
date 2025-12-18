using UnityEngine;
using UnityEditor;
using System.Linq;

namespace AICam.Editor
{
    /// <summary>
    /// Issue #407: CameraCaptureControllerにAOCを設定するエディタスクリプト
    /// </summary>
    public static class SetupPoseOverrideControllers
    {
        [MenuItem("Tools/AICam/Setup Pose Override Controllers")]
        public static void Setup()
        {
            // CameraCaptureControllerを検索
            var controller = Object.FindFirstObjectByType<AICam.UI.CameraCaptureController>();
            if (controller == null)
            {
                Debug.LogError("❌ CameraCaptureController not found in scene!");
                return;
            }

            // AOCファイルのパス（順序: p012, b010, b011, b020-b024）
            string[] aocPaths = new string[]
            {
                "Assets/Animation/PoseOverrides/p012.overrideController",
                "Assets/Animation/PoseOverrides/b010.overrideController",
                "Assets/Animation/PoseOverrides/b011.overrideController",
                "Assets/Animation/PoseOverrides/b020.overrideController",
                "Assets/Animation/PoseOverrides/b021.overrideController",
                "Assets/Animation/PoseOverrides/b022.overrideController",
                "Assets/Animation/PoseOverrides/b023.overrideController",
                "Assets/Animation/PoseOverrides/b024.overrideController"
            };

            // AOCをロード
            var aocs = aocPaths
                .Select(path => AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path))
                .ToArray();

            // nullチェック
            for (int i = 0; i < aocs.Length; i++)
            {
                if (aocs[i] == null)
                {
                    Debug.LogError($"❌ Failed to load AOC: {aocPaths[i]}");
                    return;
                }
                Debug.Log($"✓ Loaded AOC [{i}]: {aocs[i].name}");
            }

            // SerializedObjectを使用してフィールドを設定
            var serializedObject = new SerializedObject(controller);
            var property = serializedObject.FindProperty("poseOverrideControllers");

            if (property == null)
            {
                Debug.LogError("❌ poseOverrideControllers property not found!");
                return;
            }

            property.arraySize = aocs.Length;
            for (int i = 0; i < aocs.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = aocs[i];
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(controller);

            Debug.Log($"✅ Successfully set {aocs.Length} AnimatorOverrideControllers to CameraCaptureController!");
            Debug.Log("📝 Don't forget to save the scene!");
        }
    }
}
