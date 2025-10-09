using UnityEngine;

[System.Serializable]
public class PoseItem
{
    public AnimationClip clip;
    public Sprite thumbnail;
    [Tooltip("空なら clip.name を使う")]
    public string displayNameOverride;
}