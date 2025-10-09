using System.ComponentModel;
using Unity.Collections;
using UnityEngine;

public class AppMgr : MonoBehaviour
{
    public enum AppState
    {
        None = 0,       // 実行していない
        PhotoGraphy     // 撮影中
    }

    // インスタンス
	public static AppMgr instance;

    [SerializeField, ReadOnly]
    private AppState appState = AppState.None;

    public AppState State
    {
        get { return appState; }
        set { appState = value; }
    }

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


    void Start()
    {
        // 写真撮影に遷移
        State = AppState.PhotoGraphy;
        // メイン画面に遷移
        UIMgr.instance.State = UIMgr.UIState.Home;
    }


    void Update()
    {

    }
}
