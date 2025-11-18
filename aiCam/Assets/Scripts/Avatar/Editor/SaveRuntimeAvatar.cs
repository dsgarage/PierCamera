using UnityEngine;
using UnityEditor;
using System.IO;

namespace dsgarage.Avatar.Editor
{
    /// <summary>
    /// Runtime生成されたAvatarをプロジェクトにアセットとして保存するエディタツール
    /// </summary>
    public class SaveRuntimeAvatar : EditorWindow
    {
        private Animator targetAnimator;
        private string savePath = "Assets/GeneratedAvatars/";
        private string avatarName = "RuntimeGeneratedAvatar";

        [MenuItem("dsgarage/Avatar/Save Runtime Avatar")]
        public static void ShowWindow()
        {
            GetWindow<SaveRuntimeAvatar>("Save Runtime Avatar");
        }

        private void OnGUI()
        {
            GUILayout.Label("Save Runtime Avatar", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "Runtime生成されたAvatarをプロジェクトのアセットとして保存します。\n" +
                "保存後は、Editorインポート版と同様に比較や検証が可能になります。",
                MessageType.Info);

            EditorGUILayout.Space();

            // Animator選択
            targetAnimator = (Animator)EditorGUILayout.ObjectField(
                "Target Animator",
                targetAnimator,
                typeof(Animator),
                true);

            EditorGUILayout.Space();

            // 保存パス設定
            EditorGUILayout.LabelField("保存先設定", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            savePath = EditorGUILayout.TextField("Save Path", savePath);
            if (GUILayout.Button("Browse", GUILayout.Width(80)))
            {
                string selectedPath = EditorUtility.SaveFolderPanel("Save Avatar", "Assets", "");
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    // Assetsフォルダからの相対パスに変換
                    if (selectedPath.StartsWith(Application.dataPath))
                    {
                        savePath = "Assets" + selectedPath.Substring(Application.dataPath.Length);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            avatarName = EditorGUILayout.TextField("Avatar Name", avatarName);

            EditorGUILayout.Space();

            // 保存ボタン
            GUI.enabled = targetAnimator != null && targetAnimator.avatar != null;

            if (GUILayout.Button("Save Avatar", GUILayout.Height(40)))
            {
                SaveAvatar();
            }

            GUI.enabled = true;

            // 現在の状態表示
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("現在の状態", EditorStyles.boldLabel);

            if (targetAnimator == null)
            {
                EditorGUILayout.HelpBox("Animatorが選択されていません", MessageType.Warning);
            }
            else if (targetAnimator.avatar == null)
            {
                EditorGUILayout.HelpBox("選択されたAnimatorにAvatarがありません", MessageType.Warning);
            }
            else
            {
                var avatar = targetAnimator.avatar;
                EditorGUILayout.LabelField($"Avatar: {avatar.name}");
                EditorGUILayout.LabelField($"IsValid: {avatar.isValid}");
                EditorGUILayout.LabelField($"IsHuman: {avatar.isHuman}");
            }
        }

        private void SaveAvatar()
        {
            if (targetAnimator == null || targetAnimator.avatar == null)
            {
                EditorUtility.DisplayDialog("Error", "有効なAnimatorとAvatarを選択してください", "OK");
                return;
            }

            // ディレクトリ作成
            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
                AssetDatabase.Refresh();
            }

            var avatar = targetAnimator.avatar;
            string assetPath = Path.Combine(savePath, avatarName + ".asset");

            // 既存のアセットをチェック
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Avatar>(assetPath) != null)
            {
                if (!EditorUtility.DisplayDialog(
                    "Confirm Overwrite",
                    $"Avatar '{assetPath}' は既に存在します。上書きしますか？",
                    "Overwrite",
                    "Cancel"))
                {
                    return;
                }

                // 既存アセットを削除
                AssetDatabase.DeleteAsset(assetPath);
            }

            try
            {
                // Runtime生成されたAvatarを直接アセットとして保存
                // Unity 2019.3以降では、Runtime Avatarもアセット化可能
                UnityEngine.Avatar avatarCopy = Object.Instantiate(avatar);
                avatarCopy.name = avatarName;

                AssetDatabase.CreateAsset(avatarCopy, assetPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                // 保存されたAvatarを検証
                var savedAvatar = AssetDatabase.LoadAssetAtPath<UnityEngine.Avatar>(assetPath);
                if (savedAvatar != null && savedAvatar.isValid)
                {
                    EditorUtility.DisplayDialog(
                        "Success",
                        $"Avatarアセットを保存しました:\n{assetPath}\n\n" +
                        $"IsValid: {savedAvatar.isValid}\n" +
                        $"IsHuman: {savedAvatar.isHuman}\n\n" +
                        $"このAvatarは他のAnimatorに割り当てて使用できます。",
                        "OK");

                    // 保存したアセットを選択
                    Selection.activeObject = savedAvatar;
                    EditorGUIUtility.PingObject(savedAvatar);

                    Debug.Log($"[SaveRuntimeAvatar] Avatar saved successfully: {assetPath}");
                }
                else
                {
                    EditorUtility.DisplayDialog(
                        "Warning",
                        $"Avatarは保存されましたが、検証に失敗しました。\n" +
                        $"AssetPath: {assetPath}\n\n" +
                        $"保存されたAvatarが正しく機能しない可能性があります。",
                        "OK");

                    Debug.LogWarning($"[SaveRuntimeAvatar] Avatar saved but validation failed: {assetPath}");
                }
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog(
                    "Error",
                    $"Avatar保存中にエラーが発生しました:\n{e.Message}\n\n" +
                    $"Runtime生成されたAvatarは直接アセット化できない場合があります。\n" +
                    $"代わりにGameObject全体をPrefabとして保存することを検討してください。",
                    "OK");
                Debug.LogError($"[SaveRuntimeAvatar] Error saving avatar: {e}");
            }
        }
    }
}
