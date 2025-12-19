using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Cysharp.Threading.Tasks;

namespace AICam.FBXLoader
{
    /// <summary>
    /// Issue #72: アバター操作キューシステム
    /// ロード・リスポーン等の非同期操作を直列実行し、競合状態を防止
    /// </summary>
    public class AvatarOperationQueue : MonoBehaviour
    {
        public static AvatarOperationQueue Instance { get; private set; }

        #region Enums & Classes

        /// <summary>
        /// 操作タイプ
        /// </summary>
        public enum OperationType
        {
            Load,       // スロットからアバターをロード
            Respawn,    // 現在のアバターをリスポーン（初期位置に戻す）
            Unload,     // アバターをアンロード
            Clear       // 全アバターをクリア
        }

        /// <summary>
        /// 操作の優先度
        /// </summary>
        public enum OperationPriority
        {
            Normal = 0,
            High = 1,       // ユーザー操作（即座に実行したい）
            Critical = 2    // システム操作（キャンセル不可）
        }

        /// <summary>
        /// 操作クラス
        /// </summary>
        public class Operation
        {
            public readonly string Id;
            public readonly OperationType Type;
            public readonly int SlotIndex;
            public readonly OperationPriority Priority;
            public readonly DateTime CreatedAt;
            public readonly UniTaskCompletionSource<OperationResult> CompletionSource;
            public readonly CancellationTokenSource CancellationTokenSource;

            public bool IsCancelled => CancellationTokenSource.IsCancellationRequested;
            public CancellationToken CancellationToken => CancellationTokenSource.Token;

            public Operation(OperationType type, int slotIndex, OperationPriority priority = OperationPriority.Normal)
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 8);
                Type = type;
                SlotIndex = slotIndex;
                Priority = priority;
                CreatedAt = DateTime.Now;
                CompletionSource = new UniTaskCompletionSource<OperationResult>();
                CancellationTokenSource = new CancellationTokenSource();
            }

            public void Cancel()
            {
                if (!CancellationTokenSource.IsCancellationRequested)
                {
                    CancellationTokenSource.Cancel();
                }
            }

            public override string ToString()
            {
                return $"[{Id}] {Type} Slot:{SlotIndex} Priority:{Priority}";
            }
        }

        /// <summary>
        /// 操作結果
        /// </summary>
        public class OperationResult
        {
            public bool Success { get; set; }
            public bool WasCancelled { get; set; }
            public string ErrorMessage { get; set; }
            public GameObject Avatar { get; set; }
            public int SlotIndex { get; set; }

            public static OperationResult Succeeded(int slotIndex, GameObject avatar = null)
            {
                return new OperationResult
                {
                    Success = true,
                    WasCancelled = false,
                    SlotIndex = slotIndex,
                    Avatar = avatar
                };
            }

            public static OperationResult Failed(int slotIndex, string error)
            {
                return new OperationResult
                {
                    Success = false,
                    WasCancelled = false,
                    SlotIndex = slotIndex,
                    ErrorMessage = error
                };
            }

            public static OperationResult Cancelled(int slotIndex)
            {
                return new OperationResult
                {
                    Success = false,
                    WasCancelled = true,
                    SlotIndex = slotIndex,
                    ErrorMessage = "Operation was cancelled"
                };
            }
        }

        #endregion

        #region Events

        /// <summary>
        /// 操作開始時
        /// </summary>
        public event Action<Operation> OnOperationStarted;

        /// <summary>
        /// 操作完了時（成功・失敗問わず）
        /// </summary>
        public event Action<Operation, OperationResult> OnOperationCompleted;

        /// <summary>
        /// 操作キャンセル時
        /// </summary>
        public event Action<Operation> OnOperationCancelled;

        /// <summary>
        /// キューに追加時
        /// </summary>
        public event Action<Operation> OnOperationEnqueued;

        /// <summary>
        /// 進捗更新時
        /// </summary>
        public event Action<Operation, float> OnProgressUpdated;

        #endregion

        #region Fields

        [Header("Settings")]
        [SerializeField] private int maxQueueSize = 10;
        [SerializeField] private bool showDebugLog = true;

        // 操作キュー
        private Queue<Operation> operationQueue = new Queue<Operation>();

        // 現在実行中の操作
        private Operation currentOperation;

        // 処理中フラグ
        private bool isProcessing;

        // 操作実行デリゲート（AvatarSlotManagerから設定）
        private Func<Operation, UniTask<OperationResult>> executeOperation;

        #endregion

        #region Properties

        /// <summary>
        /// 処理中かどうか
        /// </summary>
        public bool IsProcessing => isProcessing;

        /// <summary>
        /// キュー内の操作数
        /// </summary>
        public int QueueCount => operationQueue.Count;

        /// <summary>
        /// 現在の操作
        /// </summary>
        public Operation CurrentOperation => currentOperation;

        /// <summary>
        /// キューが空かどうか
        /// </summary>
        public bool IsEmpty => operationQueue.Count == 0 && currentOperation == null;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("[AvatarOperationQueue] Duplicate instance, destroying...");
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void OnDestroy()
        {
            CancelAll();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        #endregion

        #region Public API

        /// <summary>
        /// 操作実行デリゲートを設定
        /// </summary>
        public void SetExecutor(Func<Operation, UniTask<OperationResult>> executor)
        {
            executeOperation = executor;
        }

        /// <summary>
        /// ロード操作をキューに追加
        /// </summary>
        /// <param name="slotIndex">スロットインデックス</param>
        /// <param name="priority">優先度（Highの場合、現在の操作をキャンセル）</param>
        /// <returns>操作結果</returns>
        public UniTask<OperationResult> EnqueueLoad(int slotIndex, OperationPriority priority = OperationPriority.Normal)
        {
            return Enqueue(new Operation(OperationType.Load, slotIndex, priority));
        }

        /// <summary>
        /// リスポーン操作をキューに追加
        /// </summary>
        public UniTask<OperationResult> EnqueueRespawn(int slotIndex)
        {
            return Enqueue(new Operation(OperationType.Respawn, slotIndex, OperationPriority.Normal));
        }

        /// <summary>
        /// アンロード操作をキューに追加
        /// </summary>
        public UniTask<OperationResult> EnqueueUnload(int slotIndex)
        {
            return Enqueue(new Operation(OperationType.Unload, slotIndex, OperationPriority.Normal));
        }

        /// <summary>
        /// 全クリア操作をキューに追加
        /// </summary>
        public UniTask<OperationResult> EnqueueClear()
        {
            return Enqueue(new Operation(OperationType.Clear, -1, OperationPriority.Critical));
        }

        /// <summary>
        /// 操作をキューに追加
        /// </summary>
        public UniTask<OperationResult> Enqueue(Operation operation)
        {
            if (operation == null)
            {
                return UniTask.FromResult(OperationResult.Failed(-1, "Operation is null"));
            }

            // キューサイズチェック
            if (operationQueue.Count >= maxQueueSize)
            {
                Log($"Queue is full, rejecting operation: {operation}");
                return UniTask.FromResult(OperationResult.Failed(operation.SlotIndex, "Queue is full"));
            }

            // 重複チェック（同じスロットのLoad操作）
            if (operation.Type == OperationType.Load)
            {
                // 現在実行中の操作が同じスロットなら無視
                if (currentOperation != null &&
                    currentOperation.Type == OperationType.Load &&
                    currentOperation.SlotIndex == operation.SlotIndex &&
                    !currentOperation.IsCancelled)
                {
                    Log($"Same slot load already in progress, ignoring: {operation}");
                    return currentOperation.CompletionSource.Task;
                }

                // キュー内に同じスロットのLoad操作があれば無視
                foreach (var queued in operationQueue)
                {
                    if (queued.Type == OperationType.Load && queued.SlotIndex == operation.SlotIndex)
                    {
                        Log($"Same slot load already queued, ignoring: {operation}");
                        return queued.CompletionSource.Task;
                    }
                }
            }

            // 優先度が高い場合、現在の操作をキャンセル
            if (operation.Priority >= OperationPriority.High && currentOperation != null)
            {
                if (currentOperation.Priority < OperationPriority.Critical)
                {
                    Log($"High priority operation, cancelling current: {currentOperation}");
                    CancelCurrent();
                }
            }

            // キューに追加
            operationQueue.Enqueue(operation);
            Log($"Enqueued: {operation}, QueueCount: {operationQueue.Count}");
            OnOperationEnqueued?.Invoke(operation);

            // 処理開始
            ProcessQueue().Forget();

            return operation.CompletionSource.Task;
        }

        /// <summary>
        /// 現在の操作をキャンセル
        /// </summary>
        public void CancelCurrent()
        {
            if (currentOperation != null && !currentOperation.IsCancelled)
            {
                Log($"Cancelling current operation: {currentOperation}");
                currentOperation.Cancel();
                OnOperationCancelled?.Invoke(currentOperation);
            }
        }

        /// <summary>
        /// 全ての操作をキャンセル
        /// </summary>
        public void CancelAll()
        {
            Log("Cancelling all operations");

            // 現在の操作をキャンセル
            CancelCurrent();

            // キュー内の全操作をキャンセル
            while (operationQueue.Count > 0)
            {
                var op = operationQueue.Dequeue();
                op.Cancel();
                op.CompletionSource.TrySetResult(OperationResult.Cancelled(op.SlotIndex));
                OnOperationCancelled?.Invoke(op);
            }
        }

        /// <summary>
        /// 特定スロットの操作をキャンセル
        /// </summary>
        public void CancelSlot(int slotIndex)
        {
            // 現在の操作が対象スロットならキャンセル
            if (currentOperation != null && currentOperation.SlotIndex == slotIndex)
            {
                CancelCurrent();
            }

            // キューから対象スロットの操作を除去
            var remaining = new Queue<Operation>();
            while (operationQueue.Count > 0)
            {
                var op = operationQueue.Dequeue();
                if (op.SlotIndex == slotIndex)
                {
                    op.Cancel();
                    op.CompletionSource.TrySetResult(OperationResult.Cancelled(op.SlotIndex));
                    OnOperationCancelled?.Invoke(op);
                }
                else
                {
                    remaining.Enqueue(op);
                }
            }
            operationQueue = remaining;
        }

        /// <summary>
        /// 進捗を報告
        /// </summary>
        public void ReportProgress(float progress)
        {
            if (currentOperation != null)
            {
                OnProgressUpdated?.Invoke(currentOperation, progress);
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// キューを処理
        /// </summary>
        private async UniTask ProcessQueue()
        {
            // 既に処理中なら何もしない
            if (isProcessing) return;

            isProcessing = true;

            try
            {
                while (operationQueue.Count > 0)
                {
                    // 次の操作を取得
                    currentOperation = operationQueue.Dequeue();

                    // キャンセル済みならスキップ
                    if (currentOperation.IsCancelled)
                    {
                        Log($"Operation already cancelled, skipping: {currentOperation}");
                        currentOperation.CompletionSource.TrySetResult(
                            OperationResult.Cancelled(currentOperation.SlotIndex));
                        currentOperation = null;
                        continue;
                    }

                    Log($"Starting operation: {currentOperation}");
                    OnOperationStarted?.Invoke(currentOperation);

                    OperationResult result;

                    try
                    {
                        // 操作実行
                        if (executeOperation != null)
                        {
                            result = await executeOperation(currentOperation);
                        }
                        else
                        {
                            Log("No executor set, operation failed");
                            result = OperationResult.Failed(currentOperation.SlotIndex, "No executor configured");
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        Log($"Operation cancelled: {currentOperation}");
                        result = OperationResult.Cancelled(currentOperation.SlotIndex);
                    }
                    catch (Exception e)
                    {
                        Log($"Operation failed with exception: {e.Message}");
                        result = OperationResult.Failed(currentOperation.SlotIndex, e.Message);
                    }

                    // 結果を設定
                    currentOperation.CompletionSource.TrySetResult(result);
                    Log($"Operation completed: {currentOperation}, Success: {result.Success}");
                    OnOperationCompleted?.Invoke(currentOperation, result);

                    currentOperation = null;

                    // 次の操作前に1フレーム待機（UI更新のため）
                    await UniTask.Yield();
                }
            }
            finally
            {
                isProcessing = false;
                currentOperation = null;
            }
        }

        private void Log(string message)
        {
            if (showDebugLog)
            {
                Debug.Log($"[AvatarOperationQueue] {message}");
            }
        }

        #endregion

        #region Debug

        /// <summary>
        /// デバッグ情報を取得
        /// </summary>
        public string GetDebugInfo()
        {
            var info = "=== AvatarOperationQueue ===\n";
            info += $"IsProcessing: {isProcessing}\n";
            info += $"QueueCount: {operationQueue.Count}\n";

            if (currentOperation != null)
            {
                info += $"Current: {currentOperation}\n";
            }

            info += "Queue:\n";
            int i = 0;
            foreach (var op in operationQueue)
            {
                info += $"  [{i}] {op}\n";
                i++;
            }

            return info;
        }

        #endregion
    }
}
