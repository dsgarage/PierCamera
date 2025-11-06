using UnityEngine;

public class PoseSwitcher : MonoBehaviour
{
    [Tooltip("操作対象の Animator")]
    public Animator animator;

    /// <summary>
    /// Animator のステート名を直接指定して切り替えます。
    /// 例: Idle / Pose1 / Pose2
    /// </summary>
    public void SetPose(string stateName)
    {
        if (animator == null)
        {
            Debug.LogError("Animator が設定されていません。");
            return;
        }
        if (string.IsNullOrEmpty(stateName))
        {
            Debug.LogWarning("stateName が空です。");
            return;
        }
        animator.Play(stateName, 0, 0f);
    }
}
