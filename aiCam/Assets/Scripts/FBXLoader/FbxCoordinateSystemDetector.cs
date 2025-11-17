using UnityEngine;
using Assimp;

namespace AICam.FBXLoader
{
    /// <summary>
    /// FBXエクスポート元ツールごとのプロファイル
    /// </summary>
    public enum FBXProfile
    {
        Unknown,        // 判定不可
        UnityStyle,     // Unity標準エクスポート
        BlenderStyle,   // Blender特有の90/270回転補正
        MixamoStyle,    // Mixamo特有の+/-90° Z回転
        VRMStyle,       // VRM系（0,180,0）のroot回転
        MaxStyle        // 3ds Max系
    }

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
        public FBXProfile profileType;  // プロファイル種別

        public override string ToString()
        {
            return $"[{profileName} / {profileType}] Up:{up}, Front:{front}, Right:{right}, RightHanded:{isRightHanded}";
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
                // NOTE: AssimpNetのバージョンによってMetadata APIが異なるため、
                // 現時点では構造解析とデフォルトプロファイルを使用
                // 将来的には、ボーンの位置関係から座標系を自動判定する実装を追加

                Debug.Log($"{LOG_PREFIX} Analyzing scene structure for coordinate system detection...");
                profile = AnalyzeSceneStructure(scene);

                Debug.Log($"{LOG_PREFIX} Using coordinate profile: {profile.profileName}");
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
        /// シーン構造を解析してFBXプロファイルを自動判定
        /// </summary>
        private static FbxCoordProfile AnalyzeSceneStructure(Scene scene)
        {
            Debug.Log($"{LOG_PREFIX} === FBX Profile Detection Start ===");

            // プロファイル判定
            FBXProfile detectedProfile = DetectFBXProfile(scene);

            Debug.Log($"{LOG_PREFIX} Detected Profile: {detectedProfile}");

            // プロファイルごとの座標系設定を返す
            var profile = CreateProfileForType(detectedProfile);

            Debug.Log($"{LOG_PREFIX} Coordinate system:");
            Debug.Log($"{LOG_PREFIX}   Up: {profile.up}, Front: {profile.front}, Right: {profile.right}");
            Debug.Log($"{LOG_PREFIX} === FBX Profile Detection End ===");

            return profile;
        }

        /// <summary>
        /// FBXプロファイルを自動判定（3段階チェック）
        /// </summary>
        private static FBXProfile DetectFBXProfile(Scene scene)
        {
            if (scene.RootNode == null)
                return FBXProfile.Unknown;

            Node rootNode = scene.RootNode;

            // RootNodeの行列をデバッグ出力
            Debug.Log($"{LOG_PREFIX} RootNode Assimp Matrix (raw):");
            Debug.Log($"{LOG_PREFIX}   [{rootNode.Transform.A1:F3}, {rootNode.Transform.A2:F3}, {rootNode.Transform.A3:F3}, {rootNode.Transform.A4:F3}]");
            Debug.Log($"{LOG_PREFIX}   [{rootNode.Transform.B1:F3}, {rootNode.Transform.B2:F3}, {rootNode.Transform.B3:F3}, {rootNode.Transform.B4:F3}]");
            Debug.Log($"{LOG_PREFIX}   [{rootNode.Transform.C1:F3}, {rootNode.Transform.C2:F3}, {rootNode.Transform.C3:F3}, {rootNode.Transform.C4:F3}]");
            Debug.Log($"{LOG_PREFIX}   [{rootNode.Transform.D1:F3}, {rootNode.Transform.D2:F3}, {rootNode.Transform.D3:F3}, {rootNode.Transform.D4:F3}]");

            // RootNodeの回転を取得（Assimp行列から直接抽出）
            Vector3 rootEuler = GetEulerFromAssimpMatrix(rootNode.Transform);

            Debug.Log($"{LOG_PREFIX} RootNode rotation (extracted from matrix): ({rootEuler.x:F1}, {rootEuler.y:F1}, {rootEuler.z:F1})");

            // ✔ Rule 1: RootNode の Y軸180° → VRoid/VRM/Blender系
            bool hasY180 = Mathf.Abs(rootEuler.y - 180f) < 5f || Mathf.Abs(rootEuler.y + 180f) < 5f;

            if (hasY180)
            {
                Debug.Log($"{LOG_PREFIX} → RootNode has Y=180° rotation (VRM/Blender pattern)");

                // Armatureノードを探す
                Node armature = FindNodeByName(rootNode, "Armature");
                if (armature != null)
                {
                    Debug.Log($"{LOG_PREFIX} Armature Assimp Matrix (raw):");
                    Debug.Log($"{LOG_PREFIX}   [{armature.Transform.A1:F3}, {armature.Transform.A2:F3}, {armature.Transform.A3:F3}, {armature.Transform.A4:F3}]");
                    Debug.Log($"{LOG_PREFIX}   [{armature.Transform.B1:F3}, {armature.Transform.B2:F3}, {armature.Transform.B3:F3}, {armature.Transform.B4:F3}]");
                    Debug.Log($"{LOG_PREFIX}   [{armature.Transform.C1:F3}, {armature.Transform.C2:F3}, {armature.Transform.C3:F3}, {armature.Transform.C4:F3}]");
                    Debug.Log($"{LOG_PREFIX}   [{armature.Transform.D1:F3}, {armature.Transform.D2:F3}, {armature.Transform.D3:F3}, {armature.Transform.D4:F3}]");

                    Vector3 armEuler = GetEulerFromAssimpMatrix(armature.Transform);

                    Debug.Log($"{LOG_PREFIX} Armature rotation (extracted from matrix): ({armEuler.x:F1}, {armEuler.y:F1}, {armEuler.z:F1})");

                    // ✔ Rule 2: Armature が X=270° → Blender特有
                    bool hasX270 = Mathf.Abs(armEuler.x - 270f) < 5f || Mathf.Abs(armEuler.x + 90f) < 5f;

                    if (hasX270)
                    {
                        Debug.Log($"{LOG_PREFIX} → Armature has X=270° rotation (Blender export pattern)");

                        // Hipsノードを探す
                        Node hips = FindNodeByName(armature, "Hips");
                        if (hips != null)
                        {
                            Vector3 hipsEuler = GetEulerFromAssimpMatrix(hips.Transform);

                            Debug.Log($"{LOG_PREFIX} Hips rotation (raw from Assimp): ({hipsEuler.x:F1}, {hipsEuler.y:F1}, {hipsEuler.z:F1})");

                            // ✔ Rule 3: Hips の X=90° → Blender の forward補正
                            bool hasX90 = Mathf.Abs(hipsEuler.x - 90f) < 5f;

                            if (hasX90)
                            {
                                Debug.Log($"{LOG_PREFIX} ✓ CONFIRMED: BlenderStyle (RootY=180, ArmX=270, HipsX=90)");
                                return FBXProfile.BlenderStyle;
                            }
                        }
                    }
                }

                // Armature判定失敗だがY180°あり → VRMStyle
                Debug.Log($"{LOG_PREFIX} → Detected as VRMStyle (Y=180° but not Blender pattern)");
                return FBXProfile.VRMStyle;
            }

            // ✔ Rule 3.5: Armature/Hips回転チェック（情報のみ・判定には使わない）
            // RootNode Y=180°がない場合、Armature/Hipsの回転はリグ内部のローカル回転であり
            // 座標系の指標ではない。UnityStyleとして扱うべき。
            Node armatureAlt = FindNodeByName(rootNode, "Armature");
            if (armatureAlt != null)
            {
                Vector3 armEulerAlt = GetEulerFromAssimpMatrix(armatureAlt.Transform);
                Debug.Log($"{LOG_PREFIX} Armature rotation (info only): ({armEulerAlt.x:F1}, {armEulerAlt.y:F1}, {armEulerAlt.z:F1})");

                Node hipsAlt = FindNodeByName(armatureAlt, "Hips");
                if (hipsAlt != null)
                {
                    Vector3 hipsEulerAlt = GetEulerFromAssimpMatrix(hipsAlt.Transform);
                    Debug.Log($"{LOG_PREFIX} Hips rotation (info only): ({hipsEulerAlt.x:F1}, {hipsEulerAlt.y:F1}, {hipsEulerAlt.z:F1})");
                    Debug.Log($"{LOG_PREFIX} → These are local rig rotations, not coordinate system indicators without RootY=180");
                }
            }

            // ✔ Rule 4: Mixamo判定 - Hips.Z=90
            Node hipsNode = FindNodeByName(rootNode, "Hips");
            if (hipsNode != null)
            {
                Vector3 hEuler = GetEulerFromAssimpMatrix(hipsNode.Transform);

                if (Mathf.Abs(hEuler.z - 90f) < 5f || Mathf.Abs(hEuler.z + 90f) < 5f)
                {
                    Debug.Log($"{LOG_PREFIX} ✓ CONFIRMED: MixamoStyle (Hips.Z=90°)");
                    return FBXProfile.MixamoStyle;
                }
            }

            Debug.Log($"{LOG_PREFIX} → No specific pattern detected, using UnityStyle default");
            return FBXProfile.UnityStyle;
        }

        /// <summary>
        /// ノードを名前で再帰検索
        /// </summary>
        private static Node FindNodeByName(Node root, string name)
        {
            if (root.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))
                return root;

            for (int i = 0; i < root.ChildCount; i++)
            {
                Node found = FindNodeByName(root.Children[i], name);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// Assimp Matrix4x4から直接Euler角を抽出（座標系変換なし、生データ）
        /// </summary>
        private static Vector3 GetEulerFromAssimpMatrix(Assimp.Matrix4x4 matrix)
        {
            // Assimp行列をUnity行列に変換（座標系変換なし）
            UnityEngine.Matrix4x4 m = new UnityEngine.Matrix4x4();
            m.m00 = matrix.A1; m.m01 = matrix.A2; m.m02 = matrix.A3; m.m03 = matrix.A4;
            m.m10 = matrix.B1; m.m11 = matrix.B2; m.m12 = matrix.B3; m.m13 = matrix.B4;
            m.m20 = matrix.C1; m.m21 = matrix.C2; m.m22 = matrix.C3; m.m23 = matrix.C4;
            m.m30 = matrix.D1; m.m31 = matrix.D2; m.m32 = matrix.D3; m.m33 = matrix.D4;

            // 行列からQuaternionを抽出
            UnityEngine.Quaternion q = m.rotation;

            // Euler角に変換
            return q.eulerAngles;
        }

        /// <summary>
        /// UnityのQuaternionをオイラー角に変換
        /// </summary>
        private static Vector3 QuaternionToEuler(UnityEngine.Quaternion q)
        {
            return q.eulerAngles;
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
        /// プロファイル種別ごとの座標系設定を生成
        /// </summary>
        private static FbxCoordProfile CreateProfileForType(FBXProfile profileType)
        {
            switch (profileType)
            {
                case FBXProfile.BlenderStyle:
                    return new FbxCoordProfile
                    {
                        up = new Vector3(0, 1, 0),
                        front = new Vector3(0, 0, 1),
                        right = new Vector3(1, 0, 0),
                        isRightHanded = true,
                        profileName = "Blender (Y-up, -Z forward with 180° Y-axis offset)",
                        profileType = FBXProfile.BlenderStyle
                    };

                case FBXProfile.VRMStyle:
                    return new FbxCoordProfile
                    {
                        up = new Vector3(0, 1, 0),
                        front = new Vector3(0, 0, 1),
                        right = new Vector3(1, 0, 0),
                        isRightHanded = true,
                        profileName = "VRM (Y-up, Z forward with 180° Y-axis offset)",
                        profileType = FBXProfile.VRMStyle
                    };

                case FBXProfile.MixamoStyle:
                    return new FbxCoordProfile
                    {
                        up = new Vector3(0, 1, 0),
                        front = new Vector3(0, 0, 1),
                        right = new Vector3(1, 0, 0),
                        isRightHanded = true,
                        profileName = "Mixamo (Y-up with Z-axis rotation)",
                        profileType = FBXProfile.MixamoStyle
                    };

                case FBXProfile.MaxStyle:
                    return new FbxCoordProfile
                    {
                        up = new Vector3(0, 0, 1),  // Z-up
                        front = new Vector3(0, 1, 0),
                        right = new Vector3(1, 0, 0),
                        isRightHanded = true,
                        profileName = "3ds Max (Z-up, Y forward)",
                        profileType = FBXProfile.MaxStyle
                    };

                case FBXProfile.UnityStyle:
                    return new FbxCoordProfile
                    {
                        up = new Vector3(0, 1, 0),
                        front = new Vector3(0, 0, 1),
                        right = new Vector3(1, 0, 0),
                        isRightHanded = true,
                        profileName = "Unity Standard (Y-up, Z forward)",
                        profileType = FBXProfile.UnityStyle
                    };

                case FBXProfile.Unknown:
                default:
                    return CreateDefaultYUpProfile();
            }
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
                profileName = "Y+-up (default)",
                profileType = FBXProfile.Unknown
            };
        }

        /// <summary>
        /// FBX座標系プロファイルからUnity座標系への変換行列を構築
        /// </summary>
        public static UnityEngine.Matrix4x4 BuildConversionMatrix(FbxCoordProfile profile)
        {
            // Unity は 左手系・Y-up・Z forward
            // FBX は 右手系・可変

            Debug.Log($"{LOG_PREFIX} Building conversion matrix for profile: {profile.profileName} ({profile.profileType})");

            UnityEngine.Matrix4x4 conversion;

            // プロファイルごとに変換行列を切り替え
            switch (profile.profileType)
            {
                case FBXProfile.BlenderStyle:
                    // Blender: Y-up, -Z forward with 180° Y-axis and X-axis flip
                    // X軸とZ軸を両方反転（右手系→左手系 + 180°回転の影響を補正）
                    conversion = UnityEngine.Matrix4x4.Scale(new Vector3(-1, 1, -1));
                    Debug.Log($"{LOG_PREFIX} Using Blender-specific conversion (X and Z flip)");
                    break;

                case FBXProfile.VRMStyle:
                    // VRM: Y-up with 180° Y-axis rotation
                    // X軸を反転（180°Y回転の影響を補正）
                    conversion = UnityEngine.Matrix4x4.Scale(new Vector3(-1, 1, -1));
                    Debug.Log($"{LOG_PREFIX} Using VRM-specific conversion (X and Z flip)");
                    break;

                case FBXProfile.MixamoStyle:
                    // Mixamo: Y-up with Z-axis rotation
                    // 標準的なZ軸反転のみ
                    conversion = UnityEngine.Matrix4x4.Scale(new Vector3(1, 1, -1));
                    Debug.Log($"{LOG_PREFIX} Using Mixamo-specific conversion (Z flip only)");
                    break;

                case FBXProfile.MaxStyle:
                    // 3ds Max: Z-up → Y-up変換が必要
                    // X軸はそのまま、Y↔Z入れ替え、右手系→左手系
                    UnityEngine.Matrix4x4 zToYUp = new UnityEngine.Matrix4x4();
                    zToYUp.SetColumn(0, new Vector4(1, 0, 0, 0));  // X: そのまま
                    zToYUp.SetColumn(1, new Vector4(0, 0, 1, 0));  // Y: 元のZ
                    zToYUp.SetColumn(2, new Vector4(0, -1, 0, 0)); // Z: 元の-Y
                    zToYUp.m33 = 1;
                    conversion = zToYUp;
                    Debug.Log($"{LOG_PREFIX} Using 3ds Max-specific conversion (Z-up to Y-up)");
                    break;

                case FBXProfile.UnityStyle:
                    // Unity標準: そのまま（軸反転なし）
                    conversion = UnityEngine.Matrix4x4.identity;
                    Debug.Log($"{LOG_PREFIX} Using Unity standard (no conversion)");
                    break;

                case FBXProfile.Unknown:
                default:
                    // デフォルト: 標準的な右手系→左手系変換のみ
                    UnityEngine.Matrix4x4 fromFBX = new UnityEngine.Matrix4x4();
                    fromFBX.SetColumn(0, new Vector4(profile.right.x, profile.right.y, profile.right.z, 0));
                    fromFBX.SetColumn(1, new Vector4(profile.up.x, profile.up.y, profile.up.z, 0));
                    fromFBX.SetColumn(2, new Vector4(profile.front.x, profile.front.y, profile.front.z, 0));
                    fromFBX.m33 = 1;

                    UnityEngine.Matrix4x4 flipHanded = UnityEngine.Matrix4x4.Scale(new Vector3(1, 1, -1));
                    conversion = flipHanded * fromFBX;
                    Debug.Log($"{LOG_PREFIX} Using default conversion (Z flip)");
                    break;
            }

            Debug.Log($"{LOG_PREFIX} Conversion matrix:");
            Debug.Log($"{LOG_PREFIX}   Row 0: {conversion.GetRow(0)}");
            Debug.Log($"{LOG_PREFIX}   Row 1: {conversion.GetRow(1)}");
            Debug.Log($"{LOG_PREFIX}   Row 2: {conversion.GetRow(2)}");

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
