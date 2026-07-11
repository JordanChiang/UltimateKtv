using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Diagnostics;
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
        private int _activeRecordersCount = 0;
        private int _activeOperations = 0;
        private readonly System.Threading.ManualResetEventSlim _allStoppedEvent = new System.Threading.ManualResetEventSlim(true);

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

        public void StartRecording(string songName, string singer)
        {
            lock (_lock)
            {
                var settings = SettingsManager.Instance.CurrentSettings;

                if (!settings.EnableRecording)
                {
                    return;
                }

                // If currently recording, stop it first.
                if (_isRecordingActive)
                {
                    AppLogger.Log("[Recording] Stopping previous recording session to start a new one.");
                    StopRecordingInternal();
                }

                // Determine file name
                string sanitizedSong = SanitizeFileName(string.IsNullOrEmpty(songName) ? "UnknownSong" : songName);
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string fileName = $"{sanitizedSong}_{timestamp}.mp3";

                string outputDir = settings.RecordingPath;
                if (!Path.IsPathRooted(outputDir))
                {
                    outputDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, outputDir);
                }

                int activeRecordersCount = 0;
                try
                {
                    if (!Directory.Exists(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    _currentFilePath = Path.Combine(outputDir, fileName);

                    string wavPathMic = Path.ChangeExtension(_currentFilePath, ".mic.wav");
                    string wavPathLoopback = Path.ChangeExtension(_currentFilePath, ".loopback.wav");

                    string localWavPathMic = wavPathMic;
                    string localWavPathLoopback = wavPathLoopback;
                    string localMp3Path = _currentFilePath;

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
                                catch {}
                            }

                            if (deviceIndex == -1)
                            {
                                deviceIndex = 0;
                            }

                            _waveSource = new WaveInEvent();
                            _waveSource.DeviceNumber = deviceIndex;
                            _waveSource.WaveFormat = new WaveFormat(44100, 16, 2); // 16-bit, Stereo, 44.1kHz

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
                                    CleanUpWriter();
                                    if (e.Exception != null)
                                    {
                                        AppLogger.LogError("[Recording] Mic recording stopped with error", e.Exception);
                                    }
                                    _activeRecordersCount--;
                                    if (_activeRecordersCount <= 0)
                                    {
                                        ConvertToMp3Async(localWavPathMic, localWavPathLoopback, localMp3Path);
                                    }
                                }
                            };

                            _waveWriter = new WaveFileWriter(wavPathMic, _waveSource.WaveFormat);
                            _waveSource.StartRecording();
                            activeRecordersCount++;
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
                        if (_waveSource != null) { try { _waveSource.Dispose(); } catch {} _waveSource = null; }
                    }

                    // 2. Try start Loopback (Line Out) recording
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
                                CleanUpLoopbackWriter();
                                if (e.Exception != null)
                                {
                                    AppLogger.LogError("[Recording] Loopback recording stopped with error", e.Exception);
                                }
                                _activeRecordersCount--;
                                if (_activeRecordersCount <= 0)
                                {
                                    ConvertToMp3Async(localWavPathMic, localWavPathLoopback, localMp3Path);
                                }
                            }
                        };

                        _loopbackWriter = new WaveFileWriter(wavPathLoopback, _loopbackSource.WaveFormat);
                        _loopbackSource.StartRecording();
                        activeRecordersCount++;
                        AppLogger.Log($"[Recording] Loopback capture started. Temp path: {wavPathLoopback}");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[Recording] Failed to start loopback recording", ex);
                        CleanUpLoopbackWriter();
                        if (_loopbackSource != null) { try { _loopbackSource.Dispose(); } catch {} _loopbackSource = null; }
                    }

                    _activeRecordersCount = activeRecordersCount;
                    if (activeRecordersCount > 0)
                    {
                        lock (_lock)
                        {
                            _activeOperations++;
                        }
                        _allStoppedEvent.Reset();
                        _isRecordingActive = true;
                    }
                    else
                    {
                        _isRecordingActive = false;
                        _allStoppedEvent.Set();
                        AppLogger.Log("[Recording] No capture devices were successfully started.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Recording] Failed to start audio recording session", ex);
                    _isRecordingActive = false;
                    lock (_lock)
                    {
                        if (activeRecordersCount > 0)
                        {
                            _activeOperations--;
                        }
                        if (_activeOperations <= 0)
                        {
                            _allStoppedEvent.Set();
                        }
                    }
                }
            }
        }

        public void StopRecording()
        {
            lock (_lock)
            {
                if (!_isRecordingActive)
                {
                    return;
                }

                StopRecordingInternal();
            }
        }

        private void StopRecordingInternal()
        {
            if (_waveSource != null)
            {
                try
                {
                    _waveSource.StopRecording();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Recording] Error stopping WaveSource", ex);
                }
                finally
                {
                    try { _waveSource.Dispose(); } catch {}
                    _waveSource = null;
                }
            }

            if (_loopbackSource != null)
            {
                try
                {
                    _loopbackSource.StopRecording();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("[Recording] Error stopping LoopbackSource", ex);
                }
                finally
                {
                    try { _loopbackSource.Dispose(); } catch {}
                    _loopbackSource = null;
                }
            }

            _isRecordingActive = false;
        }

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
            Task.Run(() =>
            {
                try
                {
                    string ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ffmpeg.exe");
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
                            process.WaitForExit();
                            if (process.ExitCode == 0)
                            {
                                AppLogger.Log($"[Recording] Successfully created mixed MP3. Deleting temporary WAV files.");
                                TryDeleteFile(wavPathMic);
                                TryDeleteFile(wavPathLoopback);
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

        private void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[Recording] Failed to delete temporary file '{path}'", ex);
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
                    // Check for exact match or contains (directsound name vs active endpoint friendly name)
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
