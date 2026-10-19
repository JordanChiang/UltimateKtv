using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using QRCoder;
using DirectShowLib;
using NAudio.Wave;

namespace UltimateKtv
{
    public partial class UserOptionsWindow : Window
    {
        private MainWindow? _parentWindow;
        private bool _isInitializing = true;

        public UserOptionsWindow(MainWindow parentWindow)
        {
            InitializeComponent();
            _parentWindow = parentWindow;
            
            // Apply theme colors to this window
            ThemeSettingsHandler.ApplyToResources(this);

            // Adjust window size while maintaining aspect ratio (1024:900)
            double designHeight = 900.0;
            double designWidth = 1024.0;
            
            double screenWorkHeight = SystemParameters.WorkArea.Height;
            double screenWorkWidth = SystemParameters.WorkArea.Width;
            double dpiScale = 1.0;

            try 
            {
                var monitors = VideoDisplayWindow.GetAvailableMonitorsInfo();
                int monitorIndex = SettingsManager.Instance.CurrentSettings.ConsoleScreen - 1;

                if (monitorIndex >= 0 && monitorIndex < monitors.Count)
                {
                    var monitor = monitors[monitorIndex];
                    dpiScale = VideoDisplayWindow.GetDpiScaleForMonitor(monitorIndex);
                    screenWorkHeight = (monitor.rcWork.Bottom - monitor.rcWork.Top) / dpiScale;
                    screenWorkWidth = (monitor.rcWork.Right - monitor.rcWork.Left) / dpiScale;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Log($"[Scaling Debug] Error calculating monitor size: {ex.Message}");
            }

            // Apply buffers
            double maxAllowedHeight = screenWorkHeight - 40;
            double maxAllowedWidth = screenWorkWidth - 40;

            // Calculate the uniform scale factor needed to fit within the available space
            double scaleHeight = maxAllowedHeight / designHeight;
            double scaleWidth = maxAllowedWidth / designWidth;
            double uniformScale = Math.Min(1.0, Math.Min(scaleHeight, scaleWidth));

            // Set the final window size maintaining aspect ratio
            this.Height = designHeight * uniformScale;
            this.Width = designWidth * uniformScale;

            // Enforce constraints
            this.MaxHeight = maxAllowedHeight;
            this.MaxWidth = maxAllowedWidth;
                        
            // 只放入「預設」與「目前設定值」兩個項目；完整的裝置列表在視窗顯示後於背景列舉
            // (DirectShow / WaveIn 裝置列舉很慢，放在建構子會讓 UI 執行緒卡住、跑馬燈停頓)
            InitializeAudioRenderer();
            InitializeAudioInputDevice();
            InitializeSettings();
            _isInitializing = false;

            ContentRendered += async (_, __) => await LoadAudioDeviceListsAsync();
        }

        #region Window reuse (Hide instead of Close)

        // 設定視窗第一次開啟時，套用範本與版面配置要花 ~400ms (UI 執行緒卡住、跑馬燈停頓)。
        // 關閉時改為隱藏，主視窗保留這個實例，之後再開啟只需重新載入設定值。
        private bool _allowClose;

        /// <summary>
        /// 重新開啟前呼叫：從目前設定重新載入所有控制項 (與新建視窗時相同)，捲回頂端並重新列舉音訊裝置。
        /// </summary>
        public void PrepareForReopen()
        {
            _isInitializing = true;
            try
            {
                InitializeAudioRenderer();
                InitializeAudioInputDevice();
                InitializeSettings();
            }
            finally
            {
                _isInitializing = false;
            }

            OptionsScrollViewer.ScrollToTop();
            _ = LoadAudioDeviceListsAsync();
        }

        /// <summary>真正關閉視窗 (主視窗關閉時呼叫，否則隱藏的視窗會讓程式無法結束)。</summary>
        public void ForceClose()
        {
            _allowClose = true;
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_allowClose)
            {
                // 外部呼叫 Close() (例如單螢幕模式關閉所有子視窗) 也改為隱藏。
                // Closing 事件中不能直接改變顯示狀態，所以延後執行。
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(Hide));
            }
            base.OnClosing(e);
        }

        #endregion

        private const string DefaultAudioRendererName = "Default DirectSound Device";
        private const string DefaultAudioInputName = "Default WaveIn Device";

        // QR code 只顯示在 200x200 的區塊，每個模組 8px 就足夠清晰 (原本 20px 會產生 ~800px 的點陣圖)
        private const int QrPixelsPerModule = 8;

        private int _httpQrVersion;
        private int _publicQrVersion;

        private void InitializeAudioRenderer()
        {
            // Placeholder list: default + currently saved device, so the saved value is selected
            // immediately and is not lost if the user presses Apply before enumeration finishes.
            AudioRendererComboBox.Items.Clear();
            AudioRendererComboBox.Items.Add(DefaultAudioRendererName);

            string saved = SettingsManager.Instance.CurrentSettings.AudioRendererDevice;
            if (!string.IsNullOrEmpty(saved) && saved != DefaultAudioRendererName)
            {
                AudioRendererComboBox.Items.Add(saved);
            }
        }

        private void InitializeAudioInputDevice()
        {
            AudioInputDeviceComboBox.Items.Clear();
            AudioInputDeviceComboBox.Items.Add(DefaultAudioInputName);

            string saved = SettingsManager.Instance.CurrentSettings.RecordingDevice;
            if (!string.IsNullOrEmpty(saved) && saved != DefaultAudioInputName)
            {
                AudioInputDeviceComboBox.Items.Add(saved);
            }
        }

        private static List<string> EnumerateAudioRenderers()
        {
            var names = new List<string>();
            try
            {
                var devices = DsDevice.GetDevicesOfCat(FilterCategory.AudioRendererCategory);
                foreach (var device in devices)
                {
                    if (!string.IsNullOrEmpty(device.Name)) names.Add(device.Name);
                    device.Dispose();
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to list audio renderer devices", ex);
            }
            return names;
        }

        private static List<string> EnumerateAudioInputs()
        {
            var names = new List<string>();
            try
            {
                int deviceCount = WaveInEvent.DeviceCount;
                for (int i = 0; i < deviceCount; i++)
                {
                    var caps = WaveInEvent.GetCapabilities(i);
                    names.Add(caps.ProductName);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to list audio input devices", ex);
            }
            return names;
        }

        /// <summary>
        /// 在背景列舉音訊裝置，完成後再填入下拉選單 (保留目前選取的項目)。
        /// </summary>
        private async Task LoadAudioDeviceListsAsync()
        {
            List<string> renderers;
            List<string> inputs;
            try
            {
                var result = await Task.Run(() => (EnumerateAudioRenderers(), EnumerateAudioInputs()));
                renderers = result.Item1;
                inputs = result.Item2;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("LoadAudioDeviceListsAsync failed", ex);
                return;
            }

            if (!IsLoaded) return; // window already closed

            bool wasInitializing = _isInitializing;
            _isInitializing = true; // suppress SelectionChanged logging while repopulating
            try
            {
                RepopulateDeviceComboBox(AudioRendererComboBox, DefaultAudioRendererName, renderers);
                RepopulateDeviceComboBox(AudioInputDeviceComboBox, DefaultAudioInputName, inputs);
            }
            finally
            {
                _isInitializing = wasInitializing;
            }
        }

        private static void RepopulateDeviceComboBox(ComboBox comboBox, string defaultName, List<string> deviceNames)
        {
            var current = comboBox.SelectedItem as string;

            comboBox.Items.Clear();
            comboBox.Items.Add(defaultName);
            foreach (var name in deviceNames)
            {
                if (!comboBox.Items.Contains(name)) comboBox.Items.Add(name);
            }

            // Same behaviour as before: if the saved device no longer exists, fall back to the default
            if (current != null && comboBox.Items.Contains(current))
                comboBox.SelectedItem = current;
            else
                comboBox.SelectedIndex = 0;
        }

        private void InitializeSettings()
        {
            if (_parentWindow == null) return;

            var settings = SettingsManager.Instance.CurrentSettings;

            // Set UI controls based on loaded settings
            // Note: _isInitializing flag prevents event handlers from executing during this phase
            PreviewToggle.IsChecked = settings.ShowPreviewInMainWindow;
            FullScreenButton.IsChecked = settings.IsMediaFullScreen;
            NewSongDaysTextBox.Text = settings.NewSongDays.ToString();
            PlayCountSlider.Value = settings.PlayCountUpdatePercentage;
            AudioAmplifySlider.Value = settings.AudioAmplify;
            HttpServerToggle.IsChecked = settings.EnableHttpServer;
            LimitCursorToggle.IsChecked = settings.LimitCursorOnMain;
            PlayHistoryCountSlider.Value = settings.PlayHistoryCount;
            PlayHistoryCountValue.Text = settings.PlayHistoryCount.ToString();
            
            // Set song sort method
            SongSortMethodComboBox.SelectedIndex = settings.SongSortMethod - 1; // Convert 1-3 to 0-2

            // Set app logger toggle
            AppLoggerToggle.IsChecked = settings.IsAppLoggerEnabled;

            // Set pre-loading toggle
            PreLoadingToggle.IsChecked = settings.EnablePreLoading;

            // Set network remote song username toggle
            NetworkRemoteSongUsernameToggle.IsChecked = settings.NetworkRemoteSongUsername;

            // Set Legacy Audio Channel Definition toggle
            LegacyAudioChannelToggle.IsChecked = settings.IsLegacyAudioChannelDefinitionEnabled;

            // Set Player Engine
            PlayerEngineComboBox.SelectedIndex = settings.PlayerEngine == 1 ? 1 : 0;
            UpdatePlayerEngineStatus();

            // Set Video Renderer
            VideoRendererComboBox.SelectedIndex = settings.VideoRendererType;

            // Set Audio Renderer
            AudioRendererComboBox.SelectedItem = settings.AudioRendererDevice;
            if (AudioRendererComboBox.SelectedIndex == -1 && AudioRendererComboBox.Items.Count > 0)
            {
                AudioRendererComboBox.SelectedIndex = 0;
            }

            // Set Recording Settings
            EnableRecordingToggle.IsChecked = settings.EnableRecording;
            UpdateRecordingUIState();

            AudioInputDeviceComboBox.SelectedItem = settings.RecordingDevice;
            if (AudioInputDeviceComboBox.SelectedIndex == -1 && AudioInputDeviceComboBox.Items.Count > 0)
            {
                AudioInputDeviceComboBox.SelectedIndex = 0;
            }

            string recPath = settings.RecordingPath;
            if (string.IsNullOrWhiteSpace(recPath))
            {
                recPath = "Recording";
            }
            if (!System.IO.Path.IsPathRooted(recPath))
            {
                recPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, recPath);
            }
            RecordingPathText.Text = recPath;
            RecordingPathText.ToolTip = recPath;

            // Set HW Accel
            EnableHWAccelToggle.IsChecked = settings.EnableHWAccel;
            HWAccelModeComboBox.SelectedIndex = settings.HWAccelMode;
            HWAccelModeComboBox.IsEnabled = settings.EnableHWAccel;

            // Set Public Server Port and generate QR code
            PublicServerPortTextBox.Text = settings.PublicServerPort.ToString();
            GeneratePublicQrCodeForPort(settings.PublicServerPort);

            // Set Change Song Library Drive settings
            ChangeSongLibraryDriveToggle.IsChecked = settings.ChangeSongLibraryDriveEnabled;
            SongLibraryDrivePathText.Text = string.IsNullOrEmpty(settings.SongLibraryDrivePath) 
                ? Loc.T("Options.NoDirSelected") 
                : settings.SongLibraryDrivePath;

            // Set Random Play settings
            RandomPlayToggle.IsChecked = settings.RandomPlayEnabled;
            // Select category by matching Tag value (0, 1, 2, or 10)
            SelectComboBoxItemByTag(RandomPlayCategoryComboBox, settings.RandomPlayCategory);
            RandomPlayAudioChannelComboBox.SelectedIndex = settings.RandomPlayAudioChannel;
            InitializeFavoriteUserComboBox();
            UpdateRandomPlayFavoriteUserVisibility();

            // UI language
            UiLanguageComboBox.ItemsSource = Loc.RefreshCultures(); // 重新掃描，使用者新增的語言檔不用重開程式
            UiLanguageComboBox.SelectedValue = Loc.Normalize(settings.UiLanguage);

            // Update HTTP server status text
            UpdateHttpServerLabelStyle();
            UpdateHttpStatusText();

            // Language settings
            LanguageMultiSelectToggle.IsChecked = settings.IsLanguageMultiSelect;
        }

        private void InitializeFavoriteUserComboBox()
        {
            RandomPlayFavoriteUserComboBox.Items.Clear();
            
            // Use MainWindow's GetFavoriteUserNames which uses the same filtering as GetUserListForFavorites
            var userNames = _parentWindow?.GetFavoriteUserNames();
            if (userNames != null)
            {
                foreach (string userName in userNames)
                {
                    RandomPlayFavoriteUserComboBox.Items.Add(userName);
                }
            }

            // Set saved selection
            var settings = SettingsManager.Instance.CurrentSettings;
            if (!string.IsNullOrEmpty(settings.RandomPlayFavoriteUser) && 
                RandomPlayFavoriteUserComboBox.Items.Contains(settings.RandomPlayFavoriteUser))
            {
                RandomPlayFavoriteUserComboBox.SelectedItem = settings.RandomPlayFavoriteUser;
            }
            else if (RandomPlayFavoriteUserComboBox.Items.Count > 0)
            {
                RandomPlayFavoriteUserComboBox.SelectedIndex = 0;
            }
        }

        private void UpdateRandomPlayFavoriteUserVisibility()
        {
            // Show the favorite user combo box only when "我的最愛" (tag 10) is selected
            bool isFavoriteCategory = false;
            if (RandomPlayCategoryComboBox.SelectedItem is ComboBoxItem item && 
                int.TryParse(item.Tag?.ToString(), out int tag))
            {
                isFavoriteCategory = (tag == 10);
            }
            RandomPlayFavoriteUserComboBox.Visibility = isFavoriteCategory ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// Selects a ComboBoxItem by its Tag value
        /// </summary>
        private void SelectComboBoxItemByTag(ComboBox comboBox, int tagValue)
        {
            foreach (var item in comboBox.Items)
            {
                if (item is ComboBoxItem cbItem && 
                    int.TryParse(cbItem.Tag?.ToString(), out int tag) && 
                    tag == tagValue)
                {
                    comboBox.SelectedItem = cbItem;
                    return;
                }
            }
            // Fallback to first item if not found
            if (comboBox.Items.Count > 0)
            {
                comboBox.SelectedIndex = 0;
            }
        }

        private void LeaveButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }



        private void PreviewToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("User setting changed: Preview enabled");
            _parentWindow?.ShowPreview();
        }

        private void PreviewToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("User setting changed: Preview disabled");
            _parentWindow?.HidePreview();
        }

        /// <summary>
        /// 顯示目前使用中的播放引擎，以及是否找到 libmpv DLL。
        /// </summary>
        private void UpdatePlayerEngineStatus()
        {
            string current = MediaHostManager.Instance.Player.EngineName;
            string dll = MpvNative.FindDll() != null
                ? Loc.T("Options.PlayerEngineMpvFound")
                : Loc.T("Options.PlayerEngineMpvMissing", Environment.Is64BitProcess ? "Codec" : "Codec_x86");
            PlayerEngineStatusText.Text = Loc.T("Options.PlayerEngineCurrent", current) + Environment.NewLine + dll;
        }

        private void NewSongDaysTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void ButtonStyleButton_Click(object sender, RoutedEventArgs e)
        {
            _parentWindow?.ButtonStyle_Click(sender, e);
        }

        private void AlwaysFullScreen_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("User setting changed: FullScreen enabled");
            _parentWindow?.SetMediaStretch(Stretch.Fill);
        }

        private void AlwaysFullScreen_UnChecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("User setting changed: FullScreen disabled");
            _parentWindow?.SetMediaStretch(Stretch.Uniform);
        }

        private void PlayCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            if (_parentWindow != null && sender is Slider slider)
            {
                int newValue = (int)Math.Round(slider.Value);
                newValue = Math.Max(10, Math.Min(90, newValue));
                AppLogger.Log($"User setting changed: PlayCountUpdatePercentage = {newValue}");
                _parentWindow.PlayCountUpdatePercentage = newValue;
            }
        }

        private void AudioAmplifySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            if (_parentWindow != null && sender is Slider slider)
            {
                int newValue = (int)Math.Round(slider.Value);
                // Ensure value is within valid range (100-500)
                newValue = Math.Max(100, Math.Min(500, newValue));
                AppLogger.Log($"User setting changed: AudioAmplify = {newValue}");
                _parentWindow.SetAudioAmplify(newValue);
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_parentWindow == null) return;

            // Update the settings object from the UI controls
            var settings = SettingsManager.Instance.CurrentSettings;
            settings.ShowPreviewInMainWindow = PreviewToggle.IsChecked == true;
            settings.IsMediaFullScreen = FullScreenButton.IsChecked == true;
            settings.PlayCountUpdatePercentage = (int)PlayCountSlider.Value;
            // Ensure AudioAmplify is within valid range (100-500)
            settings.AudioAmplify = Math.Max(100, Math.Min(500, (int)AudioAmplifySlider.Value));
            settings.EnableHttpServer = HttpServerToggle.IsChecked == true;
            settings.LimitCursorOnMain = LimitCursorToggle.IsChecked == true;
            settings.PlayHistoryCount = Math.Max(1, Math.Min(10, (int)PlayHistoryCountSlider.Value));
            settings.SongSortMethod = SongSortMethodComboBox.SelectedIndex + 1; // Convert 0-2 to 1-3
            settings.IsAppLoggerEnabled = AppLoggerToggle.IsChecked == true;
            if (int.TryParse(NewSongDaysTextBox.Text, out int days))
            {
                settings.NewSongDays = days;
            }

            // Save new options
            int oldPlayerEngine = settings.PlayerEngine;
            if (PlayerEngineComboBox.SelectedItem is ComboBoxItem engineItem && int.TryParse(engineItem.Tag.ToString(), out int engine))
            {
                settings.PlayerEngine = engine;
            }
            bool playerEngineChanged = oldPlayerEngine != settings.PlayerEngine;
            if (playerEngineChanged)
            {
                AppLogger.Log($"User setting changed: PlayerEngine = {settings.PlayerEngine}");
            }

            if (VideoRendererComboBox.SelectedItem is ComboBoxItem videoItem && int.TryParse(videoItem.Tag.ToString(), out int videoType))
            {
                settings.VideoRendererType = videoType;
            }

            if (AudioRendererComboBox.SelectedItem != null)
            {
                settings.AudioRendererDevice = AudioRendererComboBox.SelectedItem.ToString() ?? "Default DirectSound Device";
            }

            // Save Recording Settings
            settings.EnableRecording = EnableRecordingToggle.IsChecked == true;
            if (AudioInputDeviceComboBox.SelectedItem != null)
            {
                settings.RecordingDevice = AudioInputDeviceComboBox.SelectedItem.ToString() ?? "Default WaveIn Device";
            }

            string recPathText = RecordingPathText.Text.Trim();
            // Resolve what the default path would be so we can compare
            string defaultRecPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Recording");
            // Store empty string if the user has the default path selected — RecordingManager will handle the fallback
            settings.RecordingPath = string.Equals(recPathText, defaultRecPath, StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : recPathText;

            settings.EnableHWAccel = EnableHWAccelToggle.IsChecked == true;
            
            if (HWAccelModeComboBox.SelectedItem is ComboBoxItem hwItem && int.TryParse(hwItem.Tag.ToString(), out int hwMode))
            {
                settings.HWAccelMode = hwMode;
            }

            // Save Public Server Port
            if (int.TryParse(PublicServerPortTextBox.Text, out int publicPort) && publicPort > 0 && publicPort <= 65535)
            {
                settings.PublicServerPort = publicPort;
            }

            // Save Pre-Loading setting
            settings.EnablePreLoading = PreLoadingToggle.IsChecked == true;

            // Save Network Remote Song Username setting
            settings.NetworkRemoteSongUsername = NetworkRemoteSongUsernameToggle.IsChecked == true;

            // Save Legacy Audio Channel Definition setting
            settings.IsLegacyAudioChannelDefinitionEnabled = LegacyAudioChannelToggle.IsChecked == true;

            // Save Change Song Library Drive settings
            bool oldLibraryDriveEnabled = settings.ChangeSongLibraryDriveEnabled;
            string oldLibraryDrivePath = settings.SongLibraryDrivePath ?? "";
            settings.ChangeSongLibraryDriveEnabled = ChangeSongLibraryDriveToggle.IsChecked == true;
            string drivePathText = SongLibraryDrivePathText.Text;
            settings.SongLibraryDrivePath = (drivePathText == Loc.T("Options.NoDirSelected")) ? "" : drivePathText.Trim();

            // Save Random Play settings
            settings.RandomPlayEnabled = RandomPlayToggle.IsChecked == true;
            if (RandomPlayCategoryComboBox.SelectedItem is ComboBoxItem catItem && int.TryParse(catItem.Tag.ToString(), out int category))
            {
                settings.RandomPlayCategory = category;
            }
            settings.RandomPlayFavoriteUser = RandomPlayFavoriteUserComboBox.SelectedItem?.ToString() ?? "";
            if (RandomPlayAudioChannelComboBox.SelectedItem is ComboBoxItem audioItem && int.TryParse(audioItem.Tag.ToString(), out int audioChannel))
            {
                settings.RandomPlayAudioChannel = audioChannel;
            }

            // Save Language settings
            settings.IsLanguageMultiSelect = LanguageMultiSelectToggle.IsChecked == true;
            if (UiLanguageComboBox.SelectedValue is string uiLanguage && uiLanguage != Loc.CurrentCulture)
            {
                // 語言檔先完整檢查，有問題就維持目前語言 (不存設定)，並列出有問題的項目
                if (Loc.TryApply(uiLanguage, out var languageProblems))
                {
                    settings.UiLanguage = uiLanguage;
                }
                else
                {
                    MessageBox.Show(Loc.FormatProblems(uiLanguage, languageProblems), Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Warning);
                    UiLanguageComboBox.SelectedValue = Loc.CurrentCulture;
                }
            }

            // Apply changes to the running application
            _parentWindow.NewSongDays = settings.NewSongDays;

            // Apply or remove cursor limiting based on setting
            if (settings.LimitCursorOnMain)
            {
                _parentWindow?.ApplyCursorLimit();
            }
            else
            {
                _parentWindow?.RemoveCursorLimit();
            }

            // Save settings to the JSON file
            SettingsManager.Instance.SaveSettings();

            // Rebuild song file paths only when the library path changed (36k songs; skip for unrelated settings)
            if (oldLibraryDriveEnabled != settings.ChangeSongLibraryDriveEnabled || oldLibraryDrivePath != (settings.SongLibraryDrivePath ?? ""))
            {
                // 已顯示的列表與已點歌單各自持有 FilePath 複本，也要換成新路徑才會立即生效
                _parentWindow?.RemapSongFilePaths(SongDatas.RebuildSongFilePaths());
            }

            // If random play was enabled, trigger delayed check if waiting list is empty and nothing is currently playing
            if (settings.RandomPlayEnabled && _parentWindow != null)
            {
                _parentWindow.StartDelayedRandomPlayWhenEmpty();
            }

            string appliedMessage = Loc.T("Options.Applied");
            if (playerEngineChanged)
            {
                appliedMessage += Environment.NewLine + Environment.NewLine + Loc.T("Options.PlayerEngineRestart");
            }
            MessageBox.Show(appliedMessage, Loc.T("Common.Done"), MessageBoxButton.OK, MessageBoxImage.Information);
            Hide();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private async void UpdateHttpStatusText()
        {
            int version = ++_httpQrVersion;
            var settings = SettingsManager.Instance.CurrentSettings;
            if (HttpServerToggle.IsChecked == true)
            {
                int port = settings.HttpServerPort;
                HttpStatusText.Text = Loc.T("Options.ServerStarting");
                try
                {
                    // ServerUrl / GetLocalIPAddress 會列舉網路介面，QR code 產生也耗時 → 全部移到背景
                    var (serverUrl, qrImage) = await Task.Run(() =>
                    {
                        string url = HttpServer.Instance?.ServerUrl ?? $"http://{HttpServer.GetLocalIPAddress()}:{port}";
                        return (url, CreateQrImage(url));
                    });

                    if (version != _httpQrVersion) return; // toggled again meanwhile

                    HttpStatusText.Text = Loc.T("Options.ServerRunning", serverUrl);
                    QrCodeImage.Source = qrImage;
                    QrCodeImage.Visibility = qrImage != null ? Visibility.Visible : Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("UpdateHttpStatusText failed", ex);
                }
            }
            else
            {
                HttpStatusText.Text = Loc.T("Options.ServerDisabled");
                QrCodeImage.Source = null;
                QrCodeImage.Visibility = Visibility.Collapsed;
            }
        }

        private void HttpServerToggle_StateChanged(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool isEnabled = HttpServerToggle.IsChecked == true;
            AppLogger.Log($"User setting changed: HttpServer {(isEnabled ? "enabled" : "disabled")}");
            UpdateHttpServerLabelStyle();
            UpdateHttpStatusText();
        }

        private void UpdateHttpServerLabelStyle()
        {
            // Adjust the label's appearance based on the toggle state
            bool isEnabled = HttpServerToggle.IsChecked == true;
            HttpServerLabel.Opacity = isEnabled ? 1.0 : 0.5;
        }

        /// <summary>
        /// 產生 QR code 圖片 (可在背景執行緒呼叫；回傳的圖片已 Freeze)。
        /// </summary>
        private static BitmapImage? CreateQrImage(string url)
        {
            try
            {
                using (var qrGenerator = new QRCodeGenerator())
                using (var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
                using (var qrCode = new QRCode(qrCodeData))
                using (var qrCodeImage = qrCode.GetGraphic(QrPixelsPerModule))
                {
                    return BitmapToImageSource(qrCodeImage);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to generate QR code", ex);
                return null;
            }
        }

        private static BitmapImage BitmapToImageSource(Bitmap bitmap)
        {
            using var memory = new MemoryStream();
            bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Bmp);
            memory.Position = 0;
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = memory;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze(); // Freeze for performance and thread safety
            return bitmapImage;
        }

        public void FuncBtnQuit_Click(object sender, RoutedEventArgs e)
        {
            ShowQuitOptionsDialog();
        }

        private void ExitProgramButton_Click(object sender, RoutedEventArgs e)
        {
            ShowQuitOptionsDialog();
        }

        private void ShowQuitOptionsDialog()
        {
            var optionsDialog = new Window
            {
                Title = "",
                Width = 480,
                Height = 280,
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = _parentWindow,
                ShowInTaskbar = false,
                Topmost = true
            }; 

            // 深色對話框：配色、字級、按鈕樣式皆取自 Themes 的 token (與主介面一致)
            var border = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("BrushBgSecondary"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BrushBorderControl"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("RadiusMedium"),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (System.Windows.Media.Color)FindResource("ColorShadow"),
                    Direction = 315,
                    ShadowDepth = 8,
                    Opacity = 0.3,
                    BlurRadius = 12
                }
            };

            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
            var icon = new TextBlock {
                Text = "⚠",
                FontSize = (double)FindResource("FontSizeXL"),
                Foreground = (System.Windows.Media.Brush)FindResource("BrushWarning"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 16, 0)
            };
            var title = new TextBlock {
                Text = Loc.T("Options.CloseOptions"),
                FontSize = (double)FindResource("FontSizeM"),
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("BrushTextMain")
            };
            headerPanel.Children.Add(icon);
            headerPanel.Children.Add(title);
            Grid.SetRow(headerPanel, 0);
            mainGrid.Children.Add(headerPanel);

            var message = new TextBlock {
                Text = Loc.T("Options.ChooseAction"),
                FontSize = (double)FindResource("FontSizeS"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = (System.Windows.Media.Brush)FindResource("BrushTextDim"),
                LineHeight = 28
            };
            Grid.SetRow(message, 1);
            mainGrid.Children.Add(message);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            
            // 按鈕沿用 CyberButton 系列樣式：移入/按下的疊色與光暈由樣式處理
            var cancelBtn = new Button {
                Content = Loc.T("Common.Cancel"),
                Style = (Style)FindResource("CyberButton"),
                Width = 100,
                Height = 48,
                Margin = new Thickness(0, 0, 12, 0),
                FontSize = (double)FindResource("FontSizeXS")
            };

            // 圖示 + 文字 (圖示顏色跟隨按鈕 Foreground)
            object IconLabel(PackIconKind kind, string text)
            {
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new PackIcon { Kind = kind, Width = 28, Height = 28, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                panel.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
                return panel;
            }

            // 關閉程式 (較輕)：金色外框 + 離開圖示；關閉電腦 (最重)：紅色實心 + 電源圖示。
            // 以顏色、填色方式與圖示三者區分，不只靠顏色。
            var quitAppBtn = new Button {
                Content = IconLabel(PackIconKind.ExitToApp, Loc.T("Options.CloseProgram")),
                Style = (Style)FindResource("CyberAccentGoldButton"),
                Width = 150,
                Height = 48,
                Margin = new Thickness(0, 0, 12, 0),
                FontSize = (double)FindResource("FontSizeXS")
            };

            var shutdownBtn = new Button {
                Content = IconLabel(PackIconKind.Power, Loc.T("Options.Shutdown")),
                Style = (Style)FindResource("CyberButton"),
                Width = 150,
                Height = 48,
                FontSize = (double)FindResource("FontSizeXS"),
                Background = (System.Windows.Media.Brush)FindResource("BrushDanger"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("BrushDanger"),
                Foreground = (System.Windows.Media.Brush)FindResource("BrushTextMain")
            };

            cancelBtn.Click += (s, ev) => optionsDialog.Close();
            
            quitAppBtn.Click += async (s, ev) =>
            {
                optionsDialog.Close();
                // Stop recording and wait for MP3 to complete on a background thread
                // BEFORE shutting down. This prevents the UI thread from being blocked
                // while NAudio's RecordingStopped callback tries to dispatch back to it.
                await Task.Run(() =>
                {
                    try
                    {
                        RecordingManager.Instance.StopRecording();
                        RecordingManager.Instance.WaitForRecordingToFinish();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("Error stopping recording before quit", ex);
                    }
                });
                Application.Current.Shutdown();
            };

            shutdownBtn.Click += async (s, ev) =>
            {
                optionsDialog.Close();
                // Stop recording and wait for MP3 to complete before shutting down PC
                await Task.Run(() =>
                {
                    try
                    {
                        RecordingManager.Instance.StopRecording();
                        RecordingManager.Instance.WaitForRecordingToFinish();
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("Error stopping recording before shutdown", ex);
                    }
                });
                Application.Current.Shutdown();
                // Shutdown the computer
                System.Diagnostics.Process.Start("shutdown", "/s /t 0");
            };

            buttonPanel.Children.Add(cancelBtn);
            buttonPanel.Children.Add(quitAppBtn);
            buttonPanel.Children.Add(shutdownBtn);
            Grid.SetRow(buttonPanel, 2);
            mainGrid.Children.Add(buttonPanel);

            border.Child = mainGrid;
            optionsDialog.Content = border;
            optionsDialog.ShowDialog();
        }

        private void LimitCursorToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("User setting changed: LimitCursor enabled");
            _parentWindow?.ApplyCursorLimit();
        }

        private void LimitCursorToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("User setting changed: LimitCursor disabled");
            _parentWindow?.RemoveCursorLimit();
        }

        private void PlayHistoryCountSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isInitializing) return;
            if (PlayHistoryCountValue != null && sender is Slider slider)
            {
                int newValue = (int)Math.Round(slider.Value);
                newValue = Math.Max(1, Math.Min(10, newValue));
                AppLogger.Log($"User setting changed: PlayHistoryCount = {newValue}");
                PlayHistoryCountValue.Text = newValue.ToString();
            }
        }

        private void SongSortMethodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            int sortMethod = SongSortMethodComboBox.SelectedIndex + 1;
            AppLogger.Log($"User setting changed: SongSortMethod = {sortMethod}");
        }

        private void AppLoggerToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.IsEnabled = true;
            AppLogger.Log("App logging enabled by user");
        }

        private void AppLoggerToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("App logging disabled by user");
            AppLogger.IsEnabled = false;
        }

        private void VideoRendererComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (VideoRendererComboBox.SelectedItem is ComboBoxItem item)
            {
                AppLogger.Log($"User setting changed: VideoRenderer = {item.Content}");
            }
        }

        private void AudioRendererComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (AudioRendererComboBox.SelectedItem != null)
            {
                AppLogger.Log($"User setting changed: AudioRenderer = {AudioRendererComboBox.SelectedItem}");
            }
        }

        private void EnableHWAccelToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("User setting changed: HWAccel enabled");
            HWAccelModeComboBox.IsEnabled = true;
        }

        private void EnableHWAccelToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("User setting changed: HWAccel disabled");
            HWAccelModeComboBox.IsEnabled = false;
        }

        private void HWAccelModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (HWAccelModeComboBox.SelectedItem is ComboBoxItem item)
            {
                AppLogger.Log($"User setting changed: HWAccelMode = {item.Content}");
            }
        }

        private void PublicServerPortTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private async void PublicServerPortTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Auto-generate QR code when port is valid
            if (PublicQrCodeImage == null || PublicIpStatusText == null) return;

            // 初始化時由 InitializeSettings → GeneratePublicQrCodeForPort 處理，避免重複產生兩次
            if (_isInitializing) return;

            if (int.TryParse(PublicServerPortTextBox.Text, out int port) && port > 0 && port <= 65535)
            {
                await ShowPublicQrAsync(port);
            }
            else
            {
                ++_publicQrVersion;
                PublicIpStatusText.Text = "";
                PublicQrCodeImage.Source = null;
                PublicQrCodeImage.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// 取得公網 IP 並產生 QR code；網路請求與圖片產生都在背景執行。
        /// </summary>
        private async Task ShowPublicQrAsync(int port)
        {
            int version = ++_publicQrVersion;
            try
            {
                PublicIpStatusText.Text = Loc.T("Options.PublicIpFetching");
                string publicIp = await Task.Run(() => HttpServer.GetPublicIPAddressAsync());
                string publicUrl = $"http://{publicIp}:{port}";
                var qrImage = await Task.Run(() => CreateQrImage(publicUrl));

                if (version != _publicQrVersion) return; // port changed meanwhile

                PublicIpStatusText.Text = Loc.T("Options.PublicIpUrl", publicUrl);
                PublicQrCodeImage.Source = qrImage;
                PublicQrCodeImage.Visibility = qrImage != null ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                if (version != _publicQrVersion) return;
                AppLogger.LogError("Failed to generate public QR code", ex);
                PublicIpStatusText.Text = Loc.T("Options.PublicIpFailed");
                PublicQrCodeImage.Source = null;
                PublicQrCodeImage.Visibility = Visibility.Collapsed;
            }
        }

        private async void GeneratePublicQrCodeForPort(int port)
        {
            if (port <= 0 || port > 65535) return;
            await ShowPublicQrAsync(port);
        }

        private void PreLoadingToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("Pre-loading feature enabled by user");
        }

        private void PreLoadingToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("Pre-loading feature disabled by user");
        }

        private void NetworkRemoteSongUsernameToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("Network remote song username recording enabled by user");
        }

        private void NetworkRemoteSongUsernameToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("Network remote song username recording disabled by user");
        }

        private void LegacyAudioChannelToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            bool isEnabled = LegacyAudioChannelToggle.IsChecked == true;
            AppLogger.Log($"User setting changed: LegacyAudioChannelDefinition {(isEnabled ? "enabled" : "disabled")}");
        }

        private void ChangeSongLibraryDriveToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("Change song library drive enabled by user");
        }

        private void ChangeSongLibraryDriveToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("Change song library drive disabled by user");
        }

        private void BrowseSongLibraryDrive_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Loc.T("Options.SelectLibraryDir"),
                Multiselect = false
            };
            
            // Set initial path if one is already selected
            string currentPath = SongLibraryDrivePathText.Text;
            if (!string.IsNullOrEmpty(currentPath) && currentPath != Loc.T("Options.NoDirSelected") && Directory.Exists(currentPath))
            {
                dialog.InitialDirectory = currentPath;
            }
            
            if (dialog.ShowDialog() == true)
            {
                SongLibraryDrivePathText.Text = dialog.FolderName;
                SongLibraryDrivePathText.ToolTip = dialog.FolderName;
                AppLogger.Log($"Song library drive path selected: {dialog.FolderName}");
            }
        }

        private void RandomPlayToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("Random play enabled by user");
        }

        private void RandomPlayToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            AppLogger.Log("Random play disabled by user");
        }

        private void RandomPlayCategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            UpdateRandomPlayFavoriteUserVisibility();
            
            if (RandomPlayCategoryComboBox.SelectedItem is ComboBoxItem item)
            {
                AppLogger.Log($"User setting changed: RandomPlayCategory = {item.Content}");
            }
        }

        private void RandomPlayAudioChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (RandomPlayAudioChannelComboBox.SelectedItem is ComboBoxItem item)
            {
                AppLogger.Log($"User setting changed: RandomPlayAudioChannel = {item.Content}");
            }
        }

        private void BrowseRecordingPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = Loc.T("Options.SelectRecordingDir"),
                Multiselect = false
            };

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentPath = RecordingPathText.Text;
            string initialPath = currentPath;

            // Resolve relative path to absolute relative to application base directory
            if (!string.IsNullOrEmpty(currentPath) && !System.IO.Path.IsPathRooted(currentPath))
            {
                initialPath = System.IO.Path.Combine(baseDir, currentPath);
            }

            if (System.IO.Directory.Exists(initialPath))
            {
                dialog.InitialDirectory = initialPath;
            }
            else
            {
                dialog.InitialDirectory = baseDir;
            }

            if (dialog.ShowDialog() == true)
            {
                RecordingPathText.Text = dialog.FolderName;
                RecordingPathText.ToolTip = dialog.FolderName;
                AppLogger.Log($"Recording save path selected: {dialog.FolderName}");
            }
        }

        private void EnableRecordingToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateRecordingUIState();
        }

        private void EnableRecordingToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;
            UpdateRecordingUIState();
        }

        private void UpdateRecordingUIState()
        {
            bool isEnabled = EnableRecordingToggle.IsChecked == true;
            RecordingDeviceBorder.IsEnabled = isEnabled;
            RecordingPathBorder.IsEnabled = isEnabled;
            RecordingDeviceBorder.Opacity = isEnabled ? 1.0 : 0.5;
            RecordingPathBorder.Opacity = isEnabled ? 1.0 : 0.5;
        }
    }
}
