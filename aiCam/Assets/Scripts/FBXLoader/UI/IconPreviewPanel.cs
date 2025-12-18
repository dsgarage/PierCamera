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
            // スタブ実装
            Debug.Log($"[IconPreviewPanel] ShowPreviewFromFile: {filePath}");
        }
    }
}
