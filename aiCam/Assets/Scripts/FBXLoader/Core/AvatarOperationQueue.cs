using UnityEngine;
using System;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバター操作キューのスタブクラス
    /// </summary>
    public class AvatarOperationQueue
    {
        public bool IsProcessing { get; private set; } = false;

        public UniTask EnqueueAsync(Func<UniTask> operation)
        {
            return operation();
        }

        public void Clear()
        {
            // スタブ実装
        }
    }
}
