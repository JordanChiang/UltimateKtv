using UltimateKtv.Playback;
using Xunit;

namespace UltimateKtv.Tests
{
    public class RandomPlayGateTests
    {
        [Theory]
        // 隨機點歌開啟, 有無正在播歌, 待播清單數, 是否應補隨機歌
        [InlineData(true, false, 0, true)]   // 閒置、清單空           → 補
        [InlineData(true, true, 0, false)]   // 播歌中、清單空 (本次 bug) → 不補
        [InlineData(true, false, 2, false)]  // 閒置但清單有歌         → 不補
        [InlineData(true, true, 2, false)]   // 播歌中且清單有歌       → 不補
        [InlineData(false, false, 0, false)] // 隨機點歌關閉           → 不補
        public void Decides_when_to_auto_queue(bool enabled, bool hasCurrent, int count, bool expected)
            => Assert.Equal(expected, RandomPlayGate.ShouldAutoQueue(enabled, hasCurrent, count));
    }
}
