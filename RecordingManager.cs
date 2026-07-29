using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;
using NAudio.CoreAudioApi;

namespace UltimateKtv
{
    public class RecordingManager
    {
        private static readonly object _lock = new object();
        private static RecordingManager? _instance;

        public static RecordingManager Instance
        {
            get
            {
                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = new RecordingManager();
                    }
                    return _instance;
                }
            }
        }

        private WaveInEvent? _waveSource;
        private WaveFileWriter? _waveWriter;

        private WasapiLoopbackCapture? _loopbackSource;
        private WaveFileWriter? _loopbackWriter;

        private string? _currentFilePath;
        private bool _isRecordingActive = false;
        private bool _isPaused = false;

        // Tracks how many capture devices are still stopping — used with Interlocked
        // to ensure ConvertToMp3Async is triggered exactly once.
        private int _pendingStopCount = 0;

        // Paths for the current recording session (needed for pause/resume)
        private string? _currentMicPath;
        private string? _currentLoopbackPath;

        // Previous-session capture devices to stop AFTER releasing the lock
        // (set in StartRecording when a prior session is already active).
        private WaveInEvent?           _prevWaveToStop;
        private WasapiLoopbackCapture? _prevLoopbackToStop;

        private int _activeOperations = 0;
        private readonly ManualResetEventSlim _allStoppedEvent = new ManualResetEventSlim(true);

        private RecordingManager()
        {
        }

        public bool IsRecording
        {
            get
            {
                lock (_lock)
                {
                    return _isRecordingActive;
                }
            }
        }

        public bool IsPaused
        {
            get
            {
                lock (_lock)
                {
                    return _isPaused;
                }
            }
        }

        /// <summary>
        /// Starts a new recording session for the given song.
        /// If isRandomPlay is true, recording is skipped entirely.
        /// If a recording is already active, it is stopped first.
        /// </summary>
        public void StartRecording(string songName, string singer, bool isRandomPlay = false)
        {
            lock (_lock)
            {
                var settings = SettingsManager.Instance.CurrentSettings;

                if (!settings.EnableRecording)
                {
                    return;
                }

                // Do not record random-play songs
                if (isRandomPlay)
                {
                    AppLogger.Log("[Recording] Skipping recording — random play song.");
                    return;
                }

                // If currently recording, stop it first.
                // We call the public StopRecording() which handles stopping devices
                // outside the lock to avoid deadlock with RecordingStopped callbacks.
                // Because StartRecording holds _lock here and StopRecording also acquires
                // _lock (reentrant on same thread), we snapshot the devices manually inline
                // so they can be stopped below, after releasing the outer lock.
                if (_isRecordingActive)
                {
                    AppLogger.Log("[Recording] Stopping previous recording session to start a new one.");
                    // Snapshot and clear refs; devices will be stopped after we release the lock.
                    _prevWaveToStop     = _waveSource;
                    _prevLoopbackToStop = _loopbackSource;
                    _waveSource     = null;
                    _loopbackSource = null;
                    _isRecordingActive = false;
                    _isPaused          = false;
                    // Note: the previous session's _pendingStopCount and RecordingStopped
                    // callbacks will still run; they'll decrement and fire ConvertToMp3Async.
                    // Devices are stopped outside this lock block (see end of StartRecording).
                }

                // Determine file name
                string sanitizedSong = SanitizeFileName(string.IsNullOrEmpty(songName) ? "UnknownSong" : songName);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{sanitizedSong}_{timestamp}.mp3";

                // Resolve output directory — fall back to App's "Recording" folder if empty
                string outputDir = settings.RecordingPath;
                if (string.IsNullOrWhiteSpace(outputDir))
                {
                    outputDir = "Recording";
                }
                if (!Path.IsPathRooted(outputDir))
                {
                    outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputDir);
                }

                // Check free disk space on the recording drive (require at least 1 GB)
                const long MinFreeBytes = 1L * 1024 * 1024 * 1024; // 1 GB
                try
                {
                    string driveRoot = Path.GetPathRoot(outputDir) ?? outputDir;
                    var driveInfo = new DriveInfo(driveRoot);
                    long freeBytes = driveInfo.AvailableFreeSpace;
                    if (freeBytes < MinFreeBytes)
                    {
                        double freeGB = freeBytes / (1024.0 * 1024.0 * 1024.0);
                        AppLogger.Log($"[Recording] Skipping recording — insufficient disk space on '{driveRoot}': {freeGB:F2} GB free (need at least 1 GB).");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[Recording] Could not check disk space for '{outputDir}', proceeding anyway.", ex);
                }

                int startedCount = 0;
                try
                {
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    _currentFilePath = Path.Combine(outputDir, fileName);

                    string wavPathMic      = Path.ChangeExtension(_currentFilePath, ".mic.wav");
                    string wavPathLoopback = Path.ChangeExtension(_currentFilePath, ".loopback.wav");

                    // Keep paths for pause/resume
                    _currentMicPath      = wavPathMic;
                    _currentLoopbackPath = wavPathLoopback;

                    string localMp3Path = _currentFilePath;

                    // --- 1. Try start Microphone recording ---
                    try
                    {
                        int deviceCount = WaveInEvent.DeviceCount;
                        if (deviceCount > 0)
                        {
                            int deviceIndex = -1;
                            for (int i = 0; i < deviceCount; i++)
                            {
                                try
                                {
                                    var caps = WaveInEvent.GetCapabilities(i);
                                    if (caps.ProductName == settings.RecordingDevice)
                                    {
                                        deviceIndex = i;
                                        break;
                                    }
                                }
                                catch { }
                            }

                            if (deviceIndex == -1) deviceIndex = 0;

                            _waveSource = new WaveInEvent();
                            _waveSource.DeviceNumber = deviceIndex;
                            _waveSource.WaveFormat = new WaveFormat(44100, 16, 2);

                            _waveSource.DataAvailable += (s, e) =>
                            {
                                lock (_lock)
                                {
                                    if (_waveWriter != null && e.BytesRecorded > 0)
                                    {
                                        try { _waveWriter.Write(e.Buffer, 0, e.BytesRecorded); }
                                        catch (Exception ex) { AppLogger.LogError("[Recording] Error writing mic data", ex); }
                                    }
                                }
                            };

                            _waveSource.RecordingStopped += (s, e) =>
                            {
                                lock (_lock)
                                {
                                    // Only close writer when NOT paused (paused keeps writer open for resume)
                                    if (!_isPaused)
                                    {
                                        CleanUpWriter();
                                    }
                                    if (e.Exception != null)
                                    {
                                        AppLogger.LogError("[Recording] Mic recording stopped with error", e.Exception);
                                    }
                                }

                                // Use Interlocked to ensure ConvertToMp3Async is fired exactly once
                                if (!_isPaused)
                                {
                                    int remaining = Interlocked.Decrement(ref _pendingStopCount);
                                    if (remaining <= 0)
                                    {
                                        ConvertToMp3Async(wavPathMic, wavPathLoopback, localMp3Path);
                                    }
                                }
                            };

                            _waveWriter = new WaveFileWriter(wavPathMic, _waveSource.WaveFormat);
                            _waveSource.StartRecording();
                            startedCount++;
                            AppLogger.Log($"[Recording] Mic capture started using device index {deviceIndex}. Temp path: {wavPathMic}");
                        }
                        else
                        {
                            AppLogger.Log("[Recording] No audio input devices found. Mic recording skipped.");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[Recording] Failed to start mic recording", ex);
                        CleanUpWriter();
                        if (_waveSource != null) { try { _waveSource.Dispose(); } catch { } _waveSource = null; }
                    }

                    // --- 2. Try start Loopback (Line Out) recording ---
                    try
                    {
                        MMDevice? loopbackDevice = null;
                        if (!string.IsNullOrEmpty(settings.AudioRendererDevice) &&
                            settings.AudioRendererDevice != "Default DirectSound Device")
                        {
                            loopbackDevice = GetLoopbackDevice(settings.AudioRendererDevice);
                        }

                        if (loopbackDevice != null)
                        {
                            _loopbackSource = new WasapiLoopbackCapture(loopbackDevice);
                            AppLogger.Log($"[Recording] Initializing loopback capture on matched device: {loopbackDevice.FriendlyName}");
                        }
                        else
                        {
                            _loopbackSource = new WasapiLoopbackCapture();
                            AppLogger.Log("[Recording] Initializing loopback capture on default system rendering device.");
                        }

                        _loopbackSource.DataAvailable += (s, e) =>
                        {
                            lock (_lock)
                            {
                                if (_loopbackWriter != null && e.BytesRecorded > 0)
                                {
                                    try { _loopbackWriter.Write(e.Buffer, 0, e.BytesRecorded); }
                                    catch (Exception ex) { AppLogger.LogError("[Recording] Error writing loopback data", ex); }
                                }
                            }
                        };

                        _loopbackSource.RecordingStopped += (s, e) =>
                        {
                            lock (_lock)
                            {
                                if (!_isPaused)
                                {
                                    CleanUpLoopbackWriter();
                                }
                                if (e.Exception != null)
                                {
                                    AppLogger.LogError("[Recording] Loopback recording stopped with error", e.Exception);
                                }
                            }

                            if (!_isPaused)
                            {
                                int remaining = Interlocked.Decrement(ref _pendingStopCount);
                                if (remaining <= 0)
                                {
                                    ConvertToMp3Async(wavPathMic, wavPathLoopback, localMp3Path);
                                }
                            }
                        };

                        _loopbackWriter = new WaveFileWriter(wavPathLoopback, _loopbackSource.WaveFormat);
                        _loopbackSource.StartRecording();
                        startedCount++;
                        AppLogger.Log($"[Recording] Loopback capture started. Temp path: {wavPathLoopback}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[Recording] Failed to start loopback recording", ex);
                        CleanUpLoopbackWriter();
                        if (_loopbackSource != null) { try { _loopbackSource.Dispose(); } catch { } _loopbackSource = null; }
                    }

                    // Initialize the pending-stop counter to how many devices started
                    _pendingStopCount = startedCount;

                    if (startedCount > 0)
                    {
                        lock (_lock)
                        {
                            _activeOperations++;
                        }
                        _allStoppedEvent.Reset();
                        _isRecordingActive = true;
                        _isPaused = false;
                    }
                    else
                    {
                        _isRecordingActive = false;
                        _isPaused = false;
                        _allStoppedEvent.Set();
                        AppLogger.Log("[Recording] No capture devices were successfully started.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Recording] Failed to start audio recording session", ex);
                    _isRecordingActive = false;
                    _isPaused = false;
                    lock (_lock)
                    {
                        if (startedCount > 0)
                        {
                            _activeOperations--;
                        }
                        if (_activeOperations <= 0)
                        {
                            _allStoppedEvent.Set();
                        }
                    }
                }
            } // end lock(_lock)

            // Stop previous session's devices OUTSIDE the lock so their RecordingStopped
            // callbacks can acquire the lock and fire ConvertToMp3Async unblocked.
            if (_prevWaveToStop != null)
            {
                try { _prevWaveToStop.StopRecording(); }
                catch (Exception ex) { AppLogger.LogError("[Recording] Error stopping previous WaveSource", ex); }
                try { _prevWaveToStop.Dispose(); } catch { }
                _prevWaveToStop = null;
            }
            if (_prevLoopbackToStop != null)
            {
                try { _prevLoopbackToStop.StopRecording(); }
                catch (Exception ex) { AppLogger.LogError("[Recording] Error stopping previous LoopbackSource", ex); }
                try { _prevLoopbackToStop.Dispose(); } catch { }
                _prevLoopbackToStop = null;
            }
        }

        public void StopRecording()
        {
            string micPath      = "";
            string loopbackPath = "";
            string mp3Path      = "";
            bool   triggerConversionManually = false;

            // Snapshot capture device refs and path info, then mark as stopped.
            // Devices are stopped OUTSIDE the lock below so RecordingStopped
            // callbacks can freely acquire the lock (prevents deadlock with
            // WaitForRecordingToFinish on shutdown).
            WaveInEvent?           waveToStop     = null;
            WasapiLoopbackCapture? loopbackToStop = null;

            lock (_lock)
            {
                if (!_isRecordingActive)
                {
                    return;
                }

                if (_isPaused)
                {
                    // Capture devices are already stopped (PauseRecording stopped them).
                    // Writers are still open — flush them then trigger conversion manually.
                    AppLogger.Log("[Recording] StopRecording while paused — flushing writers and scheduling conversion.");

                    micPath      = _currentMicPath      ?? "";
                    loopbackPath = _currentLoopbackPath ?? "";
                    mp3Path      = _currentFilePath      ?? "";

                    CleanUpWriter();
                    CleanUpLoopbackWriter();

                    // Capture devices were already stopped by PauseRecording; just dispose.
                    waveToStop     = _waveSource;
                    loopbackToStop = _loopbackSource;
                    _waveSource     = null;
                    _loopbackSource = null;

                    _isPaused          = false;
                    _isRecordingActive = false;
                    _pendingStopCount  = 0;

                    triggerConversionManually = !string.IsNullOrEmpty(mp3Path);
                }
                else
                {
                    // Snapshot refs and mark as inactive; actual stop happens outside lock.
                    waveToStop     = _waveSource;
                    loopbackToStop = _loopbackSource;
                    _waveSource     = null;
                    _loopbackSource = null;
                    _isPaused          = false;
                    _isRecordingActive = false;
                    // _pendingStopCount stays as-is; RecordingStopped will decrement it.
                }
            }

            // --- Stop/dispose devices OUTSIDE the lock ---
            // This allows RecordingStopped event callbacks to acquire the lock
            // and call ConvertToMp3Async without deadlocking.
            if (!triggerConversionManually)
            {
                // Normal (non-paused) stop: StopRecording fires RecordingStopped async.
                if (waveToStop != null)
                {
                    try { waveToStop.StopRecording(); }
                    catch (Exception ex) { AppLogger.LogError("[Recording] Error stopping WaveSource", ex); }
                    try { waveToStop.Dispose(); } catch { }
                }
                if (loopbackToStop != null)
                {
                    try { loopbackToStop.StopRecording(); }
                    catch (Exception ex) { AppLogger.LogError("[Recording] Error stopping LoopbackSource", ex); }
                    try { loopbackToStop.Dispose(); } catch { }
                }
            }
            else
            {
                // Paused stop: devices were already stopped, just dispose.
                if (waveToStop != null)     { try { waveToStop.Dispose(); }     catch { } }
                if (loopbackToStop != null) { try { loopbackToStop.Dispose(); } catch { } }

                // Trigger conversion now that the lock is released.
                ConvertToMp3Async(micPath, loopbackPath, mp3Path);
            }
        }

        /// <summary>
        /// Pauses active recording. Capture devices are stopped but WAV writers stay open.
        /// Call ResumeRecording() to continue writing to the same files.
        /// </summary>
        public void PauseRecording()
        {
            lock (_lock)
            {
                if (!_isRecordingActive || _isPaused)
                {
                    return;
                }

                AppLogger.Log("[Recording] Pausing recording.");
                _isPaused = true;

                // Stop capture — RecordingStopped events will fire but _isPaused flag
                // prevents writer cleanup and ConvertToMp3Async from being triggered.
                if (_waveSource != null)
                {
                    try { _waveSource.StopRecording(); }
                    catch (Exception ex) { AppLogger.LogError("[Recording] Error pausing WaveSource", ex); }
                }

                if (_loopbackSource != null)
                {
                    try { _loopbackSource.StopRecording(); }
                    catch (Exception ex) { AppLogger.LogError("[Recording] Error pausing LoopbackSource", ex); }
                }
            }
        }

        /// <summary>
        /// Resumes recording after PauseRecording(). Restarts capture devices and continues
        /// writing to the same WAV files as before.
        /// </summary>
        public void ResumeRecording()
        {
            lock (_lock)
            {
                if (!_isRecordingActive || !_isPaused)
                {
                    return;
                }

                AppLogger.Log("[Recording] Resuming recording.");
                _isPaused = false;

                int restarted = 0;

                // Restart mic capture
                if (_waveSource != null && _waveWriter != null)
                {
                    try
                    {
                        _waveSource.StartRecording();
                        restarted++;
                        AppLogger.Log("[Recording] Mic capture resumed.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[Recording] Error resuming mic capture", ex);
                    }
                }

                // Restart loopback capture
                if (_loopbackSource != null && _loopbackWriter != null)
                {
                    try
                    {
                        _loopbackSource.StartRecording();
                        restarted++;
                        AppLogger.Log("[Recording] Loopback capture resumed.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[Recording] Error resuming loopback capture", ex);
                    }
                }

                // Reset the pending-stop counter so ConvertToMp3Async fires correctly on next stop
                _pendingStopCount = restarted;

                if (restarted == 0)
                {
                    // Nothing could be resumed — treat as stopped
                    _isRecordingActive = false;
                    AppLogger.Log("[Recording] Resume failed — no devices could be restarted.");
                }
            }
        }

        // StopRecordingInternal is no longer used — logic moved into StopRecording
        // to ensure devices are stopped outside the lock (deadlock prevention).

        private void CleanUpWriter()
        {
            if (_waveWriter != null)
            {
                try
                {
                    _waveWriter.Flush();
                    _waveWriter.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Recording] Error disposing WaveWriter", ex);
                }
                finally
                {
                    _waveWriter = null;
                }
            }
        }

        private void CleanUpLoopbackWriter()
        {
            if (_loopbackWriter != null)
            {
                try
                {
                    _loopbackWriter.Flush();
                    _loopbackWriter.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Recording] Error disposing Loopback WaveWriter", ex);
                }
                finally
                {
                    _loopbackWriter = null;
                }
            }
        }

        private void ConvertToMp3Async(string wavPathMic, string wavPathLoopback, string mp3Path)
        {
            Task.Run(async () =>
            {
                try
                {
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
                    if (!File.Exists(ffmpegPath))
                    {
                        AppLogger.Log($"[Recording] FFmpeg not found at '{ffmpegPath}'. Cannot convert to MP3.");
                        return;
                    }

                    bool hasMic      = File.Exists(wavPathMic);
                    bool hasLoopback = File.Exists(wavPathLoopback);

                    if (!hasMic && !hasLoopback)
                    {
                        AppLogger.Log("[Recording] No temporary recording files found to convert.");
                        return;
                    }

                    string arguments;
                    if (hasMic && hasLoopback)
                    {
                        AppLogger.Log($"[Recording] Mixing Mic + Loopback to 320kbps MP3: {wavPathMic} & {wavPathLoopback} -> {mp3Path}");
                        arguments = $"-y -i \"{wavPathLoopback}\" -i \"{wavPathMic}\" -filter_complex \"amix=inputs=2:duration=longest\" -codec:a libmp3lame -b:a 320k \"{mp3Path}\"";
                    }
                    else if (hasMic)
                    {
                        AppLogger.Log($"[Recording] Converting Mic WAV to 320kbps MP3: {wavPathMic} -> {mp3Path}");
                        arguments = $"-y -i \"{wavPathMic}\" -codec:a libmp3lame -b:a 320k \"{mp3Path}\"";
                    }
                    else
                    {
                        AppLogger.Log($"[Recording] Converting Loopback WAV to 320kbps MP3: {wavPathLoopback} -> {mp3Path}");
                        arguments = $"-y -i \"{wavPathLoopback}\" -codec:a libmp3lame -b:a 320k \"{mp3Path}\"";
                    }

                    var startInfo = new ProcessStartInfo
                    {
                        FileName               = ffmpegPath,
                        Arguments              = arguments,
                        UseShellExecute        = false,
                        CreateNoWindow         = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError  = true
                    };

                    using (var process = Process.Start(startInfo))
                    {
                        if (process != null)
                        {
                            process.WaitForExit();
                            if (process.ExitCode == 0)
                            {
                                AppLogger.Log("[Recording] Successfully created mixed MP3. Deleting temporary WAV files.");
                                await TryDeleteFileAsync(wavPathMic);
                                await TryDeleteFileAsync(wavPathLoopback);
                            }
                            else
                            {
                                string err = process.StandardError.ReadToEnd();
                                AppLogger.Log($"[Recording] FFmpeg mixing failed with exit code {process.ExitCode}. Output: {err}");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Recording] Exception during MP3 conversion/mixing", ex);
                }
                finally
                {
                    lock (_lock)
                    {
                        _activeOperations--;
                        if (_activeOperations <= 0)
                        {
                            _allStoppedEvent.Set();
                        }
                    }
                }
            });
        }

        public void WaitForRecordingToFinish()
        {
            try
            {
                if (!_allStoppedEvent.IsSet)
                {
                    AppLogger.Log("[Recording] Waiting for recording stop and MP3 transcoding to complete...");
                    bool finished = _allStoppedEvent.Wait(20000); // Wait up to 20 seconds
                    if (finished)
                    {
                        AppLogger.Log("[Recording] MP3 transcoding completed successfully before exit.");
                    }
                    else
                    {
                        AppLogger.Log("[Recording] Timeout waiting for MP3 transcoding to complete. Exiting anyway.");
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[Recording] Error waiting for recording finish", ex);
            }
        }

        /// <summary>
        /// Attempts to delete a file, retrying up to 3 times with a 500ms delay if the file
        /// is locked by another process (e.g. ffmpeg still finishing).
        /// </summary>
        private async Task TryDeleteFileAsync(string path)
        {
            const int maxRetries = 3;
            const int retryDelayMs = 500;

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    if (File.Exists(path))
                    {
                        File.Delete(path);
                    }
                    return; // success
                }
                catch (IOException ioEx) when (attempt < maxRetries)
                {
                    AppLogger.Log($"[Recording] File '{Path.GetFileName(path)}' is locked (attempt {attempt}/{maxRetries}): {ioEx.Message} — retrying in {retryDelayMs}ms...");
                    await Task.Delay(retryDelayMs);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[Recording] Failed to delete temporary file '{path}'", ex);
                    return;
                }
            }

            // Final attempt after retries
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[Recording] Failed to delete temporary file '{path}' after {maxRetries} retries", ex);
            }
        }

        private string SanitizeFileName(string fileName)
        {
            string invalidChars    = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidRegStr   = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            return Regex.Replace(fileName, invalidRegStr, "_");
        }

        private MMDevice? GetLoopbackDevice(string targetFriendlyName)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var devices    = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

                foreach (var device in devices)
                {
                    if (device.FriendlyName.Equals(targetFriendlyName, StringComparison.OrdinalIgnoreCase) ||
                        device.FriendlyName.Contains(targetFriendlyName) ||
                        targetFriendlyName.Contains(device.FriendlyName))
                    {
                        return device;
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[Recording] Error finding MMDevice for loopback device '{targetFriendlyName}'", ex);
            }
            return null;
        }
    }
}
