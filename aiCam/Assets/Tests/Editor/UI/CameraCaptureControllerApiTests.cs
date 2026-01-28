using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace AICam.UI.Tests
{
    /// <summary>
    /// CameraCaptureController の公開 API 基準テスト。
    /// リファクタリング全 Phase を通じて外部から呼ばれる API の署名が維持されることを保証する。
    /// </summary>
    [TestFixture]
    public class CameraCaptureControllerApiTests
    {
        private Type _cccType;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "AICam.UI");
            Assert.IsNotNull(assembly, "AICam.UI assembly should exist");

            _cccType = assembly.GetType("AICam.UI.CameraCaptureController");
            Assert.IsNotNull(_cccType, "CameraCaptureController should exist in AICam.UI");
        }

        #region クラス構造テスト

        [Test]
        public void CCC_IsMonoBehaviour()
        {
            Assert.IsTrue(typeof(MonoBehaviour).IsAssignableFrom(_cccType),
                "CameraCaptureController should inherit from MonoBehaviour");
        }

        [Test]
        public void CCC_ImplementsIUIBlockingProvider()
        {
            var coreAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "AICam.Core");
            var interfaceType = coreAssembly.GetType("AICam.Core.IUIBlockingProvider");

            Assert.IsTrue(interfaceType.IsAssignableFrom(_cccType),
                "CameraCaptureController should implement IUIBlockingProvider");
        }

        [Test]
        public void CCC_ImplementsILightingSettingsProvider()
        {
            var coreAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "AICam.Core");
            var interfaceType = coreAssembly.GetType("AICam.Core.ILightingSettingsProvider");

            Assert.IsTrue(interfaceType.IsAssignableFrom(_cccType),
                "CameraCaptureController should implement ILightingSettingsProvider");
        }

        #endregion

        #region 外部呼出 API 署名テスト — PlaceAvatarOnPlaneOnly から

        [Test]
        public void IsPointOverUIPanel_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("IsPointOverUIPanel",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "IsPointOverUIPanel should be public instance method");
            Assert.AreEqual(typeof(bool), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.AreEqual(1, parameters.Length);
            Assert.AreEqual(typeof(Vector2), parameters[0].ParameterType);
            Assert.AreEqual("screenPosition", parameters[0].Name);
        }

        #endregion

        #region 外部呼出 API 署名テスト — RuntimeFBXLoaderBridge から

        [Test]
        public void ReapplyLightingSettings_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("ReapplyLightingSettings",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "ReapplyLightingSettings should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);
            Assert.AreEqual(0, method.GetParameters().Length);
        }

        #endregion

        #region 外部呼出 API 署名テスト — UaaLBridge から

        [Test]
        public void SetPhotoController_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("SetPhotoController",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "SetPhotoController should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);
            Assert.AreEqual(1, method.GetParameters().Length);
        }

        [Test]
        public void IsRecording_PropertyExists()
        {
            var prop = _cccType.GetProperty("IsRecording",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(prop, "IsRecording should be public instance property");
            Assert.AreEqual(typeof(bool), prop.PropertyType);
            Assert.IsTrue(prop.CanRead);
        }

        [Test]
        public void UpdateLastCapturedPhoto_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("UpdateLastCapturedPhoto",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "UpdateLastCapturedPhoto should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.AreEqual(1, parameters.Length);
            Assert.AreEqual(typeof(Texture2D), parameters[0].ParameterType);
        }

        #endregion

        #region 外部呼出 API 署名テスト — Alert 系（複数箇所から）

        [Test]
        public void ShowInfo_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("ShowInfo",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "ShowInfo should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.GreaterOrEqual(parameters.Length, 2, "ShowInfo should have at least 2 parameters");
            Assert.AreEqual(typeof(string), parameters[0].ParameterType, "First param should be string (code)");
            Assert.AreEqual(typeof(string), parameters[1].ParameterType, "Second param should be string (message)");
        }

        [Test]
        public void ShowWarning_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("ShowWarning",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "ShowWarning should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.GreaterOrEqual(parameters.Length, 2);
            Assert.AreEqual(typeof(string), parameters[0].ParameterType);
            Assert.AreEqual(typeof(string), parameters[1].ParameterType);
        }

        [Test]
        public void ShowError_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("ShowError",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "ShowError should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.GreaterOrEqual(parameters.Length, 2);
            Assert.AreEqual(typeof(string), parameters[0].ParameterType);
            Assert.AreEqual(typeof(string), parameters[1].ParameterType);
        }

        [Test]
        public void HideAlert_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("HideAlert",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "HideAlert should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);
            Assert.AreEqual(0, method.GetParameters().Length);
        }

        #endregion

        #region 外部呼出 API 署名テスト — IconPreview 系

        [Test]
        public void ShowIconPreview_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("ShowIconPreview",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "ShowIconPreview should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);

            var parameters = method.GetParameters();
            Assert.GreaterOrEqual(parameters.Length, 2,
                "ShowIconPreview should have at least 2 parameters (texture, onConfirm)");
            Assert.AreEqual(typeof(Texture2D), parameters[0].ParameterType);
        }

        [Test]
        public void HideIconPreview_HasCorrectSignature()
        {
            var method = _cccType.GetMethod("HideIconPreview",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(method, "HideIconPreview should be public instance method");
            Assert.AreEqual(typeof(void), method.ReturnType);
            Assert.AreEqual(0, method.GetParameters().Length);
        }

        [Test]
        public void IsIconPreviewShowing_PropertyExists()
        {
            var prop = _cccType.GetProperty("IsIconPreviewShowing",
                BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(prop, "IsIconPreviewShowing should be public instance property");
            Assert.AreEqual(typeof(bool), prop.PropertyType);
            Assert.IsTrue(prop.CanRead);
        }

        #endregion
    }
}
