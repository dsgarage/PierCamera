using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ExpressionSlot : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Sprite icon;    // サムネを表示する Image

    [SerializeField] private TMP_Text tmpText;

    [SerializeField] private GameObject selected; // 任意：選択ハイライト

    private AnimationClip clip;
    private int index;
    private string label;

    /// <summary>
    /// thumbnail: null 可 / label: null または空なら clip.name を使う
    /// </summary>
    public void Bind(AnimationClip clip, int index, Sprite thumbnail = null, string label = null)
    {
        this.clip = clip;
        this.index = index;
        this.label = string.IsNullOrEmpty(label) ? (clip ? clip.name : "(None)") : label;

        Image image = GetComponent<Image>();
        if(image) image.sprite = thumbnail;

        icon = thumbnail;

        if (icon)
        {
            tmpText.text = "";
            return;
        }

        tmpText.text = $"{index + 1}.\n{this.label}";
    }

    public void SetSelected(bool on) { if (selected) selected.SetActive(on); }

    public void OnClick()
    {
        Debug.Log($"Pose clicked: {index} - {clip?.name}");
        // TODO: ここでアバターへ適用・選択制御など
        SetSelected(true);
    }
}