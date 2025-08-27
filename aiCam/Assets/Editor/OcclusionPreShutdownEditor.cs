// aiCam / OcclusionPreShutdownEditor.cs
// Editor only - 再生終了直前にオクルージョンを落とし、例外ログを抑えるユーティリティ。
#if UNITY_EDITOR
#nullable enable
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

[InitializeOnLoad]
static class OcclusionPreShutdownEditor
{
    static OcclusionPreShutdownEditor()
    {
        EditorApplication.playModeStateChanged += OnStateChanged;
    }

    private static void OnStateChanged(PlayModeStateChange c)
    {
        if (c != PlayModeStateChange.ExitingPlayMode) return;

        // 現在のシーンから全ての OcclusionManager を見つけて先に停止する
        try
        {
            foreach (var occ in Object.FindObjectsByType<AROcclusionManager>(FindObjectsSortMode.None))
            {
                if (!occ) continue;
                try
                {
                    // 直接書き換え（OcclusionToggle を持っていればそれを呼び出す）
                    var toggle = occ.GetComponent<OcclusionToggle>();
                    if (toggle) toggle.DisableDepthNow();
                    else
                    {
                        occ.requestedEnvironmentDepthMode    = UnityEngine.XR.ARSubsystems.EnvironmentDepthMode.Disabled;
                        occ.requestedHumanDepthMode          = UnityEngine.XR.ARSubsystems.HumanSegmentationDepthMode.Disabled;
                        occ.requestedHumanStencilMode        = UnityEngine.XR.ARSubsystems.HumanSegmentationStencilMode.Disabled;
                        occ.requestedOcclusionPreferenceMode = UnityEngine.XR.ARSubsystems.OcclusionPreferenceMode.NoOcclusion;
                    }
                }
                catch { /* ignore per component */ }
            }
        }
        catch { /* ignore */ }
    }
}
#endif
