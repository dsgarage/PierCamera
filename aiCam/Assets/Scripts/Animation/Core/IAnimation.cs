using Cysharp.Threading.Tasks;

/// <summary>
/// アニメーションのインターフェースクラス
/// </summary>
public interface IAnimation
{
    UniTask PlayAsync();
}