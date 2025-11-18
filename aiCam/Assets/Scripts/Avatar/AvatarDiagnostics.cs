using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace dsgarage.Avatar
{
    /// <summary>
    /// Unity EditorでインポートしたAvatarとランタイム生成したAvatarの差分を診断
    /// </summary>
    public class AvatarDiagnostics : MonoBehaviour
    {
        [Header("Comparison Targets")]
        [Tooltip("Unity Editorで正しくインポートされたAvatar")]
        [SerializeField] private UnityEngine.Avatar editorImportedAvatar;

        [Tooltip("ランタイムで生成されたAvatarを持つAnimator")]
        [SerializeField] private Animator runtimeGeneratedAnimator;

        [Header("Debug Output")]
        [SerializeField] private bool logToFile = true;
        [SerializeField] private string outputPath = "/tmp/avatar_comparison.txt";

        private StringBuilder log = new StringBuilder();

        public void CompareAvatars()
        {
            log.Clear();
            log.AppendLine("=== Avatar Comparison Report ===");
            log.AppendLine($"Timestamp: {System.DateTime.Now}");
            log.AppendLine();

            if (editorImportedAvatar == null)
            {
                log.AppendLine("ERROR: editorImportedAvatar is not assigned");
                OutputLog();
                return;
            }

            if (runtimeGeneratedAnimator == null || runtimeGeneratedAnimator.avatar == null)
            {
                log.AppendLine("ERROR: runtimeGeneratedAnimator or its avatar is not assigned");
                OutputLog();
                return;
            }

            var runtimeAvatar = runtimeGeneratedAnimator.avatar;

            log.AppendLine("## Basic Information");
            log.AppendLine($"Editor Avatar - IsValid: {editorImportedAvatar.isValid}, IsHuman: {editorImportedAvatar.isHuman}");
            log.AppendLine($"Runtime Avatar - IsValid: {runtimeAvatar.isValid}, IsHuman: {runtimeAvatar.isHuman}");
            log.AppendLine();

            // HumanDescription比較
            CompareHumanDescriptions();

            // ボーン階層比較
            CompareBoneHierarchies();

            // SkeletonBone回転比較
            CompareSkeletonBones();

            OutputLog();
        }

        private void CompareHumanDescriptions()
        {
            log.AppendLine("## HumanDescription Comparison");
            log.AppendLine("(Note: Unity doesn't expose HumanDescription from built Avatar at runtime)");
            log.AppendLine("This comparison requires Editor scripting or serialized data.");
            log.AppendLine();
        }

        private void CompareBoneHierarchies()
        {
            log.AppendLine("## Bone Hierarchy Comparison");

            var editorTransform = FindRootTransform(editorImportedAvatar);
            var runtimeTransform = runtimeGeneratedAnimator.transform;

            if (editorTransform == null)
            {
                log.AppendLine("WARNING: Could not find editor avatar root transform");
                return;
            }

            log.AppendLine($"Editor Root: {editorTransform.name}");
            log.AppendLine($"Runtime Root: {runtimeTransform.name}");
            log.AppendLine();

            // Compare Humanoid bone mappings
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var editorBone = FindBoneInHierarchy(editorTransform, bone.ToString());
                var runtimeBone = runtimeGeneratedAnimator.GetBoneTransform(bone);

                if (editorBone != null && runtimeBone != null)
                {
                    log.AppendLine($"{bone}:");
                    log.AppendLine($"  Editor:  {editorBone.name} - LocalRot: {editorBone.localRotation}");
                    log.AppendLine($"  Runtime: {runtimeBone.name} - LocalRot: {runtimeBone.localRotation}");

                    float angleDiff = Quaternion.Angle(editorBone.localRotation, runtimeBone.localRotation);
                    if (angleDiff > 1f)
                    {
                        log.AppendLine($"  ⚠️ ROTATION DIFFERENCE: {angleDiff:F2}°");
                    }
                    log.AppendLine();
                }
                else if (runtimeBone != null)
                {
                    log.AppendLine($"{bone}: Runtime only - {runtimeBone.name}");
                }
            }
        }

        private void CompareSkeletonBones()
        {
            log.AppendLine("## SkeletonBone Rotation Analysis");
            log.AppendLine("Analyzing runtime-generated bones:");
            log.AppendLine();

            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var transform = runtimeGeneratedAnimator.GetBoneTransform(bone);
                if (transform != null)
                {
                    // T-Poseからのずれをチェック
                    Vector3 forward = transform.rotation * Vector3.forward;
                    Vector3 up = transform.rotation * Vector3.up;
                    Vector3 right = transform.rotation * Vector3.right;

                    log.AppendLine($"{bone} ({transform.name}):");
                    log.AppendLine($"  LocalRotation: {transform.localRotation}");
                    log.AppendLine($"  WorldRotation: {transform.rotation}");
                    log.AppendLine($"  Forward: {forward}");
                    log.AppendLine($"  Up: {up}");
                    log.AppendLine($"  Right: {right}");

                    // Check for common issues
                    if (IsArmBone(bone))
                    {
                        float armAngle = Vector3.Angle(right, Vector3.right);
                        if (armAngle > 30f)
                        {
                            log.AppendLine($"  ⚠️ Arm not in T-Pose: {armAngle:F1}° from horizontal");
                        }
                    }

                    log.AppendLine();
                }
            }
        }

        private Transform FindRootTransform(UnityEngine.Avatar avatar)
        {
            // Unity Editorでインポートされたモデルを探す
            // 通常、Scene内の他のGameObjectに存在する
            var allAnimators = FindObjectsOfType<Animator>();
            foreach (var anim in allAnimators)
            {
                if (anim.avatar == avatar)
                {
                    return anim.transform;
                }
            }
            return null;
        }

        private Transform FindBoneInHierarchy(Transform root, string boneName)
        {
            if (root.name == boneName) return root;

            foreach (Transform child in root)
            {
                var result = FindBoneInHierarchy(child, boneName);
                if (result != null) return result;
            }

            return null;
        }

        private bool IsArmBone(HumanBodyBones bone)
        {
            return bone == HumanBodyBones.LeftUpperArm || bone == HumanBodyBones.RightUpperArm ||
                   bone == HumanBodyBones.LeftLowerArm || bone == HumanBodyBones.RightLowerArm;
        }

        private void OutputLog()
        {
            Debug.Log(log.ToString());

            if (logToFile)
            {
                try
                {
                    System.IO.File.WriteAllText(outputPath, log.ToString());
                    Debug.Log($"[AvatarDiagnostics] Comparison report saved to: {outputPath}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[AvatarDiagnostics] Failed to write log file: {e.Message}");
                }
            }
        }

        [ContextMenu("Compare Avatars")]
        public void CompareAvatarsMenu()
        {
            CompareAvatars();
        }
    }
}
