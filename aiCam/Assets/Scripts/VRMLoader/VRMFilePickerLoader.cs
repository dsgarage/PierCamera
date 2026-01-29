using UnityEngine;
using VRM;
using UniGLTF;
using System.Threading.Tasks;
using System.Linq;
using Cysharp.Threading.Tasks;
using AICam.Core.IO;
using AICam.Core.Texture;

public class VRMFilePickerLoader : MonoBehaviour
{
    [Header("読み込んだモデルの親 Transform（未指定ならこの GameObject 配下）")]
    public Transform parent;

    [Header("適用する AnimatorController（任意）")]
    public RuntimeAnimatorController animatorController;

    [Header("読み込み後、自動でこのステートを再生（任意）")]
    public string initialStateName = "StandingLocomotion_Eku";

    private RuntimeGltfInstance _loadedInstance;
    public GameObject LoadedModel => _loadedInstance?.Root;

    private bool _isLoading;

    // Issue #440: 圧縮テクスチャデシリアライザ (VRMテクスチャメモリ約89%削減)
    private static readonly CompressedTextureDeserializer _textureDeserializer = new CompressedTextureDeserializer();

    /// <summary>
    /// ファイルピッカーを使ってVRMを読み込む
    /// </summary>
    public async Task LoadFromFilePicker()
    {
        Debug.Log("[VRMFilePicker] Opening file picker...");

        if (_isLoading)
        {
            Debug.LogWarning("[VRMFilePicker] Already loading, skipping...");
            return;
        }

        _isLoading = true;

        try
        {
#if UNITY_EDITOR || (!UNITY_IOS && !UNITY_ANDROID)
            // Editorまたはデスクトップの場合はSystem.Windows.Formsを使用
            await LoadFromFilePickerDesktop();
#elif UNITY_IOS || UNITY_ANDROID
            // iOSまたはAndroidの場合はNativeFilePickerを使用
            await LoadFromFilePickerMobile();
#endif
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRMFilePicker] Error: {e}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    /// <summary>
    /// デスクトップ用のファイルピッカー
    /// </summary>
    private async Task LoadFromFilePickerDesktop()
    {
#if UNITY_EDITOR
        // Editorでは UnityEditor.EditorUtility.OpenFilePanel を使用
        string path = UnityEditor.EditorUtility.OpenFilePanel("Select VRM File", "", "vrm");

        if (string.IsNullOrEmpty(path))
        {
            Debug.Log("[VRMFilePicker] File selection cancelled");
            return;
        }

        Debug.Log($"[VRMFilePicker] Selected file: {path}");
        await LoadFromPath(path);
#else
        Debug.LogError("[VRMFilePicker] Desktop file picker not implemented for standalone builds");
#endif
    }

    /// <summary>
    /// モバイル用のファイルピッカー（NativeFilePicker使用）
    /// </summary>
    private async Task LoadFromFilePickerMobile()
    {
        // NativeFilePickerがインストールされている場合のみ動作
        // インストール方法: Package Manager → Add package from git URL
        // https://github.com/yasirkula/UnityNativeFilePicker.git

        try
        {
            Debug.Log("[VRMFilePicker] Opening NativeFilePicker...");

            // NativeFilePicker.PickFile() を直接呼び出し
            var tcs = new TaskCompletionSource<string>();

            // iOSではファイル拡張子ではなくUTI(Uniform Type Identifier)を使用
            // VRMファイル用のUTIは存在しないため、汎用的なファイルタイプを使用
#if UNITY_IOS
            string[] allowedFileTypes = new string[] { "public.data", "public.content", "public.item" };
            Debug.Log("[VRMFilePicker] iOS: Using UTI types for file picker");
#elif UNITY_ANDROID
            string[] allowedFileTypes = new string[] { "*/*" };
            Debug.Log("[VRMFilePicker] Android: Using MIME type for file picker");
#else
            string[] allowedFileTypes = new string[] { "*/*" };
#endif

            NativeFilePicker.PickFile((path) =>
            {
                Debug.Log($"[VRMFilePicker] File picker callback: {path}");
                tcs.SetResult(path);
            }, allowedFileTypes);

            Debug.Log("[VRMFilePicker] Waiting for file selection...");
            string selectedPath = await tcs.Task;

            if (string.IsNullOrEmpty(selectedPath))
            {
                Debug.Log("[VRMFilePicker] File selection cancelled");
                return;
            }

            Debug.Log($"[VRMFilePicker] Selected file: {selectedPath}");

            // VRMファイルかどうかを確認
            if (!selectedPath.ToLower().EndsWith(".vrm"))
            {
                Debug.LogWarning($"[VRMFilePicker] Selected file is not a VRM file: {selectedPath}");
                Debug.LogWarning("[VRMFilePicker] Attempting to load anyway...");
            }

            await LoadFromPath(selectedPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRMFilePicker] NativeFilePicker error: {e}");
            Debug.LogException(e);
        }
    }

    /// <summary>
    /// 指定されたパスからVRMを読み込む
    /// </summary>
    private async Task LoadFromPath(string path)
    {
        try
        {
            Debug.Log($"[VRMFilePicker] Loading VRM from: {path}");

            if (!System.IO.File.Exists(path))
            {
                Debug.LogError($"[VRMFilePicker] File not found: {path}");
                return;
            }

            // Issue #426/#440: チャンク化ファイル読み込みでUIフリーズを軽減
            byte[] bytes = await ChunkedFileReader.ReadAllBytesAsync(path);
            Debug.Log($"[VRMFilePicker] Read {bytes.Length} bytes");

            // VrmUtility を使って読み込み
            // Issue #440: 圧縮テクスチャデシリアライザを使用
            Debug.Log($"[VRMFilePicker] Calling VrmUtility.LoadBytesAsync...");
            _loadedInstance = await VrmUtility.LoadBytesAsync(
                path: System.IO.Path.GetFileName(path),
                bytes: bytes,
                awaitCaller: new RuntimeOnlyAwaitCaller(),
                materialGeneratorCallback: null,
                metaCallback: null,
                textureDeserializer: _textureDeserializer,
                loadAnimation: false,
                springboneRuntime: null
            );

            Debug.Log($"[VRMFilePicker] VrmUtility.LoadBytesAsync completed. Instance: {_loadedInstance != null}");

            if (_loadedInstance == null)
            {
                Debug.LogError("[VRMFilePicker] Failed to create VRM instance!");
                return;
            }

            // メッシュの表示と配置
            Debug.Log($"[VRMFilePicker] Enabling update when offscreen and showing meshes...");
            _loadedInstance.EnableUpdateWhenOffscreen();
            _loadedInstance.ShowMeshes();

            var loadedModel = _loadedInstance.Root;
            Debug.Log($"[VRMFilePicker] Root GameObject: {loadedModel.name}");

            var parentToUse = parent != null ? parent : transform;
            Debug.Log($"[VRMFilePicker] Setting parent to: {parentToUse.name}");
            loadedModel.transform.SetParent(parentToUse, false);
            loadedModel.transform.localPosition = Vector3.zero;
            loadedModel.transform.localRotation = Quaternion.Euler(0, 180, 0);
            loadedModel.transform.localScale = Vector3.one;

            Debug.Log($"[VRMFilePicker] Model Position: {loadedModel.transform.position}, Rotation: {loadedModel.transform.rotation.eulerAngles}");

            // Animator 設定
            Debug.Log($"[VRMFilePicker] Setting up Animator...");
            var animator = loadedModel.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.Log($"[VRMFilePicker] No Animator found, adding component...");
                animator = loadedModel.AddComponent<Animator>();
            }

            // AnimatorControllerが未指定の場合、Loco_Eku.controllerを探してロード
            if (animatorController == null)
            {
                Debug.Log($"[VRMFilePicker] No animator controller assigned, searching for Loco_Eku.controller...");

                // Resourcesフォルダから探す
                var controller = Resources.Load<RuntimeAnimatorController>("Loco_Eku");

                if (controller == null)
                {
                    var controllers = Resources.LoadAll<RuntimeAnimatorController>("");
                    controller = controllers.FirstOrDefault(c => c.name == "Loco_Eku");

                    if (controller == null)
                    {
                        Debug.LogWarning($"[VRMFilePicker] Loco_Eku.controller not found in Resources folder");
                    }
                }

                if (controller != null)
                {
                    animatorController = controller;
                    Debug.Log($"[VRMFilePicker] Loaded Loco_Eku controller: {controller.name}");
                }
            }

            if (animatorController != null)
            {
                Debug.Log($"[VRMFilePicker] Setting animator controller: {animatorController.name}");
                animator.runtimeAnimatorController = animatorController;
            }

            // 初期ステート再生
            if (!string.IsNullOrEmpty(initialStateName) && animator.runtimeAnimatorController != null)
            {
                Debug.Log($"[VRMFilePicker] Playing initial state: {initialStateName}");
                animator.Play(initialStateName, 0, 0f);
            }

            Debug.Log("[VRMFilePicker] VRM model loaded successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRMFilePicker] Error loading VRM: {e}");
            throw;
        }
    }

    private void OnDestroy()
    {
        // リソースのクリーンアップ
        _loadedInstance?.Dispose();
    }
}
