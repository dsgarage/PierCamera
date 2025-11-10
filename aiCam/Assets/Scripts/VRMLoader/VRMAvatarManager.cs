using UnityEngine;
using VRM;
using UniGLTF;
using Cysharp.Threading.Tasks;
using System;

namespace AICam.VRM
{
    /// <summary>
    /// VRMアバターの読み込みとAR空間への配置を管理
    /// </summary>
    public class VRMAvatarManager : MonoBehaviour
    {
        [Header("AR Configuration")]
        [SerializeField] private Transform arPlacementTarget;
        [SerializeField] private Vector3 defaultScale = Vector3.one;

        [Header("Animation")]
        [SerializeField] private RuntimeAnimatorController defaultAnimatorController;
        [SerializeField] private string initialStateName = "Idle";

        private RuntimeGltfInstance currentInstance;
        private GameObject currentAvatarRoot;

        /// <summary>
        /// 現在読み込まれているアバターのGameObject
        /// </summary>
        public GameObject CurrentAvatar => currentAvatarRoot;

        /// <summary>
        /// ファイルパスからVRMを読み込んでAR空間に配置
        /// </summary>
        public async UniTask<GameObject> LoadVRMFromPathAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[VRMAvatarManager] File path is null or empty");
                return null;
            }

            Debug.Log($"[VRMAvatarManager] Loading VRM from: {filePath}");

            try
            {
                // 既存のアバターを削除
                if (currentInstance != null)
                {
                    Debug.Log("[VRMAvatarManager] Disposing previous avatar instance");
                    currentInstance.Dispose();
                    currentInstance = null;
                }

                if (currentAvatarRoot != null)
                {
                    Debug.Log("[VRMAvatarManager] Destroying previous avatar root");
                    Destroy(currentAvatarRoot);
                    currentAvatarRoot = null;
                }

                // ファイルの存在確認
                if (!System.IO.File.Exists(filePath))
                {
                    Debug.LogError($"[VRMAvatarManager] File not found: {filePath}");
                    return null;
                }

                // ファイル読み込み
                byte[] bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                Debug.Log($"[VRMAvatarManager] Read {bytes.Length} bytes from file");

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
                    Debug.LogError("[VRMAvatarManager] Failed to load VRM");
                    return null;
                }

                Debug.Log("[VRMAvatarManager] VRM instance created successfully");

                // メッシュの表示設定
                currentInstance.EnableUpdateWhenOffscreen();
                currentInstance.ShowMeshes();

                currentAvatarRoot = currentInstance.Root;
                Debug.Log($"[VRMAvatarManager] Avatar root: {currentAvatarRoot.name}");

                // AR空間への配置
                PlaceInARSpace(currentAvatarRoot);

                // アニメーションの設定
                SetupAnimator(currentAvatarRoot);

                Debug.Log("[VRMAvatarManager] VRM loaded and placed in AR space successfully");
                return currentAvatarRoot;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VRMAvatarManager] Error loading VRM: {e.Message}");
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

            Debug.Log($"[VRMAvatarManager] Avatar placed at: {avatar.transform.position}");
        }

        /// <summary>
        /// Animatorの設定
        /// </summary>
        private void SetupAnimator(GameObject avatar)
        {
            if (avatar == null) return;

            var animator = avatar.GetComponent<Animator>();
            if (animator == null)
            {
                animator = avatar.AddComponent<Animator>();
                Debug.Log("[VRMAvatarManager] Added Animator component");
            }

            // AnimatorControllerの設定
            if (defaultAnimatorController != null)
            {
                animator.runtimeAnimatorController = defaultAnimatorController;
                Debug.Log($"[VRMAvatarManager] Set animator controller: {defaultAnimatorController.name}");

                // 初期ステートの再生
                if (!string.IsNullOrEmpty(initialStateName))
                {
                    animator.Play(initialStateName, 0, 0f);
                    Debug.Log($"[VRMAvatarManager] Playing initial state: {initialStateName}");
                }
            }
            else
            {
                Debug.LogWarning("[VRMAvatarManager] No animator controller assigned");
            }
        }

        /// <summary>
        /// サムネイル生成用のスクリーンショットを撮影
        /// </summary>
        public async UniTask<Texture2D> GenerateThumbnailAsync(GameObject avatar)
        {
            if (avatar == null)
            {
                Debug.LogError("[VRMAvatarManager] Avatar is null, cannot generate thumbnail");
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

            Debug.Log("[VRMAvatarManager] Generated thumbnail (placeholder)");
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

            Debug.Log("[VRMAvatarManager] Current avatar cleared");
        }

        private void OnDestroy()
        {
            ClearCurrentAvatar();
        }
    }
}
