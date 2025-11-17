using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

namespace AICam.Animation
{
    /// <summary>
    /// アニメーションの再生管理クラス
    /// </summary>
    public class AnimationSequence
    {
        private readonly List<IAnimation> animations = new();
        public AnimationSequence Add(IAnimation animation)
        {
            animations.Add(animation);
            return this;
        }
        // 直列（順番）再生
        public async UniTask PlaySequentialAsync()
        {
            foreach (var anim in animations)
            {
                await anim.PlayAsync();
            }
        }
        // 並列（一斉）再生
        public async UniTask PlayParallelAsync()
        {
            await UniTask.WhenAll(animations.Select(a => a.PlayAsync()));
        }
    }
}