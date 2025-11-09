using UnityEngine;
using VRM;
using UniGLTF;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;

namespace AICam.VRM
{
    /// <summary>
    /// VRMアバターの読み込み専用マネージャー（テンプレート生成）
    /// 責任: VRMファイルの読み込みとテンプレートGameObjectの生成のみ
    /// インスタンス化と配置はAvatarInstanceManagerが行う
    /// </summary>
    public class VRMAvatarManager : MonoBehaviour
    {
        private RuntimeGltfInstance currentInstance;
        private GameObject currentAvatarTemplate;
        private static bool shadersPreloaded = false;

        /// <summary>
        /// 現在読み込まれているアバターテンプレート（非アクティブ）
        /// </summary>
        public GameObject CurrentAvatar => currentAvatarTemplate;

        /// <summary>
        /// VRM読み込みに必要なシェーダーをプリロードする
        /// </summary>
        private void PreloadShaders()
        {
            if (shadersPreloaded) return;

            var shaderNames = new List<string>
            {
                "UniGLTF/UniUnlit",
                "VRM10/MToon10",
                "VRM10/Universal Render Pipeline/MToon10",
                "VRM/MToon"
            };

            int loadedCount = 0;
            foreach (var shaderName in shaderNames)
            {
                var shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    // シェーダーをウォームアップ（コンパイル）
                    Shader.WarmupAllShaders();
                    loadedCount++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    Debug.Log($"[VRMAvatarManager] Preloaded shader: {shaderName}");
#endif
                }
                else
                {
                    Debug.LogWarning($"[VRMAvatarManager] Shader not found: {shaderName}");
                }
            }

            shadersPreloaded = true;
            Debug.Log($"[VRMAvatarManager] Preloaded {loadedCount}/{shaderNames.Count} shaders");
        }

        private void Awake()
        {
            // アプリ起動時にシェーダーをプリロード
            PreloadShaders();
        }

        /// <summary>
        /// ファイルパスからVRMを読み込み、テンプレートとして返す
        /// 返されるGameObjectは非アクティブ状態
        /// インスタンス化と配置はAvatarInstanceManagerが行う
        /// </summary>
        public async UniTask<GameObject> LoadVRMFromPathAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                Debug.LogError("[VRMAvatarManager] File path is null or empty");
                return null;
            }

            Debug.Log($"[VRMAvatarManager] Loading VRM from: {filePath}");

            // シェーダーがまだロードされていない場合はロード
            PreloadShaders();

            try
            {
                // 既存のテンプレートを削除
                if (currentInstance != null)
                {
                    Debug.Log("[VRMAvatarManager] Disposing previous template instance");
                    currentInstance.Dispose();
                    currentInstance = null;
                }

                if (currentAvatarTemplate != null)
                {
                    Debug.Log("[VRMAvatarManager] Destroying previous template");
                    Destroy(currentAvatarTemplate);
                    currentAvatarTemplate = null;
                }

                // ファイルの存在確認
                if (!System.IO.File.Exists(filePath))
                {
                    Debug.LogError($"[VRMAvatarManager] File not found: {filePath}");
                    return null;
                }

                // ファイル読み込み
                byte[] bytes = System.IO.File.ReadAllBytes(filePath);
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

                currentAvatarTemplate = currentInstance.Root;
                currentAvatarTemplate.name = $"{System.IO.Path.GetFileNameWithoutExtension(filePath)}_Template";

                // テンプレートは非アクティブにする（インスタンス化時にアクティブになる）
                currentAvatarTemplate.SetActive(false);

                Debug.Log($"[VRMAvatarManager] Template created: {currentAvatarTemplate.name} (inactive)");
                Debug.Log("[VRMAvatarManager] VRM loaded successfully as template");

                return currentAvatarTemplate;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VRMAvatarManager] Error loading VRM: {e.Message}");
                Debug.LogError($"[VRMAvatarManager] File path: {filePath}");
                Debug.LogError($"[VRMAvatarManager] Exception type: {e.GetType().Name}");

                if (e.InnerException != null)
                {
                    Debug.LogError($"[VRMAvatarManager] Inner exception: {e.InnerException.Message}");
                }

                Debug.LogException(e);
                return null;
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[VRMAvatarManager] Generated thumbnail (placeholder)");
#endif
            return thumbnail;
        }

        /// <summary>
        /// 現在のアバターテンプレートを削除
        /// </summary>
        public void ClearCurrentAvatar()
        {
            if (currentInstance != null)
            {
                currentInstance.Dispose();
                currentInstance = null;
            }

            if (currentAvatarTemplate != null)
            {
                Destroy(currentAvatarTemplate);
                currentAvatarTemplate = null;
            }

            Debug.Log("[VRMAvatarManager] Current template cleared");
        }

        private void OnDestroy()
        {
            ClearCurrentAvatar();
        }
    }
}
