using MaterialDesignThemes.Wpf;
using System;
using UltimateKtv.Playback;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        private int _currentWaitingListPage = 1;
        private int _totalWaitingListPages = 1;

        /// <summary>
        /// Gets information about the currently playing song for HTTP/Web services.
        /// </summary>
        public (bool IsPlaying, string SongName, string SingerName, string SongId, string OrderedBy) GetCurrentPlayingSongInfo()
        {
            var current = _playback.Current;
            if (current == null)
            {
                return (false, string.Empty, string.Empty, string.Empty, string.Empty);
            }

            return (true, current.SongName, current.SingerName, current.SongId, current.OrderedBy);
        }

        public int GetWaitingListCount()
        {
            return _playback.Snapshot.Count;
        }

        public List<WaitingListItem> GetWaitingListItems()
        {
            return _playback.Snapshot.Select(ToWaitingListItem).ToList();
        }

        private static WaitingListItem ToWaitingListItem(QueuedSong song) => new WaitingListItem
        {
            Id = song.Id,
            SongId = song.SongId,
            WaitingListSongName = song.SongName,
            WaitingListSingerName = song.SingerName,
            FilePath = song.FilePath,
            Volume = song.Volume,
            AudioTrack = song.AudioTrack,
            OrderedBy = song.OrderedBy,
            IsYoutube = song.IsYoutube
        };

        /// <summary>
        /// Initializes the waiting list with empty rows
        /// </summary>
        private void InitializeWaitingList()
        {
            _currentWaitingListPage = 1;
            UpdateWaitingListDisplay();
            UpdateCurrentPlayingDisplay();
        }

        /// <summary>
        /// Updates the currently playing song/singer display bar above the media player panel.
        /// </summary>
        public void UpdateCurrentPlayingDisplay(string songName = "", string singerName = "")
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => UpdateCurrentPlayingDisplay(songName, singerName));
                return;
            }

            if (NowPlayingBorder == null || NowPlayingBadgeBorder == null || NowPlayingSongText == null)
            {
                return;
            }

            if (string.IsNullOrEmpty(songName))
            {
                songName = _playback.Current?.SongName ?? string.Empty;
                singerName = _playback.Current?.SingerName ?? string.Empty;
            }

            bool isPlaying = !string.IsNullOrEmpty(songName);

            if (isPlaying)
            {
                bool isPaused = false;
                if (IsSongPlaybackActive)
                {
                    if (ktvPlayer != null && ktvPlayer.IsPlaying)
                    {
                        if (_pauseBtnShowsResume)
                        {
                            SetPauseButtonResume(false);
                        }
                        isPaused = false;
                    }
                    else if (_pauseBtnShowsResume)
                    {
                        isPaused = true;
                    }
                }

                if (isPaused)
                {
                    NowPlayingIcon.Kind = PackIconKind.PauseCircle;
                    NowPlayingIcon.SetResourceReference(PackIcon.ForegroundProperty, "BrushWarning");
                    NowPlayingBadgeBorder.SetResourceReference(Border.BackgroundProperty, "BrushBgSecondary");
                    NowPlayingBadgeBorder.SetResourceReference(Border.BorderBrushProperty, "BrushWarning");
                    NowPlayingBadgeBorder.SetResourceReference(FrameworkElement.ToolTipProperty, Loc.ResourcePrefix + "NowPlaying.Paused");
                    PauseNowPlayingBreathing();
                }
                else if (_playback.Current?.IsRandom == true)
                {
                    NowPlayingIcon.Kind = PackIconKind.ShuffleVariant;
                    NowPlayingIcon.SetResourceReference(PackIcon.ForegroundProperty, "PrimaryLightBrush");
                    NowPlayingBadgeBorder.SetResourceReference(Border.BackgroundProperty, "BrushCyberButtonGradient");
                    NowPlayingBadgeBorder.SetResourceReference(Border.BorderBrushProperty, "PrimaryBrush");
                    NowPlayingBadgeBorder.SetResourceReference(FrameworkElement.ToolTipProperty, Loc.ResourcePrefix + "NowPlaying.Random");
                    StartNowPlayingBreathing();
                }
                else
                {
                    NowPlayingIcon.Kind = PackIconKind.PlayCircle;
                    NowPlayingIcon.SetResourceReference(PackIcon.ForegroundProperty, "PrimaryLightBrush");
                    NowPlayingBadgeBorder.SetResourceReference(Border.BackgroundProperty, "BrushCyberButtonGradient");
                    NowPlayingBadgeBorder.SetResourceReference(Border.BorderBrushProperty, "PrimaryBrush");
                    NowPlayingBadgeBorder.SetResourceReference(FrameworkElement.ToolTipProperty, Loc.ResourcePrefix + "NowPlaying.Playing");
                    StartNowPlayingBreathing();
                }

                NowPlayingSongText.Text = songName;
                NowPlayingSongText.FontWeight = FontWeights.Bold;
                NowPlayingSongText.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextMain");

                if (!string.IsNullOrEmpty(singerName))
                {
                    NowPlayingSeparator.Visibility = Visibility.Visible;
                    NowPlayingSingerText.Text = singerName;
                    NowPlayingSingerText.FontWeight = FontWeights.SemiBold;
                    NowPlayingSingerText.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryLightBrush");
                    NowPlayingBorder.ToolTip = $"{songName} / {singerName}";
                }
                else
                {
                    NowPlayingSeparator.Visibility = Visibility.Collapsed;
                    NowPlayingSingerText.Text = string.Empty;
                    NowPlayingBorder.ToolTip = songName;
                }
            }
            else
            {
                NowPlayingIcon.Kind = PackIconKind.MusicNote;
                NowPlayingBadgeBorder.SetResourceReference(Border.BackgroundProperty, "BrushBgSecondary");
                NowPlayingBadgeBorder.SetResourceReference(Border.BorderBrushProperty, "BrushBorderDefault");
                NowPlayingIcon.SetResourceReference(PackIcon.ForegroundProperty, "BrushTextDim");
                NowPlayingBadgeBorder.SetResourceReference(FrameworkElement.ToolTipProperty, Loc.ResourcePrefix + "NowPlaying.Idle");

                NowPlayingSongText.SetResourceReference(TextBlock.TextProperty, Loc.ResourcePrefix + "NowPlaying.None");
                NowPlayingSongText.FontWeight = FontWeights.Normal;
                NowPlayingSongText.SetResourceReference(TextBlock.ForegroundProperty, "BrushTextMuted");

                NowPlayingSeparator.Visibility = Visibility.Collapsed;
                NowPlayingSingerText.Text = string.Empty;
                NowPlayingBorder.ToolTip = null;
                StopNowPlayingBreathing();
            }
        }

        private Storyboard? _nowPlayingBreathingStoryboard;
        // Stop/Pause on a storyboard that was never begun logs a WPF Animation warning
        private bool _isNowPlayingBreathing;

        /// <summary>
        /// Initializes the smooth breathing animation on NowPlayingInfoPanel.
        /// </summary>
        private void InitializeNowPlayingBreathingAnimation()
        {
            if (NowPlayingInfoPanel == null) return;

            var anim = new DoubleAnimation
            {
                From = 1.3,
                To = 0.18,
                Duration = new Duration(TimeSpan.FromSeconds(1.5)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Storyboard.SetTarget(anim, NowPlayingInfoPanel);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));

            _nowPlayingBreathingStoryboard = new Storyboard();
            _nowPlayingBreathingStoryboard.Children.Add(anim);
        }

        /// <summary>
        /// Starts breathing animation for the currently playing song info.
        /// </summary>
        private void StartNowPlayingBreathing()
        {
            if (NowPlayingInfoPanel == null) return;

            if (_nowPlayingBreathingStoryboard == null)
            {
                InitializeNowPlayingBreathingAnimation();
            }

            try
            {
                if (_nowPlayingBreathingStoryboard != null)
                {
                    _nowPlayingBreathingStoryboard.Begin(this, isControllable: true);
                    _isNowPlayingBreathing = true;
                }
            }
            catch { }
        }

        /// <summary>
        /// Stops breathing animation and restores full opacity.
        /// </summary>
        private void StopNowPlayingBreathing()
        {
            try
            {
                if (_isNowPlayingBreathing)
                {
                    _nowPlayingBreathingStoryboard?.Stop(this);
                    _isNowPlayingBreathing = false;
                }
            }
            catch { }

            if (NowPlayingInfoPanel != null)
            {
                NowPlayingInfoPanel.Opacity = 1.0;
            }
        }

        /// <summary>
        /// Pauses breathing animation when playback is paused.
        /// </summary>
        private void PauseNowPlayingBreathing()
        {
            try
            {
                if (_isNowPlayingBreathing)
                {
                    _nowPlayingBreathingStoryboard?.Pause(this);
                }
            }
            catch { }
        }

        /// <summary>
        /// Updates the WaitingListGrid display (自動播放由 PlaybackSession 決定)
        /// </summary>
        private void UpdateWaitingListDisplay()
        {
            // Calculate pagination
            var nonEmptySongs = _playback.Snapshot.Select(ToWaitingListItem).ToList();
            _totalWaitingListPages = Math.Max(1, (int)Math.Ceiling((double)nonEmptySongs.Count / WaitingListPageSize));

            // Ensure current page is valid
            if (_currentWaitingListPage > _totalWaitingListPages)
            {
                _currentWaitingListPage = _totalWaitingListPages;
            }

            // Get songs for current page
            var songsForPage = nonEmptySongs
                .Skip((_currentWaitingListPage - 1) * WaitingListPageSize)
                .Take(WaitingListPageSize)
                .ToList();

            // Fill remaining slots with empty items to always show 6 rows
            var displayList = new List<WaitingListItem>(songsForPage);
            while (displayList.Count < WaitingListPageSize)
            {
                displayList.Add(new WaitingListItem());
            }

            // Update the grid in place: 沿用既有的列，只更新內容 (不重建 DataGridRow)。
            // 取出列的項目往後使用時要 Clone()，因為列的內容會在下次更新時被改掉。
            ShowPageItems(WaitingListGrid, _waitingListPageItems, displayList);

            // Update pagination info (2 lines)
            WaitListPageInfo.Text = Loc.T("Wait.PageInfo", nonEmptySongs.Count, _currentWaitingListPage, _totalWaitingListPages);

            // Enable/disable pagination buttons
            WaitListPageUp.IsEnabled = _currentWaitingListPage > 1;
            WaitListPageDown.IsEnabled = _currentWaitingListPage < _totalWaitingListPages;

            // Notify web clients that waiting list has changed in background
            Task.Run(() => HttpServer.BroadcastEvent("playlistChanged", new { count = nonEmptySongs.Count }));

            // Pre-loading logic: cache the first song file content in background if enabled
            if (SettingsManager.Instance.CurrentSettings.EnablePreLoading && nonEmptySongs.Any())
            {
                var firstSong = nonEmptySongs.First();
                if (firstSong.FilePath != _preLoadedSongPath)
                {
                    string targetFilePath = firstSong.FilePath;
                    string songName = firstSong.WaitingListSongName;

                    // Offload heavy I/O and GC to background thread to never block UI thread / Marquee
                    Task.Run(() =>
                    {
                        try
                        {
                            // Clear old cache before loading new one to free memory
                            if (_preLoadedFileContent != null)
                            {
                                _preLoadedFileContent = null;
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }

                            if (!string.IsNullOrEmpty(targetFilePath) && File.Exists(targetFilePath))
                            {
                                var fileInfo = new FileInfo(targetFilePath);
                                long fileSize = fileInfo.Length;

                                try
                                {
                                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                                    byte[] content = File.ReadAllBytes(targetFilePath);
                                    stopwatch.Stop();

                                    _preLoadedFileContent = content;
                                    _preLoadedSongPath = targetFilePath;
                                    _preLoadedMemoryUsage = content.Length;
                                    _preLoadingCacheCount++;
                                    AppLogger.Log($"Pre-loading: Cached '{songName}' ({FormatBytes(_preLoadedMemoryUsage)}) in {stopwatch.ElapsedMilliseconds}ms [#{_preLoadingCacheCount}]");
                                }
                                catch (OutOfMemoryException)
                                {
                                    AppLogger.LogError($"Pre-loading: OutOfMemory - File '{Path.GetFileName(targetFilePath)}' size {FormatBytes(fileSize)} exceeds available memory", null);
                                    _preLoadedSongPath = null;
                                    _preLoadedFileContent = null;
                                    _preLoadedMemoryUsage = 0;
                                }
                            }
                            else
                            {
                                _preLoadedSongPath = null;
                                _preLoadedFileContent = null;
                                _preLoadedMemoryUsage = 0;
                            }
                        }
                        catch (Exception ex)
                        {
                            AppLogger.LogError($"Pre-loading: Error caching '{songName}'", ex);
                            _preLoadedSongPath = null;
                            _preLoadedFileContent = null;
                            _preLoadedMemoryUsage = 0;
                        }
                    });
                }
            }
            else if (!nonEmptySongs.Any() && _preLoadedFileContent != null)
            {
                // Clear preloaded content in background if waiting list is empty
                Task.Run(() =>
                {
                    _preLoadedSongPath = null;
                    _preLoadedFileContent = null;
                    _preLoadedMemoryUsage = 0;
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    AppLogger.Log("Pre-loading: Cleared cache (waiting list empty)");
                });
            }
        }

        // Ensure row selection and open context menu at mouse position on right-click
        private void WaitingListGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.OriginalSource is not DependencyObject dep) return;
                var row = FindVisualParent<DataGridRow>(dep);
                if (row != null)
                {
                    var item = row.Item as WaitingListItem;
                    if (item != null && !string.IsNullOrEmpty(item.WaitingListSongName))
                    {
                        // Store the item for menu actions and highlight the selected row
                        _waitingListContextItem = item.Clone(); // 固定列在換頁/更新時會被改掉，先複製
                        if (row != null) row.IsSelected = true;
                        if (WaitingListGrid != null) WaitingListGrid.SelectedItem = item;
                        UpdateContextMenuWithUserInfo(item);
                        var cm = WaitingListGrid?.ContextMenu;
                        if (cm != null)
                        {
                            cm.Placement = PlacementMode.MousePoint;
                            cm.IsOpen = true;
                            e.Handled = true;
                        }
                    }
                }
            }
            catch { }
        }

        // Open context menu on left-click as well
        private void WaitingListGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (e.OriginalSource is not DependencyObject dep) return;
                var row = FindVisualParent<DataGridRow>(dep);
                if (row != null)
                {
                    var item = row.Item as WaitingListItem;
                    if (item != null && !string.IsNullOrEmpty(item.WaitingListSongName))
                    {
                        _waitingListContextItem = item.Clone(); // 固定列在換頁/更新時會被改掉，先複製
                        if (row != null) row.IsSelected = true;
                        if (WaitingListGrid != null) WaitingListGrid.SelectedItem = item;
                        UpdateContextMenuWithUserInfo(item);
                        var cm = WaitingListGrid?.ContextMenu;
                        if (cm != null)
                        {
                            cm.Placement = PlacementMode.MousePoint;
                            cm.IsOpen = true;
                            e.Handled = true;
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Updates the context menu to include username information if available
        /// </summary>
        private void UpdateContextMenuWithUserInfo(WaitingListItem item)
        {
            try
            {
                var cm = WaitingListGrid?.ContextMenu;
                if (cm == null) return;

                // Remove any existing username footer
                var existingFooter = cm.Items.OfType<MenuItem>().FirstOrDefault(m => m.Tag?.ToString() == "UserInfoFooter");
                if (existingFooter != null)
                {
                    cm.Items.Remove(existingFooter);
                }

                // Remove any existing separator
                var existingSeparator = cm.Items.OfType<Separator>().FirstOrDefault(s => s.Tag?.ToString() == "UserInfoSeparator");
                if (existingSeparator != null)
                {
                    cm.Items.Remove(existingSeparator);
                }

                // Add username footer at bottom if OrderedBy is not empty
                if (!string.IsNullOrEmpty(item.OrderedBy))
                {
                    var separator = new Separator { Tag = "UserInfoSeparator" };
                    cm.Items.Add(separator);

                    // Create a StackPanel with colored icon and username
                    var headerPanel = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0)
                    };

                    // Add green web icon
                    var webIcon = new PackIcon
                    {
                        Kind = PackIconKind.Web,
                        Width = 16,
                        Height = 16,
                        Foreground = (System.Windows.Media.Brush)FindResource("BrushSuccess"),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    headerPanel.Children.Add(webIcon);

                    // Add username text
                    var usernameText = new TextBlock
                    {
                        Text = item.OrderedBy,
                        FontWeight = FontWeights.Bold,
                        FontSize = (double)FindResource("FontSizeXS"),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    headerPanel.Children.Add(usernameText);

                    var userInfoFooter = new MenuItem
                    {
                        Header = headerPanel,
                        IsEnabled = false,
                        Tag = "UserInfoFooter"
                    };
                    cm.Items.Add(userInfoFooter);
                }
            }
            catch { }
        }

        // Clear any accidental selection to keep grid non-selectable for this panel
        private void WaitingListGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (WaitingListGrid != null && WaitingListGrid.SelectedIndex != -1)
                {
                    WaitingListGrid.SelectedIndex = -1;
                }
            }
            catch { }
        }

        /// <summary>
        /// Context menu action: Move selected waiting item to first (UI click handler)
        /// </summary>
        private void WaitingList_MoveToFirst_Click(object sender, RoutedEventArgs e)
        {
            var selected = _waitingListContextItem ?? (WaitingListGrid?.SelectedItem as WaitingListItem);
            if (selected == null || string.IsNullOrEmpty(selected.WaitingListSongName)) return;
            MoveSongToFirst(selected.Id, OrderSource.User);
        }

        /// <summary>
        /// Context menu action: Remove selected waiting item from list (UI click handler)
        /// </summary>
        private void WaitingList_RemoveFromList_Click(object sender, RoutedEventArgs e)
        {
            var selected = _waitingListContextItem ?? (WaitingListGrid?.SelectedItem as WaitingListItem);
            if (selected == null || string.IsNullOrEmpty(selected.WaitingListSongName)) return;
            RemoveSongFromList(selected.Id, OrderSource.User);
        }

        /// <summary>
        /// Context menu action: Move selected waiting item down by one position (UI click handler)
        /// </summary>
        private void WaitingList_MoveToNext_Click(object sender, RoutedEventArgs e)
        {
            var selected = _waitingListContextItem ?? (WaitingListGrid?.SelectedItem as WaitingListItem);
            if (selected == null || string.IsNullOrEmpty(selected.WaitingListSongName)) return;
            MoveSongToNext(selected.Id, OrderSource.User);
        }

        /// <summary>
        /// Refreshes the song playback marquee on player screen with the latest next song info.
        /// Useful when songs are added, removed, or reordered in the waiting list during playback.
        /// </summary>
        private void RefreshCurrentPlaybackMarquee(bool onlyIfActiveOrPending = false)
        {
            var current = _playback.Current;
            if (current != null && _videoDisplayWindow != null)
            {
                if (onlyIfActiveOrPending && !MarqueeAPI.IsSongPlaybackMarqueeActiveOrPending(Enums.MarqueeDisplayDevice.PlayerScreen))
                {
                    return;
                }

                ShowSongPlaybackMarquee(current);
            }
        }

        /// <summary>電視上的播放中跑馬燈：目前歌曲 + 待播清單下一首。</summary>
        private void ShowSongPlaybackMarquee(QueuedSong current)
        {
            var next = _playback.Snapshot.FirstOrDefault();
            MarqueeAPI.ShowSongPlaybackMarquee(
                string.IsNullOrEmpty(current.SongName) ? Loc.T("Song.Unknown") : current.SongName,
                current.SingerName,
                next?.SongName,
                next?.SingerName,
                current.IsRandom,
                current.OrderedBy,
                next?.OrderedBy,
                Enums.MarqueeDisplayDevice.PlayerScreen
            );
        }

        /// <summary>
        /// 歌曲列表列內的「插播」按鈕：加入已點歌單並排到第一首
        /// </summary>
        private void SongInsertButton_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not SongDisplayItem song || song.IsYoutube) return;
            AppLogger.Log($"User action: Insert button clicked for song '{song.SongName}'");
            // Clone: 列表使用固定列，換頁時列的內容會被改掉
            _ = OrderSongAsync(song.Clone(), insertFirst: true);
            ClearSongGridsSelection();
        }

        /// <summary>
        /// 點歌：加入待播清單 (重複檢查、隨機歌切斷由 PlaybackSession 處理)，並依結果顯示提示/跑馬燈。
        /// </summary>
        /// <param name="song">The song to add</param>
        /// <param name="insertFirst">插播：放到已點歌單第一首</param>
        internal async Task<EnqueueResult> OrderSongAsync(SongDisplayItem song, OrderSource source = OrderSource.User, bool insertFirst = false)
        {
            // PlaybackSession 只能在 UI 執行緒使用；呼叫端多為 fire-and-forget，在這裡轉回 UI 執行緒以免點歌被吞掉
            if (!Dispatcher.CheckAccess())
            {
                AppLogger.Log($"OrderSongAsync called off the UI thread ({source}): '{song.SongName}'");
                return await Dispatcher.InvokeAsync(() => OrderSongAsync(song, source, insertFirst)).Task.Unwrap();
            }

            var queued = new QueuedSong
            {
                SongId = song.SongId,
                SongName = song.SongName,
                // YouTube 的 SingerName 存的是頻道名/歌曲時間，不是歌手
                SingerName = song.IsYoutube ? string.Empty : song.SingerName,
                FilePath = song.FilePath,
                Volume = song.Volume,
                AudioTrack = song.AudioTrack,
                OrderedBy = song.OrderedBy,
                IsYoutube = song.IsYoutube
            };

            // 點歌時若正在播隨機歌，PlaybackSession 會切掉它，此時不顯示「已點歌」跑馬燈
            bool cutsOffRandom = source != OrderSource.Random && _playback.Current?.IsRandom == true;
            // 「已點歌」通知會把播放中跑馬燈移到 pending，先記下原本是否在播
            bool wasPlaybackActive = MarqueeAPI.IsSongPlaybackMarqueeActiveOrPending(Enums.MarqueeDisplayDevice.PlayerScreen);

            var result = await _playback.EnqueueAsync(queued, source, insertFirst);

            switch (result)
            {
                case EnqueueResult.Duplicate:
                    Debug.WriteLine($"Song already exists in waiting list: {song.SongName} by {song.SingerName}");
                    if (source is OrderSource.User or OrderSource.YouTube)
                    {
                        ShowDuplicateSongMessage(song.SongName, song.SingerName);
                    }
                    return result;

                case EnqueueResult.MovedToFirst:
                    RefreshCurrentPlaybackMarquee(onlyIfActiveOrPending: true);
                    return result;

                case EnqueueResult.Added when source != OrderSource.Random:
                    break;

                default:
                    return result;
            }

            // Dismiss web host info marquee on first song order (single-monitor mode)
            if (_isWebHostMarqueeDisplayed)
            {
                MarqueeAPI.Stop(Enums.MarqueeDisplayDevice.PlayerScreen);
                _isWebHostMarqueeDisplayed = false;
                AppLogger.Log("Web host info marquee dismissed on first song order");
            }

            // Show marquee on player screen using customizable settings ONLY if not cutting off a random song
            if (!cutsOffRandom)
            {
                MarqueeAPI.ShowSongAddedNotification(
                    song.SongName,
                    song.IsYoutube ? string.Empty : song.SingerName, // YT 的 SingerName 存的是頻道名/歌曲時間
                    song.OrderedBy,
                    Enums.MarqueeDisplayDevice.PlayerScreen
                );

                // If playback marquee was active on screen (or pending), update it to reflect the new next song (MarqueeTextString1)
                if (wasPlaybackActive)
                {
                    RefreshCurrentPlaybackMarquee(onlyIfActiveOrPending: true);
                }
            }

            return result;
        }

        /// <summary>
        /// Shows a message when trying to add a duplicate song
        /// </summary>
        private async void ShowDuplicateSongMessage(string songName, string singerName)
        {
            var dialogContent = new StackPanel
            {
                Margin = new Thickness(16)
            };

            // Add icon
            var icon = new PackIcon
            {
                Kind = PackIconKind.AlertCircle,
                Width = 48,
                Height = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("BrushWarning"),
                Margin = new Thickness(0, 0, 0, 16)
            };
            dialogContent.Children.Add(icon);

            // Add title
            var title = new TextBlock
            {
                Text = Loc.T("Wait.DuplicateTitle"),
                FontSize = (double)FindResource("FontSizeS"),
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            dialogContent.Children.Add(title);

            // Add message
            var message = new TextBlock
            {
                Text = Loc.T("Wait.DuplicateMessage", songName, singerName),
                FontSize = (double)FindResource("FontSizeXS"),
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 24)
            };
            dialogContent.Children.Add(message);

            // Add OK button
            var okButton = new Button
            {
                Content = Loc.T("Common.OK"),
                Width = 140,
                Height = 48,
                FontSize = (double)FindResource("FontSizeS"),
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Command = DialogHost.CloseDialogCommand,
                CommandParameter = true
            };
            ThemeSettingsHandler.ApplyOutlinedButtonStyle(okButton, 20);
            dialogContent.Children.Add(okButton);

            // Show dialog
            await ShowDialogSafeAsync(dialogContent, "RootDialog");
        }

        private void WaitListPageUp_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWaitingListPage > 1)
            {
                AppLogger.Log($"User action: Waiting list page up to {_currentWaitingListPage - 1}");
                _currentWaitingListPage--;
                UpdateWaitingListDisplay();
            }
        }

        private void WaitListPageDown_Click(object sender, RoutedEventArgs e)
        {
            if (_currentWaitingListPage < _totalWaitingListPages)
            {
                AppLogger.Log($"User action: Waiting list page down to {_currentWaitingListPage + 1}");
                _currentWaitingListPage++;
                UpdateWaitingListDisplay();
            }
        }

        /// <summary>
        /// Moves a song to the first position in the waiting list.
        /// Called by both UI context menu and remote web API.
        /// </summary>
        public void MoveSongToFirst(Guid id, OrderSource source = OrderSource.Remote)
        {
            if (_playback.MoveToFirst(id, source))
            {
                RefreshCurrentPlaybackMarquee(onlyIfActiveOrPending: true);
            }
        }

        public void RemoveSongFromList(Guid id, OrderSource source = OrderSource.Remote)
        {
            if (_playback.Remove(id, source))
            {
                RefreshCurrentPlaybackMarquee(onlyIfActiveOrPending: true);
            }
        }

        /// <summary>與下一首交換位置。</summary>
        public void MoveSongToNext(Guid id, OrderSource source = OrderSource.Remote)
        {
            if (_playback.MoveDown(id, source))
            {
                RefreshCurrentPlaybackMarquee(onlyIfActiveOrPending: true);
            }
        }

        /// <summary>
        /// Clears the pre-loaded cache and logs the action
        /// </summary>
        private void ClearPreLoadingCache()
        {
            if (_preLoadedSongPath != null || _preLoadedFileContent != null)
            {
                AppLogger.Log($"Pre-loading: Cache cleared ({FormatBytes(_preLoadedMemoryUsage)})");
                _preLoadedSongPath = null;
                _preLoadedFileContent = null;
                _preLoadedMemoryUsage = 0;
            }
        }

        /// <summary>
        /// Public method to clear pre-loading cache on app shutdown
        /// </summary>
        public void ClearPreLoadingCacheOnShutdown()
        {
            if (SettingsManager.Instance.CurrentSettings.EnablePreLoading)
            {
                ClearPreLoadingCache();
            }
        }

        /// <summary>
        /// Formats bytes to human-readable format
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.##} {sizes[order]}";
        }
    }
}