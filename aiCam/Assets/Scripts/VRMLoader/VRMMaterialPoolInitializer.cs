using UnityEngine;

/// <summary>
/// VRMMaterialPoolを自動的に初期化するクラス
/// RuntimeInitializeOnLoadMethodでゲーム起動時に実行される
/// </summary>
public static class VRMMaterialPoolInitializer
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Initialize()
    {
        // VRMMaterialPoolがシーンに存在しない場合、自動的に作成
        if (VRMMaterialPool.Instance == null)
        {
            GameObject poolObject = new GameObject("VRMMaterialPool");
            poolObject.AddComponent<VRMMaterialPool>();

            Debug.Log("[VRMMaterialPoolInitializer] ✅ VRMMaterialPool automatically created");
        }
        else
        {
            Debug.Log("[VRMMaterialPoolInitializer] VRMMaterialPool already exists in scene");
        }
    }
}
