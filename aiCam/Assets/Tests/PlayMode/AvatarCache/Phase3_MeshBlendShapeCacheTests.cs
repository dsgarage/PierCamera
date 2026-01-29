using System.Collections;
using System.Collections.Generic;
using System.IO;
using AICam.AvatarCache;
using AICam.AvatarCache.Serializers;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AICam.Tests.PlayMode.AvatarCache
{
    /// <summary>
    /// Phase 3: Mesh/BlendShape キャッシュテスト
    ///
    /// テスト対象:
    /// - メッシュデータのバイナリシリアライズ/デシリアライズ
    /// - BlendShapeデータのシリアライズ/デシリアライズ
    /// - キャッシュからのメッシュ再構築
    /// - ボーンウェイトの保存/復元
    /// </summary>
    [TestFixture]
    public class Phase3_MeshBlendShapeCacheTests : AvatarCacheTestBase
    {
        #region Mesh Extraction Tests

        [UnityTest]
        public IEnumerator メッシュ_ロード済みアバターから抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            Assert.IsNotNull(avatar);

            // Act
            var skinnedMeshRenderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var meshRenderers = avatar.GetComponentsInChildren<MeshRenderer>();

            // Assert
            Assert.IsTrue(skinnedMeshRenderers.Length > 0 || meshRenderers.Length > 0,
                "アバターにメッシュレンダラーが存在すべき");

            // MeshCacheSerializerでシリアライズ可能か確認
            var meshes = new List<Mesh>();
            int totalVertices = 0;
            foreach (var smr in skinnedMeshRenderers)
            {
                if (smr.sharedMesh != null)
                {
                    meshes.Add(smr.sharedMesh);
                    totalVertices += smr.sharedMesh.vertexCount;
                }
            }

            var tempPath = Path.Combine(TestCacheDirectory, "mesh_extract_test.bin");
            MeshCacheSerializer.SerializeToBinary(meshes.ToArray(), tempPath);

            Debug.Log($"[Phase3Test] SkinnedMeshRenderer数: {skinnedMeshRenderers.Length}, 総頂点数: {totalVertices}");
        });

        [UnityTest]
        public IEnumerator メッシュ_頂点と三角形を抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var smr = avatar.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.IsNotNull(smr?.sharedMesh, "SkinnedMeshRendererにメッシュが存在すべき");

            var mesh = smr.sharedMesh;

            // Act
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uvs = mesh.uv;
            var triangles = mesh.triangles;

            // Assert
            Assert.IsTrue(vertices.Length > 0, "頂点が存在すべき");
            Assert.AreEqual(vertices.Length, normals.Length, "法線数が頂点数と一致すべき");
            Assert.IsTrue(triangles.Length > 0, "三角形が存在すべき");
            Assert.IsTrue(triangles.Length % 3 == 0, "三角形数は3の倍数であるべき");

            // MeshCacheSerializerでシリアライズ可能か確認
            var tempPath = Path.Combine(TestCacheDirectory, "mesh_vertices_test.bin");
            MeshCacheSerializer.SerializeToBinary(new Mesh[] { mesh }, tempPath);

            Debug.Log($"[Phase3Test] メッシュ: {vertices.Length} 頂点, {triangles.Length / 3} 三角形");
        });

        #endregion

        #region Mesh Binary Serialization Tests

        [UnityTest]
        public IEnumerator メッシュ_MeshCacheSerializerでバイナリにシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var meshes = new List<Mesh>();
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh != null)
                    meshes.Add(smr.sharedMesh);
            }

            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var coreDir = Path.Combine(cacheDir, "core");
            Directory.CreateDirectory(coreDir);

            var meshesPath = Path.Combine(coreDir, "meshes.bin");

            // Act - 実際のMeshCacheSerializer.SerializeToBinaryを呼び出す
            MeshCacheSerializer.SerializeToBinary(meshes.ToArray(), meshesPath);

            // Assert
            AssertFileExists(meshesPath, "meshes.bin");
            Assert.IsTrue(MeshCacheSerializer.ValidateMagic(meshesPath), "MESHマジックが正しいこと");

            var fileSize = new FileInfo(meshesPath).Length;
            Debug.Log($"[Phase3Test] メッシュバイナリ保存: {fileSize / 1024}KB");
        });

        [UnityTest]
        public IEnumerator メッシュ_MeshCacheSerializerでバイナリからデシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var originalMeshes = new List<Mesh>();
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh != null)
                    originalMeshes.Add(smr.sharedMesh);
            }

            var tempPath = Path.Combine(TestCacheDirectory, "temp_mesh.bin");
            MeshCacheSerializer.SerializeToBinary(originalMeshes.ToArray(), tempPath);

            // Act - 実際のMeshCacheSerializer.DeserializeFromBinaryを呼び出す
            var loadedMeshes = MeshCacheSerializer.DeserializeFromBinary(tempPath);

            // Assert
            Assert.IsNotNull(loadedMeshes);
            Assert.AreEqual(originalMeshes.Count, loadedMeshes.Length, "メッシュ数が一致すべき");

            for (int i = 0; i < originalMeshes.Count; i++)
            {
                Assert.AreEqual(originalMeshes[i].vertexCount, loadedMeshes[i].vertexCount,
                    $"メッシュ{i}の頂点数が一致すべき");
            }

            Debug.Log($"[Phase3Test] メッシュデシリアライズ: {loadedMeshes.Length}メッシュ");

            // Cleanup
            foreach (var mesh in loadedMeshes)
            {
                Object.Destroy(mesh);
            }
        });

        [UnityTest]
        public IEnumerator メッシュ_マジックナンバー検証ができること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var coreDir = Path.Combine(cacheDir, "core");
            Directory.CreateDirectory(coreDir);

            var invalidPath = Path.Combine(coreDir, "invalid.bin");
            File.WriteAllText(invalidPath, "INVALID DATA");

            // Act & Assert
            Assert.IsFalse(MeshCacheSerializer.ValidateMagic(invalidPath),
                "無効なファイルはマジック検証に失敗すべき");

            Debug.Log("[Phase3Test] マジックナンバー検証成功");
        });

        #endregion

        #region BlendShape Tests

        [UnityTest]
        public IEnumerator BlendShape_メッシュから抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var smr = avatar.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = smr.sharedMesh;
            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            // Act
            var blendShapeCount = mesh.blendShapeCount;

            // BlendShapeCacheSerializerでシリアライズ可能か確認
            var tempPath = Path.Combine(TestCacheDirectory, "blendshape_extract_test.bin");
            BlendShapeCacheSerializer.SerializeToBinary(smrs, tempPath);

            // Assert
            Debug.Log($"[Phase3Test] BlendShape数: {blendShapeCount}");

            if (blendShapeCount > 0)
            {
                for (int i = 0; i < Mathf.Min(5, blendShapeCount); i++)
                {
                    var name = mesh.GetBlendShapeName(i);
                    var frameCount = mesh.GetBlendShapeFrameCount(i);
                    Debug.Log($"  BlendShape[{i}]: {name} ({frameCount} フレーム)");
                }
            }
        });

        [UnityTest]
        public IEnumerator BlendShape_BlendShapeCacheSerializerでバイナリにシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var coreDir = Path.Combine(cacheDir, "core");
            Directory.CreateDirectory(coreDir);

            var blendShapesPath = Path.Combine(coreDir, "blendshapes.bin");

            // Act - 実際のBlendShapeCacheSerializer.SerializeToBinaryを呼び出す
            BlendShapeCacheSerializer.SerializeToBinary(smrs, blendShapesPath);

            // Assert
            AssertFileExists(blendShapesPath, "blendshapes.bin");
            Assert.IsTrue(BlendShapeCacheSerializer.ValidateMagic(blendShapesPath),
                "BLNDマジックが正しいこと");

            var fileSize = new FileInfo(blendShapesPath).Length;
            Debug.Log($"[Phase3Test] BlendShapeバイナリ保存: {fileSize / 1024}KB");
        });

        [UnityTest]
        public IEnumerator BlendShape_BlendShapeCacheSerializerでデシリアライズして適用できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var originalMeshes = new List<Mesh>();
            foreach (var smr in smrs)
            {
                if (smr.sharedMesh != null)
                    originalMeshes.Add(smr.sharedMesh);
            }

            var tempPath = Path.Combine(TestCacheDirectory, "temp_blendshapes.bin");
            BlendShapeCacheSerializer.SerializeToBinary(smrs, tempPath);

            // 新しいメッシュを作成（名前もコピーしてマッチングに使用）
            var newMeshes = new Mesh[originalMeshes.Count];
            for (int i = 0; i < originalMeshes.Count; i++)
            {
                newMeshes[i] = new Mesh();
                newMeshes[i].name = originalMeshes[i].name;
                newMeshes[i].vertices = originalMeshes[i].vertices;
                newMeshes[i].normals = originalMeshes[i].normals;
                newMeshes[i].triangles = originalMeshes[i].triangles;
            }

            // Act - 実際のBlendShapeCacheSerializer.DeserializeAndApplyを呼び出す
            BlendShapeCacheSerializer.DeserializeAndApply(tempPath, newMeshes);

            // Assert
            for (int i = 0; i < originalMeshes.Count; i++)
            {
                Assert.AreEqual(originalMeshes[i].blendShapeCount, newMeshes[i].blendShapeCount,
                    $"メッシュ{i}のBlendShape数が一致すべき");
            }

            Debug.Log("[Phase3Test] BlendShapeデシリアライズ・適用成功");

            // Cleanup
            foreach (var mesh in newMeshes)
            {
                Object.Destroy(mesh);
            }
        });

        #endregion

        #region Bone Weights Tests

        [UnityTest]
        public IEnumerator ボーンウェイト_SkinnedMeshから抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var smr = avatar.GetComponentInChildren<SkinnedMeshRenderer>();
            var mesh = smr.sharedMesh;

            // Act
            var boneWeights = mesh.boneWeights;
            var bindPoses = mesh.bindposes;
            var bones = smr.bones;

            // Assert
            Assert.IsTrue(boneWeights.Length > 0, "ボーンウェイトが存在すべき");
            Assert.IsTrue(bindPoses.Length > 0, "バインドポーズが存在すべき");
            Assert.IsTrue(bones.Length > 0, "ボーンが存在すべき");
            Assert.AreEqual(mesh.vertexCount, boneWeights.Length, "ボーンウェイト数が頂点数と一致すべき");

            // MeshCacheSerializerでボーンウェイトを含めてシリアライズ可能か確認
            var tempPath = Path.Combine(TestCacheDirectory, "boneweight_test.bin");
            MeshCacheSerializer.SerializeToBinary(new Mesh[] { mesh }, tempPath);

            Debug.Log($"[Phase3Test] ボーンウェイト: {boneWeights.Length}, バインドポーズ: {bindPoses.Length}, ボーン: {bones.Length}");

            // ボーンウェイトの合計が1になるか確認（最初の10頂点）
            for (int i = 0; i < Mathf.Min(10, boneWeights.Length); i++)
            {
                var bw = boneWeights[i];
                var weightSum = bw.weight0 + bw.weight1 + bw.weight2 + bw.weight3;
                Assert.AreEqual(1f, weightSum, 0.01f, $"頂点 {i} のウェイト合計が約1.0であるべき");
            }
        });

        #endregion
    }
}
