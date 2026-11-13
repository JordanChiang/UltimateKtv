using Xunit;

namespace UltimateKtv.Tests
{
    /// <summary>
    /// DirectShow 音訊渲染器選擇規則的測試。
    /// 回歸來源：commit c783754 曾在預設音效卡時把 AudioRenderer 設成 null，
    /// 導致 MediaKit 的 InsertAudioRenderer 回傳 null、開檔連接音訊渲染器時爆 E_POINTER，
    /// 讓 DirectShow 引擎完全無法播放 (libmpv 不受影響)。
    /// </summary>
    public class DirectShowAudioRendererTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Default DirectSound Device")]
        public void 預設或空值_回傳預設裝置名稱(string? device)
        {
            Assert.Equal("Default DirectSound Device", DirectShowAudioRenderer.Resolve(device));
        }

        [Fact]
        public void 特定裝置_原樣回傳()
        {
            const string device = "喇叭 (High Definition Audio Device)";
            Assert.Equal(device, DirectShowAudioRenderer.Resolve(device));
        }

        // 核心不變量：任何輸入都不得回傳 null / 空字串，否則 MediaKit 開檔會爆 E_POINTER
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Default DirectSound Device")]
        [InlineData("喇叭 (High Definition Audio Device)")]
        public void 任何輸入_都不回傳null或空字串(string? device)
        {
            Assert.False(string.IsNullOrEmpty(DirectShowAudioRenderer.Resolve(device)));
        }
    }
}
