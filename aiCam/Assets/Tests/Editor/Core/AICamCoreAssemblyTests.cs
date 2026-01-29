using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace AICam.Core.Tests
{
    /// <summary>
    /// AICam.Core アセンブリ構造のテスト
    /// Phase 1: アセンブリ分割による依存関係最適化
    /// </summary>
    [TestFixture]
    public class AICamCoreAssemblyTests
    {
        private Assembly _coreAssembly;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            // AICam.Core アセンブリを取得
            _coreAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "AICam.Core");
        }

        #region アセンブリ存在テスト

        [Test]
        public void AICamCoreAssembly_Exists()
        {
            // Assert
            Assert.IsNotNull(_coreAssembly, "AICam.Core assembly should exist");
        }

        [Test]
        public void AICamCoreAssembly_HasCorrectName()
        {
            // Assert
            Assert.AreEqual("AICam.Core", _coreAssembly.GetName().Name);
        }

        #endregion

        #region 名前空間テスト

        [Test]
        public void ChunkedFileReader_ExistsInCorrectNamespace()
        {
            // Arrange
            var type = _coreAssembly.GetType("AICam.Core.IO.ChunkedFileReader");

            // Assert
            Assert.IsNotNull(type, "ChunkedFileReader should exist in AICam.Core.IO namespace");
            Assert.IsTrue(type.IsClass);
            Assert.IsTrue(type.IsAbstract && type.IsSealed, "ChunkedFileReader should be static");
        }

        [Test]
        public void CompressedTextureDeserializer_ExistsInCorrectNamespace()
        {
            // Arrange
            var type = _coreAssembly.GetType("AICam.Core.Texture.CompressedTextureDeserializer");

            // Assert
            Assert.IsNotNull(type, "CompressedTextureDeserializer should exist in AICam.Core.Texture namespace");
            Assert.IsTrue(type.IsClass);
            Assert.IsFalse(type.IsAbstract, "CompressedTextureDeserializer should not be abstract");
        }

        #endregion

        #region インターフェース実装テスト

        [Test]
        public void CompressedTextureDeserializer_ImplementsITextureDeserializer()
        {
            // Arrange
            var type = _coreAssembly.GetType("AICam.Core.Texture.CompressedTextureDeserializer");
            var interfaceType = type.GetInterface("ITextureDeserializer");

            // Assert
            Assert.IsNotNull(interfaceType, "CompressedTextureDeserializer should implement ITextureDeserializer");
        }

        #endregion

        #region メソッドシグネチャテスト

        [Test]
        public void ChunkedFileReader_HasReadAllBytesAsyncMethod()
        {
            // Arrange
            var type = _coreAssembly.GetType("AICam.Core.IO.ChunkedFileReader");
            // 複数のオーバーロードがある場合に備えてGetMethodsを使用
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "ReadAllBytesAsync")
                .ToArray();

            // Assert
            Assert.Greater(methods.Length, 0, "ChunkedFileReader should have ReadAllBytesAsync method");

            // 最初のオーバーロードを確認
            var method = methods[0];
            Assert.IsTrue(method.IsStatic, "ReadAllBytesAsync should be static");

            // パラメータ確認（最初のパラメータがfilePathであることを確認）
            var parameters = method.GetParameters();
            Assert.GreaterOrEqual(parameters.Length, 1, "ReadAllBytesAsync should have at least 1 parameter");
            Assert.AreEqual("filePath", parameters[0].Name, "First parameter should be filePath");
            Assert.AreEqual(typeof(string), parameters[0].ParameterType);
        }

        [Test]
        public void CompressedTextureDeserializer_HasLoadTextureAsyncMethod()
        {
            // Arrange
            var type = _coreAssembly.GetType("AICam.Core.Texture.CompressedTextureDeserializer");
            var method = type.GetMethod("LoadTextureAsync");

            // Assert
            Assert.IsNotNull(method, "CompressedTextureDeserializer should have LoadTextureAsync method");

            // パラメータ確認
            var parameters = method.GetParameters();
            Assert.AreEqual(2, parameters.Length, "LoadTextureAsync should have 2 parameters");
        }

        [Test]
        public void CompressedTextureDeserializer_HasIsCompressionAvailableProperty()
        {
            // Arrange
            var type = _coreAssembly.GetType("AICam.Core.Texture.CompressedTextureDeserializer");
            var property = type.GetProperty("IsCompressionAvailable");

            // Assert
            Assert.IsNotNull(property, "CompressedTextureDeserializer should have IsCompressionAvailable property");
            Assert.AreEqual(typeof(bool), property.PropertyType);
            Assert.IsTrue(property.CanRead, "IsCompressionAvailable should be readable");
        }

        #endregion

        #region 依存関係テスト

        [Test]
        public void AICamCoreAssembly_ReferencesUniTask()
        {
            // Arrange
            var references = _coreAssembly.GetReferencedAssemblies();

            // Assert
            var hasUniTask = references.Any(r => r.Name.Contains("UniTask"));
            Assert.IsTrue(hasUniTask, "AICam.Core should reference UniTask");
        }

        [Test]
        public void AICamCoreAssembly_ReferencesUniGLTF()
        {
            // Arrange
            var references = _coreAssembly.GetReferencedAssemblies();

            // Assert
            var hasUniGLTF = references.Any(r => r.Name.Contains("UniGLTF"));
            Assert.IsTrue(hasUniGLTF, "AICam.Core should reference UniGLTF");
        }

        [Test]
        public void AICamCoreAssembly_DoesNotReferenceVRMLoader()
        {
            // Arrange
            var references = _coreAssembly.GetReferencedAssemblies();

            // Assert - Core should not depend on VRMLoader (reverse dependency would be circular)
            var hasVRMLoader = references.Any(r => r.Name.Contains("VRMLoader"));
            Assert.IsFalse(hasVRMLoader, "AICam.Core should not reference AICam.VRMLoader (would be circular)");
        }

        #endregion

        #region エクスポートテスト

        [Test]
        public void AICamCoreAssembly_ExportsPublicTypes()
        {
            // Arrange
            var publicTypes = _coreAssembly.GetExportedTypes();

            // Assert
            Assert.Greater(publicTypes.Length, 0, "AICam.Core should export public types");

            // IOとTextureの名前空間にタイプがあることを確認
            var ioTypes = publicTypes.Where(t => t.Namespace == "AICam.Core.IO").ToList();
            var textureTypes = publicTypes.Where(t => t.Namespace == "AICam.Core.Texture").ToList();

            Assert.Greater(ioTypes.Count, 0, "Should have types in AICam.Core.IO namespace");
            Assert.Greater(textureTypes.Count, 0, "Should have types in AICam.Core.Texture namespace");
        }

        #endregion
    }
}
