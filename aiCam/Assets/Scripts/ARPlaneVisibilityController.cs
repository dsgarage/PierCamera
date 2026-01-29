using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// AR平面検知プレートの表示/非表示を制御するクラス
/// UIのToggleやCheckboxと連携して使用します
/// </summary>
[DefaultExecutionOrder(-10)] // ARPlaneManagerより後に実行
public class ARPlaneVisibilityController : MonoBehaviour
{
    [Header("References")]
    [Tooltip("AR平面を管理するマネージャー（自動取得可能）")]
    [SerializeField] private ARPlaneManager planeManager;

    [Header("Settings")]
    [Tooltip("起動時に平面プレートを表示するか")]
    [SerializeField] private bool showPlanesOnStart = true;

    [Tooltip("平面検知自体を無効化するか（falseの場合は検知は継続し、表示のみ切り替え）")]
    [SerializeField] private bool disableDetectionWhenHidden = false;

    private bool isVisible;
    private readonly List<ARPlane> trackedPlanes = new List<ARPlane>();

    #region Unity Lifecycle

    void Awake()
    {
        // ARPlaneManagerが未設定なら自動取得
        if (!planeManager)
        {
            planeManager = FindFirstObjectByType<ARPlaneManager>(FindObjectsInactive.Include);
            Debug.Log($"[ARPlaneVisibilityController] ARPlaneManager auto-detected: {(planeManager != null ? planeManager.name : "NULL")}");
        }
        else
        {
            Debug.Log($"[ARPlaneVisibilityController] ARPlaneManager was set in Inspector: {planeManager.name}");
        }

        if (!planeManager)
        {
            Debug.LogError("[ARPlaneVisibilityController] ARPlaneManager not found! Plane visualization will not work.");
            enabled = false;
            return;
        }

        // Issue #473: ARPlaneManagerの状態を確認
        Debug.Log($"[ARPlaneVisibilityController] ARPlaneManager.enabled={planeManager.enabled}, detectionMode={planeManager.requestedDetectionMode}");

        // 初期状態を設定
        isVisible = showPlanesOnStart;
        Debug.Log($"[ARPlaneVisibilityController] Initial visibility set to: {isVisible}");
    }

    void Start()
    {
        // Issue #473: 起動後の状態を確認
        StartCoroutine(CheckPlaneDetectionAfterStartup());
    }

    System.Collections.IEnumerator CheckPlaneDetectionAfterStartup()
    {
        // ARSession起動を待つ
        yield return new WaitForSeconds(2.0f);

        if (!planeManager)
        {
            Debug.LogError("[ARPlaneVisibilityController] ARPlaneManager is NULL after startup!");
            yield break;
        }

        int planeCount = 0;
        foreach (var plane in planeManager.trackables)
        {
            planeCount++;
        }

        bool hasSubsystem = planeManager.subsystem != null;
        bool subsystemRunning = hasSubsystem && planeManager.subsystem.running;

        Debug.Log($"[ARPlaneVisibilityController] Status after 2s: " +
                  $"enabled={planeManager.enabled}, " +
                  $"subsystem={hasSubsystem}, running={subsystemRunning}, " +
                  $"trackedPlanes={planeCount}, isVisible={isVisible}");

        // 5秒後に再チェック
        yield return new WaitForSeconds(3.0f);

        planeCount = 0;
        foreach (var plane in planeManager.trackables)
        {
            planeCount++;
        }

        Debug.Log($"[ARPlaneVisibilityController] Status after 5s: trackedPlanes={planeCount}");
    }

    void OnEnable()
    {
        if (planeManager)
        {
            // 平面の追加/更新/削除イベントを購読
            planeManager.planesChanged += OnPlanesChanged;
        }

        // 既存の平面に初期状態を適用
        ApplyVisibilityToAllPlanes();
    }

    void OnDisable()
    {
        if (planeManager)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    #endregion

    #region Public API

    /// <summary>
    /// 平面プレートの表示状態を設定します
    /// UI Toggle/Checkboxの OnValueChanged から呼び出してください
    /// </summary>
    /// <param name="visible">true: 表示, false: 非表示</param>
    public void SetPlanesVisible(bool visible)
    {
        Debug.Log($"[ARPlaneVisibilityController] SetPlanesVisible called: {visible}");

        if (isVisible == visible)
        {
            Debug.Log($"[ARPlaneVisibilityController] Already in state: {visible}, skipping.");
            return;
        }

        isVisible = visible;

        // 平面検知自体のON/OFF
        if (disableDetectionWhenHidden && planeManager)
        {
            planeManager.enabled = visible;
            Debug.Log($"[ARPlaneVisibilityController] ARPlaneManager.enabled = {visible}");
        }

        // 既存の全平面に適用
        ApplyVisibilityToAllPlanes();
    }

    /// <summary>
    /// 現在の表示状態を取得します
    /// </summary>
    public bool IsPlanesVisible => isVisible;

    /// <summary>
    /// 平面プレートの表示/非表示を切り替えます
    /// </summary>
    public void TogglePlanesVisibility()
    {
        SetPlanesVisible(!isVisible);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 平面の変更イベントハンドラー
    /// </summary>
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // 新しく追加された平面に表示状態を適用
        if (args.added != null)
        {
            foreach (var plane in args.added)
            {
                ApplyVisibilityToPlane(plane, isVisible);
            }
        }

        // 更新された平面にも適用（念のため）
        if (args.updated != null)
        {
            foreach (var plane in args.updated)
            {
                ApplyVisibilityToPlane(plane, isVisible);
            }
        }
    }

    /// <summary>
    /// すべての検知済み平面に表示状態を適用
    /// </summary>
    private void ApplyVisibilityToAllPlanes()
    {
        if (!planeManager)
        {
            Debug.LogWarning("[ARPlaneVisibilityController] PlaneManager is null!");
            return;
        }

        // 現在追跡中のすべての平面を取得
        trackedPlanes.Clear();
        foreach (var plane in planeManager.trackables)
        {
            trackedPlanes.Add(plane);
        }

        Debug.Log($"[ARPlaneVisibilityController] Applying visibility to {trackedPlanes.Count} planes. Visible: {isVisible}");

        // 各平面に表示状態を適用
        foreach (var plane in trackedPlanes)
        {
            ApplyVisibilityToPlane(plane, isVisible);
        }
    }

    /// <summary>
    /// 個別の平面に表示状態を適用
    /// </summary>
    private void ApplyVisibilityToPlane(ARPlane plane, bool visible)
    {
        if (!plane)
            return;

        int componentsFound = 0;

        // 1. すべてのRendererを制御（MeshRenderer, LineRenderer等）
        var renderers = plane.GetComponentsInChildren<Renderer>(true);
        foreach (var renderer in renderers)
        {
            if (renderer)
            {
                renderer.enabled = visible;
                componentsFound++;
            }
        }

        // 2. ARPlaneMeshVisualizerを制御（AR Foundation標準コンポーネント）
        var meshVisualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
        if (meshVisualizer)
        {
            meshVisualizer.enabled = visible;
            componentsFound++;
        }

        // 3. CanvasやCanvasRendererを制御（UI要素として表示されている場合）
        var canvasRenderers = plane.GetComponentsInChildren<CanvasRenderer>(true);
        foreach (var canvasRenderer in canvasRenderers)
        {
            if (canvasRenderer)
            {
                canvasRenderer.gameObject.SetActive(visible);
                componentsFound++;
            }
        }

        // 4. 最も確実な方法: GameObjectの階層を制御
        // ARPlaneのビジュアル子オブジェクトを直接ON/OFF
        Transform visualTransform = plane.transform.Find("Visual") ??
                                     plane.transform.Find("Mesh") ??
                                     plane.transform.Find("Plane Mesh");

        if (visualTransform != null)
        {
            visualTransform.gameObject.SetActive(visible);
            componentsFound++;
        }
        else
        {
            // 名前が不明な場合、すべての子オブジェクトを制御
            foreach (Transform child in plane.transform)
            {
                // ARPlane自体の機能を壊さないよう、特定の名前以外を制御
                if (!child.name.Contains("Anchor") && !child.name.Contains("Tracking"))
                {
                    child.gameObject.SetActive(visible);
                    componentsFound++;
                }
            }
        }

        Debug.Log($"[ARPlaneVisibilityController] Plane '{plane.name}': Controlled {componentsFound} components/objects. Visible: {visible}");
    }

    #endregion

#if UNITY_EDITOR
    void OnValidate()
    {
        // Editor上でパラメータ変更時、実行中なら即座に反映
        if (Application.isPlaying && planeManager)
        {
            ApplyVisibilityToAllPlanes();
        }
    }
#endif
}
