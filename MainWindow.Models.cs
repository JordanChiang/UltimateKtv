using System;
using System.Globalization;

namespace UltimateKtv
{
    /// <summary>
    /// Data class for song display in the SongListGrid
    /// </summary>
    public class SongDisplayItem
    {
        public string SongId { get; set; } = string.Empty;
        public string SongName { get; set; } = string.Empty;
        public string SingerName { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public int Song_WordCount { get; set; }
        public int Song_PlayCount { get; set; }
        public DateTime? Song_CreatDate { get; set; } // Date when song was added
        public int Volume { get; set; } = 90; // Default volume from Song_Volume
        public int AudioTrack { get; set; } = 0; // Default audio track from Song_Track
        public string OrderedBy { get; set; } = string.Empty; // Username who ordered the song
        public bool IsYoutube { get; set; } = false;
        public string? ThumbnailUrl { get; set; }
    }

    /// <summary>
    /// Data class for waiting list items in the WaitingListGrid
    /// </summary>
    public class WaitingListItem
    {
        public string? SongId { get; set; }
        public string WaitingListSongName { get; set; } = string.Empty; // Song Name
        public string WaitingListSingerName { get; set; } = string.Empty; // Singer Name
        public string FilePath { get; set; } = string.Empty; // File path for playback
        public int Volume { get; set; } = 90; // Volume setting
        public int AudioTrack { get; set; } = 0; // Audio track setting
        public string OrderedBy { get; set; } = string.Empty; // Username who ordered the song
        public bool IsYoutube { get; set; } = false;
        public Guid Id { get; set; } = Guid.NewGuid(); // Unique ID for the waiting list item
    }
}