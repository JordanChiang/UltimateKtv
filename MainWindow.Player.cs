using System;
using UltimateKtv.Playback;
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
        // 人聲/伴唱切換 (伴唱位置、固定人聲)
        private VocalSwitch _vocal = null!;

        /// <summary>VocalSwitch 眼中的 player 音軌/聲道。</summary>
        private sealed class PlayerAudioTracks : IAudioTracks
        {
            public IReadOnlyList<int> Streams => MediaHostManager.Instance.Player.AudioStreams;
            public int Track { get => MediaHostManager.Instance.Player.AudioTrack; set => MediaHostManager.Instance.Player.AudioTrack = value; }
            public int Channel { get => MediaHostManager.Instance.Player.AudioChannel; set => MediaHostManager.Instance.Player.AudioChannel = value; }
        }
        
        // LRC fields
        private List<LrcLine>? _currentLyrics;
        private int _currentLyricIndex = -1;

        private void InitializeMediaPlayer()
        {
            try
            {
                var sharedMediaPlayer = MediaHostManager.Instance.Player;

                // 開檔/播完/失敗交給 PlaybackSession；其餘事件由主視窗直接處理
                sharedMediaPlayer.MediaPositionChanged += MediaUriElement_MediaPositionChanged;
                // libmpv：電視畫面是原生視窗，滑鼠移動/雙擊由引擎回報 (DirectShow 不會觸發)
                sharedMediaPlayer.VideoMouseActivity += Player_VideoMouseActivity;
                sharedMediaPlayer.VideoDoubleClick += Player_VideoDoubleClick;
                DebugLog("Media player event handlers wired up.");
            }
            catch (Exception ex)
            {
                DebugLog($"Error initializing media player: {ex.Message}\n{ex}");
            }
        }

        private void Player_VideoMouseActivity(object? sender, EventArgs e)
        {
            ResetCursorIdleTimer();
        }

        private void Player_VideoDoubleClick(object? sender, EventArgs e)
        {
            // 與 VideoDisplayWindow 雙擊相同：單螢幕模式回到主控台
            if (IsSingleMonitorMode && _videoDisplayWindow != null)
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
                ShowFromVideoWindow();
            }
        }

        // 播放場次：待播清單、正在播放的歌、何時播下一首都由它決定
        private PlaybackSession _playback = null!;

        /// <summary>建立播放場次並接上主視窗的反應；必須在 UI 執行緒、使用待播清單之前呼叫。</summary>
        private void InitializePlaybackSession()
        {
            _vocal = new VocalSwitch(new PlayerAudioTracks());
            _playback = new PlaybackSession(
                new PlayerTarget(this),
                path => Task.Run(() =>
                {
                    try { return File.Exists(path); }
                    catch { return false; }
                }));

            _playback.QueueChanged += (_, _) => UpdateWaitingListDisplay();
            _playback.SongStarting += Playback_SongStarting;
            _playback.SongOpened += Playback_SongOpened;
            _playback.SongEnded += Playback_SongEnded;
            _playback.BecameIdle += Playback_BecameIdle;
        }

        /// <summary>
        /// PlaybackSession 與 UKtvPlayer 之間的 adapter：開檔前套用設定/音量，並把 player 事件帶回 UI 執行緒。
        /// </summary>
        private sealed class PlayerTarget : IPlaybackTarget
        {
            private readonly MainWindow _window;
            // libmpv 在 MediaOpened 回呼結束後才取消暫停；回呼中 (開檔中被切歌) 若立刻換歌，
            // 新歌會在套用聲道/音量前就開始播，所以延後到回呼結束後再開檔/停止
            private bool _raisingOpened;

            public PlayerTarget(MainWindow window)
            {
                _window = window;
                var player = MediaHostManager.Instance.Player;
                player.MediaOpened += (_, _) => OnUiThread(() =>
                {
                    _raisingOpened = true;
                    try { Opened?.Invoke(this, EventArgs.Empty); }
                    finally { _raisingOpened = false; }
                });
                player.MediaEnded += (_, _) => OnUiThread(() => Ended?.Invoke(this, EventArgs.Empty));
                // libmpv 的 MediaFailed 可能從背景執行緒觸發
                player.MediaFailed += (_, e) => OnUiThread(() => Failed?.Invoke(this, e.Exception));
            }

            public event EventHandler? Opened;
            public event EventHandler? Ended;
            public event EventHandler<Exception?>? Failed;

            public void Open(QueuedSong song)
            {
                if (_raisingOpened)
                {
                    _window.Dispatcher.BeginInvoke(() => Open(song));
                    return;
                }

                var w = _window;

                // Stop current playback and start the new one
                SafeStop(w.ktvPlayer, "ktvPlayer");

                // Set the volume slider to the song's specific setting (volume lock: keep the locked value)
                w.VolumeSlider.Value = w.VolumeLockToggle.IsChecked != true ? song.Volume : w._lockedVolume;

                // Log if using pre-loaded file content
                if (SettingsManager.Instance.CurrentSettings.EnablePreLoading &&
                    w._preLoadedSongPath == song.FilePath && w._preLoadedFileContent != null)
                {
                    AppLogger.Log($"Pre-loading: Using cached file ({w.FormatBytes(w._preLoadedMemoryUsage)})");
                }

                var settings = SettingsManager.Instance.CurrentSettings;
                w.ktvPlayer.Stretch = settings.IsMediaFullScreen ? Stretch.Fill : Stretch.Uniform;
                w.ktvPlayer.ApplySettings(settings); // renderer / audio device / HW accel / codec directory

                w.ktvPlayer.Source = new Uri(song.FilePath); // This triggers MediaOpened/MediaFailed.
                DebugLog($"Main media Source set to: {song.FilePath}");
            }

            public void Stop()
            {
                if (_raisingOpened)
                {
                    _window.Dispatcher.BeginInvoke(Stop);
                    return;
                }
                SafeStop(_window.ktvPlayer, "ktvPlayer");
            }

            private void OnUiThread(Action action)
            {
                if (_window.Dispatcher.CheckAccess()) action();
                else _window.Dispatcher.BeginInvoke(action);
            }
        }

        private void Playback_SongStarting(object? sender, QueuedSong song)
        {
            SetPlayerControlsEnabled(false); // Disable controls during transition
            IsPlayingYoutube = song.IsYoutube;
            PlayingFilePath = song.FilePath;
            SetPauseButtonResume(false);

            if (song.IsYoutube)
            {
                // For YouTube, create a stand-in Song (not part of the song library)
                _playingSongData = new Song
                {
                    Id = song.SongId,
                    Name = song.SongName,
                    Singer = song.SingerName,
                    Lang = "Youtube",
                    FilePath = song.FilePath,
                    Volume = song.Volume,
                    Track = song.AudioTrack,
                };
            }
            else
            {
                // Get song metadata from database cache
                _playingSongData = SongDatas.SongData?.FirstOrDefault(s => s.FilePath == song.FilePath);
            }

            UpdateCurrentPlayingDisplay();
        }

        private void Playback_SongEnded(object? sender, SongEndedEventArgs e)
        {
            DebugLog($"[MainPlayer] Song ended ({e.Reason}): {e.Song.SongName}");

            // 播完/切歌/循環時保存使用者調整的音量 (失敗、被切掉的隨機歌不存)
            if (e.Reason is SongEndReason.Finished or SongEndReason.Skipped or SongEndReason.Replayed)
            {
                SaveCurrentSongVolume();
            }

            try
            {
                RecordingManager.Instance.StopRecording();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Error stopping recording on song end", ex);
            }

            if (e.Reason is SongEndReason.Finished or SongEndReason.Skipped or SongEndReason.Replayed)
            {
                // Stop all marquees when media ends
                MarqueeAPI.StopAll();
            }

            // Clear Lyrics
            _currentLyrics = null;
            _currentLyricIndex = -1;
            _videoDisplayWindow?.UpdateLyrics("", Visibility.Collapsed);

            if (e.Reason != SongEndReason.Replayed)
            {
                IsPlayingYoutube = false;
                _playingSongData = null;
            }

            UpdateCurrentPlayingDisplay();
            SetPlayerControlsEnabled(true); // Re-enable controls so the user can try another action
        }

        private void Playback_BecameIdle(object? sender, EventArgs e)
        {
            DebugLog("PlaybackSession: No songs in waiting list.");
            IsPlayingYoutube = false;
            _playingSongData = null;
            UpdateCurrentPlayingDisplay();

            try
            {
                RecordingManager.Instance.StopRecording();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Error stopping recording when playback became idle", ex);
            }

            SetPlayerControlsEnabled(true); // Keep controls interactive when idle
            SetPauseButtonResume(false); // Reset pause button text

            // Clear pre-loaded cache when list is empty
            ClearPreLoadingCache();

            // Start a delayed random play check if enabled
            StartDelayedRandomPlayWhenEmpty();
        }

        private void Playback_SongOpened(object? sender, QueuedSong song)
        {
            AppLogger.Log($"mediaUriElement_MediaOpened");
            ThreadPool.QueueUserWorkItem(_ => ResourceMonitor.LogSnapshot("Song started"));
            DebugLog($"mediaUriElement_MediaOpened");
            // This is the correct and safe place to configure and start playback.
            var player = ktvPlayer;

            if (_playingSongData == null)
            {
                DebugLog($"     but _playingSongData = null");
                SetPlayerControlsEnabled(true);
                return;
            }

            try
            {
                // Only update the volume from the song's data if the volume lock is NOT active.
                if (VolumeLockToggle.IsChecked != true)
                {
                    if (_playingSongData.Volume is int dbVolume)
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
                    DebugLog($"     failed to set player.Volume={PlayingFileVolume} ");
                }

                // Apply the global AudioAmplify setting from the options panel
                player.AudioAmplify = SettingsManager.Instance.CurrentSettings.AudioAmplify;
                DebugLog($"     AudioAmplify= {player.AudioAmplify}, VideoRendererType: {player.VideoRendererName}");

                // 伴唱位置、預設伴唱、固定人聲：由 VocalSwitch 依歌庫的 Song_Track 決定
                int? songTrack = _playingSongData.Track;
                _vocal.ApplyForNewSong(
                    songTrack,
                    SettingsManager.Instance.CurrentSettings.IsLegacyAudioChannelDefinitionEnabled,
                    IsPlayingYoutube,
                    SettingsManager.Instance.CurrentSettings.YoutubeDefaultNoVocal);
                DebugLog($"     Audio: music={_vocal.MusicTrack}, fixed={_vocal.FixedTrack}, track={player.AudioTrack}, channel={player.AudioChannel}, streams={player.AudioStreams.Count}");

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
                var songName = _playingSongData.Name;
                var singerName = _playingSongData.Singer;
                
                try
                {
                    RecordingManager.Instance.StartRecording(songName, singerName, song.IsRandom);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Error starting recording in mediaUriElement_MediaOpened", ex);
                }

                // Stop any leftover marquee/static text on console screen since NowPlayingBorder displays current song info
                MarqueeAPI.Stop(MarqueeDisplayDevice.ConsoleScreen);
                SetPauseButtonResume(false);
                UpdateCurrentPlayingDisplay();
                // Display marquee on player display device
                if (_videoDisplayWindow != null)
                {
                    var next = _playback.Snapshot.FirstOrDefault();
                    MarqueeAPI.ShowSongPlaybackMarquee(songName, singerName, next?.SongName, next?.SingerName, song.IsRandom, song.OrderedBy, next?.OrderedBy, MarqueeDisplayDevice.PlayerScreen);

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
                ApplyRandomPlayAudioChannel(song);

                // Clear today's play list on first song, then record this song play
                // no needed for random play
                var songId = _playingSongData.Id;
                if (!string.IsNullOrEmpty(songId) && (!song.IsRandom))
                {
                    if (IsPlayingYoutube)
                    {
                        // PlayCount is incremented in the position timer once PlayCountUpdatePercentage is reached, like local songs
                        long ticks = player.MediaDuration;
                        int lengthSeconds = ticks > 0 ? (int)TimeSpan.FromTicks(ticks).TotalSeconds : 0;
                        if (lengthSeconds > 0)
                        {
                            Task.Run(() =>
                            {
                                if (SongDatas.UpdateYoutubeSongLength(songId, lengthSeconds))
                                {
                                    Dispatcher.BeginInvoke(() => RefreshYoutubeSongLengthInLists(songId, lengthSeconds));
                                }
                            });
                        }
                    }
                    else
                    {
                        if (_isFirstSongInSession)
                        {
                            ClearTodayPlayList();
                            _isFirstSongInSession = false;
                        }
                        RecordSongPlay(songId);
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLog($"[MainPlayer] mediaUriElement_MediaOpened exception: {ex.Message}");
                AppLogger.LogError("[MainPlayer] mediaUriElement_MediaOpened exception", ex);
            }
            finally
            {
                // Reset the play count increment flag for the new song.
                _playCountIncrementedForCurrentSong = false;
                DebugLog("Play count increment flag reset for new song.");
                SetPlayerControlsEnabled(true);
                SetPauseButtonResume(false); // Ensure pause button text is correct for new song
            }
        }

        private void MediaUriElement_MediaPositionChanged(object? sender, EventArgs e)
        {
            // This event fires repeatedly as the media plays.
            if (ktvPlayer != null && ktvPlayer.HasVideo)
            {
                try
                {
                    _isUpdatingProgressSlider = true;

                    var currentPositionTicks = ktvPlayer.MediaPosition;
                    var totalDurationTicks = ktvPlayer.MediaDuration;

                    // Convert ticks to TimeSpan
                    var currentPosition = TimeSpan.FromTicks(currentPositionTicks);
                    var totalDuration = TimeSpan.FromTicks(totalDurationTicks);

                    // Update time displays only when text actually changes (avoids constant relayout on every tick)
                    if (!_isUserDraggingProgressSlider)
                    {
                        string currentFormatted = FormatTime(currentPosition);
                        if (CurrentTimeText.Text != currentFormatted)
                        {
                            CurrentTimeText.Text = currentFormatted;
                        }
                    }

                    if (totalDurationTicks > 0)
                    {
                        string totalFormatted = FormatTime(totalDuration);
                        if (TotalTimeText.Text != totalFormatted)
                        {
                            TotalTimeText.Text = totalFormatted;
                        }

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
                                _currentLyricIndex++;
                            }
                        }

                        // Calculate progress percentage (used for both slider and play count check)
                        var progressPercentage = ((double)currentPositionTicks / totalDurationTicks) * 100.0;

                        // Update progress slider only if user is not dragging and progress moved noticeably (>= 0.25%)
                        if (!_isUserDraggingProgressSlider)
                        {
                            double newSliderVal = Math.Max(0, Math.Min(100, progressPercentage));
                            if (Math.Abs(VideoProgressSlider.Value - newSliderVal) >= 0.25)
                            {
                                VideoProgressSlider.Value = newSliderVal;
                            }
                        }

                        // Increment play count if position more than user criteria and not already incremented
                        // no needed for random play
                        if (!_playCountIncrementedForCurrentSong && progressPercentage > PlayCountUpdatePercentage && (_playback.Current?.IsRandom != true))
                        {
                            if (!IsPlayingYoutube && _playingSongData != null)
                            {
                                int newPlayCount = _playingSongData.PlayCount + 1;
                                _playingSongData.PlayCount = newPlayCount;
                                _playCountIncrementedForCurrentSong = true;
                                DebugLog($"In-memory play count for '{_playingSongData.Name}' incremented to {newPlayCount}.");

                                // Persist the change to the database asynchronously
                                if (!string.IsNullOrEmpty(_playingSongData.Id))
                                {
                                    string songId = _playingSongData.Id;
                                    Task.Run(() =>
                                    {
                                        try
                                        {
                                            SongLibrary.SetPlayCount(songId, newPlayCount);
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
                            else if (IsPlayingYoutube && _playingSongData != null && !string.IsNullOrEmpty(_playingSongData.Id))
                            {
                                _playCountIncrementedForCurrentSong = true;
                                string ytSongId = _playingSongData.Id;
                                string songNameForLog = _playingSongData.Name.Length > 0 ? _playingSongData.Name : "Unknown";
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        // Song_Id is the history Id (Y0017) for desktop orders but the video ID for web orders
                                        int rows = SongLibrary.IncrementYoutubePlayCount(ytSongId);
                                        AppLogger.Log($"DB Write: Incremented PlayCount for Youtube '{songNameForLog}' ({ytSongId}), rows affected={rows}");
                                    }
                                    catch (Exception ex)
                                    {
                                        AppLogger.LogError($"DB Write failed: PlayCount update for '{songNameForLog}'", ex);
                                    }
                                });
                            }
                            else
                            {
                                // Still set flag to true to avoid repeated attempts on invalid data
                                _playCountIncrementedForCurrentSong = true;
                                DebugLog("Could not increment play count: _playingSongData is null or has no Song_Id.");
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

            if (!string.IsNullOrEmpty(_playingSongData.Id))
            {
                string songId = _playingSongData.Id;
                int currentVolume = (int)VolumeSlider.Value;
                
                // Compare with original PlayingFileVolume
                if (currentVolume != PlayingFileVolume)
                {
                    DebugLog($"User changed volume from {PlayingFileVolume} to {currentVolume}. Saving to DB for Song_Id '{songId}'.");
                    
                    // update in memory DB
                    var memorySong = SongDatas.FindSongById(songId);
                    if (memorySong != null)
                    {
                        memorySong.Volume = currentVolume;
                    }                    

                    // Update DB asynchronously
                    Task.Run(() =>
                    {
                        try
                        {
                            SongLibrary.SetVolume(songId, currentVolume);
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

        /// <summary>
        /// 判斷目前是否有歌曲正在播放或處於載入播放流程中（含暫停中）
        /// </summary>
        private bool IsSongPlaybackActive => ktvPlayer?.Source != null || _playback.Current != null;

        private void SkipSong_Click(object sender, RoutedEventArgs e) => SkipCurrentSong();

        /// <summary>切歌：主畫面切歌鈕與手機共用；解除暫停與單曲循環後播下一首。</summary>
        public void SkipCurrentSong()
        {
            try
            {
                // 只有在歌曲播放（或載入中）時才真正作動
                if (!IsSongPlaybackActive)
                {
                    DebugLog("SkipCurrentSong: No song is currently playing, ignoring.");
                    return;
                }

                AppLogger.Log("User action: Skip song");
                DebugLog("SkipCurrentSong: Skip song");

                SetPauseButtonResume(false);
                if (_isLoopPlay)
                {
                    SetLoopPlay(false);
                }

                if (_playback.Current != null)
                {
                    // 存音量、停錄音、停跑馬燈在 SongEnded 處理；開檔中則等開檔完成再切
                    _playback.Skip();
                }
                else
                {
                    // 不是從待播清單播放的影片：直接停止
                    SafeStop(ktvPlayer, nameof(ktvPlayer));
                }
            }
            catch (Exception ex)
            {
                DebugLog($"SkipCurrentSong: Error skipping song: {ex.Message}\n{ex}");
                AppLogger.LogError("SkipCurrentSong: Error skipping song", ex);
            }
        }

        private bool _pauseBtnShowsResume;

        /// <summary>
        /// 暫停鈕文字 (暫停/繼續) 以資源參照設定，切換介面語言時跟著更新；狀態另存，不再比對按鈕文字。
        /// </summary>
        private void SetPauseButtonResume(bool resume)
        {
            _pauseBtnShowsResume = resume;
            PauseBtn?.SetResourceReference(ContentControl.ContentProperty, Loc.ResourcePrefix + (resume ? "Player.Resume" : "Player.Pause"));
        }

        private void Pause_Click(object sender, RoutedEventArgs e)
        {
            // Pause/Resume toggle
            try
            {
                // 只有在歌曲播放（或載入中）時才真正作動
                if (!IsSongPlaybackActive)
                {
                    DebugLog("Pause_Click: No song is currently playing, ignoring.");
                    return;
                }

                if (ktvPlayer.IsPlaying)
                {
                    AppLogger.Log("User action: Pause button clicked");
                    ktvPlayer.Pause();

                    // Pause recording in sync with media
                    try { RecordingManager.Instance.PauseRecording(); }
                    catch (Exception ex) { AppLogger.LogError("Error pausing recording in Pause_Click", ex); }

                    SetPauseButtonResume(true);
                    UpdateCurrentPlayingDisplay();
                    Debug.WriteLine("Media paused");
                }
                else
                {
                    AppLogger.Log("User action: Resume button clicked");
                    ktvPlayer.Play();

                    // Resume recording in sync with media
                    try { RecordingManager.Instance.ResumeRecording(); }
                    catch (Exception ex) { AppLogger.LogError("Error resuming recording in Pause_Click", ex); }

                    SetPauseButtonResume(false);
                    UpdateCurrentPlayingDisplay();
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
            if (_isLoopPlay)
            {
                // 點1下後則取消循環播放變成原來的"重播"功能
                SetLoopPlay(false);
                return;
            }

            // 只有在歌曲播放或有正在/剛播放歌曲時才真正作動
            if (!IsSongPlaybackActive && string.IsNullOrEmpty(PlayingFilePath))
            {
                DebugLog("Repeat_Click: No song is currently playing, ignoring.");
                return;
            }

            ReplayCurrentSong();
        }

        public void ReplayCurrentSong()
        {
            // Replay current song (閒置時重唱最後一首)。PlaybackSession 會停止並重新指定 Source，
            // 而不是 MediaPosition = 0：graph 已拆除時設定位置會卡住。
            try
            {
                _playback.Replay();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error replaying song: {ex.Message}");
                AppLogger.LogError("Error replaying song", ex);
            }
        }

        public void SetLoopPlay(bool enable)
        {
            _isLoopPlay = enable;
            _playback.LoopCurrent = enable;

            if (RepeatBtn == null) return;

            if (_isLoopPlay)
            {
                RepeatBtn.SetResourceReference(ContentControl.ContentProperty, Loc.ResourcePrefix + "Player.Loop");
                RepeatBtn.Background = _fixedButtonBackground ?? ThemeSettingsHandler.PrimaryBrush;
                RepeatBtn.Foreground = _activeButtonForeground ?? (Brush)FindResource("BrushTextMain");
                StartRepeatBtnFlash();
                MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.LoopOn"), 5);
                AppLogger.Log("User action: Loop play enabled");
                DebugLog("SetLoopPlay: Loop play enabled");
            }
            else
            {
                RepeatBtn.SetResourceReference(ContentControl.ContentProperty, Loc.ResourcePrefix + "Player.Replay");
                StopRepeatBtnFlash();
                RepeatBtn.ClearValue(OpacityProperty);
                RepeatBtn.ClearValue(Button.BackgroundProperty);
                RepeatBtn.ClearValue(Button.ForegroundProperty);
                MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.LoopOff"), 5);
                AppLogger.Log("User action: Loop play disabled");
                DebugLog("SetLoopPlay: Loop play disabled");
            }
        }

        // Flag to track if the user is currently dragging the video progress slider
        private bool _isUserDraggingProgressSlider = false;

        private void VideoProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ktvPlayer == null || !ktvPlayer.HasVideo || _isUpdatingProgressSlider)
                return;

            // During dragging, update CurrentTimeText to show preview time
            if (_isUserDraggingProgressSlider)
            {
                try
                {
                    var totalDurationTicks = ktvPlayer.MediaDuration;
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
                    var totalDurationTicks = ktvPlayer.MediaDuration;
                    if (totalDurationTicks > 0)
                    {
                        var newPositionTicks = (long)(totalDurationTicks * (e.NewValue / 100.0));
                        ktvPlayer.MediaPosition = newPositionTicks;
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
            if (ktvPlayer != null && ktvPlayer.HasVideo && !_isUpdatingProgressSlider)
            {
                try
                {
                    var totalDurationTicks = ktvPlayer.MediaDuration;
                    if (totalDurationTicks > 0)
                    {
                        var newPositionTicks = (long)(totalDurationTicks * (VideoProgressSlider.Value / 100.0));
                        ktvPlayer.MediaPosition = newPositionTicks;
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
            if (ktvPlayer == null) return;

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
            if (ktvPlayer == null || _isUpdatingProgressSlider)
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
                ktvPlayer.Volume = ComputePlayerVolume(sliderValue);
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
        private void UpdateVocalMusicButtonStates()
        {
            if (ktvPlayer == null || VocalBtn == null || MusicBtn == null)
            {
                return;
            }

            var mode = _vocal.Mode;

            if (!_vocal.IsYoutube)
            {
                // Always stop any flashing animation before reapplying normal styles
                StopVocalBtnFlash();
                VocalBtn.ClearValue(OpacityProperty);
            }

            // If a track is fixed, the Vocal button gets a special "locked" style.
            if (mode == VocalMode.Fixed)
            {
                VocalBtn.Background = _fixedButtonBackground;
                VocalBtn.Foreground = _activeButtonForeground;

                // Music button is always inactive in this mode
                MusicBtn.ClearValue(Button.BackgroundProperty);
                MusicBtn.ClearValue(Button.ForegroundProperty);

                // Start the flashing animation on the button
                StartVocalBtnFlash();
                return;
            }

            // Apply styles based on which button is active
            // Use cached brushes for performance to avoid UI thread stalls.
            if (mode == VocalMode.Music)
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

        public void VocalBtn_Click(object sender, RoutedEventArgs e) => SwitchToVocal();

        public void MusicBtn_Click(object sender, RoutedEventArgs e) => SwitchToMusic();

        /// <summary>切到人聲 (人聲鈕、手機、隨機播放共用)。固定人聲時只解除固定。</summary>
        public void SwitchToVocal()
        {
            // 只有在歌曲播放（或載入中）時才真正作動（若處於固定聲道狀態則允許點擊解除）
            if (!IsSongPlaybackActive && !_vocal.IsFixed)
            {
                DebugLog("SwitchToVocal: No song is currently playing, ignoring.");
                return;
            }

            var result = _vocal.SwitchToVocal();
            AppLogger.Log($"VocalBtn: {result}, tracks={ktvPlayer.AudioStreams.Count}, track={ktvPlayer.AudioTrack}, channel={ktvPlayer.AudioChannel}");
            AfterVocalSwitch(result);
        }

        /// <summary>切到伴唱 (伴唱鈕、手機、隨機播放共用)。固定人聲時只解除固定。</summary>
        public void SwitchToMusic()
        {
            if (!IsSongPlaybackActive && !_vocal.IsFixed)
            {
                DebugLog("SwitchToMusic: No song is currently playing, ignoring.");
                return;
            }

            var result = _vocal.SwitchToMusic();
            AppLogger.Log($"MusicBtn: {result}, tracks={ktvPlayer.AudioStreams.Count}, track={ktvPlayer.AudioTrack}, channel={ktvPlayer.AudioChannel}");
            AfterVocalSwitch(result);
        }

        private void AfterVocalSwitch(VocalSwitchResult result)
        {
            if (result == VocalSwitchResult.Ignored) return;

            if (result == VocalSwitchResult.Unfixed)
            {
                AppLogger.Log("User action: Disabled fixed vocal track");
                // Stop the flashing animation and reset opacity before updating styles
                StopVocalBtnFlash();
                VocalBtn.ClearValue(OpacityProperty);
                MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.TrackUnfixed"), 2);
            }
            UpdateVocalMusicButtonStates();
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
            if (ktvPlayer != null)
            {
                await ShowSetAsMusicTrackDialog();
            }
        }

        private async void VocalBtn_RightClick(object sender, MouseButtonEventArgs e)
        {
            AppLogger.Log("User action: Vocal button right click");
            DebugLog("VocalBtn_RightClick triggered");

            // Right click action: Show option to set current channel as music track
            if (ktvPlayer != null)
            {
                await ShowSetAsMusicTrackDialog();
            }
        }

        private Task ShowSetAsMusicTrackDialog()
        {
            try
            {
                if (VocalBtn == null) return Task.CompletedTask;

                var currentTrack = ktvPlayer.AudioTrack;
                DebugLog($"ShowSetAsMusicTrackDialog: Current AudioTrack={currentTrack}, Current MusicTrack={_vocal.MusicTrack}");

                // 當副選單出現時，原來的按鈕無效 (Disabled)
                if (VocalBtn != null) VocalBtn.IsEnabled = false;

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
                    Background = (Brush)FindResource("BrushBgSecondary"),
                    BorderBrush = ThemeSettingsHandler.PrimaryBrush,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = (CornerRadius)FindResource("RadiusSmall"),
                    Height = 60,
                    Padding = new Thickness(8, 6, 8, 6),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = (Color)FindResource("ColorShadow"),
                        Direction = 315,
                        ShadowDepth = 5,
                        Opacity = 0.5
                    }
                };

                // Button panel - now the main content
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0)
                };

                var setAsMusicButton = new Button
                {
                    Content = Loc.T("Player.SetTrackAsMusic"),
                    Height = 42,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                ThemeSettingsHandler.ApplyOutlinedButtonStyle(setAsMusicButton, 16);

                var setFixeTrackButton = new Button
                {
                    Content = Loc.T("Player.FixTrack"),
                    Height = 42,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                ThemeSettingsHandler.ApplyOutlinedButtonStyle(setFixeTrackButton, 16);

                var cancelButton = new Button
                {
                    Content = Loc.T("Common.Cancel"),
                    Height = 42,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                ThemeSettingsHandler.ApplyOutlinedButtonStyle(cancelButton, 16);

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
                    var targetTrack = _vocal.Fix(currentTrack);
                    AppLogger.Log($"User action: Fix audio track to {targetTrack}");
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
                    // 副選單關閉後，恢復原來的按鈕功能 (Enabled)
                    if (VocalBtn != null) VocalBtn.IsEnabled = true;

                    // Process result after popup closes
                    if (result == true) // "Set as Music" was clicked
                    {
                        DebugLog($"User confirmed: Setting AudioTrack {currentTrack} as music track");

                        // Set current audio track as the music track and switch to music
                        bool singleStream = ktvPlayer.AudioStreams.Count <= 1;
                        int newTrack = _vocal.SetAsMusic(currentTrack);
                        if (singleStream)
                            MarqueeAPI.ShowStaticAnnouncement(Loc.T(newTrack == 1 ? "Player.LeftAsMusic" : "Player.RightAsMusic"), 5);
                        else
                            MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.TrackAsMusic", newTrack), 5);
                        UpdateVocalMusicButtonStates();

                        // If sync is enabled, update the database
                        if (_playingSongData != null)
                        {
                            if (!string.IsNullOrEmpty(_playingSongData.Id))
                            {
                                string songId = _playingSongData.Id;

                                // Update in-memory data
                                _playingSongData.Track = newTrack;

                                // Persist change to database asynchronously
                                Task.Run(() =>
                                {
                                    try
                                    {
                                        SongLibrary.SetMusicTrack(songId, newTrack);
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

                        DebugLog($"Music track updated to: {newTrack}");
                    }
                    else if (result == false) // "Cancel" was clicked
                    {
                        DebugLog("User cancelled the dialog");
                    }
                    else // "Fix Track" was clicked (result is null)
                    {
                        int fixedTrack = _vocal.FixedTrack;
                        DebugLog($"User confirmed: Fixing AudioTrack to {fixedTrack}");
                        if (ktvPlayer.AudioStreams.Count <= 1 && fixedTrack == 1)
                            MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.LeftFixed"), 5);
                        else if (ktvPlayer.AudioStreams.Count <= 1 && fixedTrack == 2)
                            MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.RightFixed"), 5);
                        else
                            MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.TrackFixed", fixedTrack), 5);

                        // Update button visuals to reflect the new "fixed" state
                        UpdateVocalMusicButtonStates();
                    }
                };

                // Show the popup
                popup.IsOpen = true;
            }
            catch (Exception ex)
            {
                if (VocalBtn != null) VocalBtn.IsEnabled = true;
                DebugLog($"Error in ShowSetAsMusicTrackDialog: {ex.Message}");
                AppLogger.LogError("Error in ShowSetAsMusicTrackDialog", ex);
                MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.SetFailed"), 2);
            }
            return Task.CompletedTask;
        }

        private Task MusicBtn_LongPress()
        {
            DebugLog("MusicBtn_LongPress triggered");

            // Long press action: Show music volume adjustment or advanced music settings
            if (ktvPlayer != null)
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
            if (ktvPlayer != null)
            {
                // You can add specific right click functionality here
                // For example: Show context menu, reset music settings, etc.
            }
        }

        #endregion

        #region RepeatBtn Long Press and Right Click Handlers

        private DispatcherTimer? _repeatBtnLongPressTimer;
        private bool _repeatBtnLongPressTriggered = false;
        private bool _isLoopPlay = false;

        public bool IsLoopPlay => _isLoopPlay;

        private void RepeatBtn_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _repeatBtnLongPressTriggered = false;

                // Start long press timer (500ms)
                _repeatBtnLongPressTimer?.Stop();
                _repeatBtnLongPressTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _repeatBtnLongPressTimer.Tick += (s, args) =>
                {
                    _repeatBtnLongPressTimer?.Stop();
                    _repeatBtnLongPressTriggered = true;
                    ShowLoopPlayDialog();
                };
                _repeatBtnLongPressTimer.Start();
            }
        }

        private void RepeatBtn_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _repeatBtnLongPressTimer?.Stop();

            // If long press was triggered, prevent the normal click
            if (_repeatBtnLongPressTriggered)
            {
                e.Handled = true;
                _repeatBtnLongPressTriggered = false;
            }
        }

        private void RepeatBtn_RightClick(object sender, MouseButtonEventArgs e)
        {
            AppLogger.Log("User action: Repeat button right click");
            DebugLog("RepeatBtn_RightClick triggered");
            ShowLoopPlayDialog();
        }

        private void ShowLoopPlayDialog()
        {
            try
            {
                if (RepeatBtn == null) return;

                AppLogger.Log("User action: Show loop play dialog");
                DebugLog($"ShowLoopPlayDialog: Current _isLoopPlay={_isLoopPlay}");

                // 當副選單出現時，原來的按鈕無效 (Disabled)
                RepeatBtn.IsEnabled = false;

                // Create a popup positioned near the RepeatBtn
                var popup = new System.Windows.Controls.Primitives.Popup
                {
                    PlacementTarget = RepeatBtn,
                    Placement = System.Windows.Controls.Primitives.PlacementMode.Top,
                    AllowsTransparency = true,
                    StaysOpen = false
                };

                // Create the dialog content with styling matching Vocal dialog
                var dialogBorder = new Border
                {
                    Background = (Brush)FindResource("BrushBgSecondary"),
                    BorderBrush = ThemeSettingsHandler.PrimaryBrush,
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = (CornerRadius)FindResource("RadiusSmall"),
                    Height = 60,
                    Padding = new Thickness(8, 6, 8, 6),
                    Effect = new System.Windows.Media.Effects.DropShadowEffect
                    {
                        Color = (Color)FindResource("ColorShadow"),
                        Direction = 315,
                        ShadowDepth = 5,
                        Opacity = 0.5
                    }
                };

                // Button panel
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0)
                };

                var loopPlayButton = new Button
                {
                    Content = _isLoopPlay ? Loc.T("Player.CancelLoop") : Loc.T("Player.LoopPlay"),
                    Height = 42,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                ThemeSettingsHandler.ApplyOutlinedButtonStyle(loopPlayButton, 16);

                var cancelButton = new Button
                {
                    Content = Loc.T("Common.Cancel"),
                    Height = 42,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                ThemeSettingsHandler.ApplyOutlinedButtonStyle(cancelButton, 16);

                buttonPanel.Children.Add(loopPlayButton);
                buttonPanel.Children.Add(cancelButton);

                dialogBorder.Child = buttonPanel;
                popup.Child = dialogBorder;

                bool? result = null;

                loopPlayButton.Click += (s, e) =>
                {
                    result = !_isLoopPlay;
                    AppLogger.Log($"User action: Clicked loopPlayButton in popup, target state: {result}");
                    popup.IsOpen = false;
                };

                cancelButton.Click += (s, e) =>
                {
                    result = null;
                    popup.IsOpen = false;
                };

                popup.Closed += (s, e) =>
                {
                    // 副選單關閉後，恢復原來的按鈕功能 (Enabled)
                    RepeatBtn.IsEnabled = true;

                    if (result.HasValue)
                    {
                        SetLoopPlay(result.Value);
                    }
                };

                popup.IsOpen = true;
            }
            catch (Exception ex)
            {
                RepeatBtn.IsEnabled = true;
                DebugLog($"ShowLoopPlayDialog error: {ex.Message}");
                AppLogger.LogError("ShowLoopPlayDialog error", ex);
            }
        }

        #endregion
        
        private void InitializeButtonFlashAnimation(Button button)
        {
            var storyboard = new Storyboard();
            var animation = new DoubleAnimation
            {
                From = 1.0,
                To = 0.4, // A visible but noticeable dimming
                Duration = new Duration(TimeSpan.FromMilliseconds(600)),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };
            Storyboard.SetTargetProperty(animation, new PropertyPath(OpacityProperty));
            storyboard.Children.Add(animation);

            if (button == VocalBtn)
            {
                _vocalBtnFlashStoryboard = storyboard;
            }
            else if (button == RepeatBtn)
            {
                _repeatBtnFlashStoryboard = storyboard;
            }
        }

        // Stopping a storyboard that was never begun logs a WPF Animation warning, so track whether each flash is running
        private bool _isVocalBtnFlashing;
        private bool _isRepeatBtnFlashing;

        private void StartVocalBtnFlash()
        {
            if (_vocalBtnFlashStoryboard == null) return;
            _vocalBtnFlashStoryboard.Begin(VocalBtn, true);
            _isVocalBtnFlashing = true;
        }

        private void StopVocalBtnFlash()
        {
            if (!_isVocalBtnFlashing) return;
            _vocalBtnFlashStoryboard?.Stop(VocalBtn);
            _isVocalBtnFlashing = false;
        }

        private void StartRepeatBtnFlash()
        {
            if (_repeatBtnFlashStoryboard == null) return;
            _repeatBtnFlashStoryboard.Begin(RepeatBtn, true);
            _isRepeatBtnFlashing = true;
        }

        private void StopRepeatBtnFlash()
        {
            if (!_isRepeatBtnFlashing) return;
            _repeatBtnFlashStoryboard?.Stop(RepeatBtn);
            _isRepeatBtnFlashing = false;
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
            MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.VolumeLocked", _lockedVolume), 2);
        }

        /// <summary>
        /// Event handler for when the volume lock toggle is unchecked.
        /// Releases the volume lock so songs can use their individual volume settings.
        /// </summary>
        private void VolumeLockToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Volume lock deactivated");
            DebugLog("Volume lock deactivated.");
            MarqueeAPI.ShowStaticAnnouncement(Loc.T("Player.VolumeUnlocked"), 2);
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

        /// <summary>切換音量鎖定，回傳切換後是否鎖定。</summary>
        public bool ToggleVolumeLock()
        {
            bool newState = !(VolumeLockToggle.IsChecked ?? false);
            VolumeLockToggle.IsChecked = newState;
            AppLogger.Log($"Remote action: Volume Lock toggled to {newState}");
            return newState;
        }
    }
}