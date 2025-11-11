// csharp
/*******************************************************************************************
 *  RuntimeFBXLoader2.cs ― FBX → GameObject（SkinnedMeshRenderer）変換 + デバッグログ
 *  - Armature/直下子の localRotation(Euler) の Before/After を出力
 *  - 基底行列 basisB の列ベクトル（ex,ey,ez）と det(B) を出力
 *  - 指定ノード（既定: "Hips"）の mFbxLocal / mUnityLocal（行列とTRS）を出力
 *
 *  注意: 本コードはロギングが主目的です。姿勢補正（NormalizeArmature など）は行いません。
 *******************************************************************************************/
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif
using Assimp;

// ───────── エイリアス ─────────
using AssimpMaterial   = Assimp.Material;
using UnityMaterial    = UnityEngine.Material;
using AssimpMesh       = Assimp.Mesh;
using UnityMesh        = UnityEngine.Mesh;
using AssimpNode       = Assimp.Node;
using UnityNode        = UnityEngine.GameObject;
using AssimpScene      = Assimp.Scene;
using AssimpMatrix4x4  = Assimp.Matrix4x4;
using UnityMatrix4x4   = UnityEngine.Matrix4x4;
using UnityQuaternion  = UnityEngine.Quaternion;
using AssimpBone       = Assimp.Bone;

namespace AICam.FBXLoader
{
    using Cysharp.Threading.Tasks;

    public class RuntimeFBXLoader3 : IRuntimeFBXLoader
    {
        private const string MeshNodeTag = "MeshNode";

        // デバッグ設定
        private const bool DebugLogEnabled = true;            // 全体のON/OFF
        private const string DebugArmatureName = "Armature";  // Armature 名
        private const string DebugMatrixNodeName = "Hips";    // 詳細行列ログ対象ノード名
        private const bool OrientationDebugLog = false;       // 自動整列の補助ログ

        // Assimp ノード名 → Unity Transform
        private readonly Dictionary<string, Transform> nodeNameToTransform = new(StringComparer.OrdinalIgnoreCase);

        // MeshNode名 → Assimp Material名のリストを対応付ける辞書
        private readonly Dictionary<string, List<string>> meshNodeToMaterialNames = new();

        // 後段で rootBone を統一するために収集
        private readonly List<SkinnedMeshRenderer> createdSmrs = new();
        private Transform builtRootTransform;

        // MeshNode名とMaterial名のマッピング情報を取得
        public Dictionary<string, List<string>> GetMeshNodeToMaterialNames() => meshNodeToMaterialNames;

        // FBX->Unity 基底変換（Node.Metadata から構築）
        private UnityMatrix4x4 basisB    = UnityMatrix4x4.identity; // 直交行列を想定
        private UnityMatrix4x4 basisBinv = UnityMatrix4x4.identity; // 逆（直交なので転置で可）
        private float          basisDet  = 1f;                      // det(B)

        // Armature Euler Before スナップショット
        private Dictionary<Transform, Vector3> _armatureEulerBefore;

        // =====================================================================================
        // 公開 API（例）: ロードして GameObject を返す
        // =====================================================================================
        public async UniTask<UnityNode> LoadFBX(string fbxPath)
        {
            EnsureTagExists(MeshNodeTag);

            if (string.IsNullOrEmpty(fbxPath))
                throw new ArgumentNullException(nameof(fbxPath));
            if (!File.Exists(fbxPath))
                throw new FileNotFoundException($"FBX not found: {fbxPath}");

            // ---------- Assimp Import ----------
            AssimpScene scene;
            using (var importer = new AssimpContext())
            {
                scene = importer.ImportFile(
                    fbxPath,
                    PostProcessSteps.Triangulate |
                    PostProcessSteps.CalculateTangentSpace |
                    PostProcessSteps.JoinIdenticalVertices |
                    PostProcessSteps.SortByPrimitiveType |
                    PostProcessSteps.RemoveRedundantMaterials |
                    PostProcessSteps.OptimizeMeshes |
                    PostProcessSteps.LimitBoneWeights
                );
            }
            if (scene == null || scene.RootNode == null)
                throw new Exception("Assimp import failed (scene/root null)");

            // FBXメタデータ（Node.Metadata）から基底変換を決定
            BuildBasisFromFbxMetadata(scene);
            if (DebugLogEnabled) LogBasisInfo("basisB(resolved)", basisB);

            nodeNameToTransform.Clear();
            createdSmrs.Clear();
            builtRootTransform = null;

            // ---------- Unity Object 化 ----------
            UnityNode root = ProcessNode(scene.RootNode, null, scene, fbxPath);
            builtRootTransform = root.transform;
            root.name = Path.GetFileNameWithoutExtension(fbxPath);

            // デバッグ用: Armature/直下子の Euler BEFORE
            if (DebugLogEnabled) SnapshotArmatureEulerBefore();

            // 骨＋PCA で Z+前 / Y+上 に自動整列（必要であればログを付与）
            AutoOrientRootSmart();

            // デバッグ用: Armature/直下子の Euler AFTER
            if (DebugLogEnabled) LogArmatureEulerDiff();

            // 全SMRの rootBone を統一し、bindposes を再生成
            FinalizeRootBoneAndBindposes();

            return root;
        }

        // =====================================================================================
        // ノード → GameObject（再帰）
        // =====================================================================================
        private UnityNode ProcessNode(AssimpNode node, Transform parent, AssimpScene scene, string fbxPath)
        {
            var go = new UnityNode(node.Name);
            var tr = go.transform;

            if (parent) tr.SetParent(parent, false);

            // Assimp のローカル行列 → Unity（B*M*B^-1）→ 反射を回転に含めないTRS分解
            var mFbxLocal   = ConvertAssimpMatrix(node.Transform);
            var mUnityLocal = basisB * mFbxLocal * basisBinv;
            DecomposeTRS_NoReflection(mUnityLocal, out var t, out var q, out var s);
            tr.localPosition = t;
            tr.localRotation = q;
            tr.localScale    = s;

            if (DebugLogEnabled && string.Equals(node.Name, DebugMatrixNodeName, StringComparison.OrdinalIgnoreCase))
            {
                LogNodeMatrixDetail(node.Name, mFbxLocal, mUnityLocal);
            }

            nodeNameToTransform[node.Name] = tr;

            // ---------- メッシュがある場合 ----------
            if (node.MeshIndices.Count > 0)
            {
                var smr = go.AddComponent<SkinnedMeshRenderer>();

                UnityMesh mesh = BuildCombinedMesh(
                    node.MeshIndices,
                    scene,
                    out var bones,
                    out var boneWeights
                );

                smr.sharedMesh   = mesh;
                mesh.boneWeights = boneWeights;
                smr.bones        = bones.ToArray();

                // 先頭マテリアルのみ設定（必要なら拡張）
                if (scene.Materials?.Count > 0)
                    ApplyMaterialWithTexture(scene.Materials[0], smr, fbxPath);

                go.tag = MeshNodeTag;

                // 収集
                createdSmrs.Add(smr);
            }

            // ---------- 子ノード ----------
            foreach (var child in node.Children)
                ProcessNode(child, tr, scene, fbxPath);

            return go;
        }

        // =====================================================================================
        // 骨+PCAから正面/上を推定し、最上位Transformを補正（Z+前 / Y+上）
        // =====================================================================================
        private void AutoOrientRootSmart()
        {
            if (!builtRootTransform) return;

            // 1) 推定ベクトルを収集（骨優先、無ければPCA）
            bool hasSkeleton = TryEstimateAxesFromSkeleton(out var upS, out var rightS, out var fwdS);
            bool hasPca      = TryEstimateAxesFromPCA(out var upP, out var rightP, out var fwdP);

            // どちらも推定失敗なら終了
            if (!hasSkeleton && !hasPca) return;

            // 探索に使う推定セット（複数あれば両方スコアリングに使う）
            var estimates = new List<(Vector3 up, Vector3 right, Vector3 fwd, float wUp, float wRight, float wFwd)>();
            if (hasSkeleton) estimates.Add((upS, rightS, fwdS, 1.0f, 0.25f, 1.0f));
            if (hasPca)      estimates.Add((upP, rightP, fwdP, 0.8f, 0.2f, 0.8f));

            // 2) 90度回転の候補（右手系）を生成
            var candidates = GenerateRightAngleRotations();

            // 3) 各候補をスコアリングして最大を選ぶ
            UnityQuaternion best = UnityQuaternion.identity;
            float bestScore = float.NegativeInfinity;

            foreach (var q in candidates)
            {
                float score = 0f;
                foreach (var est in estimates)
                {
                    if (est.fwd   != Vector3.zero) score += est.wFwd   * Vector3.Dot(q * est.fwd.normalized,   Vector3.forward);
                    if (est.up    != Vector3.zero) score += est.wUp    * Vector3.Dot(q * est.up.normalized,    Vector3.up);
                    if (est.right != Vector3.zero) score += est.wRight * Vector3.Dot(q * est.right.normalized, Vector3.right);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    best = q;
                }
            }

            // 4) 適用（十分な変化のみ）
            if (UnityQuaternion.Angle(UnityQuaternion.identity, best) > 0.5f)
            {
                if (OrientationDebugLog)
                    Debug.Log($"[FBXLoader] AutoOrient: best score={bestScore:F3}, rotEuler={best.eulerAngles}");
                builtRootTransform.rotation = best * builtRootTransform.rotation;
            }
        }

        // 骨から Up/Right/Forward を推定（複数の骨組を利用）
        private bool TryEstimateAxesFromSkeleton(out Vector3 up, out Vector3 right, out Vector3 fwd)
        {
            up = Vector3.zero; right = Vector3.zero; fwd = Vector3.zero;

            // 代表ボーン取得
            Transform hips       = FindByNames("Hips", "Pelvis");
            Transform head       = FindByNames("Head", "Neck");
            Transform lShoulder  = FindByNames("LeftShoulder", "LeftUpperArm", "L_Arm", "Shoulder_L");
            Transform rShoulder  = FindByNames("RightShoulder","RightUpperArm","R_Arm", "Shoulder_R");
            Transform lHip       = FindByNames("LeftUpperLeg","LeftThigh","L_Leg","LeftHip");
            Transform rHip       = FindByNames("RightUpperLeg","RightThigh","R_Leg","RightHip");

            bool ok = false;

            // Up: Head - Hips
            if (hips && head)
            {
                var v = (head.position - hips.position);
                if (v.sqrMagnitude > 1e-6f) { up = v.normalized; ok = true; }
            }

            // Right: Shoulders または Hips 間
            if (lShoulder && rShoulder)
            {
                var v = (rShoulder.position - lShoulder.position);
                if (v.sqrMagnitude > 1e-6f) right = v.normalized;
            }
            if (right == Vector3.zero && lHip && rHip)
            {
                var v = (rHip.position - lHip.position);
                if (v.sqrMagnitude > 1e-6f) right = v.normalized;
            }

            // Forward: Up × Right（右手系）
            if (right != Vector3.zero && up != Vector3.zero)
                fwd = Vector3.Cross(up, right).normalized;

            return ok || right != Vector3.zero || fwd != Vector3.zero;
        }

        // PCAで Up/Right/Forward を推定（全SMRの頂点を使って主成分を得る）
        private bool TryEstimateAxesFromPCA(out Vector3 up, out Vector3 right, out Vector3 fwd)
        {
            up = right = fwd = Vector3.zero;

            var verts = new List<Vector3>(4096);
            foreach (var smr in createdSmrs)
            {
                if (!smr || smr.sharedMesh == null) continue;
                var m = smr.localToWorldMatrix;
                var vtx = smr.sharedMesh.vertices;
                if (vtx == null || vtx.Length == 0) continue;

                int step = Mathf.Max(1, vtx.Length / 4000); // 最大約4k頂点に間引き
                for (int i = 0; i < vtx.Length; i += step)
                    verts.Add(m.MultiplyPoint3x4(vtx[i]));
            }
            if (verts.Count < 10) return false;

            // 平均
            Vector3 mean = Vector3.zero;
            foreach (var v in verts) mean += v;
            mean /= verts.Count;

            // 共分散 3x3
            double xx=0,xy=0,xz=0, yy=0,yz=0, zz=0;
            foreach (var v in verts)
            {
                var d = v - mean;
                xx += d.x*d.x; xy += d.x*d.y; xz += d.x*d.z;
                yy += d.y*d.y; yz += d.y*d.z;
                zz += d.z*d.z;
            }

            // パワーイテレーションで最大固有ベクトル（Up候補）
            Vector3 e1 = PowerIterate(xx,xy,xz,yy,yz,zz);
            if (e1 == Vector3.zero) return false;

            // 2番目（Right候補）: 直交正規化して再度
            Vector3 e2 = PowerIterate(xx,xy,xz,yy,yz,zz, e1);
            if (e2 == Vector3.zero) return false;

            // 3番目（Forward候補）: 右手系
            Vector3 e3 = Vector3.Cross(e1, e2).normalized;
            if (e3 == Vector3.zero) return false;

            up    = e1.normalized;
            right = e2.normalized;
            fwd   = Vector3.Cross(up, right).normalized;

            if (OrientationDebugLog)
                Debug.Log($"[FBXLoader] PCA up={up}, right={right}, fwd={fwd}");
            return up != Vector3.zero && right != Vector3.zero && fwd != Vector3.zero;
        }

        // 3x3 対称行列のパワーイテレーション。avoid と直交化する場合あり
        private static Vector3 PowerIterate(double xx,double xy,double xz,double yy,double yz,double zz, Vector3 avoid = default)
        {
            Vector3 v = new Vector3(0.3f, 0.7f, 0.6f);
            if (avoid != default)
            {
                v -= Vector3.Dot(v, avoid) * avoid;
            }
            if (v == Vector3.zero) v = Vector3.right;
            v.Normalize();

            for (int i = 0; i < 16; i++)
            {
                var w = new Vector3(
                    (float)(xx*v.x + xy*v.y + xz*v.z),
                    (float)(xy*v.x + yy*v.y + yz*v.z),
                    (float)(xz*v.x + yz*v.y + zz*v.z)
                );
                if (avoid != default)
                    w -= Vector3.Dot(w, avoid) * avoid;

                float n = w.magnitude;
                if (n < 1e-8f) break;
                v = w / n;
            }
            return v.normalized;
        }

        // 90度回転の候補群（右手系のみ）
        private static IEnumerable<UnityQuaternion> GenerateRightAngleRotations()
        {
            var list = new List<UnityQuaternion>();
            int[] angles = { 0, 90, 180, 270 };
            foreach (int ax in angles)
            foreach (int ay in angles)
            foreach (int az in angles)
            {
                var q = UnityQuaternion.Euler(ax, ay, az);
                var m = UnityMatrix4x4.Rotate(q);
                var ex = new Vector3(m.m00, m.m10, m.m20);
                var ey = new Vector3(m.m01, m.m11, m.m21);
                var ez = new Vector3(m.m02, m.m12, m.m22);
                float det = Vector3.Dot(Vector3.Cross(ex, ey), ez);
                if (det > 0.5f)
                    list.Add(q);
            }
            return list;
        }

        private Transform FindByNames(params string[] names)
        {
            if (names == null || names.Length == 0) return null;
            foreach (var n in names)
                if (nodeNameToTransform.TryGetValue(n, out var t) && t) return t;
            foreach (var kv in nodeNameToTransform)
                foreach (var n in names)
                    if (kv.Key.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0 && kv.Value)
                        return kv.Value;
            return null;
        }

        // =====================================================================================
        // SMR 仕上げ（rootBone統一＋bindposes再生成）
        // =====================================================================================
        private void FinalizeRootBoneAndBindposes()
        {
            if (createdSmrs.Count == 0) return;

            // 1) Hips 優先
            Transform skeletonRoot = null;
            if (nodeNameToTransform.TryGetValue("Hips", out var hips) && hips) skeletonRoot = hips;

            // 2) Armature の最初の子
            if (!skeletonRoot) skeletonRoot = GetArmatureChild();

            // 3) 全SMRで使用している全ボーンの LCA
            if (!skeletonRoot)
            {
                var allBones = new HashSet<Transform>();
                foreach (var smr in createdSmrs)
                {
                    var bones = smr ? smr.bones : null;
                    if (bones == null) continue;
                    foreach (var b in bones) if (b) allBones.Add(b);
                }
                if (allBones.Count > 0)
                    skeletonRoot = FindCommonAncestor(allBones.ToList());
            }

            // 4) 最終手段
            if (!skeletonRoot)
                skeletonRoot = builtRootTransform != null ? builtRootTransform : createdSmrs[0].transform.root;

            // 一括適用
            foreach (var smr in createdSmrs)
            {
                if (!smr) continue;
                smr.rootBone = skeletonRoot;
                RebuildBindposesUnityLike(smr);
            }
        }

        private Transform GetArmatureChild()
        {
            if (nodeNameToTransform.TryGetValue("Armature", out var arm) && arm)
                return arm.childCount > 0 ? arm.GetChild(0) : arm;
            return null;
        }

        private static Transform FindCommonAncestor(IList<Transform> nodes)
        {
            if (nodes == null || nodes.Count == 0) return null;
            if (nodes.Count == 1) return nodes[0];

            var candidates = new HashSet<Transform>();
            var cur = nodes[0];
            while (cur != null) { candidates.Add(cur); cur = cur.parent; }

            for (int i = 1; i < nodes.Count; i++)
            {
                var set = new HashSet<Transform>();
                cur = nodes[i];
                while (cur != null) { set.Add(cur); cur = cur.parent; }
                candidates.IntersectWith(set);
                if (candidates.Count == 0) return null;
            }

            Transform best = null; int bestDepth = int.MinValue;
            foreach (var t in candidates)
            {
                int d = 0; var p = t;
                while (p != null) { d++; p = p.parent; }
                if (d > bestDepth) { bestDepth = d; best = t; }
            }
            return best;
        }

        public static void RebuildBindposesUnityLike(SkinnedMeshRenderer smr)
        {
            if (!smr || !smr.rootBone || !smr.sharedMesh) return;
            var bones = smr.bones ?? Array.Empty<Transform>();
            if (bones.Length == 0) return;

            var rootL2W = smr.rootBone.localToWorldMatrix;
            var bindposes = new UnityMatrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
            {
                var b = bones[i];
                bindposes[i] = b ? b.worldToLocalMatrix * rootL2W : UnityMatrix4x4.identity;
            }
            smr.sharedMesh.bindposes = bindposes;
        }

        // =====================================================================================
        // Mesh 構築（同ノード配下の複数メッシュを結合）
        // =====================================================================================
        private UnityMesh BuildCombinedMesh(
            IList<int> meshIdx,
            AssimpScene scene,
            out List<Transform> boneTrs,
            out BoneWeight[] bwArr)
        {
            boneTrs = new List<Transform>();

            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var tans  = new List<Vector4>();
            var uvs   = new List<Vector2>();
            var tris  = new List<int>();

            var boneNameToIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var vtxInfluences   = new List<List<(int idx, float w)>>();

            int totalVtx = 0;
            foreach (int i in meshIdx) totalVtx += scene.Meshes[i].VertexCount;
            for (int i = 0; i < totalVtx; i++) vtxInfluences.Add(new());

            int vOffset = 0;

            foreach (int idx in meshIdx)
            {
                AssimpMesh am = scene.Meshes[idx];

                // 頂点（基底変換を適用）
                foreach (var v in am.Vertices)
                    verts.Add(ApplyBasisToPoint(new Vector3(v.X, v.Y, v.Z)));

                // 法線
                if (am.HasNormals)
                {
                    foreach (var n in am.Normals)
                        norms.Add(ApplyBasisToVector(new Vector3(n.X, n.Y, n.Z)));
                }

                // 接線（w は反射時のみ反転）
                if (am.HasTangentBasis && am.Tangents != null && am.Tangents.Count == am.VertexCount)
                {
                    for (int i = 0; i < am.VertexCount; i++)
                    {
                        var t = am.Tangents[i];
                        tans.Add(ApplyBasisToTangent(new Vector4(t.X, t.Y, t.Z, 1)));
                    }
                }

                // UV0
                if (am.HasTextureCoords(0))
                {
                    foreach (var uv in am.TextureCoordinateChannels[0])
                        uvs.Add(new Vector2(uv.X, uv.Y));
                }

                // 三角形（det(B)<0 のときだけ反転）
                foreach (var f in am.Faces)
                {
                    if (f.IndexCount == 3)
                    {
                        if (basisDet < 0f)
                        {
                            tris.Add(f.Indices[0] + vOffset);
                            tris.Add(f.Indices[2] + vOffset);
                            tris.Add(f.Indices[1] + vOffset);
                        }
                        else
                        {
                            tris.Add(f.Indices[0] + vOffset);
                            tris.Add(f.Indices[1] + vOffset);
                            tris.Add(f.Indices[2] + vOffset);
                        }
                    }
                }

                // ボーン
                if (am.HasBones)
                {
                    foreach (AssimpBone ab in am.Bones)
                    {
                        if (!boneNameToIndex.TryGetValue(ab.Name, out int bIdx))
                        {
                            Transform bt = FindBoneTransform(ab.Name);
                            if (!bt)
                            {
                                Debug.LogWarning($"[FBXLoader] Bone '{ab.Name}' not found – skipped");
                                continue;
                            }

                            boneTrs.Add(bt);
                            bIdx = boneTrs.Count - 1;
                            boneNameToIndex[ab.Name] = bIdx;
                        }

                        foreach (var w in ab.VertexWeights)
                        {
                            vtxInfluences[w.VertexID + vOffset].Add((bIdx, w.Weight));
                        }
                    }
                }

                vOffset += am.VertexCount;
            }

            // BoneWeight 配列（上位4つ, 正規化）
            bwArr = new BoneWeight[vtxInfluences.Count];
            for (int i = 0; i < vtxInfluences.Count; i++)
            {
                var list = vtxInfluences[i];
                if (list.Count > 1) list.Sort((a, b) => b.w.CompareTo(a.w));

                BoneWeight bw = new();
                float sum = 0;
                for (int c = 0; c < list.Count && c < 4; c++)
                {
                    int idx = list[c].idx; float w = list[c].w;
                    switch (c)
                    {
                        case 0: bw.boneIndex0 = idx; bw.weight0 = w; break;
                        case 1: bw.boneIndex1 = idx; bw.weight1 = w; break;
                        case 2: bw.boneIndex2 = idx; bw.weight2 = w; break;
                        case 3: bw.boneIndex3 = idx; bw.weight3 = w; break;
                    }
                    sum += w;
                }
                if (sum > 1e-8f && Math.Abs(sum - 1f) > 1e-5f)
                {
                    float inv = 1f / sum;
                    bw.weight0 *= inv; bw.weight1 *= inv; bw.weight2 *= inv; bw.weight3 *= inv;
                }
                bwArr[i] = bw;
            }

            var uMesh = new UnityMesh
            {
                indexFormat = (verts.Count > 65535) ? UnityEngine.Rendering.IndexFormat.UInt32
                                                    : UnityEngine.Rendering.IndexFormat.UInt16
            };

            uMesh.SetVertices(verts);
            if (norms.Count == verts.Count) uMesh.SetNormals(norms); else uMesh.RecalculateNormals();
            if (tans.Count  == verts.Count) uMesh.SetTangents(tans);
            if (uvs.Count   == verts.Count) uMesh.SetUVs(0, uvs);
            uMesh.SetTriangles(tris, 0, true);
            uMesh.RecalculateBounds();

            return uMesh;
        }

        // =====================================================================================
        // タグ Utility（Editor 専用）
        // =====================================================================================
        private void EnsureTagExists(string tag)
        {
#if UNITY_EDITOR
            if (!TagExists(tag)) AddTag(tag);
#endif
        }
#if UNITY_EDITOR
        private static bool TagExists(string tag)
        {
            foreach (var t in InternalEditorUtility.tags)
                if (t == tag) return true;
            return false;
        }
        private static void AddTag(string tag)
        {
            SerializedObject tagMgr = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty tagsProp = tagMgr.FindProperty("tags");
            for (int i = 0; i < tagsProp.arraySize; i++)
                if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag) return;

            tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
            tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tag;
            tagMgr.ApplyModifiedProperties();
            Debug.Log($"[FBXLoader] Tag '{tag}' added.");
        }
#endif

        // =====================================================================================
        // Material / Texture
        // =====================================================================================
        private void ApplyMaterialWithTexture(AssimpMaterial aMat, SkinnedMeshRenderer rdr, string fbxPath)
        {
            Shader std = Shader.Find("Standard");
            if (!std) return;

            UnityMaterial mat = new UnityMaterial(std);

            if (aMat.GetMaterialTexture(TextureType.Diffuse, 0, out var texSlot))
            {
                string baseDir = Path.GetDirectoryName(fbxPath) ?? "";
                string texPath = Path.Combine(baseDir, texSlot.FilePath);
                Texture2D tex  = LoadTexture(texPath);
                if (tex) mat.mainTexture = tex;
            }
            rdr.material = mat;
        }

        private static Texture2D LoadTexture(string path)
        {
            if (!File.Exists(path)) return null;
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            return tex.LoadImage(File.ReadAllBytes(path)) ? tex : null;
        }

        // =====================================================================================
        // ボーン探索（完全一致→部分一致）
        // =====================================================================================
        private Transform FindBoneTransform(string name)
        {
            if (nodeNameToTransform.TryGetValue(name, out var tr) && tr) return tr;
            foreach (var kv in nodeNameToTransform)
                if (kv.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return kv.Value;
            return null;
        }

        // =====================================================================================
        // 変換ユーティリティ
        // =====================================================================================
        private static UnityMatrix4x4 ConvertAssimpMatrix(AssimpMatrix4x4 m)
        {
            // ここでは「転置せずに」Assimpの行値をそのまま列に詰める形（元実装互換の例）
            var u = new UnityMatrix4x4();
            u.m00 = m.A1; u.m01 = m.A2; u.m02 = m.A3; u.m03 = m.A4;
            u.m10 = m.B1; u.m11 = m.B2; u.m12 = m.B3; u.m13 = m.B4;
            u.m20 = m.C1; u.m21 = m.C2; u.m22 = m.C3; u.m23 = m.C4;
            u.m30 = m.D1; u.m31 = m.D2; u.m32 = m.D3; u.m33 = m.D4;
            return u;
        }

        // 反射を回転に入れない分解（det<0 のときスケールZへ符号を寄せる）
        private static void DecomposeTRS_NoReflection(UnityMatrix4x4 m, out Vector3 t, out UnityQuaternion r, out Vector3 s)
        {
            t = new Vector3(m.m03, m.m13, m.m23);

            var x = new Vector3(m.m00, m.m10, m.m20);
            var y = new Vector3(m.m01, m.m11, m.m21);
            var z = new Vector3(m.m02, m.m12, m.m22);

            float sx = x.magnitude; if (sx == 0f) sx = 1f; x /= sx;
            float sy = y.magnitude; if (sy == 0f) sy = 1f; y /= sy;
            float sz = z.magnitude; if (sz == 0f) sz = 1f; z /= sz;

            float det = Vector3.Dot(Vector3.Cross(x, y), z);
            if (det < 0f) { sz = -sz; z = -z; }

            var rotM = new UnityMatrix4x4();
            rotM.SetColumn(0, new Vector4(x.x, x.y, x.z, 0));
            rotM.SetColumn(1, new Vector4(y.x, y.y, y.z, 0));
            rotM.SetColumn(2, new Vector4(z.x, z.y, z.z, 0));
            rotM.m33 = 1f;

            r = UnityQuaternion.LookRotation(rotM.GetColumn(2), rotM.GetColumn(1));
            s = new Vector3(sx, sy, sz);
        }

        // =====================================================================================
        // 基底行列の構築（Node.Metadata をリフレクションで読む）
        // =====================================================================================
        private void BuildBasisFromFbxMetadata(AssimpScene scene)
        {
            // デフォルト：Front=-Z, Up=+Y（多くのFBXに一致）
            basisB    = UnityMatrix4x4.Scale(new Vector3(1, 1, -1));
            basisBinv = basisB;
            basisDet  = -1f;

            try
            {
                if (scene == null || scene.RootNode == null) return;

                bool found = TryBuildBasisFromNodeMetadata(scene.RootNode, out var B, out var det);
                if (!found)
                {
                    // 全ノード走査
                    var q = new Queue<AssimpNode>();
                    q.Enqueue(scene.RootNode);
                    while (q.Count > 0 && !found)
                    {
                        var n = q.Dequeue();
                        if (n != scene.RootNode && TryBuildBasisFromNodeMetadata(n, out B, out det))
                        {
                            found = true;
                            basisB = B; basisBinv = B.transpose; basisDet = det;
                            break;
                        }
                        foreach (var c in n.Children) q.Enqueue(c);
                    }
                }
                else
                {
                    basisB = B; basisBinv = B.transpose; basisDet = det;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FBXLoader] Metadata basis fallback used: {e.Message}");
            }
        }

        private static bool TryBuildBasisFromNodeMetadata(AssimpNode node, out UnityMatrix4x4 B, out float det)
        {
            B = UnityMatrix4x4.identity;
            det = 1f;
            if (node == null || node.Metadata == null) return false;

            if (TryReadAxisTriplet(node.Metadata, out int upAxis, out int upSign,
                                                  out int frontAxis, out int frontSign,
                                                  out int rightAxis, out int rightSign))
            {
                Vector3 AxisOf(int idx)
                {
                    switch (idx)
                    {
                        case 0: return Vector3.right;
                        case 1: return Vector3.up;
                        case 2: return Vector3.forward;
                        default: return Vector3.zero;
                    }
                }
                float S(int s) => s >= 0 ? 1f : -1f;

                Vector3 ex = AxisOf(rightAxis) * S(rightSign); // X
                Vector3 ey = AxisOf(upAxis)    * S(upSign);    // Y
                Vector3 ez = AxisOf(frontAxis) * S(frontSign); // Z

                B = new UnityMatrix4x4();
                B.SetColumn(0, new Vector4(ex.x, ex.y, ex.z, 0));
                B.SetColumn(1, new Vector4(ey.x, ey.y, ey.z, 0));
                B.SetColumn(2, new Vector4(ez.x, ez.y, ez.z, 0));
                B.SetColumn(3, new Vector4(0, 0, 0, 1));

                det = Vector3.Dot(Vector3.Cross(ex, ey), ez);
                return true;
            }
            return false;
        }

        // Metadata から int を取得（Assimpバージョン差吸収のためリフレクションで読む）
        private static bool TryGetMetaIntObject(object mdObj, string key, out int value)
        {
            value = 0;
            if (mdObj == null) return false;

            var mdType = mdObj.GetType();

            var contains = mdType.GetMethod("ContainsKey", new[] { typeof(string) });
            if (contains == null) return false;
            bool has = false;
            try { has = (bool)contains.Invoke(mdObj, new object[] { key }); }
            catch { has = false; }
            if (!has) return false;

            var indexer = mdType.GetProperty("Item");
            if (indexer == null) return false;

            object entry = null;
            try { entry = indexer.GetValue(mdObj, new object[] { key }); }
            catch { entry = null; }
            if (entry == null) return false;

            object data = null;
            var entryType = entry.GetType();
            var dataProp = entryType.GetProperty("Data");
            if (dataProp != null)
            {
                try { data = dataProp.GetValue(entry); } catch { data = null; }
            }
            if (data == null) data = entry;

            try
            {
                if (data is int i) { value = i; return true; }
                if (data is long l) { value = (int)l; return true; }
                if (data is short s) { value = s; return true; }
                if (data is byte b) { value = b; return true; }
                if (data is float f) { value = (int)Math.Round(f); return true; }
                if (data is double d) { value = (int)Math.Round(d); return true; }
                if (data is bool z) { value = z ? 1 : 0; return true; }
                if (data is string str && int.TryParse(str, out var si)) { value = si; return true; }
            }
            catch { /* ignore */ }

            return false;
        }

        // Up/Front/Right を読む（代替キーにも対応）
        private static bool TryReadAxisTriplet(object mdObj,
            out int upAxis, out int upSign,
            out int frontAxis, out int frontSign,
            out int rightAxis, out int rightSign)
        {
            upAxis = upSign = frontAxis = frontSign = rightAxis = rightSign = 0;

            bool ok =
                TryGetMetaIntObject(mdObj, "UpAxis", out upAxis) &&
                TryGetMetaIntObject(mdObj, "UpAxisSign", out upSign) &&
                TryGetMetaIntObject(mdObj, "FrontAxis", out frontAxis) &&
                TryGetMetaIntObject(mdObj, "FrontAxisSign", out frontSign) &&
                TryGetMetaIntObject(mdObj, "CoordAxis", out rightAxis) &&
                TryGetMetaIntObject(mdObj, "CoordAxisSign", out rightSign);

            if (ok) return true;

            ok =
                TryGetMetaIntObject(mdObj, "OriginalUpAxis", out upAxis) &&
                TryGetMetaIntObject(mdObj, "OriginalUpAxisSign", out upSign) &&
                TryGetMetaIntObject(mdObj, "OriginalFrontAxis", out frontAxis) &&
                TryGetMetaIntObject(mdObj, "OriginalFrontAxisSign", out frontSign) &&
                TryGetMetaIntObject(mdObj, "OriginalCoordAxis", out rightAxis) &&
                TryGetMetaIntObject(mdObj, "OriginalCoordAxisSign", out rightSign);

            return ok;
        }

        // ベクトル/頂点/接線に基底を適用
        private Vector3 ApplyBasisToPoint(Vector3 p)  => basisB.MultiplyPoint3x4(p);
        private Vector3 ApplyBasisToVector(Vector3 v) => basisB.MultiplyVector(v);
        private Vector4 ApplyBasisToTangent(Vector4 t)
        {
            var v = new Vector3(t.x, t.y, t.z);
            v = ApplyBasisToVector(v);
            float w = (basisDet < 0f) ? -t.w : t.w;
            return new Vector4(v.x, v.y, v.z, w);
        }

        // =====================================================================================
        // デバッグユーティリティ
        // =====================================================================================
        private void SnapshotArmatureEulerBefore()
        {
            var arm = FindByNames(DebugArmatureName);
            if (!arm) arm = builtRootTransform;
            if (!arm)
            {
                Debug.LogWarning("[FBXLoader/Debug] Armature が見つかりませんでした。");
                return;
            }

            _armatureEulerBefore = new Dictionary<Transform, Vector3>
            {
                [arm] = arm.localRotation.eulerAngles
            };
            for (int i = 0; i < arm.childCount; i++)
            {
                var c = arm.GetChild(i);
                _armatureEulerBefore[c] = c.localRotation.eulerAngles;
            }

            Debug.Log($"[FBXLoader/Debug] Armature Euler BEFORE captured: {arm.name} (+{arm.childCount} children)");
        }

        private void LogArmatureEulerDiff()
        {
            var arm = FindByNames(DebugArmatureName);
            if (!arm) arm = builtRootTransform;
            if (!arm)
            {
                Debug.LogWarning("[FBXLoader/Debug] Armature が見つからず、Euler差分を出力できません。");
                return;
            }
            if (_armatureEulerBefore == null)
            {
                Debug.LogWarning("[FBXLoader/Debug] BEFORE スナップショットが無いため差分を出力できません。");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine("=== [FBXLoader/Debug] Armature localRotation(Euler) Diff (Before -> After | Δ) ===");

            void PrintOne(Transform t)
            {
                var after = t.localRotation.eulerAngles;
                if (!_armatureEulerBefore.TryGetValue(t, out var before))
                    before = Vector3.positiveInfinity;

                var d = EulerDelta(before, after);
                sb.AppendLine($"{t.name,-24}  {FmtEuler(before)}  ->  {FmtEuler(after)}   | Δ {FmtEuler(d)}");
            }

            PrintOne(arm);
            for (int i = 0; i < arm.childCount; i++)
                PrintOne(arm.GetChild(i));

            Debug.Log(sb.ToString());
        }

        private void LogBasisInfo(string label, UnityMatrix4x4 B)
        {
            var ex = new Vector3(B.m00, B.m10, B.m20);
            var ey = new Vector3(B.m01, B.m11, B.m21);
            var ez = new Vector3(B.m02, B.m12, B.m22);
            float det = Vector3.Dot(Vector3.Cross(ex, ey), ez);

            var sb = new StringBuilder();
            sb.AppendLine($"=== [FBXLoader/Debug] {label} ===");
            sb.AppendLine($"ex:{FmtVec(ex)}  ey:{FmtVec(ey)}  ez:{FmtVec(ez)}  det:{det:F3}");
            sb.AppendLine(FormatMatrix(B, "B"));
            Debug.Log(sb.ToString());
        }

        private void LogNodeMatrixDetail(string nodeName, UnityMatrix4x4 mFbxLocal, UnityMatrix4x4 mUnityLocal)
        {
            DecomposeTRS_NoReflection(mUnityLocal, out var tU, out var rU, out var sU);

            bool TryDecompose(UnityMatrix4x4 m, out Vector3 t, out UnityQuaternion r, out Vector3 s)
            {
                t = m.GetColumn(3);
                var x = new Vector3(m.m00, m.m10, m.m20);
                var y = new Vector3(m.m01, m.m11, m.m21);
                var z = new Vector3(m.m02, m.m12, m.m22);
                float sx = x.magnitude; float sy = y.magnitude; float sz = z.magnitude;
                if (sx < 1e-8f || sy < 1e-8f || sz < 1e-8f) { r = UnityQuaternion.identity; s = Vector3.one; return false; }
                x /= sx; y /= sy; z /= sz;
                r = UnityQuaternion.LookRotation(z, y);
                s = new Vector3(sx, sy, sz);
                return true;
            }
            TryDecompose(mFbxLocal, out var tF, out var rF, out var sF);

            var sb = new StringBuilder();
            sb.AppendLine($"=== [FBXLoader/Debug] Node Matrix Detail: {nodeName} ===");
            sb.AppendLine(FormatMatrix(mFbxLocal, "mFbxLocal (raw→unity)"));
            sb.AppendLine($"Fbx TRS  T:{FmtVec(tF)}  R:{FmtEuler(rF.eulerAngles)}  S:{FmtVec(sF)}");
            sb.AppendLine(FormatMatrix(mUnityLocal, "mUnityLocal (= B * Fbx * B^-1)"));
            sb.AppendLine($"Unity TRS T:{FmtVec(tU)}  R:{FmtEuler(rU.eulerAngles)}  S:{FmtVec(sU)}");
            Debug.Log(sb.ToString());
        }

        private static string FormatMatrix(UnityMatrix4x4 m, string title)
        {
            return
$@"[{title}]
[{m.m00,8:F5} {m.m01,8:F5} {m.m02,8:F5} {m.m03,8:F5}]
[{m.m10,8:F5} {m.m11,8:F5} {m.m12,8:F5} {m.m13,8:F5}]
[{m.m20,8:F5} {m.m21,8:F5} {m.m22,8:F5} {m.m23,8:F5}]
[{m.m30,8:F5} {m.m31,8:F5} {m.m32,8:F5} {m.m33,8:F5}]";
        }

        private static string FmtVec(Vector3 v) => $"({v.x:F5},{v.y:F5},{v.z:F5})";
        private static string FmtEuler(Vector3 e) => $"({e.x:F3},{e.y:F3},{e.z:F3})";

        private static Vector3 EulerDelta(Vector3 before, Vector3 after)
        {
            float d(float a, float b)
            {
                float x = Mathf.Repeat(b - a + 180f, 360f) - 180f;
                return x;
            }
            return new Vector3(d(before.x, after.x), d(before.y, after.y), d(before.z, after.z));
        }
    }
}