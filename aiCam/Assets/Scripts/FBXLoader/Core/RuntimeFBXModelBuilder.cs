// RuntimeFBXModelBuilder.cs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cysharp.Threading.Tasks;
using Assimp;

namespace AICam.FBXLoader
{
    using UnityGO = UnityEngine.GameObject;

    public sealed class RuntimeFBXModelBuilder : IRuntimeFBXLoader
    {
        private readonly Dictionary<string, UnityEngine.Transform> _nameToTransform =
            new Dictionary<string, UnityEngine.Transform>(StringComparer.OrdinalIgnoreCase);

        private readonly List<UnityEngine.SkinnedMeshRenderer> _createdSmrs = new List<UnityEngine.SkinnedMeshRenderer>();

        private readonly Dictionary<string, List<string>> _meshNodeToMaterialNames = new Dictionary<string, List<string>>();

        private UnityEngine.Transform _rootTr;

        // Shader キャッシュ（起動時間短縮）
        private static UnityEngine.Shader _cachedStandardShader;

        public Dictionary<string, List<string>> GetMeshNodeToMaterialNames() => _meshNodeToMaterialNames;

        public async UniTask<UnityGO> LoadFBX(string fbxPath)
        {
            if (string.IsNullOrEmpty(fbxPath))
                throw new ArgumentNullException("fbxPath");
            if (!File.Exists(fbxPath))
                throw new FileNotFoundException("FBX not found: " + fbxPath);

            Assimp.Scene scene;

            await UniTask.SwitchToThreadPool();
            try
            {
                using (var importer = new AssimpContext())
                {
                    // FBX の Up/Front を Assimp に任せる（=0）
                    importer.SetConfig(new Assimp.Configs.IntegerPropertyConfig(
                        "AI_CONFIG_IMPORT_FBX_IGNORE_UP_DIRECTION", 0));

                    scene = importer.ImportFile(
                        fbxPath,
                        PostProcessSteps.Triangulate
                        | PostProcessSteps.CalculateTangentSpace
                        | PostProcessSteps.JoinIdenticalVertices
                        | PostProcessSteps.SortByPrimitiveType
                        | PostProcessSteps.RemoveRedundantMaterials
                        | PostProcessSteps.OptimizeMeshes
                        | PostProcessSteps.LimitBoneWeights
                    );
                }
            }
            catch (Exception e)
            {
                throw new Exception("Assimp import failed: " + e.Message, e);
            }

            await UniTask.SwitchToMainThread();

            _nameToTransform.Clear();
            _createdSmrs.Clear();
            _meshNodeToMaterialNames.Clear();
            _rootTr = null;

            // ★ 単一ルートで構築（pivot を作らない）
            var rootGo = BuildTransformHierarchy(scene.RootNode, null);
            rootGo.name = Path.GetFileNameWithoutExtension(fbxPath);
            _rootTr = rootGo.transform;

            AttachMeshesRecursive(scene.RootNode, scene, fbxPath);

            foreach (var smr in _createdSmrs)
                RebuildBindposes(smr);

            return rootGo;
        }

        // ---------------------------
        // Transform ツリー構築
        // ---------------------------
        private UnityGO BuildTransformHierarchy(Assimp.Node node, UnityEngine.Transform parent)
        {
            var go = new UnityGO(string.IsNullOrEmpty(node.Name) ? "Node" : node.Name);
            var tr = go.transform;
            if (parent != null) tr.SetParent(parent, false);

            var unityLocal = ToUnityMatrix(node.Transform);
            UnityEngine.Vector3 t, s;
            UnityEngine.Quaternion r;
            DecomposeTRS(unityLocal, out t, out r, out s);

            // === 右手系(Assimp) → 左手系(Unity) の最低限変換 ===
            // 位置は X 反転
            t.x = -t.x;

            // 回転は「左右反転」相当：Quaternion(-x, +y, +z, -w)
            r = new UnityEngine.Quaternion(-r.x, r.y, r.z, -r.w);

            tr.localPosition = t;
            tr.localRotation = r;
            tr.localScale = s;

            _nameToTransform[node.Name ?? string.Empty] = tr;

            for (int i = 0; i < node.ChildCount; i++)
                BuildTransformHierarchy(node.Children[i], tr);

            return go;
        }

        // ---------------------------
        // メッシュ適用
        // ---------------------------
        private void AttachMeshesRecursive(Assimp.Node node, Assimp.Scene scene, string fbxPath)
        {
            UnityEngine.Transform tr;
            if (_nameToTransform.TryGetValue(node.Name ?? string.Empty, out tr))
            {
                if (node.MeshCount > 0)
                {
                    bool hasBones = node.MeshIndices.Any(i => scene.Meshes[i].HasBones);

                    if (hasBones)
                    {
                        var smr = tr.gameObject.AddComponent<UnityEngine.SkinnedMeshRenderer>();
                        List<UnityEngine.Transform> bones;
                        UnityEngine.BoneWeight[] weights;
                        List<string> materialNames;
                        var mesh = BuildMeshWithBones(node.MeshIndices, scene, out bones, out weights, out materialNames);

                        // Material名をMeshNode名と紐付けて辞書に保存
                        if (materialNames.Count > 0)
                        {
                            _meshNodeToMaterialNames[tr.gameObject.name] = materialNames;
                        }

                        smr.sharedMesh = mesh;
                        smr.bones = bones.ToArray();

                        var hips = FindBone("Hips");
                        smr.rootBone = (hips != null) ? hips : tr;

                        mesh.boneWeights = weights;
                        ApplyFirstMaterial(scene, fbxPath, smr);
                        _createdSmrs.Add(smr);

                        // ルート（Hips）空間へベイクしてトランスフォーム差を相殺
                        UnityEngine.Matrix4x4 bake = smr.rootBone.worldToLocalMatrix * tr.localToWorldMatrix;

                        // === ★ X軸反転を打ち消す補正 ===
                        bake.m00 *= -1f;

                        // ベイク実行
                        MeshBakeUtil.BakeToSpace(mesh, bake, bake, true);

                        // ベイク後にゼロ化
                        tr.localPosition = UnityEngine.Vector3.zero;
                        tr.localRotation = UnityEngine.Quaternion.identity;
                        tr.localScale = UnityEngine.Vector3.one;
                    }
                    else
                    {
                        var mf = tr.gameObject.AddComponent<UnityEngine.MeshFilter>();
                        var mr = tr.gameObject.AddComponent<UnityEngine.MeshRenderer>();
                        mf.sharedMesh = BuildMeshNoBones(node.MeshIndices, scene);
                        ApplyFirstMaterial(scene, fbxPath, mr);
                    }
                }
            }

            for (int i = 0; i < node.ChildCount; i++)
                AttachMeshesRecursive(node.Children[i], scene, fbxPath);
        }

        // ---------------------------
        // 非スキンメッシュ構築
        // ---------------------------
        private UnityEngine.Mesh BuildMeshNoBones(IList<int> meshIndices, Assimp.Scene scene)
        {
            var verts = new List<UnityEngine.Vector3>();
            var norms = new List<UnityEngine.Vector3>();
            var uvs = new List<UnityEngine.Vector2>();
            var tris = new List<int>();

            int vOffset = 0;

            foreach (var idx in meshIndices)
            {
                var m = scene.Meshes[idx];
                verts.AddRange(m.Vertices.Select(v => new UnityEngine.Vector3(v.X, v.Y, v.Z)));
                if (m.HasNormals) norms.AddRange(m.Normals.Select(n => new UnityEngine.Vector3(n.X, n.Y, n.Z)));
                if (m.HasTextureCoords(0))
                    uvs.AddRange(m.TextureCoordinateChannels[0].Select(uv => new UnityEngine.Vector2(uv.X, uv.Y)));
                foreach (var f in m.Faces)
                {
                    if (f.IndexCount == 3)
                    {
                        tris.Add(f.Indices[0] + vOffset);
                        tris.Add(f.Indices[1] + vOffset);
                        tris.Add(f.Indices[2] + vOffset);
                    }
                }
                vOffset += m.VertexCount;
            }

            var mesh = new UnityEngine.Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            if (norms.Count == verts.Count) mesh.SetNormals(norms);
            if (uvs.Count == verts.Count) mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();

            // Issue #456: 一時リスト即時解放（メモリピーク削減）
            verts.Clear(); verts.TrimExcess();
            norms.Clear(); norms.TrimExcess();
            uvs.Clear(); uvs.TrimExcess();
            tris.Clear(); tris.TrimExcess();

            return mesh;
        }

        // ---------------------------
        // スキンメッシュ構築
        // ---------------------------
        private UnityEngine.Mesh BuildMeshWithBones(
            IList<int> meshIndices,
            Assimp.Scene scene,
            out List<UnityEngine.Transform> boneTrs,
            out UnityEngine.BoneWeight[] bwArray,
            out List<string> materialNames)
        {
            boneTrs = new List<UnityEngine.Transform>();
            materialNames = new List<string>();
            var boneIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            var verts = new List<UnityEngine.Vector3>();
            var norms = new List<UnityEngine.Vector3>();
            var uvs = new List<UnityEngine.Vector2>();
            var tris = new List<int>();
            var influences = new List<List<(int, float)>>();

            int totalVtx = meshIndices.Sum(i => scene.Meshes[i].VertexCount);
            for (int i = 0; i < totalVtx; i++) influences.Add(new List<(int, float)>());
            int vOffset = 0;

            foreach (var idx in meshIndices)
            {
                var m = scene.Meshes[idx];

                // Material名を抽出
                if (m.MaterialIndex >= 0 && m.MaterialIndex < scene.MaterialCount)
                {
                    var material = scene.Materials[m.MaterialIndex];
                    if (!string.IsNullOrEmpty(material.Name))
                    {
                        materialNames.Add(material.Name);
                    }
                }

                verts.AddRange(m.Vertices.Select(v => new UnityEngine.Vector3(v.X, v.Y, v.Z)));
                if (m.HasNormals) norms.AddRange(m.Normals.Select(n => new UnityEngine.Vector3(n.X, n.Y, n.Z)));
                if (m.HasTextureCoords(0))
                    uvs.AddRange(m.TextureCoordinateChannels[0].Select(uv => new UnityEngine.Vector2(uv.X, uv.Y)));
                foreach (var f in m.Faces)
                {
                    if (f.IndexCount == 3)
                    {
                        tris.Add(f.Indices[0] + vOffset);
                        tris.Add(f.Indices[1] + vOffset);
                        tris.Add(f.Indices[2] + vOffset);
                    }
                }

                if (m.HasBones)
                {
                    foreach (var ab in m.Bones)
                    {
                        UnityEngine.Transform bt;
                        if (!_nameToTransform.TryGetValue(ab.Name ?? string.Empty, out bt)) continue;

                        int bIdx;
                        if (!boneIndexMap.TryGetValue(ab.Name, out bIdx))
                        {
                            boneTrs.Add(bt);
                            bIdx = boneTrs.Count - 1;
                            boneIndexMap[ab.Name] = bIdx;
                        }

                        foreach (var w in ab.VertexWeights)
                            influences[w.VertexID + vOffset].Add((bIdx, w.Weight));
                    }
                }

                vOffset += m.VertexCount;
            }

            bwArray = new UnityEngine.BoneWeight[influences.Count];
            for (int i = 0; i < influences.Count; i++)
            {
                var list = influences[i];
                if (list.Count > 1) list.Sort((a, b) => b.Item2.CompareTo(a.Item2));

                var bw = new UnityEngine.BoneWeight();
                float sum = 0f;
                for (int c = 0; c < list.Count && c < 4; c++)
                {
                    int idx = list[c].Item1; float w = list[c].Item2;
                    if (c == 0) { bw.boneIndex0 = idx; bw.weight0 = w; }
                    else if (c == 1) { bw.boneIndex1 = idx; bw.weight1 = w; }
                    else if (c == 2) { bw.boneIndex2 = idx; bw.weight2 = w; }
                    else { bw.boneIndex3 = idx; bw.weight3 = w; }
                    sum += w;
                }
                if (sum > 0f)
                {
                    float inv = 1f / sum;
                    bw.weight0 *= inv; bw.weight1 *= inv; bw.weight2 *= inv; bw.weight3 *= inv;
                }
                bwArray[i] = bw;
            }

            var mesh = new UnityEngine.Mesh();
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            if (norms.Count == verts.Count) mesh.SetNormals(norms);
            if (uvs.Count == verts.Count) mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();

            // Issue #456: 一時リスト即時解放（メモリピーク削減）
            // 特に influences は入れ子リストで大量のメモリを使用するため重要
            verts.Clear(); verts.TrimExcess();
            norms.Clear(); norms.TrimExcess();
            uvs.Clear(); uvs.TrimExcess();
            tris.Clear(); tris.TrimExcess();
            foreach (var inf in influences) { inf.Clear(); inf.TrimExcess(); }
            influences.Clear(); influences.TrimExcess();

            return mesh;
        }

        // ---------------------------
        // Matrix utils
        // ---------------------------
        private static UnityEngine.Matrix4x4 ToUnityMatrix(Assimp.Matrix4x4 m)
        {
            UnityEngine.Matrix4x4 u = new UnityEngine.Matrix4x4();
            u.m00 = m.A1; u.m01 = m.A2; u.m02 = m.A3; u.m03 = m.A4;
            u.m10 = m.B1; u.m11 = m.B2; u.m12 = m.B3; u.m13 = m.B4;
            u.m20 = m.C1; u.m21 = m.C2; u.m22 = m.C3; u.m23 = m.C4;
            u.m30 = m.D1; u.m31 = m.D2; u.m32 = m.D3; u.m33 = m.D4;
            return u;
        }

        private static void DecomposeTRS(UnityEngine.Matrix4x4 m, out UnityEngine.Vector3 t, out UnityEngine.Quaternion r, out UnityEngine.Vector3 s)
        {
            t = new UnityEngine.Vector3(m.m03, m.m13, m.m23);
            var x = new UnityEngine.Vector3(m.m00, m.m10, m.m20);
            var y = new UnityEngine.Vector3(m.m01, m.m11, m.m21);
            var z = new UnityEngine.Vector3(m.m02, m.m12, m.m22);
            float sx = x.magnitude; if (sx > 1e-8f) x /= sx; else sx = 1f;
            float sy = y.magnitude; if (sy > 1e-8f) y /= sy; else sy = 1f;
            float sz = z.magnitude; if (sz > 1e-8f) z /= sz; else sz = 1f;
            s = new UnityEngine.Vector3(sx, sy, sz);
            r = UnityEngine.Quaternion.LookRotation(z, y);
        }

        private UnityEngine.Transform FindBone(string name)
        {
            UnityEngine.Transform tr;
            _nameToTransform.TryGetValue(name ?? string.Empty, out tr);
            return tr;
        }

        private static void ApplyFirstMaterial(Assimp.Scene scene, string fbxPath, UnityEngine.Renderer renderer)
        {
            // キャッシュされたシェーダーを使用（起動時間短縮）
            if (_cachedStandardShader == null)
                _cachedStandardShader = UnityEngine.Shader.Find("Standard");
            if (_cachedStandardShader == null) return;
            var mat = new UnityEngine.Material(_cachedStandardShader);
            renderer.sharedMaterial = mat;
        }

        private static void RebuildBindposes(UnityEngine.SkinnedMeshRenderer smr)
        {
            if (smr == null || smr.sharedMesh == null || smr.rootBone == null) return;
            var bones = smr.bones ?? new UnityEngine.Transform[0];
            if (bones.Length == 0) return;
            var rootL2W = smr.rootBone.localToWorldMatrix;
            var bindposes = new UnityEngine.Matrix4x4[bones.Length];
            for (int i = 0; i < bones.Length; i++)
                bindposes[i] = (bones[i] != null)
                    ? bones[i].worldToLocalMatrix * rootL2W
                    : UnityEngine.Matrix4x4.identity;
            smr.sharedMesh.bindposes = bindposes;
        }
    }

    // ==== メッシュベイクユーティリティ ====
    static class MeshBakeUtil
    {
        public static void BakeToSpace(
            UnityEngine.Mesh mesh,
            UnityEngine.Matrix4x4 bake,
            UnityEngine.Matrix4x4 bake3x3,
            bool fixWindingIfReflected)
        {
            var verts = mesh.vertices;
            for (int i = 0; i < verts.Length; i++)
                verts[i] = bake.MultiplyPoint3x4(verts[i]);
            mesh.vertices = verts;

            if (mesh.normals != null && mesh.normals.Length == verts.Length)
            {
                var norms = mesh.normals;
                for (int i = 0; i < norms.Length; i++)
                    norms[i] = (bake3x3.MultiplyVector(norms[i])).normalized;
                mesh.normals = norms;
            }

            if (mesh.tangents != null && mesh.tangents.Length == verts.Length)
            {
                var tans = mesh.tangents;
                for (int i = 0; i < tans.Length; i++)
                {
                    var t3 = new UnityEngine.Vector3(tans[i].x, tans[i].y, tans[i].z);
                    var t3b = (bake3x3.MultiplyVector(t3)).normalized;
                    tans[i] = new UnityEngine.Vector4(t3b.x, t3b.y, t3b.z, tans[i].w);
                }
                mesh.tangents = tans;
            }

            // 反射対策（三角の向き入替え）
            float det =
                bake.m00 * (bake.m11 * bake.m22 - bake.m12 * bake.m21) -
                bake.m01 * (bake.m10 * bake.m22 - bake.m12 * bake.m20) +
                bake.m02 * (bake.m10 * bake.m21 - bake.m11 * bake.m20);

            if (fixWindingIfReflected && det < 0f)
            {
                for (int si = 0; si < mesh.subMeshCount; si++)
                {
                    var inds = mesh.GetIndices(si);
                    for (int k = 0; k + 2 < inds.Length; k += 3)
                    {
                        int tmp = inds[k + 1];
                        inds[k + 1] = inds[k + 2];
                        inds[k + 2] = tmp;
                    }
                    mesh.SetIndices(inds, mesh.GetTopology(si), si, true);
                }
            }

            mesh.RecalculateBounds();
        }
    }
}
