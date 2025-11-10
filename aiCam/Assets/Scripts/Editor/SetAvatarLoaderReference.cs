using UnityEngine;
using UnityEditor;
using AICam.UI;
using AICam.VRM;

namespace AICam.Editor
{
    /// <summary>
    /// CameraCaptureControllerにRuntimeAvatarLoaderの参照を設定するEditor拡張
    /// </summary>
    public static class SetAvatarLoaderReference
    {
        [MenuItem("AICam/Setup/Set RuntimeAvatarLoader Reference")]
        public static void SetReference()
        {
            // CameraCaptureControllerを検索
            var cameraController = Object.FindFirstObjectByType<CameraCaptureController>();

            if (cameraController == null)
            {
                Debug.LogError("[SetAvatarLoaderReference] CameraCaptureController not found in scene.");
                return;
            }

            // RuntimeAvatarLoaderを検索
            var avatarLoader = Object.FindFirstObjectByType<RuntimeAvatarLoader>();

            if (avatarLoader == null)
            {
                Debug.LogError("[SetAvatarLoaderReference] RuntimeAvatarLoader not found in scene.");
                return;
            }

            // SerializedObjectを使用して参照を設定
            var serializedObject = new SerializedObject(cameraController);
            var avatarLoaderProp = serializedObject.FindProperty("avatarLoader");

            if (avatarLoaderProp != null)
            {
                avatarLoaderProp.objectReferenceValue = avatarLoader;
                serializedObject.ApplyModifiedProperties();

                EditorUtility.SetDirty(cameraController.gameObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
                );

                Debug.Log($"[SetAvatarLoaderReference] ✅ Set avatarLoader reference from {cameraController.gameObject.name} to {avatarLoader.gameObject.name}");
            }
            else
            {
                Debug.LogError("[SetAvatarLoaderReference] avatarLoader field not found on CameraCaptureController");
            }
        }

        [MenuItem("AICam/Setup/Set Body AnimatorController")]
        public static void SetBodyAnimatorController()
        {
            // RuntimeAvatarLoaderを検索
            var avatarLoader = Object.FindFirstObjectByType<RuntimeAvatarLoader>();

            if (avatarLoader == null)
            {
                Debug.LogError("[SetAvatarLoaderReference] RuntimeAvatarLoader not found in scene.");
                return;
            }

            // UnityChanLocomotionsを検索
            var guids = AssetDatabase.FindAssets("UnityChanLocomotions t:RuntimeAnimatorController");
            RuntimeAnimatorController controller = null;

            if (guids.Length > 0)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[0]);
                controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(path);
                Debug.Log($"[SetAvatarLoaderReference] Found UnityChanLocomotions at: {path}");
            }

            if (controller == null)
            {
                Debug.LogError("[SetAvatarLoaderReference] UnityChanLocomotions AnimatorController not found!");
                return;
            }

            // SerializedObjectを使用してbodyAnimatorControllerを設定
            var serializedObject = new SerializedObject(avatarLoader);
            var bodyControllerProp = serializedObject.FindProperty("bodyAnimatorController");

            if (bodyControllerProp != null)
            {
                bodyControllerProp.objectReferenceValue = controller;
                serializedObject.ApplyModifiedProperties();

                EditorUtility.SetDirty(avatarLoader.gameObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
                );

                Debug.Log($"[SetAvatarLoaderReference] ✅ Set bodyAnimatorController to: {controller.name}");
            }
            else
            {
                Debug.LogError("[SetAvatarLoaderReference] bodyAnimatorController field not found on RuntimeAvatarLoader");
            }
        }

        [MenuItem("AICam/Setup/Clear AnimatorController References")]
        public static void ClearAnimatorControllerReferences()
        {
            // RuntimeAvatarLoaderを検索
            var avatarLoader = Object.FindFirstObjectByType<RuntimeAvatarLoader>();

            if (avatarLoader == null)
            {
                Debug.LogError("[SetAvatarLoaderReference] RuntimeAvatarLoader not found in scene.");
                return;
            }

            var serializedObject = new SerializedObject(avatarLoader);

            // faceAnimatorControllerをnullに設定
            var faceControllerProp = serializedObject.FindProperty("faceAnimatorController");
            if (faceControllerProp != null)
            {
                faceControllerProp.objectReferenceValue = null;
                Debug.Log("[SetAvatarLoaderReference] Cleared faceAnimatorController reference");
            }

            // bodyAnimatorControllerをnullに設定
            var bodyControllerProp = serializedObject.FindProperty("bodyAnimatorController");
            if (bodyControllerProp != null)
            {
                bodyControllerProp.objectReferenceValue = null;
                Debug.Log("[SetAvatarLoaderReference] Cleared bodyAnimatorController reference");
            }

            serializedObject.ApplyModifiedProperties();

            // シーンをダーティマーク
            EditorUtility.SetDirty(avatarLoader.gameObject);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
            );

            Debug.Log("[SetAvatarLoaderReference] ✅ All AnimatorController references cleared! VRM will use default A-pose.");
        }

        [MenuItem("AICam/Setup/Clear AvatarPrefab Reference")]
        public static void ClearAvatarPrefabReference()
        {
            // PlaceAvatarOnPlaneOnlyを検索
            var placer = Object.FindFirstObjectByType<PlaceAvatarOnPlaneOnly>();

            if (placer == null)
            {
                Debug.LogError("[SetAvatarLoaderReference] PlaceAvatarOnPlaneOnly not found in scene.");
                return;
            }

            var serializedObject = new SerializedObject(placer);

            // avatarPrefabをnullに設定
            var avatarPrefabProp = serializedObject.FindProperty("avatarPrefab");
            if (avatarPrefabProp != null)
            {
                avatarPrefabProp.objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties();

                EditorUtility.SetDirty(placer.gameObject);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
                );

                Debug.Log("[SetAvatarLoaderReference] ✅ avatarPrefab reference cleared! Will use only RuntimeAvatarLoader.");
            }
            else
            {
                Debug.LogError("[SetAvatarLoaderReference] avatarPrefab field not found on PlaceAvatarOnPlaneOnly");
            }
        }

        [MenuItem("AICam/Setup/Clear Kyoko References")]
        public static void ClearKyokoReferences()
        {
            int clearedCount = 0;

            // 1. PlaceAvatarOnPlaneOnlyのavatarPrefabをクリア
            var placer = Object.FindFirstObjectByType<PlaceAvatarOnPlaneOnly>();
            if (placer != null)
            {
                var placerSO = new SerializedObject(placer);
                var avatarPrefabProp = placerSO.FindProperty("avatarPrefab");
                if (avatarPrefabProp != null && avatarPrefabProp.objectReferenceValue != null)
                {
                    Debug.Log($"[SetAvatarLoaderReference] Clearing avatarPrefab: {avatarPrefabProp.objectReferenceValue.name}");
                    avatarPrefabProp.objectReferenceValue = null;
                    placerSO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(placer.gameObject);
                    clearedCount++;
                }
            }

            // 2. FaceUIManagerのeditorTargetControllerをクリア
            var faceUIManager = Object.FindFirstObjectByType<FaceUIManager>();
            if (faceUIManager != null)
            {
                var faceUISO = new SerializedObject(faceUIManager);
                var editorTargetProp = faceUISO.FindProperty("editorTargetController");
                if (editorTargetProp != null && editorTargetProp.objectReferenceValue != null)
                {
                    Debug.Log($"[SetAvatarLoaderReference] Clearing editorTargetController: {editorTargetProp.objectReferenceValue.name}");
                    editorTargetProp.objectReferenceValue = null;
                    faceUISO.ApplyModifiedProperties();
                    EditorUtility.SetDirty(faceUIManager.gameObject);
                    clearedCount++;
                }
            }

            // 3. シーン内の全Kyoko GameObjectを検索して削除
            var allObjects = Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            foreach (var obj in allObjects)
            {
                if (obj.name.ToLower().Contains("kyoko"))
                {
                    Debug.Log($"[SetAvatarLoaderReference] Destroying GameObject: {obj.name} at path: {GetGameObjectPath(obj)}");
                    Object.DestroyImmediate(obj);
                    clearedCount++;
                }
            }

            if (clearedCount > 0)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                    UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene()
                );
                Debug.Log($"[SetAvatarLoaderReference] ✅ Cleared {clearedCount} Kyoko references from scene!");
            }
            else
            {
                Debug.Log("[SetAvatarLoaderReference] No Kyoko references found in scene.");
            }
        }

        private static string GetGameObjectPath(GameObject obj)
        {
            string path = obj.name;
            Transform parent = obj.transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            return path;
        }
    }
}
