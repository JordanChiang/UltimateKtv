using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        private void MaleSingerFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            FilterAndDisplaySingers("MaleSinger", s => s.TryGetValue("Singer_Type", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) && typeVal == 0);
        }

        private void FemaleSingerFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            FilterAndDisplaySingers("FemaleSinger", s => s.TryGetValue("Singer_Type", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) && typeVal == 1);
        }

        private void GroupSingerFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            FilterAndDisplaySingers("GroupSinger", s => s.TryGetValue("Singer_Type", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) && typeVal == 2);
        }

        private void ForeignMaleSingerFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            FilterAndDisplaySingers("ForeignMaleSinger", s => s.TryGetValue("Singer_Type", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) && typeVal == 4);
        }

        private void ForeignFemaleSingerFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            FilterAndDisplaySingers("ForeignFemaleSinger", s => s.TryGetValue("Singer_Type", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) && typeVal == 5);
        }

        private void ForeignGroupSingerFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            FilterAndDisplaySingers("ForeignGroupSinger", s => s.TryGetValue("Singer_Type", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) && typeVal == 6);
        }

        private void OtherSingerFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            var knownTypes = new HashSet<int> { 0, 1, 2, 4, 5, 6 };
            FilterAndDisplaySingers("OtherSinger", s =>
            {
                if (s.TryGetValue("Singer_Type", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal))
                {
                    // If type is valid, check if it's NOT a known type
                    return !knownTypes.Contains(typeVal);
                }
                // Include singers with missing or invalid type in the "Other" category
                return true;
            });
        }


        private void NewSongFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var lang = (sender as Button)?.Tag as string ?? "國語";
            var cutoffDate = DateTime.Today.AddDays(-NewSongDays);

            var newSongs = SongDatas.SongData?
                .Where(s =>
                {
                    bool isNew = false;
                    if (s.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime createDate)
                    {
                        isNew = createDate >= cutoffDate;
                    }

                    if (!isNew) return false;

                    var songLang = s.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() : "";
                    if (lang == "其它")
                    {
                        return songLang != "國語" && songLang != "台語";
                    }
                    return songLang == lang;
                })
                .Select(s => new SongDisplayItem
                {
                    SongId = s.TryGetValue("Song_Id", out var id) ? id?.ToString() ?? "" : "",
                    SongName = s.TryGetValue("Song_SongName", out var n) ? n?.ToString() ?? "" : "",
                    SingerName = s.TryGetValue("Song_Singer", out var sn) ? sn?.ToString() ?? "" : "",
                    Language = s.TryGetValue("Song_Lang", out var lg) ? lg?.ToString() ?? "" : "",
                    FilePath = s.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                    Song_WordCount = s.TryGetValue("Song_WordCount", out var wcObj) && int.TryParse(wcObj?.ToString(), out int wc) ? wc : 0,
                    Song_PlayCount = s.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int pc) ? pc : 0,
                    Song_CreatDate = s.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime dt ? dt : (DateTime?)null,
                    Volume = s.TryGetValue("Song_Volume", out var vol) && int.TryParse(vol?.ToString(), out var iv) ? iv : 90,
                    AudioTrack = s.TryGetValue("Song_Track", out var tr) && int.TryParse(tr?.ToString(), out var it) ? it : 0,
                })
                .ToList() ?? new List<SongDisplayItem>();

            // Apply sorting based on settings
            newSongs = SongDatas.ApplySongSorting(newSongs, (s, key) => s.GetType().GetProperty(key)?.GetValue(s, null));

            DisplaySongsInGrid(newSongs, $"新進歌曲 - {lang}");
        }

        private void RankingFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var filterTag = (sender as Button)?.Tag as string ?? "國語-單曲";
            string[] parts = filterTag.Split('-');
            string langFilter = parts[0];
            string typeFilter = parts.Length > 1 ? parts[1] : "";

            var rankedSongs = SongDatas.SongData?
                .Where(s =>
                {
                    // Skip songs that have never been played
                    var playCount = s.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int pc) ? pc : 0;
                    if (playCount == 0) return false;

                    var songLang = s.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() : "";
                    bool langMatch;
                    if (langFilter == "其它")
                    {
                        langMatch = songLang != "國語" && songLang != "台語";
                    }
                    else
                    {
                        langMatch = songLang == langFilter;
                    }

                    if (!langMatch) return false;

                    // Determine if the song is a duet based on Song_SingerType field from the song data.
                    // As per the request, a value of 3 indicates a duet ("合唱").
                    var songSingerType = s.TryGetValue("Song_SingerType", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) ? typeVal : -1;
                    bool isDuet = songSingerType == 3;

                    if (typeFilter == "合唱") return isDuet;
                    if (typeFilter == "單曲") return !isDuet;

                    // If it's Mandarin or Taiwanese but no type filter is specified, default to excluding duets
                    if (langFilter == "國語" || langFilter == "台語") return !isDuet;

                    return true;
                })
                .Select(s => new SongDisplayItem
                {
                    SongId = s.TryGetValue("Song_Id", out var id) ? id?.ToString() ?? "" : "",
                    SongName = s.TryGetValue("Song_SongName", out var n) ? n?.ToString() ?? "" : "",
                    SingerName = s.TryGetValue("Song_Singer", out var sn) ? sn?.ToString() ?? "" : "",
                    Language = s.TryGetValue("Song_Lang", out var lg) ? lg?.ToString() ?? "" : "",
                    FilePath = s.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "",
                    Song_WordCount = s.TryGetValue("Song_WordCount", out var wcObj) && int.TryParse(wcObj?.ToString(), out int wc) ? wc : 0,
                    Song_PlayCount = s.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int pc) ? pc : 0,
                    Volume = s.TryGetValue("Song_Volume", out var vol) && int.TryParse(vol?.ToString(), out var iv) ? iv : 90,
                    AudioTrack = s.TryGetValue("Song_Track", out var tr) && int.TryParse(tr?.ToString(), out var it) ? it : 0,
                })
                .OrderByDescending(s => s.Song_PlayCount)
                .ToList() ?? new List<SongDisplayItem>();

            DisplaySongsInGrid(rankedSongs, $"排行 - {filterTag}");
        }

        private void GenerationFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var genKey = (sender as Button)?.Tag as string ?? "1";

            // Retrieve the pre-cached name and song list.
            var periodName = SongDatas.GenerationNames?.GetValueOrDefault(genKey, $"第{genKey}代");
            var cachedSongs = SongDatas.GenerationSongs?.GetValueOrDefault(genKey, new List<Dictionary<string, object?>>());

            if (cachedSongs == null || periodName == null)
            {
                //               DisplaySongsInGrid(new List<SongDisplayItem>(), $"世代 - 第{genKey}代 (快取錯誤)");
                return;
            }

            // Convert the cached data into the UI display model. This is very fast.
            var genSongs = cachedSongs.Select(s => new SongDisplayItem
            {
                SongId = s.TryGetValue("Song_Id", out var idObj) ? idObj?.ToString() ?? "" : "",
                SongName = s.TryGetValue("Song_SongName", out var nameObj) ? nameObj?.ToString() ?? "" : "",
                SingerName = s.TryGetValue("Song_Singer", out var singerObj) ? singerObj?.ToString() ?? "" : "",
                Language = s.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() ?? "" : "",
                FilePath = s.TryGetValue("FilePath", out var pathObj) ? pathObj?.ToString() ?? "" : "",
                Song_WordCount = s.TryGetValue("Song_WordCount", out var wcObj) && int.TryParse(wcObj?.ToString(), out int wc) ? wc : 0,
                Song_PlayCount = s.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int pc) ? pc : 0,
                Song_CreatDate = s.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime dt ? dt : (DateTime?)null,
                Volume = s.TryGetValue("Song_Volume", out var volObj) && int.TryParse(volObj?.ToString(), out int vol) ? vol : 90,
                AudioTrack = s.TryGetValue("Song_Track", out var trackObj) && int.TryParse(trackObj?.ToString(), out int track) ? track : 0
            }).ToList();

            // Apply sorting based on settings
            genSongs = SongDatas.ApplySongSorting(genSongs, (s, key) => s.GetType().GetProperty(key)?.GetValue(s, null));

            DisplaySongsInGrid(genSongs, $"世代 - {periodName}");
        }
    }
}