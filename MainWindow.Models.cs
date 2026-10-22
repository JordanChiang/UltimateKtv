using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace UltimateKtv
{
    /// <summary>
    /// 提供 INotifyPropertyChanged 的基底類別。
    /// 分頁列表使用「固定的一組列 (slot)」，換頁時只更新屬性值，
    /// DataGrid 不需要重建任何一列 (避免換頁時 UI 執行緒卡住、跑馬燈停頓)。
    /// </summary>
    public abstract class NotifyItemBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>歌星按鈕格 (QuickSingerGrid) 的項目；Category 決定文字顏色。</summary>
    public sealed record QuickSingerItem(string Name, string Category)
    {
        /// <summary>Singer_Type：0/4 男、1/5 女、2/6 團體 (含外語)，其餘為其他。</summary>
        public static QuickSingerItem Create(string name, string singerType)
        {
            string category = singerType switch
            {
                "0" or "4" => "Male",
                "1" or "5" => "Female",
                "2" or "6" => "Group",
                _ => "Other"
            };
            return new QuickSingerItem(name, category);
        }
    }

    /// <summary>
    /// Data class for song display in the SongListGrid
    /// </summary>
    public class SongDisplayItem : NotifyItemBase
    {
        private string _songId = string.Empty;
        private string _songName = string.Empty;
        private string _singerName = string.Empty;
        private string _language = string.Empty;
        private string _filePath = string.Empty;
        private int _songWordCount;
        private int _songPlayCount;
        private DateTime? _songCreatDate;
        private int _volume = 90;
        private int _audioTrack = 0;
        private string _orderedBy = string.Empty;
        private bool _isYoutube = false;
        private string _youtubeId = string.Empty;
        private string? _thumbnailUrl;
        private string _duration = string.Empty;

        public string SongId { get => _songId; set => Set(ref _songId, value); }
        public string SongName { get => _songName; set => Set(ref _songName, value); }
        public string SingerName { get => _singerName; set => Set(ref _singerName, value); }
        public string Language { get => _language; set => Set(ref _language, value); }
        public string FilePath { get => _filePath; set => Set(ref _filePath, value); }
        public int Song_WordCount { get => _songWordCount; set => Set(ref _songWordCount, value); }
        public int Song_PlayCount { get => _songPlayCount; set => Set(ref _songPlayCount, value); }
        public DateTime? Song_CreatDate { get => _songCreatDate; set => Set(ref _songCreatDate, value); } // Date when song was added
        public int Volume { get => _volume; set => Set(ref _volume, value); } // Default volume from Song_Volume
        public int AudioTrack { get => _audioTrack; set => Set(ref _audioTrack, value); } // Default audio track from Song_Track
        public string OrderedBy { get => _orderedBy; set => Set(ref _orderedBy, value); } // Username who ordered the song
        public bool IsYoutube { get => _isYoutube; set => Set(ref _isYoutube, value); }
        public string YoutubeId { get => _youtubeId; set => Set(ref _youtubeId, value); }
        public string? ThumbnailUrl { get => _thumbnailUrl; set => Set(ref _thumbnailUrl, value); }
        /// <summary>YouTube 縮圖右上角顯示的歌曲長度 (H:MM:SS 或 M:SS)，未知時為空字串。</summary>
        public string Duration { get => _duration; set => Set(ref _duration, value); }

        /// <summary>從歌庫的歌建立列表項目；歌庫未設定音量時用 90、伴唱位置用 0。</summary>
        public static SongDisplayItem FromSong(Song song) => new SongDisplayItem
        {
            SongId = song.Id,
            SongName = song.Name,
            SingerName = song.Singer,
            Language = song.Lang,
            FilePath = song.FilePath,
            Song_WordCount = song.WordCount,
            Song_PlayCount = song.PlayCount,
            Song_CreatDate = song.CreatDate,
            Volume = song.Volume ?? 90,
            AudioTrack = song.Track ?? 0,
        };

        /// <summary>把秒數格式化為 H:MM:SS (不到 1 小時為 M:SS)；0 以下回傳空字串。</summary>
        public static string FormatDuration(double totalSeconds)
        {
            if (totalSeconds <= 0) return string.Empty;
            var d = TimeSpan.FromSeconds(totalSeconds);
            return d.TotalHours >= 1
                ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
                : $"{d.Minutes}:{d.Seconds:D2}";
        }

        /// <summary>把另一個項目的所有欄位複製到這個項目 (用於分頁列表的固定列)。</summary>
        public void CopyFrom(SongDisplayItem src)
        {
            SongId = src.SongId;
            SongName = src.SongName;
            SingerName = src.SingerName;
            Language = src.Language;
            FilePath = src.FilePath;
            Song_WordCount = src.Song_WordCount;
            Song_PlayCount = src.Song_PlayCount;
            Song_CreatDate = src.Song_CreatDate;
            Volume = src.Volume;
            AudioTrack = src.AudioTrack;
            OrderedBy = src.OrderedBy;
            IsYoutube = src.IsYoutube;
            YoutubeId = src.YoutubeId;
            ThumbnailUrl = src.ThumbnailUrl;
            Duration = src.Duration;
        }

        /// <summary>
        /// 建立獨立的複本。從列表的固定列取出項目要往後傳遞 (加入待播、加入最愛、下載) 時使用，
        /// 避免之後換頁時列的內容被改掉。
        /// </summary>
        public SongDisplayItem Clone()
        {
            var copy = new SongDisplayItem();
            copy.CopyFrom(this);
            return copy;
        }
    }

    /// <summary>
    /// Data class for waiting list items in the WaitingListGrid
    /// </summary>
    public class WaitingListItem : NotifyItemBase
    {
        private string? _songId;
        private string _waitingListSongName = string.Empty;
        private string _waitingListSingerName = string.Empty;
        private string _filePath = string.Empty;
        private int _volume = 90;
        private int _audioTrack = 0;
        private string _orderedBy = string.Empty;
        private bool _isYoutube = false;
        private Guid _id = Guid.NewGuid();

        public string? SongId { get => _songId; set => Set(ref _songId, value); }
        public string WaitingListSongName { get => _waitingListSongName; set => Set(ref _waitingListSongName, value); } // Song Name
        public string WaitingListSingerName { get => _waitingListSingerName; set => Set(ref _waitingListSingerName, value); } // Singer Name
        public string FilePath { get => _filePath; set => Set(ref _filePath, value); } // File path for playback
        public int Volume { get => _volume; set => Set(ref _volume, value); } // Volume setting
        public int AudioTrack { get => _audioTrack; set => Set(ref _audioTrack, value); } // Audio track setting
        public string OrderedBy { get => _orderedBy; set => Set(ref _orderedBy, value); } // Username who ordered the song
        public bool IsYoutube { get => _isYoutube; set => Set(ref _isYoutube, value); }
        public Guid Id { get => _id; set => Set(ref _id, value); } // Unique ID for the waiting list item

        public void CopyFrom(WaitingListItem src)
        {
            SongId = src.SongId;
            WaitingListSongName = src.WaitingListSongName;
            WaitingListSingerName = src.WaitingListSingerName;
            FilePath = src.FilePath;
            Volume = src.Volume;
            AudioTrack = src.AudioTrack;
            OrderedBy = src.OrderedBy;
            IsYoutube = src.IsYoutube;
            Id = src.Id;
        }

        public WaitingListItem Clone()
        {
            var copy = new WaitingListItem();
            copy.CopyFrom(this);
            return copy;
        }
    }
}
