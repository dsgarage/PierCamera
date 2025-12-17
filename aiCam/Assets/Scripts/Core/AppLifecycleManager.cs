using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;

namespace AICam.Core
{
    /// <summary>
    /// アプリケーションライフサイクル管理
    /// バックグラウンド移行時にARSession/カメラを適切に停止し、
    /// Watchdog Timeout (0x8BADF00D) を防止する
    ///
    /// Issue #409: バックグラウンドクラッシュの修正
    ///
    /// 問題の根本原因:
    /// - AVCaptureSession.stopRunning() は同期的でブロッキングな呼び出し
    /// - メインスレッドで呼ばれると10秒以上かかる場合がある
    /// - iOSのWatchdogは5秒でアプリを強制終了する
    ///
    /// 対策:
    /// 1. OnApplicationFocusで早期にARSessionを停止（Pauseより先に呼ばれる）
    /// 2. ARCameraManager/ARCameraBackgroundも停止してカメラフィードを解放
    /// 3. 全てのAR関連コンポーネントを無効化
    /// 4. アプリ終了時にも同様の処理を実行
    /// </summary>
    public class AppLifecycleManager : MonoBehaviour
    {
        public static AppLifecycleManager Instance { get; private set; }

        [Header("References (Auto-detected if null)")]
        [SerializeField] private ARSession arSession;
        [SerializeField] private ARCameraManager arCameraManager;
        [SerializeField] private ARCameraBackground arCameraBackground;
        [SerializeField] private Camera mainCamera;

        [Header("Settings")]
        [Tooltip("バックグラウンド移行時にARSessionを停止する")]
        [SerializeField] private bool pauseARSessionOnBackground = true;

        [Tooltip("フォーカス喪失時にも停止する（より早期の停止）")]
        [SerializeField] private bool stopOnFocusLost = true;

        [Header("Debug")]
        [SerializeField] private bool enableDebugLog = true;

        // 状態管理
        private bool isInBackground = false;
        private bool hasFocus = true;
        private bool arSessionWasEnabled = false;
        private bool arCameraManagerWasEnabled = false;
        private bool arCameraBackgroundWasEnabled = false;
        private Coroutine resumeCoroutine;

        // スレッド安全性のためのロック
        private readonly object stateLock = new object();
        private volatile bool isProcessingStateChange = false;

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
                // DontDestroyOnLoadは使用しない - シーンと一緒に破棄される方が安全
                Log("[AppLifecycleManager] Initialized");
            }
            else
            {
                Destroy(gameObject);
                return;
            }
        }

        private void Start()
        {
            // 参照の自動検出
            FindARComponents();

            // アプリの終了イベントを購読
            Application.quitting += OnApplicationQuitting;

            // 低メモリ警告を購読
            Application.lowMemory += OnLowMemory;
        }

        private void FindARComponents()
        {
            if (arSession == null)
            {
                arSession = FindFirstObjectByType<ARSession>(FindObjectsInactive.Include);
                if (arSession != null)
                {
                    Log($"[AppLifecycleManager] ARSession found: {arSession.name}");
                }
            }

            if (arCameraManager == null)
            {
                arCameraManager = FindFirstObjectByType<ARCameraManager>(FindObjectsInactive.Include);
                if (arCameraManager != null)
                {
                    Log($"[AppLifecycleManager] ARCameraManager found: {arCameraManager.name}");
                }
            }

            if (arCameraBackground == null)
            {
                arCameraBackground = FindFirstObjectByType<ARCameraBackground>(FindObjectsInactive.Include);
                if (arCameraBackground != null)
                {
                    Log($"[AppLifecycleManager] ARCameraBackground found: {arCameraBackground.name}");
                }
            }

            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }
        }

        private void OnDestroy()
        {
            Application.quitting -= OnApplicationQuitting;
            Application.lowMemory -= OnLowMemory;

            if (resumeCoroutine != null)
            {
                StopCoroutine(resumeCoroutine);
            }
        }

        /// <summary>
        /// アプリがフォーカスを失った/得た時に呼ばれる
        /// iOS では OnApplicationPause の前に呼ばれることがある
        /// これが最初の防衛ラインとなる
        /// </summary>
        private void OnApplicationFocus(bool focus)
        {
            Log($"[AppLifecycleManager] OnApplicationFocus({focus})");
            hasFocus = focus;

            if (!focus && stopOnFocusLost)
            {
                // フォーカスを失った時点で即座にARを停止
                // これによりOnApplicationPauseより早くリソースを解放できる
                StopAllARSubsystems();
            }
        }

        /// <summary>
        /// アプリがバックグラウンドに移行/復帰した時に呼ばれる
        /// </summary>
        private void OnApplicationPause(bool pauseStatus)
        {
            Log($"[AppLifecycleManager] OnApplicationPause({pauseStatus})");

            if (pauseStatus)
            {
                // バックグラウンドに移行
                HandleEnterBackground();
            }
            else
            {
                // フォアグラウンドに復帰
                HandleEnterForeground();
            }
        }

        /// <summary>
        /// バックグラウンド移行時の処理
        /// 重要: この処理は素早く完了する必要がある（5秒以内）
        /// </summary>
        private void HandleEnterBackground()
        {
            lock (stateLock)
            {
                if (isInBackground) return;
                isInBackground = true;
            }

            Log("[AppLifecycleManager] Entering background - stopping AR subsystems...");

            // OnApplicationFocusで既に停止している可能性があるが、
            // 確実に停止するために再度実行
            StopAllARSubsystems();

            // フレームレートを最小に
            Application.targetFrameRate = 1;

            // 時間スケールも停止（Update等の処理を軽減）
            Time.timeScale = 0f;

            Log("[AppLifecycleManager] Background preparation complete");
        }

        /// <summary>
        /// 全てのARサブシステムを停止
        /// AVCaptureSessionのブロッキングを回避するため、
        /// 依存関係の逆順で停止する
        /// </summary>
        private void StopAllARSubsystems()
        {
            if (isProcessingStateChange) return;

            try
            {
                isProcessingStateChange = true;

                if (!pauseARSessionOnBackground) return;

                // 1. まずカメラバックグラウンドを停止（レンダリングを止める）
                if (arCameraBackground != null)
                {
                    arCameraBackgroundWasEnabled = arCameraBackground.enabled;
                    if (arCameraBackground.enabled)
                    {
                        arCameraBackground.enabled = false;
                        Log("[AppLifecycleManager] ARCameraBackground disabled");
                    }
                }

                // 2. カメラマネージャーを停止（カメラフィードを止める）
                if (arCameraManager != null)
                {
                    arCameraManagerWasEnabled = arCameraManager.enabled;
                    if (arCameraManager.enabled)
                    {
                        arCameraManager.enabled = false;
                        Log("[AppLifecycleManager] ARCameraManager disabled");
                    }
                }

                // 3. 最後にARSessionを停止
                // 注意: これが内部でAVCaptureSession.stopRunning()を呼ぶ
                // 上記のコンポーネントを先に停止することで、
                // stopRunningの処理が軽くなることを期待
                if (arSession != null)
                {
                    arSessionWasEnabled = arSession.enabled;
                    if (arSession.enabled)
                    {
                        arSession.enabled = false;
                        Log("[AppLifecycleManager] ARSession disabled");
                    }
                }
            }
            finally
            {
                isProcessingStateChange = false;
            }
        }

        /// <summary>
        /// フォアグラウンド復帰時の処理
        /// </summary>
        private void HandleEnterForeground()
        {
            lock (stateLock)
            {
                if (!isInBackground) return;
                isInBackground = false;
            }

            Log("[AppLifecycleManager] Entering foreground - resuming AR subsystems...");

            // 時間スケールを復元
            Time.timeScale = 1f;

            // コルーチンで段階的に復帰
            if (resumeCoroutine != null)
            {
                StopCoroutine(resumeCoroutine);
            }
            resumeCoroutine = StartCoroutine(ResumeARSessionGradually());
        }

        /// <summary>
        /// ARSessionを段階的に再開
        /// 急激な再開はクラッシュの原因になる可能性があるため、
        /// 段階的に復帰する
        /// </summary>
        private IEnumerator ResumeARSessionGradually()
        {
            // 数フレーム待機してシステムの安定化を待つ
            yield return null;
            yield return null;

            // フレームレートを復元
            Application.targetFrameRate = 60;
            Log("[AppLifecycleManager] Frame rate restored to 60");

            // もう少し待機
            yield return new WaitForSecondsRealtime(0.1f);

            // 停止時と逆順で再開

            // 1. ARSessionを再開
            if (pauseARSessionOnBackground && arSession != null && arSessionWasEnabled)
            {
                arSession.enabled = true;
                Log("[AppLifecycleManager] ARSession re-enabled");

                // ARSessionが安定するまで待機
                yield return new WaitForSecondsRealtime(0.2f);
            }

            // 2. カメラマネージャーを再開
            if (arCameraManager != null && arCameraManagerWasEnabled)
            {
                arCameraManager.enabled = true;
                Log("[AppLifecycleManager] ARCameraManager re-enabled");
                yield return null;
            }

            // 3. カメラバックグラウンドを再開
            if (arCameraBackground != null && arCameraBackgroundWasEnabled)
            {
                arCameraBackground.enabled = true;
                Log("[AppLifecycleManager] ARCameraBackground re-enabled");
            }

            // 安定化のため追加で待機
            yield return new WaitForSecondsRealtime(0.1f);

            Log("[AppLifecycleManager] Foreground resume complete");
            resumeCoroutine = null;
        }

        /// <summary>
        /// 低メモリ警告時の処理
        /// </summary>
        private void OnLowMemory()
        {
            Log("[AppLifecycleManager] Low memory warning received");

            // メモリ解放のためにGCを実行
            System.GC.Collect();
            Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// アプリ終了時の処理
        /// Watchdog Timeoutを防ぐため、素早く終了する必要がある
        /// </summary>
        private void OnApplicationQuitting()
        {
            Log("[AppLifecycleManager] Application quitting - cleaning up...");

            // 終了時も同様にARサブシステムを停止
            // ただし、ここでブロックすると危険なので、
            // 状態フラグのみを操作する

            if (arCameraBackground != null && arCameraBackground.enabled)
            {
                arCameraBackground.enabled = false;
            }

            if (arCameraManager != null && arCameraManager.enabled)
            {
                arCameraManager.enabled = false;
            }

            if (arSession != null && arSession.enabled)
            {
                arSession.enabled = false;
            }
        }

        /// <summary>
        /// 外部からARSessionの状態を取得
        /// </summary>
        public bool IsARSessionActive
        {
            get { return arSession != null && arSession.enabled && !isInBackground && hasFocus; }
        }

        /// <summary>
        /// 外部からバックグラウンド状態を取得
        /// </summary>
        public bool IsInBackgroundState
        {
            get { return isInBackground; }
        }

        /// <summary>
        /// 外部からフォーカス状態を取得
        /// </summary>
        public bool HasFocus
        {
            get { return hasFocus; }
        }

        private void Log(string message)
        {
            if (enableDebugLog)
            {
                Debug.Log(message);
            }
        }
    }
}
