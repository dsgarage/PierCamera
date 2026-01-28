using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// HUD（CanvasGroup）内の ScrollRect に、FaceController の表情ボタンを自動生成する。
/// - アバター生成時に InitializeWithAvatar(newAvatar) を呼ぶ
/// - アバターが消えたらHUDを隠す
/// - 撮影時にHUDを一時的に隠すユーティリティ付き（必要なら追加）
/// </summary>
public class FaceUIManager : MonoBehaviour
{
    [Header("参照")]
    public CanvasGroup hudGroup;      // HUD全体（CanvasGroup）

    [Header("ScrollRect 一式")]
    public ScrollRect scrollRect;     // ScrollRect
    public RectTransform content;     // Viewport 配下の Content（VerticalLayout + ContentSizeFitter 推奨）
    public Button buttonPrefab;       // ラベル(Text/TMP)付きの UGUI Button

    private FaceController faceController;       // 表情管理（Initialize で自動セット）
    private GameObject avatar;         // アバター（Initialize で自動セット）

    [Header("Editor 用ターゲット（固定生成に使用）")]
    public FaceController editorTargetController;

    void Start()
    {
        SetVisible(hudGroup, false);
    }

    void Update()
    {
        // アバターが消えたらHUDも消す
        if (avatar == null && hudGroup && hudGroup.alpha > 0f)
        {
            ClearButtons();
            SetVisible(hudGroup, false);
            faceController = null;
        }
    }

    /// <summary>
    /// 生成されたアバターを受け取り、HUDを初期化＆表示するエントリポイント
    /// </summary>
    public void InitializeWithAvatar(GameObject newAvatar)
    {
        avatar = newAvatar;
        faceController = avatar?.GetComponent<FaceController>();
        OnPlaced(); // 既存ロジックをそのまま使う
    }

    /// <summary>
    /// 外部からHUDを明示的にクリアしたい場合に呼ぶ
    /// </summary>
    public void ClearHUD()
    {
        avatar = null;
        faceController   = null;
        ClearButtons();
        SetVisible(hudGroup, false);
    }

    void OnPlaced()
    {
        if (faceController != null && faceController.FaceNames.Count > 0)
        {
            RegenerateButtons();
            SetVisible(hudGroup, true);
            ScrollToTop();
        }
        else
        {
            Debug.LogError($"[FaceUIManager.OnPlaced]:faceController = {faceController}");
            Debug.LogError($"[FaceUIManager.OnPlaced]:faceController.FaceNames.Count = {faceController.FaceNames.Count}");
            ClearButtons();
            SetVisible(hudGroup, false);
        }
    }

    void RegenerateButtons()
    {
        ClearButtons();

        foreach (var name in faceController.FaceNames)
        {
            var btn = Instantiate(buttonPrefab, content);
            btn.name = $"Btn_{name}";

            // --- ラベル設定（UGUI / TMP 両対応） ---
            var ugui = btn.GetComponentInChildren<Text>(true);
            if (ugui)
            {
                ugui.text = name;
                ugui.fontStyle = FontStyle.Bold; // 目視しやすく
            }

            var tmp = btn.GetComponentInChildren<TMPro.TMP_Text>(true);
            if (tmp)
            {
                tmp.text = name;
                tmp.fontStyle |= TMPro.FontStyles.Bold;               // B をON
                tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap; // 折返し禁止
                tmp.ForceMeshUpdate();
            }

            // --- 動作（ButtonFaceAction / onClick）どちらでも効くように ---
            var act = btn.GetComponent<ButtonFaceAction>();
            if (act)
            {
                act.controller = faceController;
                act.faceName   = name;
            }

            // ButtonFaceAction を使わないパスでも動くように保険で onClick も積む
            string captured = name;
            btn.onClick.AddListener(() => faceController.SetFace(captured, keep: true));
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(content);
        Canvas.ForceUpdateCanvases();
        ScrollToTop();
    }

    public void ClearButtons()
    {
        if (!content) return;

        // 子オブジェクトを全部列挙して削除
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i).gameObject;
#if UNITY_EDITOR
            // エディタ上でUndo対応にしたい場合
            if (!Application.isPlaying)
                DestroyImmediate(child);
            else
                Destroy(child);
#else
            Destroy(child);
#endif
        }
    }

    void ScrollToTop()
    {
        if (!scrollRect) return;
        scrollRect.verticalNormalizedPosition = 1f; // 1=上端
    }

    public static void SetVisible(CanvasGroup g, bool v)
    {
        if (!g) return;
        g.alpha = v ? 1f : 0f;
        g.interactable = v;
        g.blocksRaycasts = v;
    }
}