using UnityEngine;
using DG.Tweening;
using Cysharp.Threading.Tasks;

namespace AICam.Animation
{
    public class OnSettingPosingAnim : IAnimation
{
    readonly RectTransform target;
    readonly float startY;
    readonly float endY;
    readonly float duration;
    readonly Ease ease;
    readonly bool ignoreTimeScale;

    Tween tween;

    /// <param name="target">動かすRectTransform</param>
    /// <param name="duration">秒数</param>
    /// <param name="startY">開始Y</param>
    /// <param name="endY">終了Y</param>
    /// <param name="ease">補間</param>
    /// <param name="ignoreTimeScale">UIならtrue推奨</param>
    public OnSettingPosingAnim(
        RectTransform target,
        float duration = 0.6f,
        float startY = -454.66f,
        float endY   = 454.66f,
        Ease ease = Ease.InOutQuad,
        bool ignoreTimeScale = true)
    {
        this.target = target;
        this.duration = duration;
        this.startY = startY;
        this.endY = endY;
        this.ease = ease;
        this.ignoreTimeScale = ignoreTimeScale;
    }

    public async UniTask PlayAsync()
    {
        if (!target) return;

        // 開始位置を保証
        var pos = target.anchoredPosition;
        target.anchoredPosition = new Vector2(pos.x, startY);

        // 前回のトゥイーンを掃除
        tween?.Kill();

        tween = target
            .DOAnchorPosY(endY, duration)
            .SetEase(ease)
            .SetUpdate(ignoreTimeScale);

        try
        {
            await tween.AsyncWaitForCompletion();
        }
        finally
        {
            tween = null;
        }
    }
    }
}
