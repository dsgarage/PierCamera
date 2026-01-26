using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AICam.AvatarCache;
using AICam.AvatarCache.Serializers;
using AICam.Tests.PlayMode.AvatarCache;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRM;
using UniGLTF;

namespace AICam.Tests.PlayMode.Integration
{
    /// <summary>
    /// Phase 10: キャッシュ整合性テスト
    ///
    /// VRMをロード → キャッシュ作成 → キャッシュからロード → オリジナルと比較
    /// スキニング（bindposes/ボーン）の整合性を重点的に検証する
    /// </summary>
    [TestFixture]
    public class Phase10_CacheIntegrityTests
    {
        // Eku VRMの保存先パス
        private static readonly string EkuVrmPath =
            "/Users/daisuketsukada/Library/Mobile Documents/com~apple~CloudDocs/Eku_VRM_v1_0_0 3.vrm";

        private string _testCacheDir;
        private AvatarCacheManager _cacheManager;
        private RuntimeGltfInstance _loadedInstance;
        private GameObject _originalAvatar;
        private GameObject _cachedAvatar;

        [SetUp]
        public void SetUp()
        {
            _testCacheDir = Path.Combine(Application.temporaryCachePath, "CacheIntegrityTest");
            if (Directory.Exists(_testCacheDir))
                Directory.Delete(_testCacheDir, true);
            Directory.CreateDirectory(_testCacheDir);

            _cacheManager = new AvatarCacheManager(_testCacheDir);
        }

        [TearDown]
        public void TearDown()
        {
            if (_loadedInstance != null)
            {
                _loadedInstance.Dispose();
                _loadedInstance = null;
            }
            if (_originalAvatar != null)
            {
                UnityEngine.Object.Destroy(_originalAvatar);
                _originalAvatar = null;
            }
            if (_cachedAvatar != null)
            {
                UnityEngine.Object.Destroy(_cachedAvatar);
                _cachedAvatar = null;
            }
            if (Directory.Exists(_testCacheDir))
            {
                try { Directory.Delete(_testCacheDir, true); }
                catch (Exception e) { Debug.LogWarning($"Cleanup failed: {e.Message}"); }
            }
        }

        /// <summary>
        /// VRMをロードするヘルパー
        /// </summary>
        private async UniTask<GameObject> LoadVrmFromPathAsync(string vrmPath)
        {
            if (!File.Exists(vrmPath))
                throw new FileNotFoundException($"VRM file not found: {vrmPath}");

            var bytes = await File.ReadAllBytesAsync(vrmPath);
            _loadedInstance = await VrmUtility.LoadBytesAsync(
                path: Path.GetFileName(vrmPath),
                bytes: bytes,
                awaitCaller: new RuntimeOnlyAwaitCaller()
            );
            _loadedInstance.EnableUpdateWhenOffscreen();
            _loadedInstance.ShowMeshes();

            return _loadedInstance.Root;
        }

        // ====================================================================
        // メインテスト: キャッシュ→ロード→比較
        // ====================================================================

        [UnityTest]
        public IEnumerator キャッシュからロードしたアバターのボーン階層がオリジナルと一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange: VRMをロード
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);
            Assert.IsNotNull(_originalAvatar, "VRMロードに失敗");

            // Act: キャッシュ作成 → キャッシュからロード
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);
            Assert.IsNotNull(_cachedAvatar, "キャッシュからのロードに失敗");

            // Assert: ボーン階層を比較
            var originalTransforms = _originalAvatar.GetComponentsInChildren<Transform>();
            var cachedTransforms = _cachedAvatar.GetComponentsInChildren<Transform>();

            Assert.AreEqual(originalTransforms.Length, cachedTransforms.Length,
                $"ボーン数が不一致: original={originalTransforms.Length}, cached={cachedTransforms.Length}");

            int nameMatchCount = 0;
            for (int i = 0; i < originalTransforms.Length; i++)
            {
                Assert.AreEqual(originalTransforms[i].name, cachedTransforms[i].name,
                    $"ボーン名が不一致 [{i}]: original='{originalTransforms[i].name}', cached='{cachedTransforms[i].name}'");
                nameMatchCount++;
            }
            Debug.Log($"[CacheIntegrity] ボーン階層: {nameMatchCount}/{originalTransforms.Length} 名前一致");
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたアバターのボーンTransformがオリジナルと一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            // Assert: 各ボーンのlocalTransformを比較
            // 注意: VRM Spring Bone（物理ボーン）はランタイムで回転が変化するため、
            // スケルトンボーンと物理ボーンを分けて検証する
            var originalTransforms = _originalAvatar.GetComponentsInChildren<Transform>();
            var cachedTransforms = _cachedAvatar.GetComponentsInChildren<Transform>();

            Assert.AreEqual(originalTransforms.Length, cachedTransforms.Length, "ボーン数が不一致");

            const float posTolerance = 0.001f;
            const float rotTolerance = 0.1f; // degrees
            const float scaleTolerance = 0.001f;

            int skelPosErrors = 0, skelRotErrors = 0;
            int physPosErrors = 0, physRotErrors = 0;
            int skelBoneCount = 0, physBoneCount = 0;
            float maxSkelRotError = 0;
            string worstSkelRotBone = "";

            for (int i = 0; i < originalTransforms.Length; i++)
            {
                var origT = originalTransforms[i];
                var cacheT = cachedTransforms[i];
                bool isPhysicsBone = IsPhysicsBone(origT.name);

                if (isPhysicsBone) physBoneCount++;
                else skelBoneCount++;

                // localPosition
                float posDist = Vector3.Distance(origT.localPosition, cacheT.localPosition);
                if (posDist > posTolerance)
                {
                    if (isPhysicsBone) physPosErrors++;
                    else skelPosErrors++;
                }

                // localRotation
                float rotAngle = Quaternion.Angle(origT.localRotation, cacheT.localRotation);
                if (rotAngle > rotTolerance)
                {
                    if (isPhysicsBone)
                    {
                        physRotErrors++;
                    }
                    else
                    {
                        skelRotErrors++;
                        if (rotAngle > maxSkelRotError) { maxSkelRotError = rotAngle; worstSkelRotBone = origT.name; }
                        Debug.LogWarning($"[CacheIntegrity] SKELETON rotation mismatch '{origT.name}': " +
                            $"orig={origT.localRotation.eulerAngles}, cached={cacheT.localRotation.eulerAngles}, diff={rotAngle:F4}deg");
                    }
                }

                // localScale
                float scaleDist = Vector3.Distance(origT.localScale, cacheT.localScale);
                Assert.Less(scaleDist, scaleTolerance,
                    $"Scale mismatch '{origT.name}': orig={origT.localScale}, cached={cacheT.localScale}");
            }

            Debug.Log($"[CacheIntegrity] スケルトンボーン: posErrors={skelPosErrors}, rotErrors={skelRotErrors} / {skelBoneCount}");
            Debug.Log($"[CacheIntegrity] 物理ボーン(参考): posErrors={physPosErrors}, rotErrors={physRotErrors} / {physBoneCount}");
            if (skelRotErrors > 0) Debug.Log($"  Worst skeleton rotation error: {worstSkelRotBone} ({maxSkelRotError:F4}deg)");

            // スケルトンボーンは厳密に一致すること
            Assert.AreEqual(0, skelPosErrors, $"Skeleton position mismatches: {skelPosErrors} (worst: {worstSkelRotBone})");
            Assert.AreEqual(0, skelRotErrors, $"Skeleton rotation mismatches: {skelRotErrors} (worst: {worstSkelRotBone} {maxSkelRotError:F4}deg)");

            // 物理ボーンは参考情報として記録（Spring Boneランタイムの影響で差が出るのは正常）
            if (physRotErrors > 0)
            {
                Debug.Log($"[CacheIntegrity] 物理ボーンの回転差異 {physRotErrors}件はSpring Boneの影響（正常）");
            }
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたSMRのボーン数がbindposesと一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            // Assert: 各SMRのbones数とbindposes数が一致すること
            var originalSmrs = _originalAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var cachedSmrs = _cachedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            Assert.AreEqual(originalSmrs.Length, cachedSmrs.Length,
                $"SMR数が不一致: original={originalSmrs.Length}, cached={cachedSmrs.Length}");

            for (int s = 0; s < cachedSmrs.Length; s++)
            {
                var smr = cachedSmrs[s];
                var mesh = smr.sharedMesh;
                Assert.IsNotNull(mesh, $"SMR[{s}] '{smr.name}' のメッシュがnull");

                int bindposeCount = mesh.bindposes?.Length ?? 0;
                int boneCount = smr.bones?.Length ?? 0;
                int nullBones = 0;
                if (smr.bones != null)
                    foreach (var b in smr.bones) if (b == null) nullBones++;

                Debug.Log($"[CacheIntegrity] SMR '{smr.name}': bindposes={bindposeCount}, bones={boneCount}, nullBones={nullBones}");

                Assert.AreEqual(bindposeCount, boneCount,
                    $"SMR '{smr.name}': bindposes({bindposeCount}) != bones({boneCount})");
                Assert.AreEqual(0, nullBones,
                    $"SMR '{smr.name}': {nullBones} null bones detected");
            }

            // オリジナルとも比較
            for (int s = 0; s < originalSmrs.Length; s++)
            {
                var origSmr = originalSmrs[s];
                var cachedSmr = cachedSmrs[s];

                int origBoneCount = origSmr.bones?.Length ?? 0;
                int cachedBoneCount = cachedSmr.bones?.Length ?? 0;
                Assert.AreEqual(origBoneCount, cachedBoneCount,
                    $"SMR '{origSmr.name}': original bones({origBoneCount}) != cached bones({cachedBoneCount})");
            }
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたメッシュのbindposesがオリジナルと一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            // Assert: bindposesの値を1対1で比較
            var originalSmrs = _originalAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var cachedSmrs = _cachedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            Assert.AreEqual(originalSmrs.Length, cachedSmrs.Length, "SMR数が不一致");

            for (int s = 0; s < originalSmrs.Length; s++)
            {
                var origMesh = originalSmrs[s].sharedMesh;
                var cachedMesh = cachedSmrs[s].sharedMesh;
                if (origMesh == null || cachedMesh == null) continue;

                var origBindposes = origMesh.bindposes;
                var cachedBindposes = cachedMesh.bindposes;

                Assert.AreEqual(origBindposes.Length, cachedBindposes.Length,
                    $"Mesh '{origMesh.name}': bindpose count mismatch");

                int mismatchCount = 0;
                for (int i = 0; i < origBindposes.Length; i++)
                {
                    if (!MatricesApproxEqual(origBindposes[i], cachedBindposes[i], 0.0001f))
                    {
                        mismatchCount++;
                        if (mismatchCount <= 3)
                        {
                            Debug.LogWarning($"[CacheIntegrity] Bindpose mismatch [{s}][{i}] in '{origMesh.name}'");
                        }
                    }
                }

                Assert.AreEqual(0, mismatchCount,
                    $"Mesh '{origMesh.name}': {mismatchCount}/{origBindposes.Length} bindposes differ");
                Debug.Log($"[CacheIntegrity] Mesh '{origMesh.name}': {origBindposes.Length} bindposes all match");
            }
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたアバターのbindposeとbone世界座標が一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            // Assert: bindpose.inverse と bone.localToWorldMatrix が一致（スキニングの核心テスト）
            // 注意: Spring Bone（物理ボーン）はランタイムで移動するため、
            // スケルトンボーンと物理ボーンを分けて検証する
            Debug.Log("=== Original Avatar bindpose-bone consistency ===");
            var origResult = CheckBindposeBoneConsistency(_originalAvatar);
            Debug.Log($"[CacheIntegrity] Original: {origResult.skelMatch}/{origResult.skelTotal} skeleton OK, " +
                $"{origResult.physMismatch}/{origResult.physTotal} physics mismatches (expected)");

            Debug.Log("=== Cached Avatar bindpose-bone consistency ===");
            var cachedResult = CheckBindposeBoneConsistency(_cachedAvatar);
            Debug.Log($"[CacheIntegrity] Cached: {cachedResult.skelMatch}/{cachedResult.skelTotal} skeleton OK, " +
                $"{cachedResult.physMismatch}/{cachedResult.physTotal} physics mismatches (expected)");

            // スケルトンボーンのbindpose整合性: キャッシュがオリジナルより悪化していないこと
            Assert.LessOrEqual(cachedResult.skelMismatch, origResult.skelMismatch,
                $"Cached avatar skeleton has MORE bindpose-bone mismatches than original! " +
                $"(original: {origResult.skelMismatch}, cached: {cachedResult.skelMismatch})");

            // スケルトンボーンのmismatch rate が低いこと（10%以下）
            if (cachedResult.skelTotal > 0)
            {
                float skelMismatchRate = (float)cachedResult.skelMismatch / cachedResult.skelTotal;
                Debug.Log($"[CacheIntegrity] Cached skeleton mismatch rate: {skelMismatchRate:P1}");
                Assert.Less(skelMismatchRate, 0.1f,
                    $"Skeleton bindpose-bone mismatch rate too high: {skelMismatchRate:P1} " +
                    $"({cachedResult.skelMismatch}/{cachedResult.skelTotal})");
            }

            // 物理ボーンのmismatchは参考記録のみ（Spring Boneの影響で差が出るのは正常）
            Debug.Log($"[CacheIntegrity] 物理ボーン bindpose mismatch: original={origResult.physMismatch}, cached={cachedResult.physMismatch} (参考)");
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたメッシュの頂点数がオリジナルと一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            // Assert
            var originalSmrs = _originalAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var cachedSmrs = _cachedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            Assert.AreEqual(originalSmrs.Length, cachedSmrs.Length, "SMR数が不一致");

            for (int s = 0; s < originalSmrs.Length; s++)
            {
                var origMesh = originalSmrs[s].sharedMesh;
                var cachedMesh = cachedSmrs[s].sharedMesh;
                if (origMesh == null) continue;
                Assert.IsNotNull(cachedMesh, $"Cached mesh is null for '{originalSmrs[s].name}'");

                Assert.AreEqual(origMesh.vertexCount, cachedMesh.vertexCount,
                    $"Mesh '{origMesh.name}': vertex count mismatch (orig={origMesh.vertexCount}, cached={cachedMesh.vertexCount})");

                Assert.AreEqual(origMesh.subMeshCount, cachedMesh.subMeshCount,
                    $"Mesh '{origMesh.name}': subMesh count mismatch");

                Assert.AreEqual(origMesh.blendShapeCount, cachedMesh.blendShapeCount,
                    $"Mesh '{origMesh.name}': blendShape count mismatch (orig={origMesh.blendShapeCount}, cached={cachedMesh.blendShapeCount})");

                Debug.Log($"[CacheIntegrity] Mesh '{origMesh.name}': vertices={origMesh.vertexCount}, " +
                    $"subMeshes={origMesh.subMeshCount}, blendShapes={origMesh.blendShapeCount} - all match");
            }
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたSMRのボーン名がオリジナルと同じ順序であること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            // Assert: SMRのbonesの名前と順序がオリジナルと一致
            var originalSmrs = _originalAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var cachedSmrs = _cachedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            for (int s = 0; s < Mathf.Min(originalSmrs.Length, cachedSmrs.Length); s++)
            {
                var origBones = originalSmrs[s].bones;
                var cachedBones = cachedSmrs[s].bones;
                if (origBones == null || cachedBones == null) continue;

                Assert.AreEqual(origBones.Length, cachedBones.Length,
                    $"SMR[{s}] '{originalSmrs[s].name}': bone array length mismatch");

                int orderMismatch = 0;
                for (int i = 0; i < origBones.Length; i++)
                {
                    if (origBones[i] == null || cachedBones[i] == null) continue;
                    if (origBones[i].name != cachedBones[i].name)
                    {
                        orderMismatch++;
                        if (orderMismatch <= 5)
                        {
                            Debug.LogWarning($"[CacheIntegrity] Bone order mismatch SMR[{s}][{i}]: " +
                                $"orig='{origBones[i].name}', cached='{cachedBones[i].name}'");
                        }
                    }
                }

                Assert.AreEqual(0, orderMismatch,
                    $"SMR '{originalSmrs[s].name}': {orderMismatch} bone order mismatches (must match bindpose order)");
                Debug.Log($"[CacheIntegrity] SMR '{originalSmrs[s].name}': {origBones.Length} bones in correct order");
            }
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたメッシュのボーンウェイトがオリジナルと一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            // Assert: 各メッシュのBoneWeightを比較
            var originalSmrs = _originalAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var cachedSmrs = _cachedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            Assert.AreEqual(originalSmrs.Length, cachedSmrs.Length, "SMR数が不一致");

            for (int s = 0; s < originalSmrs.Length; s++)
            {
                var origMesh = originalSmrs[s].sharedMesh;
                var cachedMesh = cachedSmrs[s].sharedMesh;
                if (origMesh == null || cachedMesh == null) continue;

                var origWeights = origMesh.boneWeights;
                var cachedWeights = cachedMesh.boneWeights;

                Assert.AreEqual(origWeights.Length, cachedWeights.Length,
                    $"Mesh '{origMesh.name}': boneWeight count mismatch (orig={origWeights.Length}, cached={cachedWeights.Length})");

                int weightValueMismatch = 0;
                int indexMismatch = 0;
                int badSumCount = 0;
                const float weightTolerance = 0.0001f;
                const float sumTolerance = 0.01f;

                for (int i = 0; i < origWeights.Length; i++)
                {
                    var orig = origWeights[i];
                    var cached = cachedWeights[i];

                    // ウェイト値の比較
                    if (Mathf.Abs(orig.weight0 - cached.weight0) > weightTolerance ||
                        Mathf.Abs(orig.weight1 - cached.weight1) > weightTolerance ||
                        Mathf.Abs(orig.weight2 - cached.weight2) > weightTolerance ||
                        Mathf.Abs(orig.weight3 - cached.weight3) > weightTolerance)
                    {
                        weightValueMismatch++;
                        if (weightValueMismatch <= 3)
                        {
                            Debug.LogWarning($"[CacheIntegrity] Weight mismatch '{origMesh.name}' vertex[{i}]: " +
                                $"orig=({orig.weight0:F4},{orig.weight1:F4},{orig.weight2:F4},{orig.weight3:F4}), " +
                                $"cached=({cached.weight0:F4},{cached.weight1:F4},{cached.weight2:F4},{cached.weight3:F4})");
                        }
                    }

                    // ボーンインデックスの比較
                    if (orig.boneIndex0 != cached.boneIndex0 ||
                        orig.boneIndex1 != cached.boneIndex1 ||
                        orig.boneIndex2 != cached.boneIndex2 ||
                        orig.boneIndex3 != cached.boneIndex3)
                    {
                        indexMismatch++;
                        if (indexMismatch <= 3)
                        {
                            Debug.LogWarning($"[CacheIntegrity] BoneIndex mismatch '{origMesh.name}' vertex[{i}]: " +
                                $"orig=({orig.boneIndex0},{orig.boneIndex1},{orig.boneIndex2},{orig.boneIndex3}), " +
                                $"cached=({cached.boneIndex0},{cached.boneIndex1},{cached.boneIndex2},{cached.boneIndex3})");
                        }
                    }

                    // キャッシュ側のウェイト合計が1.0になること
                    float cachedSum = cached.weight0 + cached.weight1 + cached.weight2 + cached.weight3;
                    if (Mathf.Abs(cachedSum - 1.0f) > sumTolerance)
                    {
                        badSumCount++;
                        if (badSumCount <= 3)
                        {
                            Debug.LogWarning($"[CacheIntegrity] Weight sum != 1.0: '{origMesh.name}' vertex[{i}] " +
                                $"sum={cachedSum:F6} ({cached.weight0:F4}+{cached.weight1:F4}+{cached.weight2:F4}+{cached.weight3:F4})");
                        }
                    }
                }

                Debug.Log($"[CacheIntegrity] Mesh '{origMesh.name}' BoneWeight: {origWeights.Length} weights, " +
                    $"valueMismatch={weightValueMismatch}, indexMismatch={indexMismatch}, badSum={badSumCount}");

                Assert.AreEqual(0, weightValueMismatch,
                    $"Mesh '{origMesh.name}': {weightValueMismatch}/{origWeights.Length} weight value mismatches");
                Assert.AreEqual(0, indexMismatch,
                    $"Mesh '{origMesh.name}': {indexMismatch}/{origWeights.Length} bone index mismatches");
                Assert.AreEqual(0, badSumCount,
                    $"Mesh '{origMesh.name}': {badSumCount}/{cachedWeights.Length} vertices with weight sum != 1.0");
            }
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたアバターのHumanoidボーンワールド座標がオリジナルと一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            // 1フレーム待ってAnimatorにボーン位置を更新させる
            await UniTask.Yield();
            await UniTask.Yield();

            // Assert: Animator経由でHumanoidボーンのワールド座標を比較
            var origAnimator = _originalAvatar.GetComponent<Animator>();
            var cachedAnimator = _cachedAvatar.GetComponent<Animator>();

            Assert.IsNotNull(origAnimator, "Original avatar has no Animator");
            Assert.IsNotNull(cachedAnimator, "Cached avatar has no Animator");
            Assert.IsNotNull(origAnimator.avatar, "Original Animator has no Avatar");
            Assert.IsNotNull(cachedAnimator.avatar, "Cached Animator has no Avatar");
            Assert.IsTrue(origAnimator.avatar.isHuman, "Original Avatar is not humanoid");
            Assert.IsTrue(cachedAnimator.avatar.isHuman, "Cached Avatar is not humanoid");

            // 肩・腕・肘・手を重点的に検証
            var criticalBones = new[]
            {
                HumanBodyBones.Hips,
                HumanBodyBones.Spine,
                HumanBodyBones.Chest,
                HumanBodyBones.UpperChest,
                HumanBodyBones.Neck,
                HumanBodyBones.Head,
                HumanBodyBones.LeftShoulder,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                HumanBodyBones.RightShoulder,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg,
                HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot,
                HumanBodyBones.RightUpperLeg,
                HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot,
            };

            const float posTolerance = 0.01f; // 1cm
            const float rotTolerance = 1.0f;  // 1 degree
            int posMismatches = 0;
            int rotMismatches = 0;

            foreach (var bone in criticalBones)
            {
                var origBone = origAnimator.GetBoneTransform(bone);
                var cachedBone = cachedAnimator.GetBoneTransform(bone);

                if (origBone == null && cachedBone == null) continue;

                Assert.IsNotNull(origBone, $"Original missing bone: {bone}");
                Assert.IsNotNull(cachedBone, $"Cached missing bone: {bone}");

                // ワールド位置の比較
                float posDist = Vector3.Distance(origBone.position, cachedBone.position);
                if (posDist > posTolerance)
                {
                    posMismatches++;
                    Debug.LogError($"[CacheIntegrity] HUMANOID WORLD POS MISMATCH '{bone}': " +
                        $"orig={origBone.position}, cached={cachedBone.position}, diff={posDist:F4}m");
                }

                // ワールド回転の比較
                float rotAngle = Quaternion.Angle(origBone.rotation, cachedBone.rotation);
                if (rotAngle > rotTolerance)
                {
                    rotMismatches++;
                    Debug.LogError($"[CacheIntegrity] HUMANOID WORLD ROT MISMATCH '{bone}': " +
                        $"orig={origBone.rotation.eulerAngles}, cached={cachedBone.rotation.eulerAngles}, diff={rotAngle:F2}deg");
                }
                else
                {
                    Debug.Log($"[CacheIntegrity] '{bone}': pos diff={posDist:F4}m, rot diff={rotAngle:F2}deg - OK");
                }
            }

            Debug.Log($"[CacheIntegrity] Humanoidボーン検証: posMismatches={posMismatches}, rotMismatches={rotMismatches} / {criticalBones.Length}");
            Assert.AreEqual(0, posMismatches, $"Humanoid world position mismatches: {posMismatches}");
            Assert.AreEqual(0, rotMismatches, $"Humanoid world rotation mismatches: {rotMismatches}");
        });

        [UnityTest]
        public IEnumerator キャッシュからロードしたアバターの肩周辺スキニング結果がオリジナルと一致すること() => UniTask.ToCoroutine(async () =>
        {
            // Arrange
            _originalAvatar = await LoadVrmFromPathAsync(EkuVrmPath);

            // Act
            await _cacheManager.CreateCacheAsync(EkuVrmPath, _originalAvatar);
            var cacheId = AvatarCacheManager.CalculateFileHash(EkuVrmPath);
            _cachedAvatar = await _cacheManager.LoadFromCacheAsync(cacheId);

            await UniTask.Yield();
            await UniTask.Yield();

            // Assert: 実際のスキニング計算結果を比較
            // skinned position = Σ(weight_i * bone_i.localToWorldMatrix * bindpose_i * vertex)
            var originalSmrs = _originalAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();
            var cachedSmrs = _cachedAvatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            Assert.AreEqual(originalSmrs.Length, cachedSmrs.Length, "SMR数が不一致");

            const float tolerance = 0.02f; // 2cm
            int totalTested = 0;
            int totalMismatches = 0;

            for (int s = 0; s < originalSmrs.Length; s++)
            {
                var origSmr = originalSmrs[s];
                var cachedSmr = cachedSmrs[s];
                var origMesh = origSmr.sharedMesh;
                var cachedMesh = cachedSmr.sharedMesh;
                if (origMesh == null || cachedMesh == null) continue;

                var origWeights = origMesh.boneWeights;
                var cachedWeights = cachedMesh.boneWeights;
                var origVerts = origMesh.vertices;
                var cachedVerts = cachedMesh.vertices;
                var origBindposes = origMesh.bindposes;
                var cachedBindposes = cachedMesh.bindposes;

                if (origWeights.Length == 0 || origBindposes.Length == 0) continue;

                // 均等間隔で最大100頂点をサンプリング
                int step = Mathf.Max(1, origVerts.Length / 100);
                int meshMismatches = 0;

                for (int v = 0; v < origVerts.Length; v += step)
                {
                    var origPos = ComputeSkinnedPosition(origVerts[v], origWeights[v], origBindposes, origSmr.bones);
                    var cachedPos = ComputeSkinnedPosition(cachedVerts[v], cachedWeights[v], cachedBindposes, cachedSmr.bones);

                    float dist = Vector3.Distance(origPos, cachedPos);
                    totalTested++;

                    if (dist > tolerance)
                    {
                        totalMismatches++;
                        meshMismatches++;
                        if (meshMismatches <= 5)
                        {
                            // どのボーンの影響が大きいか特定
                            var bw = cachedWeights[v];
                            string boneInfo = "";
                            if (bw.weight0 > 0.01f && bw.boneIndex0 < cachedSmr.bones.Length && cachedSmr.bones[bw.boneIndex0] != null)
                                boneInfo += $" [{bw.boneIndex0}]{cachedSmr.bones[bw.boneIndex0].name}({bw.weight0:F2})";
                            if (bw.weight1 > 0.01f && bw.boneIndex1 < cachedSmr.bones.Length && cachedSmr.bones[bw.boneIndex1] != null)
                                boneInfo += $" [{bw.boneIndex1}]{cachedSmr.bones[bw.boneIndex1].name}({bw.weight1:F2})";

                            Debug.LogError($"[CacheIntegrity] SKINNING MISMATCH '{origMesh.name}' v[{v}]: " +
                                $"dist={dist:F4}m, orig={origPos}, cached={cachedPos}, bones:{boneInfo}");
                        }
                    }
                }

                if (meshMismatches > 0)
                {
                    Debug.LogError($"[CacheIntegrity] Mesh '{origMesh.name}': {meshMismatches} skinning mismatches (sampled {origVerts.Length / step} vertices)");
                }
                else
                {
                    Debug.Log($"[CacheIntegrity] Mesh '{origMesh.name}': skinning OK (sampled {origVerts.Length / step} vertices)");
                }
            }

            Debug.Log($"[CacheIntegrity] スキニング検証: {totalMismatches}/{totalTested} mismatches");
            Assert.AreEqual(0, totalMismatches,
                $"Skinning result mismatches: {totalMismatches}/{totalTested} vertices differ by > {tolerance}m");
        });

        // ====================================================================
        // ヘルパーメソッド
        // ====================================================================

        private struct ConsistencyResult
        {
            public int skelTotal;
            public int skelMatch;
            public int skelMismatch;
            public int physTotal;
            public int physMatch;
            public int physMismatch;
        }

        /// <summary>
        /// 全SMRのbindposeとbone world transformの整合性をチェック
        /// スケルトンボーンと物理ボーン（Spring Bone）を分けてカウント
        /// </summary>
        private ConsistencyResult CheckBindposeBoneConsistency(GameObject avatar)
        {
            var result = new ConsistencyResult();
            var smrs = avatar.GetComponentsInChildren<SkinnedMeshRenderer>();

            foreach (var smr in smrs)
            {
                var mesh = smr.sharedMesh;
                if (mesh == null) continue;

                var bindposes = mesh.bindposes;
                var bones = smr.bones;
                if (bindposes == null || bones == null) continue;

                int count = Mathf.Min(bindposes.Length, bones.Length);
                for (int i = 0; i < count; i++)
                {
                    if (bones[i] == null) continue;
                    bool isPhysics = IsPhysicsBone(bones[i].name);

                    if (isPhysics) result.physTotal++;
                    else result.skelTotal++;

                    var bindposeInv = bindposes[i].inverse;
                    var bpPos = new Vector3(bindposeInv.m03, bindposeInv.m13, bindposeInv.m23);
                    var bonePos = bones[i].position;
                    float posError = Vector3.Distance(bpPos, bonePos);

                    var bpRot = bindposeInv.rotation;
                    var boneRot = bones[i].rotation;
                    float rotError = Quaternion.Angle(bpRot, boneRot);

                    if (posError > 0.01f || rotError > 1f)
                    {
                        if (isPhysics)
                        {
                            result.physMismatch++;
                        }
                        else
                        {
                            result.skelMismatch++;
                            Debug.LogWarning($"[Consistency] SKELETON MISMATCH '{smr.name}' bone '{bones[i].name}': " +
                                $"posErr={posError:F4}m, rotErr={rotError:F2}deg");
                        }
                    }
                    else
                    {
                        if (isPhysics) result.physMatch++;
                        else result.skelMatch++;
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// VRM Spring Bone（物理シミュレーション）の対象ボーンかどうかを判定
        /// Hair, Tail, Skirt などランタイムで回転が変化するボーン
        /// </summary>
        private static bool IsPhysicsBone(string boneName)
        {
            if (string.IsNullOrEmpty(boneName)) return false;
            var lower = boneName.ToLower();
            return lower.Contains("hair") || lower.Contains("tail") || lower.Contains("skirt") ||
                   lower.Contains("ribbon") || lower.Contains("cloth") || lower.Contains("chain") ||
                   lower.Contains("swing") || lower.Contains("dangle") || lower.Contains("accessory");
        }

        /// <summary>
        /// スキニング計算: 1頂点のワールド座標を算出
        /// worldPos = Σ(weight_i * bone_i.localToWorldMatrix * bindpose_i * vertex)
        /// </summary>
        private static Vector3 ComputeSkinnedPosition(Vector3 vertex, BoneWeight bw, Matrix4x4[] bindposes, Transform[] bones)
        {
            Vector3 result = Vector3.zero;

            if (bw.weight0 > 0 && bw.boneIndex0 < bones.Length && bones[bw.boneIndex0] != null)
            {
                var m = bones[bw.boneIndex0].localToWorldMatrix * bindposes[bw.boneIndex0];
                result += bw.weight0 * m.MultiplyPoint3x4(vertex);
            }
            if (bw.weight1 > 0 && bw.boneIndex1 < bones.Length && bones[bw.boneIndex1] != null)
            {
                var m = bones[bw.boneIndex1].localToWorldMatrix * bindposes[bw.boneIndex1];
                result += bw.weight1 * m.MultiplyPoint3x4(vertex);
            }
            if (bw.weight2 > 0 && bw.boneIndex2 < bones.Length && bones[bw.boneIndex2] != null)
            {
                var m = bones[bw.boneIndex2].localToWorldMatrix * bindposes[bw.boneIndex2];
                result += bw.weight2 * m.MultiplyPoint3x4(vertex);
            }
            if (bw.weight3 > 0 && bw.boneIndex3 < bones.Length && bones[bw.boneIndex3] != null)
            {
                var m = bones[bw.boneIndex3].localToWorldMatrix * bindposes[bw.boneIndex3];
                result += bw.weight3 * m.MultiplyPoint3x4(vertex);
            }

            return result;
        }

        /// <summary>
        /// 2つのMatrix4x4が近似的に等しいか判定
        /// </summary>
        private static bool MatricesApproxEqual(Matrix4x4 a, Matrix4x4 b, float tolerance)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int col = 0; col < 4; col++)
                {
                    if (Mathf.Abs(a[row, col] - b[row, col]) > tolerance)
                        return false;
                }
            }
            return true;
        }
    }
}
