using CrazyKTV_MediaKit.DirectShow.Controls;
using CrazyKTV_MediaKit.DirectShow.MediaPlayers;
using System;
using System.Data.OleDb;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MaterialDesignThemes.Wpf;
using System.Windows.Threading;
using UltimateKtv.Enums;
using System.Collections.Generic;
using UltimateKtv.Models;
using UltimateKtv.Services;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        private bool LegacyAudioChannelDefinition;
        
        // LRC fields
        private List<LrcLine>? _currentLyrics;
        private int _currentLyricIndex = -1;

        private void InitializeMediaPlayer()
        {
            try
            {
                var sharedMediaPlayer = MediaHostManager.Instance.Element;

                // Wire up this window's event handlers to the shared media player
                sharedMediaPlayer.MediaFailed += mediaUriElement_MediaFailed;
                sharedMediaPlayer.MediaOpened += mediaUriElement_MediaOpened;
                sharedMediaPlayer.MediaClosed += MediaUriElement_MediaClosed;
                sharedMediaPlayer.MediaEnded += MediaUriElement_MediaEnded;
                sharedMediaPlayer.MediaPositionChanged += MediaUriElement_MediaPositionChanged;
                DebugLog("Media player event handlers wired up.");
            }
            catch (Exception ex)
            {
                DebugLog($"Error initializing media player: {ex.Message}\n{ex}");
            }
        }

        private void mediaUriElement_MediaFailed(object? sender, MediaFailedEventArgs e)
        {
            Debug.WriteLine($"Media failed to load or play. Error: {e.Exception.Message}");
            AppLogger.LogError($"Media failed to load or play", e.Exception);

            // Ensure we are on the UI thread as this event may come from a background thread
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => mediaUriElement_MediaFailed(sender, e));
                return;
            }

            _mediaLoadCts?.Cancel(); // Cancel any pending media load timeout

            // Failure: Release the transition lock so the next operation can proceed.
            _isTransitioningSong = false;
            ProcessPendingSongs(); // Retry pending adds
            _isPlayingFromWaitingList = false;
            _isRandomSongPlaying = false;
            IsPlayingYoutube = false;
            
            // Clear Lyrics
            _currentLyrics = null;
            _currentLyricIndex = -1;
            _videoDisplayWindow?.UpdateLyrics("", Visibility.Collapsed);
            
            DebugLog("mediaUriElement_MediaFailed: Lock released (_isTransitioningSong = false).");
            SetPlayerControlsEnabled(true); // Re-enable controls so the user can try another action
            // Optionally, try to play the next song in the list automatically on failure.
            PlayNextSongFromWaitingList();
        }

        private void mediaUriElement_MediaOpened(object sender, RoutedEventArgs e)
        {
            AppLogger.Log($"mediaUriElement_MediaOpened");
            DebugLog($"mediaUriElement_MediaOpened");
            // This event fires after the media source has been successfully opened and is ready.
            _mediaLoadCts?.Cancel(); // Cancel any pending media load timeout
            // This is the correct and safe place to configure and start playback.
            if (sender is not MediaUriElement player)
            {
                DebugLog($"     incorrect sender={sender} ");
                return;
            }

            if (_playingSongData == null)
            {
                DebugLog($"     mediaUriElement_MediaOpened but _playingSongData = null");
                return;
            }

            try
            {
                // Only update the volume from the song's data if the volume lock is NOT active.
                if (VolumeLockToggle.IsChecked != true)
                {
                    if (_playingSongData.TryGetValue("Song_Volume", out var volumeObj) &&
                        volumeObj != null &&
                        int.TryParse(volumeObj.ToString(), out int dbVolume))
                    {
                        PlayingFileVolume = dbVolume;
                    }
                    else
                    {
                        PlayingFileVolume = 60; // Fallback to a sensible default
                    }
                }
                else
                {
                    // Volume lock is active: use the locked volume value
                    PlayingFileVolume = _lockedVolume;
                }

                // Apply initial mapped volume to the player based on PlayingFileVolume
                try
                {
                    // PlayingFileVolume is 0..100
                    player.Volume = ComputePlayerVolume(PlayingFileVolume);
                    
                    // Synchronize the UI slider with the actual loaded volume (e.g. if DB was just updated)
                    if (VolumeLockToggle.IsChecked != true && VolumeSlider.Value != PlayingFileVolume)
                    {
                        VolumeSlider.Value = PlayingFileVolume;
                    }
                }
                catch {
                    DebugLog($"     mediaUriElement_MediaOpened: failed to set player.Volume={PlayingFileVolume} ");
                }

                LegacyAudioChannelDefinition = SettingsManager.Instance.CurrentSettings.IsLegacyAudioChannelDefinitionEnabled;

                //player.Volume = MediaPlayerContainer.ActualVolume;
                
                // Retrieve and set the audio track for the current song
                if (_playingSongData.TryGetValue("Song_Track", out var trackObj) &&
                    trackObj != null &&
                    int.TryParse(trackObj.ToString(), out int dbTrack))
                {
                    if(LegacyAudioChannelDefinition || player.AudioStreams.Count >= 3) 
                    {
                        PlayingFileMusicTrack = dbTrack;
                    }
                    else
                    {
                        // LegacyAudioChannelDefinition is false and only 1 or 2 audio tracks
                        PlayingFileMusicTrack = dbTrack == 1 ? 2 : 1;                     
                    }
                }
                else
                {
                    // Fallback to a default track if not found or invalid
                    PlayingFileMusicTrack = 0;
                }

                // Apply the global AudioAmplify setting from the options panel
                player.AudioAmplify = SettingsManager.Instance.CurrentSettings.AudioAmplify;
                DebugLog($"     AudioAmplify= {player.AudioAmplify}, VideoRendererType: {player.VideoRenderer}");
                
                // Check if a vocal track is fixed and apply it if valid for the current media.
                if (_isVocalTrackFixed && _fixedAudioTrack != -1)// && audioTracks.Contains(_fixedAudioTrack))
                {
                    player.AudioTrack = _fixedAudioTrack;
                    DebugLog($"     Applying fixed audio track: {_fixedAudioTrack}");
                }
                else if (player.AudioStreams.Count == 1)
                {
                    // Only 1 audio channel
                    player.AudioChannel = PlayingFileMusicTrack;
                    player.AudioTrack = player.AudioStreams[0];
                }
                else
                {
                    // Multiple audio channels: Fallback to the designated music track
                    if (player.AudioStreams.Contains(PlayingFileMusicTrack))
                    {
                        player.AudioTrack = PlayingFileMusicTrack;
                    }
                    else if (player.AudioStreams.Count > 0)
                    {
                        // If the designated music track is not available in this file,
                        // default to the first available track.
                        player.AudioTrack = player.AudioStreams[0];
                    }
                }

                if(IsPlayingYoutube)
                {
                    player.AudioChannel = 3;
                    if(SettingsManager.Instance.CurrentSettings.YoutubeDefaultNoVocal == false)
                        player.AudioChannel = 1;
                }
                
                // Attempt to load LRC file
                _currentLyrics = null;
                _currentLyricIndex = -1;
                _videoDisplayWindow?.UpdateLyrics("", Visibility.Collapsed);

                if (!string.IsNullOrEmpty(PlayingFilePath) && !IsPlayingYoutube)
                {
                    string lrcPath = Path.ChangeExtension(PlayingFilePath, ".lrc");
                    DebugLog($"[LRC] Checking for LRC file at: {lrcPath}");
                    AppLogger.Log($"[LRC] Checking for LRC file at: {lrcPath}");
                    if (File.Exists(lrcPath))
                    {
                        var parsedLyrics = LrcParser.Parse(lrcPath);
                        if (parsedLyrics != null && parsedLyrics.Count > 0)
                        {
                            _currentLyrics = parsedLyrics;
                            _currentLyricIndex = 0;
                            _videoDisplayWindow?.UpdateLyrics("", Visibility.Visible); // Show the block, even if first text is empty
                            DebugLog($"[LRC] Loaded {parsedLyrics.Count} lyric lines from {lrcPath}");
                            AppLogger.Log($"[LRC] Loaded {parsedLyrics.Count} lyric lines from {lrcPath}");
                        }
                        else
                        {
                            DebugLog($"[LRC] Parsed lyrics are null or empty for {lrcPath}");
                        }
                    }
                }
                
                Debug.WriteLine($"Playing video path :      {PlayingFilePath}");
                Debug.WriteLine($"        Audio Volume:     {PlayingFileVolume}");
                Debug.WriteLine($"        Audio Track:      {player.AudioTrack}");
                Debug.WriteLine($"        Audio Channel:    {player.AudioChannel}");
                Debug.WriteLine($"        IsPlayingYoutube: {IsPlayingYoutube}");
                AppLogger.Log($"    vol={PlayingFileVolume}, track={player.AudioTrack}, channel={player.AudioChannel} ");
                // Show marquee with song name and singer name before playing
                var songName = _playingSongData.TryGetValue("Song_SongName", out var nameObj) ? nameObj?.ToString() ?? "未知歌曲" : "未知歌曲";
                var singerName = _playingSongData.TryGetValue("Song_Singer", out var singerObj) ? singerObj?.ToString() ?? "未知歌手" : "未知歌手";
                var songCount = _playingSongData.TryGetValue("Song_PlayCount", out var pcObj);
                // Display marquee on main window                 
                MarqueeAPI.ShowCustomStaticText(
                    $"{singerName} {songName}",
                    TextSettingsHandler.MarqueeForeground,
                    TextSettingsHandler.FontFamily,
                    TextSettingsHandler.Settings.NotificationFontSize,
                    MarqueePosition.Top,
                    0,
                    MarqueeDisplayDevice.ConsoleScreen
                );
                // Display marquee on player display device
                if (_videoDisplayWindow != null)
                {
                    MarqueeAPI.ShowSongInfo(songName, singerName, 120, MarqueeDisplayDevice.PlayerScreen);

                        // Ensure player window is on top in single monitor mode when song starts
                        if (IsSingleMonitorMode)
                        {
                            DebugLog("Single monitor mode: Song starting. Bringing VideoDisplayWindow to front.");

                            // Close any owned windows (Settings, Quit dialog, etc.) to ensure they don't block the video
                            var ownedWindows = this.OwnedWindows.Cast<Window>().ToList();
                            foreach (var window in ownedWindows)
                            {
                                DebugLog($"Closing owned window: {window.Title}");
                                window.Close();
                            }
                            
                            // Reset cursor idle timer to ensure it auto-hides after switching
                            ResetCursorIdleTimer();

                            this.Visibility = Visibility.Hidden;
                            this.WindowState = WindowState.Minimized;

                        // Ensure VideoDisplayWindow is activated and brought to front
                        _videoDisplayWindow.Show();
                        _videoDisplayWindow.Activate();
                        // Brief Topmost toggle to force window to front over other apps if necessary
                        _videoDisplayWindow.Topmost = true;
                        _videoDisplayWindow.Topmost = false;
                    }
                }

                DebugLog($"Marquee displayed: {songName} - {singerName}");

                //player.Play();

                UpdateVocalMusicButtonStates();
                
                // Reset or reapply pitch for the new song
                ResetPitchForNewSong();

                // Apply audio channel for random play songs (if applicable)
                ApplyRandomPlayAudioChannel();

                // Clear today's play list on first song, then record this song play
                // no needed for random play or youtube songs
                var songId = _playingSongData.TryGetValue("Song_Id", out var idObj) ? idObj?.ToString() ?? "" : "";
                if (!string.IsNullOrEmpty(songId) && (!_isRandomSongPlaying) && (!IsPlayingYoutube))
                {
                    if (_isFirstSongInSession)
                    {
                        ClearTodayPlayList();
                        _isFirstSongInSession = false;
                    }
                    RecordSongPlay(songId);
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[MainPlayer] mediaUriElement_MediaOpened exception: {ex.Message}");
                AppLogger.LogError("[MainPlayer] mediaUriElement_MediaOpened exception", ex);
            }
            finally
            {
                // Success: The new media is open and playing. Release the transition lock.
                _isTransitioningSong = false;
                ProcessPendingSongs(); // Retry pending adds
                DebugLog("mediaUriElement_MediaOpened: Lock released (_isTransitioningSong = false).");
                // Reset the play count increment flag for the new song.
                _playCountIncrementedForCurrentSong = false;
                DebugLog("Play count increment flag reset for new song.");
                SetPlayerControlsEnabled(true);
                PauseBtn.Content = "暫停"; // Ensure pause button text is correct for new song
            }
        }

        private void MediaUriElement_MediaClosed(object sender, RoutedEventArgs e)
        {
            // This event fires when the media is closed (e.g., by calling Stop()).
        }

        private void MediaUriElement_MediaEnded(object sender, RoutedEventArgs e)
        {
            // This event fires when the media finishes playing.
            DebugLog("[MainPlayer] MediaEnded.");

            SaveCurrentSongVolume();

            // Stop all marquees when media ends
            MarqueeAPI.StopAll();
            DebugLog("All marquees stopped on media end.");

            _isPlayingFromWaitingList = false;
            _isRandomSongPlaying = false;
            IsPlayingYoutube = false;
            
            // Clear Lyrics
            _currentLyrics = null;
            _currentLyricIndex = -1;
            _videoDisplayWindow?.UpdateLyrics("", Visibility.Collapsed);
            
            // PlayNextSongFromWaitingList will handle random play with 2-second delay if playlist is empty
            PlayNextSongFromWaitingList();
        }

        private void MediaUriElement_MediaPositionChanged(object sender, RoutedEventArgs e)
        {
            // This event fires repeatedly as the media plays.
            if (mediaUriElement != null && mediaUriElement.HasVideo)
            {
                try
                {
                    _isUpdatingProgressSlider = true;

                    var currentPositionTicks = mediaUriElement.MediaPosition;
                    var totalDurationTicks = mediaUriElement.MediaDuration;

                    // Convert ticks to TimeSpan
                    var currentPosition = TimeSpan.FromTicks(currentPositionTicks);
                    var totalDuration = TimeSpan.FromTicks(totalDurationTicks);

                    // Update time displays (only if user is not dragging - during drag, ValueChanged updates the preview time)
                    if (!_isUserDraggingProgressSlider)
                    {
                        CurrentTimeText.Text = FormatTime(currentPosition);
                    }

                    if (totalDurationTicks > 0)
                    {
                        TotalTimeText.Text = FormatTime(totalDuration);

                        // Update lyrics synchronously with position
                        if (_currentLyrics != null && _currentLyrics.Count > 0)
                        {
                            // If the user drags or the time jumps backwards, reset index to find correct line
                            if (_currentLyricIndex >= _currentLyrics.Count || 
                                (_currentLyricIndex > 0 && currentPosition < _currentLyrics[_currentLyricIndex - 1].Timestamp))
                            {
                                DebugLog($"[LRC] Resetting lyric index.");
                                _currentLyricIndex = 0;
                            }

                            // Advance lyrics index until the next timestamp is greater than current position
                            while (_currentLyricIndex < _currentLyrics.Count && currentPosition >= _currentLyrics[_currentLyricIndex].Timestamp)
                            {
                                _videoDisplayWindow?.UpdateLyrics(_currentLyrics[_currentLyricIndex].Text, Visibility.Visible);
                                // DebugLog($"[LRC] Showing lyric: {_currentLyrics[_currentLyricIndex].Text}"); // Can be spammy, leave commented unless needed
                                _currentLyricIndex++;
                            }
                        }

                        // Calculate progress percentage (used for both slider and play count check)
                        var progressPercentage = ((double)currentPositionTicks / totalDurationTicks) * 100.0;

                        // Update progress slider only if user is not dragging
                        if (!_isUserDraggingProgressSlider)
                        {
                            VideoProgressSlider.Value = Math.Max(0, Math.Min(100, progressPercentage));
                        }

                        // Increment play count if position more than user criteria and not already incremented
                        // no needed for random play or youtube songs
                        if (!_playCountIncrementedForCurrentSong && progressPercentage > PlayCountUpdatePercentage && (!_isRandomSongPlaying) && (!IsPlayingYoutube))
                        {
                            if (_playingSongData != null && _playingSongData.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int playCount))
                            {
                                int newPlayCount = playCount + 1;
                                _playingSongData["Song_PlayCount"] = newPlayCount;
                                _playCountIncrementedForCurrentSong = true;
                                DebugLog($"In-memory play count for '{_playingSongData["Song_SongName"]}' incremented to {newPlayCount}.");

                                // Persist the change to the database asynchronously
                                if (_playingSongData.TryGetValue("Song_Id", out var idObj) && idObj != null)
                                {
                                    string songId = idObj.ToString()!;
                                    Task.Run(() =>
                                    {
                                        try
                                        {
                                            string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrazySong.mdb");
                                            string sql = "UPDATE ktv_Song SET Song_PlayCount = ? WHERE Song_Id = ?";
                                            var parameters = new OleDbParameter[] { new OleDbParameter("?", newPlayCount), new OleDbParameter("?", songId) };
                                            DbHelper.Access.ExecuteNonQuery(dbPath, sql, null, parameters);
                                            AppLogger.Log($"DB Write: Updated Song_PlayCount to {newPlayCount} for Song_Id '{songId}'");
                                            DebugLog($"Successfully saved new play count ({newPlayCount}) for Song_Id '{songId}' to the database.");
                                        }
                                        catch (Exception ex)
                                        {
                                            DebugLog($"Failed to save play count to database: {ex.Message}");
                                            AppLogger.LogError($"DB Write failed: Song_PlayCount update for Song_Id '{songId}'", ex);
                                        }
                                    });
                                }
                            }
                            else
                            {
                                // Still set flag to true to avoid repeated attempts on invalid data
                                _playCountIncrementedForCurrentSong = true;
                                DebugLog("Could not increment play count: _playingSongData is null or Song_PlayCount is invalid.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLog($"Error updating media position: {ex.Message}");
                    AppLogger.LogError("Error updating media position", ex);
                }
                finally
                {
                    _isUpdatingProgressSlider = false;
                }
            }
        }

        private void SaveCurrentSongVolume()
        {
            // Do not save volume if it's 0 (invalid/muted) or 100 (invalid/muted)
            if (VolumeSlider.Value == 0 || VolumeSlider.Value > 100) return;
            
            // Only update the volume from the song's data if the volume lock is NOT active.
            if (VolumeLockToggle.IsChecked == true) return;
            
            if (_playingSongData == null || IsPlayingYoutube) return;

            if (_playingSongData.TryGetValue("Song_Id", out var idObj) && idObj != null)
            {
                string songId = idObj.ToString()!;
                int currentVolume = (int)VolumeSlider.Value;
                
                // Compare with original PlayingFileVolume
                if (currentVolume != PlayingFileVolume)
                {
                    DebugLog($"User changed volume from {PlayingFileVolume} to {currentVolume}. Saving to DB for Song_Id '{songId}'.");
                    
                    // update in memory DB
                    var memorySong = SongDatas.SongData?.FirstOrDefault(s => s != null && s.TryGetValue("Song_Id", out var obj) && obj?.ToString() == songId);
                    if (memorySong != null)
                    {
                        memorySong["Song_Volume"] = currentVolume;
                    }                    

                    // Update DB asynchronously
                    Task.Run(() =>
                    {
                        try
                        {
                            string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrazySong.mdb");
                            string sql = "UPDATE ktv_Song SET Song_Volume = ? WHERE Song_Id = ?";
                            var parameters = new OleDbParameter[] { 
                                new OleDbParameter("?", currentVolume), 
                                new OleDbParameter("?", songId) 
                            };
                            DbHelper.Access.ExecuteNonQuery(dbPath, sql, null, parameters);
                            DebugLog($"DB Write: Updated Song_Volume to {currentVolume} for Song_Id '{songId}'");
                            AppLogger.Log($"DB Write: Updated Song_Volume to {currentVolume} for Song_Id '{songId}'");
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"Failed to save volume to database: {ex.Message}");
                            AppLogger.LogError($"DB Write failed: Song_Volume update for Song_Id '{songId}'", ex);
                        }
                    });
                }
            }
        }

        public void SkipSong_Click(object sender, RoutedEventArgs e)
        {
            // The button's IsEnabled state, controlled by the transition lock,
            // now serves as the primary guard against spamming.
            try
            {
                SaveCurrentSongVolume();
                AppLogger.Log("User action: Skip song button clicked");
                DebugLog("SkipSong_Click: Skip song button clicked");
                mediaUriElement.Stop();
                mediaUriElement.Source = null; // Clear source to release resources immediately
                _isRandomSongPlaying = false;
                MarqueeAPI.StopAll();
                PlayNextSongFromWaitingList();
            }
            catch (Exception ex)
            {
                DebugLog($"SkipSong_Click: Error skipping song: {ex.Message}\n{ex}");
                AppLogger.LogError("SkipSong_Click: Error skipping song", ex);
            }
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            // Pause/Resume toggle
            try
            {
                if (mediaUriElement.IsPlaying)
                {
                    AppLogger.Log("User action: Pause button clicked");
                    mediaUriElement.Pause();

                    PauseBtn.Content = "繼續";
                    Debug.WriteLine("Media paused");
                }
                else
                {
                    AppLogger.Log("User action: Resume button clicked");
                    mediaUriElement.Play();

                    PauseBtn.Content = "暫停";
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Pause_Click: Error toggling pause: {ex.Message}\n{ex}");
                AppLogger.LogError("Pause_Click: Error toggling pause", ex);
            }
        }

        private void Repeat_Click(object sender, RoutedEventArgs e)
        {
            // Replay current song
            try
            {
                if (!string.IsNullOrEmpty(PlayingFilePath))
                {
                    AppLogger.Log($"User action: Repeat button clicked for '{PlayingFilePath}'");
                    // To safely repeat, we must stop the current playback and
                    // re-assign the Source. This forces the media graph to be
                    // rebuilt correctly, avoiding hangs when the player is in a
                    // stopped or closed state.
                    // Simply setting MediaPosition = 0 is not safe if the graph is torn down.
                    DebugLog($"Repeat_Click: Re-playing '{PlayingFilePath}'");

                    // Stop any current playback and release resources
                    SafeStop(mediaUriElement, nameof(mediaUriElement));

                    // Re-assign the source to trigger a new playback session from the start
                    var uri = new Uri(PlayingFilePath, UriKind.Absolute);
                    mediaUriElement.Source = uri;
                    _isRandomSongPlaying = false;
                }
                else
                {
                    DebugLog("Repeat_Click: No song is currently playing (PlayingFilePath is empty).");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error replaying song: {ex.Message}");
                AppLogger.LogError("Error replaying song", ex);
            }
        }

        // Flag to track if the user is currently dragging the video progress slider
        private bool _isUserDraggingProgressSlider = false;

        private void VideoProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (mediaUriElement == null || !mediaUriElement.HasVideo || _isUpdatingProgressSlider)
                return;

            // During dragging, update CurrentTimeText to show preview time
            if (_isUserDraggingProgressSlider)
            {
                try
                {
                    var totalDurationTicks = mediaUriElement.MediaDuration;
                    if (totalDurationTicks > 0)
                    {
                        var previewPositionTicks = (long)(totalDurationTicks * (e.NewValue / 100.0));
                        var previewPosition = TimeSpan.FromTicks(previewPositionTicks);
                        CurrentTimeText.Text = FormatTime(previewPosition);
                    }
                }
                catch { }
            }
            else
            {
                // Not dragging (i.e., user clicked directly on the track) - seek immediately
                try
                {
                    var totalDurationTicks = mediaUriElement.MediaDuration;
                    if (totalDurationTicks > 0)
                    {
                        var newPositionTicks = (long)(totalDurationTicks * (e.NewValue / 100.0));
                        mediaUriElement.MediaPosition = newPositionTicks;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting video position: {ex.Message}");
                    AppLogger.LogError("Error setting video position", ex);
                }
            }
        }

        private void VideoProgressSlider_DragStarted(object sender, System.Windows.Controls.Primitives.DragStartedEventArgs e)
        {
            _isUserDraggingProgressSlider = true;
        }

        private void VideoProgressSlider_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            _isUserDraggingProgressSlider = false;

            // Now seek to the final position after the user finished dragging
            if (mediaUriElement != null && mediaUriElement.HasVideo && !_isUpdatingProgressSlider)
            {
                try
                {
                    var totalDurationTicks = mediaUriElement.MediaDuration;
                    if (totalDurationTicks > 0)
                    {
                        var newPositionTicks = (long)(totalDurationTicks * (VideoProgressSlider.Value / 100.0));
                        mediaUriElement.MediaPosition = newPositionTicks;
//                        DebugLog($"Video position set to {TimeSpan.FromTicks(newPositionTicks):mm\\:ss} after drag completed.");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting video position on drag completed: {ex.Message}");
                    AppLogger.LogError("Error setting video position on drag completed", ex);
                }
            }
        }

        /// <summary>
        /// Handles clicks on the mute button (speaker icon). Toggles mute state.
        /// </summary>
        private void MuteButton_Click(object sender, RoutedEventArgs e)
        {
            if (mediaUriElement == null) return;

            _isMuted = !_isMuted;

            if (_isMuted)
            {
                AppLogger.Log("User action: Mute button clicked");
                // Store current volume and mute
                _volumeBeforeMute = VolumeSlider.Value;
                VolumeSlider.Value = 0; // This will trigger VolumeSlider_ValueChanged to update UI and player
            }
            else
            {
                AppLogger.Log("User action: Unmute button clicked");
                // Restore volume to previous level, or a sensible default if it was 0
                VolumeSlider.Value = _volumeBeforeMute > 0 ? _volumeBeforeMute : 50;
            }
        }


        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (mediaUriElement == null || _isUpdatingProgressSlider)
                return;

            var slider = sender as Slider;
            if (slider == null) return;

            // Snap the value to the nearest multiple of 5
            double snappedValue = Math.Round(e.NewValue / 5.0) * 5.0;

            // If the value is not snapped, update the slider. This will re-trigger the event.
            if (Math.Abs(snappedValue - slider.Value) > 0.01)
            {
                slider.Value = snappedValue;
                return; // The event will fire again with the snapped value.
            }

            // Now we have a snapped value, proceed with setting the volume.
            _isUpdatingProgressSlider = true;
            try
            {
                double sliderValue = snappedValue;

                // Set the player volume and update the UI text.
                mediaUriElement.Volume = ComputePlayerVolume(sliderValue);
                VolumeText.Text = $"{(int)sliderValue}";

                // Show corner notification
                MarqueeAPI.ShowVolumeLevel(sliderValue.ToString(), MarqueeDisplayDevice.PlayerScreen);

                // Update the volume icon based on the new value
                if (sliderValue == 0)
                {
                    VolumeIcon.Kind = PackIconKind.VolumeOff;
                    _isMuted = true;
                }
                else
                {
                    // Use the same icon for all audible levels for consistent visual size
                    VolumeIcon.Kind = PackIconKind.VolumeHigh;
                    if (_isMuted)
                    {
                        _isMuted = false;
                    }
                }
            }
            finally
            {
                _isUpdatingProgressSlider = false;
            }
        }

        /// <summary>
        /// Updates the visual state of the Vocal and Music buttons to show which is active.
        /// </summary>

        bool isMusicActive;
        private void UpdateVocalMusicButtonStates()
        {
            if (mediaUriElement == null || VocalBtn == null || MusicBtn == null)
            {
                return;
            }

            // For YouTube songs
            if (IsPlayingYoutube == true)
            {
                // toggle 2 states for Youtube
                if(mediaUriElement.AudioChannel == 0)
                {
                    VocalBtn.Background = _activeButtonBackground;
                    VocalBtn.Foreground = _activeButtonForeground;

                    MusicBtn.ClearValue(Button.BackgroundProperty);
                    MusicBtn.ClearValue(Button.ForegroundProperty);
                }
                else
                {
                    // Vocal is active, Music is inactive
                    MusicBtn.Background = _activeButtonBackground;
                    MusicBtn.Foreground = _activeButtonForeground;

                    VocalBtn.ClearValue(Button.BackgroundProperty);
                    VocalBtn.ClearValue(Button.ForegroundProperty);
                }
                return;
            }

                // Always stop any flashing animation before reapplying normal styles
                _vocalBtnFlashStoryboard?.Stop(VocalBtn);
                VocalBtn.ClearValue(OpacityProperty);

                // If a track is fixed, the Vocal button gets a special "locked" style.
                if (_isVocalTrackFixed)
                {
                    VocalBtn.Background = _fixedButtonBackground;
                    VocalBtn.Foreground = _activeButtonForeground;

                    // Music button is always inactive in this mode
                    MusicBtn.ClearValue(Button.BackgroundProperty);
                    MusicBtn.ClearValue(Button.ForegroundProperty);
                    isMusicActive = false; // Ensure state is correct

                    // Start the flashing animation on the button
                    _vocalBtnFlashStoryboard?.Begin(VocalBtn, true);
                    return; // Exit early
                }

                if (mediaUriElement.AudioStreams?.Count > 1)
                {
                    // Multi-track: active if current track is the designated music track
                    isMusicActive = (mediaUriElement.AudioTrack == PlayingFileMusicTrack);
                }
                else
                {
                    // Single-track: active if current channel is the designated music channel
                    isMusicActive = (mediaUriElement.AudioChannel == PlayingFileMusicTrack);
                }

            // Apply styles based on which button is active
            // Use cached brushes for performance to avoid UI thread stalls.
            if (isMusicActive)
            {
                // Music is active, Vocal is inactive
                MusicBtn.Background = _activeButtonBackground;
                MusicBtn.Foreground = _activeButtonForeground;

                VocalBtn.ClearValue(Button.BackgroundProperty);
                VocalBtn.ClearValue(Button.ForegroundProperty);
            }
            else
            {
                // Vocal is active, Music is inactive
                VocalBtn.Background = _activeButtonBackground;
                VocalBtn.Foreground = _activeButtonForeground;

                MusicBtn.ClearValue(Button.BackgroundProperty);
                MusicBtn.ClearValue(Button.ForegroundProperty);
            }
        }

        public void VocalBtn_Click(object sender, RoutedEventArgs e)
        {
            DebugLog($"VocalBtn_Click: audio track count={mediaUriElement.AudioStreams?.Count ?? 0} _isVocalTrackFixed={_isVocalTrackFixed}");

            // If track is fixed, a normal click on the vocal button will disable the fixed state.
            if (_isVocalTrackFixed)
            {
                AppLogger.Log("User action: Disabled fixed vocal track");
                _isVocalTrackFixed = false;
                _fixedAudioTrack = -1;
                // Stop the flashing animation and reset opacity before updating styles
                _vocalBtnFlashStoryboard?.Stop(VocalBtn);
                VocalBtn.ClearValue(OpacityProperty);
                UpdateVocalMusicButtonStates();
                MarqueeAPI.ShowStaticAnnouncement("已解除固定聲道", 2);
                return;
            }

            // For YouTube songs, simply set AudioChannel to 0
            // mediaUriElement.AudioChannel=0: Sterao
            // mediaUriElement.AudioChannel=1: Left 
            // mediaUriElement.AudioChannel=2: Right
            // mediaUriElement.AudioChannel=3: Vocal remove
            if (IsPlayingYoutube)
            {
                if (mediaUriElement != null)
                {
                    mediaUriElement.AudioChannel = 0;
                    DebugLog("YouTube VocalBtn_Click: Forced AudioChannel=0");
                    UpdateVocalMusicButtonStates();
                }
                return;
            }
            var sw = Stopwatch.StartNew();
            try
            {
                if (mediaUriElement == null)
                    return;

                if (mediaUriElement.AudioStreams?.Count == 1)
                {
                    // Only 1 audio channel, toggle between 1 and 2
                    if (PlayingFileMusicTrack == 1)
                        mediaUriElement.AudioChannel = 2;
                    else
                        mediaUriElement.AudioChannel = 1;

                    DebugLog($"        1 audio tracks mode. new Track ={mediaUriElement.AudioTrack}, AudioChannel={mediaUriElement.AudioChannel}");
                    AppLogger.Log($"        1 audio tracks mode. new Track ={mediaUriElement.AudioTrack}, AudioChannel={mediaUriElement.AudioChannel}");
                }
                else if (mediaUriElement.AudioStreams?.Count == 2 && (!isMusicActive))
                {
                    DebugLog($"        2 audio tracks mode. current audio track is {mediaUriElement.AudioTrack} ");

                    if (mediaUriElement.AudioTrack == PlayingFileMusicTrack)
                    {
                        if (mediaUriElement.AudioStreams[0] == mediaUriElement.AudioTrack)
                            mediaUriElement.AudioTrack = mediaUriElement.AudioStreams[1];
                        else
                            mediaUriElement.AudioTrack = mediaUriElement.AudioStreams[0];
                    }

                    DebugLog($"        new track is {mediaUriElement.AudioTrack}");
                    AppLogger.Log($"        new track is {mediaUriElement.AudioTrack}");
                }
                else
                {
                    // Cycle through available audio tracks
                    var audioTracks = mediaUriElement.AudioStreams;
                    if (audioTracks == null || audioTracks.Count < 2) // Need at least 2 tracks to cycle
                    {
                        DebugLog($"        Multi-track mode. Not enough tracks to cycle ({audioTracks?.Count ?? 0}).");
                        return;
                    }

                    // Find the index of the current audio channel
                    var currentIndex = audioTracks.IndexOf(mediaUriElement.AudioTrack);

                    // If not found, it's the first click, so start from the music track
                    if (currentIndex == -1)
                    {
                        currentIndex = audioTracks.IndexOf(PlayingFileMusicTrack);
                    }

                    // This should not happen if the list is correct, but as a fallback
                    if (currentIndex == -1)
                    {
                        currentIndex = 0;
                    }

                    // Get the next index, wrapping around
                    var nextIndex = (currentIndex + 1) % audioTracks.Count;

                    // Set the new audio channel
                    mediaUriElement.AudioTrack = audioTracks[nextIndex];
                    DebugLog($"        Multi-track mode. new tracks is {mediaUriElement.AudioTrack}");
                    AppLogger.Log($"        Multi-track mode. new tracks is {mediaUriElement.AudioTrack}");
                }
                UpdateVocalMusicButtonStates();
            }
            finally
            {
                sw.Stop();
                DebugLog($"VocalBtn_Click completed in {sw.ElapsedMilliseconds}ms");
            }
        }

        public void MusicBtn_Click(object sender, RoutedEventArgs e)
        {
            // For YouTube songs, simply set AudioChannel to 3
            if (IsPlayingYoutube && mediaUriElement != null)
            {
                mediaUriElement.AudioChannel = 3;
                DebugLog("YouTube MusicBtn_Click: Forced AudioChannel=3");
                UpdateVocalMusicButtonStates();
                return;
            }
            var sw = Stopwatch.StartNew();
            try
            {
                // If track is fixed, a click on the music button will also disable the fixed state.
                if (_isVocalTrackFixed)
                {
                    AppLogger.Log("User action: Disabled fixed vocal track via music button");
                    _isVocalTrackFixed = false;
                    _fixedAudioTrack = -1;
                    // Stop the flashing animation and reset opacity before updating styles
                    _vocalBtnFlashStoryboard?.Stop(VocalBtn);
                    VocalBtn.ClearValue(OpacityProperty);
                    UpdateVocalMusicButtonStates();
                    MarqueeAPI.ShowStaticAnnouncement("已解除固定聲道", 2);
                    // Do not proceed to change the track, just disable the mode.
                    return;
                }

                if (mediaUriElement == null)
                    return;

                int audioStreamsCount = mediaUriElement.AudioStreams?.Count ?? 0;
                DebugLog($"MusicBtn_Click: AudioTrackList.Count={audioStreamsCount}, AudioChannel={mediaUriElement.AudioChannel}");
                AppLogger.Log($"MusicBtn_Click: AudioTrackList.Count={audioStreamsCount}, AudioChannel={mediaUriElement.AudioChannel}");

                if (isMusicActive)
                    return;

                if (audioStreamsCount <= 1)
                {
                    // Single-track: Set the audio channel to the music channel
                    mediaUriElement.AudioChannel = PlayingFileMusicTrack;
                }
                else
                {
                    // Multi-track: Set the audio track to the music track
                    if (mediaUriElement.AudioTrack != PlayingFileMusicTrack)
                        mediaUriElement.AudioTrack = PlayingFileMusicTrack;
                }
                UpdateVocalMusicButtonStates();
            }
            finally
            {
                sw.Stop();
                DebugLog($"MusicBtn_Click completed in {sw.ElapsedMilliseconds}ms");
            }
        }

        #region VocalBtn and MusicBtn Long Press and Right Click Handlers

        private DispatcherTimer? _vocalBtnLongPressTimer;
        private DispatcherTimer? _musicBtnLongPressTimer;
        private bool _vocalBtnLongPressTriggered = false;
        private bool _musicBtnLongPressTriggered = false;

        private void VocalBtn_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _vocalBtnLongPressTriggered = false;

                // Start long press timer (500ms)
                _vocalBtnLongPressTimer = new DispatcherTimer();
                _vocalBtnLongPressTimer.Interval = TimeSpan.FromMilliseconds(500);
                _vocalBtnLongPressTimer.Tick += (s, args) =>
                {
                    _vocalBtnLongPressTimer?.Stop();
                    _vocalBtnLongPressTriggered = true;
                    VocalBtn_LongPress();
                };
                _vocalBtnLongPressTimer.Start();
            }
        }

        private void VocalBtn_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _vocalBtnLongPressTimer?.Stop();

            // If long press was triggered, prevent the normal click
            if (_vocalBtnLongPressTriggered)
            {
                e.Handled = true;
                _vocalBtnLongPressTriggered = false;
            }
        }

        // Music button: start long-press detection on mouse down
        private void MusicBtn_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _musicBtnLongPressTriggered = false;

                _musicBtnLongPressTimer?.Stop();
                _musicBtnLongPressTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _musicBtnLongPressTimer.Tick += async (s, args) =>
                {
                    _musicBtnLongPressTimer?.Stop();
                    _musicBtnLongPressTriggered = true;
                    await MusicBtn_LongPress();
                };
                _musicBtnLongPressTimer.Start();
            }
        }

        // Music button: stop long-press timer on mouse up and swallow click if long-press already handled
        private void MusicBtn_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _musicBtnLongPressTimer?.Stop();
            if (_musicBtnLongPressTriggered)
            {
                e.Handled = true;
                _musicBtnLongPressTriggered = false;
            }
        }

        private async void VocalBtn_LongPress()
        {
            AppLogger.Log("User action: Vocal button long press");
            DebugLog("VocalBtn_LongPress triggered");

            // Long press action: Show option to set current channel as music track
            if (mediaUriElement != null)
            {
                await ShowSetAsMusicTrackDialog();
            }
        }

        private async void VocalBtn_RightClick(object sender, MouseButtonEventArgs e)
        {
            AppLogger.Log("User action: Vocal button right click");
            DebugLog("VocalBtn_RightClick triggered");

            // Right click action: Show option to set current channel as music track
            if (mediaUriElement != null)
            {
                await ShowSetAsMusicTrackDialog();
            }
        }

        private Task ShowSetAsMusicTrackDialog()
        {
            try
            {
                var currentTrack = mediaUriElement.AudioTrack;
                DebugLog($"ShowSetAsMusicTrackDialog: Current AudioTrack={currentTrack}, Current PlayingFileMusicTrack={PlayingFileMusicTrack}");

                // Create a popup positioned near the VocalBtn
                var popup = new System.Windows.Controls.Primitives.Popup
                {
                    PlacementTarget = VocalBtn,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                    AllowsTransparency = true,
                    StaysOpen = false
                };

                // Create the dialog content with reduced padding
                var dialogBorder = new Border
                {
                    Background = new SolidColorBrush(Colors.White),
                    BorderBrush = new SolidColorBrush(Colors.Gray),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Height = 44, // Match button height + margins
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = Colors.Black,
                        Direction = 315,
                        ShadowDepth = 5,
                        Opacity = 0.3
                    }
                };

                // Button panel - now the main content
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Height = 40,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4) // Small margin for the buttons
                };

                var setAsMusicButton = new Button
                {
                    Content = "設定此聲道為伴唱",
                    Margin = new Thickness(0, 0, 8, 0)
                };
                TextSettingsHandler.ApplyOutlinedButtonStyle(setAsMusicButton, 18);

                var setFixeTrackButton = new Button
                {
                    Content = "固定此聲道播放",
                    Margin = new Thickness(0, 0, 8, 0)
                };
                TextSettingsHandler.ApplyOutlinedButtonStyle(setFixeTrackButton, 18);

                var cancelButton = new Button
                {
                    Content = "取消",
                    Margin = new Thickness(0, 0, 8, 0)
                };
                TextSettingsHandler.ApplyOutlinedButtonStyle(cancelButton, 18);

                buttonPanel.Children.Add(setAsMusicButton);
                buttonPanel.Children.Add(cancelButton);
                buttonPanel.Children.Add(setFixeTrackButton);

                dialogBorder.Child = buttonPanel;
                popup.Child = dialogBorder;

                bool? result = null;

                setAsMusicButton.Click += (s, e) =>
                {
                    AppLogger.Log($"User action: Set audio track {currentTrack} as music track");
                    result = true;
                    popup.IsOpen = false;
                };

                setFixeTrackButton.Click += (s, e) =>
                {
                    AppLogger.Log($"User action: Fix audio track to {currentTrack}");
                    // Set the fixed track state and close the popup
                    _isVocalTrackFixed = true;
                    _fixedAudioTrack = currentTrack;
                    result = null; // Indicate a different action from 'set as music'
                    popup.IsOpen = false;
                };

                cancelButton.Click += (s, e) =>
                {
                    result = false;
                    popup.IsOpen = false;
                };

                // Handle popup closed event
                popup.Closed += (s, e) =>
                {
                    // Process result after popup closes
                    if (result == true) // "Set as Music" was clicked
                    {
                        DebugLog($"User confirmed: Setting AudioTrack {currentTrack} as PlayingFileMusicTrack");

                        // Set current audio track as the music track
                        if (mediaUriElement.AudioStreams?.Count <= 1)
                        {
                            PlayingFileMusicTrack = (PlayingFileMusicTrack == 1) ? 2 : 1;
                            if(PlayingFileMusicTrack==1)
                                MarqueeAPI.ShowStaticAnnouncement($"已設定左聲道為伴唱", 5);
                            else
                                MarqueeAPI.ShowStaticAnnouncement($"已設定右聲道為伴唱", 5);
                        }
                        else
                        {
                            PlayingFileMusicTrack = currentTrack;
                            MarqueeAPI.ShowStaticAnnouncement($"已設定聲道 {currentTrack} 為伴唱", 5);
                        }
                        // Call MusicBtn_Click to switch as music mode      
                        MusicBtn_Click(this, new RoutedEventArgs());

                        // If sync is enabled, update the database
                        if (_playingSongData != null)
                        {
                            if (_playingSongData.TryGetValue("Song_Id", out var idObj) && idObj != null)
                            {
                                string songId = idObj.ToString()!;
                                int newTrack = PlayingFileMusicTrack;

                                // Update in-memory data
                                _playingSongData["Song_Track"] = newTrack;

                                // Persist change to database asynchronously
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrazySong.mdb");
                                        string sql = "UPDATE ktv_Song SET Song_Track = ? WHERE Song_Id = ?";
                                        var parameters = new OleDbParameter[] { new OleDbParameter("?", newTrack), new OleDbParameter("?", songId) };
                                        DbHelper.Access.ExecuteNonQuery(dbPath, sql, null, parameters);
                                        AppLogger.Log($"DB Write: Updated Song_Track to {newTrack} for Song_Id '{songId}'");
                                        DebugLog($"Successfully saved new Song_Track ({newTrack}) for Song_Id '{songId}' to the database.");
                                    }
                                    catch (Exception ex)
                                    {
                                        DebugLog($"Failed to save Song_Track to database: {ex.Message}");
                                        AppLogger.LogError($"DB Write failed: Song_Track update for Song_Id '{songId}'", ex);
                                    }
                                });
                            }
                        }

                        DebugLog($"PlayingFileMusicTrack updated to: {PlayingFileMusicTrack}");
                    }
                    else if (result == false) // "Cancel" was clicked
                    {
                        DebugLog("User cancelled the dialog");
                    }
                    else // "Fix Track" was clicked (result is null)
                    {
                        DebugLog($"User confirmed: Fixing AudioTrack to {currentTrack}");
                        MarqueeAPI.ShowStaticAnnouncement($"已固定聲道 {currentTrack}", 5);

                        // Update button visuals to reflect the new "fixed" state
                        UpdateVocalMusicButtonStates();
                    }
                };

                // Show the popup
                popup.IsOpen = true;
            }
            catch (Exception ex)
            {
                DebugLog($"Error in ShowSetAsMusicTrackDialog: {ex.Message}");
                AppLogger.LogError("Error in ShowSetAsMusicTrackDialog", ex);
                MarqueeAPI.ShowStaticAnnouncement("設定失敗", 2);
            }
            return Task.CompletedTask;
        }

        private Task MusicBtn_LongPress()
        {
            DebugLog("MusicBtn_LongPress triggered");

            // Long press action: Show music volume adjustment or advanced music settings
            if (mediaUriElement != null)
            {
                // You can add specific long press functionality here
                // For example: Open music settings dialog, adjust music volume, etc.
            }
            return Task.CompletedTask;
        }

        private void MusicBtn_RightClick(object sender, MouseButtonEventArgs e)
        {
            DebugLog("MusicBtn_RightClick triggered");

            // Right click action: Show music options menu or reset music settings
            if (mediaUriElement != null)
            {
                // You can add specific right click functionality here
                // For example: Show context menu, reset music settings, etc.
            }
        }

        #endregion
        
        private void InitializeVocalButtonFlashAnimation()
        {
            _vocalBtnFlashStoryboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.4, // A visible but noticeable dimming
                Duration = new Duration(TimeSpan.FromMilliseconds(600)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
            _vocalBtnFlashStoryboard.Children.Add(animation);
        }

        // New: centralized, reusable volume mapper for project-wide usage.
        // Use VolumeMapper.ComputeFromPercent(0..100) or ComputeFromNormalized(0.0..1.0).
        // Tuning knobs are public to make live adjustments easy.
        public static class VolumeMapper
        {
            public enum Mode { Linear, Exponential, Logarithmic }

            // Public knobs for easy tuning from other parts of the app or tests.
            public static Mode MappingMode { get; set; } = Mode.Exponential;
            public static double Exponent { get; set; } = 0.1; // <1 gives softer low end, >1 compresses low end
            public static double MinDb { get; set; } = -10.0;  // for logarithmic mapping, e.g. -60..0

            // Input: slider percent 0..100 -> output: 0.0..1.0 (rounded to 3 decimals)
            public static double ComputeFromPercent(double sliderPercent)
            {
                if (sliderPercent <= 0) return 0.0;
                if (sliderPercent >= 100) return 1.0;
                double normalized = sliderPercent / 100.0;
                return ComputeFromNormalized(normalized);
            }

            // Input: normalized 0.0..1.0 -> output: 0.0..1.0 (rounded to 3 decimals)
            public static double ComputeFromNormalized(double normalized)
            {
                if (normalized <= 0) return 0.0;
                if (normalized >= 1.0) return 1.0;

                double result;
                switch (MappingMode)
                {
                    case Mode.Linear:
                        result = normalized;
                        break;

                    case Mode.Exponential:
                        // Use exponent to shape curve. Normalize by dividing by max value to ensure range 0.0~1.0
                        // When exponent < 1, raises values (softer low end). When > 1, compresses values.
                        double maxExpValue = Math.Pow(1.0, Exponent); // This is always 1.0, but kept for clarity
                        result = Math.Pow(normalized, Exponent) / maxExpValue;
                        break;

                    case Mode.Logarithmic:
                    default:
                        // Map normalized to dB range [MinDb .. 0], then convert to linear amplitude.
                        // Normalize the result to ensure output range is 0.0~1.0
                        double db = MinDb + (normalized * (-MinDb));
                        double linearValue = Math.Pow(10.0, db / 20.0);
                        
                        // Calculate min and max linear values for normalization
                        double minLinear = Math.Pow(10.0, MinDb / 20.0);
                        double maxLinear = 1.0; // Math.Pow(10.0, 0 / 20.0) = 1.0
                        
                        // Normalize to 0.0~1.0 range
                        result = (linearValue - minLinear) / (maxLinear - minLinear);
                        break;
                }

                // Ensure value is clamped and rounded once here for consistent usage across project.
                result = Math.Max(0.0, Math.Min(1.0, result));
//                DebugLog($"Volume change to {Math.Round(result, 3)}");
                return Math.Round(result, 3);
            }
        }

        // Keep existing private helper but forward to the centralized VolumeMapper.
        private double ComputePlayerVolume(double sliderPercent)
        {
            return VolumeMapper.ComputeFromPercent(sliderPercent);
        }

        /// <summary>
        /// Event handler for when the volume lock toggle is checked.
        /// Stores the current volume to be applied to subsequent songs.
        /// </summary>
        private void VolumeLockToggle_Checked(object sender, RoutedEventArgs e)
        {
            // Store the current volume when lock is activated
            _lockedVolume = (int)VolumeSlider.Value;
            AppLogger.Log($"User action: Volume lock activated at {_lockedVolume}");
            DebugLog($"Volume lock activated. Locked volume: {_lockedVolume}");
            MarqueeAPI.ShowStaticAnnouncement($"音量已鎖定在 {_lockedVolume}", 2);
        }

        /// <summary>
        /// Event handler for when the volume lock toggle is unchecked.
        /// Releases the volume lock so songs can use their individual volume settings.
        /// </summary>
        private void VolumeLockToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Volume lock deactivated");
            DebugLog("Volume lock deactivated.");
            MarqueeAPI.ShowStaticAnnouncement("音量鎖定已解除", 2);
        }

        public void VolumeUp()
        {
            double newValue = Math.Min(100, VolumeSlider.Value + 5);
            VolumeSlider.Value = newValue;
            AppLogger.Log($"Remote action: Volume Up to {newValue}");
        }

        public void VolumeDown()
        {
            double newValue = Math.Max(0, VolumeSlider.Value - 5);
            VolumeSlider.Value = newValue;
            AppLogger.Log($"Remote action: Volume Down to {newValue}");
        }

        public void ToggleVolumeLock()
        {
            bool newState = !(VolumeLockToggle.IsChecked ?? false);
            VolumeLockToggle.IsChecked = newState;
            AppLogger.Log($"Remote action: Volume Lock toggled to {newState}");
        }
    }
}