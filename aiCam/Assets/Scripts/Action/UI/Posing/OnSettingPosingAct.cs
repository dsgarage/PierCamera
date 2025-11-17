using Cysharp.Threading.Tasks;
using UnityEngine;
using AICam.Animation;

public class OnSettingPosingAct : ActionBase
{
    RectTransform rect;
    public OnSettingPosingAct(RectTransform rect)
    {
        this.rect = rect;
    }

    protected override async UniTask Execute()
    {
        // アバターポーズ設定状態に遷移
        UIMgr.instance.State = UIMgr.UIState.Posing;

        ////////////////////////
        ///// アニメーション /////
        ////////////////////////
        AnimationSequence animSeq = new AnimationSequence();

        animSeq.Add(new OnSettingPosingAnim(rect));

        await animSeq.PlayParallelAsync(); // アニメーション実行

        // 次フレームまで待機
        await UniTask.Yield();
    }
}
