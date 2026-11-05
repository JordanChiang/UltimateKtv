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
        /// <summary>
        /// 歌星頁上方的一個歌手類別按鈕。Key 是記住頁數用的名稱；ButtonTag 是按鈕的 Tag，也是按鈕文字的語言檔 key。
        /// </summary>
        private sealed record SingerFilter(string Key, SingerCategory Category, string ButtonTag);

        /// <summary>歌星頁上方的歌手類別按鈕，依顯示順序。</summary>
        private static readonly SingerFilter[] SingerFilters =
        {
            new("MaleSinger", SingerCategory.Male, "SingerType.Male"),
            new("FemaleSinger", SingerCategory.Female, "SingerType.Female"),
            new("GroupSinger", SingerCategory.Group, "SingerType.Group"),
            new("ForeignMaleSinger", SingerCategory.ForeignMale, "SingerType.ForeignMale"),
            new("ForeignFemaleSinger", SingerCategory.ForeignFemale, "SingerType.ForeignFemale"),
            new("ForeignGroupSinger", SingerCategory.ForeignGroup, "SingerType.ForeignGroup"),
            new("OtherSinger", SingerCategory.Other, "SingerType.Other"),
        };

        // 新進、排行第一次進入時選的次功能 (按鈕 Tag)
        private const string DefaultNewSongLang = "國語";
        private const string DefaultRankingFilter = "國語-單曲";

        // 歌星、新進、排行上次選的次功能；再進入該頁時自動選它 (只在這次執行期間記住)
        private SingerFilter _lastSingerFilter = SingerFilters[0];
        private string _lastNewSongLang = DefaultNewSongLang;
        private string _lastRankingFilter = DefaultRankingFilter;

        /// <summary>歌星頁上方的歌手類別按鈕 (依按鈕 Tag 找出類別)。</summary>
        private void SingerFilter_Click(object sender, RoutedEventArgs e)
        {
            var tag = (sender as Button)?.Tag as string;
            var filter = Array.Find(SingerFilters, f => f.ButtonTag == tag);
            if (filter == null) return;
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            FilterAndDisplaySingers(filter);
        }

        private async void NewSongFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var lang = (sender as Button)?.Tag as string ?? DefaultNewSongLang;
            _lastNewSongLang = lang;
            HighlightFilterButtons(lang);
            var options = SongQueryOptions.FromSettings(SettingsManager.Instance.CurrentSettings);

            var newSongs = await Task.Run(() =>
                SongQuery.NewSongs(SongDatas.SongData, lang, options).Select(SongDisplayItem.FromSong).ToList());

            DisplaySongsInGrid(newSongs, Loc.T("Title.NewSongs", Loc.SongLang(lang)));
        }

        private async void RankingFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var filterTag = (sender as Button)?.Tag as string ?? DefaultRankingFilter;
            _lastRankingFilter = filterTag;
            HighlightFilterButtons(filterTag);

            var rankedSongs = await Task.Run(() =>
                SongQuery.Ranking(SongDatas.SongData, filterTag).Select(SongDisplayItem.FromSong).ToList());

            DisplaySongsInGrid(rankedSongs, Loc.T("Title.Ranking", Loc.SongFilter(filterTag)));
        }

        private async void GenerationFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var genKey = (sender as Button)?.Tag as string ?? "1";

            // Retrieve the pre-cached name and song list.
            var periodName = SongDatas.GenerationNames?.GetValueOrDefault(genKey, Loc.T("Title.GenerationN", genKey));
            var cachedSongs = SongDatas.GenerationSongs?.GetValueOrDefault(genKey, new List<Song>());

            if (cachedSongs == null || periodName == null)
            {
                return;
            }

            var options = SongQueryOptions.FromSettings(SettingsManager.Instance.CurrentSettings);
            var genSongs = await Task.Run(() =>
                SongQuery.Sort(cachedSongs, options).Select(SongDisplayItem.FromSong).ToList());

            DisplaySongsInGrid(genSongs, Loc.T("Title.Generation", periodName));
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

            // 1. Update top language buttons
            foreach (var filterBtn in _filterButtons)
            {
                filterBtn.Visibility = System.Windows.Visibility.Visible;
                string btnTag = filterBtn.Tag?.ToString() ?? "";
                SetFilterButtonSelected(filterBtn, _selectedLanguages.Contains(btnTag));
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
                SetFilterButtonSelected(btn, !_isDuetOnly && _selectedSingerType == btnTag);
            }

            // 3. Word count buttons - they are in LanguageWordCountFilterGrid
            if (LanguageWordCountFilterGrid != null)
            {
                foreach (var child in LanguageWordCountFilterGrid.Children)
                {
                    if (child is Button btn)
                    {
                        string btnTag = btn.Tag?.ToString() ?? "";
                        SetFilterButtonSelected(btn, _selectedWordCountRange == btnTag);
                    }
                }
            }
        }

        /// <summary>
        /// 歌星、新進、排行頁：上方次功能按鈕中 Tag 等於 selectedTag 的那顆反白，其餘恢復一般樣式。
        /// </summary>
        private void HighlightFilterButtons(string selectedTag)
        {
            foreach (var btn in _filterButtons)
            {
                if (btn.Visibility != System.Windows.Visibility.Visible) continue;
                if ((btn.Tag as string) == selectedTag) SetFilterButtonSelected(btn, true);
                else ResetFilterButtonStyle(btn);
            }
        }

        /// <summary>次功能按鈕回到樣式本身的外觀 (保留滑過/按下效果)。</summary>
        private static void ResetFilterButtonStyle(Button btn)
        {
            btn.FontWeight = FontWeights.Normal;
            btn.ClearValue(Control.BackgroundProperty);
            btn.ClearValue(Control.ForegroundProperty);
            btn.ClearValue(Control.BorderBrushProperty);
        }

        /// <summary>上方次功能按鈕的選中/一般樣式 (語系頁與歌星、新進、排行頁共用)。筆刷每次重新取，主題變更後也正確。</summary>
        private void SetFilterButtonSelected(Button btn, bool isSelected)
        {
            if (isSelected)
            {
                btn.FontWeight = System.Windows.FontWeights.ExtraBold;
                btn.Background = (System.Windows.Media.Brush)(TryFindResource("BrushCyberActiveGradient") ?? FindResource("PrimaryBrush"));
                btn.Foreground = (System.Windows.Media.Brush)FindResource("BrushOnAccentActive");
                btn.BorderBrush = (System.Windows.Media.Brush)(TryFindResource("BrushAccent") ?? TryFindResource("PrimaryHueMidBrush") ?? btn.Background);
            }
            else
            {
                btn.FontWeight = System.Windows.FontWeights.Normal;
                btn.Background = (System.Windows.Media.Brush)(TryFindResource("BrushButtonFlat") ?? System.Windows.Media.Brushes.Transparent);
                btn.Foreground = (System.Windows.Media.Brush)TryFindResource("SingerButtonBackground");
                btn.BorderBrush = (System.Windows.Media.Brush)TryFindResource("SingerButtonBackground");
            }
        }

        private async void ApplyLanguageCombinedFilter()
        {
            var selectedLangs = new HashSet<string>(_selectedLanguages);
            bool isDuetOnly = _isDuetOnly;
            string selectedSingerType = _selectedSingerType;
            string selectedWcRange = _selectedWordCountRange;

            var options = SongQueryOptions.FromSettings(SettingsManager.Instance.CurrentSettings);

            var filteredSongs = await Task.Run(() =>
                SongQuery.ByLanguage(SongDatas.SongData, selectedLangs, isDuetOnly, selectedSingerType, selectedWcRange, options)
                    .Select(SongDisplayItem.FromSong).ToList());

            string title = Loc.T("Title.LanguageSongs");
            if (_selectedLanguages.Any()) title += $" - {string.Join(",", _selectedLanguages.Select(Loc.SongLang))}";
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