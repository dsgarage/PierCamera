using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// VRM用のマテリアルを起動時にロードしてプールしておくクラス
/// </summary>
public class VRMMaterialPool : MonoBehaviour
{
    private static VRMMaterialPool instance;
    public static VRMMaterialPool Instance => instance;

    private Dictionary<string, Material> materialPool = new Dictionary<string, Material>();

    [Header("ロードするマテリアルのパス")]
    [SerializeField] private string materialFolderPath = "VRMMaterials";

    [Header("デバッグ情報")]
    [SerializeField] private bool showDebugLog = true;

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);

        LoadAllMaterials();
    }

    /// <summary>
    /// Resourcesフォルダから全てのVRMマテリアルをロード
    /// </summary>
    private void LoadAllMaterials()
    {
        if (showDebugLog)
            Debug.Log($"[VRMMaterialPool] Loading materials from Resources/{materialFolderPath}...");

        // Resourcesフォルダから全てのマテリアルをロード
        Material[] materials = Resources.LoadAll<Material>(materialFolderPath);

        if (materials == null || materials.Length == 0)
        {
            Debug.LogWarning($"[VRMMaterialPool] No materials found in Resources/{materialFolderPath}");
            return;
        }

        foreach (Material mat in materials)
        {
            if (mat == null || mat.shader == null)
            {
                Debug.LogWarning($"[VRMMaterialPool] Invalid material or shader found");
                continue;
            }

            string shaderName = mat.shader.name;

            if (!materialPool.ContainsKey(shaderName))
            {
                materialPool[shaderName] = mat;

                if (showDebugLog)
                    Debug.Log($"[VRMMaterialPool] ✅ Loaded: {mat.name} (Shader: {shaderName})");
            }
            else
            {
                if (showDebugLog)
                    Debug.LogWarning($"[VRMMaterialPool] Duplicate shader found: {shaderName}");
            }
        }

        if (showDebugLog)
            Debug.Log($"[VRMMaterialPool] Total materials loaded: {materialPool.Count}");
    }

    /// <summary>
    /// シェーダー名からマテリアルを取得
    /// </summary>
    public Material GetMaterial(string shaderName)
    {
        if (materialPool.TryGetValue(shaderName, out Material mat))
        {
            return mat;
        }

        if (showDebugLog)
            Debug.LogWarning($"[VRMMaterialPool] Material not found for shader: {shaderName}");

        return null;
    }

    /// <summary>
    /// プールされているマテリアルの数を取得
    /// </summary>
    public int GetPooledMaterialCount()
    {
        return materialPool.Count;
    }

    /// <summary>
    /// プールされている全てのシェーダー名を取得
    /// </summary>
    public string[] GetAllShaderNames()
    {
        string[] names = new string[materialPool.Count];
        materialPool.Keys.CopyTo(names, 0);
        return names;
    }

    /// <summary>
    /// プール情報をログ出力
    /// </summary>
    public void PrintPoolInfo()
    {
        Debug.Log($"[VRMMaterialPool] === Material Pool Info ===");
        Debug.Log($"[VRMMaterialPool] Total materials: {materialPool.Count}");

        foreach (var kvp in materialPool)
        {
            Debug.Log($"[VRMMaterialPool]   - {kvp.Key} -> {kvp.Value.name}");
        }
    }
}
