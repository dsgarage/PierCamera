using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using TMPro;

namespace AICam.FBXLoader
{
    /// <summary>
    /// アラートバーUIコントローラー
    /// 警告（黄色）とエラー（赤）の2種類のアラートを表示
    /// </summary>
    public class AlertBarController : MonoBehaviour
    {
        public static AlertBarController Instance { get; private set; }

        [Header("UI References")]
        [SerializeField] private GameObject alertBarPanel;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private Button closeButton;

        [Header("Colors")]
        [SerializeField] private Color warningColor = new Color(1f, 0.8f, 0f, 0.95f);      // 黄色（警告）
        [SerializeField] private Color errorColor = new Color(0.9f, 0.2f, 0.2f, 0.95f);    // 赤（エラー）
        [SerializeField] private Color warningTextColor = Color.black;
        [SerializeField] private Color errorTextColor = Color.white;

        [Header("Settings")]
        [SerializeField] private float warningAutoHideDelay = 5f;    // 警告の自動非表示時間
        [SerializeField] private float errorAutoHideDelay = 0f;      // エラーは自動非表示しない（0で無効）
        [SerializeField] private float fadeInDuration = 0.3f;
        [SerializeField] private float fadeOutDuration = 0.3f;

        public enum AlertType
        {
            Warning,    // 黄色 - 処理は継続可能だが注意が必要
            Error       // 赤 - 処理を継続するのが困難
        }

        // アラートコード定義
        public static class AlertCodes
        {
            // 警告（Warning）
            public const string MANIFEST_NOT_FOUND = "W001";
            public const string WEIGHT_ASSIGNMENT_PARTIAL = "W002";
            public const string BINDPOSE_MISMATCH = "W003";
            public const string ANIMATION_MISSING = "W004";
            public const string MATERIAL_FALLBACK = "W005";
            public const string VRM_VERSION_UNKNOWN = "W006";
            public const string EXPRESSION_EMPTY = "W007";
            public const string EXPRESSION_BLENDSHAPE_MISSING = "W008";

            // エラー（Error）
            public const string AVATAR_BUILD_FAILED = "E001";
            public const string WEIGHT_ASSIGNMENT_FAILED = "E002";
            public const string BINDPOSE_INVALID = "E003";
            public const string FILE_NOT_FOUND = "E004";
            public const string FILE_FORMAT_INVALID = "E005";
            public const string VRM_LOAD_FAILED = "E006";
            public const string FBX_LOAD_FAILED = "E007";
            public const string HUMANOID_SETUP_FAILED = "E008";
        }

        private Coroutine hideCoroutine;
        private CanvasGroup canvasGroup;
        private bool isShowing;

        private void Awake()
        {
            // シングルトン設定
            if (Instance == null)
            {
                Instance = this;
            }
            else
            {
                Debug.LogWarning("[AlertBar] Instance already exists, destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            // CanvasGroup取得または追加
            canvasGroup = alertBarPanel?.GetComponent<CanvasGroup>();
            if (canvasGroup == null && alertBarPanel != null)
            {
                canvasGroup = alertBarPanel.AddComponent<CanvasGroup>();
            }

            // 閉じるボタンのイベント設定
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(Hide);
            }

            // 初期状態は非表示
            if (alertBarPanel != null)
            {
                alertBarPanel.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        /// <summary>
        /// 警告を表示（黄色）
        /// </summary>
        public void ShowWarning(string code, string message)
        {
            Show(AlertType.Warning, code, message);
        }

        /// <summary>
        /// エラーを表示（赤）
        /// </summary>
        public void ShowError(string code, string message)
        {
            Show(AlertType.Error, code, message);
        }

        /// <summary>
        /// アラートを表示
        /// </summary>
        public void Show(AlertType type, string code, string message)
        {
            if (alertBarPanel == null || messageText == null || backgroundImage == null)
            {
                Debug.LogError($"[AlertBar] UI references not set! Type: {type}, Code: {code}, Message: {message}");
                return;
            }

            // 既存の自動非表示をキャンセル
            if (hideCoroutine != null)
            {
                StopCoroutine(hideCoroutine);
                hideCoroutine = null;
            }

            // 色設定
            if (type == AlertType.Warning)
            {
                backgroundImage.color = warningColor;
                messageText.color = warningTextColor;
            }
            else
            {
                backgroundImage.color = errorColor;
                messageText.color = errorTextColor;
            }

            // メッセージ設定
            string typePrefix = type == AlertType.Warning ? "Warning" : "Error";
            messageText.text = $"[{code}] {message}";

            // ログ出力
            if (type == AlertType.Warning)
            {
                Debug.LogWarning($"[AlertBar] {typePrefix} [{code}]: {message}");
            }
            else
            {
                Debug.LogError($"[AlertBar] {typePrefix} [{code}]: {message}");
            }

            // 表示
            alertBarPanel.SetActive(true);
            isShowing = true;

            // フェードイン
            StartCoroutine(FadeIn());

            // 自動非表示設定
            float autoHideDelay = type == AlertType.Warning ? warningAutoHideDelay : errorAutoHideDelay;
            if (autoHideDelay > 0)
            {
                hideCoroutine = StartCoroutine(AutoHide(autoHideDelay));
            }
        }

        /// <summary>
        /// アラートを非表示
        /// </summary>
        public void Hide()
        {
            if (!isShowing) return;

            if (hideCoroutine != null)
            {
                StopCoroutine(hideCoroutine);
                hideCoroutine = null;
            }

            StartCoroutine(FadeOut());
        }

        private IEnumerator FadeIn()
        {
            if (canvasGroup == null) yield break;

            canvasGroup.alpha = 0f;
            float elapsed = 0f;

            while (elapsed < fadeInDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(0f, 1f, elapsed / fadeInDuration);
                yield return null;
            }

            canvasGroup.alpha = 1f;
        }

        private IEnumerator FadeOut()
        {
            if (canvasGroup == null)
            {
                if (alertBarPanel != null)
                {
                    alertBarPanel.SetActive(false);
                }
                isShowing = false;
                yield break;
            }

            float elapsed = 0f;

            while (elapsed < fadeOutDuration)
            {
                elapsed += Time.deltaTime;
                canvasGroup.alpha = Mathf.Lerp(1f, 0f, elapsed / fadeOutDuration);
                yield return null;
            }

            canvasGroup.alpha = 0f;
            alertBarPanel.SetActive(false);
            isShowing = false;
        }

        private IEnumerator AutoHide(float delay)
        {
            yield return new WaitForSeconds(delay);
            Hide();
        }

        #region Static Helper Methods

        /// <summary>
        /// Manifestファイルがない警告
        /// </summary>
        public static void WarnManifestNotFound(string details = "")
        {
            string msg = "Manifestファイルが見つかりません。デフォルト設定を使用します。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowWarning(AlertCodes.MANIFEST_NOT_FOUND, msg);
        }

        /// <summary>
        /// ウェイト割り当て部分失敗の警告
        /// </summary>
        public static void WarnWeightAssignmentPartial(string details = "")
        {
            string msg = "一部のウェイト割り当てに問題があります。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowWarning(AlertCodes.WEIGHT_ASSIGNMENT_PARTIAL, msg);
        }

        /// <summary>
        /// BindPose不一致の警告
        /// </summary>
        public static void WarnBindPoseMismatch(string details = "")
        {
            string msg = "BindPoseの設定に不一致があります。ポーズが正しく表示されない可能性があります。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowWarning(AlertCodes.BINDPOSE_MISMATCH, msg);
        }

        /// <summary>
        /// VRMバージョン不明の警告
        /// </summary>
        public static void WarnVrmVersionUnknown(string details = "")
        {
            string msg = "VRMバージョンを検出できませんでした。VRM 0.x として読み込みを試みます。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowWarning(AlertCodes.VRM_VERSION_UNKNOWN, msg);
        }

        /// <summary>
        /// アバター構築失敗のエラー
        /// </summary>
        public static void ErrorAvatarBuildFailed(string details = "")
        {
            string msg = "アバターの構築に失敗しました。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowError(AlertCodes.AVATAR_BUILD_FAILED, msg);
        }

        /// <summary>
        /// ウェイト割り当て失敗のエラー
        /// </summary>
        public static void ErrorWeightAssignmentFailed(string details = "")
        {
            string msg = "ウェイトの割り当てに失敗しました。モデルが正しく表示されません。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowError(AlertCodes.WEIGHT_ASSIGNMENT_FAILED, msg);
        }

        /// <summary>
        /// BindPose無効のエラー
        /// </summary>
        public static void ErrorBindPoseInvalid(string details = "")
        {
            string msg = "BindPoseの設定が無効です。モデルを読み込めません。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowError(AlertCodes.BINDPOSE_INVALID, msg);
        }

        /// <summary>
        /// ファイルが見つからないエラー
        /// </summary>
        public static void ErrorFileNotFound(string filePath)
        {
            string msg = $"ファイルが見つかりません: {filePath}";
            Instance?.ShowError(AlertCodes.FILE_NOT_FOUND, msg);
        }

        /// <summary>
        /// ファイル形式が無効の警告
        /// Issue #416: 黄色（警告）で表示
        /// </summary>
        public static void ErrorFileFormatInvalid(string details = "")
        {
            string msg = "ロードできないファイル形式です。VRM/FBXファイルを選択してください。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowWarning(AlertCodes.FILE_FORMAT_INVALID, msg);
        }

        /// <summary>
        /// VRM読み込み失敗のエラー
        /// </summary>
        public static void ErrorVrmLoadFailed(string details = "")
        {
            string msg = "VRMファイルの読み込みに失敗しました。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowError(AlertCodes.VRM_LOAD_FAILED, msg);
        }

        /// <summary>
        /// FBX読み込み失敗のエラー
        /// </summary>
        public static void ErrorFbxLoadFailed(string details = "")
        {
            string msg = "FBXファイルの読み込みに失敗しました。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowError(AlertCodes.FBX_LOAD_FAILED, msg);
        }

        /// <summary>
        /// Humanoidセットアップ失敗のエラー
        /// </summary>
        public static void ErrorHumanoidSetupFailed(string details = "")
        {
            string msg = "Humanoidの設定に失敗しました。アニメーションが正しく動作しません。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowError(AlertCodes.HUMANOID_SETUP_FAILED, msg);
        }

        /// <summary>
        /// 表情が空（BlendShapeバインディングがない）の警告
        /// </summary>
        public static void WarnExpressionEmpty(string expressionName)
        {
            string msg = $"表情「{expressionName}」にはBlendShapeが設定されていません。";
            Instance?.ShowWarning(AlertCodes.EXPRESSION_EMPTY, msg);
        }

        /// <summary>
        /// 表情のBlendShapeが見つからない警告
        /// </summary>
        public static void WarnExpressionBlendShapeMissing(string expressionName, string details = "")
        {
            string msg = $"表情「{expressionName}」のBlendShapeが見つかりません。";
            if (!string.IsNullOrEmpty(details)) msg += $" ({details})";
            Instance?.ShowWarning(AlertCodes.EXPRESSION_BLENDSHAPE_MISSING, msg);
        }

        #endregion
    }
}
