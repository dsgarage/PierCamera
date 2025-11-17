#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace TriLibCore.Editor
{
    [InitializeOnLoad]
    public static class InputSystemChecker
    {
        static InputSystemChecker()
        {
            EditorSceneManager.sceneOpened += (_, __) => Check();
        }

        private static void Check()
        {

#if ENABLE_INPUT_SYSTEM
            if (Object.FindFirstObjectByType<StandaloneInputModule>() != null)
            {
                EditorUtility.DisplayDialog(
                    "Update EventSystem",
                    "Your project is using the new Input System,\n" +
                    "but this scene still uses a StandaloneInputModule.\n\n" +
                    "Please replace it with an InputSystemUIInputModule.",
                    "OK");
            }
#endif
        }
    }
}
#endif