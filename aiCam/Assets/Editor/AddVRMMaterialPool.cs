using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;

public class AddVRMMaterialPool : EditorWindow
{
    [MenuItem("Tools/Add VRM Material Pool to Scene")]
    static void AddMaterialPoolToScene()
    {
        // AR Sessionを探す
        GameObject arSession = GameObject.Find("AR Session");

        if (arSession == null)
        {
            Debug.LogWarning("AR Session not found. Creating new GameObject for VRMMaterialPool...");
            arSession = new GameObject("VRMMaterialPool");
        }

        // 既に存在するかチェック
        VRMMaterialPool existingPool = arSession.GetComponent<VRMMaterialPool>();
        if (existingPool != null)
        {
            Debug.LogWarning("VRMMaterialPool already exists on " + arSession.name);
            Selection.activeGameObject = arSession;
            return;
        }

        // コンポーネントを追加
        VRMMaterialPool pool = arSession.AddComponent<VRMMaterialPool>();

        // シーンをダーティとしてマーク
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        Debug.Log($"✅ VRMMaterialPool added to {arSession.name}");

        // Hierarchyで選択
        Selection.activeGameObject = arSession;
    }
}
