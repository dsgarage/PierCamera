using UnityEngine;
using VRM;
using UniGLTF;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine.Networking;

public class VRMLoaderFromStreaming : MonoBehaviour
{
    [Header("VRM ファイル名（拡張子なし）")]
    public string vrmFileName = "Eku_VRM_v1_0_0";

    [Header("読み込み元（Resources or StreamingAssets）")]
    public LoadSource loadSource = LoadSource.StreamingAssets;

    [Header("読み込んだモデルの親 Transform（未指定ならこの GameObject 配下）")]
    public Transform parent;

    [Header("適用する AnimatorController（任意）")]
    public RuntimeAnimatorController animatorController;

    [Header("読み込み後、自動でこのステートを再生（任意）")]
    public string initialStateName = "StandingLocomotion_Eku";

    private RuntimeGltfInstance _loadedInstance;
    public GameObject LoadedModel => _loadedInstance?.Root;

    private bool _isLoading;

    public enum LoadSource
    {
        Resources,
        StreamingAssets
    }

    private void Start()
    {
        // UIから LoadAsync を呼び出す形式に変更
        // 自動ロードは無効化
    }

    public async Task LoadAsync()
    {
        Debug.Log($"[VRMLoader] LoadAsync called. IsLoading: {_isLoading}");

        if (_isLoading)
        {
            Debug.LogWarning("[VRMLoader] Already loading, skipping...");
            return;
        }

        _isLoading = true;

        try
        {
            byte[] bytes;

            if (loadSource == LoadSource.Resources)
            {
                Debug.Log($"[VRMLoader] Loading VRM from Resources: {vrmFileName}");

                // Resourcesから読み込み
                var textAsset = Resources.Load<TextAsset>(vrmFileName);
                if (textAsset == null)
                {
                    Debug.LogError($"[VRMLoader] VRM ファイルが見つかりません: Resources/{vrmFileName}");
                    return;
                }

                bytes = textAsset.bytes;
                Debug.Log($"[VRMLoader] Loaded {bytes.Length} bytes from Resources");
            }
            else
            {
                // StreamingAssetsから読み込み
#if UNITY_ANDROID
                // Androidの場合、Application.streamingAssetsPathはjar:file://形式のURLを返す
                string path = Application.streamingAssetsPath + "/" + vrmFileName + ".vrm";
                Debug.Log($"[VRMLoader] Loading VRM from StreamingAssets (Android): {path}");

                using (var request = UnityWebRequest.Get(path))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[VRMLoader] Failed to load from StreamingAssets: {request.error}");
                        Debug.LogError($"[VRMLoader] Path: {path}");
                        return;
                    }

                    bytes = request.downloadHandler.data;
                    Debug.Log($"[VRMLoader] Read {bytes.Length} bytes from StreamingAssets via UnityWebRequest");
                }
#elif UNITY_IOS
                // iOSの場合、Application.streamingAssetsPathはファイルシステムパスを返すので、file://を追加
                string filePath = Path.Combine(Application.streamingAssetsPath, vrmFileName + ".vrm");
                string path = "file://" + filePath;
                Debug.Log($"[VRMLoader] Loading VRM from StreamingAssets (iOS): {path}");

                using (var request = UnityWebRequest.Get(path))
                {
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        await Task.Yield();
                    }

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        Debug.LogError($"[VRMLoader] Failed to load from StreamingAssets: {request.error}");
                        Debug.LogError($"[VRMLoader] Path: {path}");
                        Debug.LogError($"[VRMLoader] File path: {filePath}");
                        return;
                    }

                    bytes = request.downloadHandler.data;
                    Debug.Log($"[VRMLoader] Read {bytes.Length} bytes from StreamingAssets via UnityWebRequest");
                }
#else
                // デスクトップではFile.ReadAllBytesを使用
                var path = Path.Combine(Application.streamingAssetsPath, vrmFileName + ".vrm");
                Debug.Log($"[VRMLoader] Loading VRM from StreamingAssets: {path}");

                if (!File.Exists(path))
                {
                    Debug.LogError($"[VRMLoader] VRM ファイルが見つかりません: {path}");
                    return;
                }

                bytes = File.ReadAllBytes(path);
                Debug.Log($"[VRMLoader] Read {bytes.Length} bytes from file");
#endif
            }

            // VrmUtility を使って読み込み
            Debug.Log($"[VRMLoader] Calling VrmUtility.LoadBytesAsync...");
            _loadedInstance = await VrmUtility.LoadBytesAsync(
                path: vrmFileName,
                bytes: bytes,
                awaitCaller: new RuntimeOnlyAwaitCaller(),
                materialGeneratorCallback: null,
                metaCallback: null,
                textureDeserializer: null,
                loadAnimation: false,
                springboneRuntime: null
            );

            Debug.Log($"[VRMLoader] VrmUtility.LoadBytesAsync completed. Instance: {_loadedInstance != null}");

            if (_loadedInstance == null)
            {
                Debug.LogError("[VRMLoader] Failed to create VRM instance!");
                return;
            }

            // メッシュの表示と配置
            Debug.Log($"[VRMLoader] Enabling update when offscreen and showing meshes...");
            _loadedInstance.EnableUpdateWhenOffscreen();
            _loadedInstance.ShowMeshes();

            var loadedModel = _loadedInstance.Root;
            Debug.Log($"[VRMLoader] Root GameObject: {loadedModel.name}");

            var parentToUse = parent != null ? parent : transform;
            Debug.Log($"[VRMLoader] Setting parent to: {parentToUse.name}");
            loadedModel.transform.SetParent(parentToUse, false);
            loadedModel.transform.localPosition = Vector3.zero;
            loadedModel.transform.localRotation = Quaternion.Euler(0, 180, 0);
            loadedModel.transform.localScale = Vector3.one;

            Debug.Log($"[VRMLoader] Model Position: {loadedModel.transform.position}, Rotation: {loadedModel.transform.rotation.eulerAngles}");

            // Animator 設定
            Debug.Log($"[VRMLoader] Setting up Animator...");
            var animator = loadedModel.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.Log($"[VRMLoader] No Animator found, adding component...");
                animator = loadedModel.AddComponent<Animator>();
            }
            else
            {
                Debug.Log($"[VRMLoader] Animator already exists");
            }

            // AnimatorControllerが未指定の場合、Loco_Eku.controllerを探してロード
            if (animatorController == null)
            {
                Debug.Log($"[VRMLoader] No animator controller assigned, searching for Loco_Eku.controller...");

                // Resourcesフォルダから探す
                var controller = Resources.Load<RuntimeAnimatorController>("Loco_Eku");

                if (controller == null)
                {
                    // Resources内の全てのAnimatorControllerを検索
                    var controllers = Resources.LoadAll<RuntimeAnimatorController>("");
                    controller = controllers.FirstOrDefault(c => c.name == "Loco_Eku");

                    if (controller == null)
                    {
                        Debug.LogWarning($"[VRMLoader] Loco_Eku.controller not found in Resources folder");
                        Debug.LogWarning($"[VRMLoader] Please move Loco_Eku.controller to a Resources folder");
                    }
                }

                if (controller != null)
                {
                    animatorController = controller;
                    Debug.Log($"[VRMLoader] Loaded Loco_Eku controller: {controller.name}");
                }
            }

            if (animatorController != null)
            {
                Debug.Log($"[VRMLoader] Setting animator controller: {animatorController.name}");
                animator.runtimeAnimatorController = animatorController;
            }
            else
            {
                Debug.Log($"[VRMLoader] No animator controller assigned");
            }

            // 初期ステート再生
            if (!string.IsNullOrEmpty(initialStateName) && animator.runtimeAnimatorController != null)
            {
                Debug.Log($"[VRMLoader] Playing initial state: {initialStateName}");
                animator.Play(initialStateName, 0, 0f);
            }

            Debug.Log("[VRMLoader] VRM モデル読み込み完了");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[VRMLoader] VRM 読み込み中にエラー: {e}");
            Debug.LogException(e);
        }
        finally
        {
            _isLoading = false;
            Debug.Log($"[VRMLoader] LoadAsync finished. IsLoading: {_isLoading}");
        }
    }

    private void OnDestroy()
    {
        // リソースのクリーンアップ
        _loadedInstance?.Dispose();
    }
}
