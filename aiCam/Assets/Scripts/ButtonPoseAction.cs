using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class ButtonPoseAction : MonoBehaviour
{
    [Header("Animator")] public Animator animator;
    [Min(0)] public int layerIndex = 2;
    [Min(0f)] public float crossFadeTime = 0.1f;

    [Header("State")]
    public string statePath;
    public AnimationClip clip;

    private Button cachedButton;

    private void Awake()
    {
        cachedButton = GetComponent<Button>();
        if (cachedButton != null)
        {
            cachedButton.onClick.RemoveListener(Apply);
            cachedButton.onClick.AddListener(Apply);
        }
    }

    private void OnEnable()
    {
        if (cachedButton == null)
        {
            cachedButton = GetComponent<Button>();
            if (cachedButton != null)
            {
                cachedButton.onClick.RemoveListener(Apply);
                cachedButton.onClick.AddListener(Apply);
            }
        }
    }

    public void Configure(Animator animator, string statePath, AnimationClip clip, float crossFadeTime, int layerIndex)
    {
        this.animator = animator;
        this.statePath = statePath;
        this.clip = clip;
        if (crossFadeTime >= 0f) this.crossFadeTime = crossFadeTime;
        this.layerIndex = Mathf.Max(0, layerIndex);
    }

    public void Apply()
    {
        var targetAnimator = animator;
        if (!targetAnimator)
        {
            Debug.LogWarning($"[{nameof(ButtonPoseAction)}] Animator is null on {name}.");
            return;
        }

        if (targetAnimator.layerCount == 0)
        {
            Debug.LogWarning($"[{nameof(ButtonPoseAction)}] Animator {targetAnimator} has no layers.", targetAnimator);
            return;
        }

        int layer = Mathf.Clamp(layerIndex, 0, targetAnimator.layerCount - 1);

        string resolvedPath = ResolveStatePath(targetAnimator, layer);
        if (string.IsNullOrEmpty(resolvedPath))
        {
            Debug.LogWarning($"[{nameof(ButtonPoseAction)}] State path is empty on {name}.");
            return;
        }

        int stateHash = Animator.StringToHash(resolvedPath);
        if (!targetAnimator.HasState(layer, stateHash))
        {
            string fallback = BuildFallbackStatePath(targetAnimator, layer, resolvedPath);
            if (!string.IsNullOrEmpty(fallback))
            {
                stateHash = Animator.StringToHash(fallback);
            }

            if (!targetAnimator.HasState(layer, stateHash))
            {
                Debug.LogWarning($"[{nameof(ButtonPoseAction)}] State '{resolvedPath}' not found on layer {layer} of {targetAnimator}.", targetAnimator);
                return;
            }
        }

        targetAnimator.CrossFade(stateHash, crossFadeTime, layer);
    }

    private string ResolveStatePath(Animator animator, int layer)
    {
        string path = statePath;
        if (string.IsNullOrEmpty(path))
        {
            path = clip ? clip.name : string.Empty;
        }
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        path = path.Replace('/', '.');

        if (!path.Contains("."))
        {
            string layerName = SafeGetLayerName(animator, layer);
            if (!string.IsNullOrEmpty(layerName))
            {
                path = $"{layerName}.{path}";
            }
        }

        return path;
    }

    private string BuildFallbackStatePath(Animator animator, int layer, string original)
    {
        string layerName = SafeGetLayerName(animator, layer);
        if (!string.IsNullOrEmpty(layerName) && original.StartsWith(layerName + "."))
        {
            string withoutLayer = original.Substring(layerName.Length + 1);
            if (!string.IsNullOrEmpty(withoutLayer))
            {
                return $"{layerName}.{withoutLayer.Replace('/', '.')}";
            }
        }

        return original.Replace('/', '.');
    }

    private static string SafeGetLayerName(Animator animator, int layer)
    {
        if (!animator) return string.Empty;
        if (layer < 0 || layer >= animator.layerCount) return string.Empty;
        return animator.GetLayerName(layer);
    }
}
