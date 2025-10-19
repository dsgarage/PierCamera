using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// オクルージョンを強制的に有効化する簡易版
/// デバッグ用 - 問題の切り分けに使用
/// </summary>
public class AROcclusionForceEnable : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AROcclusionManager occlusion;

    [Header("Settings")]
    [SerializeField] private EnvironmentDepthMode depthMode = EnvironmentDepthMode.Medium;

    void Start()
    {
        Debug.Log("[AROcclusionForceEnable] Start called");

        if (!occlusion)
        {
            occlusion = GetComponent<AROcclusionManager>();
        }

        if (!occlusion)
        {
            Debug.LogError("[AROcclusionForceEnable] AROcclusionManager not found!");
            return;
        }

        // 起動時は無効化
        occlusion.enabled = false;
        Debug.Log("[AROcclusionForceEnable] AROcclusionManager disabled initially");

        // 2秒後に強制有効化
        Invoke(nameof(EnableOcclusion), 2f);
    }

    void EnableOcclusion()
    {
        Debug.Log("[AROcclusionForceEnable] Attempting to enable occlusion...");

        if (!occlusion)
        {
            Debug.LogError("[AROcclusionForceEnable] Occlusion manager is null!");
            return;
        }

        // モード設定
        occlusion.requestedEnvironmentDepthMode = depthMode;
        occlusion.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
        occlusion.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
        occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;

        Debug.Log($"[AROcclusionForceEnable] Modes set - DepthMode: {depthMode}");

        // 有効化
        occlusion.enabled = true;

        Debug.Log("[AROcclusionForceEnable] AROcclusionManager enabled!");

        // 1秒後に状態確認
        Invoke(nameof(CheckStatus), 1f);
    }

    void CheckStatus()
    {
        if (!occlusion)
        {
            Debug.LogError("[AROcclusionForceEnable] Occlusion manager became null!");
            return;
        }

        Debug.Log($"[AROcclusionForceEnable] Status Check:");
        Debug.Log($"  - Enabled: {occlusion.enabled}");
        Debug.Log($"  - Subsystem: {(occlusion.subsystem != null ? "exists" : "null")}");
        Debug.Log($"  - Subsystem Running: {(occlusion.subsystem?.running ?? false)}");
        Debug.Log($"  - Current Env Depth: {occlusion.currentEnvironmentDepthMode}");
        Debug.Log($"  - Requested Env Depth: {occlusion.requestedEnvironmentDepthMode}");
        Debug.Log($"  - Current Preference: {occlusion.currentOcclusionPreferenceMode}");
        Debug.Log($"  - Requested Preference: {occlusion.requestedOcclusionPreferenceMode}");

        // デバイスがサポートしているか確認
        var desc = occlusion.descriptor;
        if (desc != null)
        {
            Debug.Log($"  - Descriptor available: {desc.id}");
            // 実際のサポート状況はCurrentモードで判断
            if (occlusion.currentEnvironmentDepthMode != EnvironmentDepthMode.Disabled)
            {
                Debug.Log("  - Environment Depth is WORKING!");
            }
            else
            {
                Debug.LogWarning("  - Environment Depth is NOT working (disabled or unsupported)");
            }
        }
        else
        {
            Debug.LogWarning("  - Descriptor is not available (device may not support occlusion)");
        }
    }
}
