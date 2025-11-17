using UnityEngine;
using System;
using Cysharp.Threading.Tasks;
using AICam.VRM;
using AICam.AvatarBuilder;
using VRM;
using UniGLTF;
using TriLibCore;
using TriLibCore.General;

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
        [SerializeField] private Vector3 modelPosition = Vector3.zero;
        [SerializeField] private Vector3 modelRotation = Vector3.zero; // 変更: デフォルトの向き
        [SerializeField] private Vector3 modelScale = Vector3.one;

        [Header("Animation")]
        [SerializeField] private RuntimeAnimatorController animatorController;
        [SerializeField] private string initialStateName = "Idle";

        [Header("Avatar Generation")]
        [SerializeField] private bool useRuntimeHumanoidAvatarBuilder = false;
        [SerializeField] private TriLibCore.Mappers.HumanoidAvatarMapper humanoidAvatarMapper;

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
        /// FBXファイルをロード（TriLib使用）
        /// </summary>
        private async UniTask LoadFBXFile(Action<float> onProgress, Action<bool> onComplete)
        {
            try
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Starting FBX load: {browser.SelectedPath}");

                onProgress?.Invoke(20f);

                // TriLibのロードオプションを作成（BindPose保持を最優先に設定）
                var assetLoaderOptions = AssetLoader.CreateDefaultLoaderOptions(false, true);

                // ===== BindPose破壊を防ぐための重要設定 =====
                assetLoaderOptions.AnimationType = AnimationType.Humanoid; // Humanoidに設定してSkinnedMeshRendererを生成

                // 注意：CreateDefaultLoaderOptions()のデフォルト設定を信頼
                // OptimizeGraph, OptimizeMeshes, PreTransformVertices などの
                // BindPose破壊オプションが含まれていないことを前提とする
                Debug.Log("[RuntimeFBXLoaderBridge] Using TriLib default loader options for BindPose preservation");

                // HumanoidAvatarMapperを設定
                if (humanoidAvatarMapper != null)
                {
                    assetLoaderOptions.HumanoidAvatarMapper = humanoidAvatarMapper;
                    Debug.Log($"[RuntimeFBXLoaderBridge] Using HumanoidAvatarMapper: {humanoidAvatarMapper.name}");
                }
                else
                {
                    Debug.LogWarning("[RuntimeFBXLoaderBridge] No HumanoidAvatarMapper assigned. Using TriLib defaults.");
                }

                bool loadComplete = false;
                bool loadSuccess = false;
                float currentProgress = 20f;

                // TriLibでFBXをロード
                Transform parent = modelParent != null ? modelParent : transform;

                AssetLoader.LoadModelFromFile(
                    path: browser.SelectedPath,
                    onLoad: (assetLoaderContext) =>
                    {
                        Debug.Log("[RuntimeFBXLoaderBridge] FBX meshes loaded");
                        onProgress?.Invoke(50f);
                    },
                    onMaterialsLoad: (assetLoaderContext) =>
                    {
                        Debug.Log("[RuntimeFBXLoaderBridge] FBX materials loaded");
                        currentModel = assetLoaderContext.RootGameObject;

                        if (currentModel != null)
                        {
                            Debug.Log($"[RuntimeFBXLoaderBridge] FBX root: {currentModel.name}");

                            // モデルを配置
                            PlaceModel(currentModel);

                            // TriLibが生成したAnimatorを確認
                            var animator = currentModel.GetComponent<Animator>();
                            if (animator != null)
                            {
                                Debug.Log($"[RuntimeFBXLoaderBridge] TriLib generated Animator. Avatar: {(animator.avatar != null ? animator.avatar.name : "null")}");

                                // SkinnedMeshRendererの確認
                                var skinnedMeshRenderers = currentModel.GetComponentsInChildren<SkinnedMeshRenderer>();
                                Debug.Log($"[RuntimeFBXLoaderBridge] Found {skinnedMeshRenderers.Length} SkinnedMeshRenderer(s)");
                                foreach (var smr in skinnedMeshRenderers)
                                {
                                    Debug.Log($"[RuntimeFBXLoaderBridge]   - SMR: {smr.name}, bones: {smr.bones?.Length ?? 0}, rootBone: {(smr.rootBone != null ? smr.rootBone.name : "null")}");
                                }
                            }
                            else
                            {
                                Debug.LogWarning("[RuntimeFBXLoaderBridge] No Animator component found!");
                            }

                            // Avatar生成方法の選択
                            onProgress?.Invoke(70f);
                            UnityEngine.Avatar newAvatar = null;

                            if (useRuntimeHumanoidAvatarBuilder)
                            {
                                Debug.Log("[RuntimeFBXLoaderBridge] Using RuntimeHumanoidAvatarBuilder for Avatar generation");
                                var avatarBuilder = new RuntimeHumanoidAvatarBuilder();
                                newAvatar = avatarBuilder.CreateHumanoidAvatarFromFBX(currentModel.name, currentModel);
                            }
                            else
                            {
                                Debug.Log("[RuntimeFBXLoaderBridge] Using TriLib-generated Avatar");
                                newAvatar = animator?.avatar;
                            }

                            if (newAvatar != null && newAvatar.isValid && newAvatar.isHuman)
                            {
                                string avatarSource = useRuntimeHumanoidAvatarBuilder ? "RuntimeHumanoidAvatarBuilder" : "TriLib";
                                Debug.Log($"[RuntimeFBXLoaderBridge] ✓ Avatar ready from {avatarSource}. IsValid: {newAvatar.isValid}, IsHuman: {newAvatar.isHuman}");

                                // Animatorに設定
                                if (animator == null)
                                {
                                    animator = currentModel.AddComponent<Animator>();
                                }

                                if (useRuntimeHumanoidAvatarBuilder && animator.avatar != null && animator.avatar != newAvatar)
                                {
                                    Debug.Log($"[RuntimeFBXLoaderBridge] Replacing TriLib Avatar with RuntimeHumanoidAvatarBuilder Avatar");
                                }

                                animator.avatar = newAvatar;

                                onProgress?.Invoke(90f);

                                // アニメーションの設定
                                SetupAnimator(currentModel);

                                onProgress?.Invoke(100f);
                                loadSuccess = true;
                            }
                            else
                            {
                                string avatarSource = useRuntimeHumanoidAvatarBuilder ? "RuntimeHumanoidAvatarBuilder" : "TriLib";
                                Debug.LogError($"[RuntimeFBXLoaderBridge] ✗ Failed to create valid Avatar. Source: {avatarSource}, Avatar: {(newAvatar != null ? "not null" : "null")}, IsValid: {newAvatar?.isValid}, IsHuman: {newAvatar?.isHuman}");

                                // フォールバック: TriLibのAvatarを使用
                                if (!useRuntimeHumanoidAvatarBuilder && animator != null && animator.avatar != null)
                                {
                                    // TriLib使用時にここに来ることはないはず
                                    Debug.LogError("[RuntimeFBXLoaderBridge] Unexpected: TriLib Avatar is invalid");
                                    loadSuccess = false;
                                }
                                else if (useRuntimeHumanoidAvatarBuilder && animator != null && animator.avatar != null && animator.avatar.isValid && animator.avatar.isHuman)
                                {
                                    Debug.Log("[RuntimeFBXLoaderBridge] Falling back to TriLib-generated Avatar");
                                    newAvatar = animator.avatar;
                                    SetupAnimator(currentModel);
                                    loadSuccess = true;
                                }
                                else
                                {
                                    Debug.LogError("[RuntimeFBXLoaderBridge] No valid Avatar available");
                                    loadSuccess = false;
                                }
                            }
                        }
                        else
                        {
                            Debug.LogError("[RuntimeFBXLoaderBridge] Failed to load FBX: RootGameObject is null");
                            loadSuccess = false;
                        }

                        loadComplete = true;
                    },
                    onProgress: (assetLoaderContext, progress) =>
                    {
                        currentProgress = 20f + (progress * 30f);
                        onProgress?.Invoke(currentProgress);
                    },
                    onError: (contextualizedError) =>
                    {
                        Debug.LogError($"[RuntimeFBXLoaderBridge] FBX load error: {contextualizedError}");
                        loadSuccess = false;
                        loadComplete = true;
                    },
                    wrapperGameObject: parent.gameObject,
                    assetLoaderOptions: assetLoaderOptions
                );

                // ロード完了を待機
                while (!loadComplete)
                {
                    await UniTask.Yield();
                }

                Debug.Log($"[RuntimeFBXLoaderBridge] FBX load completed. Success: {loadSuccess}");
                onComplete?.Invoke(loadSuccess);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] FBX load failed: {e.Message}");
                Debug.LogException(e);
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
