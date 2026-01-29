using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using AICam.AvatarCache;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アバタースロットUIコンポーネント
    /// Issue #73: 円形プログレスインジケーター対応
    /// </summary>
    public class AvatarSlot : MonoBehaviour, IPointerClickHandler, IPointerDownHandler, IPointerUpHandler
    {
        [Header("UI References")]
        [SerializeField] private Image iconImage;
        [SerializeField] private Image emptyStateImage;
        [SerializeField] private GameObject selectedIndicator;
        [SerializeField] private TextMeshProUGUI slotNumberText;
        [SerializeField] private Button slotButton;

        [Header("Progress Indicator (Issue #73)")]
        [SerializeField] private RawImage progressRing;
        [SerializeField] private Material progressMaterialTemplate;

        [Header("Settings")]
        [SerializeField] private float longPressThreshold = 0.5f;
        [SerializeField] private float doubleTapThreshold = 0.3f;
        [SerializeField] private Color emptyColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
        [SerializeField] private Color configuredColor = Color.white;
        [SerializeField] private Color loadingColor = new Color(1f, 1f, 1f, 0.4f); // ロード中は薄く表示

        // スロットデータ
        private AvatarSlotData slotData;
        private int slotIndex = -1;
        private bool isSelected;
        private Sprite currentIcon;

        // 長押し検出用
        private bool isPointerDown;
        private float pointerDownTime;

        // ダブルタップ検出用
        private float lastClickTime;
        private bool waitingForSecondTap;

        // Issue #73: プログレス関連
        private Material progressMaterialInstance;
        private bool isLoading;
        private Coroutine hideProgressCoroutine;
        private static readonly int ProgressProperty = Shader.PropertyToID("_Progress");

        // イベント
        public event Action<int> OnSlotClicked;
        public event Action<int> OnSlotLongPressed;
        public event Action<int> OnSlotDoubleTapped;

        public int SlotIndex => slotIndex;
        public bool IsConfigured => slotData != null && slotData.IsConfigured;
        public AvatarSlotData SlotData => slotData;
        public bool IsLoading => isLoading;

        private void Awake()
        {
            // ボタンクリックイベントを設定
            if (slotButton != null)
            {
                slotButton.onClick.AddListener(OnButtonClick);
            }
        }

        private void Update()
        {
            // 長押し検出
            if (isPointerDown)
            {
                if (Time.time - pointerDownTime >= longPressThreshold)
                {
                    isPointerDown = false;
                    OnLongPress();
                }
            }
        }

        /// <summary>
        /// スロットを初期化
        /// </summary>
        public void Initialize(int index, AvatarSlotData data = null)
        {
            slotIndex = index;
            slotData = data ?? new AvatarSlotData(index);

            UpdateVisual();

            Debug.Log($"[AvatarSlot] Initialized slot {index}, Configured: {IsConfigured}");
        }

        /// <summary>
        /// スロットデータを設定
        /// </summary>
        public void SetSlotData(AvatarSlotData data)
        {
            slotData = data;
            slotData.slotIndex = slotIndex;

            UpdateVisual();
        }

        /// <summary>
        /// アイコン画像を設定
        /// </summary>
        public void SetIcon(Sprite icon)
        {
            currentIcon = icon;

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.color = icon != null ? configuredColor : emptyColor;
                iconImage.gameObject.SetActive(icon != null);
            }

            if (emptyStateImage != null)
            {
                emptyStateImage.gameObject.SetActive(icon == null);
            }
        }

        /// <summary>
        /// ファイルパスからアイコンを読み込んで設定
        /// </summary>
        public void LoadAndSetIcon(string iconPath)
        {
            if (string.IsNullOrEmpty(iconPath))
            {
                SetIcon(null);
                return;
            }

            Sprite sprite = AvatarIconCapture.LoadIconAsSprite(iconPath);
            SetIcon(sprite);
        }

        /// <summary>
        /// 選択状態を設定
        /// </summary>
        public void SetSelected(bool selected)
        {
            isSelected = selected;

            if (selectedIndicator != null)
            {
                selectedIndicator.SetActive(selected);
            }
        }

        /// <summary>
        /// 空の状態に戻す
        /// </summary>
        public void Clear()
        {
            if (slotData != null)
            {
                slotData.Clear();
            }

            currentIcon = null;
            UpdateVisual();
        }

        /// <summary>
        /// 表示を更新
        /// </summary>
        private void UpdateVisual()
        {
            // スロット番号
            if (slotNumberText != null)
            {
                slotNumberText.text = (slotIndex + 1).ToString();
            }

            // アイコン
            if (IsConfigured && slotData.HasIcon)
            {
                LoadAndSetIcon(slotData.iconFilePath);
            }
            else
            {
                SetIcon(null);
            }

            // 選択状態
            SetSelected(isSelected);
        }

        /// <summary>
        /// ボタンクリック時
        /// </summary>
        private void OnButtonClick()
        {
            // ロード中なら無視（重複ロード防止）
            if (isLoading)
            {
                Debug.Log($"[AvatarSlot] Slot {slotIndex} is loading, ignoring click");
                return;
            }

            float currentTime = Time.time;

            // ダブルタップ検出（設定済みスロットのみ）
            if (IsConfigured && waitingForSecondTap && (currentTime - lastClickTime) <= doubleTapThreshold)
            {
                // ダブルタップ検出
                Debug.Log($"[AvatarSlot] Slot {slotIndex} double-tapped");
                waitingForSecondTap = false;
                OnSlotDoubleTapped?.Invoke(slotIndex);
                return;
            }

            // シングルクリック
            Debug.Log($"[AvatarSlot] Slot {slotIndex} clicked, Configured: {IsConfigured}");
            lastClickTime = currentTime;
            waitingForSecondTap = IsConfigured; // 設定済みの場合のみダブルタップを待機

            // 設定済みスロットの場合、即座にロード中フラグを設定（重複クリック防止）
            if (IsConfigured)
            {
                isLoading = true;
            }

            OnSlotClicked?.Invoke(slotIndex);
        }

        /// <summary>
        /// 長押し時
        /// </summary>
        private void OnLongPress()
        {
            Debug.Log($"[AvatarSlot] Slot {slotIndex} long pressed");
            OnSlotLongPressed?.Invoke(slotIndex);
        }

        #region Pointer Events

        public void OnPointerClick(PointerEventData eventData)
        {
            // 長押しでない場合のみクリックとして処理
            if (!isPointerDown && Time.time - pointerDownTime < longPressThreshold)
            {
                // ボタンのOnClickで処理するので、ここでは何もしない
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPointerDown = true;
            pointerDownTime = Time.time;
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPointerDown = false;
        }

        #endregion

        #region Progress Indicator (Issue #73)

        /// <summary>
        /// プログレスマテリアルを初期化
        /// </summary>
        private void InitializeProgressMaterial()
        {
            if (progressRing == null) return;

            // マテリアルインスタンスを作成（共有マテリアルを変更しないため）
            if (progressMaterialTemplate != null && progressMaterialInstance == null)
            {
                progressMaterialInstance = new Material(progressMaterialTemplate);
                progressRing.material = progressMaterialInstance;
            }

            // 初期状態は非表示
            progressRing.gameObject.SetActive(false);
        }

        /// <summary>
        /// ロード進捗を設定（0.0 〜 1.0）
        /// </summary>
        /// <param name="progress01">進捗（0.0〜1.0）</param>
        public void SetProgress(float progress01)
        {
            if (progressRing == null) return;

            // マテリアル初期化
            if (progressMaterialInstance == null)
            {
                InitializeProgressMaterial();
            }

            // 進捗を0-1にクランプ
            progress01 = Mathf.Clamp01(progress01);

            isLoading = progress01 > 0f && progress01 < 1f;

            // 表示/非表示
            bool shouldShow = progress01 > 0f && progress01 < 1f;
            if (progressRing.gameObject.activeSelf != shouldShow)
            {
                progressRing.gameObject.SetActive(shouldShow);
            }

            // シェーダーパラメータ更新
            if (progressMaterialInstance != null && shouldShow)
            {
                progressMaterialInstance.SetFloat(ProgressProperty, progress01);
            }
        }

        /// <summary>
        /// ロード開始
        /// </summary>
        public void StartLoading()
        {
            // 非表示コルーチンをキャンセル
            if (hideProgressCoroutine != null)
            {
                StopCoroutine(hideProgressCoroutine);
                hideProgressCoroutine = null;
            }

            isLoading = true;
            SetProgress(0.01f); // 0より大きい値で開始（0だと非表示）

            // アイコンを薄く表示
            SetIconLoadingState(true);

            Debug.Log($"[AvatarSlot] Slot {slotIndex} loading started");
        }

        /// <summary>
        /// ロード完了
        /// </summary>
        public void CompleteLoading()
        {
            SetProgress(1f);

            // アイコンを通常表示に戻す
            SetIconLoadingState(false);

            // 少し遅延してから非表示（完了アニメーション用）
            if (hideProgressCoroutine != null)
            {
                StopCoroutine(hideProgressCoroutine);
            }
            hideProgressCoroutine = StartCoroutine(HideProgressAfterDelay(0.3f));

            Debug.Log($"[AvatarSlot] Slot {slotIndex} loading completed");
        }

        /// <summary>
        /// ロードキャンセル/失敗
        /// </summary>
        public void CancelLoading()
        {
            if (hideProgressCoroutine != null)
            {
                StopCoroutine(hideProgressCoroutine);
                hideProgressCoroutine = null;
            }

            isLoading = false;

            // アイコンを通常表示に戻す
            SetIconLoadingState(false);

            if (progressRing != null)
            {
                progressRing.gameObject.SetActive(false);
            }

            Debug.Log($"[AvatarSlot] Slot {slotIndex} loading cancelled");
        }

        private IEnumerator HideProgressAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);

            if (progressRing != null)
            {
                progressRing.gameObject.SetActive(false);
            }
            isLoading = false;
            hideProgressCoroutine = null;
        }

        /// <summary>
        /// アイコンのロード中表示状態を設定
        /// </summary>
        /// <param name="loading">ロード中かどうか</param>
        private void SetIconLoadingState(bool loading)
        {
            if (iconImage != null && currentIcon != null)
            {
                iconImage.color = loading ? loadingColor : configuredColor;
            }
        }

        #endregion

        private void OnDestroy()
        {
            // アイコンのSprite/Textureを解放
            if (currentIcon != null && currentIcon.texture != null)
            {
                // RuntimeでロードしたTextureの場合のみ解放
                // Note: Spriteの解放はSprite.Createで作成した場合のみ必要
            }

            // Issue #73: プログレスマテリアルインスタンスを解放
            if (progressMaterialInstance != null)
            {
                Destroy(progressMaterialInstance);
                progressMaterialInstance = null;
            }
        }
    }
}
