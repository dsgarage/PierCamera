using UnityEngine;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバター操作キューのスタブクラス
    /// </summary>
    public class AvatarOperationQueue : MonoBehaviour
    {
        public bool IsProcessing { get; private set; } = false;

        public event Action<Operation> OnOperationStarted;
        public event Action<int, float> OnProgressUpdated;
        public event Action<OperationResult> OnOperationCompleted;
        public event Action<OperationResult> OnOperationCancelled;

        public enum OperationPriority
        {
            Low,
            Normal,
            High
        }

        public enum OperationType
        {
            Load,
            Respawn,
            Unload,
            Clear
        }

        public class Operation
        {
            public int SlotIndex { get; set; }
            public OperationType Type { get; set; }
            public OperationPriority Priority { get; set; }
            public CancellationToken CancellationToken { get; set; }
        }

        public class OperationResult
        {
            public bool Success { get; set; }
            public int SlotIndex { get; set; }
            public GameObject Avatar { get; set; }
            public string ErrorMessage { get; set; }
            public bool WasCancelled { get; set; }

            public static OperationResult Succeeded(int slotIndex, GameObject avatar = null)
            {
                return new OperationResult { Success = true, SlotIndex = slotIndex, Avatar = avatar };
            }

            public static OperationResult Failed(int slotIndex, string error)
            {
                return new OperationResult { Success = false, SlotIndex = slotIndex, ErrorMessage = error };
            }

            public static OperationResult Cancelled(int slotIndex)
            {
                return new OperationResult { Success = false, SlotIndex = slotIndex, WasCancelled = true };
            }
        }

        private Func<Operation, UniTask<OperationResult>> executor;

        public void SetExecutor(Func<Operation, UniTask<OperationResult>> executor)
        {
            this.executor = executor;
        }

        public UniTask<OperationResult> EnqueueLoad(int slotIndex, OperationPriority priority = OperationPriority.Normal)
        {
            return UniTask.FromResult(OperationResult.Succeeded(slotIndex));
        }

        public UniTask EnqueueAsync(Func<UniTask> operation)
        {
            return operation();
        }

        public void ReportProgress(int slotIndex, float progress)
        {
            OnProgressUpdated?.Invoke(slotIndex, progress);
        }

        public void Clear()
        {
            // スタブ実装
        }
    }
}
