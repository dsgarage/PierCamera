using UnityEngine;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Rendering;
using System.Collections.Generic;

/// <summary>
/// シェーダーの事前コンパイルとバリアント最適化
/// </summary>
public class ShaderPrecompiler : MonoBehaviour
{
    [MenuItem("Tools/Shader/Precompile All Shaders")]
    public static void PrecompileAllShaders()
    {
        Debug.Log("[ShaderPrecompiler] Starting shader precompilation...");

        // シェーダーをすべて検索
        string[] shaderGuids = AssetDatabase.FindAssets("t:Shader");
        int total = shaderGuids.Length;
        int current = 0;

        foreach (string guid in shaderGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);

            if (shader != null)
            {
                current++;
                EditorUtility.DisplayProgressBar(
                    "Precompiling Shaders",
                    $"Processing: {shader.name} ({current}/{total})",
                    (float)current / total
                );

                // シェーダーをウォームアップ
                ShaderUtil.CompilePass(AssetDatabase.LoadAssetAtPath<Material>(path), 0, true);
            }
        }

        EditorUtility.ClearProgressBar();
        Debug.Log($"[ShaderPrecompiler] Completed! Processed {current} shaders.");
    }

    [MenuItem("Tools/Shader/Create Shader Variant Collection")]
    public static void CreateShaderVariantCollection()
    {
        // 新しいShaderVariantCollectionを作成
        ShaderVariantCollection svc = new ShaderVariantCollection();

        // プロジェクト内のすべてのマテリアルを検索
        string[] materialGuids = AssetDatabase.FindAssets("t:Material");
        HashSet<Shader> processedShaders = new HashSet<Shader>();
        int addedCount = 0;

        foreach (string guid in materialGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (mat != null && mat.shader != null)
            {
                try
                {
                    ShaderVariantCollection.ShaderVariant variant = new ShaderVariantCollection.ShaderVariant(
                        mat.shader,
                        UnityEngine.Rendering.PassType.ForwardBase,
                        mat.shaderKeywords
                    );

                    if (svc.Add(variant))
                    {
                        addedCount++;
                        processedShaders.Add(mat.shader);
                    }
                }
                catch (System.Exception)
                {
                    // 一部のシェーダーはバリアント追加に失敗することがある
                }
            }
        }

        // アセットとして保存
        string savePath = "Assets/Settings/ProjectShaderVariants.shadervariants";

        // ディレクトリ作成
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
        {
            AssetDatabase.CreateFolder("Assets", "Settings");
        }

        AssetDatabase.CreateAsset(svc, savePath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[ShaderPrecompiler] Created ShaderVariantCollection at {savePath}");
        Debug.Log($"[ShaderPrecompiler] Added {addedCount} variants from {processedShaders.Count} shaders");

        // Graphics Settingsに追加するかどうか確認
        if (EditorUtility.DisplayDialog(
            "Add to Graphics Settings?",
            "Do you want to add this ShaderVariantCollection to Graphics Settings for preloading?",
            "Yes", "No"))
        {
            AddToGraphicsSettings(savePath);
        }
    }

    private static void AddToGraphicsSettings(string svcPath)
    {
        ShaderVariantCollection svc = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(svcPath);
        if (svc == null) return;

        // GraphicsSettings経由でPreloaded Shadersに追加
        SerializedObject graphicsSettings = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]
        );

        SerializedProperty preloadedShaders = graphicsSettings.FindProperty("m_PreloadedShaders");

        // 既に追加されているか確認
        bool alreadyExists = false;
        for (int i = 0; i < preloadedShaders.arraySize; i++)
        {
            if (preloadedShaders.GetArrayElementAtIndex(i).objectReferenceValue == svc)
            {
                alreadyExists = true;
                break;
            }
        }

        if (!alreadyExists)
        {
            preloadedShaders.InsertArrayElementAtIndex(preloadedShaders.arraySize);
            preloadedShaders.GetArrayElementAtIndex(preloadedShaders.arraySize - 1).objectReferenceValue = svc;
            graphicsSettings.ApplyModifiedProperties();
            Debug.Log("[ShaderPrecompiler] Added to Graphics Settings preloaded shaders");
        }
        else
        {
            Debug.Log("[ShaderPrecompiler] Already in Graphics Settings");
        }
    }

    [MenuItem("Tools/Shader/Clear Shader Cache")]
    public static void ClearShaderCache()
    {
        // シェーダーキャッシュをクリア
        string cachePath = System.IO.Path.Combine(
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
            "Unity/caches"
        );

        if (EditorUtility.DisplayDialog(
            "Clear Shader Cache",
            $"This will clear the shader cache.\nPath: {cachePath}\n\nContinue?",
            "Clear", "Cancel"))
        {
            try
            {
                if (System.IO.Directory.Exists(cachePath))
                {
                    System.IO.Directory.Delete(cachePath, true);
                    Debug.Log("[ShaderPrecompiler] Shader cache cleared");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ShaderPrecompiler] Failed to clear cache: {e.Message}");
            }
        }
    }
}
