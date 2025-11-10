using UnityEngine;
using UnityEditor;
using System.IO;

public class CreateVRMMaterials : EditorWindow
{
    [MenuItem("Tools/Create VRM Materials")]
    static void CreateMaterials()
    {
        string resourcesPath = "Assets/Resources/VRMMaterials";

        // フォルダが存在しない場合は作成
        if (!AssetDatabase.IsValidFolder(resourcesPath))
        {
            string[] folders = resourcesPath.Split('/');
            string currentPath = folders[0];
            for (int i = 1; i < folders.Length; i++)
            {
                string nextPath = currentPath + "/" + folders[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                }
                currentPath = nextPath;
            }
        }

        // VRM 0.x - MToon
        CreateMaterial("VRM/MToon", "MToon", resourcesPath);

        // VRM 1.0 - MToon10
        CreateMaterial("VRM10/MToon10", "MToon10", resourcesPath);
        CreateMaterial("VRM10/MToon10 (URP)", "MToon10_URP", resourcesPath);

        // UniGLTF - Unlit
        CreateMaterial("UniGLTF/UniUnlit", "UniUnlit", resourcesPath);

        // Standard (Unity built-in)
        CreateMaterial("Standard", "Standard", resourcesPath);

        // Unlit系
        CreateMaterial("Unlit/Texture", "Unlit_Texture", resourcesPath);
        CreateMaterial("Unlit/Color", "Unlit_Color", resourcesPath);

        // UnityChan Shader
        CreateMaterial("UnityChanShader/Clothing", "UnityChan_Clothing", resourcesPath);
        CreateMaterial("UnityChanShader/Hair", "UnityChan_Hair", resourcesPath);
        CreateMaterial("UnityChanShader/Skin", "UnityChan_Skin", resourcesPath);
        CreateMaterial("UnityChanShader/Eye", "UnityChan_Eye", resourcesPath);

        // lilToon
        CreateMaterial("lilToon", "lilToon", resourcesPath);
        CreateMaterial("lilToon [Outline]", "lilToon_Outline", resourcesPath);
        CreateMaterial("lilToon [Transparent]", "lilToon_Transparent", resourcesPath);
        CreateMaterial("lilToon [Cutout]", "lilToon_Cutout", resourcesPath);
        CreateMaterial("lilToon [OnePassTransparent]", "lilToon_OnePassTransparent", resourcesPath);
        CreateMaterial("lilToon [TwoPassTransparent]", "lilToon_TwoPassTransparent", resourcesPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("✅ VRM Materials created successfully in " + resourcesPath);
    }

    static void CreateMaterial(string shaderName, string materialName, string path)
    {
        Shader shader = Shader.Find(shaderName);
        if (shader == null)
        {
            Debug.LogWarning($"⚠️ Shader not found: {shaderName}");
            return;
        }

        Material material = new Material(shader);
        string assetPath = $"{path}/{materialName}.mat";

        AssetDatabase.CreateAsset(material, assetPath);
        Debug.Log($"✅ Created material: {materialName} with shader: {shaderName}");
    }
}
