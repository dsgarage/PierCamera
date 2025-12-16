using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバターの顔アイコンを撮影するユーティリティ
    /// </summary>
    public class AvatarIconCapture : MonoBehaviour
    {
        public const int ICON_SIZE = 512;
        public const float CAMERA_DISTANCE_MULTIPLIER = 6.0f;  // 少し近づく
        public const float CAMERA_FOV = 15f;  // FOVを狭くして望遠効果
        public const float MIN_CAMERA_DISTANCE = 0.8f;  // 最小距離

        private static AvatarIconCapture _instance;
        public static AvatarIconCapture Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("AvatarIconCapture");
                    _instance = go.AddComponent<AvatarIconCapture>();
                    DontDestroyOnLoad(go);
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
        }

        /// <summary>
        /// アバターの顔アイコンを撮影してTexture2Dとして返す
        /// </summary>
        /// <param name="avatar">撮影対象のアバター</param>
        /// <returns>撮影したTexture2D、失敗した場合はnull</returns>
        public async UniTask<Texture2D> CaptureAsTextureAsync(GameObject avatar)
        {
            if (avatar == null)
            {
                Debug.LogError("[AvatarIconCapture] Avatar is null");
                return null;
            }

            // 元のアクティブ状態を保存
            bool wasActive = avatar.activeSelf;

            try
            {
                Debug.Log($"[AvatarIconCapture] Starting capture for: {avatar.name}, wasActive: {wasActive}");

                // 撮影のために一時的にアクティブにする
                if (!wasActive)
                {
                    avatar.SetActive(true);
                    Debug.Log("[AvatarIconCapture] Temporarily activated avatar for capture");
                }

                // 頭部の位置を取得
                Transform head = FindHead(avatar);
                if (head == null)
                {
                    Debug.LogWarning("[AvatarIconCapture] Head bone not found, using model center");
                    head = avatar.transform;
                }

                // レンダリングが安定するまで待機
                await UniTask.Yield();
                await UniTask.WaitForEndOfFrame(this);

                // 撮影
                Texture2D icon = CaptureHeadIcon(avatar, head);
                if (icon == null)
                {
                    Debug.LogError("[AvatarIconCapture] Failed to capture icon");
                    return null;
                }

                Debug.Log($"[AvatarIconCapture] Captured icon: {icon.width}x{icon.height}");
                return icon;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarIconCapture] Error capturing icon: {e.Message}");
                Debug.LogException(e);
                return null;
            }
            finally
            {
                // 元の状態に戻す（ただし、ロード直後は通常アクティブのままにする）
                // 注: 呼び出し元で状態管理するため、ここでは戻さない
            }
        }

        /// <summary>
        /// アバターの顔アイコンを撮影して保存
        /// </summary>
        /// <param name="avatar">撮影対象のアバター</param>
        /// <param name="savePath">保存先パス（.png）</param>
        /// <returns>保存に成功した場合はパス、失敗した場合はnull</returns>
        public async UniTask<string> CaptureAndSaveAsync(GameObject avatar, string savePath)
        {
            if (avatar == null)
            {
                Debug.LogError("[AvatarIconCapture] Avatar is null");
                return null;
            }

            try
            {
                Debug.Log($"[AvatarIconCapture] Starting capture for: {avatar.name}");

                // 頭部の位置を取得
                Transform head = FindHead(avatar);
                if (head == null)
                {
                    Debug.LogWarning("[AvatarIconCapture] Head bone not found, using model center");
                    head = avatar.transform;
                }

                // 1フレーム待ってレンダリングを安定させる
                await UniTask.Yield();

                // 撮影
                Texture2D icon = CaptureHeadIcon(avatar, head);
                if (icon == null)
                {
                    Debug.LogError("[AvatarIconCapture] Failed to capture icon");
                    return null;
                }

                // 保存
                bool saved = SaveIconToPNG(icon, savePath);

                // テクスチャを解放
                DestroyImmediate(icon);

                if (saved)
                {
                    Debug.Log($"[AvatarIconCapture] Icon saved to: {savePath}");
                    return savePath;
                }
                else
                {
                    Debug.LogError($"[AvatarIconCapture] Failed to save icon to: {savePath}");
                    return null;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarIconCapture] Error capturing icon: {e.Message}");
                Debug.LogException(e);
                return null;
            }
        }

        /// <summary>
        /// アバターから頭部のTransformを取得
        /// </summary>
        private Transform FindHead(GameObject avatar)
        {
            // Animatorから取得
            var animator = avatar.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                {
                    Debug.Log($"[AvatarIconCapture] Found head via Animator: {head.name}");
                    return head;
                }
            }

            // UniHumanoid.Humanoidから取得
            var humanoid = avatar.GetComponent<UniHumanoid.Humanoid>();
            if (humanoid != null && humanoid.Head != null)
            {
                Debug.Log($"[AvatarIconCapture] Found head via Humanoid: {humanoid.Head.name}");
                return humanoid.Head;
            }

            // 名前で検索
            string[] headNames = { "Head", "head", "頭", "J_Bip_C_Head", "Bip01_Head", "Bip001_Head" };
            foreach (var name in headNames)
            {
                Transform found = FindChildByName(avatar.transform, name);
                if (found != null)
                {
                    Debug.Log($"[AvatarIconCapture] Found head by name: {found.name}");
                    return found;
                }
            }

            Debug.LogWarning("[AvatarIconCapture] Could not find head bone");
            return null;
        }

        /// <summary>
        /// 名前で子オブジェクトを再帰検索
        /// </summary>
        private Transform FindChildByName(Transform parent, string name)
        {
            if (parent.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return parent;

            foreach (Transform child in parent)
            {
                Transform found = FindChildByName(child, name);
                if (found != null) return found;
            }

            return null;
        }

        /// <summary>
        /// 頭部のアイコンを撮影
        /// </summary>
        private Texture2D CaptureHeadIcon(GameObject avatar, Transform head)
        {
            // 撮影用のRenderTextureを作成
            RenderTexture renderTexture = new RenderTexture(ICON_SIZE, ICON_SIZE, 24, RenderTextureFormat.ARGB32);
            renderTexture.antiAliasing = 4;

            // 撮影用カメラを作成
            GameObject cameraObj = new GameObject("IconCaptureCamera");
            Camera captureCamera = cameraObj.AddComponent<Camera>();

            try
            {
                // カメラ設定
                captureCamera.targetTexture = renderTexture;
                captureCamera.clearFlags = CameraClearFlags.SolidColor;
                captureCamera.backgroundColor = new Color(0, 0, 0, 0); // 透明背景
                captureCamera.fieldOfView = CAMERA_FOV;
                captureCamera.nearClipPlane = 0.01f;
                captureCamera.farClipPlane = 10f;
                captureCamera.cullingMask = ~0; // 全てのレイヤーを撮影

                // 顔の大きさを推定（頭から首への距離を参考）
                float headSize = EstimateHeadSize(avatar, head);
                float distance = Mathf.Max(headSize * CAMERA_DISTANCE_MULTIPLIER, MIN_CAMERA_DISTANCE);

                Debug.Log($"[AvatarIconCapture] HeadSize: {headSize:F3}, Distance: {distance:F3}");

                // カメラを頭の正面に配置
                Vector3 headPosition = head.position;
                Vector3 avatarForward = avatar.transform.forward;

                // 顔の中心を狙う（頭ボーンより上を見る）
                Vector3 targetPosition = headPosition + Vector3.up * (headSize * 0.4f);

                // カメラ位置を計算（アバターの正面にカメラを配置）
                cameraObj.transform.position = targetPosition + avatarForward * distance;
                cameraObj.transform.LookAt(targetPosition);

                Debug.Log($"[AvatarIconCapture] Head: {headPosition}, Target: {targetPosition}, Camera: {cameraObj.transform.position}");

                Debug.Log($"[AvatarIconCapture] Camera setup - Distance: {distance:F2}, HeadSize: {headSize:F2}");
                Debug.Log($"[AvatarIconCapture] Head position: {headPosition}, Camera position: {cameraObj.transform.position}");

                // レンダリング
                captureCamera.Render();

                // RenderTextureからTexture2Dに変換
                Texture2D texture = new Texture2D(ICON_SIZE, ICON_SIZE, TextureFormat.RGBA32, false);
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, ICON_SIZE, ICON_SIZE), 0, 0);
                texture.Apply();
                RenderTexture.active = null;

                return texture;
            }
            finally
            {
                // クリーンアップ
                DestroyImmediate(cameraObj);
                renderTexture.Release();
                DestroyImmediate(renderTexture);
            }
        }

        /// <summary>
        /// 頭の大きさを推定
        /// </summary>
        private float EstimateHeadSize(GameObject avatar, Transform head)
        {
            // Animatorから首を取得して頭との距離を測る
            var animator = avatar.GetComponent<Animator>();
            if (animator != null && animator.avatar != null && animator.avatar.isHuman)
            {
                Transform neck = animator.GetBoneTransform(HumanBodyBones.Neck);
                if (neck != null && head != null)
                {
                    float neckToHead = Vector3.Distance(neck.position, head.position);
                    // 頭の大きさは首から頭の約2倍と推定
                    return neckToHead * 2f;
                }
            }

            // SkinnedMeshRendererのバウンドから推定
            var renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            if (renderers.Length > 0)
            {
                float totalHeight = 0;
                foreach (var renderer in renderers)
                {
                    totalHeight = Mathf.Max(totalHeight, renderer.bounds.size.y);
                }
                // 人体の頭は全身の約1/7.5
                return totalHeight / 7.5f;
            }

            // デフォルト値（一般的な人間の頭のサイズ）
            return 0.25f;
        }

        /// <summary>
        /// Texture2DをPNGファイルとして保存
        /// </summary>
        private bool SaveIconToPNG(Texture2D texture, string path)
        {
            try
            {
                // ディレクトリがなければ作成
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // PNG形式でエンコード
                byte[] pngData = texture.EncodeToPNG();
                if (pngData == null || pngData.Length == 0)
                {
                    Debug.LogError("[AvatarIconCapture] Failed to encode texture to PNG");
                    return false;
                }

                // ファイルに保存
                File.WriteAllBytes(path, pngData);

                Debug.Log($"[AvatarIconCapture] Saved PNG ({pngData.Length} bytes) to: {path}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarIconCapture] Error saving PNG: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 保存されたアイコンをSprite として読み込む
        /// </summary>
        public static Sprite LoadIconAsSprite(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Debug.LogWarning($"[AvatarIconCapture] Icon file not found: {path}");
                return null;
            }

            try
            {
                byte[] pngData = File.ReadAllBytes(path);
                Texture2D texture = new Texture2D(ICON_SIZE, ICON_SIZE, TextureFormat.RGBA32, false);

                if (!texture.LoadImage(pngData))
                {
                    Debug.LogError($"[AvatarIconCapture] Failed to load image from: {path}");
                    return null;
                }

                Sprite sprite = Sprite.Create(
                    texture,
                    new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f)
                );

                Debug.Log($"[AvatarIconCapture] Loaded icon sprite from: {path}");
                return sprite;
            }
            catch (Exception e)
            {
                Debug.LogError($"[AvatarIconCapture] Error loading icon: {e.Message}");
                return null;
            }
        }
    }
}
