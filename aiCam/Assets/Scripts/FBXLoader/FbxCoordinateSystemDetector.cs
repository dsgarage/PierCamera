using UnityEngine;
using Assimp;

namespace AICam.FBXLoader
{
    /// <summary>
    /// FBXの座標系プロファイル
    /// </summary>
    public struct FbxCoordProfile
    {
        public Vector3 up;              // 上方向ベクトル
        public Vector3 front;           // 前方向ベクトル
        public Vector3 right;           // 右方向ベクトル
        public bool isRightHanded;      // 右手系かどうか
        public string profileName;      // プロファイル名（デバッグ用）

        public override string ToString()
        {
            return $"[{profileName}] Up:{up}, Front:{front}, Right:{right}, RightHanded:{isRightHanded}";
        }
    }

    /// <summary>
    /// FBXファイルの座標系を自動検出し、Unity座標系への変換行列を生成する
    /// </summary>
    public class FbxCoordinateSystemDetector
    {
        private const string LOG_PREFIX = "[FbxCoordDetector]";

        /// <summary>
        /// Assimpシーンから座標系プロファイルを抽出
        /// </summary>
        public static FbxCoordProfile ExtractFbxCoordProfile(Scene scene)
        {
            var profile = new FbxCoordProfile();

            try
            {
                // RootNodeのMetadataから座標系情報を取得を試みる
                if (scene.RootNode != null && scene.RootNode.HasMetadata)
                {
                    Debug.Log($"{LOG_PREFIX} Attempting to read metadata from RootNode...");

                    // メタデータから軸情報を取得
                    int upAxis = GetMetadataInt(scene.RootNode, "UpAxis", -1);
                    int upAxisSign = GetMetadataInt(scene.RootNode, "UpAxisSign", 1);
                    int frontAxis = GetMetadataInt(scene.RootNode, "FrontAxis", -1);
                    int frontAxisSign = GetMetadataInt(scene.RootNode, "FrontAxisSign", 1);
                    int coordAxis = GetMetadataInt(scene.RootNode, "CoordAxis", -1);
                    int coordAxisSign = GetMetadataInt(scene.RootNode, "CoordAxisSign", 1);

                    if (upAxis >= 0)
                    {
                        profile.up = AxisToVector(upAxis, upAxisSign);
                        profile.front = AxisToVector(frontAxis >= 0 ? frontAxis : 2, frontAxisSign);
                        profile.right = AxisToVector(coordAxis >= 0 ? coordAxis : 0, coordAxisSign);
                        profile.isRightHanded = true;
                        profile.profileName = $"{AxisName(upAxis)}{SignChar(upAxisSign)}-up (metadata)";

                        Debug.Log($"{LOG_PREFIX} Detected from metadata:");
                        Debug.Log($"{LOG_PREFIX}   UpAxis: {upAxis} = {profile.up}");
                        Debug.Log($"{LOG_PREFIX}   Profile: {profile.profileName}");

                        return profile;
                    }
                }

                // メタデータが取得できない場合、ボーン構造から推測
                Debug.LogWarning($"<color=red>{LOG_PREFIX} No metadata available. Analyzing scene structure...</color>");
                profile = AnalyzeSceneStructure(scene);

                Debug.LogWarning($"<color=red>{LOG_PREFIX} Detected from structure analysis:</color>");
                Debug.LogWarning($"<color=red>{LOG_PREFIX}   Profile: {profile.profileName}</color>");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"<color=red>{LOG_PREFIX} Failed to detect coordinate system: {e.Message}</color>");
                Debug.LogWarning($"<color=red>{LOG_PREFIX} Using default Y-up profile.</color>");
                profile = CreateDefaultYUpProfile();
            }

            return profile;
        }

        /// <summary>
        /// シーン構造を解析して座標系を推測
        /// </summary>
        private static FbxCoordProfile AnalyzeSceneStructure(Scene scene)
        {
            // ボーンの向きから座標系を推測
            // Hips→Spineの方向ベクトルを確認してY-upかZ-upかを判定

            // とりあえずデフォルトのY-upプロファイルを返す
            // より高度な解析が必要な場合は、ボーンの位置関係から判断
            var profile = CreateDefaultYUpProfile();
            profile.profileName = "Y+-up (structure analysis)";

            return profile;
        }

        /// <summary>
        /// ノードのメタデータから整数値を取得
        /// </summary>
        private static int GetMetadataInt(Node node, string key, int defaultValue)
        {
            if (!node.HasMetadata)
                return defaultValue;

            for (int i = 0; i < node.MetadataCount; i++)
            {
                var metaKey = node.GetMetadataKey(i);
                if (metaKey == key)
                {
                    var entry = node.GetMetadataEntry(i);
                    if (entry.DataType == MetaDataType.Int32 || entry.DataType == MetaDataType.Int64)
                    {
                        return System.Convert.ToInt32(entry.Data);
                    }
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// 軸インデックスとサインからベクトルを生成
        /// </summary>
        private static Vector3 AxisToVector(int axis, int sign)
        {
            return axis switch
            {
                0 => new Vector3(sign, 0, 0), // X軸
                1 => new Vector3(0, sign, 0), // Y軸
                2 => new Vector3(0, 0, sign), // Z軸
                _ => Vector3.zero
            };
        }

        /// <summary>
        /// 軸インデックスから名前を取得
        /// </summary>
        private static string AxisName(int axis)
        {
            return axis switch
            {
                0 => "X",
                1 => "Y",
                2 => "Z",
                _ => "?"
            };
        }

        /// <summary>
        /// サインから文字を取得
        /// </summary>
        private static char SignChar(int sign)
        {
            return sign >= 0 ? '+' : '-';
        }

        /// <summary>
        /// デフォルトのY-upプロファイルを作成
        /// </summary>
        private static FbxCoordProfile CreateDefaultYUpProfile()
        {
            return new FbxCoordProfile
            {
                up = new Vector3(0, 1, 0),
                front = new Vector3(0, 0, 1),
                right = new Vector3(1, 0, 0),
                isRightHanded = true,
                profileName = "Y+-up (default)"
            };
        }

        /// <summary>
        /// FBX座標系プロファイルからUnity座標系への変換行列を構築
        /// </summary>
        public static UnityEngine.Matrix4x4 BuildConversionMatrix(FbxCoordProfile profile)
        {
            // Unity は 左手系・Y-up・Z forward
            // FBX は 右手系・可変

            Debug.Log($"{LOG_PREFIX} Building conversion matrix for profile: {profile.profileName}");

            // FBX基準軸からの変換行列を構築
            UnityEngine.Matrix4x4 fromFBX = new UnityEngine.Matrix4x4();
            fromFBX.SetColumn(0, new Vector4(profile.right.x, profile.right.y, profile.right.z, 0));
            fromFBX.SetColumn(1, new Vector4(profile.up.x, profile.up.y, profile.up.z, 0));
            fromFBX.SetColumn(2, new Vector4(profile.front.x, profile.front.y, profile.front.z, 0));
            fromFBX.m33 = 1;

            // 右手系 → 左手系への変換（Z軸反転）
            UnityEngine.Matrix4x4 flipHanded = UnityEngine.Matrix4x4.Scale(new Vector3(1, 1, -1));

            UnityEngine.Matrix4x4 conversion = flipHanded * fromFBX;

            Debug.Log($"{LOG_PREFIX} Conversion matrix:");
            Debug.Log($"{LOG_PREFIX}   {conversion.GetRow(0)}");
            Debug.Log($"{LOG_PREFIX}   {conversion.GetRow(1)}");
            Debug.Log($"{LOG_PREFIX}   {conversion.GetRow(2)}");

            return conversion;
        }

        /// <summary>
        /// Assimp Matrix4x4をUnity Matrix4x4に変換（座標系変換適用）
        /// </summary>
        public static UnityEngine.Matrix4x4 ConvertAssimpMatrix(Assimp.Matrix4x4 assimpMatrix, UnityEngine.Matrix4x4 conversionMatrix)
        {
            // Assimp Matrix → Unity Matrix
            UnityEngine.Matrix4x4 unityMatrix = new UnityEngine.Matrix4x4();

            // 列優先で変換
            for (int col = 0; col < 4; col++)
            {
                for (int row = 0; row < 4; row++)
                {
                    unityMatrix[row, col] = assimpMatrix[row + 1, col + 1]; // Assimp is 1-indexed
                }
            }

            // 座標系変換を適用
            return conversionMatrix * unityMatrix * conversionMatrix.inverse;
        }

        /// <summary>
        /// Assimpベクトルを座標系変換してUnity Vector3に変換
        /// </summary>
        public static Vector3 ConvertVector(Assimp.Vector3D assimpVec, UnityEngine.Matrix4x4 conversionMatrix)
        {
            Vector3 vec = new Vector3(assimpVec.X, assimpVec.Y, assimpVec.Z);
            return conversionMatrix.MultiplyPoint3x4(vec);
        }

        /// <summary>
        /// Assimp Quaternionを座標系変換してUnity Quaternionに変換
        /// </summary>
        public static UnityEngine.Quaternion ConvertQuaternion(Assimp.Quaternion assimpQuat, UnityEngine.Matrix4x4 conversionMatrix)
        {
            UnityEngine.Quaternion quat = new UnityEngine.Quaternion(assimpQuat.X, assimpQuat.Y, assimpQuat.Z, assimpQuat.W);

            // 回転も座標系変換を適用
            Vector3 forward = conversionMatrix.MultiplyVector(quat * Vector3.forward);
            Vector3 up = conversionMatrix.MultiplyVector(quat * Vector3.up);

            return UnityEngine.Quaternion.LookRotation(forward, up);
        }
    }
}
