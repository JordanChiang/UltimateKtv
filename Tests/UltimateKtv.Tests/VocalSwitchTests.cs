using UltimateKtv.Playback;
using Xunit;

namespace UltimateKtv.Tests
{
    public class VocalSwitchTests
    {
        private sealed class FakeAudio : IAudioTracks
        {
            public FakeAudio(params int[] streams) => StreamList = streams.ToList();
            public List<int> StreamList { get; }
            public IReadOnlyList<int> Streams => StreamList;
            public int Track { get; set; }
            public int Channel { get; set; }
        }

        private static (VocalSwitch Switch, FakeAudio Audio) Create(params int[] streams)
        {
            var audio = new FakeAudio(streams);
            return (new VocalSwitch(audio), audio);
        }

        // ---------- 開歌 ----------

        [Fact]
        public void Single_stream_plays_the_music_channel()
        {
            var (vs, audio) = Create(1);

            vs.ApplyForNewSong(songTrack: 2, legacyDefinition: true, isYoutube: false, youtubeDefaultNoVocal: false);

            Assert.Equal(2, vs.MusicTrack);
            Assert.Equal(2, audio.Channel);
            Assert.Equal(1, audio.Track);
            Assert.Equal(VocalMode.Music, vs.Mode);
        }

        [Fact]
        public void New_definition_with_two_streams_treats_song_track_as_vocal()
        {
            var (vs, audio) = Create(1, 2);

            vs.ApplyForNewSong(songTrack: 1, legacyDefinition: false, isYoutube: false, youtubeDefaultNoVocal: false);

            Assert.Equal(2, vs.MusicTrack);
            Assert.Equal(2, audio.Track);
        }

        [Fact]
        public void Three_streams_use_song_track_as_music_even_with_new_definition()
        {
            var (vs, audio) = Create(1, 2, 3);

            vs.ApplyForNewSong(songTrack: 3, legacyDefinition: false, isYoutube: false, youtubeDefaultNoVocal: false);

            Assert.Equal(3, vs.MusicTrack);
            Assert.Equal(3, audio.Track);
        }

        [Fact]
        public void Missing_music_track_falls_back_to_first_stream()
        {
            var (vs, audio) = Create(4, 5);

            vs.ApplyForNewSong(songTrack: null, legacyDefinition: true, isYoutube: false, youtubeDefaultNoVocal: false);

            Assert.Equal(0, vs.MusicTrack);
            Assert.Equal(4, audio.Track);
        }

        [Fact]
        public void Youtube_defaults_to_vocal_removed_when_configured()
        {
            var (vs, audio) = Create(1);

            vs.ApplyForNewSong(songTrack: 0, legacyDefinition: true, isYoutube: true, youtubeDefaultNoVocal: true);

            Assert.Equal(3, audio.Channel);
            Assert.Equal(VocalMode.Music, vs.Mode);
        }

        // ---------- 人聲 / 伴唱鈕 ----------

        [Fact]
        public void Single_stream_switches_between_channels()
        {
            var (vs, audio) = Create(1);
            vs.ApplyForNewSong(1, true, false, false);

            vs.SwitchToVocal();
            Assert.Equal(2, audio.Channel);
            Assert.Equal(VocalMode.Vocal, vs.Mode);

            vs.SwitchToMusic();
            Assert.Equal(1, audio.Channel);
            Assert.Equal(VocalMode.Music, vs.Mode);
        }

        [Fact]
        public void Two_streams_vocal_picks_the_other_track_and_stays()
        {
            var (vs, audio) = Create(1, 2);
            vs.ApplyForNewSong(2, true, false, false);

            vs.SwitchToVocal();
            Assert.Equal(1, audio.Track);

            vs.SwitchToVocal();
            Assert.Equal(1, audio.Track);
        }

        [Fact]
        public void Many_streams_vocal_cycles_tracks()
        {
            var (vs, audio) = Create(1, 2, 3);
            vs.ApplyForNewSong(1, true, false, false);

            vs.SwitchToVocal();
            Assert.Equal(2, audio.Track);
            vs.SwitchToVocal();
            Assert.Equal(3, audio.Track);
            vs.SwitchToVocal();
            Assert.Equal(1, audio.Track);
        }

        [Fact]
        public void Music_when_already_music_is_ignored()
        {
            var (vs, _) = Create(1, 2);
            vs.ApplyForNewSong(2, true, false, false);

            Assert.Equal(VocalSwitchResult.Ignored, vs.SwitchToMusic());
        }

        [Fact]
        public void Youtube_toggles_between_stereo_and_vocal_removed()
        {
            var (vs, audio) = Create(1);
            vs.ApplyForNewSong(0, true, true, true);

            vs.SwitchToVocal();
            Assert.Equal(0, audio.Channel);
            Assert.Equal(VocalMode.Vocal, vs.Mode);

            vs.SwitchToMusic();
            Assert.Equal(3, audio.Channel);
        }

        // ---------- 設為伴唱 / 固定人聲 ----------

        [Fact]
        public void Set_as_music_on_single_stream_swaps_channels()
        {
            var (vs, audio) = Create(1);
            vs.ApplyForNewSong(1, true, false, false);

            int saved = vs.SetAsMusic(audio.Track);

            Assert.Equal(2, saved);
            Assert.Equal(2, audio.Channel);
        }

        [Fact]
        public void Set_as_music_on_many_streams_uses_current_track()
        {
            var (vs, audio) = Create(1, 2, 3);
            vs.ApplyForNewSong(1, true, false, false);
            vs.SwitchToVocal(); // now on track 2

            int saved = vs.SetAsMusic(audio.Track);

            Assert.Equal(2, saved);
            Assert.Equal(VocalMode.Music, vs.Mode);
        }

        [Fact]
        public void Fixed_vocal_is_applied_to_the_next_song()
        {
            var (vs, audio) = Create(1, 2, 3);
            vs.ApplyForNewSong(1, true, false, false);
            vs.SwitchToVocal(); // track 2
            vs.Fix(audio.Track);

            vs.ApplyForNewSong(1, true, false, false);

            Assert.Equal(VocalMode.Fixed, vs.Mode);
            Assert.Equal(2, audio.Track);
            Assert.Equal(0, audio.Channel);
        }

        [Fact]
        public void Fixed_vocal_on_two_streams_picks_the_non_music_track()
        {
            var (vs, audio) = Create(1, 2, 3);
            vs.ApplyForNewSong(1, true, false, false);
            vs.Fix(3);
            audio.StreamList.RemoveAt(2); // next song has 2 streams

            vs.ApplyForNewSong(1, true, false, false);

            Assert.Equal(2, audio.Track);
        }

        [Fact]
        public void Fixed_vocal_on_single_stream_uses_the_opposite_channel()
        {
            var (vs, audio) = Create(1);
            vs.ApplyForNewSong(1, true, false, false);
            vs.SwitchToVocal();
            Assert.Equal(2, vs.Fix(audio.Track));

            vs.ApplyForNewSong(2, true, false, false);

            Assert.Equal(1, audio.Channel);
        }

        [Fact]
        public void Either_button_unfixes_without_switching()
        {
            var (vs, audio) = Create(1, 2, 3);
            vs.ApplyForNewSong(1, true, false, false);
            vs.Fix(2);
            vs.ApplyForNewSong(1, true, false, false);

            Assert.Equal(VocalSwitchResult.Unfixed, vs.SwitchToMusic());
            Assert.False(vs.IsFixed);
            Assert.Equal(2, audio.Track);

            vs.Fix(2);
            Assert.Equal(VocalSwitchResult.Unfixed, vs.SwitchToVocal());
            Assert.Equal(2, audio.Track);
        }
    }
}
