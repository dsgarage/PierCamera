using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
public class CanvasScalerLinker : MonoBehaviour
{
    public CanvasScaler master; // 参照元（UISafeCanvas 側など）
    CanvasScaler self;

    void OnEnable()
    {
        self = GetComponent<CanvasScaler>();
        if (!self) self = gameObject.AddComponent<CanvasScaler>();
        Apply();
    }

    void Update()
    {
        // Editor上での変更追従用（軽い処理）
        Apply();
    }

    void Apply()
    {
        if (!master || !self || master == self) return;
        self.uiScaleMode            = master.uiScaleMode;
        self.referenceResolution    = master.referenceResolution;
        self.screenMatchMode        = master.screenMatchMode;
        self.matchWidthOrHeight     = master.matchWidthOrHeight;
        self.referencePixelsPerUnit = master.referencePixelsPerUnit;
        self.dynamicPixelsPerUnit   = master.dynamicPixelsPerUnit;
    }
}