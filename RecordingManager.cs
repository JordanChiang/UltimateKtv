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

        private RecordingSession? _currentSession;
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
                    return _currentSession != null && !_currentSession.IsStopped;
                }
            }
        }

        public bool IsPaused
        {
            get
            {
                lock (_lock)
                {
                    return _currentSession != null && _currentSession.IsPaused;
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
            RecordingSession? prevSession = null;

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

                // If currently recording, snapshot previous session to stop it outside lock
                if (_currentSession != null && !_currentSession.IsStopped)
                {
                    AppLogger.Log("[Recording] Stopping previous recording session to start a new one.");
                    prevSession = _currentSession;
                    _currentSession = null;
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

                try
                {
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Trigger async cleanup of any orphaned WAV files left from past runs
                    Task.Run(() => CleanupOrphanedWavFiles(outputDir));

                    string mp3Path = Path.Combine(outputDir, fileName);
                    string micPath = Path.ChangeExtension(mp3Path, ".mic.wav");
                    string loopbackPath = Path.ChangeExtension(mp3Path, ".loopback.wav");

                    var newSession = new RecordingSession(mp3Path, micPath, loopbackPath, GetLoopbackDevice);

                    if (newSession.Start(settings))
                    {
                        _currentSession = newSession;
                        _activeOperations++;
                        _allStoppedEvent.Reset();
                    }
                    else
                    {
                        AppLogger.Log("[Recording] No capture devices were successfully started.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Recording] Failed to start audio recording session", ex);
                }
            } // end lock(_lock)

            // Stop previous session outside lock to avoid deadlocks
            if (prevSession != null)
            {
                prevSession.Stop(OnSessionConversionCompleted);
            }
        }

        public void StopRecording()
        {
            RecordingSession? sessionToStop = null;
            lock (_lock)
            {
                if (_currentSession == null)
                {
                    return;
                }
                sessionToStop = _currentSession;
                _currentSession = null;
            }

            sessionToStop?.Stop(OnSessionConversionCompleted);
        }

        public void PauseRecording()
        {
            lock (_lock)
            {
                _currentSession?.Pause();
            }
        }

        public void ResumeRecording()
        {
            lock (_lock)
            {
                _currentSession?.Resume();
            }
        }

        private async Task OnSessionConversionCompleted()
        {
            lock (_lock)
            {
                _activeOperations--;
                if (_activeOperations <= 0)
                {
                    _activeOperations = 0;
                    _allStoppedEvent.Set();
                }
            }
            await Task.CompletedTask;
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

        private static async Task ConvertToMp3Async(string wavPathMic, string wavPathLoopback, string mp3Path)
        {
            try
            {
                string ffmpegPath = Path.Combine(YtDlpHelper.GetFFmpegDir(), "ffmpeg.exe");
                if (!File.Exists(ffmpegPath))
                {
                    AppLogger.Log($"[Recording] FFmpeg not found at '{ffmpegPath}'. Cannot convert to MP3.");
                    return;
                }

                bool hasMic = File.Exists(wavPathMic);
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
                    FileName = ffmpegPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process != null)
                    {
                        string err = await process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync();

                        if (process.ExitCode == 0)
                        {
                            AppLogger.Log("[Recording] Successfully created mixed MP3. Deleting temporary WAV files.");
                            await TryDeleteFileAsync(wavPathMic);
                            await TryDeleteFileAsync(wavPathLoopback);
                        }
                        else
                        {
                            AppLogger.Log($"[Recording] FFmpeg mixing failed with exit code {process.ExitCode}. Output: {err}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[Recording] Exception during MP3 conversion/mixing", ex);
            }
        }

        /// <summary>
        /// Scans the output directory for leftover .mic.wav or .loopback.wav files whose corresponding .mp3 file
        /// ALREADY exists and is non-empty, deleting them cleanly.
        /// </summary>
        private static void CleanupOrphanedWavFiles(string outputDir)
        {
            try
            {
                if (!Directory.Exists(outputDir)) return;

                var wavFiles = Directory.GetFiles(outputDir, "*.wav");
                foreach (var wavFile in wavFiles)
                {
                    string? mp3Path = null;
                    if (wavFile.EndsWith(".mic.wav", StringComparison.OrdinalIgnoreCase))
                    {
                        mp3Path = wavFile.Substring(0, wavFile.Length - ".mic.wav".Length) + ".mp3";
                    }
                    else if (wavFile.EndsWith(".loopback.wav", StringComparison.OrdinalIgnoreCase))
                    {
                        mp3Path = wavFile.Substring(0, wavFile.Length - ".loopback.wav".Length) + ".mp3";
                    }

                    if (mp3Path != null && File.Exists(mp3Path) && new FileInfo(mp3Path).Length > 0)
                    {
                        try
                        {
                            File.Delete(wavFile);
                            AppLogger.Log($"[Recording] Cleaned up orphaned temporary file: {Path.GetFileName(wavFile)}");
                        }
                        catch (Exception ex)
                        {
                            AppLogger.Log($"[Recording] Could not clean up orphaned file '{Path.GetFileName(wavFile)}': {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[Recording] Error during CleanupOrphanedWavFiles", ex);
            }
        }

        private static async Task TryDeleteFileAsync(string path)
        {
            const int maxRetries = 5;
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
            string invalidChars = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            string invalidRegStr = string.Format(@"([{0}]*\.+$)|([{0}]+)", invalidChars);
            return Regex.Replace(fileName, invalidRegStr, "_");
        }

        private MMDevice? GetLoopbackDevice(string targetFriendlyName)
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

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

        private class RecordingSession
        {
            private readonly object _sessionLock = new object();
            private readonly Func<string, MMDevice?> _getLoopbackDeviceFunc;

            private WaveInEvent? _waveSource;
            private WaveFileWriter? _waveWriter;
            private WasapiLoopbackCapture? _loopbackSource;
            private WaveFileWriter? _loopbackWriter;
            private Func<Task>? _onConversionComplete;

            private int _pendingStopCount = 0;
            private bool _isPaused = false;
            private bool _isStopped = false;
            private bool _conversionTriggered = false;

            public string Mp3Path { get; }
            public string MicPath { get; }
            public string LoopbackPath { get; }

            public RecordingSession(string mp3Path, string micPath, string loopbackPath, Func<string, MMDevice?> getLoopbackDeviceFunc)
            {
                Mp3Path = mp3Path;
                MicPath = micPath;
                LoopbackPath = loopbackPath;
                _getLoopbackDeviceFunc = getLoopbackDeviceFunc;
            }

            public bool IsPaused
            {
                get
                {
                    lock (_sessionLock) return _isPaused;
                }
            }

            public bool IsStopped
            {
                get
                {
                    lock (_sessionLock) return _isStopped;
                }
            }

            public bool Start(AppSettings settings)
            {
                lock (_sessionLock)
                {
                    int startedCount = 0;

                    // 1. Try start Microphone recording
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
                            _waveSource.WaveFormat = new WaveFormat(44100, 24, 2);

                            _waveSource.DataAvailable += (s, e) =>
                            {
                                lock (_sessionLock)
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
                                OnDeviceStopped(isMic: true, e.Exception);
                            };

                            _waveWriter = new WaveFileWriter(MicPath, _waveSource.WaveFormat);
                            _waveSource.StartRecording();
                            startedCount++;
                            AppLogger.Log($"[Recording] Mic capture started using device index {deviceIndex}. Temp path: {MicPath}");
                        }
                        else
                        {
                            AppLogger.Log("[Recording] No audio input devices found. Mic recording skipped.");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[Recording] Failed to start mic recording", ex);
                        CleanUpMicWriter();
                        if (_waveSource != null) { try { _waveSource.Dispose(); } catch { } _waveSource = null; }
                    }

                    // 2. Try start Loopback (Line Out) recording
                    try
                    {
                        MMDevice? loopbackDevice = null;
                        if (!string.IsNullOrEmpty(settings.AudioRendererDevice) &&
                            settings.AudioRendererDevice != "Default DirectSound Device")
                        {
                            loopbackDevice = _getLoopbackDeviceFunc(settings.AudioRendererDevice);
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
                            lock (_sessionLock)
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
                            OnDeviceStopped(isMic: false, e.Exception);
                        };

                        _loopbackWriter = new WaveFileWriter(LoopbackPath, _loopbackSource.WaveFormat);
                        _loopbackSource.StartRecording();
                        startedCount++;
                        AppLogger.Log($"[Recording] Loopback capture started. Temp path: {LoopbackPath}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[Recording] Failed to start loopback recording", ex);
                        CleanUpLoopbackWriter();
                        if (_loopbackSource != null) { try { _loopbackSource.Dispose(); } catch { } _loopbackSource = null; }
                    }

                    _pendingStopCount = startedCount;
                    _isStopped = (startedCount == 0);
                    _isPaused = false;

                    return startedCount > 0;
                }
            }

            public void Pause()
            {
                lock (_sessionLock)
                {
                    if (_isStopped || _isPaused) return;

                    AppLogger.Log("[Recording] Pausing recording.");
                    _isPaused = true;

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

            public void Resume()
            {
                lock (_sessionLock)
                {
                    if (_isStopped || !_isPaused) return;

                    AppLogger.Log("[Recording] Resuming recording.");
                    _isPaused = false;

                    int restarted = 0;
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

                    _pendingStopCount = restarted;
                    if (restarted == 0)
                    {
                        _isStopped = true;
                        AppLogger.Log("[Recording] Resume failed — no devices could be restarted.");
                    }
                }
            }

            public void Stop(Func<Task>? onConversionComplete = null)
            {
                WaveInEvent? waveToStop = null;
                WasapiLoopbackCapture? loopbackToStop = null;
                bool triggerManual = false;

                lock (_sessionLock)
                {
                    _onConversionComplete = onConversionComplete;

                    if (_isStopped) return;
                    _isStopped = true;

                    if (_isPaused)
                    {
                        AppLogger.Log("[Recording] StopRecording while paused — flushing writers and scheduling conversion.");
                        CleanUpWriters();

                        waveToStop = _waveSource;
                        loopbackToStop = _loopbackSource;
                        _waveSource = null;
                        _loopbackSource = null;
                        _isPaused = false;
                        _pendingStopCount = 0;

                        triggerManual = true;
                    }
                    else
                    {
                        waveToStop = _waveSource;
                        loopbackToStop = _loopbackSource;
                        _waveSource = null;
                        _loopbackSource = null;
                        _isPaused = false;
                    }
                }

                if (!triggerManual)
                {
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
                    if (waveToStop != null) { try { waveToStop.Dispose(); } catch { } }
                    if (loopbackToStop != null) { try { loopbackToStop.Dispose(); } catch { } }

                    TriggerConversion(_onConversionComplete);
                }
            }

            private void OnDeviceStopped(bool isMic, Exception? ex)
            {
                bool shouldTrigger = false;

                lock (_sessionLock)
                {
                    if (!_isPaused)
                    {
                        if (isMic) CleanUpMicWriter();
                        else CleanUpLoopbackWriter();
                    }

                    if (ex != null)
                    {
                        AppLogger.LogError($"[Recording] {(isMic ? "Mic" : "Loopback")} recording stopped with error", ex);
                    }

                    if (!_isPaused)
                    {
                        _pendingStopCount--;
                        if (_pendingStopCount <= 0 && !_conversionTriggered)
                        {
                            _conversionTriggered = true;
                            shouldTrigger = true;
                        }
                    }
                }

                if (shouldTrigger)
                {
                    CleanUpWriters();
                    TriggerConversion(_onConversionComplete);
                }
            }

            private void CleanUpMicWriter()
            {
                lock (_sessionLock)
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
                            AppLogger.LogError("[Recording] Error disposing Mic WaveWriter", ex);
                        }
                        finally
                        {
                            _waveWriter = null;
                        }
                    }
                }
            }

            private void CleanUpLoopbackWriter()
            {
                lock (_sessionLock)
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
            }

            private void CleanUpWriters()
            {
                CleanUpMicWriter();
                CleanUpLoopbackWriter();
            }

            private void TriggerConversion(Func<Task>? onConversionComplete)
            {
                Task.Run(async () =>
                {
                    try
                    {
                        CleanUpWriters();
                        await ConvertToMp3Async(MicPath, LoopbackPath, Mp3Path);
                    }
                    finally
                    {
                        if (onConversionComplete != null)
                        {
                            await onConversionComplete();
                        }
                    }
                });
            }
        }
    }
}
