using UnityEngine;
using VRM;
using UniGLTF;
using Cysharp.Threading.Tasks;
using System;
using AICam.FBXLoader.IO;

namespace AICam.VRM
{
    /// <summary>
    /// VRM/FBXアバターのランタイム読み込みとAR空間への配置を管理
    /// </summary>
    public class RuntimeAvatarLoader : MonoBehaviour
    {
        [Header("AR Configuration")]
        [SerializeField] private Transform arPlacementTarget;
        [SerializeField] private Vector3 defaultScale = Vector3.one;

        [Header("Animation")]
        [SerializeField] private RuntimeAnimatorController faceAnimatorController;  // 表情用（BlendShape制御）
        [SerializeField] private RuntimeAnimatorController bodyAnimatorController;  // ポーズ用（Transform制御）
        [SerializeField] private string initialStateName = "Idle";

        private RuntimeGltfInstance currentInstance;
        private GameObject currentAvatarRoot;

        /// <summary>
        /// 現在読み込まれているアバターのGameObject
        /// </summary>
        public GameObject CurrentAvatar => currentAvatarRoot;

        /// <summary>
        /// ファイルピッカーを開いてVRMを読み込む
        /// </summary>
        public async UniTask<GameObject> LoadFromFilePicker()
        {
            Debug.Log("[RuntimeAvatarLoader] Opening file picker...");

            try
            {
#if UNITY_EDITOR
                // Editorでは UnityEditor.EditorUtility.OpenFilePanel を使用
                string path = UnityEditor.EditorUtility.OpenFilePanel("Select VRM File", "", "vrm");

                if (string.IsNullOrEmpty(path))
                {
                    Debug.Log("[RuntimeAvatarLoader] File selection cancelled");
                    return null;
                }

                Debug.Log($"[RuntimeAvatarLoader] Selected file: {path}");
                return await LoadVRMFromPathAsync(path);
#elif UNITY_IOS || UNITY_ANDROID
                // iOSまたはAndroidの場合はNativeFilePickerを使用
                return await LoadFromFilePickerMobile();
#else
                Debug.LogError("[RuntimeAvatarLoader] File picker not supported on this platform");
                return null;
#endif
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeAvatarLoader] Error opening file picker: {e}");
                return null;
            }
        }

#if UNITY_IOS || UNITY_ANDROID
        /// <summary>
        /// モバイル用のファイルピッカー（NativeFilePicker使用）
        /// </summary>
        private async UniTask<GameObject> LoadFromFilePickerMobile()
        {
            try
            {
                Debug.Log("[RuntimeAvatarLoader] Opening NativeFilePicker...");

                var tcs = new UniTaskCompletionSource<string>();

                // iOSではUTI、AndroidではMIMEタイプを使用
#if UNITY_IOS
                string[] allowedFileTypes = new string[] { "public.data", "public.content", "public.item" };
                Debug.Log("[RuntimeAvatarLoader] iOS: Using UTI types for file picker");
#elif UNITY_ANDROID
                string[] allowedFileTypes = new string[] { "*/*" };
                Debug.Log("[RuntimeAvatarLoader] Android: Using MIME type for file picker");
#endif

                NativeFilePicker.PickFile((path) =>
                {
                    Debug.Log($"[RuntimeAvatarLoader] File picker callback: {path}");
                    tcs.TrySetResult(path);
                }, allowedFileTypes);

                Debug.Log("[RuntimeAvatarLoader] Waiting for file selection...");
                string selectedPath = await tcs.Task;

                if (string.IsNullOrEmpty(selectedPath))
                {
                    Debug.Log("[RuntimeAvatarLoader] File selection cancelled");
                    return null;
                }

                Debug.Log($"[RuntimeAvatarLoader] Selected file: {selectedPath}");

                // VRMファイルかどうかを確認
                if (!selectedPath.ToLower().EndsWith(".vrm"))
                {
                    Debug.LogWarning($"[RuntimeAvatarLoader] Selected file may not be a VRM file: {selectedPath}");
                }

                return await LoadVRMFromPathAsync(selectedPath);
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeAvatarLoader] NativeFilePicker error: {e}");
                return null;
            }
        }
#endif

        /// <summary>
        /// ファイルパスからVRMを読み込んでAR空間に配置
        /// </summary>
        public async UniTask<GameObject> LoadVRMFromPathAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[RuntimeAvatarLoader] File path is null or empty");
                return null;
            }

            Debug.Log($"[RuntimeAvatarLoader] Loading VRM from: {filePath}");

            try
            {
                // 既存のアバターを削除
                if (currentInstance != null)
                {
                    Debug.Log("[RuntimeAvatarLoader] Disposing previous avatar instance");
                    currentInstance.Dispose();
                    currentInstance = null;
                }

                if (currentAvatarRoot != null)
                {
                    Debug.Log("[RuntimeAvatarLoader] Destroying previous avatar root");
                    Destroy(currentAvatarRoot);
                    currentAvatarRoot = null;
                }

                // ファイルの存在確認
                if (!System.IO.File.Exists(filePath))
                {
                    Debug.LogError($"[RuntimeAvatarLoader] File not found: {filePath}");
                    return null;
                }

                // Issue #440: チャンク化ファイル読み込み
                byte[] bytes = await ChunkedFileReader.ReadAllBytesAsync(filePath);
                Debug.Log($"[RuntimeAvatarLoader] Read {bytes.Length} bytes from file");

                // VRMをパース
                currentInstance = await VrmUtility.LoadBytesAsync(
                    path: System.IO.Path.GetFileName(filePath),
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
                    Debug.LogError("[RuntimeAvatarLoader] Failed to load VRM");
                    return null;
                }

                Debug.Log("[RuntimeAvatarLoader] VRM instance created successfully");

                // メッシュの表示設定
                currentInstance.EnableUpdateWhenOffscreen();
                currentInstance.ShowMeshes();

                currentAvatarRoot = currentInstance.Root;
                Debug.Log($"[RuntimeAvatarLoader] Avatar root: {currentAvatarRoot.name}");

                // VRMを非表示にしておく（PlaceAvatarOnPlaneOnlyが配置する時に表示される）
                currentAvatarRoot.SetActive(false);
                Debug.Log("[RuntimeAvatarLoader] Avatar loaded but not placed yet. Tap on AR plane to place.");

                // アニメーションの設定（ファイルタイプに応じて）
                SetupAnimator(currentAvatarRoot, filePath);

                Debug.Log("[RuntimeAvatarLoader] VRM loaded successfully. Ready to be placed on AR plane.");
                return currentAvatarRoot;
            }
            catch (Exception e)
            {
                Debug.LogError($"[RuntimeAvatarLoader] Error loading VRM: {e.Message}");
                Debug.LogException(e);
                return null;
            }
        }

        /// <summary>
        /// アバターをAR空間に配置
        /// </summary>
        private void PlaceInARSpace(GameObject avatar)
        {
            if (avatar == null) return;

            Transform parentTransform = arPlacementTarget != null ? arPlacementTarget : transform;

            avatar.transform.SetParent(parentTransform, false);
            avatar.transform.localPosition = Vector3.zero;
            avatar.transform.localRotation = Quaternion.Euler(0, 180, 0); // ユーザーの方を向く
            avatar.transform.localScale = defaultScale;

            Debug.Log($"[RuntimeAvatarLoader] Avatar placed at: {avatar.transform.position}");
        }

        /// <summary>
        /// Animatorの設定（表情とポーズのAnimatorControllerを設定）
        /// </summary>
        private void SetupAnimator(GameObject avatar, string filePath)
        {
            if (avatar == null) return;

            var animator = avatar.GetComponent<Animator>();
            if (animator == null)
            {
                animator = avatar.AddComponent<Animator>();
                Debug.Log("[RuntimeAvatarLoader] Added Animator component");
            }

            // Issue #407: AnimatorControllerはCameraCaptureController.ApplyDefaultAOCで設定するため、ここでは設定しない
            Debug.Log($"[RuntimeAvatarLoader] Animator setup complete. Controller will be assigned by CameraCaptureController.ApplyDefaultAOC()");

            // 表情用AnimatorControllerは別途Animatorレイヤーとして追加することも可能
            // 現在は単純実装のため、bodyAnimatorControllerのみを使用
            // 将来的には UnityEditor.Animations.AnimatorController を使用して
            // レイヤーを追加する実装に拡張可能

            if (faceAnimatorController != null)
            {
                Debug.Log($"[RuntimeAvatarLoader] Face animator controller available: {faceAnimatorController.name}");
                Debug.Log("[RuntimeAvatarLoader] Note: Face animation layer integration can be implemented in the future");
            }
        }

        /// <summary>
        /// サムネイル生成用のスクリーンショットを撮影
        /// </summary>
        public async UniTask<Texture2D> GenerateThumbnailAsync(GameObject avatar)
        {
            if (avatar == null)
            {
                Debug.LogError("[RuntimeAvatarLoader] Avatar is null, cannot generate thumbnail");
                return null;
            }

            // TODO: アバターのスクリーンショットを撮影してサムネイルを生成
            // 仮実装：ダミーテクスチャを返す
            await UniTask.Delay(100);

            var thumbnail = new Texture2D(128, 128);
            Color[] pixels = new Color[128 * 128];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0.5f, 0.7f, 0.9f, 1f);
            }
            thumbnail.SetPixels(pixels);
            thumbnail.Apply();

            Debug.Log("[RuntimeAvatarLoader] Generated thumbnail (placeholder)");
            return thumbnail;
        }

        /// <summary>
        /// 現在のアバターを削除
        /// </summary>
        public void ClearCurrentAvatar()
        {
            if (currentInstance != null)
            {
                currentInstance.Dispose();
                currentInstance = null;
            }

            if (currentAvatarRoot != null)
            {
                Destroy(currentAvatarRoot);
                currentAvatarRoot = null;
            }

            Debug.Log("[RuntimeAvatarLoader] Current avatar cleared");
        }

        private void OnDestroy()
        {
            ClearCurrentAvatar();
        }
    }
}
