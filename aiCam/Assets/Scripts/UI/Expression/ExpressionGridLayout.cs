using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Experimental.SceneManagement;
#endif

[ExecuteAlways]
public class ExpressionGridLayout : MonoBehaviour
{
    [Header("Editor 用ターゲット（固定生成に使用）")]
    public FaceController editorTargetController;

    [Header("Target Area (ScrollRect Content 推奨)")]
    [SerializeField] private RectTransform contentRect;

    [Header("Slot Prefab (任意のUI)")]
    [SerializeField] private GameObject slotPrefab;

    [Header("Data")]
    [Tooltip("表示したいポーズ（AnimationClip と サムネ画像）の一覧")]
    [SerializeField] private List<ExpressionItem> items = new List<ExpressionItem>();

    [Header("Layout")]
    [Min(1)] [SerializeField] private int columns = 6;
    [SerializeField] private Vector2 spacing = new Vector2(8, 8);
    [SerializeField] private Vector2 padding = new Vector2(12, 12);
    [SerializeField] private bool squareCell = true;
    [Min(0)] [SerializeField] private float minCellWidth = 0f;
    [SerializeField] private bool autoScrollToTop = true;

    [Header("Rebuild Policy")]
    [Tooltip("Update 押下時に一度すべての子を削除し、必要数だけ新規生成します。")]
    [SerializeField] private bool fullRebuildOnUpdate = true;

    // （確認用）
    [SerializeField, ReadOnly] private Vector2 lastCellSize;
    [SerializeField, ReadOnly] private int lastRows;

    public IReadOnlyList<ExpressionItem> Items => items;

    public void SetItems(IEnumerable<ExpressionItem> list, bool andRebuild = true)
    {
        items = new List<ExpressionItem>(list ?? Array.Empty<ExpressionItem>());
        if (andRebuild) UpdateLayoutNow();
    }

    public void RebuildLayoutFromScratch()
    {
        if (!contentRect)
        {
            Debug.LogWarning("[ExpressionGridLayout] contentRect is null.");
            return;
        }
        ClearAllSlotsInContentHard();
        CreateChildrenExact(items.Count);
        UpdateLayoutCore(); // レイアウトだけ回す
    }

    /// <summary>
    /// Update ボタンからも呼ばれる。必要なら「全削除→新規生成」を先に行う。
    /// </summary>
    public void UpdateLayoutNow()
    {
        if (contentRect == null || slotPrefab == null)
        {
            Debug.LogWarning("[ExpressionGridLayout] contentRect or slotPrefab is null.");
            return;
        }

        EnsurePivotTopLeft(contentRect);

        if (fullRebuildOnUpdate)
        {
            ClearAllSlotsInContentHard();
            CreateChildrenExact(items.Count);
        }
        else
        {
            EnsureChildrenCount(items.Count); // 既存再利用モード
        }

        UpdateLayoutCore();

        if (autoScrollToTop)
        {
            var sr = contentRect.GetComponentInParent<ScrollRect>();
            if (sr && sr.vertical) sr.normalizedPosition = new Vector2(sr.normalizedPosition.x, 1f);
        }
    }

    /// <summary>
    /// レイアウト・データバインド本体
    /// </summary>
    private void UpdateLayoutCore()
    {
        var rectWidth = GetRectWidth(contentRect);
        float availableW = Mathf.Max(0, rectWidth - padding.x * 2f - spacing.x * (columns - 1));
        float cellW = (columns > 0) ? availableW / columns : availableW;
        if (minCellWidth > 0) cellW = Mathf.Max(cellW, minCellWidth);

        float cellH = squareCell ? cellW : cellW; // 必要なら別ロジックに
        var cellSize = new Vector2(Mathf.Floor(cellW), Mathf.Floor(cellH));
        lastCellSize = cellSize;

        int rows = Mathf.CeilToInt(items.Count / (float)columns);
        lastRows = rows;

        float totalH = padding.y * 2f + rows * cellSize.y + Mathf.Max(0, rows - 1) * spacing.y;
        var sd = contentRect.sizeDelta;
        contentRect.sizeDelta = new Vector2(sd.x, totalH);

        for (int i = 0; i < items.Count; i++)
        {
            var child = contentRect.GetChild(i) as RectTransform;
            if (!child) continue;

            child.gameObject.SetActive(true);
            child.anchorMin = new Vector2(0, 1);
            child.anchorMax = new Vector2(0, 1);
            child.pivot     = new Vector2(0, 1);
            child.sizeDelta = cellSize;

            int col = i % columns;
            int row = i / columns;
            float x = padding.x + col * (cellSize.x + spacing.x);
            float y = padding.y + row * (cellSize.y + spacing.y);
            child.anchoredPosition = new Vector2(x, -y);

            // スロットへデータを渡す
            var slot = child.GetComponent<PoseSlot>();
            if (slot != null)
            {
                var it = items[i];
                string label = !string.IsNullOrEmpty(it.displayNameOverride)
                             ? it.displayNameOverride
                             : (it.clip ? it.clip.name : "(None)");
                slot.Bind(it.clip, i, it.thumbnail, label);
            }
        }

        // 余剰分は非表示（fullRebuildOnUpdate=true なら基本来ないが安全のため）
        for (int i = items.Count; i < contentRect.childCount; i++)
            contentRect.GetChild(i).gameObject.SetActive(false);
    }

    // ===== 生成/削除ユーティリティ =====

    /// <summary>
    /// Content 直下の子オブジェクトを「確実に」全削除。
    /// Editor: Undo 対応＋Immediate / Play: 非表示→Destroy
    /// </summary>
    private void ClearAllSlotsInContentHard()
    {
        if (!contentRect) return;

        // まず配列に退避（削除中に childCount を参照しない）
        var victims = new List<GameObject>(contentRect.childCount);
        for (int i = 0; i < contentRect.childCount; i++)
            victims.Add(contentRect.GetChild(i).gameObject);

        foreach (var go in victims)
        {
            if (!go) continue;

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                try
                {
                    Undo.DestroyObjectImmediate(go);
                }
                catch
                {
                    DestroyImmediate(go, allowDestroyingAssets: false);
                }
            }
            else
#endif
            {
                // まず見えなくする（同フレームで重複表示しないため）
                go.SetActive(false);
                Destroy(go); // 次フレームで確実に消える
            }
        }

        // Transform の親子関係キャッシュをクリーン
        contentRect.DetachChildren();

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Prefab ステージでも dirty マーク
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null) EditorSceneManager.MarkSceneDirty(stage.scene);
            else EditorSceneManager.MarkSceneDirty(gameObject.scene);
            EditorUtility.SetDirty(contentRect);
        }
#endif
    }

    /// <summary>
    /// ちょうど n 個だけ新規生成する（既存は前段で全削除されている前提）
    /// </summary>
    private void CreateChildrenExact(int n)
    {
        if (!contentRect || !slotPrefab) return;
        for (int i = 0; i < n; i++)
        {
            GameObject go;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                go = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, contentRect);
            else
                go = Instantiate(slotPrefab, contentRect);
#else
            go = Instantiate(slotPrefab, contentRect);
#endif
            var btnFaceAct = go.GetComponent<ButtonFaceAction>();
            if (btnFaceAct) btnFaceAct.faceName = items[i].clip.name;
            go.name = $"Slot_{i:000}";
            go.SetActive(true);
        }
    }

    /// <summary>
    /// 既存を活かす（不足分だけ Instantiate）
    /// </summary>
    private void EnsureChildrenCount(int needed)
    {
        int current = contentRect.childCount;
        for (int i = 0; i < current; i++)
            contentRect.GetChild(i).gameObject.SetActive(i < needed);

        for (int i = current; i < needed; i++)
        {
            GameObject go;
#if UNITY_EDITOR
            if (!Application.isPlaying)
                go = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, contentRect);
            else
                go = Instantiate(slotPrefab, contentRect);
#else
            go = Instantiate(slotPrefab, contentRect);
#endif
            go.name = $"Slot_{i:000}";
            go.SetActive(true);
        }
    }

    private static float GetRectWidth(RectTransform rt) => rt.rect.width;

    private static void EnsurePivotTopLeft(RectTransform rt)
    {
        if (rt.pivot != new Vector2(0, 1))
            rt.pivot = new Vector2(0, 1);
        if (rt.anchorMin != new Vector2(0, 1) || rt.anchorMax != new Vector2(1, 1))
        {
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(0, rt.anchoredPosition.y);
        }
    }

    // インスペクターのコンテキストメニューからも呼べる
    [ContextMenu("Update Layout")]
    private void ContextUpdate() => UpdateLayoutNow();
}