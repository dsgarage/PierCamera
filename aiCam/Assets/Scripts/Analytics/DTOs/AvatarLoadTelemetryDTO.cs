using System;

namespace AICam.Analytics.DTOs
{
    /// <summary>
    /// アバターロードテレメトリDTO
    /// サーバーに送信するデータ構造
    /// </summary>
    [Serializable]
    public class AvatarLoadTelemetryDTO
    {
        // セッション情報
        public string sessionId;
        public string timestamp;  // ISO 8601 形式（ロード日時）

        // ファイル情報
        public string fileName;
        public string fileExtension;  // "vrm" or "fbx"
        public long fileSizeBytes;
        public string fileHash;  // MD5

        // VRMバージョン
        public string vrmVersion;  // "VRM_0_x", "VRM_1_0", "FBX"

        // VRMメタデータ
        public VrmMetadata0x vrmMeta0x;
        public VrmMetadata10 vrmMeta10;

        // メッシュ統計
        public MeshStatistics meshStats;

        // テクスチャ情報
        public TextureStatistics textureStats;

        // Unity Avatar情報
        public AvatarInfo avatarInfo;

        // 初期化状態
        public InitializationFlags initFlags;

        // パフォーマンス
        public PerformanceMetrics performance;

        // デバイス情報
        public DeviceInfo device;

        // 結果
        public bool success;
        public string errorMessage;
        public int slotIndex;
    }

    /// <summary>
    /// VRM 0.x メタデータ
    /// </summary>
    [Serializable]
    public class VrmMetadata0x
    {
        public string title;
        public string version;
        public string author;
        public string contactInformation;
        public string reference;
        public string allowedUser;  // "OnlyAuthor", "ExplicitlyLicensedPerson", "Everyone"
        public string violentUsage;
        public string sexualUsage;
        public string commercialUsage;
        public string otherPermissionUrl;
        public string licenseType;
    }

    /// <summary>
    /// VRM 1.0 メタデータ
    /// </summary>
    [Serializable]
    public class VrmMetadata10
    {
        public string name;
        public string version;
        public string[] authors;
        public string copyrightInformation;
        public string contactInformation;
        public string[] references;
        public string thirdPartyLicenses;
        public string avatarPermission;
        public string violentUsage;
        public string sexualUsage;
        public string commercialUsage;
        public bool allowPoliticalOrReligiousUsage;
        public bool allowAntisocialOrHateUsage;
        public string creditNotation;
        public bool allowRedistribution;
        public string modification;
        public string otherLicenseUrl;
    }

    /// <summary>
    /// メッシュ統計情報
    /// </summary>
    [Serializable]
    public class MeshStatistics
    {
        public int skinnedMeshRendererCount;
        public int meshRendererCount;
        public int totalVertexCount;
        public int totalTriangleCount;
        public int totalBlendShapeCount;
        public int totalMaterialCount;
        public int totalBoneCount;
        public int springBoneCount;
        public string[] materialNames;
        public string[] shaderNames;
        public ShaderInfo[] shaderDetails;  // 詳細なシェーダー情報
    }

    /// <summary>
    /// シェーダー詳細情報
    /// </summary>
    [Serializable]
    public class ShaderInfo
    {
        public string name;
        public string materialName;
        public int renderQueue;
        public string[] keywords;
        public int passCount;
        public bool isSupported;
    }

    /// <summary>
    /// テクスチャ統計情報
    /// </summary>
    [Serializable]
    public class TextureStatistics
    {
        public int textureCount;
        public long totalTextureMemoryBytes;
        public int maxTextureWidth;
        public int maxTextureHeight;
        public string[] textureFormats;  // RGBA32, ASTC, ETC2, etc.
        public int[] textureWidths;
        public int[] textureHeights;
        public string[] textureNames;
    }

    /// <summary>
    /// Unity Avatar情報
    /// </summary>
    [Serializable]
    public class AvatarInfo
    {
        public bool isValid;
        public bool isHuman;
        public int humanBoneCount;      // HumanDescription.human.Length
        public int skeletonBoneCount;   // HumanDescription.skeleton.Length
        public float armStretch;
        public float legStretch;
        public float upperArmTwist;
        public float lowerArmTwist;
        public float upperLegTwist;
        public float lowerLegTwist;
        public float feetSpacing;
        public bool hasTranslationDoF;
    }

    /// <summary>
    /// 初期化状態フラグ
    /// </summary>
    [Serializable]
    public class InitializationFlags
    {
        public bool animatorInitialized;
        public bool avatarValid;
        public bool springBoneInitialized;
        public bool expressionInitialized;   // VRM Expression
        public bool lookAtInitialized;       // VRM LookAt
        public bool materialInitialized;
        public bool meshRendererEnabled;
        public bool rootMotionEnabled;
    }

    /// <summary>
    /// パフォーマンス計測
    /// </summary>
    [Serializable]
    public class PerformanceMetrics
    {
        public float loadTimeSeconds;
        public float fileReadTimeSeconds;
        public float parseTimeSeconds;
        public float meshSetupTimeSeconds;
        public long memoryUsedBytes;
        public long textureMemoryBytes;
    }

    /// <summary>
    /// デバイス情報
    /// </summary>
    [Serializable]
    public class DeviceInfo
    {
        public string deviceModel;
        public string deviceName;
        public string osVersion;
        public string unityVersion;
        public string appVersion;
        public string buildVersion;    // ビルド番号
        public string bundleVersion;   // CFBundleVersion (iOS) / versionCode (Android)
        public bool hasLiDAR;
        public string deviceCategory;
        public int systemMemoryMB;
        public int graphicsMemoryMB;
        public string graphicsDeviceName;
    }
}
