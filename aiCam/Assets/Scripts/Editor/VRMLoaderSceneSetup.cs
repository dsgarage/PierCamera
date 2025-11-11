using UnityEngine;
using UnityEditor;
using AICam.FBXLoader;

namespace AICam.Editor
{
    /// <summary>
    /// FBXLoaderシーンにVRMローダーをセットアップするエディタースクリプト
    /// </summary>
    public class VRMLoaderSceneSetup
    {
        [MenuItem("Tools/AICam/Setup VRM Loader in FBXLoader Scene")]
        public static void SetupVRMLoader()
        {
            // ModelSpawnPointを検索または作成
            GameObject spawnPoint = GameObject.Find("ModelSpawnPoint");

            if (spawnPoint == null)
            {
                spawnPoint = new GameObject("ModelSpawnPoint");
                spawnPoint.transform.position = new Vector3(0, 0.5f, 1.5f);
                Debug.Log("[VRMLoaderSetup] Created ModelSpawnPoint");
            }
            else
            {
                Debug.Log("[VRMLoaderSetup] ModelSpawnPoint already exists");
            }

            // RuntimeManagerを検索
            var runtimeManager = GameObject.Find("RuntimeManager");
            if (runtimeManager == null)
            {
                Debug.LogError("[VRMLoaderSetup] RuntimeManager not found in scene!");
                return;
            }

            // RuntimeFBXLoaderBridgeコンポーネントを取得
            var loaderBridge = runtimeManager.GetComponent<RuntimeFBXLoaderBridge>();
            if (loaderBridge == null)
            {
                Debug.LogError("[VRMLoaderSetup] RuntimeFBXLoaderBridge component not found!");
                return;
            }

            // SerializedObjectを使って private フィールドに値を設定
            SerializedObject so = new SerializedObject(loaderBridge);

            // modelParentフィールドを設定
            SerializedProperty modelParentProp = so.FindProperty("modelParent");
            if (modelParentProp != null)
            {
                modelParentProp.objectReferenceValue = spawnPoint.transform;
                Debug.Log("[VRMLoaderSetup] Set modelParent to ModelSpawnPoint");
            }

            // modelPositionフィールドを設定
            SerializedProperty modelPositionProp = so.FindProperty("modelPosition");
            if (modelPositionProp != null)
            {
                modelPositionProp.vector3Value = Vector3.zero;
            }

            // modelRotationフィールドを設定
            SerializedProperty modelRotationProp = so.FindProperty("modelRotation");
            if (modelRotationProp != null)
            {
                modelRotationProp.vector3Value = new Vector3(0, 180, 0);
            }

            // modelScaleフィールドを設定
            SerializedProperty modelScaleProp = so.FindProperty("modelScale");
            if (modelScaleProp != null)
            {
                modelScaleProp.vector3Value = Vector3.one;
            }

            // 変更を適用
            so.ApplyModifiedProperties();

            // シーンをダーティマークして保存を促す
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            Debug.Log("[VRMLoaderSetup] VRM Loader setup completed successfully!");
            EditorUtility.DisplayDialog(
                "VRM Loader Setup",
                "VRM Loader has been set up successfully!\n\n" +
                "- ModelSpawnPoint created at (0, 0.5, 1.5)\n" +
                "- RuntimeFBXLoaderBridge configured\n\n" +
                "Don't forget to save the scene!",
                "OK"
            );
        }
    }
}
