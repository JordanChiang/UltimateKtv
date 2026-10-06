using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Input;

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


        private async void NewSongFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var lang = (sender as Button)?.Tag as string ?? "國語";
            var cutoffDate = DateTime.Today.AddDays(-NewSongDays);

            var newSongs = await Task.Run(() =>
            {
                var songs = SongDatas.SongData?
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

                // Fast direct property getter instead of reflection
                return SongDatas.ApplySongSorting(songs, (s, key) => key switch
                {
                    "Song_PlayCount" or "PlayCount" => s.Song_PlayCount,
                    "Song_CreatDate" => s.Song_CreatDate,
                    "Song_WordCount" => s.Song_WordCount,
                    "Song_SongName" or "SongName" => s.SongName,
                    _ => null
                });
            });

            DisplaySongsInGrid(newSongs, $"新進歌曲 - {lang}");
        }

        private async void RankingFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var filterTag = (sender as Button)?.Tag as string ?? "國語-單曲";
            string[] parts = filterTag.Split('-');
            string langFilter = parts[0];
            string typeFilter = parts.Length > 1 ? parts[1] : "";

            var rankedSongs = await Task.Run(() =>
            {
                return SongDatas.SongData?
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

                        var songSingerType = s.TryGetValue("Song_SingerType", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) ? typeVal : -1;
                        bool isDuet = songSingerType == 3;

                        if (typeFilter == "合唱") return isDuet;
                        if (typeFilter == "單曲") return !isDuet;

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
            });

            DisplaySongsInGrid(rankedSongs, $"排行 - {filterTag}");
        }

        private async void GenerationFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var genKey = (sender as Button)?.Tag as string ?? "1";

            // Retrieve the pre-cached name and song list.
            var periodName = SongDatas.GenerationNames?.GetValueOrDefault(genKey, $"第{genKey}代");
            var cachedSongs = SongDatas.GenerationSongs?.GetValueOrDefault(genKey, new List<Dictionary<string, object?>>());

            if (cachedSongs == null || periodName == null)
            {
                return;
            }

            var genSongs = await Task.Run(() =>
            {
                var songs = cachedSongs.Select(s => new SongDisplayItem
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

                // Fast direct property getter instead of reflection
                return SongDatas.ApplySongSorting(songs, (s, key) => key switch
                {
                    "Song_PlayCount" or "PlayCount" => s.Song_PlayCount,
                    "Song_CreatDate" => s.Song_CreatDate,
                    "Song_WordCount" => s.Song_WordCount,
                    "Song_SongName" or "SongName" => s.SongName,
                    _ => null
                });
            });

            DisplaySongsInGrid(genSongs, $"世代 - {periodName}");
        }

        private void LanguageFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                string lang = btn.Tag.ToString()!;
                bool isMultiSelect = SettingsManager.Instance.CurrentSettings.IsLanguageMultiSelect;

                if (isMultiSelect)
                {
                    if (_selectedLanguages.Contains(lang))
                    {
                        _selectedLanguages.Remove(lang);
                    }
                    else
                    {
                        _selectedLanguages.Add(lang);
                    }
                }
                else
                {
                    // Single select
                    _selectedLanguages.Clear();
                    _selectedLanguages.Add(lang);
                }

                UpdateLanguageFilterButtonHighlights();
                ApplyLanguageCombinedFilter();
            }
        }

        private void UpdateLanguageFilterButtonHighlights()
        {
            // Ensure filter rows are visible when in Language mode
            if (LanguageSecondFilterGrid != null) LanguageSecondFilterGrid.Visibility = System.Windows.Visibility.Visible;
            if (LanguageWordCountFilterGrid != null) LanguageWordCountFilterGrid.Visibility = System.Windows.Visibility.Visible;

            var activeBg = (System.Windows.Media.Brush)(TryFindResource("BrushCyberActiveGradient") ?? TryFindResource("PrimaryHueMidBrush") ?? System.Windows.Media.Brushes.DarkOrange);
            var inactiveBg = (System.Windows.Media.Brush)(TryFindResource("BrushCyberButtonGradient") ?? System.Windows.Media.Brushes.Transparent);
            var activeFg = System.Windows.Media.Brushes.White;
            var inactiveFg = (System.Windows.Media.Brush)TryFindResource("SingerButtonBackground");
            var activeBorder = (System.Windows.Media.Brush)(TryFindResource("BrushAccentCyan") ?? TryFindResource("PrimaryHueMidBrush") ?? activeBg);
            var inactiveBorder = (System.Windows.Media.Brush)TryFindResource("SingerButtonBackground");

            // 1. Update top language buttons
            foreach (var filterBtn in _filterButtons)
            {
                filterBtn.Visibility = System.Windows.Visibility.Visible;
                string btnTag = filterBtn.Tag?.ToString() ?? "";
                bool isSelected = _selectedLanguages.Contains(btnTag);
                
                filterBtn.FontWeight = isSelected ? System.Windows.FontWeights.ExtraBold : System.Windows.FontWeights.Normal;
                filterBtn.Background = isSelected ? activeBg : inactiveBg;
                filterBtn.Foreground = isSelected ? activeFg : inactiveFg;
                filterBtn.BorderBrush = isSelected ? activeBorder : inactiveBorder;
            }

            // 1.5 Update Duet toggle button state and background
            if (LanguageDuetToggle != null)
            {
                LanguageDuetToggle.IsChecked = _isDuetOnly;
            }

            // 2. Update singer type buttons
            var singerTypeButtons = new[] { LanguageMaleBtn, LanguageFemaleBtn, LanguageGroupBtn, LanguageOtherBtn };
            foreach (var btn in singerTypeButtons)
            {
                if (btn == null) continue;
                
                // If "合唱" is checked, keep them visible but disable them to maintain grid layout
                btn.Visibility = System.Windows.Visibility.Visible;
                btn.IsEnabled = !_isDuetOnly;
                
                string btnTag = btn.Tag?.ToString() ?? "";
                bool isSelected = !_isDuetOnly && _selectedSingerType == btnTag;

                btn.FontWeight = isSelected ? System.Windows.FontWeights.ExtraBold : System.Windows.FontWeights.Normal;
                btn.Background = isSelected ? activeBg : inactiveBg;
                btn.Foreground = isSelected ? activeFg : inactiveFg;
                btn.BorderBrush = isSelected ? activeBorder : inactiveBorder;
            }

            // 3. Word count buttons - they are in LanguageWordCountFilterGrid
            if (LanguageWordCountFilterGrid != null)
            {
                foreach (var child in LanguageWordCountFilterGrid.Children)
                {
                    if (child is Button btn)
                    {
                        string btnTag = btn.Tag?.ToString() ?? "";
                        bool isSelected = _selectedWordCountRange == btnTag;

                        btn.FontWeight = isSelected ? System.Windows.FontWeights.ExtraBold : System.Windows.FontWeights.Normal;
                        btn.Background = isSelected ? activeBg : inactiveBg;
                        btn.Foreground = isSelected ? activeFg : inactiveFg;
                        btn.BorderBrush = isSelected ? activeBorder : inactiveBorder;
                    }
                }
            }
        }

        private async void ApplyLanguageCombinedFilter()
        {
            var selectedLangs = new HashSet<string>(_selectedLanguages);
            bool isDuetOnly = _isDuetOnly;
            string selectedSingerType = _selectedSingerType;
            string selectedWcRange = _selectedWordCountRange;

            var filteredSongs = await Task.Run(() =>
            {
                var songs = SongDatas.SongData?
                    .Where(s =>
                    {
                        // 1. Language Filter
                        if (selectedLangs.Any())
                        {
                            var songLang = s.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() ?? "" : "";
                            if (!selectedLangs.Contains(songLang))
                            {
                                // Special case for "其他" (Other) if it's in the selection
                                if (selectedLangs.Contains("其他") || selectedLangs.Contains("其它"))
                                {
                                    // If "其他" is selected, we include anything not in the standard list
                                    var standardLangs = new HashSet<string> { "國語", "台語", "英語", "日語", "粵語", "兒歌" };
                                    if (standardLangs.Contains(songLang ?? "")) return false;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                        }

                        // 2. Duet Toggle (獨唱)
                        var songSingerType = s.TryGetValue("Song_SingerType", out var typeObj) && int.TryParse(typeObj?.ToString(), out int typeVal) ? typeVal : -1;
                        bool isDuet = songSingerType == 3;
                        if (isDuetOnly)
                        {
                            if (!isDuet) return false;
                        }
                        else
                        {
                            if (isDuet) return false;
                        }

                        // 3. Singer Type Filter (男, 女, 團體, 其他)
                        if (!string.IsNullOrEmpty(selectedSingerType))
                        {
                            var sTypeVal = songSingerType;
                            
                            if (sTypeVal != -1)
                            {
                                if (selectedSingerType == "99") // Other
                                {
                                    if (sTypeVal == 0 || sTypeVal == 1 || sTypeVal == 2 || sTypeVal == 3) return false;
                                }
                                else if (int.TryParse(selectedSingerType, out int targetType))
                                {
                                    if (sTypeVal != targetType) return false;
                                }
                            }
                            else if (selectedSingerType != "99")
                            {
                                return false;
                            }
                        }

                        // 4. Word Count Filter
                        if (!string.IsNullOrEmpty(selectedWcRange))
                        {
                            var wcObj = s.TryGetValue("Song_WordCount", out var wco) ? wco : null;
                            int wc = 0;
                            if (wcObj != null && int.TryParse(wcObj.ToString(), out int wcv))
                            {
                                wc = wcv;
                            }

                            var parts = selectedWcRange.Split('-');
                            if (parts.Length == 2)
                            {
                                int min = int.Parse(parts[0]);
                                int max = int.Parse(parts[1]);
                                if (wc < min || wc > max) return false;
                            }
                            else if (int.TryParse(selectedWcRange, out int targetWc))
                            {
                                if (wc != targetWc) return false;
                            }
                        }

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
                        Song_CreatDate = s.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime dt ? dt : (DateTime?)null,
                        Volume = s.TryGetValue("Song_Volume", out var vol) && int.TryParse(vol?.ToString(), out var iv) ? iv : 90,
                        AudioTrack = s.TryGetValue("Song_Track", out var tr) && int.TryParse(tr?.ToString(), out var it) ? it : 0,
                    })
                    .ToList() ?? new List<SongDisplayItem>();

                // Fast direct property getter instead of reflection
                return SongDatas.ApplySongSorting(songs, (s, key) => key switch
                {
                    "Song_PlayCount" or "PlayCount" => s.Song_PlayCount,
                    "Song_CreatDate" => s.Song_CreatDate,
                    "Song_WordCount" => s.Song_WordCount,
                    "Song_SongName" or "SongName" => s.SongName,
                    _ => null
                });
            });

            string title = "語系點歌";
            if (_selectedLanguages.Any()) title += $" - {string.Join(",", _selectedLanguages)}";
            DisplayLanguageSongsInGrid(filteredSongs, title);

            // Ensure grid has focus for keyboard navigation
            if (LanguageSongListGrid != null && LanguageSongListGrid.Visibility == System.Windows.Visibility.Visible)
            {
                LanguageSongListGrid.Focus();
                Keyboard.Focus(LanguageSongListGrid);
            }
        }
    }
}