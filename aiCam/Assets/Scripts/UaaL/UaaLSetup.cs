using UnityEngine;
using Pier.UaaL;

namespace AICam.UaaL
{
    /// <summary>
    /// UaaL Setup Component
    /// シーンに配置してReact Native連携を有効化
    /// CommandReceiverとUaaLManagerを自動セットアップ
    /// </summary>
    public class UaaLSetup : MonoBehaviour
    {
        [Header("Auto Setup")]
        [SerializeField] private bool autoSetup = true;

        private void Awake()
        {
            if (!autoSetup) return;

            // Ensure CommandReceiver exists
            if (CommandReceiver.Instance == null)
            {
                var receiverGO = new GameObject("CommandReceiver");
                receiverGO.AddComponent<CommandReceiver>();
                Debug.Log("[UaaLSetup] CommandReceiver created");
            }

            // Ensure UaaLManager exists
            if (UaaLManager.Instance == null)
            {
                var managerGO = new GameObject("UaaLManager");
                managerGO.AddComponent<UaaLManager>();
                Debug.Log("[UaaLSetup] UaaLManager created");
            }

            Debug.Log("[UaaLSetup] UaaL components initialized");
        }
    }
}
