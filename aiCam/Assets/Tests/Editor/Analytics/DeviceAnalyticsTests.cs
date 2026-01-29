using NUnit.Framework;
using PierCamera.Analytics;

namespace AICam.Analytics.Tests
{
    /// <summary>
    /// Issue #473: DeviceAnalytics.HasLiDAR() のテスト
    /// LiDAR判定が正しく動作することを確認
    /// </summary>
    [TestFixture]
    public class DeviceAnalyticsTests
    {
        #region LiDARあり端末のテスト

        [Test]
        public void HasLiDAR_iPhone12Pro_ReturnsTrue()
        {
            // iPhone 12 Pro
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone13,3"));
        }

        [Test]
        public void HasLiDAR_iPhone12ProMax_ReturnsTrue()
        {
            // iPhone 12 Pro Max
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone13,4"));
        }

        [Test]
        public void HasLiDAR_iPhone13Pro_ReturnsTrue()
        {
            // iPhone 13 Pro
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone14,2"));
        }

        [Test]
        public void HasLiDAR_iPhone13ProMax_ReturnsTrue()
        {
            // iPhone 13 Pro Max
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone14,3"));
        }

        [Test]
        public void HasLiDAR_iPhone14Pro_ReturnsTrue()
        {
            // iPhone 14 Pro
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone15,2"));
        }

        [Test]
        public void HasLiDAR_iPhone14ProMax_ReturnsTrue()
        {
            // iPhone 14 Pro Max
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone15,3"));
        }

        [Test]
        public void HasLiDAR_iPhone15Pro_ReturnsTrue()
        {
            // iPhone 15 Pro
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone16,1"));
        }

        [Test]
        public void HasLiDAR_iPhone15ProMax_ReturnsTrue()
        {
            // iPhone 15 Pro Max
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone16,2"));
        }

        [Test]
        public void HasLiDAR_iPhone16Pro_ReturnsTrue()
        {
            // iPhone 16 Pro
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone17,1"));
        }

        [Test]
        public void HasLiDAR_iPhone16ProMax_ReturnsTrue()
        {
            // iPhone 16 Pro Max
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPhone17,2"));
        }

        [Test]
        public void HasLiDAR_iPadPro2020_ReturnsTrue()
        {
            // iPad Pro 2020
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPad8,9"));
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPad8,10"));
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPad8,11"));
            Assert.IsTrue(DeviceAnalytics.HasLiDAR("iPad8,12"));
        }

        #endregion

        #region LiDARなし端末のテスト

        [Test]
        public void HasLiDAR_iPhone11_ReturnsFalse()
        {
            // iPhone 11 - Issue #473 で問題が報告された端末
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone12,1"));
        }

        [Test]
        public void HasLiDAR_iPhone11Pro_ReturnsFalse()
        {
            // iPhone 11 Pro (LiDARなし)
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone12,3"));
        }

        [Test]
        public void HasLiDAR_iPhone11ProMax_ReturnsFalse()
        {
            // iPhone 11 Pro Max (LiDARなし)
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone12,5"));
        }

        [Test]
        public void HasLiDAR_iPhone12_ReturnsFalse()
        {
            // iPhone 12 (非Pro)
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone13,2"));
        }

        [Test]
        public void HasLiDAR_iPhone12Mini_ReturnsFalse()
        {
            // iPhone 12 mini
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone13,1"));
        }

        [Test]
        public void HasLiDAR_iPhone13_ReturnsFalse()
        {
            // iPhone 13 (非Pro)
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone14,5"));
        }

        [Test]
        public void HasLiDAR_iPhone13Mini_ReturnsFalse()
        {
            // iPhone 13 mini
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone14,4"));
        }

        [Test]
        public void HasLiDAR_iPhone14_ReturnsFalse()
        {
            // iPhone 14 (非Pro)
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone14,7"));
        }

        [Test]
        public void HasLiDAR_iPhone14Plus_ReturnsFalse()
        {
            // iPhone 14 Plus
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone14,8"));
        }

        [Test]
        public void HasLiDAR_iPhoneSE2nd_ReturnsFalse()
        {
            // iPhone SE (2nd gen)
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone12,8"));
        }

        [Test]
        public void HasLiDAR_iPhoneSE3rd_ReturnsFalse()
        {
            // iPhone SE (3rd gen)
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone14,6"));
        }

        [Test]
        public void HasLiDAR_iPhoneXS_ReturnsFalse()
        {
            // iPhone XS
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone11,2"));
        }

        [Test]
        public void HasLiDAR_iPhoneXR_ReturnsFalse()
        {
            // iPhone XR
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("iPhone11,8"));
        }

        #endregion

        #region エッジケースのテスト

        [Test]
        public void HasLiDAR_NullInput_ReturnsFalse()
        {
            // null入力
            Assert.IsFalse(DeviceAnalytics.HasLiDAR(null));
        }

        [Test]
        public void HasLiDAR_EmptyInput_ReturnsFalse()
        {
            // 空文字入力
            Assert.IsFalse(DeviceAnalytics.HasLiDAR(""));
        }

        [Test]
        public void HasLiDAR_UnknownDevice_ReturnsFalse()
        {
            // 未知のデバイス
            Assert.IsFalse(DeviceAnalytics.HasLiDAR("UnknownDevice"));
        }

        #endregion

        #region デバイスカテゴリのテスト

        [Test]
        public void GetDeviceCategory_iPhone11_ReturnsLowEnd()
        {
            // iPhone 11はLowEnd（LiDARなし）
            Assert.AreEqual(DeviceAnalytics.DeviceCategory.LowEnd, DeviceAnalytics.GetDeviceCategory("iPhone12,1"));
        }

        [Test]
        public void GetDeviceCategory_iPhone13_ReturnsStandard()
        {
            // iPhone 13（非Pro）はStandard
            Assert.AreEqual(DeviceAnalytics.DeviceCategory.Standard, DeviceAnalytics.GetDeviceCategory("iPhone14,5"));
        }

        [Test]
        public void GetDeviceCategory_iPhone14Pro_ReturnsMidRange()
        {
            // iPhone 14 ProはMidRange
            Assert.AreEqual(DeviceAnalytics.DeviceCategory.MidRange, DeviceAnalytics.GetDeviceCategory("iPhone15,2"));
        }

        [Test]
        public void GetDeviceCategory_iPhone15Pro_ReturnsHighEnd()
        {
            // iPhone 15 ProはHighEnd
            Assert.AreEqual(DeviceAnalytics.DeviceCategory.HighEnd, DeviceAnalytics.GetDeviceCategory("iPhone16,1"));
        }

        #endregion
    }
}
