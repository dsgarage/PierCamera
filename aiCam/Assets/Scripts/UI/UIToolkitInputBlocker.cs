using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;

namespace AICam.UI
{
    /// <summary>
    /// UIToolkitの入力がAR側に貫通するのを防ぐためのブロッカー
    /// PanelEventHandlerと連携してEventSystemにUIToolkitの入力状態を通知
    ///
    /// 課題対応:
    /// - K2: UI上タップ時にAR側タップ処理をスキップするためのゲート処理
    /// - K3: UIToolkitが入力を正しく拾うための基盤設定
    ///
    /// 方針:
    /// - S1: AR側のタップ処理に「UI上か判定 → UI上ならreturn」のガードを入れる
    /// - S2: UIToolkit側のpickingModeをPositionにして「透明でもタップを吸うUI面」を明示する
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UIToolkitInputBlocker : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("デバッグログを出力")]
        [SerializeField] private bool debugLog = false;

        private UIDocument _uiDocument;
        private VisualElement _root;
        private bool _isPointerOverUI = false;

        /// <summary>
        /// 現在ポインターがUI上にあるかどうか
        /// 外部から参照して入力をブロックするために使用
        /// </summary>
        public bool IsPointerOverUI => _isPointerOverUI;

        /// <summary>
        /// シングルトンインスタンス（複数UIDocumentがある場合は最初のもの）
        /// </summary>
        public static UIToolkitInputBlocker Instance { get; private set; }

        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            _uiDocument = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            if (_uiDocument == null) return;

            _root = _uiDocument.rootVisualElement;
            if (_root == null) return;

            // ポインターイベントを登録
            _root.RegisterCallback<PointerEnterEvent>(OnPointerEnter, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerLeaveEvent>(OnPointerLeave, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            _root.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);

            if (debugLog)
            {
                Debug.Log("[UIToolkitInputBlocker] Registered pointer events on root");
            }
        }

        private void OnDisable()
        {
            if (_root == null) return;

            _root.UnregisterCallback<PointerEnterEvent>(OnPointerEnter);
            _root.UnregisterCallback<PointerLeaveEvent>(OnPointerLeave);
            _root.UnregisterCallback<PointerDownEvent>(OnPointerDown);
            _root.UnregisterCallback<PointerUpEvent>(OnPointerUp);
            _root.UnregisterCallback<PointerMoveEvent>(OnPointerMove);
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnPointerEnter(PointerEnterEvent evt)
        {
            // picking-mode="Position"の要素上にポインターが入った
            if (evt.target is VisualElement ve && ve.pickingMode == PickingMode.Position)
            {
                _isPointerOverUI = true;
                if (debugLog)
                {
                    Debug.Log($"[UIToolkitInputBlocker] PointerEnter: {ve.name}");
                }
            }
        }

        private void OnPointerLeave(PointerLeaveEvent evt)
        {
            _isPointerOverUI = false;
            if (debugLog)
            {
                Debug.Log("[UIToolkitInputBlocker] PointerLeave");
            }
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.target is VisualElement ve && ve.pickingMode == PickingMode.Position)
            {
                _isPointerOverUI = true;
                if (debugLog)
                {
                    Debug.Log($"[UIToolkitInputBlocker] PointerDown on: {ve.name}");
                }
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            // PointerUpではまだUI上にいるかチェック
            if (debugLog)
            {
                Debug.Log("[UIToolkitInputBlocker] PointerUp");
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            // ポインター移動中もUI上かチェック
            bool wasOverUI = _isPointerOverUI;
            _isPointerOverUI = evt.target is VisualElement ve && ve.pickingMode == PickingMode.Position;

            if (debugLog && wasOverUI != _isPointerOverUI)
            {
                Debug.Log($"[UIToolkitInputBlocker] PointerMove: overUI changed to {_isPointerOverUI}");
            }
        }

        /// <summary>
        /// 指定されたスクリーン座標がUIToolkit上にあるかチェック
        /// RuntimePanelUtils.ScreenToPanelを使用して正確な座標変換を行う
        /// </summary>
        public bool IsScreenPositionOverUI(Vector2 screenPosition)
        {
            if (_uiDocument == null || _root == null) return false;

            var panel = _root.panel;
            if (panel == null) return false;

            // スクリーン座標をパネル座標に変換
            // UIToolkit: Y軸が上から下
            // Unity Screen: Y軸が下から上
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(
                panel,
                new Vector2(screenPosition.x, Screen.height - screenPosition.y)
            );

            // パネル座標でヒットテスト
            var pickedElement = panel.Pick(panelPosition);

            if (pickedElement != null && pickedElement.pickingMode == PickingMode.Position)
            {
                if (debugLog)
                {
                    Debug.Log($"[UIToolkitInputBlocker] Hit: {pickedElement.name} at panel({panelPosition})");
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// 静的メソッド：UIToolkit上にあるかチェック
        /// PlaceAvatarOnPlaneOnlyなどから簡単に呼び出せる
        /// </summary>
        public static bool IsOverUI(Vector2 screenPosition)
        {
            if (Instance != null)
            {
                return Instance.IsScreenPositionOverUI(screenPosition);
            }
            return false;
        }
    }
}
