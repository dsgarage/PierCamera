using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace AICam.UI.Tests
{
    /// <summary>
    /// Phase 00 で作成した asmdef アセンブリ構造の検証テスト。
    /// リファクタリング全 Phase を通じてアセンブリ構成が壊れないことを保証する。
    /// </summary>
    [TestFixture]
    public class AssemblyStructureTests
    {
        private Assembly[] _allAssemblies;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
        }

        private Assembly GetAssembly(string name)
        {
            return _allAssemblies.FirstOrDefault(a => a.GetName().Name == name);
        }

        #region アセンブリ存在テスト

        [TestCase("AICam.Core")]
        [TestCase("AICam.AR")]
        [TestCase("AICam.Expression")]
        [TestCase("AICam.FBXLoader")]
        [TestCase("AICam.AvatarBuilder")]
        [TestCase("AICam.UI")]
        [TestCase("AICam.BugReport")]
        public void Assembly_Exists(string assemblyName)
        {
            var assembly = GetAssembly(assemblyName);
            Assert.IsNotNull(assembly, $"{assemblyName} assembly should exist");
        }

        #endregion

        #region Core インターフェース存在テスト

        [Test]
        public void IUIBlockingProvider_ExistsInCore()
        {
            var assembly = GetAssembly("AICam.Core");
            var type = assembly.GetType("AICam.Core.IUIBlockingProvider");
            Assert.IsNotNull(type, "IUIBlockingProvider should exist in AICam.Core");
            Assert.IsTrue(type.IsInterface);
        }

        [Test]
        public void IUIBlockingProvider_HasIsPointOverUIPanel()
        {
            var assembly = GetAssembly("AICam.Core");
            var type = assembly.GetType("AICam.Core.IUIBlockingProvider");
            var method = type.GetMethod("IsPointOverUIPanel");
            Assert.IsNotNull(method, "IUIBlockingProvider should have IsPointOverUIPanel method");

            var parameters = method.GetParameters();
            Assert.AreEqual(1, parameters.Length);
            Assert.AreEqual(typeof(UnityEngine.Vector2), parameters[0].ParameterType);
            Assert.AreEqual(typeof(bool), method.ReturnType);
        }

        [Test]
        public void ILightingSettingsProvider_ExistsInCore()
        {
            var assembly = GetAssembly("AICam.Core");
            var type = assembly.GetType("AICam.Core.ILightingSettingsProvider");
            Assert.IsNotNull(type, "ILightingSettingsProvider should exist in AICam.Core");
            Assert.IsTrue(type.IsInterface);
        }

        [Test]
        public void ILightingSettingsProvider_HasReapplyLightingSettings()
        {
            var assembly = GetAssembly("AICam.Core");
            var type = assembly.GetType("AICam.Core.ILightingSettingsProvider");
            var method = type.GetMethod("ReapplyLightingSettings");
            Assert.IsNotNull(method, "ILightingSettingsProvider should have ReapplyLightingSettings method");
            Assert.AreEqual(0, method.GetParameters().Length);
        }

        [Test]
        public void IAvatarPlacer_ExistsInCore()
        {
            var assembly = GetAssembly("AICam.Core");
            var type = assembly.GetType("AICam.Core.IAvatarPlacer");
            Assert.IsNotNull(type, "IAvatarPlacer should exist in AICam.Core");
            Assert.IsTrue(type.IsInterface);
        }

        [Test]
        public void IAvatarPlacer_HasPlacedAvatarProperty()
        {
            var assembly = GetAssembly("AICam.Core");
            var type = assembly.GetType("AICam.Core.IAvatarPlacer");
            var prop = type.GetProperty("PlacedAvatar");
            Assert.IsNotNull(prop, "IAvatarPlacer should have PlacedAvatar property");
            Assert.AreEqual(typeof(UnityEngine.GameObject), prop.PropertyType);
            Assert.IsTrue(prop.CanRead);
            Assert.IsTrue(prop.CanWrite);
        }

        [Test]
        public void IAvatarPlacer_HasPlaceAvatarAhead()
        {
            var assembly = GetAssembly("AICam.Core");
            var type = assembly.GetType("AICam.Core.IAvatarPlacer");
            var method = type.GetMethod("PlaceAvatarAhead");
            Assert.IsNotNull(method, "IAvatarPlacer should have PlaceAvatarAhead method");
            Assert.AreEqual(typeof(bool), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.AreEqual(2, parameters.Length);
            Assert.AreEqual(typeof(UnityEngine.GameObject), parameters[0].ParameterType);
            Assert.AreEqual(typeof(float), parameters[1].ParameterType);
        }

        #endregion

        #region 循環依存禁止テスト

        [Test]
        public void AICamCore_DoesNotReferenceUI()
        {
            var assembly = GetAssembly("AICam.Core");
            var references = assembly.GetReferencedAssemblies();
            Assert.IsFalse(
                references.Any(r => r.Name == "AICam.UI"),
                "AICam.Core must not reference AICam.UI (circular dependency)");
        }

        [Test]
        public void AICamCore_DoesNotReferenceFBXLoader()
        {
            var assembly = GetAssembly("AICam.Core");
            var references = assembly.GetReferencedAssemblies();
            Assert.IsFalse(
                references.Any(r => r.Name == "AICam.FBXLoader"),
                "AICam.Core must not reference AICam.FBXLoader (circular dependency)");
        }

        [Test]
        public void AICamCore_DoesNotReferenceAR()
        {
            var assembly = GetAssembly("AICam.Core");
            var references = assembly.GetReferencedAssemblies();
            Assert.IsFalse(
                references.Any(r => r.Name == "AICam.AR"),
                "AICam.Core must not reference AICam.AR (circular dependency)");
        }

        [Test]
        public void AICamAR_DoesNotReferenceFBXLoader()
        {
            var assembly = GetAssembly("AICam.AR");
            var references = assembly.GetReferencedAssemblies();
            Assert.IsFalse(
                references.Any(r => r.Name == "AICam.FBXLoader"),
                "AICam.AR must not reference AICam.FBXLoader (circular dependency)");
        }

        [Test]
        public void AICamAR_DoesNotReferenceUI()
        {
            var assembly = GetAssembly("AICam.AR");
            var references = assembly.GetReferencedAssemblies();
            Assert.IsFalse(
                references.Any(r => r.Name == "AICam.UI"),
                "AICam.AR must not reference AICam.UI (circular dependency)");
        }

        #endregion
    }
}
