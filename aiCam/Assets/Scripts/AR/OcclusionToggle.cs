// aiCam / OcclusionToggle.cs
// Unity 6000.1.11f1 / AR Foundation 6.x
// ----------------------------------------------------------------------------
// AROcclusionManager の環境深度/人物セグメンテーション等を安全に ON/OFF 切り替え。
// アプリ終了やシーン切替のタイミングでの例外・レース条件を避ける防御的実装。
// 既存クラス名・公開メソッドは維持。
// ----------------------------------------------------------------------------

#nullable enable
using System;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

[DisallowMultipleComponent]
[RequireComponent(typeof(AROcclusionManager))]
public sealed class OcclusionToggle : MonoBehaviour
{
    [Header("References")]
    [Tooltip("対象の AROcclusionManager。未設定時は同じ GameObject から取得")]
    [SerializeField] private AROcclusionManager? occ;

    [Header("Settings")]
    [Tooltip("ON 時に要求する EnvironmentDepth の品質")]
    [SerializeField] private EnvironmentDepthMode depthWhenOn = EnvironmentDepthMode.Medium;

    // 再開時に戻したい値（DisableDepthNow で Disabled を記録）
    private EnvironmentDepthMode _lastRequested = EnvironmentDepthMode.Disabled;

    // 終了前に明示的に無効化したフラグ（OnDisable の二重停止を緩和）
    private bool _preDisabled;

    private void Awake()
    {
        if (!occ) occ = GetComponent<AROcclusionManager>();
        CacheCurrentModes();
    }

    private void OnEnable()
    {
        // 有効化時に以前の要求値へ復帰
        TrySetAll(
            env: _lastRequested,
            humanStencil: HumanSegmentationStencilMode.Fastest,
            humanDepth: HumanSegmentationDepthMode.Fastest,
            pref: OcclusionPreferenceMode.PreferEnvironmentOcclusion);
        _preDisabled = false;
    }

    private void OnDisable()
    {
        // PlayMode 終了間際などで Null 例外になりやすい箇所は広めに try
        if (_preDisabled) return;
        try { SafeDisableModes(); } catch { /* ignore */ }
    }

    /// <summary>現在の EnvironmentDepth 要求モードを返します。</summary>
    public EnvironmentDepthMode CurrentDepthMode =>
        occ ? occ.requestedEnvironmentDepthMode : EnvironmentDepthMode.Disabled;

    /// <summary>オクルージョンを有効化（環境深度＋人物）</summary>
    public void EnableDepth()
    {
        _lastRequested = depthWhenOn;
        TrySetAll(
            env: depthWhenOn,
            humanStencil: HumanSegmentationStencilMode.Fastest,
            humanDepth: HumanSegmentationDepthMode.Fastest,
            pref: OcclusionPreferenceMode.PreferEnvironmentOcclusion);
        _preDisabled = false;
    }

    /// <summary>オクルージョンを完全無効化（深度／人物／優先度）</summary>
    public void DisableDepth()
    {
        _lastRequested = EnvironmentDepthMode.Disabled;
        try { SafeDisableModes(); } catch (Exception e) { Debug.LogWarning(e.Message); }
        _preDisabled = false;
    }

    /// <summary>
    /// ゲーム終了直前など「今すぐ」確実に落としたい場合に呼び出してください。
    /// OnDisable の二重停止を避けるため内部フラグを立てます。
    /// </summary>
    public void DisableDepthNow()
    {
        _lastRequested = EnvironmentDepthMode.Disabled;
        try { SafeDisableModes(); } catch { /* ignore */ }
        _preDisabled = true;
    }

    private void CacheCurrentModes()
    {
        if (!occ) return;
        _lastRequested = occ.requestedEnvironmentDepthMode;
    }

    private void TrySetAll(
        EnvironmentDepthMode env,
        HumanSegmentationStencilMode humanStencil,
        HumanSegmentationDepthMode humanDepth,
        OcclusionPreferenceMode pref)
    {
        if (!occ) return;
        try
        {
            var d = occ.descriptor; // null の場合はサポートなし
            if (d == null) return;

            occ.requestedEnvironmentDepthMode    = env;
            occ.requestedHumanStencilMode        = humanStencil;
            occ.requestedHumanDepthMode          = humanDepth;
            occ.requestedOcclusionPreferenceMode = pref;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OcclusionToggle] SetAll failed: {e.Message}");
        }
    }

    private void SafeDisableModes()
    {
        if (!occ) return;
        try
        {
            var d = occ.descriptor;
            if (d == null) return;

            occ.requestedEnvironmentDepthMode    = EnvironmentDepthMode.Disabled;
            occ.requestedHumanDepthMode          = HumanSegmentationDepthMode.Disabled;
            occ.requestedHumanStencilMode        = HumanSegmentationStencilMode.Disabled;
            occ.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[OcclusionToggle] Disable failed: {e.Message}");
        }
    }
}
