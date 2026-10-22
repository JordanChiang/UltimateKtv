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
            var options = SongQueryOptions.FromSettings(SettingsManager.Instance.CurrentSettings);

            var newSongs = await Task.Run(() =>
                SongQuery.NewSongs(SongDatas.SongData, lang, options).Select(SongDisplayItem.FromSong).ToList());

            DisplaySongsInGrid(newSongs, Loc.T("Title.NewSongs", Loc.SongLang(lang)));
        }

        private async void RankingFilter_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var filterTag = (sender as Button)?.Tag as string ?? "國語-單曲";

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

            var activeBg = (System.Windows.Media.Brush)(TryFindResource("BrushCyberActiveGradient") ?? FindResource("PrimaryBrush"));
            var inactiveBg = (System.Windows.Media.Brush)(TryFindResource("BrushButtonFlat") ?? System.Windows.Media.Brushes.Transparent);
            var activeFg = (System.Windows.Media.Brush)FindResource("BrushOnAccentActive");
            var inactiveFg = (System.Windows.Media.Brush)TryFindResource("SingerButtonBackground");
            var activeBorder = (System.Windows.Media.Brush)(TryFindResource("BrushAccent") ?? TryFindResource("PrimaryHueMidBrush") ?? activeBg);
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