using System;

namespace AICam.AvatarCache
{
    /// <summary>
    /// アバタースロットデータ
    /// slots.jsonにシリアライズされる
    /// </summary>
    [Serializable]
    public class SlotsData
    {
        public int version;
        public int activeSlotIndex;
        public SlotEntry[] slots;
    }

    /// <summary>
    /// 個別スロットエントリ
    /// </summary>
    [Serializable]
    public class SlotEntry
    {
        public int slotIndex;
        public string cacheId;
        public string displayName;
        public string iconPath;
        public string lastUsedAt;
    }
}
