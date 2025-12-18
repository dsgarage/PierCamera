using UnityEngine;
using System;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバターアイコンキャプチャのスタブクラス
    /// </summary>
    public class AvatarIconCapture : MonoBehaviour
    {
        public static AvatarIconCapture Instance { get; private set; }

        public event Action<Texture2D> OnCaptureComplete;
        public event Action<string> OnCaptureFailed;

        private void Awake()
        {
            Instance = this;
        }

        public UniTask<Texture2D> CaptureIconAsync(GameObject avatar, int width = 256, int height = 256)
        {
            var texture = new Texture2D(width, height);
            return UniTask.FromResult(texture);
        }

        public UniTask<Texture2D> CaptureAsTextureAsync(GameObject avatar)
        {
            var texture = new Texture2D(256, 256);
            return UniTask.FromResult(texture);
        }

        public UniTask<string> CaptureAndSaveAsync(GameObject avatar, string savePath)
        {
            // スタブ実装 - パスをそのまま返す
            Debug.Log($"[AvatarIconCapture] CaptureAndSaveAsync: {savePath}");
            return UniTask.FromResult(savePath);
        }

        public void CaptureIcon(GameObject avatar, Action<Texture2D> onComplete)
        {
            var texture = new Texture2D(256, 256);
            onComplete?.Invoke(texture);
        }

        public static void SaveIcon(Texture2D texture, string path)
        {
            Debug.Log($"[AvatarIconCapture] Saving icon to: {path}");
        }

        public static Texture2D LoadIcon(string path)
        {
            return null;
        }
    }
}
