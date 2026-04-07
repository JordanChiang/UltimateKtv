using MaterialDesignThemes.Wpf;
using System;
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
using CrazyKTV_MediaKit.DirectShow.MediaPlayers;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        private List<WaitingListItem> _waitingList = new List<WaitingListItem>();
        private int _currentWaitingListPage = 1;
        private int _totalWaitingListPages = 1;

        // Queue for songs added during a transition
        private Queue<SongDisplayItem> _pendingSongs = new Queue<SongDisplayItem>();

        /// <summary>
        /// Processes any songs that were queued during a transition.
        /// Should be called whenever _isTransitioningSong becomes false.
        /// </summary>
        private void ProcessPendingSongs()
        {
            if (_pendingSongs != null && _pendingSongs.Count > 0)
            {
                DebugLog($"ProcessPendingSongs: Processing {_pendingSongs.Count} pending songs");
                
                int maxLoop = _pendingSongs.Count + 5; // Safety break
                int current = 0;
                
                while (_pendingSongs.Count > 0 && current < maxLoop)
                {
                    var song = _pendingSongs.Dequeue();
                    AddSongToWaitingList(song);
                    current++;
                }
            }
        }

        /// <summary>
        /// Initializes the waiting list with empty rows
        /// </summary>
        private void InitializeWaitingList()
        {
            _waitingList.Clear(); // Clear the ObservableCollection
            _currentWaitingListPage = 1;
            UpdateWaitingListDisplay();
        }

        /// <summary>
        /// Updates the WaitingListGrid display and handles auto-play logic
        /// </summary>
        private void UpdateWaitingListDisplay()
        {
            // Calculate pagination
            var nonEmptySongs = _waitingList.Where(item => !string.IsNullOrEmpty(item.WaitingListSongName)).ToList();
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

            // Fill remaining slots with empty items to always show 9 rows
            var displayList = new List<WaitingListItem>(songsForPage);
            while (displayList.Count < WaitingListPageSize)
            {
                displayList.Add(new WaitingListItem());
            }

            // Update the grid
            WaitingListGrid.ItemsSource = null;
            WaitingListGrid.ItemsSource = displayList;

            // Update pagination info (2 lines)
            WaitListPageInfo.Text = $"第 {_currentWaitingListPage}/{_totalWaitingListPages} 頁\n({nonEmptySongs.Count} 首歌曲)";

            // Enable/disable pagination buttons
            WaitListPageUp.IsEnabled = _currentWaitingListPage > 1;
            WaitListPageDown.IsEnabled = _currentWaitingListPage < _totalWaitingListPages;

            // Notify web clients that waiting list has changed
            HttpServer.BroadcastEvent("playlistChanged", new { count = nonEmptySongs.Count });

            // Pre-loading logic: cache the first song file content if enabled
            if (SettingsManager.Instance.CurrentSettings.EnablePreLoading && nonEmptySongs.Any())
            {
                var firstSong = nonEmptySongs.First();
                if (firstSong.FilePath != _preLoadedSongPath)
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

                        if (File.Exists(firstSong.FilePath))
                        {
                            var fileInfo = new FileInfo(firstSong.FilePath);
                            long fileSize = fileInfo.Length;
                            
                            try
                            {
                                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                                _preLoadedFileContent = File.ReadAllBytes(firstSong.FilePath);
                                stopwatch.Stop();
                                
                                _preLoadedSongPath = firstSong.FilePath;
                                _preLoadedMemoryUsage = _preLoadedFileContent.Length;
                                _preLoadingCacheCount++;
                                AppLogger.Log($"Pre-loading: Cached '{firstSong.WaitingListSongName}' ({FormatBytes(_preLoadedMemoryUsage)}) in {stopwatch.ElapsedMilliseconds}ms [#{_preLoadingCacheCount}]");
                            }
                            catch (OutOfMemoryException)
                            {
                                AppLogger.LogError($"Pre-loading: OutOfMemory - File '{Path.GetFileName(firstSong.FilePath)}' size {FormatBytes(fileSize)} exceeds available memory", null);
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
                        AppLogger.LogError($"Pre-loading: Error caching '{Path.GetFileName(firstSong.FilePath)}'", ex);
                        _preLoadedSongPath = null;
                        _preLoadedFileContent = null;
                        _preLoadedMemoryUsage = 0;
                    }
                }
            }

            // Auto-play logic: if list is not empty and nothing is currently playing
            if (nonEmptySongs.Any() && !_isPlayingFromWaitingList)
            {
                PlayNextSongFromWaitingList();
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
                        // Store the item for menu actions (avoid relying on SelectedItem)
                        _waitingListContextItem = item;
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
                        _waitingListContextItem = item;
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
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")),
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 0, 6, 0)
                    };
                    headerPanel.Children.Add(webIcon);

                    // Add username text
                    var usernameText = new TextBlock
                    {
                        Text = item.OrderedBy,
                        FontWeight = FontWeights.Bold,
                        FontSize = 16,
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
            MoveSongToFirst(selected.Id, isRemoteCall: false);
        }

        /// <summary>
        /// Context menu action: Remove selected waiting item from list (UI click handler)
        /// </summary>
        private void WaitingList_RemoveFromList_Click(object sender, RoutedEventArgs e)
        {
            var selected = _waitingListContextItem ?? (WaitingListGrid?.SelectedItem as WaitingListItem);
            if (selected == null || string.IsNullOrEmpty(selected.WaitingListSongName)) return;
            RemoveSongFromList(selected.Id, isRemoteCall: false);
        }

        /// <summary>
        /// Context menu action: Move selected waiting item down by one position (UI click handler)
        /// </summary>
        private void WaitingList_MoveToNext_Click(object sender, RoutedEventArgs e)
        {
            var selected = _waitingListContextItem ?? (WaitingListGrid?.SelectedItem as WaitingListItem);
            if (selected == null || string.IsNullOrEmpty(selected.WaitingListSongName)) return;
            MoveSongToNext(selected.Id, isRemoteCall: false);
        }

        /// <summary>
        /// Plays the first song from waiting list and removes it
        /// </summary>
        private void PlayNextSongFromWaitingList() // Note: Not async void, timeout is handled in a background task.
        {
            // If a transition is already in progress, do nothing. This is the main lock.
            if (_isTransitioningSong)
            {
                DebugLog("PlayNextSongFromWaitingList: Aborted, a transition is already in progress.");
                return;
            }

            try
            {
                // Check if there are any playable songs in the list.
                var firstSong = _waitingList?.FirstOrDefault(item => !string.IsNullOrEmpty(item.WaitingListSongName));

                if (firstSong == null)
                {
                    DebugLog("PlayNextSongFromWaitingList: No songs in waiting list.");
                    _isPlayingFromWaitingList = false;
                    
                    // Stop playback first
                    SafeStop(mediaUriElement, nameof(mediaUriElement));
                    SetPlayerControlsEnabled(false); // Nothing to control
                    PauseBtn.Content = "暫停"; // Reset pause button text
                    
                    // Clear pre-loaded cache when list is empty
                    ClearPreLoadingCache();
                    
                    // Start a 1-second delayed random play check if enabled
                    StartDelayedRandomPlayWhenEmpty();
                    return; // No songs to play.
                }

                // Lock: We are now officially starting a transition.
                _isTransitioningSong = true;
                DebugLog("PlayNextSongFromWaitingList: Lock acquired (_isTransitioningSong = true).");
                SetPlayerControlsEnabled(false); // Disable controls during transition

                if (!File.Exists(firstSong!.FilePath))
                {
                    DebugLog($"Video file not found: {firstSong.FilePath}. Removing and trying next.");
                    AppLogger.Log($"Video file not found: {firstSong.FilePath}. Removing and trying next.");
                    _waitingList!.Remove(firstSong);
                    _isTransitioningSong = false; // Release lock before letting Update trigger a new attempt.
                    ProcessPendingSongs(); // Check if any songs were added while we were trying this one
                    UpdateWaitingListDisplay(); // This will re-trigger this method if more songs exist.
                    return;
                }

                // Set playing state flag
                _isPlayingFromWaitingList = true;
                _isRandomSongPlaying = (firstSong.OrderedBy == "隨機播放");
                IsPlayingYoutube = firstSong.IsYoutube;


                PlayingFilePath = firstSong.FilePath;
                
                if (firstSong.IsYoutube)
                {
                    // For YouTube, create a dummy _playingSongData dictionary
                    _playingSongData = new Dictionary<string, object?>
                    {
                        { "Song_Id", firstSong.SongId },
                        { "Song_SongName", firstSong.WaitingListSongName },
                        { "Song_Singer", firstSong.WaitingListSingerName },
                        { "Song_Lang", "Youtube" },
                        { "FilePath", firstSong.FilePath },
                        { "Song_Volume", firstSong.Volume },
                        { "Song_Track", firstSong.AudioTrack },
                        { "IsYoutube", true }
                    };
                    DebugLog($"Created dummy _playingSongData for YouTube: {firstSong.WaitingListSongName}");
                }
                else
                {
                    // Get song metadata from database cache
                    _playingSongData = SongDatas.SongData?.FirstOrDefault(s =>
                        s != null && (s.TryGetValue("FilePath", out var fp) ? fp?.ToString() ?? "" : "") == firstSong.FilePath
                    );
                }
                // Log if using pre-loaded file content
                if (SettingsManager.Instance.CurrentSettings.EnablePreLoading && 
                    _preLoadedSongPath == firstSong.FilePath && _preLoadedFileContent != null)
                {
                    AppLogger.Log($"Pre-loading: Using cached file ({FormatBytes(_preLoadedMemoryUsage)})");
                }

                DebugLog($"Playing from waiting list: {firstSong!.WaitingListSongName} by {firstSong.WaitingListSingerName}");

                _waitingList!.Remove(firstSong);

                // Update display immediately after removing the song
                UpdateWaitingListDisplay();

                // Stop current playback and start the new one
                SafeStop(mediaUriElement, nameof(mediaUriElement));

                // Cancel any pending media load timeout and create a new one
                _mediaLoadCts?.Cancel();
                _mediaLoadCts = new CancellationTokenSource();
                var cancellationToken = _mediaLoadCts.Token;

                // Track the current playing file and its properties
                // Set the volume slider and player volume to the song's specific setting
                if (VolumeLockToggle.IsChecked != true)
                {
                    VolumeSlider.Value = firstSong.Volume;
                }
                else
                {
                    // Volume lock is active: use the locked volume value
                    VolumeSlider.Value = _lockedVolume;
                }
/*
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
*/
            // Apply settings
            var settings = SettingsManager.Instance.CurrentSettings;
            
            // Video Renderer
            mediaUriElement.VideoRenderer = settings.VideoRendererType == 1 
                ? VideoRendererType.EnhancedVideoRenderer 
                : VideoRendererType.VideoMixingRenderer9;

            // Audio Renderer
            if (!string.IsNullOrEmpty(settings.AudioRendererDevice) && settings.AudioRendererDevice != "Default DirectSound Device")
            {
                mediaUriElement.AudioRenderer = settings.AudioRendererDevice;
            }

            // HW Acceleration
            mediaUriElement.EnableHWAccel = settings.EnableHWAccel;
            if (settings.EnableHWAccel)
            {
                // Assuming DefaultHWAccel takes the int value directly or cast to an enum
                 // 0:"AutoDetect", 1:"NVIDIA CUVID", 2:"Intel® Quick Sync", 3:"DXVA2 (copy-back)", 4:"DXVA2 (native)", 5:"D3D11"
                 mediaUriElement.DefaultHWAccel = (CrazyKTV_MediaKit.DirectShow.Interfaces.LavVideo.LAVHWAccel)settings.HWAccelMode;
            }

            mediaUriElement.DeeperColor = !(settings.VideoRendererType == 0);
            mediaUriElement.Stretch = settings.IsMediaFullScreen ? Stretch.Fill : Stretch.Uniform;
            mediaUriElement.EnableAudioCompressor = false;
            mediaUriElement.EnableAudioProcessor = false;
            //element.EndInit();
            
            // Configure codec directory
            string codecFolderName = Environment.Is64BitProcess ? "Codec" : "Codec_x86";
            string codecPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, codecFolderName);
            mediaUriElement.MediaUriPlayer.CodecsDirectory = codecPath;

            mediaUriElement.Source = new Uri(firstSong.FilePath); // This triggers MediaOpened/MediaFailed where the lock will be released.
            DebugLog($"Main media Source set to: {firstSong.FilePath}");
            AppLogger.Log($"Playing from waiting list: {firstSong!.WaitingListSongName} by {firstSong.WaitingListSingerName}");

/*
                // Start a timeout task. If MediaOpened/Failed doesn't fire within 10s, we assume a hang.
                Task.Run(async () =>
                {
                    // Use Task.WhenAny to avoid TaskCanceledException from Task.Delay
                    var tcs = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => tcs.TrySetResult(true)))
                    {
                        var delayTask = Task.Delay(TimeSpan.FromSeconds(5));
                        var completedTask = await Task.WhenAny(delayTask, tcs.Task);

                        if (completedTask == tcs.Task)
                        {
                            // Cancelled (MediaOpened/Failed called)
                            DebugLog("Media load timeout task cancelled (operation was successful).");
                            return;
                        }
                    }

                    // TIMEOUT
                    DebugLog($"!!! MEDIA LOAD TIMEOUT for '{firstSong.FilePath}'. The player appears to be hung.");

                    // Dispatch back to the UI thread to perform a full player reset.
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        // Show message on UI thread
                        MessageBox.Show("媒體載入逾時，播放器可能已當機。正在嘗試恢復...", "錯誤", MessageBoxButton.OK, MessageBoxImage.Error);

                        if (_isTransitioningSong) // Check lock again on UI thread
                        {
                            DebugLog("Timeout recovery: Performing full player re-initialization.");

                            // 1. Unsubscribe from the old, broken media element's events to prevent memory leaks.
                            var oldPlayer = mediaUriElement;
                            if (oldPlayer != null)
                            {
                                DebugLog("Timeout recovery: Unsubscribing events from old player instance.");
                                oldPlayer.MediaFailed -= mediaUriElement_MediaFailed;
                                oldPlayer.MediaOpened -= mediaUriElement_MediaOpened;
                                oldPlayer.MediaClosed -= MediaUriElement_MediaClosed;
                                oldPlayer.MediaEnded -= MediaUriElement_MediaEnded;
                                oldPlayer.MediaPositionChanged -= MediaUriElement_MediaPositionChanged;
                                oldPlayer.Close();
                            }

                            // 2. Force-close the secondary display window if it exists.
                            _videoDisplayWindow?.ForceClose();
                            _videoDisplayWindow = null;

                            // 3. Tell the MediaHostManager to create a NEW MediaUriElement instance.
                            MediaHostManager.Instance.ResetInstance();
                            DebugLog("Timeout recovery: A new MediaUriElement instance has been created.");

                            // 4. Re-wire all event handlers to the NEW instance.
                            InitializeMediaPlayer();

                            // 5. Clear the main window's media container.
                            MediaPlayerContainer.Children.Clear();

                            // 6. Re-initialize the display with the NEW media element.
                            InitializeDisplayConfiguration();

                            // 7. Release the lock, re-enable controls, and try the next song.
                            _isTransitioningSong = false;
                            SetPlayerControlsEnabled(true);
                            PlayNextSongFromWaitingList();
                        }
                    });
                });
*/
            }

            catch (Exception ex)
            {
                DebugLog($"Error playing from waiting list: {ex.Message}\n{ex}");
                AppLogger.LogError("Error playing from waiting list", ex);
                _isPlayingFromWaitingList = false;
                _isTransitioningSong = false; // Release lock on any unexpected error.
                ProcessPendingSongs(); // Retry pending adds even on error
                SetPlayerControlsEnabled(true); // Also re-enable controls on error
            }

        }

        /// <summary>
        /// Adds a song to the waiting list with duplicate checking
        /// </summary>
        /// <param name="song">The song to add</param>
        private void AddSongToWaitingList(SongDisplayItem song)
        {
            // Check if file path is valid
            bool isYoutube = song.IsYoutube;
            if (!isYoutube && (string.IsNullOrEmpty(song.FilePath) || !File.Exists(song.FilePath)))
            {
                DebugLog($"Invalid file path for song '{song.SongName}': {song.FilePath ?? "(empty)"}");
                AppLogger.Log($"Invalid file path for song '{song.SongName}': {song.FilePath ?? "(empty)"}");
                return;
            }

            if(_isTransitioningSong)
            {
                DebugLog($"AddSongToWaitingList, _isTransitioningSong = true, queueing '{song.SongName}'");
                AppLogger.Log($"AddSongToWaitingList, _isTransitioningSong = true, queueing '{song.SongName}'");
                _pendingSongs.Enqueue(song);
                return;
            }

            // Check for duplicates (both song name and singer must match)
            var isDuplicate = _waitingList.Any(item =>
                !string.IsNullOrEmpty(item.WaitingListSongName) &&
                item.WaitingListSongName.Equals(song.SongName, StringComparison.OrdinalIgnoreCase) &&
                item.WaitingListSingerName.Equals(song.SingerName, StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                Debug.WriteLine($"Song already exists in waiting list: {song.SongName} by {song.SingerName}");
                ShowDuplicateSongMessage(song.SongName, song.SingerName);
                return;
            }

            var waitingItem = new WaitingListItem
            {
                WaitingListSongName = song.SongName,
                SongId = song.SongId, // Pass the SongId
                WaitingListSingerName = song.IsYoutube ? string.Empty : song.SingerName,
                FilePath = song.FilePath,
                Volume = song.Volume,
                AudioTrack = song.AudioTrack,
                OrderedBy = song.OrderedBy,
                IsYoutube = song.IsYoutube
            };

            // Dismiss web host info marquee on first song order (single-monitor mode)
            if (_isWebHostMarqueeDisplayed)
            {
                MarqueeAPI.Stop(Enums.MarqueeDisplayDevice.PlayerScreen);
                _isWebHostMarqueeDisplayed = false;
                AppLogger.Log("Web host info marquee dismissed on first song order");
            }

            // Add to the end of the list
            _waitingList.Add(waitingItem);

            AppLogger.Log($"User action: Added song '{song.SongName}' by '{song.SingerName}' to waiting list" + 
                (!string.IsNullOrEmpty(song.OrderedBy) ? $" (Ordered by: {song.OrderedBy})" : ""));
            
            // If a random song is currently playing, cut it off so the user's song can play immediately
            if (_isRandomSongPlaying && _isPlayingFromWaitingList)
            {
                DebugLog("AddSongToWaitingList: User ordered a song while random song is playing. Cutting off random song.");
                AppLogger.Log("Random play cutoff: User ordered a song, stopping random playback.");
                
                // Reset flags first to avoid race conditions with MediaEnded
                _isRandomSongPlaying = false;
                _isPlayingFromWaitingList = false;
                
                // Stop the player. This will trigger MediaEnded/MediaClosed, 
                // but since _isPlayingFromWaitingList is now false, PlayNextSongFromWaitingList 
                // in MediaEnded will find the list is NOT empty and play the user's song.
                SafeStop(mediaUriElement, nameof(mediaUriElement));
            }

            Debug.WriteLine($"Added to waiting list: {song.SongName} by {song.SingerName}" + 
                (!string.IsNullOrEmpty(song.OrderedBy) ? $" (Ordered by: {song.OrderedBy})" : ""));

            // Show marquee on player screen for 3 seconds with large font
            MarqueeAPI.ShowCustomStaticText(
                $"點播: {song.SongName} - {song.SingerName}",
                TextSettingsHandler.SongAddedForeground,
                TextSettingsHandler.FontFamily,
                TextSettingsHandler.Settings.SongAddedFontSize,
                Enums.MarqueePosition.Top,
                3,
                Enums.MarqueeDisplayDevice.PlayerScreen
            );

            UpdateWaitingListDisplay();
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
                Foreground = new SolidColorBrush(Colors.Orange),
                Margin = new Thickness(0, 0, 0, 16)
            };
            dialogContent.Children.Add(icon);

            // Add title
            var title = new TextBlock
            {
                Text = "重複歌曲",
                FontSize = 20,
                FontWeight = FontWeights.Medium,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 8)
            };
            dialogContent.Children.Add(title);

            // Add message
            var message = new TextBlock
            {
                Text = $"歌曲「{songName}」- {singerName} 已在等待清單中",
                FontSize = 16,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 24)
            };
            dialogContent.Children.Add(message);

            // Add OK button
            var okButton = new Button
            {
                Content = "確定",
                HorizontalAlignment = HorizontalAlignment.Center,
                Command = DialogHost.CloseDialogCommand,
                CommandParameter = true
            };
            TextSettingsHandler.ApplyOutlinedButtonStyle(okButton, 16);
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
        /// <param name="id">The unique ID of the waiting list item</param>
        /// <param name="isRemoteCall">True if called from web API, false if from UI</param>
        public void MoveSongToFirst(Guid id, bool isRemoteCall = true)
        {
            try
            {
                int idx = _waitingList.FindIndex(w => w.Id == id);
                if (idx <= 0)
                {
                    UpdateWaitingListDisplay();
                    return;
                }

                var item = _waitingList[idx];
                var source = isRemoteCall ? "Remote" : "User";
                AppLogger.Log($"{source} action: Move song '{item.WaitingListSongName}' to first in waiting list");
                _waitingList.RemoveAt(idx);
                _waitingList.Insert(0, item);
                UpdateWaitingListDisplay();
            }
            catch (Exception ex)
            {
                var source = isRemoteCall ? "Remote" : "User";
                AppLogger.LogError($"Error moving song to first in waiting list ({source})", ex);
            }
        }

        /// <summary>
        /// Removes a song from the waiting list.
        /// Called by both UI context menu and remote web API.
        /// </summary>
        /// <param name="id">The unique ID of the waiting list item</param>
        /// <param name="isRemoteCall">True if called from web API, false if from UI</param>
        public void RemoveSongFromList(Guid id, bool isRemoteCall = true)
        {
            try
            {
                int idx = _waitingList.FindIndex(w => w.Id == id);
                if (idx >= 0)
                {
                    var item = _waitingList[idx];
                    var source = isRemoteCall ? "Remote" : "User";
                    AppLogger.Log($"{source} action: Remove song '{item.WaitingListSongName}' from waiting list");
                    _waitingList.RemoveAt(idx);
                    UpdateWaitingListDisplay();
                }
            }
            catch (Exception ex)
            {
                var source = isRemoteCall ? "Remote" : "User";
                AppLogger.LogError($"Error removing song from waiting list ({source})", ex);
            }
        }

        /// <summary>
        /// Moves a song down by one position in the waiting list.
        /// Called by both UI context menu and remote web API.
        /// </summary>
        /// <param name="id">The unique ID of the waiting list item</param>
        /// <param name="isRemoteCall">True if called from web API, false if from UI</param>
        public void MoveSongToNext(Guid id, bool isRemoteCall = true)
        {
            try
            {
                int idx = _waitingList.FindIndex(w => w.Id == id);
                // If found and not already the last item, swap with the next item
                if (idx >= 0 && idx < _waitingList.Count - 1)
                {
                    var item = _waitingList[idx];
                    var source = isRemoteCall ? "Remote" : "User";
                    AppLogger.Log($"{source} action: Move song '{item.WaitingListSongName}' down in waiting list");
                    var temp = _waitingList[idx + 1];
                    _waitingList[idx + 1] = _waitingList[idx];
                    _waitingList[idx] = temp;
                    UpdateWaitingListDisplay();
                }
            }
            catch (Exception ex)
            {
                var source = isRemoteCall ? "Remote" : "User";
                AppLogger.LogError($"Error moving song down in waiting list ({source})", ex);
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