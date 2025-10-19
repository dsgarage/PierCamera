// Assets/Scripts/AR/Input/TouchRouter.cs
using System;
using UnityEngine;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace AR.Input
{
    /// <summary>
    /// シングルタップ・ダブルタップを判定してイベントを発火
    /// </summary>
    public class TouchRouter : MonoBehaviour
    {
        [Header("Double Tap Settings")]
        [Tooltip("ダブルタップと判定する最大時間間隔（秒）")]
        [SerializeField] private float doubleTapMaxIntervalSec = 0.3f;

        [Tooltip("ダブルタップと判定する最大移動距離（ピクセル）")]
        [SerializeField] private float doubleTapMaxMovePixels = 20f;

        // イベント
        public event Action<Vector2> OnSingleTap;
        public event Action<Vector2> OnDoubleTap;

        // ダブルタップ検出用の状態
        private float lastTapTime = -1f;
        private Vector2 lastTapPosition;
        private bool waitingForSecondTap = false;

        void OnEnable()
        {
            EnhancedTouchSupport.Enable();
        }

        void OnDisable()
        {
            EnhancedTouchSupport.Disable();
        }

        void Update()
        {
            // タッチが2本以上なら無視
            if (Touch.activeTouches.Count > 1)
            {
                waitingForSecondTap = false;
                return;
            }

            // タッチが1本の場合
            if (Touch.activeTouches.Count == 1)
            {
                var touch = Touch.activeTouches[0];

                // タッチ終了時のみ処理
                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    Vector2 touchPos = touch.screenPosition;

                    // 移動量チェック
                    float moveDistance = Vector2.Distance(touch.startScreenPosition, touchPos);
                    if (moveDistance > doubleTapMaxMovePixels)
                    {
                        waitingForSecondTap = false;
                        return;
                    }

                    float currentTime = Time.time;

                    // ダブルタップ判定
                    if (waitingForSecondTap &&
                        (currentTime - lastTapTime) <= doubleTapMaxIntervalSec &&
                        Vector2.Distance(lastTapPosition, touchPos) <= doubleTapMaxMovePixels)
                    {
                        // ダブルタップ検出
                        OnDoubleTap?.Invoke(touchPos);
                        waitingForSecondTap = false;
                        lastTapTime = -1f;
                    }
                    else
                    {
                        // 最初のタップまたはシングルタップ候補
                        lastTapTime = currentTime;
                        lastTapPosition = touchPos;
                        waitingForSecondTap = true;

                        // 遅延してシングルタップ判定
                        Invoke(nameof(CheckSingleTap), doubleTapMaxIntervalSec);
                    }
                }
            }
        }

        private void CheckSingleTap()
        {
            // ダブルタップが発生していなければシングルタップとして発火
            if (waitingForSecondTap && Time.time - lastTapTime >= doubleTapMaxIntervalSec)
            {
                OnSingleTap?.Invoke(lastTapPosition);
                waitingForSecondTap = false;
            }
        }
    }
}
