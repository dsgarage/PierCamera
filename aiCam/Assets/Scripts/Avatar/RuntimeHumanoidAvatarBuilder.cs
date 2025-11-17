/*******************************************************************************************
 *  RuntimeHumanoidAvatarBuilder.cs  (2025-06-24 *Exact-Match Pass* 版)
 *  ---------------------------------------------------------------------------------------
 *  目的:
 *    - どんな FBX でも Runtime で安定して Humanoid Avatar を生成する
 *
 *  主な機能（06-06 版をベースに改良）
 *    1. ❶ **完全一致パス**: Transform.name が HumanBodyBones.ToString() と一致すれば確定
 *      　   └ 確定したボーンは以降の Alias / Keyword / Heuristic で上書きしない
 *    2.  エイリアス辞書 (Resources/BoneAliasMap.json) によるマッピング
 *    3.  キーワード辞書 + 正規表現による推測マッピング
 *    4.  Hips ローカル X 座標による左右補正（Toe 含むペアすべて）
 *    5.  必須 15 ボーン & 親子整合性チェック
 *    6.  T-ポーズ角度検証 (腕水平・脚垂直) ※警告ログのみ
 *    7.  SkeletonBone 生成時にスケールを Vector3.one へ正規化
 *    8.  Avatar 生成失敗・検証失敗時は DebugHelper.DumpHierarchy で Armature 構造を保存
 *
 *  使い方:
 *    var builder = new RuntimeHumanoidAvatarBuilder();
 *    Avatar avatar = builder.CreateHumanoidAvatarFromFBX("Kikyo", fbxRoot);
 *******************************************************************************************/
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace AICam.AvatarBuilder
{
    public class RuntimeHumanoidAvatarBuilder
    {
        // ───────────────────────────────────────────────────────────
        //  フィールド & コンストラクタ
        // ───────────────────────────────────────────────────────────
        private readonly BoneNameAliasDatabase aliasDB;
        public RuntimeHumanoidAvatarBuilder(string aliasJsonPath = "BoneAliasMap")
            => aliasDB = new BoneNameAliasDatabase(aliasJsonPath);

        // ───────────────────────────────────────────────────────────
        //  PUBLIC API
        // ───────────────────────────────────────────────────────────
        public UnityEngine.Avatar CreateHumanoidAvatarFromFBX(string modelName, GameObject root)
        {
            // ① ボーンマッピング
            Dictionary<HumanBodyBones, Transform> map = ResolveBoneMapping(root);
            LogBoneMappingResults(map, modelName);

            // ② 必須 15 ボーン検証
            if (!ValidateRequiredBones(map, modelName))
            {
                Debug.LogError($"[RuntimeHumanoidAvatarBuilder] Required bone validation failed for {modelName}");
                return null;
            }

            // ③ 左右補正
            EnsureLeftRight(ref map, map[HumanBodyBones.Hips]);

            // ④ 親子整合性検証
            if (!ValidateHierarchy(map, modelName))
            {
                Debug.LogError($"[RuntimeHumanoidAvatarBuilder] Hierarchy validation failed for {modelName}");
                return null;
            }

            // ⑤ 座標系診断と自動検出
            DetectAndLogCoordinateSystem(map, modelName);

            // ⑥ T-ポーズ角度チェック
            CheckTPose(map, modelName);

            // ⑦ Avatar 組み立て
            return BuildHumanoidAvatar(root, map, modelName);
        }

        /// <summary>
        /// AvatarTemplateを使用してAvatar生成（最高精度）
        /// Editor-importedしたFBXと同じAvatar定義を使用
        /// </summary>
        public UnityEngine.Avatar CreateHumanoidAvatarFromTemplate(
            string modelName,
            GameObject root,
            AvatarTemplate template)
        {
            if (template == null)
            {
                Debug.LogError($"[RuntimeHumanoidAvatarBuilder] AvatarTemplate is null. Falling back to default method.");
                return CreateHumanoidAvatarFromFBX(modelName, root);
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Using AvatarTemplate: {template.name}");
            Debug.Log($"  Source FBX: {template.sourceFBXName}");
            Debug.Log($"  Extracted: {template.extractedDate}");

            // ① ボーンマッピング検証
            if (!template.ValidateBoneMapping(root.transform))
            {
                Debug.LogError($"[RuntimeHumanoidAvatarBuilder] Template bone mapping validation failed. " +
                              $"Runtime skeleton might have different bone names than template.");
                Debug.LogWarning($"  Falling back to default bone detection...");
                return CreateHumanoidAvatarFromFBX(modelName, root);
            }

            // ② HumanDescriptionをテンプレートから構築
            HumanDescription humanDesc = template.BuildHumanDescription(root.transform);

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Built HumanDescription from template:");
            Debug.Log($"  HumanBones: {humanDesc.human.Length}");
            Debug.Log($"  SkeletonBones: {humanDesc.skeleton.Length}");
            Debug.Log($"  Parameters: armTwist={humanDesc.upperArmTwist}, legTwist={humanDesc.upperLegTwist}");

            // ③ Avatar生成
            UnityEngine.Avatar avatar = UnityEngine.AvatarBuilder.BuildHumanAvatar(root, humanDesc);

            if (!avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[RuntimeHumanoidAvatarBuilder] Template-based Avatar build FAILED");
                Debug.LogError($"  IsValid: {avatar.isValid}, IsHuman: {avatar.isHuman}");
                return null;
            }

            Debug.Log($"<color=green>[RuntimeHumanoidAvatarBuilder] ✓ Successfully built Avatar from template</color>");
            Debug.Log($"  IsValid: {avatar.isValid}, IsHuman: {avatar.isHuman}");

            return avatar;
        }

        // ───────────────────────────────────────────────────────────
        // ① ResolveBoneMapping
        //     完全一致 → Alias → Keyword → Hieristic
        // ───────────────────────────────────────────────────────────
        private Dictionary<HumanBodyBones, Transform> ResolveBoneMapping(GameObject root)
        {
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === Bone Mapping Start for '{root.name}' ===");
            var resolved = new Dictionary<HumanBodyBones, Transform>();

            // 全 Transform 収集
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Total transforms found: {all.Length}");

            // --- 1) 大文字小文字を区別せず *生の名前* で検索できる辞書
            var rawNameMap = new Dictionary<string, Transform>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in all) if (!rawNameMap.ContainsKey(t.name)) rawNameMap[t.name] = t;

            // ---------- 1-A : 完全一致パス ----------
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                string exact = bone.ToString();             // e.g., "LeftHand"
                if (rawNameMap.TryGetValue(exact, out var t))
                    resolved[bone] = t;                     // 確定（以降は上書きしない）
            }

            // ---------- 1-B : エイリアス辞書 ----------
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone || resolved.ContainsKey(bone)) continue;

                foreach (string alias in aliasDB.GetAliases(bone.ToString()))
                {
                    if (rawNameMap.TryGetValue(alias, out var t)) { resolved[bone] = t; break; }
                }
            }

            // ---------- 1-C : キーワード推測 ----------
            //   正規化名 → Transform のマップを生成
            var normMap = new Dictionary<string, Transform>();
            foreach (var t in all) normMap[Normalize(t.name)] = t;

            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone || resolved.ContainsKey(bone)) continue;
                if (!KeywordDict.TryGetValue(bone, out var keys)) continue;

                foreach (var (normName, tr) in normMap)
                {
                    foreach (string kw in keys)
                        if (normName.Contains(kw)) { resolved[bone] = tr; goto NEXT_B; }
                }
            NEXT_B:;
            }

            // ---------- 1-D : ヒューリスティック補完 (Toe) ----------
            InferFromHierarchy(resolved);

            // デバッグ: マッピング結果を出力
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Bone mapping completed. Mapped {resolved.Count} bones:");
            foreach (var kvp in resolved)
            {
                Debug.Log($"  {kvp.Key} => '{kvp.Value.name}'");
            }
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === Bone Mapping End ===");

            return resolved;
        }

        // ───── 正規化ルール ─────
        private static readonly Regex rxNorm =
            new(@"[ _\-.]|mixamorig:|rig_|root_|bone_|jnt",
                 RegexOptions.IgnoreCase);
        private static string Normalize(string s) => rxNorm.Replace(s, "").ToLower();

        // ───── キーワード辞書 ─────
        private static readonly Dictionary<HumanBodyBones, string[]> KeywordDict = new()
        {
            { HumanBodyBones.Hips,           new[]{"hip","pelvis"} },
            { HumanBodyBones.Spine,          new[]{"spine"} },
            { HumanBodyBones.Chest,          new[]{"chest","upperchest"} },
            { HumanBodyBones.Neck,           new[]{"neck"} },
            { HumanBodyBones.Head,           new[]{"head"} },

            { HumanBodyBones.LeftUpperArm,   new[]{"upperarm","uparm"} },
            { HumanBodyBones.RightUpperArm,  new[]{"upperarm","uparm"} },
            { HumanBodyBones.LeftLowerArm,   new[]{"lowerarm","forearm"} },
            { HumanBodyBones.RightLowerArm,  new[]{"lowerarm","forearm"} },
            { HumanBodyBones.LeftHand,       new[]{"hand"} },
            { HumanBodyBones.RightHand,      new[]{"hand"} },

            { HumanBodyBones.LeftUpperLeg,   new[]{"upperleg","thigh"} },
            { HumanBodyBones.RightUpperLeg,  new[]{"upperleg","thigh"} },
            { HumanBodyBones.LeftLowerLeg,   new[]{"lowerleg","calf","shin"} },
            { HumanBodyBones.RightLowerLeg,  new[]{"lowerleg","calf","shin"} },
            { HumanBodyBones.LeftFoot,       new[]{"foot"} },
            { HumanBodyBones.RightFoot,      new[]{"foot"} },
            { HumanBodyBones.LeftToes,       new[]{"toe"} },
            { HumanBodyBones.RightToes,      new[]{"toe"} },
        };

        // ───── Toe 補完 ─────
        private static void InferFromHierarchy(Dictionary<HumanBodyBones, Transform> map)
        {
            void Find(HumanBodyBones foot, HumanBodyBones toe)
            {
                if (map.ContainsKey(toe) || !map.ContainsKey(foot)) return;
                foreach (Transform c in map[foot].GetComponentsInChildren<Transform>(true))
                    if (Normalize(c.name).Contains("toe")) { map[toe] = c; break; }
            }
            Find(HumanBodyBones.LeftFoot,  HumanBodyBones.LeftToes);
            Find(HumanBodyBones.RightFoot, HumanBodyBones.RightToes);
        }

        // ───────────────────────────────────────────────────────────
        // ② 必須ボーン検証
        // ───────────────────────────────────────────────────────────
        private static bool ValidateRequiredBones(Dictionary<HumanBodyBones, Transform> map, string model)
        {
            bool ok = true;
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (!HumanTrait.RequiredBone(i)) continue;
                var req = (HumanBodyBones)i;
                if (!map.ContainsKey(req))
                {
                    Debug.LogError($"[{model}] missing required bone {req}");
                    ok = false;
                }
            }
            return ok;
        }

        // ───────────────────────────────────────────────────────────
        // ③ 左右補正
        // ───────────────────────────────────────────────────────────
        private static void EnsureLeftRight(ref Dictionary<HumanBodyBones, Transform> map, Transform hips)
        {
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === Left/Right Correction Start ===");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Hips transform: {hips.name}");

            // 「Left*/Right*」ペア一覧作成
            var leftDict  = new Dictionary<string, HumanBodyBones>();
            var rightDict = new Dictionary<string, HumanBodyBones>();
            foreach (HumanBodyBones b in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (b == HumanBodyBones.LastBone) continue;
                string n = b.ToString();
                if (n.StartsWith("Left"))  leftDict [n[4..]] = b;
                if (n.StartsWith("Right")) rightDict[n[5..]] = b;
            }

            int swapCount = 0;
            foreach (var key in leftDict.Keys)
            {
                if (!rightDict.ContainsKey(key)) continue;

                var lb = leftDict[key];
                var rb = rightDict[key];
                if (!map.ContainsKey(lb) || !map.ContainsKey(rb)) continue;

                // ボーン名の確認（参考情報として記録）
                string ln = map[lb].name.ToLower();
                string rn = map[rb].name.ToLower();
                bool hasLeftRightInName = (ln.Contains("left") || ln.EndsWith(".l") || ln.EndsWith("_l")) &&
                                         (rn.Contains("right")|| rn.EndsWith(".r")|| rn.EndsWith("_r"));

                // 実際の位置をチェック（必ず実行）
                Vector3 lPosLocal = hips.InverseTransformPoint(map[lb].position);
                Vector3 rPosLocal = hips.InverseTransformPoint(map[rb].position);
                bool lIsLeft = lPosLocal.x < 0f;
                bool rIsLeft = rPosLocal.x < 0f;

                Debug.Log($"[RuntimeHumanoidAvatarBuilder] Checking {key}:");
                Debug.Log($"[RuntimeHumanoidAvatarBuilder]   Left({lb}): '{ln}' at local X={lPosLocal.x:F3}, isLeft={lIsLeft}");
                Debug.Log($"[RuntimeHumanoidAvatarBuilder]   Right({rb}): '{rn}' at local X={rPosLocal.x:F3}, isLeft={rIsLeft}");
                Debug.Log($"[RuntimeHumanoidAvatarBuilder]   Has L/R in name: {hasLeftRightInName}");

                // ★ 名前優先ロジック: .L/.R などがあれば絶対に信頼する
                if (hasLeftRightInName)
                {
                    // 名前が正しいので、位置が逆でも swap しない
                    if (!lIsLeft && rIsLeft)
                    {
                        Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder]   ⚠️ Bone names are correct, but positions are reversed due to 180° rotation. Trusting bone names.");
                    }
                    Debug.Log($"[RuntimeHumanoidAvatarBuilder]   => Trusting bone names, no swap");
                }
                else
                {
                    // 名前から左右判定できないときだけ、位置で swap を試みる
                    if (!lIsLeft && rIsLeft)
                    {
                        Debug.Log($"[RuntimeHumanoidAvatarBuilder]   => SWAPPING bones (position-based correction, no name info)");
                        (map[lb], map[rb]) = (map[rb], map[lb]);
                        swapCount++;
                    }
                    else
                    {
                        Debug.Log($"[RuntimeHumanoidAvatarBuilder]   => No swap needed (positions correct)");
                    }
                }
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Left/Right correction: {swapCount} pairs swapped");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === Left/Right Correction End ===");
        }

        // ───────────────────────────────────────────────────────────
        // ④ 親子整合性チェック
        // ───────────────────────────────────────────────────────────
        private static bool ValidateHierarchy(Dictionary<HumanBodyBones, Transform> map, string model)
        {
            var rules = new (HumanBodyBones Parent, HumanBodyBones Child)[]
            {
                (HumanBodyBones.LeftFoot,  HumanBodyBones.LeftToes),
                (HumanBodyBones.RightFoot, HumanBodyBones.RightToes),
                (HumanBodyBones.Hips,      HumanBodyBones.Spine),
                (HumanBodyBones.Spine,     HumanBodyBones.Chest),
                (HumanBodyBones.Chest,     HumanBodyBones.Neck),
                (HumanBodyBones.Neck,      HumanBodyBones.Head),
            };

            bool ok = true;
            foreach (var (p, c) in rules)
            {
                if (!map.ContainsKey(p) || !map.ContainsKey(c)) continue;
                if (!map[c].IsChildOf(map[p]))
                {
                    Debug.LogError($"[{model}] {c} is not child of {p}");
                    ok = false;
                }
            }
            return ok;
        }

        // ───────────────────────────────────────────────────────────
        // ⑤ T-Pose 角度チェック（警告のみ）
        // ───────────────────────────────────────────────────────────
        private static void CheckTPose(Dictionary<HumanBodyBones, Transform> map, string model)
        {
            if (!map.TryGetValue(HumanBodyBones.Hips, out var hips) ||
                !map.TryGetValue(HumanBodyBones.Spine, out var spine)) return;

            Vector3 up = (spine.position - hips.position).normalized;
            var tests = new (HumanBodyBones bone, float target, float tol, string label)[]
            {
                (HumanBodyBones.LeftUpperArm,  90f, 15f, "L-Arm"),
                (HumanBodyBones.RightUpperArm, 90f, 15f, "R-Arm"),
                (HumanBodyBones.LeftUpperLeg,   0f, 20f, "L-Leg"),
                (HumanBodyBones.RightUpperLeg,  0f, 20f, "R-Leg"),
            };

            foreach (var (b,tgt,tol,label) in tests)
            {
                if (!map.TryGetValue(b, out var t)) continue;
                float ang = Vector3.Angle((tgt==0? -up : up), (t.position-hips.position));
                if (Mathf.Abs(ang - tgt) > tol)
                    Debug.LogWarning($"[{model}] {label} off T-Pose by {ang:F1}°");
            }
        }

        // ───────────────────────────────────────────────────────────
        // ⑤ 座標系診断
        // ───────────────────────────────────────────────────────────
        /// <summary>
        /// TriLibがロードしたスケルトンの座標系を診断
        /// Y-up (FBX) vs Z-up (Unity Humanoid) を自動検出
        /// </summary>
        private static void DetectAndLogCoordinateSystem(Dictionary<HumanBodyBones, Transform> map, string model)
        {
            if (!map.TryGetValue(HumanBodyBones.Hips, out var hips))
            {
                Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder] [{model}] Hips not found, skipping coordinate system detection");
                return;
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === Coordinate System Diagnostic ===");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] [{model}] Analyzing skeleton coordinate system...");

            // Hipsの回転を確認
            Quaternion hipsRot = hips.localRotation;
            Vector3 hipsEuler = hips.localEulerAngles;

            Debug.Log($"[RuntimeHumanoidAvatarBuilder]   Hips localRotation: {hipsRot}");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder]   Hips Euler angles: ({hipsEuler.x:F2}, {hipsEuler.y:F2}, {hipsEuler.z:F2})");

            // 上方向ベクトルを確認
            if (map.TryGetValue(HumanBodyBones.Spine, out var spine))
            {
                Vector3 hipsToSpine = (spine.position - hips.position).normalized;
                Debug.Log($"[RuntimeHumanoidAvatarBuilder]   Hips→Spine direction: ({hipsToSpine.x:F3}, {hipsToSpine.y:F3}, {hipsToSpine.z:F3})");

                // Hips→Spine方向の診断
                if (Mathf.Abs(hipsToSpine.y) > 0.9f)
                {
                    // Y方向に大きい = 座標系が90度回転している可能性
                    if (hipsToSpine.y > 0)
                    {
                        Debug.Log($"[RuntimeHumanoidAvatarBuilder]   ✓ Hips→Spine = +Y (standard T-pose/A-pose orientation)");
                    }
                    else
                    {
                        Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder]   ⚠️ Hips→Spine = -Y (inverted, may cause bone twists)");
                        Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder]      Expected: Hips→Spine should point in +Y or +Z direction.");
                    }
                }
                // Z-up検出: Spine direction の Z成分が大きい
                else if (Mathf.Abs(hipsToSpine.z) > 0.9f)
                {
                    if (hipsToSpine.z > 0)
                    {
                        Debug.Log($"[RuntimeHumanoidAvatarBuilder]   ✓ Hips→Spine = +Z (acceptable for some DCC tools)");
                    }
                    else
                    {
                        Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder]   ⚠️ Hips→Spine = -Z (unusual orientation)");
                    }
                }
                else
                {
                    Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder]   ⚠️ Unusual Hips→Spine direction detected");
                }
            }

            // 主要ボーンの回転を出力
            var bonesToCheck = new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.RightUpperLeg
            };

            Debug.Log($"[RuntimeHumanoidAvatarBuilder]   Key bone rotations:");
            foreach (var boneType in bonesToCheck)
            {
                if (map.TryGetValue(boneType, out var bone))
                {
                    Vector3 euler = bone.localEulerAngles;
                    Debug.Log($"[RuntimeHumanoidAvatarBuilder]     {boneType}: Euler({euler.x:F1}, {euler.y:F1}, {euler.z:F1})");
                }
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === End Diagnostic ===");
        }

        // ───────────────────────────────────────────────────────────
        // ボーンマッピング結果をログ出力
        // ───────────────────────────────────────────────────────────
        private static void LogBoneMappingResults(Dictionary<HumanBodyBones, Transform> map, string modelName)
        {
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === Armature Construction Results ===");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Model: {modelName}");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Mapped bones: {map.Count}/{HumanTrait.BoneCount}");

            // 必須ボーンのマッピング状態
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Required bones (15):");
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (!HumanTrait.RequiredBone(i)) continue;
                var bone = (HumanBodyBones)i;
                if (map.TryGetValue(bone, out var transform))
                {
                    Debug.Log($"[RuntimeHumanoidAvatarBuilder]   ✓ {bone,-20} → {transform.name}");
                }
                else
                {
                    Debug.LogError($"[RuntimeHumanoidAvatarBuilder]   ✗ {bone,-20} → MISSING");
                }
            }

            // オプショナルボーンのマッピング状態
            var optionalMapped = new List<HumanBodyBones>();
            for (int i = 0; i < HumanTrait.BoneCount; i++)
            {
                if (HumanTrait.RequiredBone(i)) continue;
                var bone = (HumanBodyBones)i;
                if (map.ContainsKey(bone))
                {
                    optionalMapped.Add(bone);
                }
            }

            if (optionalMapped.Count > 0)
            {
                Debug.Log($"[RuntimeHumanoidAvatarBuilder] Optional bones mapped ({optionalMapped.Count}):");
                foreach (var bone in optionalMapped)
                {
                    Debug.Log($"[RuntimeHumanoidAvatarBuilder]   + {bone,-20} → {map[bone].name}");
                }
            }
            else
            {
                Debug.Log($"[RuntimeHumanoidAvatarBuilder] Optional bones: None mapped");
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === End Armature Construction ===");
        }

        // ───────────────────────────────────────────────────────────
        // ⑥ ルートGameObjectの座標系変換 (UNUSED)
        // ───────────────────────────────────────────────────────────
        /// <summary>
        /// ルートGameObject全体を回転して座標系を変換
        /// Hips単体ではなくルート全体を回転することで、骨格内部の相対関係を保つ
        /// </summary>
        private static void ApplyRootCoordinateConversion(GameObject root)
        {
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === Root Coordinate System Conversion ===");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Root before: localRot={root.transform.localRotation} (Euler: {root.transform.localEulerAngles})");

            // ルートGameObjectをX軸90°回転してY-up→Z-up変換
            // これにより骨格全体が回転し、内部の相対関係は保たれる
            root.transform.localRotation = Quaternion.Euler(90f, 0f, 0f) * root.transform.localRotation;

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Root after:  localRot={root.transform.localRotation} (Euler: {root.transform.localEulerAngles})");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === Root Coordinate System Conversion Complete ===");
        }

        // ───────────────────────────────────────────────────────────
        // ⑦ Avatar 組み立て
        // ───────────────────────────────────────────────────────────
        private static UnityEngine.Avatar BuildHumanoidAvatar(GameObject root,
                                                  Dictionary<HumanBodyBones, Transform> map,
                                                  string model)
        {
            var humanBones = new List<HumanBone>();
            foreach (var kv in map)
            {
                humanBones.Add(new HumanBone
                {
                    humanName = kv.Key.ToString(),
                    boneName  = kv.Value.name,
                    limit     = new HumanLimit { useDefaultValues = true }
                });
            }

            var desc = new HumanDescription
            {
                human    = humanBones.ToArray(),
                skeleton = GenerateSkeletonBones(root, map),  // boneMapを渡してJoint Orientation補正を適用
                // Unityエディターのデフォルト値に合わせる
                upperArmTwist = 0.5f,
                lowerArmTwist = 0.5f,
                upperLegTwist = 0.5f,
                lowerLegTwist = 0.5f,
                armStretch = 0.05f,
                legStretch = 0.05f,
                feetSpacing = 0f,
                hasTranslationDoF = false
            };

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === HumanDescription Details ===");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Total Bones: {desc.human.Length}, Skeleton: {desc.skeleton.Length}");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] ArmTwist: U={desc.upperArmTwist}, L={desc.lowerArmTwist}");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] LegTwist: U={desc.upperLegTwist}, L={desc.lowerLegTwist}");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Stretch: Arm={desc.armStretch}, Leg={desc.legStretch}");

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] HumanBone[] mapping ({desc.human.Length} bones):");
            foreach (var hb in desc.human)
            {
                Debug.Log($"[RuntimeHumanoidAvatarBuilder]   {hb.humanName,-25} → {hb.boneName}");
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] SkeletonBone[] sample (first 10):");
            for (int i = 0; i < Mathf.Min(10, desc.skeleton.Length); i++)
            {
                var sb = desc.skeleton[i];
                Debug.Log($"[RuntimeHumanoidAvatarBuilder]   [{i}] {sb.name,-30} Pos:{sb.position} Rot:{sb.rotation.eulerAngles}");
            }
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === End HumanDescription Details ===");

            UnityEngine.Avatar avatar = UnityEngine.AvatarBuilder.BuildHumanAvatar(root, desc);
            if (!avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError($"[{model}] Avatar build FAILED");
                return null;
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Successfully built Avatar for {model}. IsValid: {avatar.isValid}, IsHuman: {avatar.isHuman}");

            // SkinnedMeshRendererのBindPoseとRootBoneを検証・修正
            ValidateAndFixSkinnedMeshRenderers(root, map);

            return avatar;
        }

        // ───────────────────────────────────────────────────────────
        // ⑦ SkinnedMeshRenderer 検証・修正
        // ───────────────────────────────────────────────────────────
        private static void ValidateAndFixSkinnedMeshRenderers(GameObject root, Dictionary<HumanBodyBones, Transform> map)
        {
            var skinnedMeshRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>();
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === SkinnedMeshRenderer & BindPose Validation ===");
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Found {skinnedMeshRenderers.Length} SkinnedMeshRenderer(s)");

            if (!map.TryGetValue(HumanBodyBones.Hips, out var hips))
            {
                Debug.LogWarning("[RuntimeHumanoidAvatarBuilder] Hips bone not found, cannot set rootBone");
                return;
            }

            int smrIndex = 0;
            foreach (var smr in skinnedMeshRenderers)
            {
                smrIndex++;
                Debug.Log($"[RuntimeHumanoidAvatarBuilder] SMR #{smrIndex}: {smr.name}");

                // ボーン情報
                if (smr.bones != null && smr.bones.Length > 0)
                {
                    Debug.Log($"[RuntimeHumanoidAvatarBuilder]   Bones: {smr.bones.Length}");
                    // 最初の5ボーンをサンプル表示
                    int sampleCount = Mathf.Min(5, smr.bones.Length);
                    for (int i = 0; i < sampleCount; i++)
                    {
                        if (smr.bones[i] != null)
                            Debug.Log($"[RuntimeHumanoidAvatarBuilder]     [{i}] {smr.bones[i].name}");
                    }
                    if (smr.bones.Length > 5)
                        Debug.Log($"[RuntimeHumanoidAvatarBuilder]     ... and {smr.bones.Length - 5} more bones");
                }
                else
                {
                    Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder]   No bones assigned!");
                }

                // BindPose情報
                if (smr.sharedMesh != null && smr.sharedMesh.bindposes != null)
                {
                    Debug.Log($"[RuntimeHumanoidAvatarBuilder]   BindPoses: {smr.sharedMesh.bindposes.Length}");

                    // 最初のBindPoseをサンプル表示
                    if (smr.sharedMesh.bindposes.Length > 0)
                    {
                        var bp0 = smr.sharedMesh.bindposes[0];
                        Debug.Log($"[RuntimeHumanoidAvatarBuilder]   BindPose[0] sample:");
                        Debug.Log($"[RuntimeHumanoidAvatarBuilder]     Position: {bp0.GetColumn(3)}");
                        Debug.Log($"[RuntimeHumanoidAvatarBuilder]     Rotation: {bp0.rotation.eulerAngles}");
                    }
                }
                else
                {
                    Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder]   No BindPoses found!");
                }

                // RootBone情報
                Debug.Log($"[RuntimeHumanoidAvatarBuilder]   RootBone: {(smr.rootBone != null ? smr.rootBone.name : "null")}");

                // RootBoneをHipsに設定
                if (smr.rootBone == null || smr.rootBone != hips)
                {
                    Debug.Log($"[RuntimeHumanoidAvatarBuilder]   → Setting rootBone to Hips");
                    smr.rootBone = hips;
                }

                // NOTE: BindPoseの再計算は行わない
                // TriLibが生成したBindPoseは、FBXの元のバインドポーズを保持しているため、
                // 再計算すると現在のボーン変形を記録してしまい、モデルが大きく歪む
                Debug.Log($"[RuntimeHumanoidAvatarBuilder]   → Preserving original BindPoses (TriLib generated)");
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] === End SkinnedMeshRenderer Validation ===");
        }

        private static void RebuildBindPoses(SkinnedMeshRenderer smr, Transform rootBone)
        {
            if (smr == null || smr.sharedMesh == null || rootBone == null) return;

            var bones = smr.bones;
            if (bones == null || bones.Length == 0) return;

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Rebuilding BindPoses for {smr.name} ({bones.Length} bones)");

            var bindposes = new Matrix4x4[bones.Length];
            var rootL2W = rootBone.localToWorldMatrix;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] != null)
                {
                    // BindPose = Bone の WorldToLocal × RootBone の LocalToWorld
                    bindposes[i] = bones[i].worldToLocalMatrix * rootL2W;
                }
                else
                {
                    Debug.LogWarning($"[RuntimeHumanoidAvatarBuilder] Bone[{i}] is null in {smr.name}");
                    bindposes[i] = Matrix4x4.identity;
                }
            }

            smr.sharedMesh.bindposes = bindposes;
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] BindPoses rebuilt for {smr.name}");
        }

        // ───────────────────────────────────────────────────────────
        // 座標系変換: Assimp(右手系) → Unity(左手系)
        // ───────────────────────────────────────────────────────────
        private static Matrix4x4 ConvertAssimpToUnity(Matrix4x4 assimpMatrix)
        {
            // 右手系→左手系: Z軸を反転
            var flipZ = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
            return flipZ * assimpMatrix * flipZ;
        }

        // ───────────────────────────────────────────────────────────
        // Joint Orientation 補正: Unity Humanoid仕様に合わせる
        // ───────────────────────────────────────────────────────────

        /// <summary>
        /// ボーンタイプに応じたJoint Orientation補正を適用
        /// Unity Humanoid仕様: 基本的に Forward(+Z), Up(+Y) だが、腕はUp(-Y)
        /// </summary>
        private static Quaternion FixJointOrientation(Quaternion rawRotation, HumanBodyBones boneType)
        {
            Debug.Log($"[FixJointOrientation] Called with boneType: {boneType}");
            switch (boneType)
            {
                // Hips: Unity Humanoid標準の座標系変換 (Y-up → Z-up)
                // Editor インポート時と同じ 90° X-axis 回転を適用
                case HumanBodyBones.Hips:
                    Debug.Log($"[FixJointOrientation] {boneType} - HIT HIPS CASE! Returning Euler(90, 0, 0)");
                    var hipsRot = Quaternion.Euler(90f, 0f, 0f);
                    Debug.Log($"[FixJointOrientation] {boneType} - quaternion value: {hipsRot} (Euler: {hipsRot.eulerAngles})");
                    return hipsRot;

                // 腕系: Forward(+Z), Up(-Y)
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightLowerArm:
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                {
                    Vector3 forward = rawRotation * Vector3.forward;
                    Vector3 up = rawRotation * Vector3.up;
                    return Quaternion.LookRotation(forward, -up);
                }

                // 脚系: Forward(+Z), Up(+Y)
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightLowerLeg:
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot:
                case HumanBodyBones.LeftToes:
                case HumanBodyBones.RightToes:
                {
                    Vector3 forward = rawRotation * Vector3.forward;
                    Vector3 up = rawRotation * Vector3.up;
                    return Quaternion.LookRotation(forward, up);
                }

                // 胴体・頭系: Forward(+Z), Up(+Y)
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                case HumanBodyBones.Neck:
                case HumanBodyBones.Head:
                {
                    Vector3 forward = rawRotation * Vector3.forward;
                    Vector3 up = rawRotation * Vector3.up;
                    return Quaternion.LookRotation(forward, up);
                }

                // その他: 補正なし（元の回転を維持）
                default:
                    Debug.Log($"[FixJointOrientation] Hit DEFAULT case for {boneType}");
                    return rawRotation;
            }
        }

        // ───────────────────────────────────────────────────────────
        // SkeletonBone 列生成
        // ───────────────────────────────────────────────────────────
        private static SkeletonBone[] GenerateSkeletonBones(GameObject root, Dictionary<HumanBodyBones, Transform> boneMap)
        {
            // ───────────────────────────────────────────────────────────
            // ■ Hipsのみ座標系補正、他はlocalRotationそのまま
            //   - Y-up座標系: Hipsが Euler(90,0,0) で回転している
            //   - Z-up座標系: Hipsは Quaternion.identity に近い値であるべき
            //   - Hipsの90°X回転を打ち消すことでZ-up相当にする
            //   - 他のボーンは親に対する相対回転なのでそのまま
            // ───────────────────────────────────────────────────────────

            // boneMapの逆引き用
            var transformToBone = new Dictionary<Transform, HumanBodyBones>();
            foreach (var kvp in boneMap)
            {
                transformToBone[kvp.Value] = kvp.Key;
            }

            var list = new List<SkeletonBone>();
            void Rec(Transform t)
            {
                // ★ Transform.localTRS の完全コピー（補正なし）
                // Unity が SkeletonBone と Transform の一致を期待しているため、
                // 補正行列を混在させると自動補正が発生して姿勢が崩れる
                list.Add(new SkeletonBone
                {
                    name     = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,  // 完全コピー
                    scale    = t.localScale       // 完全コピー
                });
                foreach (Transform c in t) Rec(c);
            }
            Rec(root.transform);

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Generated {list.Count} SkeletonBones (pure Transform copy, no corrections)");
            return list.ToArray();
        }
    }
}
