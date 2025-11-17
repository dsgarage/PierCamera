using Cysharp.Threading.Tasks;

namespace AICam.Animation
{
    /// <summary>
    /// アニメーションのインターフェースクラス
    /// </summary>
    public interface IAnimation
    {
        UniTask PlayAsync();
    }
}