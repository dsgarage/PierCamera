using Cysharp.Threading.Tasks;
using System;

/// <summary>
/// アクションの基底クラス
/// </summary>
[Serializable]
public abstract class ActionBase : IAction
{
	public bool IsRunning { get; private set; }
	public event Action<IAction> OnCompleted;
	public async UniTask ExecuteAsync()
	{
		IsRunning = true;
		await Execute();
		IsRunning = false;
		OnCompleted?.Invoke(this);
	}
	protected abstract UniTask Execute();
}