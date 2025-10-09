using UnityEngine;

public class UIMgr : MonoBehaviour
{
    public enum UIState
    {
        None,           // 実行していない
        Home,           // ホーム画面
        Posing,         // アバターポーズ設定画面
        Expression      // アバター表情設定画面
    }

    // インスタンス
    public static UIMgr instance;

    [SerializeField, ReadOnly]
    private UIState uiState = UIState.None;

    public UIState State
    {
        get { return uiState; }
        set { uiState = value; }
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

    }

    void Update()
    {

    }


    // 指定UIの遷移処理
    public void SetActiveUI(UIState uIState)
    {
        State = uiState;

        switch (State)
        {
            case UIState.Posing:        // アバターポーズ設定画面
                
                break;
            case UIState.Expression:    // アバター表情設定画面
                break;
            default:
                break;
        }

        return;
    }
}
