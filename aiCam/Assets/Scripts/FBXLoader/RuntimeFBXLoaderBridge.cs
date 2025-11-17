using UnityEngine;
using System;
using Cysharp.Threading.Tasks;
using AICam.VRM;
using AICam.AvatarBuilder;
using VRM;
using UniGLTF;

namespace AICam.FBXLoader
{
    /// <summary>
    /// RuntimeAvatarLoaderとUIを繋ぐブリッジコンポーネント
    /// 非同期VRM/FBXロード処理を管理
    /// </summary>
    public class RuntimeFBXLoaderBridge : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private FileBrowserController browser;

        [Header("Settings")]
        [SerializeField] private Transform modelParent;
        [SerializeField] private Vector3 modelPosition = new Vector3(0f, -0.3f, 1.5f); // 中心より上、カメラに近い位置
        [SerializeField] private Vector3 modelRotation = Vector3.zero; // 変更: デフォルトの向き
        [SerializeField] private Vector3 modelScale = Vector3.one;

        [Header("Animation")]
        [SerializeField] private RuntimeAnimatorController animatorController;
        [SerializeField] private string initialStateName = "Idle";

        [Header("Avatar Generation")]
        [SerializeField] private AvatarBuilder.AvatarTemplate avatarTemplate;

        private RuntimeGltfInstance currentInstance;
        private GameObject currentModel;

        void Awake()
        {
            if (browser == null)
            {
                browser = GetComponent<FileBrowserController>();
            }

            if (browser == null)
            {
                browser = FindFirstObjectByType<FileBrowserController>();
            }
        }

        /// <summary>
        /// VRM/FBXロードを開始
        /// </summary>
        public async void StartRuntimeLoad(Action<float> onProgress, Action<bool> onComplete)
        {
            if (browser == null)
            {
                Debug.LogError("[RuntimeFBXLoaderBridge] FileBrowserController not found!");
                onComplete?.Invoke(false);
                return;
            }

            if (string.IsNullOrEmpty(browser.SelectedPath))
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] No file selected!");
                onComplete?.Invoke(false);
                return;
            }

            // 既存のモデルを削除
            ClearCurrentModel();

            // 進捗レポート
            onProgress?.Invoke(10f);

            // ファイルの存在確認
            if (!System.IO.File.Exists(browser.SelectedPath))
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] File not found: {browser.SelectedPath}");
                onComplete?.Invoke(false);
                return;
            }

            // ファイル拡張子で判定
            string extension = System.IO.Path.GetExtension(browser.SelectedPath).ToLower();
            Debug.Log($"[RuntimeFBXLoaderBridge] Loading file with extension: {extension}");

            if (extension == ".vrm")
            {
                await LoadVRMFile(onProgress, onComplete);
            }
            else if (extension == ".fbx")
            {
                await LoadFBXFile(onProgress, onComplete);
            }
            else
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] Unsupported file extension: {extension}");
                onComplete?.Invoke(false);
            }
        }

        /// <summary>
        /// VRMファイルをロード
        /// </summary>
        private async UniTask LoadVRMFile(Action<float> onProgress, Action<bool> onComplete)
        {
            try
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Starting VRM load: {browser.SelectedPath}");

                onProgress?.Invoke(20f);

                // ファイル読み込み
                byte[] bytes = await System.IO.File.ReadAllBytesAsync(browser.SelectedPath);
                Debug.Log($"[RuntimeFBXLoaderBridge] Read {bytes.Length} bytes from file");

                onProgress?.Invoke(40f);

                // VRMをパース
                currentInstance = await VrmUtility.LoadBytesAsync(
                    path: System.IO.Path.GetFileName(browser.SelectedPath),
                    bytes: bytes,
                    awaitCaller: new RuntimeOnlyAwaitCaller(),
                    materialGeneratorCallback: null,
                    metaCallback: null,
                    textureDeserializer: null,
                    loadAnimation: false,
                    springboneRuntime: null
                );

                if (currentInstance == null)
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] Failed to load VRM");
                    onComplete?.Invoke(false);
                    return;
                }

                onProgress?.Invoke(70f);

                Debug.Log("[RuntimeFBXLoaderBridge] VRM instance created successfully");

                // メッシュの表示設定
                currentInstance.EnableUpdateWhenOffscreen();
                currentInstance.ShowMeshes();

                currentModel = currentInstance.Root;
                Debug.Log($"[RuntimeFBXLoaderBridge] Avatar root: {currentModel.name}");

                // モデルを配置
                PlaceModel(currentModel);

                // アニメーションの設定
                SetupAnimator(currentModel);

                onProgress?.Invoke(100f);

                Debug.Log("[RuntimeFBXLoaderBridge] VRM load completed successfully");
                onComplete?.Invoke(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] VRM load failed: {e.Message}");
                Debug.LogException(e);
                onComplete?.Invoke(false);
            }
        }

        /// <summary>
        /// FBXファイルをロード（Assimp使用・骨格のみ）
        /// </summary>
        private async UniTask LoadFBXFile(Action<float> onProgress, Action<bool> onComplete)
        {
            try
            {
                // ログキャプチャ開始
                FBXImportLogger.StartCapture();

                Debug.Log($"[RuntimeFBXLoaderBridge] Starting Assimp FBX load: {browser.SelectedPath}");

                onProgress?.Invoke(20f);

                // Assimpでボーン階層をロード（非同期）
                var loader = new RuntimeAssimpFBXLoader();
                currentModel = await loader.LoadBoneHierarchy(browser.SelectedPath);

                if (currentModel == null)
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] Failed to load FBX skeleton");
                    onProgress?.Invoke(100f);
                    onComplete?.Invoke(false);
                    FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);
                    return;
                }

                Debug.Log($"[RuntimeFBXLoaderBridge] Successfully loaded skeleton: {currentModel.name}");
                onProgress?.Invoke(30f);

                // モデルを配置
                PlaceModel(currentModel);
                onProgress?.Invoke(40f);

                // メッシュをロード（SkinnedMeshRenderer with bones/bindposes）（非同期）
                Debug.Log("[RuntimeFBXLoaderBridge] Loading meshes...");
                await loader.LoadMeshes(currentModel);
                onProgress?.Invoke(50f);

                // デバッグビジュアライザーをアタッチ
                var visualizer = currentModel.AddComponent<AICam.DebugTools.BoneDebugVisualizer>();
                Debug.Log("[RuntimeFBXLoaderBridge] BoneDebugVisualizer attached");
                onProgress?.Invoke(60f);

                // Humanoidボーンマッピング
                var boneMap = loader.MapHumanoidBones(currentModel.transform);
                Debug.Log($"[RuntimeFBXLoaderBridge] Humanoid bones mapped: {boneMap.Count}");
                onProgress?.Invoke(70f);

                // Avatar生成
                var avatarBuilder = new RuntimeHumanoidAvatarBuilder();
                UnityEngine.Avatar newAvatar = null;

                if (avatarTemplate != null)
                {
                    Debug.Log($"[RuntimeFBXLoaderBridge] Using AvatarTemplate: {avatarTemplate.name}");
                    newAvatar = avatarBuilder.CreateHumanoidAvatarFromTemplate(
                        currentModel.name,
                        currentModel,
                        avatarTemplate);
                }
                else
                {
                    Debug.Log("[RuntimeFBXLoaderBridge] Using RuntimeHumanoidAvatarBuilder");
                    newAvatar = avatarBuilder.CreateHumanoidAvatarFromFBX(currentModel.name, currentModel);
                }

                onProgress?.Invoke(80f);

                bool loadSuccess = false;

                if (newAvatar != null && newAvatar.isValid && newAvatar.isHuman)
                {
                    Debug.Log($"[RuntimeFBXLoaderBridge] ✓ Avatar created. IsValid: {newAvatar.isValid}, IsHuman: {newAvatar.isHuman}");

                    // Animatorをアタッチ
                    var animator = currentModel.GetComponent<Animator>();
                    if (animator == null)
                    {
                        animator = currentModel.AddComponent<Animator>();
                    }

                    animator.avatar = newAvatar;
                    animator.applyRootMotion = true;

                    onProgress?.Invoke(90f);

                    // アニメーションの設定
                    SetupAnimator(currentModel);

                    onProgress?.Invoke(100f);
                    loadSuccess = true;
                }
                else
                {
                    Debug.LogError($"[RuntimeFBXLoaderBridge] ✗ Failed to create Avatar. IsValid: {newAvatar?.isValid}, IsHuman: {newAvatar?.isHuman}");
                    loadSuccess = false;
                }

                Debug.Log($"[RuntimeFBXLoaderBridge] Assimp FBX load completed. Success: {loadSuccess}");

                // 非同期処理を模倣（UIの進捗表示のため）
                await UniTask.Yield();

                // ログとスクリーンショットを保存
                if (loadSuccess && currentModel != null)
                {
                    // 6方向の複合スクリーンショットを撮影
                    FBXImportLogger.CaptureMultiAngleScreenshot(currentModel);
                    // 通常のスクリーンショットも保存
                    FBXImportLogger.StopCaptureAndSave(takeScreenshot: true);
                }
                else
                {
                    FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);
                }

                onComplete?.Invoke(loadSuccess);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] FBX load failed: {e.Message}");
                Debug.LogException(e);

                // エラー時もログを保存
                FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);

                onComplete?.Invoke(false);
            }
        }

        /// <summary>
        /// モデルを配置
        /// </summary>
        private void PlaceModel(GameObject model)
        {
            if (model == null) return;

            Transform parent = modelParent != null ? modelParent : transform;
            model.transform.SetParent(parent, false);
            model.transform.localPosition = modelPosition;
            model.transform.localRotation = Quaternion.Euler(modelRotation);
            model.transform.localScale = modelScale;

            Debug.Log($"[RuntimeFBXLoaderBridge] Model placed at World Position: {model.transform.position}");
            Debug.Log($"[RuntimeFBXLoaderBridge] Model Rotation: {model.transform.rotation.eulerAngles}");
            Debug.Log($"[RuntimeFBXLoaderBridge] Model Scale: {model.transform.lossyScale}");
            Debug.Log($"[RuntimeFBXLoaderBridge] Parent: {(parent != null ? parent.name : "null")}");

            // レンダラーの確認
            var renderers = model.GetComponentsInChildren<Renderer>();
            Debug.Log($"[RuntimeFBXLoaderBridge] Found {renderers.Length} renderers");
            foreach (var renderer in renderers)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Renderer: {renderer.name}, Enabled: {renderer.enabled}, Layer: {renderer.gameObject.layer}");
            }
        }

        /// <summary>
        /// Animatorの設定
        /// </summary>
        private void SetupAnimator(GameObject model)
        {
            if (model == null)
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] SetupAnimator: model is null");
                return;
            }

            Debug.Log($"[RuntimeFBXLoaderBridge] SetupAnimator called for model: {model.name}");

            var animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
                Debug.Log("[RuntimeFBXLoaderBridge] Added Animator component");
            }
            else
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Found existing Animator component. Current controller: {(animator.runtimeAnimatorController != null ? animator.runtimeAnimatorController.name : "null")}");
            }

            // Avatarのチェック
            if (animator.avatar != null)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Avatar is assigned: {animator.avatar.name}, IsValid: {animator.avatar.isValid}, IsHuman: {animator.avatar.isHuman}");
            }
            else
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] ⚠ Avatar is NOT assigned! Humanoid animation will not work.");
            }

            Debug.Log($"[RuntimeFBXLoaderBridge] animatorController field is: {(animatorController != null ? animatorController.name : "NULL - NOT ASSIGNED IN INSPECTOR!")}");

            if (animatorController != null)
            {
                animator.runtimeAnimatorController = animatorController;
                Debug.Log($"[RuntimeFBXLoaderBridge] ✓ Set animator controller: {animatorController.name}");

                // Animator設定後の確認
                if (animator.runtimeAnimatorController == animatorController)
                {
                    Debug.Log($"[RuntimeFBXLoaderBridge] ✓ Animator controller successfully applied");
                }
                else
                {
                    Debug.LogError($"[RuntimeFBXLoaderBridge] ✗ Failed to apply animator controller!");
                }

                // 初期ステートの再生
                if (!string.IsNullOrEmpty(initialStateName))
                {
                    animator.Play(initialStateName, 0, 0f);
                    Debug.Log($"[RuntimeFBXLoaderBridge] Playing initial state: {initialStateName}");
                }
            }
            else
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] ⚠ Animator controller NOT assigned in Inspector! Please assign AnimatorController in RuntimeFBXLoaderBridge component.");
            }
        }

        /// <summary>
        /// 現在のモデルを削除
        /// </summary>
        public void ClearCurrentModel()
        {
            if (currentInstance != null)
            {
                Debug.Log("[RuntimeFBXLoaderBridge] Disposing VRM instance");
                currentInstance.Dispose();
                currentInstance = null;
            }

            if (currentModel != null)
            {
                Debug.Log("[RuntimeFBXLoaderBridge] Destroying model");
                Destroy(currentModel);
                currentModel = null;
            }

            Debug.Log("[RuntimeFBXLoaderBridge] Model cleared");
        }

        void OnDestroy()
        {
            ClearCurrentModel();
        }
    }
}
