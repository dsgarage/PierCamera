using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Animator))]
public class FaceController : MonoBehaviour
{
    [Header("表情アニメーション（Clip名=ステート名に合わせる）")]
    [SerializeField] private AnimationClip[] faceClips;

    [Header("表情用Animatorレイヤー")]
    [SerializeField, Min(0)]
    private int faceLayerIndex = 0;

    [Header("フェードアウト速度（秒^-1）")]
    [SerializeField, Min(0f)] private float fadeOutSpeed = 5f;

    public bool keepFace = false;

    private Animator animator;
    private readonly Dictionary<string, int> stateHashByName = new();
    private float layerWeight = 0f;

    // UI自動生成用：表情名一覧
    private List<string> _namesCache;
    public IReadOnlyList<string> FaceNames {
        get {
            _namesCache ??= BuildNames();
            return _namesCache;
        }
    }
    private List<string> BuildNames() {
        var list = new List<string>();
        foreach (var clip in faceClips) if (clip) list.Add(clip.name);
        return list;
    }

    private void Awake()
    {
        animator = GetComponent<Animator>();

        var layerName = animator.GetLayerName(faceLayerIndex); // 例: "Face"
        stateHashByName.Clear();

        foreach (var clip in faceClips)
        {
            if (!clip) continue;

            // ★レイヤー名込みのフルパスでハッシュ化（同名衝突/別レイヤー対策）
            // サブステートマシンに置いているなら "Face.SubSM/StateName" のように
            // Animator 上の実パスに合わせてください。
            string statePath = $"{layerName}.{clip.name}";
            int hash = Animator.StringToHash(statePath);

            stateHashByName[clip.name] = hash;
        }
    }

    private void Update()
    {
        if (!keepFace && layerWeight > 0f)
        {
            layerWeight = Mathf.Max(0f, layerWeight - fadeOutSpeed * Time.deltaTime);
            animator.SetLayerWeight(faceLayerIndex, layerWeight);
        }
    }

    public void SetFace(string faceName, bool keep = true, float crossFadeTime = 0.05f)
    {
        if (string.IsNullOrEmpty(faceName)) return;
        if (!stateHashByName.TryGetValue(faceName, out var hash))
        {
            Debug.LogWarning($"[FaceController] face '{faceName}' not found.");
            return;
        }
        if (!animator.HasState(faceLayerIndex, hash))
        {
            Debug.LogWarning($"[FaceController] state '{faceName}' not found on layer {faceLayerIndex}. " +
                             $"Check layer index/name and state path.");
            return;
        }

        keepFace = keep;
        layerWeight = 1f;
        animator.SetLayerWeight(faceLayerIndex, layerWeight);
        animator.CrossFade(hash, crossFadeTime, faceLayerIndex);
    }

    // アニメーションイベントから呼ぶ場合に使用可
    public void OnCallChangeFace(string faceName)
        => SetFace(faceName, keep: true, crossFadeTime: 0.05f);

    public void ReleaseFace() => keepFace = false;

    public void ClearFace()
    {
        keepFace = false;
        layerWeight = 0f;
        animator.SetLayerWeight(faceLayerIndex, 0f);
    }
}