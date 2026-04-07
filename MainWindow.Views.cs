using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        // Toggle visibility of quick input panels
        private void ShowQuickInputPanels(bool show)
        {
            var vis = show ? Visibility.Visible : Visibility.Collapsed;
            if (SearchStyleBtnPanel != null) SearchStyleBtnPanel.Visibility = vis;
            
            if (show)
            {
                // Youtube mode collapses the char grid and results list, but shows the thumbnail grid
                if (SearchInputGrid != null) 
                    SearchInputGrid.Visibility = (_searchMode == SearchMode.Youtube || _currentQuickMethod == QuickMethod.Keyboard) ? Visibility.Collapsed : Visibility.Visible;
                
                if (SearchSymbolPanel != null) SearchSymbolPanel.Visibility = Visibility.Visible;
                
                if (YoutubeThumbnailGrid != null)
                    YoutubeThumbnailGrid.Visibility = (_searchMode == SearchMode.Youtube) ? Visibility.Visible : Visibility.Collapsed;

                if (QuickResultsContainer != null)
                    QuickResultsContainer.Visibility = Visibility.Visible;

                // Important: Collapse the main singer/song grid area in YouTube mode to give space for Row 4
                if (SingerSongContentGrid != null)
                    SingerSongContentGrid.Visibility = (_searchMode == SearchMode.Youtube) ? Visibility.Collapsed : Visibility.Visible;
            }
            else
            {
                if (SearchInputGrid != null) SearchInputGrid.Visibility = Visibility.Collapsed;
                if (SearchSymbolPanel != null) SearchSymbolPanel.Visibility = Visibility.Collapsed;
                if (YoutubeThumbnailGrid != null) YoutubeThumbnailGrid.Visibility = Visibility.Collapsed;
                if (QuickResultsContainer != null) QuickResultsContainer.Visibility = Visibility.Collapsed;
                if (QuickSongListGrid != null) QuickSongListGrid.Visibility = Visibility.Collapsed;
                if (QuickSongCountText != null) QuickSongCountText.Visibility = Visibility.Collapsed;
                
                // Restore the main singer/song grid area visibility when leaving quick search
                if (SingerSongContentGrid != null)
                    SingerSongContentGrid.Visibility = Visibility.Visible;
            }
        }

        // Toggle visibility of singer mode panels (SingerGrid/SongListGrid)
        private void ShowSingerPanels(bool show)
        {
            var vis = show ? Visibility.Visible : Visibility.Collapsed;
            if (SingerGrid != null) SingerGrid.Visibility = vis;
            if (VisualSingerGrid != null) VisualSingerGrid.Visibility = vis;
            if (SongListGrid != null) SongListGrid.Visibility = vis;

            if (show) 
            {
                ShowSingerGrid(); // Ensure correct internal visibility
                if (YoutubeThumbnailGrid != null) YoutubeThumbnailGrid.Visibility = Visibility.Collapsed;
                if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Shows the singer grid and hides the song list
        /// </summary>
        private void ShowSingerGrid()
        {
            if (IsVisualSingerStyleEffective)
            {
                if (VisualSingerGrid != null) VisualSingerGrid.Visibility = Visibility.Visible;
                if (SingerGrid != null) SingerGrid.Visibility = Visibility.Collapsed;
            }
            else
            {
                if (VisualSingerGrid != null) VisualSingerGrid.Visibility = Visibility.Collapsed;
                if (SingerGrid != null) SingerGrid.Visibility = Visibility.Visible;
            }
            if (SongListGrid != null) SongListGrid.Visibility = Visibility.Collapsed;
        }

        #region Main View Navigation Button Handlers

        private void FuncBtnBySinger_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Navigate to Singer view");
            SingerPhotoManager.CurrentSubFolder = "SingerAvatar";
            ShowSingerPanels(true);
            ShowQuickInputPanels(false);
            SetupFilterButtons(MainFilterMode.Singer);
            // Trigger the first filter button by default
            if (_filterButtons.Any() && _filterButtons[0].IsVisible) MaleSingerFilter_Click(null!, null!);
        }
        private void FuncBtnQuit_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Navigate to Quit");
            var optionsWindow = new UserOptionsWindow(this);
            optionsWindow.FuncBtnQuit_Click(sender, e);
        }
        private void FuncBtnByInput_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Navigate to Quick Input view");
            // Enter quick input search mode
            ShowQuickInputPanels(true);
            ShowSingerPanels(false);

            // On first entry, initialize to the default state.
            // On subsequent entries, the previous state is already preserved.
            if (!_isQuickSearchInitialized)
            {
                SetQuickMethod(QuickMethod.Bopomofo, BopomofoListBtn!);
            }

            // Refresh the UI state immediately (grid visibilities etc)
            UpdateSearchWords(false);
        }
        private void FuncBtnByNum_Click(object sender, RoutedEventArgs e)
        {
            // Set mode and radio state BEFORE showing panels to ensure ShowQuickInputPanels logic is correct
            if (SongRadio != null) SongRadio.IsChecked = true;
            _searchMode = SearchMode.Song;

            AppLogger.Log("User action: Navigate to Song ID view");
            // Show the quick input panels and hide the singer grid.
            ShowQuickInputPanels(true);
            ShowSingerPanels(false);

            // Set the quick search method specifically to SongId.
            SetQuickMethod(QuickMethod.SongId, SongIdListBtn!);
        }

        private void FuncBtnByFav_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Navigate to Favorites view");
            SingerPhotoManager.CurrentSubFolder = "FavoriteUser";
            ShowSingerPanels(false);
            ShowQuickInputPanels(false);
            LoadFavoriteUsers();
        }

        private void FuncBtnByNew_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Navigate to New Songs view");
            ShowSingerPanels(false);
            ShowQuickInputPanels(false);
            SetupFilterButtons(MainFilterMode.NewSong);
            // Trigger the first filter button by default
            if (_filterButtons.Any() && _filterButtons[0].IsVisible) NewSongFilter_Click(sender, new RoutedEventArgs());
        }

        private void FuncBtnByRank_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Navigate to Ranking view");
            ShowSingerPanels(false);
            ShowQuickInputPanels(false);
            SetupFilterButtons(MainFilterMode.Ranking);
            // Trigger the first filter button by default
            if (_filterButtons.Any() && _filterButtons[0].IsVisible) RankingFilter_Click(sender, new RoutedEventArgs());
        }

        #endregion

        /// <summary>
        /// Configures the top 7 filter buttons based on the selected mode.
        /// </summary>
        private void SetupFilterButtons(MainFilterMode mode)
        {
            // Clear existing handlers and hide all buttons first
            foreach (var btn in _filterButtons)
            {
                // A simple way to remove previous anonymous handlers is to nullify the event.
                // This is safe as long as we are the only ones adding/removing handlers this way.
                btn.Click -= MaleSingerFilter_Click;
                btn.Click -= FemaleSingerFilter_Click;
                btn.Click -= GroupSingerFilter_Click;
                btn.Click -= ForeignMaleSingerFilter_Click;
                btn.Click -= ForeignFemaleSingerFilter_Click;
                btn.Click -= ForeignGroupSingerFilter_Click;
                btn.Click -= OtherSingerFilter_Click;
                btn.Click -= NewSongFilter_Click;
                btn.Click -= RankingFilter_Click;
                btn.Click -= GenerationFilter_Click;
                btn.Visibility = Visibility.Collapsed;
            }

            switch (mode)
            {
                case MainFilterMode.Singer:
                    var singerFilters = new[] { "男歌手", "女歌手", "團體", "外國男", "外國女", "外國團", "其他" };
                    var singerActions = new Action<object, RoutedEventArgs>[] { MaleSingerFilter_Click, FemaleSingerFilter_Click, GroupSingerFilter_Click, ForeignMaleSingerFilter_Click, ForeignFemaleSingerFilter_Click, ForeignGroupSingerFilter_Click, OtherSingerFilter_Click };
                    for (int i = 0; i < singerFilters.Length; i++)
                    {
                        _filterButtons[i].Content = singerFilters[i];
                        _filterButtons[i].Click += new RoutedEventHandler(singerActions[i]);
                        _filterButtons[i].Visibility = Visibility.Visible;
                    }
                    break;

                case MainFilterMode.NewSong:
                    var newSongFilters = new[] { "國語", "台語", "其它" };
                    for (int i = 0; i < newSongFilters.Length; i++)
                    {
                        _filterButtons[i].Content = newSongFilters[i];
                        _filterButtons[i].Tag = newSongFilters[i];
                        _filterButtons[i].Click += NewSongFilter_Click;
                        _filterButtons[i].Visibility = Visibility.Visible;
                    }
                    break;

                case MainFilterMode.Ranking:
                    SetupRankingButtons();
                    break;
            }
        }

        /// <summary>
        /// Sets up the filter buttons for the Ranking view.
        /// </summary>
        private void SetupRankingButtons()
        {
            var rankingFilters = new[] { "國語-單曲", "台語-單曲", "國語-合唱", "台語-合唱", "其它" };
            int buttonIndex = 0;

            // Add Ranking filters
            for (int i = 0; i < rankingFilters.Length; i++)
            {
                if (buttonIndex >= _filterButtons.Count) break;
                _filterButtons[buttonIndex].Content = rankingFilters[i];
                _filterButtons[buttonIndex].Tag = rankingFilters[i];
                _filterButtons[buttonIndex].Click += RankingFilter_Click;
                _filterButtons[buttonIndex].Visibility = Visibility.Visible;
                buttonIndex++;
            }

        }

    }
}