using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
public class ActionMgr : MonoBehaviour
{
    // インスタンス
    public static ActionMgr instance;
    [SerializeReference]
    private List<ActionBase> activeActionList;
    private void Awake()
    {
        // インスタンス生成
        CreateInstance();
    }
    // インスタンスを作成
    public bool CreateInstance()
    {
        // 既にインスタンスが作成されていなければ作成する
        if (!instance)
        {
            // 作成
            instance = this;
        }
        // インスタンスが作成済みなら終了
        if (instance) { return true; }
        Debug.LogError($"{this}のインスタンスが生成できませんでした");
        return false;
    }

    public T ActivateAction<T>(params object[] args) where T : ActionBase
    {
        var action = (T)Activator.CreateInstance(typeof(T), args);  // Action を動的に生成
        Debug.Log($"{typeof(T).Name} を実行しました");
        activeActionList.Add(action);                                   // 追加
        action.OnCompleted += _ => activeActionList.Remove(action);     // 終了時にリストから削除
        action.ExecuteAsync().Forget();                                 // 非同期実行
        return action;
    }
    public async UniTask WaitForAction(ActionBase action)
    {
        // nullチェック（無効な能力の場合は警告を出して処理を終了）
        if (action == null)
        {
            Debug.LogWarning($"{nameof(WaitForAction)}の引数(action)でNULLが検知されましたので処理を終了しました。");
            return;
        }
        // 既に完了しているならはじく
        if (!action.IsRunning) return;
        // 完了を待つための非同期タスクを作成
        var tcs = new UniTaskCompletionSource();
        // アクション完了時にタスクを完了させる
        action.OnCompleted += _ => tcs.TrySetResult();
        // アクションが終了するまで待機
        await tcs.Task;
    }

    /////////////////
    ///// ボタン /////
    /////////////////

    /// <summary>
    /// アバターポーズ設定画面を開くアクションを起動
    /// </summary>
    /// <param name="rect"></param>
    public void CreateExeAct_OnPosing(RectTransform rect)
    {
        ActivateAction<OnSettingPosingAct>(rect);
    }

    /// <summary>
    /// アバターポーズ設定画面を閉じるアクションを起動
    /// </summary>
    /// <param name="rect"></param>
    public void CreateExeAct_OutPosing(RectTransform rect)
    {
        ActivateAction<OutSettingPosingAct>(rect);
    }

    /// <summary>
    /// アバター表情設定画面を開くアクションを起動
    /// </summary>
    /// <param name="rect"></param>
    public void CreateExeAct_OnExpression(RectTransform rect)
    {
        ActivateAction<OnSettingExpressionAct>(rect);
    }

    /// <summary>
    /// アバター表情設定画面を閉じるアクションを起動
    /// </summary>
    /// <param name="rect"></param>
    public void CreateExeAct_OutExpression(RectTransform rect)
    {
        ActivateAction<OutSettingExpressionAct>(rect);
    }
}