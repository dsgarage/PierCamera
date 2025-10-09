using Cysharp.Threading.Tasks;
using UnityEngine;

public class OnSettingExpressionAct : ActionBase
{
    RectTransform rect;
    public OnSettingExpressionAct(RectTransform rect)
    {
        this.rect = rect;
    }

    protected override async UniTask Execute()
    {
        // アバター表情設定状態に遷移
        UIMgr.instance.State = UIMgr.UIState.Expression;

        ////////////////////////
        ///// アニメーション /////
        ////////////////////////
        AnimationSequence animSeq = new AnimationSequence();

        animSeq.Add(new OnSettingExpressionAnim(rect));

        await animSeq.PlayParallelAsync(); // アニメーション実行

        // 次フレームまで待機
        await UniTask.Yield();
    }
}
