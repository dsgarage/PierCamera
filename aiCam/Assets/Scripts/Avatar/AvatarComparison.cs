using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace dsgarage.Avatar
{
    /// <summary>
    /// EditorインポートされたAvatarとRuntime生成Avatarの詳細比較ツール
    /// </summary>
    public class AvatarComparison : MonoBehaviour
    {
        [Header("比較対象")]
        [Tooltip("Unity Editorで正しくインポートされたkyoko Avatar")]
        [SerializeField] private UnityEngine.Avatar editorKyokoAvatar;

        [Tooltip("Runtime生成されたAvatarを持つAnimator")]
        [SerializeField] private Animator runtimeGeneratedAnimator;

        [Header("出力設定")]
        [SerializeField] private bool outputToFile = true;
        [SerializeField] private string outputPath = "/tmp/avatar_comparison_detailed.txt";

        private StringBuilder report = new StringBuilder();

        [ContextMenu("Compare Avatars (Detailed)")]
        public void CompareAvatarsDetailed()
        {
            report.Clear();
            report.AppendLine("═══════════════════════════════════════════════════════════");
            report.AppendLine("  Avatar Detailed Comparison Report");
            report.AppendLine($"  Generated: {System.DateTime.Now}");
            report.AppendLine("═══════════════════════════════════════════════════════════");
            report.AppendLine();

            if (editorKyokoAvatar == null)
            {
                report.AppendLine("❌ ERROR: editorKyokoAvatar is not assigned");
                OutputReport();
                return;
            }

            if (runtimeGeneratedAnimator == null || runtimeGeneratedAnimator.avatar == null)
            {
                report.AppendLine("❌ ERROR: runtimeGeneratedAnimator or its avatar is not assigned");
                OutputReport();
                return;
            }

            var runtimeAvatar = runtimeGeneratedAnimator.avatar;

            // 基本情報
            CompareBasicInfo(editorKyokoAvatar, runtimeAvatar);

            // ボーンマッピング比較
            CompareBoneMappings();

            // ボーン回転比較（最重要）
            CompareBoneRotations();

            // T-Pose アライメント比較
            CompareTPoseAlignment();

            OutputReport();
        }

        private void CompareBasicInfo(UnityEngine.Avatar editorAvatar, UnityEngine.Avatar runtimeAvatar)
        {
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine("1. Basic Information");
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine($"Editor Avatar:");
            report.AppendLine($"  Name: {editorAvatar.name}");
            report.AppendLine($"  IsValid: {editorAvatar.isValid}");
            report.AppendLine($"  IsHuman: {editorAvatar.isHuman}");
            report.AppendLine();
            report.AppendLine($"Runtime Avatar:");
            report.AppendLine($"  Name: {runtimeAvatar.name}");
            report.AppendLine($"  IsValid: {runtimeAvatar.isValid}");
            report.AppendLine($"  IsHuman: {runtimeAvatar.isHuman}");
            report.AppendLine();

            if (!editorAvatar.isValid || !editorAvatar.isHuman)
            {
                report.AppendLine("⚠️  WARNING: Editor Avatar is not valid or not human!");
            }
            if (!runtimeAvatar.isValid || !runtimeAvatar.isHuman)
            {
                report.AppendLine("⚠️  WARNING: Runtime Avatar is not valid or not human!");
            }
            report.AppendLine();
        }

        private void CompareBoneMappings()
        {
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine("2. Bone Mappings Comparison");
            report.AppendLine("─────────────────────────────────────────────────────────");

            // EditorのAnimatorを探す
            var editorAnimator = FindEditorAnimator();
            if (editorAnimator == null)
            {
                report.AppendLine("⚠️  Could not find Editor Animator in scene");
                report.AppendLine("   Please ensure kyoko model with Editor Avatar is in the scene");
                report.AppendLine();
                return;
            }

            int differenceCount = 0;
            int missingInRuntime = 0;
            int missingInEditor = 0;

            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var editorBone = editorAnimator.GetBoneTransform(bone);
                var runtimeBone = runtimeGeneratedAnimator.GetBoneTransform(bone);

                if (editorBone != null && runtimeBone != null)
                {
                    if (editorBone.name != runtimeBone.name)
                    {
                        report.AppendLine($"⚠️  {bone}:");
                        report.AppendLine($"     Editor:  '{editorBone.name}'");
                        report.AppendLine($"     Runtime: '{runtimeBone.name}'");
                        differenceCount++;
                    }
                }
                else if (editorBone != null && runtimeBone == null)
                {
                    report.AppendLine($"❌ {bone}: Missing in Runtime (Editor has '{editorBone.name}')");
                    missingInRuntime++;
                }
                else if (editorBone == null && runtimeBone != null)
                {
                    report.AppendLine($"➕ {bone}: Missing in Editor (Runtime has '{runtimeBone.name}')");
                    missingInEditor++;
                }
            }

            report.AppendLine();
            report.AppendLine($"Summary:");
            report.AppendLine($"  Name differences: {differenceCount}");
            report.AppendLine($"  Missing in Runtime: {missingInRuntime}");
            report.AppendLine($"  Missing in Editor: {missingInEditor}");
            report.AppendLine();
        }

        private void CompareBoneRotations()
        {
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine("3. Bone Rotations Comparison (CRITICAL)");
            report.AppendLine("─────────────────────────────────────────────────────────");

            var editorAnimator = FindEditorAnimator();
            if (editorAnimator == null)
            {
                report.AppendLine("⚠️  Could not find Editor Animator");
                report.AppendLine();
                return;
            }

            // 重要なボーンのみチェック（肩・股関節を優先）
            var criticalBones = new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest,
                HumanBodyBones.LeftShoulder,
                HumanBodyBones.RightShoulder,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.RightLowerLeg,
            };

            float totalAngleDiff = 0f;
            int comparedCount = 0;
            var largeDifferences = new List<string>();

            foreach (var bone in criticalBones)
            {
                var editorBone = editorAnimator.GetBoneTransform(bone);
                var runtimeBone = runtimeGeneratedAnimator.GetBoneTransform(bone);

                if (editorBone != null && runtimeBone != null)
                {
                    // LocalRotation比較
                    float localAngleDiff = Quaternion.Angle(editorBone.localRotation, runtimeBone.localRotation);

                    // WorldRotation比較
                    float worldAngleDiff = Quaternion.Angle(editorBone.rotation, runtimeBone.rotation);

                    report.AppendLine($"{bone} ({editorBone.name}):");
                    report.AppendLine($"  Editor  LocalRot: {editorBone.localRotation} (Euler: {editorBone.localEulerAngles})");
                    report.AppendLine($"  Runtime LocalRot: {runtimeBone.localRotation} (Euler: {runtimeBone.localEulerAngles})");
                    report.AppendLine($"  Local Angle Diff: {localAngleDiff:F2}°");
                    report.AppendLine($"  World Angle Diff: {worldAngleDiff:F2}°");

                    totalAngleDiff += localAngleDiff;
                    comparedCount++;

                    if (localAngleDiff > 15f)
                    {
                        report.AppendLine($"  ⚠️  LARGE DIFFERENCE: {localAngleDiff:F2}° (threshold: 15°)");
                        largeDifferences.Add($"{bone}: {localAngleDiff:F2}°");
                    }

                    report.AppendLine();
                }
            }

            report.AppendLine("Summary:");
            report.AppendLine($"  Average Local Angle Diff: {(comparedCount > 0 ? totalAngleDiff / comparedCount : 0):F2}°");
            report.AppendLine($"  Bones compared: {comparedCount}");
            report.AppendLine($"  Large differences (>15°): {largeDifferences.Count}");

            if (largeDifferences.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("  Large Differences Detail:");
                foreach (var diff in largeDifferences)
                {
                    report.AppendLine($"    - {diff}");
                }
            }
            report.AppendLine();
        }

        private void CompareTPoseAlignment()
        {
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine("4. T-Pose Alignment Analysis");
            report.AppendLine("─────────────────────────────────────────────────────────");

            var editorAnimator = FindEditorAnimator();
            if (editorAnimator == null)
            {
                report.AppendLine("⚠️  Could not find Editor Animator");
                report.AppendLine();
                return;
            }

            // Hipsを基準にT-Poseチェック
            var editorHips = editorAnimator.GetBoneTransform(HumanBodyBones.Hips);
            var runtimeHips = runtimeGeneratedAnimator.GetBoneTransform(HumanBodyBones.Hips);

            if (editorHips == null || runtimeHips == null)
            {
                report.AppendLine("⚠️  Hips bone not found");
                report.AppendLine();
                return;
            }

            var editorSpine = editorAnimator.GetBoneTransform(HumanBodyBones.Spine);
            var runtimeSpine = runtimeGeneratedAnimator.GetBoneTransform(HumanBodyBones.Spine);

            if (editorSpine != null && runtimeSpine != null)
            {
                Vector3 editorUp = (editorSpine.position - editorHips.position).normalized;
                Vector3 runtimeUp = (runtimeSpine.position - runtimeHips.position).normalized;

                report.AppendLine("Spine Direction (from Hips):");
                report.AppendLine($"  Editor:  {editorUp}");
                report.AppendLine($"  Runtime: {runtimeUp}");
                report.AppendLine($"  Angle Diff: {Vector3.Angle(editorUp, runtimeUp):F2}°");
                report.AppendLine();
            }

            // 腕のT-Poseチェック
            CheckArmTPose("Left",
                editorAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                runtimeGeneratedAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                editorHips, runtimeHips, editorUp);

            CheckArmTPose("Right",
                editorAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                runtimeGeneratedAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                editorHips, runtimeHips, editorUp);

            // 脚のT-Poseチェック
            CheckLegTPose("Left",
                editorAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                runtimeGeneratedAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                editorHips, runtimeHips, editorUp);

            CheckLegTPose("Right",
                editorAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                runtimeGeneratedAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                editorHips, runtimeHips, editorUp);

            report.AppendLine();
        }

        private void CheckArmTPose(string side, Transform editorArm, Transform runtimeArm,
            Transform editorHips, Transform runtimeHips, Vector3 editorUp)
        {
            if (editorArm == null || runtimeArm == null) return;

            Vector3 editorDir = (editorArm.position - editorHips.position).normalized;
            Vector3 runtimeDir = (runtimeArm.position - runtimeHips.position).normalized;

            float editorAngle = Vector3.Angle(editorUp, editorDir);
            float runtimeAngle = Vector3.Angle(editorUp, runtimeDir);

            report.AppendLine($"{side} Upper Arm (T-Pose should be ~90° from spine):");
            report.AppendLine($"  Editor:  {editorAngle:F1}° from spine up");
            report.AppendLine($"  Runtime: {runtimeAngle:F1}° from spine up");
            report.AppendLine($"  Diff: {Mathf.Abs(editorAngle - runtimeAngle):F1}°");

            if (Mathf.Abs(editorAngle - 90f) > 15f)
                report.AppendLine($"  ⚠️  Editor arm off T-Pose");
            if (Mathf.Abs(runtimeAngle - 90f) > 15f)
                report.AppendLine($"  ⚠️  Runtime arm off T-Pose by {Mathf.Abs(runtimeAngle - 90f):F1}°");

            report.AppendLine();
        }

        private void CheckLegTPose(string side, Transform editorLeg, Transform runtimeLeg,
            Transform editorHips, Transform runtimeHips, Vector3 editorUp)
        {
            if (editorLeg == null || runtimeLeg == null) return;

            Vector3 editorDir = (editorLeg.position - editorHips.position).normalized;
            Vector3 runtimeDir = (runtimeLeg.position - runtimeHips.position).normalized;

            float editorAngle = Vector3.Angle(-editorUp, editorDir);
            float runtimeAngle = Vector3.Angle(-editorUp, runtimeDir);

            report.AppendLine($"{side} Upper Leg (T-Pose should be ~0° from spine down):");
            report.AppendLine($"  Editor:  {editorAngle:F1}° from spine down");
            report.AppendLine($"  Runtime: {runtimeAngle:F1}° from spine down");
            report.AppendLine($"  Diff: {Mathf.Abs(editorAngle - runtimeAngle):F1}°");

            if (editorAngle > 20f)
                report.AppendLine($"  ⚠️  Editor leg off T-Pose");
            if (runtimeAngle > 20f)
                report.AppendLine($"  ⚠️  Runtime leg off T-Pose by {runtimeAngle:F1}°");

            report.AppendLine();
        }

        private Animator FindEditorAnimator()
        {
            // Scene内のすべてのAnimatorを探す
            var allAnimators = FindObjectsOfType<Animator>();
            foreach (var anim in allAnimators)
            {
                if (anim.avatar == editorKyokoAvatar)
                {
                    return anim;
                }
            }
            return null;
        }

        private void OutputReport()
        {
            string reportText = report.ToString();
            Debug.Log(reportText);

            if (outputToFile)
            {
                try
                {
                    System.IO.File.WriteAllText(outputPath, reportText);
                    Debug.Log($"[AvatarComparison] Detailed comparison report saved to: {outputPath}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[AvatarComparison] Failed to write report: {e.Message}");
                }
            }
        }
    }
}
