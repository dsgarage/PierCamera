using UnityEngine;
using UnityEngine.UIElements;
using System;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アイコンプレビューパネルのスタブクラス
    /// </summary>
    public class IconPreviewPanel : MonoBehaviour
    {
        public event Action OnConfirm;
        public event Action OnRetake;

        public void Show(Texture2D texture)
        {
            // スタブ実装
        }

        public void Hide()
        {
            // スタブ実装
        }

        public void ShowPreviewFromFile(string filePath)
        {
            Debug.Log($"[IconPreviewPanel] ShowPreviewFromFile: {filePath}");
        }

        public void ShowPreviewFromFile(string filePath, Action onConfirmCallback, Action onRetakeCallback = null)
        {
            Debug.Log($"[IconPreviewPanel] ShowPreviewFromFile with callbacks: {filePath}");
            // コールバックを保存して後で使用
        }
    }
}
