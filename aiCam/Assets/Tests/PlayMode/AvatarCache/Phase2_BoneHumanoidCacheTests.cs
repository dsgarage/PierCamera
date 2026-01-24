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
    /// Phase 2: ボーン/Humanoid キャッシュテスト
    ///
    /// テスト対象:
    /// - HumanDescription のシリアライズ/デシリアライズ
    /// - ボーン階層情報のシリアライズ/デシリアライズ
    /// - キャッシュからのボーン再構築
    /// - Humanoid Avatarの再構築
    /// </summary>
    [TestFixture]
    public class Phase2_BoneHumanoidCacheTests : AvatarCacheTestBase
    {
        #region Bone Hierarchy Extraction Tests

        [UnityTest]
        public IEnumerator ボーン階層_BoneHierarchyCacheSerializerで抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            Assert.IsNotNull(avatar);

            // Act - 実際のBoneHierarchyCacheSerializer.ExtractFromAvatarを呼び出す
            var boneCache = BoneHierarchyCacheSerializer.ExtractFromAvatar(avatar);

            // Assert
            Assert.IsNotNull(boneCache);
            Assert.IsNotNull(boneCache.bones);
            Assert.IsTrue(boneCache.bones.Length > 0, "ボーンが抽出されるべき");

            Debug.Log($"[Phase2Test] 抽出したボーン数: {boneCache.bones.Length}");
        });

        [UnityTest]
        public IEnumerator ボーン階層_ルートボーンが存在すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();

            // Act
            var boneCache = BoneHierarchyCacheSerializer.ExtractFromAvatar(avatar);

            // Assert - ルートボーン（parentIndex == -1）を確認
            BoneInfo rootBone = null;
            foreach (var bone in boneCache.bones)
            {
                if (bone.parentIndex == -1)
                {
                    rootBone = bone;
                    break;
                }
            }

            Assert.IsNotNull(rootBone, "ルートボーンが存在すべき");
            Debug.Log($"[Phase2Test] ルートボーン: {rootBone.name}");
        });

        #endregion

        #region Bone Hierarchy Serialization Tests

        [UnityTest]
        public IEnumerator ボーン階層_JSONにシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var boneCache = BoneHierarchyCacheSerializer.ExtractFromAvatar(avatar);

            // Act - 実際のBoneHierarchyCacheSerializer.SerializeToJsonを呼び出す
            var json = BoneHierarchyCacheSerializer.SerializeToJson(boneCache);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsNotEmpty(json);
            Assert.IsTrue(json.Contains("bones"));
            Assert.IsTrue(json.Contains("localPosition"));
            Assert.IsTrue(json.Contains("localRotation"));

            Debug.Log($"[Phase2Test] ボーン階層シリアライズ成功: {boneCache.bones.Length} bones");
        });

        [UnityTest]
        public IEnumerator ボーン階層_JSONからデシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var originalCache = BoneHierarchyCacheSerializer.ExtractFromAvatar(avatar);
            var json = BoneHierarchyCacheSerializer.SerializeToJson(originalCache);

            // Act - 実際のBoneHierarchyCacheSerializer.DeserializeFromJsonを呼び出す
            var loadedCache = BoneHierarchyCacheSerializer.DeserializeFromJson(json);

            // Assert
            Assert.IsNotNull(loadedCache);
            Assert.AreEqual(originalCache.version, loadedCache.version);
            Assert.AreEqual(originalCache.bones.Length, loadedCache.bones.Length);

            // 各ボーンの値を検証
            for (int i = 0; i < originalCache.bones.Length; i++)
            {
                Assert.AreEqual(originalCache.bones[i].name, loadedCache.bones[i].name);
                Assert.AreEqual(originalCache.bones[i].parentIndex, loadedCache.bones[i].parentIndex);
            }

            Debug.Log("[Phase2Test] ボーン階層のデシリアライズ成功");
        });

        [UnityTest]
        public IEnumerator ボーン階層_ファイルに保存できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var boneCache = BoneHierarchyCacheSerializer.ExtractFromAvatar(avatar);
            var json = BoneHierarchyCacheSerializer.SerializeToJson(boneCache);

            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var coreDir = Path.Combine(cacheDir, "core");
            Directory.CreateDirectory(coreDir);

            var bonesPath = Path.Combine(coreDir, "bones.json");

            // Act
            File.WriteAllText(bonesPath, json);

            // Assert
            AssertFileExists(bonesPath, "bones.json");

            Debug.Log($"[Phase2Test] ボーン階層を保存: {bonesPath}");
        });

        #endregion

        #region Humanoid Extraction Tests

        [UnityTest]
        public IEnumerator Humanoid_HumanoidCacheSerializerで抽出できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var animator = avatar.GetComponent<Animator>();
            Assert.IsNotNull(animator, "VRMにAnimatorが存在すべき");
            Assert.IsNotNull(animator.avatar, "AnimatorにAvatarが存在すべき");

            // Act - 実際のHumanoidCacheSerializer.ExtractFromAnimatorを呼び出す
            var humanoidCache = HumanoidCacheSerializer.ExtractFromAnimator(animator);

            // Assert
            Assert.IsNotNull(humanoidCache);
            Assert.IsNotNull(humanoidCache.mappings);
            Assert.IsTrue(humanoidCache.mappings.Length > 0, "Humanoidマッピングが存在すべき");

            Debug.Log($"[Phase2Test] Humanoidマッピング数: {humanoidCache.mappings.Length}");
        });

        [UnityTest]
        public IEnumerator Humanoid_必須ボーンが含まれること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var animator = avatar.GetComponent<Animator>();

            // Act
            var humanoidCache = HumanoidCacheSerializer.ExtractFromAnimator(animator);

            // Assert - 必須ボーンを確認
            bool hasHips = false;
            bool hasHead = false;
            bool hasSpine = false;

            foreach (var mapping in humanoidCache.mappings)
            {
                if (mapping.humanBoneName == "Hips") hasHips = true;
                if (mapping.humanBoneName == "Head") hasHead = true;
                if (mapping.humanBoneName == "Spine") hasSpine = true;
            }

            Assert.IsTrue(hasHips, "Hipsボーンが存在すべき");
            Assert.IsTrue(hasHead, "Headボーンが存在すべき");
            Assert.IsTrue(hasSpine, "Spineボーンが存在すべき");

            Debug.Log("[Phase2Test] 必須Humanoidボーン確認成功");
        });

        #endregion

        #region Humanoid Serialization Tests

        [UnityTest]
        public IEnumerator Humanoid_JSONにシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var animator = avatar.GetComponent<Animator>();
            var humanoidCache = HumanoidCacheSerializer.ExtractFromAnimator(animator);

            // Act - 実際のHumanoidCacheSerializer.SerializeToJsonを呼び出す
            var json = HumanoidCacheSerializer.SerializeToJson(humanoidCache);

            // Assert
            Assert.IsNotNull(json);
            Assert.IsNotEmpty(json);
            Assert.IsTrue(json.Contains("mappings"));
            Assert.IsTrue(json.Contains("humanBoneName"));

            Debug.Log($"[Phase2Test] Humanoidシリアライズ成功");
        });

        [UnityTest]
        public IEnumerator Humanoid_JSONからデシリアライズできること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var animator = avatar.GetComponent<Animator>();
            var originalCache = HumanoidCacheSerializer.ExtractFromAnimator(animator);
            var json = HumanoidCacheSerializer.SerializeToJson(originalCache);

            // Act - 実際のHumanoidCacheSerializer.DeserializeFromJsonを呼び出す
            var loadedCache = HumanoidCacheSerializer.DeserializeFromJson(json);

            // Assert
            Assert.IsNotNull(loadedCache);
            Assert.AreEqual(originalCache.version, loadedCache.version);
            Assert.AreEqual(originalCache.mappings.Length, loadedCache.mappings.Length);

            Debug.Log("[Phase2Test] Humanoidデシリアライズ成功");
        });

        [UnityTest]
        public IEnumerator Humanoid_ファイルに保存できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var animator = avatar.GetComponent<Animator>();
            var humanoidCache = HumanoidCacheSerializer.ExtractFromAnimator(animator);
            var json = HumanoidCacheSerializer.SerializeToJson(humanoidCache);

            var hash = AvatarCacheManager.CalculateFileHash(TestVrmPath);
            var cacheDir = GetCacheDirectoryPath(hash);
            var coreDir = Path.Combine(cacheDir, "core");
            Directory.CreateDirectory(coreDir);

            var humanoidPath = Path.Combine(coreDir, "humanoid.json");

            // Act
            File.WriteAllText(humanoidPath, json);

            // Assert
            AssertFileExists(humanoidPath, "humanoid.json");

            Debug.Log($"[Phase2Test] Humanoidキャッシュ保存: {humanoidPath}");
        });

        #endregion

        #region Bone Reconstruction Tests

        [UnityTest]
        public IEnumerator ボーン再構築_BoneHierarchyCacheSerializerで再構築できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var boneCache = BoneHierarchyCacheSerializer.ExtractFromAvatar(avatar);
            var json = BoneHierarchyCacheSerializer.SerializeToJson(boneCache);
            var loadedCache = BoneHierarchyCacheSerializer.DeserializeFromJson(json);

            // Act - 実際のBoneHierarchyCacheSerializer.Reconstructを呼び出す
            var reconstructed = BoneHierarchyCacheSerializer.Reconstruct(loadedCache);

            // Assert
            Assert.IsNotNull(reconstructed);

            // ボーン数の確認
            var reconstructedBones = reconstructed.GetComponentsInChildren<Transform>();
            Assert.AreEqual(boneCache.bones.Length, reconstructedBones.Length - 1, "再構築されたボーン数が一致すべき（ルート除く）");

            Debug.Log($"[Phase2Test] 再構築したボーン数: {reconstructedBones.Length}");

            // クリーンアップ
            Object.Destroy(reconstructed);
        });

        #endregion

        #region Avatar Creation Tests

        [UnityTest]
        public IEnumerator Avatar作成_HumanoidCacheSerializerでAvatarを作成できること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            var avatar = await LoadVrmAsync();
            var animator = avatar.GetComponent<Animator>();
            var humanoidCache = HumanoidCacheSerializer.ExtractFromAnimator(animator);
            var boneCache = BoneHierarchyCacheSerializer.ExtractFromAvatar(avatar);

            // ボーン階層を再構築
            var reconstructedRoot = BoneHierarchyCacheSerializer.Reconstruct(boneCache);

            // Act - 実際のHumanoidCacheSerializer.CreateAvatarを呼び出す
            var createdAvatar = HumanoidCacheSerializer.CreateAvatar(humanoidCache, reconstructedRoot);

            // Assert
            Assert.IsNotNull(createdAvatar);
            Assert.IsTrue(createdAvatar.isHuman, "作成されたAvatarはHumanoidであるべき");

            Debug.Log($"[Phase2Test] Avatar作成成功: {createdAvatar.name}");

            // クリーンアップ
            Object.Destroy(reconstructedRoot);
        });

        #endregion
    }
}
