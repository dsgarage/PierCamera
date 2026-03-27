using UnityEngine;
using System;
using System.Text;
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using AICam.AvatarBuilder;
using AICam.Core;
using AICam.Core.IO;
using AICam.Core.Texture;
using AICam.Analytics;
using AICam.Analytics.DTOs;
using PierCamera.Analytics;
using AICam.AvatarCache;
using UniGLTF;
using VRM;
using UniVRM10;
using Debug = UnityEngine.Debug;

namespace AICam.FBXLoader
{
    /// <summary>
    /// RuntimeAvatarLoaderとUIを繋ぐブリッジコンポーネント
    /// 非同期VRM/FBXロード処理を管理
    /// IAvatarLoaderを実装し、キャッシュシステムから利用可能
    /// </summary>
    public class RuntimeFBXLoaderBridge : MonoBehaviour, IAvatarLoader
    {
        [Header("References")]
        [SerializeField] private FileBrowserController browser;
        [SerializeField] private AvatarMemoryCache memoryCache;

        [Header("Settings")]
        [SerializeField] private Transform modelParent;
        [SerializeField] private Vector3 modelPosition = new Vector3(0f, -0.5f, 2.5f); // カメラから適切な距離に配置
        [SerializeField] private Vector3 modelRotation = Vector3.zero; // 変更: デフォルトの向き
        [SerializeField] private Vector3 modelScale = Vector3.one;

        [Header("Animation")]
        [SerializeField] private RuntimeAnimatorController animatorController;
        [SerializeField] private string initialStateName = "Idle";

        [Header("Avatar Generation")]
        [SerializeField] private AvatarBuilder.AvatarTemplate avatarTemplate;

        // VRM 0.x 用インスタンス
        private RuntimeGltfInstance currentGltfInstance;
        // VRM 1.0 用インスタンス
        private Vrm10Instance currentVrm10Instance;
        // 読み込まれたVRMのバージョン
        private VrmVersion loadedVrmVersion = VrmVersion.Unknown;

        // Issue #440: 圧縮テクスチャデシリアライザ (VRMテクスチャメモリ約89%削減)
        // 低スペック端末（iPhone 11以下）のみ圧縮を有効化
        private static CompressedTextureDeserializer _textureDeserializer;
        private static bool _textureDeserializerInitialized;

        /// <summary>
        /// テクスチャデシリアライザを遅延初期化で取得
        /// 低スペック端末（LowEnd = iPhone 11以下）のみ圧縮を有効にする
        /// </summary>
        private static CompressedTextureDeserializer TextureDeserializer
        {
            get
            {
                if (!_textureDeserializerInitialized)
                {
                    _textureDeserializerInitialized = true;

                    // デバイスカテゴリを判定
                    var category = DeviceAnalytics.GetDeviceCategory();
                    bool enableCompression = (category == DeviceAnalytics.DeviceCategory.LowEnd);

                    _textureDeserializer = new CompressedTextureDeserializer(enableCompression);

                    Debug.Log($"[RuntimeFBXLoaderBridge] TextureDeserializer initialized: " +
                              $"DeviceCategory={category}, CompressionEnabled={enableCompression}");
                }
                return _textureDeserializer;
            }
        }

        private GameObject currentModel;

        // スロット連携用
        private int currentSlotIndex = -1;
        private bool shouldCaptureIcon = false;
        private string pendingIconPath = null;

        // テレメトリ用
        private Stopwatch _loadStopwatch;
        private string _currentFilePath;
        private long _currentFileSize;

        /// <summary>
        /// 現在読み込まれているモデルを取得
        /// </summary>
        public GameObject CurrentModel => currentModel;

        /// <summary>
        /// 現在のVRMバージョンを取得
        /// </summary>
        public VrmVersion LoadedVrmVersion => loadedVrmVersion;

        /// <summary>
        /// AnimatorControllerを取得
        /// </summary>
        public RuntimeAnimatorController GetAnimatorController() => animatorController;

        public enum VrmVersion
        {
            Unknown,
            VRM_0_x,
            VRM_1_0
        }

        void Awake()
        {
            ValidateDependencies();
        }

        /// <summary>
        /// 依存関係を検証し、未設定の場合は警告を出力
        /// </summary>
        private void ValidateDependencies()
        {
            if (browser == null)
            {
                browser = GetComponent<FileBrowserController>();
            }

            if (browser == null)
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] FileBrowserController not assigned in Inspector, using FindFirstObjectByType (slow)");
                browser = FindFirstObjectByType<FileBrowserController>();
            }

            if (memoryCache == null)
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] AvatarMemoryCache not assigned in Inspector, using FindFirstObjectByType (slow)");
                memoryCache = FindFirstObjectByType<AvatarMemoryCache>();
            }
        }

        /// <summary>
        /// スロットからVRM/FBXロードを開始（パス直接指定）
        /// </summary>
        /// <param name="filePath">読み込むファイルのパス</param>
        /// <param name="slotIndex">スロットインデックス（アイコン撮影用）</param>
        /// <param name="iconPath">アイコン保存先パス</param>
        /// <param name="onProgress">進捗コールバック</param>
        /// <param name="onComplete">完了コールバック</param>
        public async void StartRuntimeLoadFromPath(string filePath, int slotIndex, string iconPath, Action<float> onProgress, Action<bool> onComplete)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[RuntimeFBXLoaderBridge] File path is empty!");
                onComplete?.Invoke(false);
                return;
            }

            // スロット情報を設定
            currentSlotIndex = slotIndex;
            shouldCaptureIcon = !string.IsNullOrEmpty(iconPath);
            pendingIconPath = iconPath;

            // 既存のモデルを削除
            ClearCurrentModel();

            // 進捗レポート
            onProgress?.Invoke(10f);

            // ファイルの存在確認
            if (!System.IO.File.Exists(filePath))
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] File not found: {filePath}");
                AlertBarController.ErrorFileNotFound(filePath);
                ResetSlotState();
                onComplete?.Invoke(false);
                return;
            }

            // ファイル拡張子で判定
            string extension = System.IO.Path.GetExtension(filePath).ToLower();
            Debug.Log($"[RuntimeFBXLoaderBridge] Loading file from path with extension: {extension}");

            if (extension == ".vrm")
            {
                await LoadVRMFileFromPath(filePath, onProgress, onComplete);
            }
            else if (extension == ".fbx")
            {
                await LoadFBXFileFromPath(filePath, onProgress, onComplete);
            }
            else
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] Unsupported file extension: {extension}");
                AlertBarController.ErrorFileFormatInvalid($"拡張子: {extension}");
                ResetSlotState();
                onComplete?.Invoke(false);
            }
        }

        /// <summary>
        /// スロット状態をリセット
        /// </summary>
        private void ResetSlotState()
        {
            currentSlotIndex = -1;
            shouldCaptureIcon = false;
            pendingIconPath = null;
        }

        /// <summary>
        /// 読み込み成功後のアイコン撮影処理
        /// </summary>
        private async UniTask CaptureIconIfNeeded()
        {
            if (!shouldCaptureIcon || string.IsNullOrEmpty(pendingIconPath) || currentModel == null)
            {
                ResetSlotState();
                return;
            }

            Debug.Log($"[RuntimeFBXLoaderBridge] Capturing icon for slot {currentSlotIndex}...");

            try
            {
                // 1フレーム待ってレンダリングを安定させる
                await UniTask.Yield();
                await UniTask.WaitForEndOfFrame(this);

                // アイコンを撮影
                string savedPath = await AvatarIconCapture.Instance.CaptureAndSaveAsync(currentModel, pendingIconPath);

                if (!string.IsNullOrEmpty(savedPath))
                {
                    Debug.Log($"[RuntimeFBXLoaderBridge] Icon captured and saved to: {savedPath}");
                }
                else
                {
                    Debug.LogWarning("[RuntimeFBXLoaderBridge] Failed to capture icon");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] Error capturing icon: {e.Message}");
            }
            finally
            {
                ResetSlotState();
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
                AlertBarController.ErrorFileNotFound(browser.SelectedPath);
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
                AlertBarController.ErrorFileFormatInvalid($"拡張子: {extension}");
                onComplete?.Invoke(false);
            }
        }

        /// <summary>
        /// VRMファイルのバージョンを検出する
        /// </summary>
        private VrmVersion DetectVrmVersion(byte[] bytes)
        {
            try
            {
                // glTF header: magic(4) + version(4) + length(4) = 12 bytes
                // First chunk: length(4) + type(4) + data
                if (bytes.Length < 20)
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] File too small to be a valid VRM");
                    return VrmVersion.Unknown;
                }

                // Skip glTF header (12 bytes) and read first chunk
                int chunkLength = BitConverter.ToInt32(bytes, 12);

                // Read JSON chunk (starts at offset 20)
                if (bytes.Length < 20 + chunkLength)
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] Invalid chunk length");
                    return VrmVersion.Unknown;
                }

                string json = Encoding.UTF8.GetString(bytes, 20, chunkLength);

                // VRM 1.0 uses "VRMC_vrm" extension
                if (json.Contains("\"VRMC_vrm\""))
                {
                    Debug.Log("[RuntimeFBXLoaderBridge] Detected VRM 1.0 format");
                    return VrmVersion.VRM_1_0;
                }

                // VRM 0.x uses "VRM" extension
                if (json.Contains("\"VRM\""))
                {
                    Debug.Log("[RuntimeFBXLoaderBridge] Detected VRM 0.x format");
                    return VrmVersion.VRM_0_x;
                }

                Debug.LogWarning("[RuntimeFBXLoaderBridge] Could not detect VRM version, assuming VRM 0.x");
                AlertBarController.WarnVrmVersionUnknown();
                return VrmVersion.VRM_0_x;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] Error detecting VRM version: {e.Message}");
                return VrmVersion.Unknown;
            }
        }

        /// <summary>
        /// VRMファイルをロード（VRM 0.x / 1.0 両対応）
        /// </summary>
        private async UniTask LoadVRMFile(Action<float> onProgress, Action<bool> onComplete)
        {
            // ログキャプチャ開始
            string fileName = System.IO.Path.GetFileNameWithoutExtension(browser.SelectedPath);
            FBXImportLogger.StartCapture($"VRM_Import_{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}");

            try
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Starting VRM load: {browser.SelectedPath}");

                // Issue #440: チャンク化ファイル読み込み（0-40%をファイル読み込みに割り当て）
                byte[] bytes = await ChunkedFileReader.ReadAllBytesAsync(
                    browser.SelectedPath,
                    onProgress,
                    progressStart: 0f,
                    progressEnd: 40f);
                Debug.Log($"[RuntimeFBXLoaderBridge] Read {bytes.Length} bytes from file");

                // テレメトリ計測開始
                StartTelemetryMeasurement(browser.SelectedPath, bytes.Length);

                onProgress?.Invoke(40f);

                // VRMバージョンを検出
                loadedVrmVersion = DetectVrmVersion(bytes);

                if (loadedVrmVersion == VrmVersion.VRM_1_0)
                {
                    // VRM 1.0 の読み込み
                    Debug.Log("[RuntimeFBXLoaderBridge] Loading as VRM 1.0 using Vrm10.LoadBytesAsync...");
                    // ControlRigGenerationOption.None: ControlRigがAnimator.avatarを上書きするのを防ぐ
                    // これにより、AnimatorOverrideControllerでのポーズ切り替えが正常に動作する
                    // Issue #440: 圧縮テクスチャデシリアライザを使用
                    currentVrm10Instance = await Vrm10.LoadBytesAsync(
                        bytes: bytes,
                        canLoadVrm0X: false,
                        showMeshes: true,
                        awaitCaller: new RuntimeOnlyAwaitCaller(),
                        textureDeserializer: TextureDeserializer,
                        controlRigGenerationOption: ControlRigGenerationOption.None
                    );

                    if (currentVrm10Instance == null)
                    {
                        Debug.LogError("[RuntimeFBXLoaderBridge] Failed to create VRM 1.0 instance!");
                        AlertBarController.ErrorVrmLoadFailed("VRM 1.0インスタンスの作成に失敗");
                        onComplete?.Invoke(false);
                        return;
                    }

                    currentModel = currentVrm10Instance.gameObject;
                    Debug.Log($"[RuntimeFBXLoaderBridge] VRM 1.0 instance created: {currentModel.name}");

                    // Issue #444: VRM 1.0 非サポート通知
                    AlertBarController.ShowWarning("[NOT SUPPORTED] A VRM 1.0 file is detected. Pier currently DOES NOT support loading VRM 1.0 files.");
                }
                else if (loadedVrmVersion == VrmVersion.VRM_0_x)
                {
                    // VRM 0.x の読み込み
                    Debug.Log("[RuntimeFBXLoaderBridge] Loading as VRM 0.x using VrmUtility.LoadBytesAsync...");
                    currentGltfInstance = await VrmUtility.LoadBytesAsync(
                        path: fileName,
                        bytes: bytes,
                        awaitCaller: new RuntimeOnlyAwaitCaller(),
                        materialGeneratorCallback: null,
                        metaCallback: null,
                        textureDeserializer: TextureDeserializer,
                        loadAnimation: false,
                        springboneRuntime: null
                    );

                    if (currentGltfInstance == null)
                    {
                        Debug.LogError("[RuntimeFBXLoaderBridge] Failed to create VRM 0.x instance!");
                        AlertBarController.ErrorVrmLoadFailed("VRM 0.xインスタンスの作成に失敗");
                        onComplete?.Invoke(false);
                        return;
                    }

                    // メッシュの表示
                    currentGltfInstance.EnableUpdateWhenOffscreen();
                    currentGltfInstance.ShowMeshes();

                    currentModel = currentGltfInstance.Root;
                    Debug.Log($"[RuntimeFBXLoaderBridge] VRM 0.x instance created: {currentModel.name}");
                }
                else
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] Unknown VRM version, cannot load!");
                    AlertBarController.ErrorVrmLoadFailed("VRMバージョンを特定できません");
                    onComplete?.Invoke(false);
                    return;
                }

                onProgress?.Invoke(70f);

                Debug.Log("[RuntimeFBXLoaderBridge] VRM instance created successfully");
                Debug.Log($"[RuntimeFBXLoaderBridge] Avatar root: {currentModel.name}");

                // モデルを配置
                PlaceModel(currentModel);

                onProgress?.Invoke(90f);

                Debug.Log($"[RuntimeFBXLoaderBridge] VRM load completed successfully (Version: {loadedVrmVersion})");

                // VRMメタデータをログ出力
                LogVrmMetadata();

                // レストポーズでスクリーンショットを撮影（Animator設定前）
                // レンダリングが確実に完了するよう1フレーム待機
                await UniTask.Yield();
                await UniTask.WaitForEndOfFrame(this);

                Debug.Log("[RuntimeFBXLoaderBridge] Capturing rest pose screenshot (before animation setup)...");
                FBXImportLogger.CaptureMultiAngleScreenshot(currentModel);
                FBXImportLogger.StopCaptureAndSave(takeScreenshot: true);
                Debug.Log("[RuntimeFBXLoaderBridge] VRM import log and screenshots saved.");

                // アニメーションの設定（スクリーンショット撮影後）
                SetupAnimator(currentModel);

                // Blob Shadow（足元の丸影）にアバターを設定
                SetupBlobShadow(currentModel.transform);

                onProgress?.Invoke(100f);

                onComplete?.Invoke(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] VRM load failed: {e.Message}");
                Debug.LogException(e);

                // アラートバーでエラー表示
                AlertBarController.ErrorVrmLoadFailed(e.Message);

                // エラー時もログを保存
                FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);

                onComplete?.Invoke(false);
            }
        }

        /// <summary>
        /// VRMファイルをロード（パス直接指定版・スロット用）
        /// </summary>
        private async UniTask LoadVRMFileFromPath(string filePath, Action<float> onProgress, Action<bool> onComplete)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);
            FBXImportLogger.StartCapture($"VRM_Import_{fileName}_{DateTime.Now:yyyyMMdd_HHmmss}");

            try
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Starting VRM load from path: {filePath}");

                // Issue #440: チャンク化ファイル読み込み（0-40%をファイル読み込みに割り当て）
                byte[] bytes = await ChunkedFileReader.ReadAllBytesAsync(
                    filePath,
                    onProgress,
                    progressStart: 0f,
                    progressEnd: 40f);
                Debug.Log($"[RuntimeFBXLoaderBridge] Read {bytes.Length} bytes from file");

                // テレメトリ計測開始
                StartTelemetryMeasurement(filePath, bytes.Length);

                onProgress?.Invoke(40f);

                loadedVrmVersion = DetectVrmVersion(bytes);

                if (loadedVrmVersion == VrmVersion.VRM_1_0)
                {
                    Debug.Log("[RuntimeFBXLoaderBridge] Loading as VRM 1.0...");
                    // ControlRigGenerationOption.None: ControlRigがAnimator.avatarを上書きするのを防ぐ
                    // Issue #440: 圧縮テクスチャデシリアライザを使用
                    currentVrm10Instance = await Vrm10.LoadBytesAsync(
                        bytes: bytes,
                        canLoadVrm0X: false,
                        showMeshes: true,
                        awaitCaller: new RuntimeOnlyAwaitCaller(),
                        textureDeserializer: TextureDeserializer,
                        controlRigGenerationOption: ControlRigGenerationOption.None
                    );

                    if (currentVrm10Instance == null)
                    {
                        Debug.LogError("[RuntimeFBXLoaderBridge] Failed to create VRM 1.0 instance!");
                        AlertBarController.ErrorVrmLoadFailed("VRM 1.0インスタンスの作成に失敗");
                        ResetSlotState();
                        onComplete?.Invoke(false);
                        return;
                    }

                    currentModel = currentVrm10Instance.gameObject;

                    // Issue #444: VRM 1.0 非サポート通知
                    AlertBarController.ShowWarning("[NOT SUPPORTED] A VRM 1.0 file is detected. Pier currently DOES NOT support loading VRM 1.0 files.");
                }
                else if (loadedVrmVersion == VrmVersion.VRM_0_x)
                {
                    Debug.Log("[RuntimeFBXLoaderBridge] Loading as VRM 0.x...");
                    currentGltfInstance = await VrmUtility.LoadBytesAsync(
                        path: fileName,
                        bytes: bytes,
                        awaitCaller: new RuntimeOnlyAwaitCaller(),
                        materialGeneratorCallback: null,
                        metaCallback: null,
                        textureDeserializer: TextureDeserializer,
                        loadAnimation: false,
                        springboneRuntime: null
                    );

                    if (currentGltfInstance == null)
                    {
                        Debug.LogError("[RuntimeFBXLoaderBridge] Failed to create VRM 0.x instance!");
                        AlertBarController.ErrorVrmLoadFailed("VRM 0.xインスタンスの作成に失敗");
                        ResetSlotState();
                        onComplete?.Invoke(false);
                        return;
                    }

                    currentGltfInstance.EnableUpdateWhenOffscreen();
                    currentGltfInstance.ShowMeshes();
                    currentModel = currentGltfInstance.Root;
                }
                else
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] Unknown VRM version!");
                    AlertBarController.ErrorVrmLoadFailed("VRMバージョンを特定できません");
                    ResetSlotState();
                    onComplete?.Invoke(false);
                    return;
                }

                onProgress?.Invoke(70f);

                PlaceModel(currentModel);
                LogVrmMetadata();

                await UniTask.Yield();
                await UniTask.WaitForEndOfFrame(this);

                FBXImportLogger.CaptureMultiAngleScreenshot(currentModel);
                FBXImportLogger.StopCaptureAndSave(takeScreenshot: true);

                SetupAnimator(currentModel);

                // Blob Shadow（足元の丸影）にアバターを設定
                SetupBlobShadow(currentModel.transform);

                // アイコン撮影
                await CaptureIconIfNeeded();

                // テレメトリ送信
                if (loadedVrmVersion == VrmVersion.VRM_1_0)
                    SendVrm10SuccessTelemetry();
                else
                    SendVrm0xSuccessTelemetry();

                onProgress?.Invoke(100f);
                onComplete?.Invoke(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] VRM load failed: {e.Message}");
                AlertBarController.ErrorVrmLoadFailed(e.Message);
                FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);

                // テレメトリ送信（失敗）
                SendFailureTelemetry(
                    loadedVrmVersion == VrmVersion.VRM_1_0 ? "VRM_1_0" : "VRM_0_x",
                    e.Message);

                ResetSlotState();
                onComplete?.Invoke(false);
            }
        }

        /// <summary>
        /// FBXファイルをロード（パス直接指定版・スロット用）
        /// </summary>
        private async UniTask LoadFBXFileFromPath(string filePath, Action<float> onProgress, Action<bool> onComplete)
        {
            try
            {
                FBXImportLogger.StartCapture();

                Debug.Log($"[RuntimeFBXLoaderBridge] Starting FBX load from path: {filePath}");

                // テレメトリ計測開始
                long fileSize = 0;
                try { fileSize = new System.IO.FileInfo(filePath).Length; } catch { }
                StartTelemetryMeasurement(filePath, fileSize);

                onProgress?.Invoke(20f);

                var loader = new RuntimeAssimpFBXLoader();
                currentModel = await loader.LoadBoneHierarchy(filePath);

                if (currentModel == null)
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] Failed to load FBX skeleton");
                    AlertBarController.ErrorFbxLoadFailed("スケルトンの読み込みに失敗");
                    FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);
                    ResetSlotState();
                    onProgress?.Invoke(100f);
                    onComplete?.Invoke(false);
                    return;
                }

                onProgress?.Invoke(30f);

                PlaceModel(currentModel);
                onProgress?.Invoke(40f);

                await loader.LoadMeshes(currentModel);
                onProgress?.Invoke(50f);

                var visualizer = currentModel.AddComponent<AICam.DebugTools.BoneDebugVisualizer>();
                onProgress?.Invoke(60f);

                var boneMap = loader.MapHumanoidBones(currentModel.transform);
                onProgress?.Invoke(70f);

                var avatarBuilder = new RuntimeHumanoidAvatarBuilder();
                UnityEngine.Avatar newAvatar = null;

                if (avatarTemplate != null)
                {
                    newAvatar = avatarBuilder.CreateHumanoidAvatarFromTemplate(
                        currentModel.name,
                        currentModel,
                        avatarTemplate);
                }
                else
                {
                    newAvatar = avatarBuilder.CreateHumanoidAvatarFromFBX(currentModel.name, currentModel);
                }

                onProgress?.Invoke(80f);

                bool loadSuccess = false;

                if (newAvatar != null && newAvatar.isValid && newAvatar.isHuman)
                {
                    var animator = currentModel.GetComponent<Animator>();
                    if (animator == null)
                    {
                        animator = currentModel.AddComponent<Animator>();
                    }

                    animator.avatar = newAvatar;
                    animator.applyRootMotion = true;

                    onProgress?.Invoke(85f);

                    var materialManager = new RuntimeMaterialManager();
                    var meshNodeToMaterialNames = loader.GetMeshNodeToMaterialNames();
                    await materialManager.AssignMaterials(currentModel, filePath, meshNodeToMaterialNames);
                    onProgress?.Invoke(90f);

                    SetupAnimator(currentModel);

                    // Blob Shadow（足元の丸影）にアバターを設定
                    SetupBlobShadow(currentModel.transform);

                    // アイコン撮影
                    await CaptureIconIfNeeded();

                    onProgress?.Invoke(100f);
                    loadSuccess = true;
                }
                else
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] Failed to create Avatar");
                    AlertBarController.ErrorAvatarBuildFailed($"IsValid: {newAvatar?.isValid}, IsHuman: {newAvatar?.isHuman}");
                    loadSuccess = false;
                }

                await UniTask.Yield();

                if (loadSuccess && currentModel != null)
                {
                    FBXImportLogger.CaptureMultiAngleScreenshot(currentModel);
                    FBXImportLogger.StopCaptureAndSave(takeScreenshot: true);

                    // テレメトリ送信（成功）
                    SendFbxSuccessTelemetry();
                }
                else
                {
                    FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);

                    // テレメトリ送信（失敗）
                    SendFailureTelemetry("FBX", "Avatar creation failed");

                    ResetSlotState();
                }

                onComplete?.Invoke(loadSuccess);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] FBX load failed: {e.Message}");
                AlertBarController.ErrorFbxLoadFailed(e.Message);
                FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);

                // テレメトリ送信（失敗）
                SendFailureTelemetry("FBX", e.Message);

                ResetSlotState();
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

                // テレメトリ計測開始
                var fileInfo = new System.IO.FileInfo(browser.SelectedPath);
                StartTelemetryMeasurement(browser.SelectedPath, fileInfo.Length);

                onProgress?.Invoke(20f);

                // Assimpでボーン階層をロード（非同期）
                var loader = new RuntimeAssimpFBXLoader();
                currentModel = await loader.LoadBoneHierarchy(browser.SelectedPath);

                if (currentModel == null)
                {
                    Debug.LogError("[RuntimeFBXLoaderBridge] Failed to load FBX skeleton");
                    AlertBarController.ErrorFbxLoadFailed("スケルトンの読み込みに失敗");
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

                    onProgress?.Invoke(85f);

                    // マテリアルを適用（Avatar生成後、SkinnedMeshRendererが確実に存在するタイミング）
                    Debug.Log("[RuntimeFBXLoaderBridge] Assigning materials from MaterialCacheDatabase...");
                    var materialManager = new RuntimeMaterialManager();
                    var meshNodeToMaterialNames = loader.GetMeshNodeToMaterialNames();
                    await materialManager.AssignMaterials(currentModel, browser.SelectedPath, meshNodeToMaterialNames);
                    onProgress?.Invoke(90f);

                    // アニメーションの設定
                    SetupAnimator(currentModel);

                    // Blob Shadow（足元の丸影）にアバターを設定
                    SetupBlobShadow(currentModel.transform);

                    onProgress?.Invoke(100f);
                    loadSuccess = true;
                }
                else
                {
                    Debug.LogError($"[RuntimeFBXLoaderBridge] ✗ Failed to create Avatar. IsValid: {newAvatar?.isValid}, IsHuman: {newAvatar?.isHuman}");
                    AlertBarController.ErrorAvatarBuildFailed($"IsValid: {newAvatar?.isValid}, IsHuman: {newAvatar?.isHuman}");
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

                // アラートバーでエラー表示
                AlertBarController.ErrorFbxLoadFailed(e.Message);

                // エラー時もログを保存
                FBXImportLogger.StopCaptureAndSave(takeScreenshot: false);

                onComplete?.Invoke(false);
            }
        }

        /// <summary>
        /// モデルを配置
        /// Issue #425: アバターロード後にカメラの1m前方に表示するよう初期位置を調整
        /// Issue #433: マテリアルキャッシュをクリアしてライティング設定を正しく反映
        /// </summary>
        private void PlaceModel(GameObject model)
        {
            if (model == null) return;

            Transform parent = modelParent != null ? modelParent : transform;
            model.transform.SetParent(parent, false);

            // Issue #425: カメラの1m前方に配置（デバイス位置からアバター内部が見える問題を回避）
            Camera mainCamera = Camera.main;
            Debug.Log($"[RuntimeFBXLoaderBridge] Issue #425: Camera.main = {(mainCamera != null ? mainCamera.name : "null")}");

            if (mainCamera != null)
            {
                // カメラの前方1mの位置を計算（Y軸は0=地面レベル）
                Vector3 cameraPosition = mainCamera.transform.position;
                Vector3 cameraForward = mainCamera.transform.forward;
                Debug.Log($"[RuntimeFBXLoaderBridge] Issue #425: Camera position = {cameraPosition}, forward = {cameraForward}");

                // 水平面上の前方ベクトルを計算（Y成分を0に）
                Vector3 horizontalForward = new Vector3(cameraForward.x, 0f, cameraForward.z);
                float horizontalMagnitude = horizontalForward.magnitude;
                Debug.Log($"[RuntimeFBXLoaderBridge] Issue #425: horizontalForward = {horizontalForward}, magnitude = {horizontalMagnitude}");

                Vector3 spawnPosition;

                // 水平方向成分が十分にある場合のみ使用（カメラが真上/真下を向いている場合は使えない）
                if (horizontalMagnitude > 0.1f)
                {
                    horizontalForward = horizontalForward / horizontalMagnitude; // 正規化
                    // カメラの水平前方1mの位置、地面レベルに配置
                    spawnPosition = new Vector3(
                        cameraPosition.x + horizontalForward.x * 1.0f,
                        0f, // 地面レベル
                        cameraPosition.z + horizontalForward.z * 1.0f
                    );
                    Debug.Log($"[RuntimeFBXLoaderBridge] Issue #425: Using camera-relative position");
                }
                else
                {
                    // カメラが真上/真下を向いている場合はカメラのZ方向に1m前に配置
                    spawnPosition = new Vector3(cameraPosition.x, 0f, cameraPosition.z + 1.0f);
                    Debug.Log($"[RuntimeFBXLoaderBridge] Issue #425: Camera pointing up/down, using fallback Z+1m");
                }

                model.transform.position = spawnPosition;

                // カメラの方を向かせる（Y軸のみ回転）
                Vector3 lookDirection = new Vector3(cameraPosition.x - spawnPosition.x, 0f, cameraPosition.z - spawnPosition.z);
                if (lookDirection.sqrMagnitude > 0.001f)
                {
                    model.transform.rotation = Quaternion.LookRotation(lookDirection);
                }
                else
                {
                    // lookDirectionがゼロに近い場合はデフォルトの向き
                    model.transform.rotation = Quaternion.identity;
                }

                Debug.Log($"[RuntimeFBXLoaderBridge] Issue #425: Avatar placed at {spawnPosition}, rotation = {model.transform.rotation.eulerAngles}");
            }
            else
            {
                // フォールバック: カメラが見つからない場合は従来の位置を使用
                model.transform.localPosition = modelPosition;
                model.transform.localRotation = Quaternion.Euler(modelRotation);
                Debug.Log($"[RuntimeFBXLoaderBridge] Issue #425 Fallback: Camera not found, using default position {modelPosition}");
            }

            model.transform.localScale = modelScale;

            Debug.Log($"[RuntimeFBXLoaderBridge] Model placed at World Position: {model.transform.position}");
            Debug.Log($"[RuntimeFBXLoaderBridge] Model Rotation: {model.transform.rotation.eulerAngles}");
            Debug.Log($"[RuntimeFBXLoaderBridge] Model Scale: {model.transform.lossyScale}");
            Debug.Log($"[RuntimeFBXLoaderBridge] Parent: {(parent != null ? parent.name : "null")}");

            // Issue #433/#442: LightingPanelControllerのライティング・シャドウ設定を再適用
            ReapplyLightingSettings();

            // レンダラーの確認
            var renderers = model.GetComponentsInChildren<Renderer>();
            Debug.Log($"[RuntimeFBXLoaderBridge] Found {renderers.Length} renderers");
            foreach (var renderer in renderers)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Renderer: {renderer.name}, Enabled: {renderer.enabled}, Layer: {renderer.gameObject.layer}");
            }
        }

        /// <summary>
        /// Issue #433/#442: LightingPanelControllerのライティング・シャドウ設定を再適用
        /// 新しいアバターがロードされた時に呼び出す
        ///
        /// 修正: FindFirstObjectByType<LightingPanelController> ではなく
        /// CameraCaptureController.ReapplyLightingSettings() を使用
        /// LightingPanelController は遅延初期化されるため、直接検索すると null になる
        /// </summary>
        private void ReapplyLightingSettings()
        {
            ILightingSettingsProvider lightingProvider = null;
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (mb is ILightingSettingsProvider provider)
                {
                    lightingProvider = provider;
                    break;
                }
            }

            if (lightingProvider != null)
            {
                lightingProvider.ReapplyLightingSettings();
                Debug.Log("[RuntimeFBXLoaderBridge] Issue #442: Delegated to ILightingSettingsProvider.ReapplyLightingSettings()");
            }
            else
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] ILightingSettingsProvider not found");
            }
        }

        /// <summary>
        /// Animatorの設定
        /// Issue #430: 診断ログを強化して問題特定を容易に
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

            // Issue #430: Avatarの詳細な診断ログ
            if (animator.avatar != null)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Avatar is assigned: {animator.avatar.name}, IsValid: {animator.avatar.isValid}, IsHuman: {animator.avatar.isHuman}");

                // Humanoid Avatarの場合、ボーンマッピングを検証
                if (animator.avatar.isHuman)
                {
                    ValidateHumanoidBones(animator, model.name);
                }
                else
                {
                    Debug.LogWarning($"[RuntimeFBXLoaderBridge] Issue #430: Avatar '{animator.avatar.name}' is NOT Humanoid. Pose playback will not work correctly.");
                    AlertBarController.ShowWarning($"アバター '{model.name}' はHumanoidではありません。ポーズが正常に動作しない可能性があります。");
                }
            }
            else
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] ⚠ Avatar is NOT assigned! Humanoid animation will not work.");
                AlertBarController.ShowWarning($"アバター '{model.name}' のAvatarが未設定です。ポーズが動作しません。");
            }

            // Issue #407: AnimatorControllerはCameraCaptureController.ApplyDefaultAOCで設定するため、ここでは設定しない
            Debug.Log($"[RuntimeFBXLoaderBridge] Animator setup complete. Controller will be assigned by CameraCaptureController.ApplyDefaultAOC()");
        }

        /// <summary>
        /// Issue #430: Humanoidボーンマッピングを検証
        /// 必須ボーンが正しくマッピングされているかチェック
        /// </summary>
        private void ValidateHumanoidBones(Animator animator, string modelName)
        {
            // 必須ボーンのリスト
            HumanBodyBones[] requiredBones = new HumanBodyBones[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg
            };

            int missingCount = 0;
            System.Text.StringBuilder missingBones = new System.Text.StringBuilder();

            foreach (var bone in requiredBones)
            {
                Transform boneTransform = animator.GetBoneTransform(bone);
                if (boneTransform == null)
                {
                    missingCount++;
                    if (missingBones.Length > 0) missingBones.Append(", ");
                    missingBones.Append(bone.ToString());
                }
            }

            if (missingCount > 0)
            {
                Debug.LogWarning($"[RuntimeFBXLoaderBridge] Issue #430: Model '{modelName}' is missing {missingCount} required bones: {missingBones}");
                AlertBarController.ShowWarning($"アバター '{modelName}' に必須ボーンが不足しています。ポーズが正常に動作しない可能性があります。");
            }
            else
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Issue #430: All required Humanoid bones are present for '{modelName}'");
            }
        }

        /// <summary>
        /// Blob Shadow（足元の丸影）を設定
        /// BlobShadowControllerが存在しなければ作成する
        /// </summary>
        private void SetupBlobShadow(Transform avatarRoot)
        {
            if (avatarRoot == null) return;

            var blobShadow = AICam.AR.BlobShadowController.Instance;

            // BlobShadowControllerが存在しなければ作成
            if (blobShadow == null)
            {
                var shadowObj = new GameObject("BlobShadow");
                blobShadow = shadowObj.AddComponent<AICam.AR.BlobShadowController>();
                Debug.Log("[RuntimeFBXLoaderBridge] Created BlobShadowController");
            }

            // アバターを設定して有効化
            blobShadow.SetAvatar(avatarRoot);
            blobShadow.SetEnabled(true);
            Debug.Log($"[RuntimeFBXLoaderBridge] BlobShadow set for avatar: {avatarRoot.name}");
        }

        /// <summary>
        /// 現在のモデルを削除（キャッシュ済みモデルは破棄しない）
        /// 注: アバターの非アクティブ化と位置保存はAvatarSlotManager側で行う
        /// </summary>
        public void ClearCurrentModel()
        {
            Debug.Log("[RuntimeFBXLoaderBridge] ClearCurrentModel called");

            bool isCached = false;

            if (this.memoryCache != null && currentModel != null)
            {
                // キャッシュに存在するモデルは破棄しない（非アクティブ化のみ）
                for (int i = 0; i < 10; i++)
                {
                    var cached = this.memoryCache.GetCachedAvatar(i);
                    if (cached == currentModel)
                    {
                        isCached = true;
                        // キャッシュ済みモデルは非アクティブ化のみ
                        currentModel.SetActive(false);
                        Debug.Log($"[RuntimeFBXLoaderBridge] Model is cached, deactivating only: {currentModel.name}");
                        break;
                    }
                }
            }

            if (!isCached && currentModel != null)
            {
                // VRM 0.x インスタンスの破棄
                if (currentGltfInstance != null)
                {
                    Debug.Log("[RuntimeFBXLoaderBridge] Disposing VRM 0.x instance");
                    currentGltfInstance.Dispose();
                    currentGltfInstance = null;
                }

                // VRM 1.0 インスタンスの破棄
                if (currentVrm10Instance != null)
                {
                    Debug.Log("[RuntimeFBXLoaderBridge] Destroying VRM 1.0 instance");
                    Destroy(currentVrm10Instance.gameObject);
                    currentVrm10Instance = null;
                }

                Debug.Log("[RuntimeFBXLoaderBridge] Destroying model");
                Destroy(currentModel);
            }

            currentModel = null;
            currentGltfInstance = null;
            currentVrm10Instance = null;
            loadedVrmVersion = VrmVersion.Unknown;
            Debug.Log("[RuntimeFBXLoaderBridge] Model cleared");
        }

        /// <summary>
        /// スロットが削除された時の通知を受け取る
        /// 削除されたスロットが現在のスロットの場合、参照をクリアする
        /// </summary>
        public void OnSlotCleared(int slotIndex)
        {
            Debug.Log($"[RuntimeFBXLoaderBridge] OnSlotCleared: slot {slotIndex}, currentSlot {currentSlotIndex}");

            if (currentSlotIndex == slotIndex)
            {
                // 削除されたスロットが現在のスロットの場合、参照をクリア
                // GameObjectはAvatarMemoryCache側で破棄されるので、ここでは参照のみクリア
                currentModel = null;
                currentGltfInstance = null;
                currentVrm10Instance = null;
                loadedVrmVersion = VrmVersion.Unknown;
                currentSlotIndex = -1;
                Debug.Log("[RuntimeFBXLoaderBridge] Current model reference cleared due to slot deletion");
            }
        }

        /// <summary>
        /// キャッシュから復元したモデルを現在のモデルとして設定
        /// メモリキャッシュからの復元時に使用（内部用）
        /// </summary>
        public void SetCurrentModel(GameObject model, int slotIndex)
        {
            if (model == null)
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] Cannot set null model");
                return;
            }

            // 現在のモデルがあれば非アクティブ化（破棄はしない、キャッシュに残す）
            if (currentModel != null && currentModel != model)
            {
                currentModel.SetActive(false);
            }

            currentModel = model;
            currentSlotIndex = slotIndex;

            // モデルをアクティブ化
            model.SetActive(true);

            // Issue #436: キャッシュから復元時にレンダラーが無効のままになる問題を修正
            // すべてのレンダラーを有効化
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.enabled = true;
            }

            // VRMバージョンを検出
            var vrm10 = model.GetComponent<Vrm10Instance>();
            if (vrm10 != null)
            {
                currentVrm10Instance = vrm10;
                loadedVrmVersion = VrmVersion.VRM_1_0;
            }
            else
            {
                // VRM 0.xの場合はRuntimeGltfInstanceは再取得できないので Unknown のまま
                loadedVrmVersion = VrmVersion.Unknown;
            }

            // Blob Shadow（足元の丸影）にアバターを設定
            SetupBlobShadow(model.transform);

            Debug.Log($"[RuntimeFBXLoaderBridge] Set current model from cache: {model.name}, slot: {slotIndex}");
        }

        /// <summary>
        /// キャッシュから復元したモデルを現在のモデルとして設定し、最終位置に配置する
        /// AvatarSlotManagerから呼ばれる
        /// 注: 他アバターの非アクティブ化はAvatarMemoryCache.ActivateAvatarで行われるため、ここでは行わない
        /// </summary>
        public void SetCurrentModelFromCache(GameObject model, int slotIndex)
        {
            if (model == null)
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] Cannot set null model from cache");
                return;
            }

            Debug.Log($"[RuntimeFBXLoaderBridge] SetCurrentModelFromCache: slot {slotIndex}, model {model.name}");

            currentModel = model;
            currentSlotIndex = slotIndex;

            // キャッシュから最終位置を復元、なければデフォルト位置を使用
            Transform parent = modelParent != null ? modelParent : transform;
            bool restoredFromCache = false;

            if (this.memoryCache != null)
            {
                var cacheEntry = this.memoryCache.GetCacheEntry(slotIndex);
                if (cacheEntry != null && cacheEntry.hasLastTransform)
                {
                    // 保存された最終位置に復元
                    cacheEntry.RestoreTransform(parent);
                    restoredFromCache = true;
                    Debug.Log($"[RuntimeFBXLoaderBridge] Restored to last position: {cacheEntry.lastWorldPosition}");
                }
            }

            if (!restoredFromCache)
            {
                // デフォルト位置に配置（初回ロード時など）
                model.transform.SetParent(parent, false);
                model.transform.localPosition = modelPosition;
                model.transform.localRotation = Quaternion.Euler(modelRotation);
                model.transform.localScale = modelScale;
                Debug.Log($"[RuntimeFBXLoaderBridge] Using default position: {modelPosition}");
            }

            // モデルをアクティブ化（ActivateAvatarで既にアクティブだが、念のため）
            model.SetActive(true);

            // レンダラーの表示を確認・有効化
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                renderer.enabled = true;
            }

            // VRMバージョンを検出
            var vrm10 = model.GetComponent<Vrm10Instance>();
            if (vrm10 != null)
            {
                currentVrm10Instance = vrm10;
                currentGltfInstance = null;
                loadedVrmVersion = VrmVersion.VRM_1_0;
            }
            else
            {
                currentVrm10Instance = null;
                // VRM 0.xの場合、メッシュの再表示を試みる
                var gltfInstance = model.GetComponent<RuntimeGltfInstance>();
                if (gltfInstance != null)
                {
                    gltfInstance.ShowMeshes();
                    loadedVrmVersion = VrmVersion.VRM_0_x;
                }
                else
                {
                    loadedVrmVersion = VrmVersion.Unknown;
                }
            }

            // Blob Shadow（足元の丸影）にアバターを設定
            SetupBlobShadow(model.transform);

            Debug.Log($"[RuntimeFBXLoaderBridge] Restored model from cache: {model.name}, slot: {slotIndex}");
            Debug.Log($"[RuntimeFBXLoaderBridge] Final Position: {model.transform.position}, Parent: {model.transform.parent?.name ?? "null"}");
        }

        void OnDestroy()
        {
            ClearCurrentModel();
        }

        /// <summary>
        /// Issue #346: バックグラウンド復帰時のモデル参照整合性チェック
        /// </summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            if (!pauseStatus)
            {
                // バックグラウンドから復帰
                ValidateCurrentModelReference();
            }
        }

        /// <summary>
        /// Issue #346: currentModel参照が有効かチェックし、無効なら参照をクリア
        /// </summary>
        private void ValidateCurrentModelReference()
        {
            // Unity の fake null チェック
            bool isValid = currentModel != null && currentModel;

            if (!isValid && currentModel != null)
            {
                Debug.LogWarning($"[RuntimeFBXLoaderBridge] Current model reference is invalid (destroyed during background), clearing references");
                currentModel = null;
                currentGltfInstance = null;
                currentVrm10Instance = null;
                loadedVrmVersion = VrmVersion.Unknown;
                currentSlotIndex = -1;
            }
            else if (isValid)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Current model reference is valid: {currentModel.name}");
            }
        }

        /// <summary>
        /// VRMメタデータをログ出力（VRM 0.x / 1.0 両対応）
        /// </summary>
        private void LogVrmMetadata()
        {
            if (currentModel == null)
            {
                Debug.LogWarning("[RuntimeFBXLoaderBridge] VRM instance is null, cannot log metadata");
                return;
            }

            var root = currentModel;
            Debug.Log($"[RuntimeFBXLoaderBridge] === VRM METADATA (Version: {loadedVrmVersion}) ===");

            // VRM Metaデータ
            if (loadedVrmVersion == VrmVersion.VRM_1_0 && currentVrm10Instance != null)
            {
                // VRM 1.0 Meta
                if (currentVrm10Instance.Vrm != null && currentVrm10Instance.Vrm.Meta != null)
                {
                    var meta = currentVrm10Instance.Vrm.Meta;
                    Debug.Log($"[RuntimeFBXLoaderBridge] VRM 1.0 Meta:");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Name: {meta.Name ?? "N/A"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Version: {meta.Version ?? "N/A"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Authors: {(meta.Authors != null ? string.Join(", ", meta.Authors) : "N/A")}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   CopyrightInformation: {meta.CopyrightInformation ?? "N/A"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   AvatarPermission: {meta.AvatarPermission}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   CommercialUsage: {meta.CommercialUsage}");
                }
                else
                {
                    Debug.LogWarning($"[RuntimeFBXLoaderBridge] VRM 1.0 Meta is NULL");
                }
            }
            else
            {
                // VRM 0.x Meta
                var vrmMeta = root.GetComponent<VRMMeta>();
                if (vrmMeta != null && vrmMeta.Meta != null)
                {
                    var meta = vrmMeta.Meta;
                    Debug.Log($"[RuntimeFBXLoaderBridge] VRM 0.x Meta:");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Title: {meta.Title ?? "N/A"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Version: {meta.Version ?? "N/A"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Author: {meta.Author ?? "N/A"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   ContactInformation: {meta.ContactInformation ?? "N/A"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   AllowedUser: {meta.AllowedUser}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   CommercialUssage: {meta.CommercialUssage}");
                }
                else
                {
                    Debug.LogWarning($"[RuntimeFBXLoaderBridge] VRM Meta is NULL");
                }
            }

            // Humanoidデータ
            Debug.Log($"[RuntimeFBXLoaderBridge] === HUMANOID BONE MAPPING ===");

            // VRM 1.0 の場合は Vrm10Instance.Humanoid を使用
            UniHumanoid.Humanoid humanoid = null;
            if (loadedVrmVersion == VrmVersion.VRM_1_0 && currentVrm10Instance != null)
            {
                // VRM 1.0 Humanoid からボーン情報をログ
                var vrm10Humanoid = currentVrm10Instance.Humanoid;
                if (vrm10Humanoid != null)
                {
                    Debug.Log($"[RuntimeFBXLoaderBridge] VRM 1.0 Humanoid data found:");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Hips: {vrm10Humanoid.Hips?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Spine: {vrm10Humanoid.Spine?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Chest: {vrm10Humanoid.Chest?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   UpperChest: {vrm10Humanoid.UpperChest?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Neck: {vrm10Humanoid.Neck?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   Head: {vrm10Humanoid.Head?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   LeftShoulder: {vrm10Humanoid.LeftShoulder?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   LeftUpperArm: {vrm10Humanoid.LeftUpperArm?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   RightShoulder: {vrm10Humanoid.RightShoulder?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   RightUpperArm: {vrm10Humanoid.RightUpperArm?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   LeftUpperLeg: {vrm10Humanoid.LeftUpperLeg?.name ?? "NULL"}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]   RightUpperLeg: {vrm10Humanoid.RightUpperLeg?.name ?? "NULL"}");

                    // 左右反転チェック（UniHumanoid.Humanoidを使用）
                    LogLeftRightBoneComparison(vrm10Humanoid);
                }
                else
                {
                    Debug.LogError($"[RuntimeFBXLoaderBridge] VRM 1.0 Humanoid data is NULL!");
                }
            }
            else
            {
                // VRM 0.x
                humanoid = root.GetComponent<UniHumanoid.Humanoid>();
            }

            if (humanoid != null)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] Humanoid data found:");

                // 主要なボーン
                Debug.Log($"[RuntimeFBXLoaderBridge]   Hips: {humanoid.Hips?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   Spine: {humanoid.Spine?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   Chest: {humanoid.Chest?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   UpperChest: {humanoid.UpperChest?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   Neck: {humanoid.Neck?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   Head: {humanoid.Head?.name ?? "NULL"}");

                // 左腕
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftShoulder: {humanoid.LeftShoulder?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftUpperArm: {humanoid.LeftUpperArm?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftLowerArm: {humanoid.LeftLowerArm?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftHand: {humanoid.LeftHand?.name ?? "NULL"}");

                // 右腕
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightShoulder: {humanoid.RightShoulder?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightUpperArm: {humanoid.RightUpperArm?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightLowerArm: {humanoid.RightLowerArm?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightHand: {humanoid.RightHand?.name ?? "NULL"}");

                // 左脚
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftUpperLeg: {humanoid.LeftUpperLeg?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftLowerLeg: {humanoid.LeftLowerLeg?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftFoot: {humanoid.LeftFoot?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftToes: {humanoid.LeftToes?.name ?? "NULL"}");

                // 右脚
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightUpperLeg: {humanoid.RightUpperLeg?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightLowerLeg: {humanoid.RightLowerLeg?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightFoot: {humanoid.RightFoot?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightToes: {humanoid.RightToes?.name ?? "NULL"}");

                // 指（左手）
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftThumbProximal: {humanoid.LeftThumbProximal?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftIndexProximal: {humanoid.LeftIndexProximal?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftMiddleProximal: {humanoid.LeftMiddleProximal?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftRingProximal: {humanoid.LeftRingProximal?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   LeftLittleProximal: {humanoid.LeftLittleProximal?.name ?? "NULL"}");

                // 指（右手）
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightThumbProximal: {humanoid.RightThumbProximal?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightIndexProximal: {humanoid.RightIndexProximal?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightMiddleProximal: {humanoid.RightMiddleProximal?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightRingProximal: {humanoid.RightRingProximal?.name ?? "NULL"}");
                Debug.Log($"[RuntimeFBXLoaderBridge]   RightLittleProximal: {humanoid.RightLittleProximal?.name ?? "NULL"}");

                // 左右ボーンの位置を比較（左右反転チェック）
                LogLeftRightBoneComparison(humanoid);
            }
            else
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] Humanoid data is NULL!");
            }

            // メッシュ情報
            Debug.Log($"[RuntimeFBXLoaderBridge] === MESH INFORMATION ===");
            var renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            Debug.Log($"[RuntimeFBXLoaderBridge] SkinnedMeshRenderer count: {renderers.Length}");
            foreach (var renderer in renderers)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge]   Mesh: {renderer.name}");
                if (renderer.sharedMesh != null)
                {
                    Debug.Log($"[RuntimeFBXLoaderBridge]     Vertices: {renderer.sharedMesh.vertexCount}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]     BlendShapes: {renderer.sharedMesh.blendShapeCount}");
                    Debug.Log($"[RuntimeFBXLoaderBridge]     SubMeshes: {renderer.sharedMesh.subMeshCount}");
                }
                if (renderer.sharedMaterials != null)
                {
                    Debug.Log($"[RuntimeFBXLoaderBridge]     Materials: {renderer.sharedMaterials.Length}");
                    foreach (var mat in renderer.sharedMaterials)
                    {
                        if (mat != null)
                        {
                            Debug.Log($"[RuntimeFBXLoaderBridge]       - {mat.name} (Shader: {mat.shader?.name ?? "NULL"})");
                        }
                    }
                }
            }

            // SpringBone情報 (VRM 0.x)
            Debug.Log($"[RuntimeFBXLoaderBridge] === SPRINGBONE INFORMATION ===");
            var springBones = root.GetComponentsInChildren<VRMSpringBone>(true);
            if (springBones != null && springBones.Length > 0)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] SpringBone data found");
                Debug.Log($"[RuntimeFBXLoaderBridge]   VRMSpringBone count: {springBones.Length}");
                int totalRootBones = 0;
                foreach (var sb in springBones)
                {
                    totalRootBones += sb.RootBones?.Count ?? 0;
                }
                Debug.Log($"[RuntimeFBXLoaderBridge]   Total root bones: {totalRootBones}");
            }
            else
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] SpringBone is NULL (no physics bones)");
            }

            Debug.Log($"[RuntimeFBXLoaderBridge] === END VRM METADATA ===");
        }

        /// <summary>
        /// 左右ボーンの位置を比較してログ出力（左右反転チェック用）
        /// </summary>
        private void LogLeftRightBoneComparison(UniHumanoid.Humanoid humanoid)
        {
            Debug.Log($"[RuntimeFBXLoaderBridge] === LEFT/RIGHT BONE POSITION CHECK ===");

            if (humanoid == null) return;

            // 肩の比較
            if (humanoid.LeftShoulder != null && humanoid.RightShoulder != null)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] LeftShoulder World X: {humanoid.LeftShoulder.position.x:F4}");
                Debug.Log($"[RuntimeFBXLoaderBridge] RightShoulder World X: {humanoid.RightShoulder.position.x:F4}");
                Debug.Log($"[RuntimeFBXLoaderBridge] -> Left should have NEGATIVE X, Right should have POSITIVE X (Unity convention)");
            }

            // 股関節の比較
            if (humanoid.LeftUpperLeg != null && humanoid.RightUpperLeg != null)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] LeftUpperLeg World X: {humanoid.LeftUpperLeg.position.x:F4}");
                Debug.Log($"[RuntimeFBXLoaderBridge] RightUpperLeg World X: {humanoid.RightUpperLeg.position.x:F4}");
                Debug.Log($"[RuntimeFBXLoaderBridge] -> Left should have NEGATIVE X, Right should have POSITIVE X (Unity convention)");
            }

            // 腕の比較
            if (humanoid.LeftUpperArm != null && humanoid.RightUpperArm != null)
            {
                Debug.Log($"[RuntimeFBXLoaderBridge] LeftUpperArm World X: {humanoid.LeftUpperArm.position.x:F4}");
                Debug.Log($"[RuntimeFBXLoaderBridge] RightUpperArm World X: {humanoid.RightUpperArm.position.x:F4}");
            }

            Debug.Log($"[RuntimeFBXLoaderBridge] === END LEFT/RIGHT CHECK ===");
        }

        #region IAvatarLoader Implementation

        /// <summary>
        /// サポートする拡張子
        /// </summary>
        public string[] SupportedExtensions => new[] { ".vrm", ".fbx" };

        /// <summary>
        /// 指定ファイルをロード可能か
        /// </summary>
        public bool CanLoad(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;
            string ext = System.IO.Path.GetExtension(filePath).ToLower();
            return ext == ".vrm" || ext == ".fbx";
        }

        /// <summary>
        /// ファイルからアバターを非同期ロード（IAvatarLoader実装）
        /// キャッシュシステムから呼ばれる純粋なロード処理
        /// アイコン撮影やログ出力は行わない
        /// </summary>
        public async UniTask<AvatarLoadResult> LoadAsync(
            string filePath,
            Transform parent,
            Action<float> onProgress = null)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return AvatarLoadResult.Failed("File path is empty");
            }

            if (!System.IO.File.Exists(filePath))
            {
                return AvatarLoadResult.Failed($"File not found: {filePath}");
            }

            string ext = System.IO.Path.GetExtension(filePath).ToLower();

            try
            {
                if (ext == ".vrm")
                {
                    return await LoadVrmAsync(filePath, parent, onProgress);
                }
                else if (ext == ".fbx")
                {
                    return await LoadFbxAsync(filePath, parent, onProgress);
                }
                else
                {
                    return AvatarLoadResult.Failed($"Unsupported file extension: {ext}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeFBXLoaderBridge] LoadAsync failed: {e.Message}");
                return AvatarLoadResult.Failed(e.Message);
            }
        }

        /// <summary>
        /// VRMファイルを純粋にロード（IAvatarLoader用）
        /// </summary>
        private async UniTask<AvatarLoadResult> LoadVrmAsync(
            string filePath,
            Transform parent,
            Action<float> onProgress)
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(filePath);

            Debug.Log($"[RuntimeFBXLoaderBridge] LoadVrmAsync: {filePath}");

            // Issue #440: チャンク化ファイル読み込み（10-30%をファイル読み込みに割り当て）
            // ChunkedFileReaderは内部でYieldするのでSwitchToThreadPoolは不要
            byte[] bytes = await ChunkedFileReader.ReadAllBytesAsync(
                filePath,
                onProgress,
                progressStart: 10f,
                progressEnd: 30f);

            onProgress?.Invoke(30f);

            var version = DetectVrmVersion(bytes);
            GameObject avatar = null;
            string vrmVersionStr = version.ToString();

            if (version == VrmVersion.VRM_1_0)
            {
                // パース前にYield（重い処理の前）
                await UniTask.Yield();

                // ControlRigGenerationOption.None: ControlRigがAnimator.avatarを上書きするのを防ぐ
                // Issue #440: 圧縮テクスチャデシリアライザを使用
                var vrm10 = await Vrm10.LoadBytesAsync(
                    bytes: bytes,
                    canLoadVrm0X: false,
                    showMeshes: true,
                    awaitCaller: new RuntimeOnlyAwaitCaller(),
                    textureDeserializer: TextureDeserializer,
                    controlRigGenerationOption: ControlRigGenerationOption.None
                );

                if (vrm10 == null)
                {
                    return AvatarLoadResult.Failed("Failed to create VRM 1.0 instance");
                }

                avatar = vrm10.gameObject;
            }
            else if (version == VrmVersion.VRM_0_x)
            {
                // パース前にYield（重い処理の前）
                await UniTask.Yield();

                var gltfInstance = await VrmUtility.LoadBytesAsync(
                    path: fileName,
                    bytes: bytes,
                    awaitCaller: new RuntimeOnlyAwaitCaller(),
                    materialGeneratorCallback: null,
                    metaCallback: null,
                    textureDeserializer: TextureDeserializer,
                    loadAnimation: false,
                    springboneRuntime: null
                );

                if (gltfInstance == null)
                {
                    return AvatarLoadResult.Failed("Failed to create VRM 0.x instance");
                }

                gltfInstance.EnableUpdateWhenOffscreen();
                gltfInstance.ShowMeshes();
                avatar = gltfInstance.Root;
            }
            else
            {
                return AvatarLoadResult.Failed("Unknown VRM version");
            }

            // パース完了後にYield（UIの更新機会を与える）
            await UniTask.Yield();

            onProgress?.Invoke(70f);

            // 配置
            Transform targetParent = parent ?? (modelParent != null ? modelParent : transform);
            avatar.transform.SetParent(targetParent, false);
            avatar.transform.localPosition = modelPosition;
            avatar.transform.localRotation = Quaternion.Euler(modelRotation);
            avatar.transform.localScale = modelScale;

            onProgress?.Invoke(85f);

            // アニメーター設定
            SetupAnimator(avatar);

            onProgress?.Invoke(100f);

            Debug.Log($"[RuntimeFBXLoaderBridge] VRM loaded successfully: {avatar.name}");
            return AvatarLoadResult.Succeeded(avatar, vrmVersionStr);
        }

        /// <summary>
        /// FBXファイルを純粋にロード（IAvatarLoader用）
        /// </summary>
        private async UniTask<AvatarLoadResult> LoadFbxAsync(
            string filePath,
            Transform parent,
            Action<float> onProgress)
        {
            Debug.Log($"[RuntimeFBXLoaderBridge] LoadFbxAsync: {filePath}");
            onProgress?.Invoke(10f);

            // UIの応答性を維持するためにYield
            await UniTask.Yield();

            var loader = new RuntimeAssimpFBXLoader();
            var avatar = await loader.LoadBoneHierarchy(filePath);

            if (avatar == null)
            {
                return AvatarLoadResult.Failed("Failed to load FBX skeleton");
            }

            // ボーン読み込み後にYield
            await UniTask.Yield();

            onProgress?.Invoke(30f);

            // 配置
            Transform targetParent = parent ?? (modelParent != null ? modelParent : transform);
            avatar.transform.SetParent(targetParent, false);
            avatar.transform.localPosition = modelPosition;
            avatar.transform.localRotation = Quaternion.Euler(modelRotation);
            avatar.transform.localScale = modelScale;

            onProgress?.Invoke(40f);

            // メッシュロード前にYield
            await UniTask.Yield();

            await loader.LoadMeshes(avatar);

            // メッシュロード後にYield
            await UniTask.Yield();

            onProgress?.Invoke(50f);

            var boneMap = loader.MapHumanoidBones(avatar.transform);
            onProgress?.Invoke(60f);

            // Avatar生成前にYield
            await UniTask.Yield();

            var avatarBuilder = new RuntimeHumanoidAvatarBuilder();
            UnityEngine.Avatar newAvatar;

            if (avatarTemplate != null)
            {
                newAvatar = avatarBuilder.CreateHumanoidAvatarFromTemplate(
                    avatar.name, avatar, avatarTemplate);
            }
            else
            {
                newAvatar = avatarBuilder.CreateHumanoidAvatarFromFBX(avatar.name, avatar);
            }

            onProgress?.Invoke(70f);

            if (newAvatar == null || !newAvatar.isValid || !newAvatar.isHuman)
            {
                UnityEngine.Object.Destroy(avatar);
                return AvatarLoadResult.Failed($"Failed to create Avatar: IsValid={newAvatar?.isValid}, IsHuman={newAvatar?.isHuman}");
            }

            var animator = avatar.GetComponent<Animator>() ?? avatar.AddComponent<Animator>();
            animator.avatar = newAvatar;
            animator.applyRootMotion = true;

            onProgress?.Invoke(80f);

            // マテリアル割り当て前にYield
            await UniTask.Yield();

            var materialManager = new RuntimeMaterialManager();
            var meshNodeToMaterialNames = loader.GetMeshNodeToMaterialNames();
            await materialManager.AssignMaterials(avatar, filePath, meshNodeToMaterialNames);

            // マテリアル割り当て後にYield
            await UniTask.Yield();

            onProgress?.Invoke(90f);

            // アニメーター設定
            SetupAnimator(avatar);

            onProgress?.Invoke(100f);

            Debug.Log($"[RuntimeFBXLoaderBridge] FBX loaded successfully: {avatar.name}");
            return AvatarLoadResult.Succeeded(avatar, "FBX");
        }

        #region Telemetry

        /// <summary>
        /// テレメトリ計測を開始
        /// </summary>
        private void StartTelemetryMeasurement(string filePath, long fileSize)
        {
            _currentFilePath = filePath;
            _currentFileSize = fileSize;
            _loadStopwatch = Stopwatch.StartNew();
        }

        /// <summary>
        /// VRM 0.x ロード成功時のテレメトリを送信
        /// </summary>
        private void SendVrm0xSuccessTelemetry()
        {
            if (_loadStopwatch == null) return;
            _loadStopwatch.Stop();

            var dto = AvatarTelemetryCollector.CollectFromVrm0x(
                currentModel,
                _currentFilePath,
                _currentFileSize,
                (float)_loadStopwatch.Elapsed.TotalSeconds,
                success: true,
                slotIndex: currentSlotIndex
            );

            SendTelemetry(dto);
        }

        /// <summary>
        /// VRM 1.0 ロード成功時のテレメトリを送信
        /// </summary>
        private void SendVrm10SuccessTelemetry()
        {
            if (_loadStopwatch == null) return;
            _loadStopwatch.Stop();

            var dto = AvatarTelemetryCollector.CollectFromVrm10(
                currentModel,
                _currentFilePath,
                _currentFileSize,
                (float)_loadStopwatch.Elapsed.TotalSeconds,
                success: true,
                slotIndex: currentSlotIndex
            );

            SendTelemetry(dto);
        }

        /// <summary>
        /// FBX ロード成功時のテレメトリを送信
        /// </summary>
        private void SendFbxSuccessTelemetry()
        {
            if (_loadStopwatch == null) return;
            _loadStopwatch.Stop();

            var dto = AvatarTelemetryCollector.CollectFromFBX(
                currentModel,
                _currentFilePath,
                _currentFileSize,
                (float)_loadStopwatch.Elapsed.TotalSeconds,
                success: true,
                slotIndex: currentSlotIndex
            );

            SendTelemetry(dto);
        }

        /// <summary>
        /// ロード失敗時のテレメトリを送信
        /// </summary>
        private void SendFailureTelemetry(string vrmVersion, string errorMessage)
        {
            if (_loadStopwatch == null) return;
            _loadStopwatch.Stop();

            AvatarLoadTelemetryDTO dto;

            switch (vrmVersion)
            {
                case "VRM_0_x":
                    dto = AvatarTelemetryCollector.CollectFromVrm0x(
                        null, _currentFilePath, _currentFileSize,
                        (float)_loadStopwatch.Elapsed.TotalSeconds,
                        success: false, errorMessage: errorMessage, slotIndex: currentSlotIndex);
                    break;
                case "VRM_1_0":
                    dto = AvatarTelemetryCollector.CollectFromVrm10(
                        null, _currentFilePath, _currentFileSize,
                        (float)_loadStopwatch.Elapsed.TotalSeconds,
                        success: false, errorMessage: errorMessage, slotIndex: currentSlotIndex);
                    break;
                default:
                    dto = AvatarTelemetryCollector.CollectFromFBX(
                        null, _currentFilePath, _currentFileSize,
                        (float)_loadStopwatch.Elapsed.TotalSeconds,
                        success: false, errorMessage: errorMessage, slotIndex: currentSlotIndex);
                    break;
            }

            SendTelemetry(dto);
        }

        /// <summary>
        /// テレメトリをサーバーに送信
        /// </summary>
        private void SendTelemetry(AvatarLoadTelemetryDTO dto)
        {
            Debug.Log($"[Telemetry Debug] SendTelemetry called, dto={dto != null}");

            if (dto == null)
            {
                Debug.LogWarning("[Telemetry Debug] dto is null, skipping");
                return;
            }

            var client = TelemetryClient.Instance;
            Debug.Log($"[Telemetry Debug] TelemetryClient.Instance={client != null}, IsEnabled={client?.IsEnabled}");
            if (client != null && client.IsEnabled)
            {
                client.SendAvatarLoadTelemetry(dto, success =>
                {
                    AICamLogger.Log(AICamLogger.Category.Telemetry,
                        $"Avatar telemetry sent: {dto.fileName}, success={dto.success}, sent={success}");
                });
            }
            else
            {
                AICamLogger.Log(AICamLogger.Category.Telemetry,
                    $"TelemetryClient not available, skipping: {dto.fileName}");
            }
        }

        #endregion

        /// <summary>
        /// アバターを破棄（IAvatarLoader実装）
        /// VRM等のリソースを適切に解放
        /// </summary>
        public void DisposeAvatar(GameObject avatar)
        {
            if (avatar == null) return;

            Debug.Log($"[RuntimeFBXLoaderBridge] DisposeAvatar: {avatar.name}");

            // VRM 1.0 チェック
            var vrm10 = avatar.GetComponent<Vrm10Instance>();
            if (vrm10 != null)
            {
                Debug.Log("[RuntimeFBXLoaderBridge] Destroying VRM 1.0 instance");
                Destroy(avatar);
                return;
            }

            // VRM 0.x チェック
            var gltfInstance = avatar.GetComponent<RuntimeGltfInstance>();
            if (gltfInstance != null)
            {
                Debug.Log("[RuntimeFBXLoaderBridge] Disposing VRM 0.x instance");
                gltfInstance.Dispose();
                return;
            }

            // 通常のGameObject
            Destroy(avatar);
        }

        #endregion
    }
}
