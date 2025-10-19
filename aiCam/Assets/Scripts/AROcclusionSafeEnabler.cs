// Assets/Scripts/AROcclusionSafeEnabler.cs
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DefaultExecutionOrder(-32000)] // 最優先で実行（AROcclusionManagerより確実に先）
[DisallowMultipleComponent]
[ExecuteAlways] // EditモードとPlayモードの両方で実行
public class AROcclusionSafeEnabler : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private AROcclusionManager occlusion;
    [SerializeField] private ARCameraManager cameraManager; // 参照は任意（存在チェック用）

    [Header("Warmup")]
    [Tooltip("ARSession/サブシステムの起動を待つフレーム数。端末により3〜5程度が安定。")]
    [Range(0, 10)] [SerializeField] private int warmupFrames = 5;

    [Header("Environment Depth (おすすめ)")]
    [SerializeField] private bool useEnvironmentDepth = true;
    [SerializeField] private EnvironmentDepthMode environmentDepthMode = EnvironmentDepthMode.Medium;

    [Header("Human Segmentation (必要な場合のみ)")]
    [SerializeField] private bool useHumanSegmentation = false;
    [SerializeField] private HumanSegmentationDepthMode    humanDepthMode   = HumanSegmentationDepthMode.Fastest;
    [SerializeField] private HumanSegmentationStencilMode  humanStencilMode = HumanSegmentationStencilMode.Fastest;

    [Header("Occlusion Preference")]
    [Tooltip("描画合成の優先度。環境優先/人物優先/無効から選択。")]
    [SerializeField] private OcclusionPreferenceMode preferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;

    private bool isOcclusionEnabled = false;

    void Reset()
    {
        if (!occlusion) occlusion = GetComponent<AROcclusionManager>();
        if (!cameraManager) cameraManager = GetComponent<ARCameraManager>();

#if UNITY_EDITOR
        // Inspector上でコンポーネントが追加された時にAROcclusionManagerを無効化
        if (occlusion != null && !Application.isPlaying)
        {
            occlusion.enabled = false;
            EditorUtility.SetDirty(occlusion);
        }
#endif
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        // Inspector上で値が変更された時、AROcclusionManagerが無効であることを確認
        if (occlusion != null && !Application.isPlaying)
        {
            if (occlusion.enabled)
            {
                occlusion.enabled = false;
                EditorUtility.SetDirty(occlusion);
                Debug.Log("[AROcclusionSafeEnabler] AROcclusionManager was enabled in Editor. Auto-disabled to prevent errors.");
            }
        }
#endif
    }

    void Awake()
    {
        // Editモードでは何もしない
        if (!Application.isPlaying)
            return;

#if UNITY_EDITOR
        // UnityEditor環境ではこのスクリプト自体を無効化
        Debug.LogWarning("[AROcclusionSafeEnabler] Running in Unity Editor. Occlusion management disabled.");
        this.enabled = false;
        return;
#endif

        Debug.Log("[AROcclusionSafeEnabler] Awake called on device");

        // 実機環境でのみ実行
        // AROcclusionManagerは既にInspectorで無効化されているはず
        if (occlusion == null)
        {
            Debug.LogError("[AROcclusionSafeEnabler] Awake: occlusion reference is null!");
            return;
        }

        if (!occlusion.enabled)
        {
            SetAllModesDisabled();
            Debug.Log("[AROcclusionSafeEnabler] Awake: AROcclusionManager was disabled, modes set to Disabled");
        }
        else
        {
            Debug.LogWarning("[AROcclusionSafeEnabler] Awake: AROcclusionManager is already enabled! This may cause issues. Disabling now...");
            occlusion.enabled = false;
            SetAllModesDisabled();
        }
    }

    void OnEnable()
    {
        Debug.Log("[AROcclusionSafeEnabler] OnEnable called");

        // Playモードでのみ実行
        if (!Application.isPlaying)
        {
            Debug.Log("[AROcclusionSafeEnabler] Not in play mode, skipping");
            return;
        }

#if UNITY_EDITOR
        // Editor環境では何もしない
        Debug.Log("[AROcclusionSafeEnabler] In editor, skipping");
        return;
#endif

        Debug.Log("[AROcclusionSafeEnabler] Starting EnableWhenReady coroutine from OnEnable");
        // サブシステムが稼働してから必要モードを要求
        StartCoroutine(EnableWhenReady());
    }

    IEnumerator EnableWhenReady()
    {
        if (occlusion == null)
        {
            Debug.LogError("[AROcclusionSafeEnabler] EnableWhenReady: occlusion is null!");
            yield break;
        }

        Debug.Log("[AROcclusionSafeEnabler] Starting EnableWhenReady coroutine...");

        // ARSessionの初期化を十分に待つ
        yield return new WaitForSeconds(0.5f);

        // 起動直後の競合を避けるため数フレーム待つ
        for (int i = 0; i < Mathf.Max(0, warmupFrames); i++)
            yield return null;

        Debug.Log("[AROcclusionSafeEnabler] Warmup complete. Enabling AROcclusionManager to start subsystem...");

        // まずAROcclusionManagerを有効化してサブシステムを起動
        SetAllModesDisabled();
        yield return null;

        occlusion.enabled = true;
        isOcclusionEnabled = true;
        Debug.Log("[AROcclusionSafeEnabler] AROcclusionManager enabled, waiting for subsystem...");
        yield return null;

        // サブシステムの準備を確認
        int retryCount = 0;
        const int maxRetries = 30; // リトライ回数を増やす

        while (retryCount < maxRetries)
        {
            bool subsystemExists = occlusion.subsystem != null;
            bool subsystemRunning = subsystemExists && occlusion.subsystem.running;

            Debug.Log($"[AROcclusionSafeEnabler] Retry {retryCount}/{maxRetries}: subsystem={subsystemExists}, running={subsystemRunning}");

            if (subsystemExists && subsystemRunning)
            {
                // サブシステムが稼働したら設定を適用
                Debug.Log("[AROcclusionSafeEnabler] Subsystem is ready! Applying occlusion modes...");

                // 必要なモードを適用
                ApplyRequestedModes();
                Debug.Log($"[AROcclusionSafeEnabler] Occlusion modes applied - EnvDepth: {occlusion.currentEnvironmentDepthMode}, Preference: {occlusion.currentOcclusionPreferenceMode}");
                yield break;
            }

            retryCount++;
            yield return new WaitForSeconds(0.1f);
        }

        // タイムアウト: サブシステムが起動しなかった
        Debug.LogWarning("[AROcclusionSafeEnabler] Occlusion subsystem did not start. Device may not support occlusion.");
        Debug.LogWarning("[AROcclusionSafeEnabler] Disabling AROcclusionManager...");
        occlusion.enabled = false;
        isOcclusionEnabled = false;
    }

    void OnDisable()
    {
        // 安全に無効化
        if (occlusion != null && isOcclusionEnabled)
        {
            try
            {
                SetAllModesDisabled();
                occlusion.enabled = false;
                isOcclusionEnabled = false;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AROcclusionSafeEnabler] Error during OnDisable: {e.Message}");
            }
        }
    }

    void OnDestroy()
    {
        // クリーンアップ
        if (occlusion != null && isOcclusionEnabled)
        {
            try
            {
                SetAllModesDisabled();
                occlusion.enabled = false;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AROcclusionSafeEnabler] Error during cleanup: {e.Message}");
            }
        }
    }

    // ================= Helpers =================

    void SetAllModesDisabled()
    {
        occlusion.requestedEnvironmentDepthMode = EnvironmentDepthMode.Disabled;
        occlusion.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
        occlusion.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
        occlusion.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.NoOcclusion;
    }

    void ApplyRequestedModes()
    {
        // Environment Depth
        occlusion.requestedEnvironmentDepthMode = useEnvironmentDepth ? environmentDepthMode
                                                                      : EnvironmentDepthMode.Disabled;

        // Human Segmentation
        if (useHumanSegmentation)
        {
            occlusion.requestedHumanDepthMode   = humanDepthMode;
            occlusion.requestedHumanStencilMode = humanStencilMode;
        }
        else
        {
            occlusion.requestedHumanDepthMode   = HumanSegmentationDepthMode.Disabled;
            occlusion.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;
        }

        // Preference（合成優先度）
        occlusion.requestedOcclusionPreferenceMode = useEnvironmentDepth || useHumanSegmentation
            ? preferenceMode
            : OcclusionPreferenceMode.NoOcclusion;
    }
}
