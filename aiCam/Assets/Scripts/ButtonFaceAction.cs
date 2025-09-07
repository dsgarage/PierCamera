using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class ButtonFaceAction : MonoBehaviour
{
    public FaceController controller;
    public string faceName;
    [Min(0f)] public float crossFadeTime = 0.05f;
    public bool keep = true;

    void Awake()
    {
        var btn = GetComponent<Button>();
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(Apply);
    }

    public void Apply()
    {
        if (controller && !string.IsNullOrEmpty(faceName))
            controller.SetFace(faceName, keep, crossFadeTime);
    }
}