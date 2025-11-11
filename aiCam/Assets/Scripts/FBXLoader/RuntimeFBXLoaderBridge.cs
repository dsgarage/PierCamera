using UnityEngine;
using System;
using Cysharp.Threading.Tasks;
using AICam.VRM;
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
        [SerializeField] private Vector3 modelPosition = Vector3.zero;
        [SerializeField] private Vector3 modelRotation = Vector3.zero; // 変更: デフォルトの向き
        [SerializeField] private Vector3 modelScale = Vector3.one;

        [Header("Animation")]
        [SerializeField] private RuntimeAnimatorController animatorController;
        [SerializeField] private string initialStateName = "Idle";

        private RuntimeGltfInstance currentInstance;
        private GameObject currentModel;
        private RuntimeMaterialManager materialManager;

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

            // RuntimeMaterialManagerを初期化
            materialManager = new RuntimeMaterialManager();
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

            // ファイルの存在確認
            if (!System.IO.File.Exists(browser.SelectedPath))
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] File not found: {browser.SelectedPath}");
                onComplete?.Invoke(false);
                return;
            }

            // 既存のモデルを削除
            ClearCurrentModel();

            // ファイル拡張子で判定
            string extension = System.IO.Path.GetExtension(browser.SelectedPath).ToLower();
            Debug.Log($"[RuntimeFBXLoaderBridge] Loading file: {browser.SelectedPath}, Extension: {extension}");

            try
            {
                if (extension == ".vrm")
                {
                    await LoadVRMAsync(onProgress, onComplete);
                }
                else if (extension == ".fbx")
                {
                    await LoadFBXAsync(onProgress, onComplete);
                }
                else
                {
                    Debug.LogError($"[RuntimeFBXLoaderBridge] Unsupported file format: {extension}");
                    onComplete?.Invoke(false);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] Load failed: {e.Message}");
                Debug.LogException(e);
                onComplete?.Invoke(false);
            }
        }

        private async UniTask LoadVRMAsync(Action<float> onProgress, Action<bool> onComplete)
        {
            Debug.Log($"[RuntimeFBXLoaderBridge] Starting VRM load: {browser.SelectedPath}");

            onProgress?.Invoke(10f);

            // ファイル読み込み
            byte[] bytes = await System.IO.File.ReadAllBytesAsync(browser.SelectedPath);
            Debug.Log($"[RuntimeFBXLoaderBridge] Read {bytes.Length} bytes from file");

            onProgress?.Invoke(30f);

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

        private async UniTask LoadFBXAsync(Action<float> onProgress, Action<bool> onComplete)
        {
            Debug.Log($"[RuntimeFBXLoaderBridge] Starting FBX load: {browser.SelectedPath}");

            onProgress?.Invoke(10f);

            // RuntimeFBXModelBuilderを使用してFBXをロード
            RuntimeFBXModelBuilder fbxLoader = new RuntimeFBXModelBuilder();
            currentModel = await fbxLoader.LoadFBX(browser.SelectedPath);

            if (currentModel == null)
            {
                Debug.LogError("[RuntimeFBXLoaderBridge] Failed to load FBX");
                onComplete?.Invoke(false);
                return;
            }

            Debug.Log($"[RuntimeFBXLoaderBridge] FBX loaded successfully: {currentModel.name}");
            onProgress?.Invoke(40f);

            // マテリアルを適用
            var meshNodeToMaterialNames = fbxLoader.GetMeshNodeToMaterialNames();
            string extractedPath = browser.SelectedPath;
            await materialManager.AssignMaterials(currentModel, extractedPath, meshNodeToMaterialNames);
            Debug.Log("[RuntimeFBXLoaderBridge] Materials assigned");
            onProgress?.Invoke(60f);

            // Humanoid Avatar を生成
            RuntimeHumanoidAvatarBuilder avatarBuilder = new RuntimeHumanoidAvatarBuilder();
            Avatar avatar = avatarBuilder.CreateHumanoidAvatarFromFBX(currentModel.name, currentModel);

            if (avatar != null && avatar.isValid && avatar.isHuman)
            {
                Debug.Log("[RuntimeFBXLoaderBridge] Humanoid Avatar created successfully");

                // Animatorを設定
                var animator = currentModel.GetComponent<Animator>();
                if (animator == null)
                {
                    animator = currentModel.AddComponent<Animator>();
                }
                animator.avatar = avatar;
            }
            else
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] Failed to create Humanoid Avatar, using generic setup");
            }

            onProgress?.Invoke(70f);

            // モデルを配置
            PlaceModel(currentModel);

            // アニメーションの設定
            SetupAnimator(currentModel);

            onProgress?.Invoke(100f);

            Debug.Log("[RuntimeFBXLoaderBridge] FBX load completed successfully");
            onComplete?.Invoke(true);
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
            if (model == null) return;

            var animator = model.GetComponent<Animator>();
            if (animator == null)
            {
                animator = model.AddComponent<Animator>();
                Debug.Log("[RuntimeFBXLoaderBridge] Added Animator component");
            }

            if (animatorController != null)
            {
                animator.runtimeAnimatorController = animatorController;
                Debug.Log($"[RuntimeFBXLoaderBridge] Set animator controller: {animatorController.name}");

                // 初期ステートの再生
                if (!string.IsNullOrEmpty(initialStateName))
                {
                    animator.Play(initialStateName, 0, 0f);
                    Debug.Log($"[RuntimeFBXLoaderBridge] Playing initial state: {initialStateName}");
                }
            }
            else
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] Animator controller not assigned");
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
