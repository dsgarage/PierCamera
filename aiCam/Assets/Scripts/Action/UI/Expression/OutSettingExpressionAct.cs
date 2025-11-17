using Cysharp.Threading.Tasks;
using UnityEngine;
using AICam.Animation;

public class OutSettingExpressionAct : ActionBase
{
    RectTransform rect;
    public OutSettingExpressionAct(RectTransform rect)
    {
        this.rect = rect;
    }

    protected override async UniTask Execute()
    {
        // ホーム状態に遷移
        UIMgr.instance.State = UIMgr.UIState.Home;

        ////////////////////////
        ///// アニメーション /////
        ////////////////////////
        AnimationSequence animSeq = new AnimationSequence();

        animSeq.Add(new OutSettingExpressionAnim(rect));

        await animSeq.PlayParallelAsync(); // アニメーション実行

        // 次フレームまで待機
        await UniTask.Yield();
    }
}
