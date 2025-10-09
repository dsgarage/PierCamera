
using Cysharp.Threading.Tasks;

public class ActiveSettingPosingAnim : IAnimation
{
    public ActiveSettingPosingAnim() { }

    public async UniTask PlayAsync()
    {
        await UniTask.Yield();
    }
}
