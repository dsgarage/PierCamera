using UnityEngine;

public class PathChecker : MonoBehaviour
{
    void Start()
    {
        Debug.Log($"[PathChecker] persistentDataPath: {Application.persistentDataPath}");
        Debug.Log($"[PathChecker] ExtractedUnityPackage: {System.IO.Path.Combine(Application.persistentDataPath, "ExtractedUnityPackage")}");

        string extractPath = System.IO.Path.Combine(Application.persistentDataPath, "ExtractedUnityPackage");
        if (System.IO.Directory.Exists(extractPath))
        {
            Debug.Log($"[PathChecker] ExtractedUnityPackage directory EXISTS");

            string[] files = System.IO.Directory.GetFiles(extractPath, "*", System.IO.SearchOption.TopDirectoryOnly);
            Debug.Log($"[PathChecker] Files in root: {files.Length}");
            foreach (string file in files)
            {
                Debug.Log($"[PathChecker]   - {System.IO.Path.GetFileName(file)}");
            }

            string[] dirs = System.IO.Directory.GetDirectories(extractPath, "*", System.IO.SearchOption.TopDirectoryOnly);
            Debug.Log($"[PathChecker] Directories in root: {dirs.Length}");
            foreach (string dir in dirs)
            {
                Debug.Log($"[PathChecker]   - {System.IO.Path.GetFileName(dir)}/");
            }
        }
        else
        {
            Debug.LogWarning($"[PathChecker] ExtractedUnityPackage directory NOT FOUND");
        }

        // ShaderDB確認
        Debug.Log($"[PathChecker] Checking ShaderDB...");
        var shaderDB = Resources.Load<UnityEngine.ScriptableObject>("ShaderDB");
        if (shaderDB == null)
        {
            Debug.LogError($"[PathChecker] ShaderDB.asset NOT FOUND in Resources");
        }
        else
        {
            Debug.Log($"[PathChecker] ShaderDB.asset found: {shaderDB.name}");
        }
    }
}
