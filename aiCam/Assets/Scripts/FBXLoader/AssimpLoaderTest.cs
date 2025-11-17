using UnityEngine;
using AICam.FBXLoader;
using AICam.AvatarBuilder;

namespace AICam.FBXLoader
{
    /// <summary>
    /// RuntimeAssimpFBXLoaderのテストとFBXImportLoggerとの統合
    /// </summary>
    public class AssimpLoaderTest : MonoBehaviour
    {
        [Header("FBXファイル")]
        [SerializeField] private string fbxFilePath = "/Users/daisuketsukada/Downloads/ExtractedUnityPackage/Kyoko_20251112_090405/Kyoko/FBX/kyoko.fbx";

        [Header("ロード設定")]
        [SerializeField] private bool loadOnStart = false;
        [SerializeField] private bool autoAttachDebugVisualizer = true;

        [Header("Avatar生成")]
        [SerializeField] private bool createAvatar = true;
        [SerializeField] private RuntimeAnimatorController animatorController;

        [Header("参照")]
        [SerializeField] private GameObject loadedModel;

        private void Start()
        {
            if (loadOnStart)
            {
                LoadFBX();
            }
        }

        [ContextMenu("Load FBX")]
        public void LoadFBX()
        {
            if (string.IsNullOrEmpty(fbxFilePath))
            {
                Debug.LogError("[AssimpLoaderTest] FBX file path is not set");
                return;
            }

            // ログキャプチャ開始
            FBXImportLogger.StartCapture();

            try
            {
                Debug.Log("[AssimpLoaderTest] === Starting Assimp FBX Load ===");
                Debug.Log($"[AssimpLoaderTest] File: {fbxFilePath}");

                // 既存のモデルを削除
                if (loadedModel != null)
                {
                    DestroyImmediate(loadedModel);
                    loadedModel = null;
                }

                // Assimpでボーン階層をロード
                var loader = new RuntimeAssimpFBXLoader();
                loadedModel = loader.LoadBoneHierarchy(fbxFilePath);

                if (loadedModel == null)
                {
                    Debug.LogError("[AssimpLoaderTest] Failed to load FBX");
                    return;
                }

                Debug.Log($"[AssimpLoaderTest] Successfully loaded: {loadedModel.name}");

                // デバッグビジュアライザーをアタッチ
                if (autoAttachDebugVisualizer)
                {
                    var visualizer = loadedModel.AddComponent<AICam.DebugTools.BoneDebugVisualizer>();
                    Debug.Log("[AssimpLoaderTest] BoneDebugVisualizer attached");
                }

                // Humanoidボーンマッピング
                var boneMap = loader.MapHumanoidBones(loadedModel.transform);
                Debug.Log($"[AssimpLoaderTest] Humanoid bones mapped: {boneMap.Count}");

                // Avatar生成
                if (createAvatar)
                {
                    CreateAvatarAndAnimator(boneMap);
                }

                Debug.Log("[AssimpLoaderTest] === FBX Load Completed ===");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AssimpLoaderTest] Exception: {e.Message}");
                Debug.LogError(e.StackTrace);
            }
            finally
            {
                // ログとスクリーンショット保存
                if (loadedModel != null)
                {
                    // 6方向スクリーンショット
                    FBXImportLogger.CaptureMultiAngleScreenshot(loadedModel);
                }
                FBXImportLogger.StopCaptureAndSave(takeScreenshot: true);
            }
        }

        private void CreateAvatarAndAnimator(System.Collections.Generic.Dictionary<HumanBodyBones, Transform> boneMap)
        {
            Debug.Log("[AssimpLoaderTest] Creating Avatar...");

            var avatarBuilder = new RuntimeHumanoidAvatarBuilder();
            var avatar = avatarBuilder.CreateHumanoidAvatarFromFBX(loadedModel.name, loadedModel);

            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[AssimpLoaderTest] Failed to create Avatar. IsValid: {avatar?.isValid}, IsHuman: {avatar?.isHuman}");
                return;
            }

            Debug.Log($"[AssimpLoaderTest] ✓ Avatar created. IsValid: {avatar.isValid}, IsHuman: {avatar.isHuman}");

            // Animatorをアタッチ
            var animator = loadedModel.GetComponent<Animator>();
            if (animator == null)
            {
                animator = loadedModel.AddComponent<Animator>();
            }

            animator.avatar = avatar;
            animator.applyRootMotion = true;

            if (animatorController != null)
            {
                animator.runtimeAnimatorController = animatorController;
                Debug.Log($"[AssimpLoaderTest] ✓ AnimatorController set: {animatorController.name}");
            }

            Debug.Log("[AssimpLoaderTest] ✓ Animator configured");

            // デバッグビジュアライザーのAnimator参照を更新
            var visualizer = loadedModel.GetComponent<AICam.DebugTools.BoneDebugVisualizer>();
            if (visualizer != null)
            {
                // InspectorでAnimatorが自動設定される
                Debug.Log("[AssimpLoaderTest] BoneDebugVisualizer updated with Animator");
            }
        }

        [ContextMenu("Reload FBX")]
        public void ReloadFBX()
        {
            LoadFBX();
        }

        [ContextMenu("Clear Loaded Model")]
        public void ClearLoadedModel()
        {
            if (loadedModel != null)
            {
                DestroyImmediate(loadedModel);
                loadedModel = null;
                Debug.Log("[AssimpLoaderTest] Loaded model cleared");
            }
        }

        [ContextMenu("Print Current Bone Hierarchy")]
        public void PrintBoneHierarchy()
        {
            if (loadedModel == null)
            {
                Debug.LogWarning("[AssimpLoaderTest] No model loaded");
                return;
            }

            var visualizer = loadedModel.GetComponent<AICam.DebugTools.BoneDebugVisualizer>();
            if (visualizer != null)
            {
                visualizer.PrintBoneHierarchy();
            }
            else
            {
                Debug.LogWarning("[AssimpLoaderTest] BoneDebugVisualizer not found");
            }
        }

        [ContextMenu("Print Avatar Info")]
        public void PrintAvatarInfo()
        {
            if (loadedModel == null)
            {
                Debug.LogWarning("[AssimpLoaderTest] No model loaded");
                return;
            }

            var visualizer = loadedModel.GetComponent<AICam.DebugTools.BoneDebugVisualizer>();
            if (visualizer != null)
            {
                visualizer.PrintAvatarInfo();
            }
            else
            {
                Debug.LogWarning("[AssimpLoaderTest] BoneDebugVisualizer not found");
            }
        }
    }
}
