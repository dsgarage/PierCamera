#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System;

namespace AICam.AvatarBuilder
{
    /// <summary>
    /// Editor-importedしたFBX AvatarからAvatar定義を抽出するツール
    /// </summary>
    public class AvatarTemplateExtractor : EditorWindow
    {
        private Avatar sourceAvatar;
        private string templateName = "MyCharacter_AvatarTemplate";
        private string savePath = "Assets/AvatarTemplates";

        [MenuItem("Window/AICam/Avatar Template Extractor")]
        public static void ShowWindow()
        {
            GetWindow<AvatarTemplateExtractor>("Avatar Template Extractor");
        }

        private void OnGUI()
        {
            GUILayout.Label("Avatar Template Extractor", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "1. ProjectウィンドウでFBXを選択してEditorでインポート（Humanoid設定）\n" +
                "2. そのFBXのAvatarをここにドラッグ\n" +
                "3. 「Extract Avatar Definition」をクリック\n" +
                "4. 生成されたAvatarTemplateをRuntimeFBXLoaderBridgeにアサイン",
                MessageType.Info);

            EditorGUILayout.Space();

            sourceAvatar = (Avatar)EditorGUILayout.ObjectField(
                "Source Avatar",
                sourceAvatar,
                typeof(Avatar),
                false);

            templateName = EditorGUILayout.TextField("Template Name", templateName);
            savePath = EditorGUILayout.TextField("Save Path", savePath);

            EditorGUILayout.Space();

            GUI.enabled = sourceAvatar != null;
            if (GUILayout.Button("Extract Avatar Definition", GUILayout.Height(40)))
            {
                ExtractAvatarDefinition();
            }
            GUI.enabled = true;

            EditorGUILayout.Space();

            if (GUILayout.Button("Create AvatarTemplates Folder"))
            {
                CreateAvatarTemplatesFolder();
            }
        }

        private void ExtractAvatarDefinition()
        {
            if (sourceAvatar == null)
            {
                EditorUtility.DisplayDialog("Error", "Please assign a source Avatar", "OK");
                return;
            }

            if (!sourceAvatar.isHuman)
            {
                EditorUtility.DisplayDialog("Error", "Source Avatar must be Humanoid", "OK");
                return;
            }

            try
            {
                // HumanDescriptionを取得
                HumanDescription humanDesc = sourceAvatar.humanDescription;

                // AvatarTemplate ScriptableObject作成
                AvatarTemplate template = ScriptableObject.CreateInstance<AvatarTemplate>();

                // データコピー
                template.humanBones = humanDesc.human;
                template.skeletonBones = humanDesc.skeleton;
                template.upperArmTwist = humanDesc.upperArmTwist;
                template.lowerArmTwist = humanDesc.lowerArmTwist;
                template.upperLegTwist = humanDesc.upperLegTwist;
                template.lowerLegTwist = humanDesc.lowerLegTwist;
                template.armStretch = humanDesc.armStretch;
                template.legStretch = humanDesc.legStretch;
                template.feetSpacing = humanDesc.feetSpacing;
                template.hasTranslationDoF = humanDesc.hasTranslationDoF;

                // メタデータ
                template.sourceFBXName = sourceAvatar.name;
                template.extractedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // 保存先パス確認
                if (!AssetDatabase.IsValidFolder(savePath))
                {
                    System.IO.Directory.CreateDirectory(savePath);
                    AssetDatabase.Refresh();
                }

                // アセット保存
                string assetPath = $"{savePath}/{templateName}.asset";
                AssetDatabase.CreateAsset(template, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 選択状態にする
                EditorGUIUtility.PingObject(template);
                Selection.activeObject = template;

                // ログ出力
                Debug.Log($"<color=green>[AvatarTemplateExtractor] ✓ Successfully extracted Avatar definition</color>");
                Debug.Log($"  Source: {sourceAvatar.name}");
                Debug.Log($"  HumanBones: {template.humanBones.Length}");
                Debug.Log($"  SkeletonBones: {template.skeletonBones.Length}");
                Debug.Log($"  Saved to: {assetPath}");

                EditorUtility.DisplayDialog(
                    "Success",
                    $"Avatar template extracted successfully!\n\nSaved to: {assetPath}\n\n" +
                    "Next: Assign this template to RuntimeFBXLoaderBridge",
                    "OK");
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarTemplateExtractor] Failed to extract Avatar: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to extract Avatar:\n{e.Message}", "OK");
            }
        }

        private void CreateAvatarTemplatesFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/AvatarTemplates"))
            {
                AssetDatabase.CreateFolder("Assets", "AvatarTemplates");
                AssetDatabase.Refresh();
                Debug.Log("[AvatarTemplateExtractor] Created Assets/AvatarTemplates folder");
                EditorUtility.DisplayDialog("Success", "Created Assets/AvatarTemplates folder", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Info", "Assets/AvatarTemplates folder already exists", "OK");
            }
        }
    }
}
#endif
