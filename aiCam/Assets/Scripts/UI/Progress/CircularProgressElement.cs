using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// 円形プログレス要素のスタブクラス
    /// </summary>
    public class CircularProgressElement : VisualElement
    {
        public float Progress { get; set; } = 0f;

        public CircularProgressElement()
        {
            style.display = DisplayStyle.None; // 非表示
        }

        public void SetProgress(float value)
        {
            Progress = value;
        }

        public void Show()
        {
            style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
        }
    }
}
