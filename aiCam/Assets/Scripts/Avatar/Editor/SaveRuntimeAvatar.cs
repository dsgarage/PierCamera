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

            // Avatarをコピーして保存
            // Note: Avatarは複雑な内部構造を持つため、単純なコピーでは完全に機能しない場合があります
            // 代わりに、元のAvatarへの参照として保存します

            try
            {
                // Runtime Avatarは直接アセット化できないため、
                // プレハブとしてAnimator全体を保存する方法を推奨
                string prefabPath = Path.Combine(savePath, avatarName + "_Prefab.prefab");

                if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
                {
                    AssetDatabase.DeleteAsset(prefabPath);
                }

                // Animatorを持つGameObjectをプレハブ化
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(targetAnimator.gameObject, prefabPath);

                if (prefab != null)
                {
                    EditorUtility.DisplayDialog(
                        "Success",
                        $"Avatar付きプレハブを保存しました:\n{prefabPath}\n\n" +
                        $"このプレハブをシーンに配置することで、\n" +
                        $"EditorインポートAvatarと同様に使用できます。",
                        "OK");

                    // 保存したアセットを選択
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                }
                else
                {
                    EditorUtility.DisplayDialog("Error", "プレハブの作成に失敗しました", "OK");
                }
            }
            catch (System.Exception e)
            {
                EditorUtility.DisplayDialog("Error", $"保存中にエラーが発生しました:\n{e.Message}", "OK");
                Debug.LogError($"[SaveRuntimeAvatar] {e}");
            }

            AssetDatabase.Refresh();
        }
    }
}
