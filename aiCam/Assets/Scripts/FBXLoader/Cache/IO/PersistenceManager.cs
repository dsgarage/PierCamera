using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace AICam.AvatarCache.IO
{
    /// <summary>
    /// 永続化マネージャー
    /// スロットデータの保存・ロードを担当
    /// </summary>
    public class PersistenceManager
    {
        private readonly string _slotsFilePath;
        private readonly List<Action<bool>> _pauseCallbacks = new List<Action<bool>>();
        private RecoveryStats _recoveryStats;
        private float _autoSaveInterval;
        private bool _isAutoSaveActive;
        private float _lastAutoSaveTime;

        public PersistenceManager(string slotsFilePath)
        {
            _slotsFilePath = slotsFilePath;
        }

        /// <summary>
        /// スロットデータを保存
        /// </summary>
        public void SaveSlots(SlotsData data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var json = JsonUtility.ToJson(data, true);
            SaveAtomic(_slotsFilePath, json);
        }

        /// <summary>
        /// スロットデータをロード
        /// </summary>
        public SlotsData LoadSlots()
        {
            if (!File.Exists(_slotsFilePath))
            {
                // ファイルがない場合はデフォルト値を返す
                return CreateDefaultSlotsData();
            }

            try
            {
                var json = File.ReadAllText(_slotsFilePath);
                var data = JsonUtility.FromJson<SlotsData>(json);

                if (data == null)
                {
                    Debug.LogWarning("[PersistenceManager] Failed to parse slots data, returning default");
                    return CreateDefaultSlotsData();
                }

                return data;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PersistenceManager] Failed to load slots: {e.Message}");

                // 破損ファイルの復旧を試みる
                if (TryRecoverCorruptedFile(_slotsFilePath, out var recovered))
                {
                    try
                    {
                        return JsonUtility.FromJson<SlotsData>(recovered);
                    }
                    catch
                    {
                        // 復旧も失敗
                    }
                }

                return CreateDefaultSlotsData();
            }
        }

        /// <summary>
        /// アトミック保存（一時ファイル経由）
        /// </summary>
        public void SaveAtomic(string filePath, string content)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentNullException(nameof(filePath));

            // ディレクトリが存在しない場合は作成
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = filePath + ".tmp";

            try
            {
                // 一時ファイルに書き込み
                File.WriteAllText(tempPath, content);

                // 既存ファイルがあれば削除
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // 一時ファイルをリネーム
                File.Move(tempPath, filePath);
            }
            catch (Exception e)
            {
                // 一時ファイルをクリーンアップ
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { }
                }

                throw new IOException($"Failed to save file atomically: {e.Message}", e);
            }
        }

        /// <summary>
        /// 破損ファイルのバックアップと復旧
        /// </summary>
        public bool TryRecoverCorruptedFile(string filePath, out string recoveredContent)
        {
            recoveredContent = null;

            if (!File.Exists(filePath))
                return false;

            try
            {
                // 破損ファイルをバックアップ
                var backupPath = filePath + ".corrupted." + DateTime.Now.Ticks;
                File.Copy(filePath, backupPath, true);
                Debug.Log($"[PersistenceManager] Corrupted file backed up to: {backupPath}");

                // バックアップファイルがあれば復旧を試みる
                var backupDir = Path.GetDirectoryName(filePath);
                var fileName = Path.GetFileName(filePath);
                var backupPattern = fileName + ".backup*";

                if (!string.IsNullOrEmpty(backupDir))
                {
                    var backupFiles = Directory.GetFiles(backupDir, backupPattern);
                    if (backupFiles.Length > 0)
                    {
                        // 最新のバックアップを使用
                        Array.Sort(backupFiles);
                        var latestBackup = backupFiles[backupFiles.Length - 1];
                        recoveredContent = File.ReadAllText(latestBackup);
                        Debug.Log($"[PersistenceManager] Recovered from backup: {latestBackup}");
                        RecordRecoveryAttempt(true);
                        return true;
                    }
                }

                RecordRecoveryAttempt(false);
                return false;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PersistenceManager] Failed to recover corrupted file: {e.Message}");
                RecordRecoveryAttempt(false);
                return false;
            }
        }

        /// <summary>
        /// デフォルトのスロットデータを作成
        /// </summary>
        private static SlotsData CreateDefaultSlotsData()
        {
            return new SlotsData
            {
                version = 1,
                activeSlotIndex = -1,
                slots = Array.Empty<SlotEntry>()
            };
        }

        /// <summary>
        /// アプリケーション一時停止時のコールバックを登録
        /// </summary>
        public void RegisterPauseCallback(Action<bool> callback)
        {
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            _pauseCallbacks.Add(callback);
            Debug.Log($"[PersistenceManager] Pause callback registered. Total callbacks: {_pauseCallbacks.Count}");
        }

        /// <summary>
        /// 登録されたコールバックを解除
        /// </summary>
        public void UnregisterPauseCallback(Action<bool> callback)
        {
            if (callback != null)
            {
                _pauseCallbacks.Remove(callback);
            }
        }

        /// <summary>
        /// 全ての一時停止コールバックを呼び出す
        /// </summary>
        public void InvokePauseCallbacks(bool paused)
        {
            foreach (var callback in _pauseCallbacks)
            {
                try
                {
                    callback?.Invoke(paused);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PersistenceManager] Pause callback error: {e.Message}");
                }
            }

            // 一時停止時にデータを保存
            if (paused)
            {
                try
                {
                    var data = LoadSlots();
                    SaveSlots(data);
                    Debug.Log("[PersistenceManager] Data saved on pause");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PersistenceManager] Failed to save on pause: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 自動保存を開始
        /// </summary>
        public void StartAutoSave(float intervalSeconds)
        {
            if (intervalSeconds <= 0)
                throw new ArgumentException("Interval must be positive", nameof(intervalSeconds));

            _autoSaveInterval = intervalSeconds;
            _isAutoSaveActive = true;
            _lastAutoSaveTime = Time.realtimeSinceStartup;

            Debug.Log($"[PersistenceManager] Auto-save started with interval: {intervalSeconds}s");
        }

        /// <summary>
        /// 自動保存を停止
        /// </summary>
        public void StopAutoSave()
        {
            _isAutoSaveActive = false;
            Debug.Log("[PersistenceManager] Auto-save stopped");
        }

        /// <summary>
        /// 自動保存が有効かどうか
        /// </summary>
        public bool IsAutoSaveActive => _isAutoSaveActive;

        /// <summary>
        /// 自動保存の間隔（秒）
        /// </summary>
        public float AutoSaveInterval => _autoSaveInterval;

        /// <summary>
        /// 自動保存の更新処理（MonoBehaviourのUpdateから呼び出す）
        /// </summary>
        public void UpdateAutoSave()
        {
            if (!_isAutoSaveActive)
                return;

            var currentTime = Time.realtimeSinceStartup;
            if (currentTime - _lastAutoSaveTime >= _autoSaveInterval)
            {
                _lastAutoSaveTime = currentTime;

                try
                {
                    var data = LoadSlots();
                    SaveSlots(data);
                    Debug.Log("[PersistenceManager] Auto-save completed");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PersistenceManager] Auto-save failed: {e.Message}");
                }
            }
        }

        /// <summary>
        /// エラー復旧統計を取得
        /// </summary>
        public RecoveryStats GetRecoveryStats()
        {
            return _recoveryStats;
        }

        /// <summary>
        /// 復旧統計を更新（内部用）
        /// </summary>
        internal void RecordRecoveryAttempt(bool success)
        {
            _recoveryStats.totalRecoveryAttempts++;
            if (success)
            {
                _recoveryStats.successfulRecoveries++;
            }
            else
            {
                _recoveryStats.failedRecoveries++;
            }
            _recoveryStats.lastRecoveryTime = DateTime.UtcNow.ToString("o");
        }

        /// <summary>
        /// 復旧統計をリセット
        /// </summary>
        public void ResetRecoveryStats()
        {
            _recoveryStats = new RecoveryStats();
        }
    }

    /// <summary>
    /// 復旧統計情報
    /// </summary>
    public struct RecoveryStats
    {
        public int totalRecoveryAttempts;
        public int successfulRecoveries;
        public int failedRecoveries;
        public string lastRecoveryTime;
    }
}
