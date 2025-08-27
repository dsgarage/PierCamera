// aiCam / PlaceOnPlane.cs
// Unity 6000.1.11f1 / AR Foundation 6.x
// ----------------------------------------------------------------------------
// 平面検出にヒットした場所へプレハブを一度だけ配置し、必要に応じて平面検出を停止します。
// 既存の公開API・クラス名は維持しているため、インスペクタの参照はそのまま動作します。
// ----------------------------------------------------------------------------

#nullable enable
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// タップ位置の平面にプレハブを1回だけ配置するミニコンポーネント。
/// 配置後は <see cref="ARPlaneManager"/> を停止して撮影時のFPSを安定化できます。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ARRaycastManager))]
public sealed class PlaceOnPlane : MonoBehaviour
{
    [Header("Placement")]
    [Tooltip("タップで配置するプレハブ。必須")]
    [SerializeField] private GameObject? placedPrefab;

    [Tooltip("配置後に停止する対象の ARPlaneManager（任意）")]
    [SerializeField] private ARPlaneManager? planeManager;

    // 共有の一時バッファ（毎フレーム確保を避ける）
    private static readonly List<ARRaycastHit> s_Hits = new();

    // 依存コンポーネント
    private ARRaycastManager _raycastManager = default!;

    // 生成済みオブジェクト（2回目以降の配置を抑止）
    private GameObject? _placedInstance;

    /// <summary>初回配置時に発火します。</summary>
    public event Action? OnPlaced;

    private void Awake()
    {
        _raycastManager = GetComponent<ARRaycastManager>();
        if (!placedPrefab)
            Debug.LogWarning("[PlaceOnPlane] placedPrefab is not assigned.");
    }

    private void Update()
    {
        // 既に配置済みなら何もしない
        if (_placedInstance != null) return;

        // タッチ開始のみ受け付ける
        if (Input.touchCount == 0) return;
        var t = Input.GetTouch(0);
        if (t.phase != TouchPhase.Began) return;

        // 画面座標→AR空間へレイキャスト（ポリゴン内ヒットのみ）
        if (_raycastManager.Raycast(t.position, s_Hits, TrackableType.PlaneWithinPolygon))
        {
            var hit  = s_Hits[0];
            var pose = hit.pose;

            // アンカーに子付けして安定化（Plane が一時的に消えても位置が保たれる）
            ARAnchor? anchor = null;
            if (planeManager && hit.trackable is ARPlane plane)
            {
                var anchorMgr = planeManager.GetComponent<ARAnchorManager>();
                if (anchorMgr)
                {
                    anchor = anchorMgr.AttachAnchor(plane, pose);
                }
            }

            var parent = anchor ? anchor.transform : null;
            _placedInstance = Instantiate(placedPrefab!, pose.position, pose.rotation, parent);

            // 配置後は任意で平面検出を停止（描画・計算コストを削減）
            if (planeManager)
            {
                try
                {
                    planeManager.requestedDetectionMode = PlaneDetectionMode.None;
                    foreach (var p in planeManager.trackables) p.gameObject.SetActive(false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[PlaceOnPlane] Failed to stop plane detection: {e.Message}");
                }
            }

            OnPlaced?.Invoke();
        }
    }
}
