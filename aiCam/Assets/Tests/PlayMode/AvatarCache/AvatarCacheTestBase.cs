using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VRM;
using UniGLTF;

namespace AICam.Tests.PlayMode.AvatarCache
{
    /// <summary>
    /// アバターキャッシュテストの基底クラス
    /// VRMロードとキャッシュ検証の共通処理を提供
    /// </summary>
    public abstract class AvatarCacheTestBase
    {
        // テスト用VRMファイルパス
        // 注: 実機テストではStreamingAssetsに配置する必要があります
        protected const string TestVrmPath = "/Users/daisuketsukada/Documents/dsgarageUnity/arCam/Eku_VRM_v1_0_0 3.vrm";

        // テスト用キャッシュディレクトリ
        protected string TestCacheDirectory => Path.Combine(Application.temporaryCachePath, "AvatarCacheTest");

        // ロードしたGameObject
        protected GameObject LoadedAvatar { get; private set; }

        // キャンセルトークン
        protected CancellationTokenSource Cts { get; private set; }

        [SetUp]
        public virtual void SetUp()
        {
            Cts = new CancellationTokenSource();

            // テスト用キャッシュディレクトリをクリーンアップ
            if (Directory.Exists(TestCacheDirectory))
            {
                Directory.Delete(TestCacheDirectory, true);
            }
            Directory.CreateDirectory(TestCacheDirectory);

            Debug.Log($"[AvatarCacheTest] SetUp - CacheDir: {TestCacheDirectory}");
        }

        [TearDown]
        public virtual void TearDown()
        {
            // ロードしたVRMインスタンスを破棄
            if (_loadedInstance != null)
            {
                _loadedInstance.Dispose();
                _loadedInstance = null;
            }

            // ロードしたアバターを破棄
            if (LoadedAvatar != null)
            {
                UnityEngine.Object.Destroy(LoadedAvatar);
                LoadedAvatar = null;
            }

            // キャンセルトークンを破棄
            Cts?.Cancel();
            Cts?.Dispose();
            Cts = null;

            // テスト用キャッシュディレクトリをクリーンアップ
            if (Directory.Exists(TestCacheDirectory))
            {
                try
                {
                    Directory.Delete(TestCacheDirectory, true);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AvatarCacheTest] Failed to cleanup test directory: {e.Message}");
                }
            }

            Debug.Log("[AvatarCacheTest] TearDown completed");
        }

        // ロードしたVRMインスタンス（Dispose用）
        private RuntimeGltfInstance _loadedInstance;

        /// <summary>
        /// VRMファイルをロード
        /// </summary>
        protected async UniTask<GameObject> LoadVrmAsync()
        {
            if (!File.Exists(TestVrmPath))
            {
                throw new FileNotFoundException($"Test VRM file not found: {TestVrmPath}");
            }

            Debug.Log($"[AvatarCacheTest] Loading VRM: {TestVrmPath}");

            // UniVRMでロード
            var bytes = await File.ReadAllBytesAsync(TestVrmPath, Cts.Token);

            // VrmUtility.LoadBytesAsync を使用
            _loadedInstance = await VrmUtility.LoadBytesAsync(
                path: Path.GetFileName(TestVrmPath),
                bytes: bytes,
                awaitCaller: new RuntimeOnlyAwaitCaller(),
                materialGeneratorCallback: null,
                metaCallback: null,
                textureDeserializer: null,
                loadAnimation: false,
                springboneRuntime: null
            );

            _loadedInstance.EnableUpdateWhenOffscreen();
            _loadedInstance.ShowMeshes();

            LoadedAvatar = _loadedInstance.Root;
            LoadedAvatar.name = "TestAvatar";

            Debug.Log($"[AvatarCacheTest] VRM loaded: {LoadedAvatar.name}");

            return LoadedAvatar;
        }

        /// <summary>
        /// ファイルのSHA256ハッシュを計算
        /// </summary>
        protected string CalculateFileHash(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// キャッシュディレクトリのパスを取得
        /// </summary>
        protected string GetCacheDirectoryPath(string hash)
        {
            return Path.Combine(TestCacheDirectory, "AvatarCache", hash);
        }

        /// <summary>
        /// ファイルが存在するか確認
        /// </summary>
        protected void AssertFileExists(string path, string description = null)
        {
            Assert.IsTrue(File.Exists(path), $"File should exist: {description ?? path}");
        }

        /// <summary>
        /// ディレクトリが存在するか確認
        /// </summary>
        protected void AssertDirectoryExists(string path, string description = null)
        {
            Assert.IsTrue(Directory.Exists(path), $"Directory should exist: {description ?? path}");
        }

        /// <summary>
        /// JSONファイルの内容を検証
        /// </summary>
        protected T LoadAndValidateJson<T>(string path, string description = null) where T : class
        {
            AssertFileExists(path, description);
            var json = File.ReadAllText(path);
            Assert.IsNotEmpty(json, $"JSON file should not be empty: {description ?? path}");

            var obj = JsonUtility.FromJson<T>(json);
            Assert.IsNotNull(obj, $"JSON should deserialize to {typeof(T).Name}: {description ?? path}");

            return obj;
        }

        /// <summary>
        /// バイナリファイルのマジックナンバーを検証
        /// </summary>
        protected void AssertBinaryMagic(string path, string expectedMagic)
        {
            AssertFileExists(path);
            using var stream = File.OpenRead(path);
            var buffer = new byte[4];
            stream.Read(buffer, 0, 4);
            var magic = System.Text.Encoding.ASCII.GetString(buffer);
            Assert.AreEqual(expectedMagic, magic, $"Binary magic should be '{expectedMagic}' but was '{magic}'");
        }
    }
}
