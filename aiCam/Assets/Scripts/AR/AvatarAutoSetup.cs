// Assets/Scripts/AR/AvatarAutoSetup.cs
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace AR
{
    /// <summary>
    /// アバターがインスタンス化された時に自動でセットアップを行う
    /// - AvatarFollowControllerを追加/設定
    /// - Colliderを追加（タップ検出用）
    /// - 必要な参照を自動設定
    ///
    /// 使い方1: Prefabにアタッチして自動セットアップ（Awakeで実行）
    /// 使い方2: 動的ロード後に AvatarAutoSetup.Setup(avatarGameObject) を呼ぶ
    /// </summary>
    [DefaultExecutionOrder(-100)] // 他のスクリプトより先に実行
    public class AvatarAutoSetup : MonoBehaviour
    {
        [Header("Follow Settings")]
        [Tooltip("維持する距離（メートル）")]
        [SerializeField] private float desiredDistance = 1.5f;

        [Tooltip("位置の補間速度（0-1）")]
        [SerializeField] private float posLerp = 0.15f;

        [Tooltip("回転の補間速度（0-1）")]
        [SerializeField] private float rotLerp = 0.15f;

        [Header("Collider Settings")]
        [Tooltip("自動でColliderを追加するか")]
        [SerializeField] private bool autoAddCollider = true;

        [Tooltip("Colliderのサイズ（アバターに合わせて調整）")]
        [SerializeField] private Vector3 colliderSize = new Vector3(0.5f, 1.8f, 0.5f);

        [Tooltip("Colliderの中心オフセット")]
        [SerializeField] private Vector3 colliderCenter = new Vector3(0, 0.9f, 0);

        [Header("Layer Settings")]
        [Tooltip("アバターに設定するレイヤー名（空なら変更しない）")]
        [SerializeField] private string avatarLayerName = ""; // 例: "ARAvatar"

        void Awake()
        {
            SetupAvatar();
        }

        // ========== 静的メソッド ==========

        /// <summary>
        /// 動的にロードされたアバターGameObjectをセットアップ（デフォルト設定）
        /// FBXからAssimpでロードした後などに呼び出す
        /// </summary>
        /// <param name="avatarGameObject">セットアップ対象のアバターGameObject</param>
        /// <returns>セットアップ成功したか</returns>
        public static bool Setup(GameObject avatarGameObject)
        {
            return Setup(avatarGameObject, new SetupConfig());
        }

        /// <summary>
        /// 動的にロードされたアバターGameObjectをセットアップ（カスタム設定）
        /// </summary>
        /// <param name="avatarGameObject">セットアップ対象のアバターGameObject</param>
        /// <param name="config">セットアップ設定</param>
        /// <returns>セットアップ成功したか</returns>
        public static bool Setup(GameObject avatarGameObject, SetupConfig config)
        {
            if (avatarGameObject == null)
            {
                Debug.LogError("[AvatarAutoSetup] avatarGameObject is null");
                return false;
            }

            Debug.Log($"[AvatarAutoSetup] Setting up avatar: {avatarGameObject.name}");

            // 1. AvatarFollowController を追加/設定
            SetupFollowControllerStatic(avatarGameObject, config);

            // 2. Collider を追加
            if (config.autoAddCollider)
            {
                SetupColliderStatic(avatarGameObject, config);
            }

            // 3. Layer を設定
            if (!string.IsNullOrEmpty(config.avatarLayerName))
            {
                SetupLayerStatic(avatarGameObject, config.avatarLayerName);
            }

            Debug.Log($"[AvatarAutoSetup] Setup complete for: {avatarGameObject.name}");
            return true;
        }

        /// <summary>
        /// セットアップ設定クラス
        /// </summary>
        public class SetupConfig
        {
            public float desiredDistance = 1.5f;
            public float posLerp = 0.15f;
            public float rotLerp = 0.15f;
            public bool autoAddCollider = true;
            public Vector3 colliderSize = new Vector3(0.5f, 1.8f, 0.5f);
            public Vector3 colliderCenter = new Vector3(0, 0.9f, 0);
            public string avatarLayerName = ""; // 空なら変更しない
        }

        /// <summary>
        /// アバターの自動セットアップ
        /// </summary>
        private void SetupAvatar()
        {
            Debug.Log($"[AvatarAutoSetup] Setting up avatar: {gameObject.name}");

            // 1. AvatarFollowController を追加/設定
            SetupFollowController();

            // 2. Collider を追加
            if (autoAddCollider)
            {
                SetupCollider();
            }

            // 3. Layer を設定
            if (!string.IsNullOrEmpty(avatarLayerName))
            {
                SetupLayer();
            }

            Debug.Log($"[AvatarAutoSetup] Setup complete for: {gameObject.name}");
        }

        /// <summary>
        /// AvatarFollowController を追加/設定
        /// </summary>
        private void SetupFollowController()
        {
            var followController = GetComponent<AvatarFollowController>();

            if (followController == null)
            {
                followController = gameObject.AddComponent<AvatarFollowController>();
                Debug.Log("[AvatarAutoSetup] Added AvatarFollowController");
            }

            // 参照を自動設定
            var raycaster = FindObjectOfType<ARRaycastManager>();
            var planeManager = FindObjectOfType<ARPlaneManager>();
            var arCamera = Camera.main;

            if (raycaster != null && planeManager != null && arCamera != null)
            {
                // Reflection を使って private フィールドに値を設定
                var type = typeof(AvatarFollowController);

                SetPrivateField(followController, type, "raycaster", raycaster);
                SetPrivateField(followController, type, "planeManager", planeManager);
                SetPrivateField(followController, type, "arCamera", arCamera);
                SetPrivateField(followController, type, "desiredDistance", desiredDistance);
                SetPrivateField(followController, type, "posLerp", posLerp);
                SetPrivateField(followController, type, "rotLerp", rotLerp);

                Debug.Log("[AvatarAutoSetup] AvatarFollowController references set automatically");
            }
            else
            {
                Debug.LogWarning("[AvatarAutoSetup] Could not find required AR components. Please set references manually.");
            }
        }

        /// <summary>
        /// Collider を追加
        /// </summary>
        private void SetupCollider()
        {
            // 既存のColliderをチェック
            var existingCollider = GetComponent<Collider>();

            if (existingCollider != null)
            {
                Debug.Log($"[AvatarAutoSetup] Collider already exists: {existingCollider.GetType().Name}");
                return;
            }

            // BoxCollider を追加
            var boxCollider = gameObject.AddComponent<BoxCollider>();
            boxCollider.size = colliderSize;
            boxCollider.center = colliderCenter;

            Debug.Log($"[AvatarAutoSetup] Added BoxCollider - Size: {colliderSize}, Center: {colliderCenter}");
        }

        /// <summary>
        /// Layer を設定
        /// </summary>
        private void SetupLayer()
        {
            int layer = LayerMask.NameToLayer(avatarLayerName);

            if (layer == -1)
            {
                Debug.LogWarning($"[AvatarAutoSetup] Layer '{avatarLayerName}' does not exist. Please create it in Project Settings.");
                return;
            }

            // 自身と全ての子オブジェクトにレイヤーを設定
            SetLayerRecursively(gameObject, layer);

            Debug.Log($"[AvatarAutoSetup] Set layer to '{avatarLayerName}' (layer {layer})");
        }

        /// <summary>
        /// 再帰的にレイヤーを設定
        /// </summary>
        private void SetLayerRecursively(GameObject obj, int layer)
        {
            obj.layer = layer;

            foreach (Transform child in obj.transform)
            {
                SetLayerRecursively(child.gameObject, layer);
            }
        }

        /// <summary>
        /// Reflection を使って private フィールドに値を設定
        /// </summary>
        private void SetPrivateField(object instance, System.Type type, string fieldName, object value)
        {
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                field.SetValue(instance, value);
            }
            else
            {
                Debug.LogWarning($"[AvatarAutoSetup] Field '{fieldName}' not found in {type.Name}");
            }
        }

        // ========== 静的メソッド用のヘルパー ==========

        /// <summary>
        /// AvatarFollowController を追加/設定（静的版）
        /// </summary>
        private static void SetupFollowControllerStatic(GameObject avatarGameObject, SetupConfig config)
        {
            var followController = avatarGameObject.GetComponent<AvatarFollowController>();

            if (followController == null)
            {
                followController = avatarGameObject.AddComponent<AvatarFollowController>();
                Debug.Log("[AvatarAutoSetup] Added AvatarFollowController");
            }

            // 参照を自動設定
            var raycaster = Object.FindObjectOfType<ARRaycastManager>();
            var planeManager = Object.FindObjectOfType<ARPlaneManager>();
            var arCamera = Camera.main;

            if (raycaster != null && planeManager != null && arCamera != null)
            {
                // Reflection を使って private フィールドに値を設定
                var type = typeof(AvatarFollowController);

                SetPrivateFieldStatic(followController, type, "raycaster", raycaster);
                SetPrivateFieldStatic(followController, type, "planeManager", planeManager);
                SetPrivateFieldStatic(followController, type, "arCamera", arCamera);
                SetPrivateFieldStatic(followController, type, "desiredDistance", config.desiredDistance);
                SetPrivateFieldStatic(followController, type, "posLerp", config.posLerp);
                SetPrivateFieldStatic(followController, type, "rotLerp", config.rotLerp);

                Debug.Log("[AvatarAutoSetup] AvatarFollowController references set automatically");
            }
            else
            {
                Debug.LogWarning("[AvatarAutoSetup] Could not find required AR components. Please set references manually.");
            }
        }

        /// <summary>
        /// Collider を追加（静的版）
        /// </summary>
        private static void SetupColliderStatic(GameObject avatarGameObject, SetupConfig config)
        {
            // 既存のColliderをチェック
            var existingCollider = avatarGameObject.GetComponent<Collider>();

            if (existingCollider != null)
            {
                Debug.Log($"[AvatarAutoSetup] Collider already exists: {existingCollider.GetType().Name}");
                return;
            }

            // BoxCollider を追加
            var boxCollider = avatarGameObject.AddComponent<BoxCollider>();
            boxCollider.size = config.colliderSize;
            boxCollider.center = config.colliderCenter;

            Debug.Log($"[AvatarAutoSetup] Added BoxCollider - Size: {config.colliderSize}, Center: {config.colliderCenter}");
        }

        /// <summary>
        /// Layer を設定（静的版）
        /// </summary>
        private static void SetupLayerStatic(GameObject avatarGameObject, string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);

            if (layer == -1)
            {
                Debug.LogWarning($"[AvatarAutoSetup] Layer '{layerName}' does not exist. Please create it in Project Settings.");
                return;
            }

            // 自身と全ての子オブジェクトにレイヤーを設定
            SetLayerRecursivelyStatic(avatarGameObject, layer);

            Debug.Log($"[AvatarAutoSetup] Set layer to '{layerName}' (layer {layer})");
        }

        /// <summary>
        /// 再帰的にレイヤーを設定（静的版）
        /// </summary>
        private static void SetLayerRecursivelyStatic(GameObject obj, int layer)
        {
            obj.layer = layer;

            foreach (Transform child in obj.transform)
            {
                SetLayerRecursivelyStatic(child.gameObject, layer);
            }
        }

        /// <summary>
        /// Reflection を使って private フィールドに値を設定（静的版）
        /// </summary>
        private static void SetPrivateFieldStatic(object instance, System.Type type, string fieldName, object value)
        {
            var field = type.GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                field.SetValue(instance, value);
            }
            else
            {
                Debug.LogWarning($"[AvatarAutoSetup] Field '{fieldName}' not found in {type.Name}");
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Inspector でパラメータ調整時にプレビュー（エディタのみ）
        /// </summary>
        void OnDrawGizmosSelected()
        {
            if (!autoAddCollider)
                return;

            // Collider のプレビュー表示
            Gizmos.color = new Color(0, 1, 0, 0.3f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(colliderCenter, colliderSize);

            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(colliderCenter, colliderSize);
        }
#endif
    }
}
