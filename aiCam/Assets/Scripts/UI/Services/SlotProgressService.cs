using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AICam.UI
{
    /// <summary>
    /// スロットごとの円形プログレス UI を管理するサービス。
    /// </summary>
    public class SlotProgressService : ISlotProgressUI
    {
        private readonly VisualElement root;
        private readonly Dictionary<Button, CircularProgressElement> slotProgressMap
            = new Dictionary<Button, CircularProgressElement>();

        public SlotProgressService(VisualElement root)
        {
            this.root = root;
        }

        public void StartSlotLoading(Button slotButton)
        {
            var progress = CreateProgressForSlot(slotButton);
            if (progress == null) return;

            UpdateProgressPosition(slotButton);

            progress.Progress = 0.01f;
            progress.style.display = DisplayStyle.Flex;

            slotButton.style.opacity = 0.4f;
        }

        public void UpdateSlotProgress(Button slotButton, float progress01)
        {
            if (!slotProgressMap.TryGetValue(slotButton, out var progress)) return;
            progress.Progress = progress01;
        }

        public void CompleteSlotLoading(Button slotButton)
        {
            if (!slotProgressMap.TryGetValue(slotButton, out var progress)) return;

            progress.Progress = 1f;
            slotButton.style.opacity = 1f;

            progress.schedule.Execute(() =>
            {
                progress.style.display = DisplayStyle.None;
                progress.Progress = 0f;
            }).StartingIn(300);
        }

        public void CancelSlotLoading(Button slotButton)
        {
            if (!slotProgressMap.TryGetValue(slotButton, out var progress)) return;

            progress.style.display = DisplayStyle.None;
            progress.Progress = 0f;
            slotButton.style.opacity = 1f;
        }

        private CircularProgressElement CreateProgressForSlot(Button slotButton)
        {
            if (slotButton == null || root == null) return null;

            if (slotProgressMap.TryGetValue(slotButton, out var existingProgress))
            {
                return existingProgress;
            }

            var progress = new CircularProgressElement();
            progress.name = $"progress_{slotButton.name}";
            progress.RingWidth = 3f;
            progress.ProgressColor = new Color(0.3f, 0.7f, 1f, 1f);
            progress.ShowBackground = false;

            progress.style.position = Position.Absolute;
            progress.style.display = DisplayStyle.None;

            root.Add(progress);
            slotProgressMap[slotButton] = progress;

            return progress;
        }

        private void UpdateProgressPosition(Button slotButton)
        {
            if (!slotProgressMap.TryGetValue(slotButton, out var progress)) return;

            var bounds = slotButton.worldBound;
            float padding = 4f;
            float size = Mathf.Max(bounds.width, bounds.height) + padding * 2;

            progress.style.width = size;
            progress.style.height = size;
            progress.style.left = bounds.x - padding;
            progress.style.top = bounds.y - padding;
        }
    }
}
