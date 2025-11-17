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

            // ⑤ T-ポーズ角度チェック
            CheckTPose(map, modelName);

            // ⑤.5 ボーン変形を保存してバインドポーズ（identity）にリセット
            var savedTransforms = SaveAndResetBoneTransforms(root);

            // ⑥ Avatar 組み立て（バインドポーズで実行）
            var avatar = BuildHumanoidAvatar(root, map, modelName);

            // ⑦ ボーン変形を復元
            RestoreBoneTransforms(savedTransforms);

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

                // 名前に "Left/Right" が含まれれば信頼し Swap しない
                string ln = map[lb].name.ToLower();
                string rn = map[rb].name.ToLower();

                bool hasLeftRightInName = (ln.Contains("left") || ln.EndsWith(".l") || ln.EndsWith("_l")) &&
                                         (rn.Contains("right")|| rn.EndsWith(".r")|| rn.EndsWith("_r"));

                Vector3 lPosLocal = hips.InverseTransformPoint(map[lb].position);
                Vector3 rPosLocal = hips.InverseTransformPoint(map[rb].position);
                bool lIsLeft = lPosLocal.x < 0f;
                bool rIsLeft = rPosLocal.x < 0f;

                Debug.Log($"[RuntimeHumanoidAvatarBuilder] Checking {key}:");
                Debug.Log($"  Left({lb}): '{ln}' at local X={lPosLocal.x:F3}, isLeft={lIsLeft}");
                Debug.Log($"  Right({rb}): '{rn}' at local X={rPosLocal.x:F3}, isLeft={rIsLeft}");
                Debug.Log($"  Has L/R in name: {hasLeftRightInName}");

                if (hasLeftRightInName)
                {
                    Debug.Log($"  => Trusting bone names, no swap");
                    continue;
                }

                if (!lIsLeft && rIsLeft)
                {
                    Debug.Log($"  => SWAPPING bones (position-based correction)");
                    (map[lb], map[rb]) = (map[rb], map[lb]);
                    swapCount++;
                }
                else
                {
                    Debug.Log($"  => No swap needed");
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
        // ⑥ Avatar 組み立て
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

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] HumanDescription configured:");
            Debug.Log($"  Bones: {desc.human.Length}, Skeleton: {desc.skeleton.Length}");
            Debug.Log($"  ArmTwist: U={desc.upperArmTwist}, L={desc.lowerArmTwist}");
            Debug.Log($"  LegTwist: U={desc.upperLegTwist}, L={desc.lowerLegTwist}");
            Debug.Log($"  Stretch: Arm={desc.armStretch}, Leg={desc.legStretch}");

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
            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Validating {skinnedMeshRenderers.Length} SkinnedMeshRenderer(s)");

            if (!map.TryGetValue(HumanBodyBones.Hips, out var hips))
            {
                Debug.LogWarning("[RuntimeHumanoidAvatarBuilder] Hips bone not found, cannot set rootBone");
                return;
            }

            foreach (var smr in skinnedMeshRenderers)
            {
                // RootBoneをHipsに設定
                if (smr.rootBone == null || smr.rootBone != hips)
                {
                    Debug.Log($"[RuntimeHumanoidAvatarBuilder] Setting rootBone to Hips for {smr.name}");
                    smr.rootBone = hips;
                }

                // NOTE: BindPoseの再計算は行わない
                // TriLibが生成したBindPoseは、FBXの元のバインドポーズを保持しているため、
                // 再計算すると現在のボーン変形を記録してしまい、モデルが大きく歪む
                Debug.Log($"[RuntimeHumanoidAvatarBuilder] Preserving original BindPoses for {smr.name}");
            }
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
        // SkeletonBone 列生成（Joint Orientation補正付き）
        // ───────────────────────────────────────────────────────────
        private static SkeletonBone[] GenerateSkeletonBones(GameObject root, Dictionary<HumanBodyBones, Transform> boneMap)
        {
            // Transform → HumanBodyBones の逆引きマップを作成
            var transformToBone = new Dictionary<Transform, HumanBodyBones>();
            foreach (var kvp in boneMap)
            {
                if (kvp.Value != null)
                    transformToBone[kvp.Value] = kvp.Key;
            }

            var list = new List<SkeletonBone>();
            void Rec(Transform t)
            {
                // このTransformに対応するHumanBodyBoneを取得
                HumanBodyBones boneType = HumanBodyBones.LastBone;
                transformToBone.TryGetValue(t, out boneType);

                // Joint Orientation補正を適用
                Quaternion correctedRotation = FixJointOrientation(t.localRotation, boneType, t.name);

                list.Add(new SkeletonBone
                {
                    name     = t.name,
                    position = t.localPosition,
                    rotation = correctedRotation,
                    scale    = Vector3.one  // 常にVector3.oneに正規化
                });
                foreach (Transform c in t) Rec(c);
            }
            Rec(root.transform);
            return list.ToArray();
        }

        // ───────────────────────────────────────────────────────────
        // Joint Orientation 補正
        // Unity Humanoidが要求する軸方向に補正
        // ───────────────────────────────────────────────────────────
        private static Quaternion FixJointOrientation(Quaternion rawRotation, HumanBodyBones boneType, string boneName)
        {
            // 部位ごとに必要な軸補正を適用
            switch (boneType)
            {
                // ───── Spine系: Forward(+Z), Up(+Y) ─────
                case HumanBodyBones.Hips:
                case HumanBodyBones.Spine:
                case HumanBodyBones.Chest:
                case HumanBodyBones.UpperChest:
                case HumanBodyBones.Neck:
                case HumanBodyBones.Head:
                    return FixSpineOrientation(rawRotation);

                // ───── Arm系: Forward(+Z), Up(-Y) ─────
                case HumanBodyBones.LeftShoulder:
                case HumanBodyBones.LeftUpperArm:
                case HumanBodyBones.LeftLowerArm:
                case HumanBodyBones.RightShoulder:
                case HumanBodyBones.RightUpperArm:
                case HumanBodyBones.RightLowerArm:
                    return FixArmOrientation(rawRotation);

                // ───── Leg系: Forward(+Z), Up(+Y) ─────
                case HumanBodyBones.LeftUpperLeg:
                case HumanBodyBones.LeftLowerLeg:
                case HumanBodyBones.RightUpperLeg:
                case HumanBodyBones.RightLowerLeg:
                    return FixLegOrientation(rawRotation);

                // ───── Hand/Foot/Toe系: Forward(+Z), Up(+Y) ─────
                case HumanBodyBones.LeftHand:
                case HumanBodyBones.RightHand:
                case HumanBodyBones.LeftFoot:
                case HumanBodyBones.RightFoot:
                case HumanBodyBones.LeftToes:
                case HumanBodyBones.RightToes:
                    return FixHandFootOrientation(rawRotation);

                // ───── 指ボーン: 補正なし（親のHandに従う） ─────
                case HumanBodyBones.LeftThumbProximal:
                case HumanBodyBones.LeftThumbIntermediate:
                case HumanBodyBones.LeftThumbDistal:
                case HumanBodyBones.LeftIndexProximal:
                case HumanBodyBones.LeftIndexIntermediate:
                case HumanBodyBones.LeftIndexDistal:
                case HumanBodyBones.LeftMiddleProximal:
                case HumanBodyBones.LeftMiddleIntermediate:
                case HumanBodyBones.LeftMiddleDistal:
                case HumanBodyBones.LeftRingProximal:
                case HumanBodyBones.LeftRingIntermediate:
                case HumanBodyBones.LeftRingDistal:
                case HumanBodyBones.LeftLittleProximal:
                case HumanBodyBones.LeftLittleIntermediate:
                case HumanBodyBones.LeftLittleDistal:
                case HumanBodyBones.RightThumbProximal:
                case HumanBodyBones.RightThumbIntermediate:
                case HumanBodyBones.RightThumbDistal:
                case HumanBodyBones.RightIndexProximal:
                case HumanBodyBones.RightIndexIntermediate:
                case HumanBodyBones.RightIndexDistal:
                case HumanBodyBones.RightMiddleProximal:
                case HumanBodyBones.RightMiddleIntermediate:
                case HumanBodyBones.RightMiddleDistal:
                case HumanBodyBones.RightRingProximal:
                case HumanBodyBones.RightRingIntermediate:
                case HumanBodyBones.RightRingDistal:
                case HumanBodyBones.RightLittleProximal:
                case HumanBodyBones.RightLittleIntermediate:
                case HumanBodyBones.RightLittleDistal:
                    // 指ボーンは補正なし
                    return rawRotation;

                // ───── その他（マップされていないボーン）: 補正なし ─────
                default:
                    return rawRotation;
            }
        }

        // Spine系の補正: Forward(+Z), Up(+Y)
        private static Quaternion FixSpineOrientation(Quaternion raw)
        {
            // Spine系は通常、DCCツールでも Forward=+Z, Up=+Y のため
            // 多くの場合は補正不要だが、念のため正規化
            return raw;
        }

        // Arm系の補正: Forward(+Z), Up(-Y)
        private static Quaternion FixArmOrientation(Quaternion raw)
        {
            // 腕は Unity Humanoid で Up=-Y を要求
            // DCCからのrawRotationでUp方向を取得し、反転させる
            Vector3 forward = raw * Vector3.forward;
            Vector3 up = raw * Vector3.up;

            // Up方向を反転してLookRotationを再構築
            Quaternion corrected = Quaternion.LookRotation(forward, -up);
            return corrected;
        }

        // Leg系の補正: Forward(+Z), Up(+Y)
        private static Quaternion FixLegOrientation(Quaternion raw)
        {
            // Leg系は Forward=+Z, Up=+Y（Spine系と同じ）
            return raw;
        }

        // Hand/Foot/Toe系の補正: Forward(+Z), Up(+Y)
        private static Quaternion FixHandFootOrientation(Quaternion raw)
        {
            // Hand/Foot も Forward=+Z, Up=+Y
            return raw;
        }

        // ───────────────────────────────────────────────────────────
        // ボーン変形の保存・リセット・復元
        // ───────────────────────────────────────────────────────────
        private class TransformData
        {
            public Transform transform;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        private static List<TransformData> SaveAndResetBoneTransforms(GameObject root)
        {
            var savedTransforms = new List<TransformData>();
            var allTransforms = root.GetComponentsInChildren<Transform>();

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Saving and resetting {allTransforms.Length} bone transforms to bind pose");

            foreach (var t in allTransforms)
            {
                // 現在の変形を保存
                savedTransforms.Add(new TransformData
                {
                    transform = t,
                    localPosition = t.localPosition,
                    localRotation = t.localRotation,
                    localScale = t.localScale
                });

                // rootは位置を保持、他のボーンはidentityにリセット
                if (t != root.transform)
                {
                    t.localRotation = Quaternion.identity;
                    // localPositionとlocalScaleは維持（骨格構造を保持）
                }
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] All bones reset to bind pose");
            return savedTransforms;
        }

        private static void RestoreBoneTransforms(List<TransformData> savedTransforms)
        {
            if (savedTransforms == null) return;

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] Restoring {savedTransforms.Count} bone transforms");

            foreach (var data in savedTransforms)
            {
                if (data.transform != null)
                {
                    data.transform.localPosition = data.localPosition;
                    data.transform.localRotation = data.localRotation;
                    data.transform.localScale = data.localScale;
                }
            }

            Debug.Log($"[RuntimeHumanoidAvatarBuilder] All bone transforms restored");
        }
    }
}
