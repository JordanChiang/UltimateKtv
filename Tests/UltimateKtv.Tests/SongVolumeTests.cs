using UltimateKtv.Playback;
using Xunit;

namespace UltimateKtv.Tests
{
    public class SongVolumeTests
    {
        [Fact]
        public void Starts_at_full_volume_unmuted_and_unlocked()
        {
            var volume = new SongVolume();
            Assert.Equal(100, volume.Level);
            Assert.False(volume.IsMuted);
            Assert.False(volume.IsLocked);
        }

        [Theory]
        [InlineData(33, 35)]
        [InlineData(32, 30)]
        [InlineData(37.4, 35)]
        [InlineData(37.6, 40)]
        [InlineData(100, 100)]
        public void Slider_values_snap_to_steps_of_five(double value, double expected)
        {
            Assert.Equal(expected, SongVolume.Snap(value));
        }

        [Fact]
        public void Up_and_down_move_one_step_within_range()
        {
            var volume = new SongVolume();
            Assert.Equal(100, volume.Up());
            Assert.Equal(95, volume.Down());
            Assert.Equal(95, volume.Level);

            volume.Set(5);
            Assert.Equal(0, volume.Down());
            Assert.True(volume.IsMuted);
            Assert.Equal(0, volume.Down());
        }

        [Fact]
        public void Song_opens_with_its_library_volume_or_the_default()
        {
            var volume = new SongVolume();
            Assert.Equal(45, volume.SongStarting(45));
            Assert.Equal(40, volume.SongOpened(40));
            Assert.Equal(SongVolume.DefaultSongVolume, volume.SongOpened(null));
        }

        [Fact]
        public void Unchanged_volume_is_not_saved()
        {
            var volume = new SongVolume();
            volume.Set(volume.SongStarting(40));
            volume.SongOpened(40);
            Assert.Null(volume.VolumeToSave());
        }

        [Fact]
        public void Adjusted_volume_is_saved_when_the_song_ends()
        {
            var volume = new SongVolume();
            volume.Set(volume.SongStarting(40));
            volume.SongOpened(40);
            volume.Up();
            Assert.Equal(45, volume.VolumeToSave());
        }

        [Fact]
        public void Library_volume_off_the_step_is_saved_as_the_snapped_volume()
        {
            var volume = new SongVolume();
            volume.SongOpened(33);
            volume.Set((int)SongVolume.Snap(33));
            Assert.Equal(35, volume.VolumeToSave());
        }

        [Fact]
        public void Muted_volume_is_not_saved()
        {
            var volume = new SongVolume();
            volume.SongOpened(40);
            volume.Set(0);
            Assert.Null(volume.VolumeToSave());
        }

        [Fact]
        public void Locked_volume_applies_to_every_song_and_is_never_saved()
        {
            var volume = new SongVolume();
            volume.Set(70);
            volume.Lock();
            volume.Down();

            Assert.Equal(70, volume.LockedLevel);
            Assert.Null(volume.VolumeToSave());
            Assert.Equal(70, volume.SongStarting(30));
            Assert.Equal(70, volume.SongOpened(30));

            volume.Unlock();
            Assert.Equal(30, volume.SongStarting(30));
            Assert.Equal(30, volume.SongOpened(30));
        }

        [Fact]
        public void Mute_remembers_the_volume_and_unmute_restores_it()
        {
            var volume = new SongVolume();
            volume.Set(40);

            Assert.Equal(0, volume.ToggleMute());
            Assert.True(volume.IsMuted);
            Assert.Equal(40, volume.ToggleMute());
            Assert.False(volume.IsMuted);
        }

        [Fact]
        public void Unmute_without_a_remembered_volume_uses_the_fallback_volume()
        {
            var volume = new SongVolume();
            volume.Set(0); // 拖到 0 = 靜音，但沒有記住靜音前的音量
            Assert.True(volume.IsMuted);

            Assert.Equal(SongVolume.UnmuteVolume, volume.ToggleMute());
            Assert.False(volume.IsMuted);
        }

        [Fact]
        public void Slider_value_close_to_a_step_keeps_its_fraction()
        {
            // 滑桿只把離 5 的倍數超過 0.01 的值拉回，35.004 會保留下來
            var volume = new SongVolume();
            volume.SongOpened(35);
            volume.Set(35.004);

            Assert.Equal(40.004, volume.Up(), 6);
            Assert.Equal(40, volume.VolumeToSave());
        }

        [Fact]
        public void Raising_the_volume_from_zero_unmutes()
        {
            var volume = new SongVolume();
            volume.Set(0);
            volume.Up();
            Assert.False(volume.IsMuted);
            Assert.Equal(5, volume.Level);
        }
    }
}
