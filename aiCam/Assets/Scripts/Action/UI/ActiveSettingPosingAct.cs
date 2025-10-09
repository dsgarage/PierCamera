using Cysharp.Threading.Tasks;

public class ActiveSettingPosingAct : ActionBase
{
    public ActiveSettingPosingAct()
    {

    }

    protected override async UniTask Execute()
    {

        // 次フレームまで待機
        await UniTask.Yield();
    }
}
