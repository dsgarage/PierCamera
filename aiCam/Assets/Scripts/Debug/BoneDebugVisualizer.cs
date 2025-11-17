using UnityEngine;
using System.Collections.Generic;

namespace AICam.DebugTools
{
    /// <summary>
    /// ボーン階層とAvatar情報をシーンビューで可視化するデバッグコンポーネント
    /// </summary>
    public class BoneDebugVisualizer : MonoBehaviour
    {
        [Header("可視化設定")]
        [SerializeField] private bool showBoneHierarchy = true;
        [SerializeField] private bool showBoneNames = true;
        [SerializeField] private bool showBoneRotations = false;
        [SerializeField] private bool showHumanoidBones = true;
        [SerializeField] private bool showBonePositions = false;

        [Header("表示スタイル")]
        [SerializeField] private Color boneLineColor = Color.cyan;
        [SerializeField] private Color humanoidBoneColor = Color.green;
        [SerializeField] private Color rootBoneColor = Color.red;
        [SerializeField] private float boneLineWidth = 2f;
        [SerializeField] private float boneSphereRadius = 0.02f;

        [Header("対象")]
        [SerializeField] private Transform rootBone;
        [SerializeField] private Animator animator;

        private Dictionary<HumanBodyBones, Transform> humanoidBoneMap;

        private void OnValidate()
        {
            if (animator == null)
                animator = GetComponent<Animator>();

            if (rootBone == null && animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                rootBone = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (rootBone != null)
                    rootBone = rootBone.root;
            }
        }

        private void OnDrawGizmos()
        {
            if (!showBoneHierarchy && !showHumanoidBones)
                return;

            if (rootBone == null)
                return;

            // Humanoidボーンマップを構築
            if (showHumanoidBones && animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                BuildHumanoidBoneMap();
            }

            // ボーン階層を描画
            DrawBoneRecursive(rootBone);
        }

        private void BuildHumanoidBoneMap()
        {
            if (humanoidBoneMap == null)
                humanoidBoneMap = new Dictionary<HumanBodyBones, Transform>();
            else
                humanoidBoneMap.Clear();

            for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                HumanBodyBones bone = (HumanBodyBones)i;
                Transform boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform != null)
                {
                    humanoidBoneMap[bone] = boneTransform;
                }
            }
        }

        private void DrawBoneRecursive(Transform bone)
        {
            if (bone == null)
                return;

            bool isHumanoidBone = humanoidBoneMap != null && humanoidBoneMap.ContainsValue(bone);
            bool isRootBone = bone == rootBone;

            // ボーンの色を決定
            Color boneColor = boneLineColor;
            if (isRootBone)
                boneColor = rootBoneColor;
            else if (isHumanoidBone && showHumanoidBones)
                boneColor = humanoidBoneColor;

            // 親への線を描画
            if (bone.parent != null && showBoneHierarchy)
            {
                Gizmos.color = boneColor;
                Gizmos.DrawLine(bone.position, bone.parent.position);
            }

            // ボーン位置に球を描画
            Gizmos.color = boneColor;
            Gizmos.DrawSphere(bone.position, boneSphereRadius);

            // ボーン名を表示
            if (showBoneNames)
            {
                DrawLabel(bone.position, bone.name, boneColor);
            }

            // ボーン回転を表示
            if (showBoneRotations)
            {
                Vector3 eulerAngles = bone.localEulerAngles;
                string rotationText = $"R({eulerAngles.x:F1}, {eulerAngles.y:F1}, {eulerAngles.z:F1})";
                DrawLabel(bone.position + Vector3.up * 0.05f, rotationText, Color.yellow);
            }

            // ボーン位置を表示
            if (showBonePositions)
            {
                Vector3 pos = bone.localPosition;
                string posText = $"P({pos.x:F2}, {pos.y:F2}, {pos.z:F2})";
                DrawLabel(bone.position - Vector3.up * 0.05f, posText, Color.cyan);
            }

            // Humanoidボーン名を表示
            if (showHumanoidBones && isHumanoidBone)
            {
                HumanBodyBones humanBone = GetHumanBodyBone(bone);
                if (humanBone != HumanBodyBones.LastBone)
                {
                    DrawLabel(bone.position + Vector3.down * 0.08f, $"[{humanBone}]", Color.green);
                }
            }

            // 子ボーンへ再帰
            foreach (Transform child in bone)
            {
                DrawBoneRecursive(child);
            }
        }

        private HumanBodyBones GetHumanBodyBone(Transform bone)
        {
            if (humanoidBoneMap == null)
                return HumanBodyBones.LastBone;

            foreach (var kvp in humanoidBoneMap)
            {
                if (kvp.Value == bone)
                    return kvp.Key;
            }
            return HumanBodyBones.LastBone;
        }

        private void DrawLabel(Vector3 position, string text, Color color)
        {
#if UNITY_EDITOR
            GUIStyle style = new GUIStyle();
            style.normal.textColor = color;
            style.fontSize = 10;
            UnityEditor.Handles.Label(position, text, style);
#endif
        }

        /// <summary>
        /// ボーン階層情報をコンソールに出力
        /// </summary>
        [ContextMenu("Print Bone Hierarchy")]
        public void PrintBoneHierarchy()
        {
            if (rootBone == null)
            {
                UnityEngine.Debug.LogWarning("[BoneDebugVisualizer] Root bone is not set");
                return;
            }

            UnityEngine.Debug.Log("=== Bone Hierarchy ===");
            PrintBoneRecursive(rootBone, 0);
        }

        private void PrintBoneRecursive(Transform bone, int depth)
        {
            string indent = new string(' ', depth * 2);
            string humanoidTag = "";

            if (humanoidBoneMap != null && humanoidBoneMap.ContainsValue(bone))
            {
                HumanBodyBones humanBone = GetHumanBodyBone(bone);
                humanoidTag = $" [Humanoid: {humanBone}]";
            }

            Vector3 euler = bone.localEulerAngles;
            UnityEngine.Debug.Log($"{indent}├─ {bone.name}{humanoidTag} Rot({euler.x:F1}, {euler.y:F1}, {euler.z:F1})");

            foreach (Transform child in bone)
            {
                PrintBoneRecursive(child, depth + 1);
            }
        }

        /// <summary>
        /// Avatar情報をコンソールに出力
        /// </summary>
        [ContextMenu("Print Avatar Info")]
        public void PrintAvatarInfo()
        {
            if (animator == null || animator.avatar == null)
            {
                UnityEngine.Debug.LogWarning("[BoneDebugVisualizer] Animator or Avatar is not set");
                return;
            }

            Avatar avatar = animator.avatar;
            UnityEngine.Debug.Log("=== Avatar Info ===");
            UnityEngine.Debug.Log($"Name: {avatar.name}");
            UnityEngine.Debug.Log($"IsValid: {avatar.isValid}");
            UnityEngine.Debug.Log($"IsHuman: {avatar.isHuman}");

            if (avatar.isHuman)
            {
                UnityEngine.Debug.Log("\n=== Humanoid Bones ===");
                int mappedCount = 0;
                for (int i = 0; i < (int)HumanBodyBones.LastBone; i++)
                {
                    HumanBodyBones bone = (HumanBodyBones)i;
                    Transform boneTransform = animator.GetBoneTransform(bone);
                    if (boneTransform != null)
                    {
                        mappedCount++;
                        Vector3 euler = boneTransform.localEulerAngles;
                        UnityEngine.Debug.Log($"  {bone,-25} → {boneTransform.name,-30} Rot({euler.x:F1}, {euler.y:F1}, {euler.z:F1})");
                    }
                }
                UnityEngine.Debug.Log($"\nTotal mapped bones: {mappedCount}/{(int)HumanBodyBones.LastBone}");
            }
        }
    }
}
