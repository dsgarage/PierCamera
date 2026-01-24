using System;
using Cysharp.Threading.Tasks;

namespace AICam.AvatarCache.IO
{
    /// <summary>
    /// 永続化マネージャー
    /// スロットデータの保存・ロードを担当
    /// </summary>
    public class PersistenceManager
    {
        private readonly string _slotsFilePath;

        public PersistenceManager(string slotsFilePath)
        {
            _slotsFilePath = slotsFilePath;
        }

        /// <summary>
        /// スロットデータを保存
        /// </summary>
        public void SaveSlots(SlotsData data)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// スロットデータをロード
        /// </summary>
        public SlotsData LoadSlots()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// アトミック保存（一時ファイル経由）
        /// </summary>
        public void SaveAtomic(string filePath, string content)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 破損ファイルのバックアップと復旧
        /// </summary>
        public bool TryRecoverCorruptedFile(string filePath, out string recoveredContent)
        {
            throw new NotImplementedException();
        }
    }
}
