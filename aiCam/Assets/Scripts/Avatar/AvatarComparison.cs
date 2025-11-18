using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace dsgarage.Avatar
{
    /// <summary>
    /// Avatar詳細比較ツール
    /// - Avatar直接指定: 保存済みAvatarアセット同士の比較
    /// - Animator経由: シーン内のAnimator経由での比較（ボーン回転・T-Pose分析）
    /// </summary>
    public class AvatarComparison : MonoBehaviour
    {
        [Header("比較対象 - Avatar直接指定")]
        [Tooltip("比較元Avatar（Editorインポート or 保存済みRuntime Avatar）")]
        [SerializeField] private UnityEngine.Avatar referenceAvatar;

        [Tooltip("比較先Avatar（Runtime生成 or 保存済みAvatar）")]
        [SerializeField] private UnityEngine.Avatar targetAvatar;

        [Header("比較対象 - Animator経由（オプション）")]
        [Tooltip("比較元Animator（Avatarが割り当てられていない場合に使用）")]
        [SerializeField] private Animator referenceAnimator;

        [Tooltip("比較先Animator（Avatarが割り当てられていない場合に使用）")]
        [SerializeField] private Animator targetAnimator;

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

            // Avatar参照を取得（直接 or Animator経由）
            UnityEngine.Avatar refAvatar = referenceAvatar;
            if (refAvatar == null && referenceAnimator != null)
            {
                refAvatar = referenceAnimator.avatar;
            }

            UnityEngine.Avatar tgtAvatar = targetAvatar;
            if (tgtAvatar == null && targetAnimator != null)
            {
                tgtAvatar = targetAnimator.avatar;
            }

            // 検証
            if (refAvatar == null)
            {
                report.AppendLine("❌ ERROR: Reference Avatar is not assigned");
                report.AppendLine("   referenceAvatar または referenceAnimator を設定してください");
                OutputReport();
                return;
            }

            if (tgtAvatar == null)
            {
                report.AppendLine("❌ ERROR: Target Avatar is not assigned");
                report.AppendLine("   targetAvatar または targetAnimator を設定してください");
                OutputReport();
                return;
            }

            // 基本情報
            CompareBasicInfo(refAvatar, tgtAvatar);

            // ボーンマッピング比較（Animatorが必要）
            if (referenceAnimator != null && targetAnimator != null)
            {
                CompareBoneMappings();
                CompareBoneRotations();
                CompareTPoseAlignment();
            }
            else
            {
                report.AppendLine("─────────────────────────────────────────────────────────");
                report.AppendLine("2-4. Bone Rotations / T-Pose Comparison");
                report.AppendLine("─────────────────────────────────────────────────────────");
                report.AppendLine("⚠️  Animatorが設定されていないため、ボーン回転比較はスキップされました。");
                report.AppendLine("   詳細な回転比較を行うには、referenceAnimator と targetAnimator を設定してください。");
                report.AppendLine();
            }

            OutputReport();
        }

        private void CompareBasicInfo(UnityEngine.Avatar refAvatar, UnityEngine.Avatar tgtAvatar)
        {
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine("1. Basic Information");
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine($"Reference Avatar:");
            report.AppendLine($"  Name: {refAvatar.name}");
            report.AppendLine($"  IsValid: {refAvatar.isValid}");
            report.AppendLine($"  IsHuman: {refAvatar.isHuman}");
            report.AppendLine();
            report.AppendLine($"Target Avatar:");
            report.AppendLine($"  Name: {tgtAvatar.name}");
            report.AppendLine($"  IsValid: {tgtAvatar.isValid}");
            report.AppendLine($"  IsHuman: {tgtAvatar.isHuman}");
            report.AppendLine();

            if (!refAvatar.isValid || !refAvatar.isHuman)
            {
                report.AppendLine("⚠️  WARNING: Reference Avatar is not valid or not human!");
            }
            if (!tgtAvatar.isValid || !tgtAvatar.isHuman)
            {
                report.AppendLine("⚠️  WARNING: Target Avatar is not valid or not human!");
            }
            report.AppendLine();
        }

        private void CompareBoneMappings()
        {
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine("2. Bone Mappings Comparison");
            report.AppendLine("─────────────────────────────────────────────────────────");

            if (referenceAnimator == null || targetAnimator == null)
            {
                report.AppendLine("⚠️  Animators not set, skipping bone mapping comparison");
                report.AppendLine();
                return;
            }

            int differenceCount = 0;
            int missingInTarget = 0;
            int missingInReference = 0;

            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;

                var refBone = referenceAnimator.GetBoneTransform(bone);
                var tgtBone = targetAnimator.GetBoneTransform(bone);

                if (refBone != null && tgtBone != null)
                {
                    if (refBone.name != tgtBone.name)
                    {
                        report.AppendLine($"⚠️  {bone}:");
                        report.AppendLine($"     Reference: '{refBone.name}'");
                        report.AppendLine($"     Target:    '{tgtBone.name}'");
                        differenceCount++;
                    }
                }
                else if (refBone != null && tgtBone == null)
                {
                    report.AppendLine($"❌ {bone}: Missing in Target (Reference has '{refBone.name}')");
                    missingInTarget++;
                }
                else if (refBone == null && tgtBone != null)
                {
                    report.AppendLine($"➕ {bone}: Missing in Reference (Target has '{tgtBone.name}')");
                    missingInReference++;
                }
            }

            report.AppendLine();
            report.AppendLine($"Summary:");
            report.AppendLine($"  Name differences: {differenceCount}");
            report.AppendLine($"  Missing in Target: {missingInTarget}");
            report.AppendLine($"  Missing in Reference: {missingInReference}");
            report.AppendLine();
        }

        private void CompareBoneRotations()
        {
            report.AppendLine("─────────────────────────────────────────────────────────");
            report.AppendLine("3. Bone Rotations Comparison (CRITICAL)");
            report.AppendLine("─────────────────────────────────────────────────────────");

            if (referenceAnimator == null || targetAnimator == null)
            {
                report.AppendLine("⚠️  Animators not set, skipping rotation comparison");
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
                var refBone = referenceAnimator.GetBoneTransform(bone);
                var tgtBone = targetAnimator.GetBoneTransform(bone);

                if (refBone != null && tgtBone != null)
                {
                    // LocalRotation比較
                    float localAngleDiff = Quaternion.Angle(refBone.localRotation, tgtBone.localRotation);

                    // WorldRotation比較
                    float worldAngleDiff = Quaternion.Angle(refBone.rotation, tgtBone.rotation);

                    report.AppendLine($"{bone} ({refBone.name}):");
                    report.AppendLine($"  Reference LocalRot: {refBone.localRotation} (Euler: {refBone.localEulerAngles})");
                    report.AppendLine($"  Target    LocalRot: {tgtBone.localRotation} (Euler: {tgtBone.localEulerAngles})");
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

            if (referenceAnimator == null || targetAnimator == null)
            {
                report.AppendLine("⚠️  Animators not set, skipping T-pose comparison");
                report.AppendLine();
                return;
            }

            // Hipsを基準にT-Poseチェック
            var refHips = referenceAnimator.GetBoneTransform(HumanBodyBones.Hips);
            var tgtHips = targetAnimator.GetBoneTransform(HumanBodyBones.Hips);

            if (refHips == null || tgtHips == null)
            {
                report.AppendLine("⚠️  Hips bone not found");
                report.AppendLine();
                return;
            }

            var refSpine = referenceAnimator.GetBoneTransform(HumanBodyBones.Spine);
            var tgtSpine = targetAnimator.GetBoneTransform(HumanBodyBones.Spine);

            // refUpをスコープ外で定義（CheckArmTPose/CheckLegTPoseで使用）
            Vector3 refUp = Vector3.up; // デフォルト値

            if (refSpine != null && tgtSpine != null)
            {
                refUp = (refSpine.position - refHips.position).normalized;
                Vector3 tgtUp = (tgtSpine.position - tgtHips.position).normalized;

                report.AppendLine("Spine Direction (from Hips):");
                report.AppendLine($"  Reference: {refUp}");
                report.AppendLine($"  Target:    {tgtUp}");
                report.AppendLine($"  Angle Diff: {Vector3.Angle(refUp, tgtUp):F2}°");
                report.AppendLine();
            }

            // 腕のT-Poseチェック
            CheckArmTPose("Left",
                referenceAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperArm),
                refHips, tgtHips, refUp);

            CheckArmTPose("Right",
                referenceAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperArm),
                refHips, tgtHips, refUp);

            // 脚のT-Poseチェック
            CheckLegTPose("Left",
                referenceAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                targetAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg),
                refHips, tgtHips, refUp);

            CheckLegTPose("Right",
                referenceAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                targetAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg),
                refHips, tgtHips, refUp);

            report.AppendLine();
        }

        private void CheckArmTPose(string side, Transform refArm, Transform tgtArm,
            Transform refHips, Transform tgtHips, Vector3 refUp)
        {
            if (refArm == null || tgtArm == null) return;

            Vector3 refDir = (refArm.position - refHips.position).normalized;
            Vector3 tgtDir = (tgtArm.position - tgtHips.position).normalized;

            float refAngle = Vector3.Angle(refUp, refDir);
            float tgtAngle = Vector3.Angle(refUp, tgtDir);

            report.AppendLine($"{side} Upper Arm (T-Pose should be ~90° from spine):");
            report.AppendLine($"  Reference: {refAngle:F1}° from spine up");
            report.AppendLine($"  Target:    {tgtAngle:F1}° from spine up");
            report.AppendLine($"  Diff: {Mathf.Abs(refAngle - tgtAngle):F1}°");

            if (Mathf.Abs(refAngle - 90f) > 15f)
                report.AppendLine($"  ⚠️  Reference arm off T-Pose");
            if (Mathf.Abs(tgtAngle - 90f) > 15f)
                report.AppendLine($"  ⚠️  Target arm off T-Pose by {Mathf.Abs(tgtAngle - 90f):F1}°");

            report.AppendLine();
        }

        private void CheckLegTPose(string side, Transform refLeg, Transform tgtLeg,
            Transform refHips, Transform tgtHips, Vector3 refUp)
        {
            if (refLeg == null || tgtLeg == null) return;

            Vector3 refDir = (refLeg.position - refHips.position).normalized;
            Vector3 tgtDir = (tgtLeg.position - tgtHips.position).normalized;

            float refAngle = Vector3.Angle(-refUp, refDir);
            float tgtAngle = Vector3.Angle(-refUp, tgtDir);

            report.AppendLine($"{side} Upper Leg (T-Pose should be ~0° from spine down):");
            report.AppendLine($"  Reference: {refAngle:F1}° from spine down");
            report.AppendLine($"  Target:    {tgtAngle:F1}° from spine down");
            report.AppendLine($"  Diff: {Mathf.Abs(refAngle - tgtAngle):F1}°");

            if (refAngle > 20f)
                report.AppendLine($"  ⚠️  Reference leg off T-Pose");
            if (tgtAngle > 20f)
                report.AppendLine($"  ⚠️  Target leg off T-Pose by {tgtAngle:F1}°");

            report.AppendLine();
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
