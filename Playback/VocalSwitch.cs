using System.Collections.Generic;
using System.Linq;

namespace UltimateKtv.Playback
{
    /// <summary>
    /// player 的音軌/聲道旋鈕。Channel：0=立體聲, 1=左聲道, 2=右聲道, 3=消人聲。
    /// </summary>
    public interface IAudioTracks
    {
        IReadOnlyList<int> Streams { get; }
        int Track { get; set; }
        int Channel { get; set; }
    }

    /// <summary>人聲/伴唱按鈕應顯示的狀態。</summary>
    public enum VocalMode
    {
        Vocal,
        Music,
        /// <summary>固定人聲：每首歌開播都套用同一個人聲音軌/聲道。</summary>
        Fixed
    }

    public enum VocalSwitchResult
    {
        Switched,
        /// <summary>原本是固定人聲，這次點擊只解除固定。</summary>
        Unfixed,
        Ignored
    }

    /// <summary>
    /// 人聲/伴唱切換：把「這首歌的伴唱在哪個音軌或聲道」的規則藏在這裡，
    /// 呼叫端只說要人聲還是伴唱。固定人聲的狀態跨歌曲保留。
    /// </summary>
    public sealed class VocalSwitch
    {
        private readonly IAudioTracks _audio;

        public VocalSwitch(IAudioTracks audio)
        {
            _audio = audio;
        }

        /// <summary>伴唱所在的音軌 (多音軌) 或聲道 (單音軌)。</summary>
        public int MusicTrack { get; private set; }

        public bool IsYoutube { get; private set; }

        /// <summary>固定的人聲音軌/聲道；-1 表示未固定。</summary>
        public int FixedTrack { get; private set; } = -1;

        public bool IsFixed => FixedTrack != -1;

        private int StreamCount => _audio.Streams.Count;

        public VocalMode Mode
        {
            get
            {
                // YouTube 只有兩種狀態：立體聲 = 人聲，其餘 = 伴唱
                if (IsYoutube) return _audio.Channel == 0 ? VocalMode.Vocal : VocalMode.Music;
                if (IsFixed) return VocalMode.Fixed;
                bool musicActive = StreamCount > 1
                    ? _audio.Track == MusicTrack
                    : _audio.Channel == MusicTrack;
                return musicActive ? VocalMode.Music : VocalMode.Vocal;
            }
        }

        /// <summary>
        /// 新歌開檔後套用：決定伴唱位置，預設播伴唱；固定人聲時改播人聲。
        /// </summary>
        /// <param name="songTrack">歌庫的 Song_Track；null 表示沒有資料。</param>
        /// <param name="legacyDefinition">舊式聲道定義：Song_Track 直接是伴唱位置。</param>
        public void ApplyForNewSong(int? songTrack, bool legacyDefinition, bool isYoutube, bool youtubeDefaultNoVocal)
        {
            IsYoutube = isYoutube;

            if (songTrack is int dbTrack)
            {
                // 新式定義且只有 1~2 個音軌時，Song_Track 記的是人聲位置
                MusicTrack = legacyDefinition || StreamCount >= 3 ? dbTrack : (dbTrack == 1 ? 2 : 1);
            }
            else
            {
                MusicTrack = 0;
            }

            var streams = _audio.Streams;
            if (IsFixed)
            {
                if (streams.Count <= 1)
                {
                    // 單音軌：人聲在伴唱的另一個聲道
                    if (streams.Count > 0) _audio.Track = streams[0];
                    _audio.Channel = MusicTrack == 1 ? 2 : 1;
                }
                else
                {
                    _audio.Channel = 0; // Stereo
                    if (streams.Count >= 3 && streams.Contains(FixedTrack))
                    {
                        _audio.Track = FixedTrack;
                    }
                    else
                    {
                        // 雙音軌或沒有固定的那個音軌：取第一個不是伴唱的音軌
                        int vocalTrack = streams.FirstOrDefault(t => t != MusicTrack);
                        if (vocalTrack == 0) vocalTrack = streams[0];
                        _audio.Track = vocalTrack;
                    }
                }
            }
            else if (streams.Count == 1)
            {
                _audio.Channel = MusicTrack;
                _audio.Track = streams[0];
            }
            else if (streams.Contains(MusicTrack))
            {
                _audio.Track = MusicTrack;
            }
            else if (streams.Count > 0)
            {
                _audio.Track = streams[0];
            }

            if (isYoutube)
            {
                _audio.Channel = youtubeDefaultNoVocal ? 3 : 1;
            }
        }

        /// <summary>人聲鈕：單音軌換到人聲聲道；多音軌輪流切換音軌。固定人聲時只解除固定。</summary>
        public VocalSwitchResult SwitchToVocal()
        {
            if (IsFixed)
            {
                FixedTrack = -1;
                return VocalSwitchResult.Unfixed;
            }

            if (IsYoutube)
            {
                _audio.Channel = 0;
                return VocalSwitchResult.Switched;
            }

            var streams = _audio.Streams;
            if (streams.Count == 1)
            {
                _audio.Channel = MusicTrack == 1 ? 2 : 1;
                return VocalSwitchResult.Switched;
            }

            if (streams.Count == 2 && Mode != VocalMode.Music)
            {
                // 雙音軌且已在人聲：不動
                return VocalSwitchResult.Switched;
            }

            if (streams.Count < 2) return VocalSwitchResult.Ignored;

            // 多音軌：從目前音軌 (找不到時從伴唱音軌) 換到下一個
            int currentIndex = IndexOf(streams, _audio.Track);
            if (currentIndex == -1) currentIndex = IndexOf(streams, MusicTrack);
            if (currentIndex == -1) currentIndex = 0;
            _audio.Track = streams[(currentIndex + 1) % streams.Count];
            return VocalSwitchResult.Switched;
        }

        /// <summary>伴唱鈕：換到伴唱。固定人聲時只解除固定。</summary>
        public VocalSwitchResult SwitchToMusic()
        {
            if (IsYoutube)
            {
                _audio.Channel = 3;
                return VocalSwitchResult.Switched;
            }

            if (IsFixed)
            {
                FixedTrack = -1;
                return VocalSwitchResult.Unfixed;
            }

            if (Mode == VocalMode.Music) return VocalSwitchResult.Ignored;

            if (StreamCount <= 1)
            {
                _audio.Channel = MusicTrack;
            }
            else if (_audio.Track != MusicTrack)
            {
                _audio.Track = MusicTrack;
            }
            return VocalSwitchResult.Switched;
        }

        /// <summary>
        /// 「設為伴唱」：單音軌時左右聲道互換；多音軌時把 currentTrack 當作伴唱，並切到伴唱。
        /// </summary>
        /// <param name="currentTrack">選單打開時正在播的音軌。</param>
        /// <returns>新的伴唱位置 (要寫回歌庫的 Song_Track)。</returns>
        public int SetAsMusic(int currentTrack)
        {
            MusicTrack = StreamCount <= 1 ? (MusicTrack == 1 ? 2 : 1) : currentTrack;
            SwitchToMusic();
            return MusicTrack;
        }

        /// <summary>「固定人聲」：之後每首歌都播這個音軌 (單音軌時是聲道)。</summary>
        /// <param name="currentTrack">選單打開時正在播的音軌。</param>
        public int Fix(int currentTrack)
        {
            FixedTrack = StreamCount <= 1 ? _audio.Channel : currentTrack;
            return FixedTrack;
        }

        private static int IndexOf(IReadOnlyList<int> list, int value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == value) return i;
            }
            return -1;
        }
    }
}
