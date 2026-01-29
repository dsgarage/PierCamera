using UnityEngine;

namespace AICam.UI
{
    /// <summary>
    /// スロットのファイルタイプ
    /// </summary>
    public enum SlotFileType
    {
        None,
        VRM,
        FBX
    }

    /// <summary>
    /// スロットデータ（ファイルパス、サムネイル、ロード済みアバターを管理）
    /// </summary>
    public class SlotData
    {
        public string filePath;
        public SlotFileType fileType;
        public Texture2D thumbnail;
        public GameObject loadedAvatar;
        public bool IsConfigured => !string.IsNullOrEmpty(filePath);
    }
}
