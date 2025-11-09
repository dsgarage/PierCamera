using System;
using System.Collections.Generic;
using UnityEngine;

namespace AICam.UI
{
    /// <summary>
    /// アバタースロットのデータを保存するためのクラス
    /// </summary>
    [Serializable]
    public class AvatarSlotData
    {
        public string slotId;
        public string filePath;
        public string thumbnailBase64;
        public DateTime lastUsed;

        public AvatarSlotData(string slotId, string filePath, string thumbnailBase64 = null)
        {
            this.slotId = slotId;
            this.filePath = filePath;
            this.thumbnailBase64 = thumbnailBase64;
            this.lastUsed = DateTime.Now;
        }
    }

    /// <summary>
    /// アバタースロットのコレクション（JSON保存用）
    /// </summary>
    [Serializable]
    public class AvatarSlotDataCollection
    {
        public List<AvatarSlotData> slots = new List<AvatarSlotData>();
    }

    /// <summary>
    /// アバタースロットの永続化マネージャー
    /// </summary>
    public static class AvatarSlotPersistence
    {
        private const string SaveKey = "AvatarSlots";

        /// <summary>
        /// スロットデータを保存
        /// </summary>
        public static void SaveSlot(AvatarSlotData slotData)
        {
            var collection = LoadAllSlots();

            // 既存のスロットを更新または追加
            var existingIndex = collection.slots.FindIndex(s => s.slotId == slotData.slotId);
            if (existingIndex >= 0)
            {
                collection.slots[existingIndex] = slotData;
            }
            else
            {
                collection.slots.Add(slotData);
            }

            SaveAllSlots(collection);
        }

        /// <summary>
        /// スロットデータを取得
        /// </summary>
        public static AvatarSlotData LoadSlot(string slotId)
        {
            var collection = LoadAllSlots();
            return collection.slots.Find(s => s.slotId == slotId);
        }

        /// <summary>
        /// すべてのスロットデータを取得
        /// </summary>
        public static AvatarSlotDataCollection LoadAllSlots()
        {
            var json = PlayerPrefs.GetString(SaveKey, "");
            if (string.IsNullOrEmpty(json))
            {
                return new AvatarSlotDataCollection();
            }

            try
            {
                return JsonUtility.FromJson<AvatarSlotDataCollection>(json);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to load avatar slots: {e.Message}");
                return new AvatarSlotDataCollection();
            }
        }

        /// <summary>
        /// すべてのスロットデータを保存
        /// </summary>
        public static void SaveAllSlots(AvatarSlotDataCollection collection)
        {
            try
            {
                var json = JsonUtility.ToJson(collection, true);
                PlayerPrefs.SetString(SaveKey, json);
                PlayerPrefs.Save();
                Debug.Log($"✅ Saved {collection.slots.Count} avatar slots");
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to save avatar slots: {e.Message}");
            }
        }

        /// <summary>
        /// スロットデータを削除
        /// </summary>
        public static void DeleteSlot(string slotId)
        {
            var collection = LoadAllSlots();
            collection.slots.RemoveAll(s => s.slotId == slotId);
            SaveAllSlots(collection);
        }

        /// <summary>
        /// すべてのスロットデータを削除
        /// </summary>
        public static void ClearAllSlots()
        {
            PlayerPrefs.DeleteKey(SaveKey);
            PlayerPrefs.Save();
            Debug.Log("✅ Cleared all avatar slots");
        }
    }
}
