using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using QRCoder;
using DirectShowLib;

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
            TextSettingsHandler.ApplyToResources(this);

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

            AppLogger.Log($"[Scaling Debug] Monitor WorkArea: {screenWorkWidth}x{screenWorkHeight}");
            AppLogger.Log($"[Scaling Debug] Uniform Scale: {uniformScale:F3}");
            AppLogger.Log($"[Scaling Debug] Final Window Size: {this.Width:F1}x{this.Height:F1}");
            
            InitializeAudioRenderer();
            InitializeSettings();
            _isInitializing = false;
        }

        private void InitializeAudioRenderer()
        {
            AudioRendererComboBox.Items.Clear();
            AudioRendererComboBox.Items.Add("Default DirectSound Device");
            
            try
            {
                var devices = DsDevice.GetDevicesOfCat(FilterCategory.AudioRendererCategory);
                foreach (var device in devices)
                {
                    AudioRendererComboBox.Items.Add(device.Name);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to list audio renderer devices", ex);
            }
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

            // Set Video Renderer
            VideoRendererComboBox.SelectedIndex = settings.VideoRendererType;

            // Set Audio Renderer
            AudioRendererComboBox.SelectedItem = settings.AudioRendererDevice;
            if (AudioRendererComboBox.SelectedIndex == -1 && AudioRendererComboBox.Items.Count > 0)
            {
                AudioRendererComboBox.SelectedIndex = 0;
            }

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
                ? "(尚未選擇目錄)" 
                : settings.SongLibraryDrivePath;

            // Set Random Play settings
            RandomPlayToggle.IsChecked = settings.RandomPlayEnabled;
            // Select category by matching Tag value (0, 1, 2, or 10)
            SelectComboBoxItemByTag(RandomPlayCategoryComboBox, settings.RandomPlayCategory);
            RandomPlayAudioChannelComboBox.SelectedIndex = settings.RandomPlayAudioChannel;
            InitializeFavoriteUserComboBox();
            UpdateRandomPlayFavoriteUserVisibility();

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
            this.Close();
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
            if (VideoRendererComboBox.SelectedItem is ComboBoxItem videoItem && int.TryParse(videoItem.Tag.ToString(), out int videoType))
            {
                settings.VideoRendererType = videoType;
            }

            if (AudioRendererComboBox.SelectedItem != null)
            {
                settings.AudioRendererDevice = AudioRendererComboBox.SelectedItem.ToString() ?? "Default DirectSound Device";
            }

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
            settings.ChangeSongLibraryDriveEnabled = ChangeSongLibraryDriveToggle.IsChecked == true;
            string drivePathText = SongLibraryDrivePathText.Text;
            settings.SongLibraryDrivePath = (drivePathText == "(尚未選擇目錄)") ? "" : drivePathText.Trim();

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

            // Rebuild song file paths to apply the new library path immediately
            SongDatas.RebuildSongFilePaths();

            MessageBox.Show("設定已套用", "完成", MessageBoxButton.OK, MessageBoxImage.Information);
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void UpdateHttpStatusText()
        {
            var settings = SettingsManager.Instance.CurrentSettings;
            if (HttpServerToggle.IsChecked == true)
            {
                string serverUrl = HttpServer.Instance?.ServerUrl ?? $"http://{HttpServer.GetLocalIPAddress()}:{settings.HttpServerPort}";
                HttpStatusText.Text = $"伺服器已啟用，可由此網址連線: {serverUrl}";
                GenerateAndDisplayQrCode(serverUrl);
                QrCodeImage.Visibility = Visibility.Visible;
            }
            else
            {
                HttpStatusText.Text = "伺服器未啟用。變更將在下次程式啟動時生效。";
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

        private void GenerateAndDisplayQrCode(string url)
        {
            try
            {
                using (var qrGenerator = new QRCodeGenerator())
                using (var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
                using (var qrCode = new QRCode(qrCodeData))
                using (var qrCodeImage = qrCode.GetGraphic(20))
                {
                    QrCodeImage.Source = BitmapToImageSource(qrCodeImage);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to generate QR code", ex);
                QrCodeImage.Source = null; // Clear image on error
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
                Width = 450,
                Height = 280,
                WindowStyle = System.Windows.WindowStyle.None,
                AllowsTransparency = true,
                Background = System.Windows.Media.Brushes.Transparent,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
                Owner = _parentWindow,
                ShowInTaskbar = false,
                Topmost = true
            }; 

            var border = new Border
            {
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(250, 250, 250)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Direction = 315,
                    ShadowDepth = 8,
                    Opacity = 0.3,
                    BlurRadius = 10
                }
            };

            var mainGrid = new Grid { Margin = new Thickness(20) };
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
            var icon = new TextBlock {
                Text = "⚠",
                FontSize = 32,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 152, 0)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 15, 0)
            };
            var title = new TextBlock {
                Text = "關閉選項",
                FontSize = 24,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60))
            };
            headerPanel.Children.Add(icon);
            headerPanel.Children.Add(title);
            Grid.SetRow(headerPanel, 0);
            mainGrid.Children.Add(headerPanel);

            var message = new TextBlock {
                Text = "請選擇要執行的操作：",
                FontSize = 18,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 20),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(80, 80, 80)),
                LineHeight = 24
            };
            Grid.SetRow(message, 1);
            mainGrid.Children.Add(message);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            
            var cancelBtn = new Button {
                Content = "取消",
                Width = 100,
                Height = 45,
                Margin = new Thickness(0, 0, 10, 0),
                FontSize = 16,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(60, 60, 60)),
                Cursor = Cursors.Hand
            };

            var quitAppBtn = new Button {
                Content = "關閉程式",
                Width = 120,
                Height = 45,
                Margin = new Thickness(0, 0, 10, 0),
                FontSize = 16,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54)),
                BorderThickness = new Thickness(0),
                Foreground = System.Windows.Media.Brushes.White,
                Cursor = Cursors.Hand
            };

            var shutdownBtn = new Button {
                Content = "關閉電腦",
                Width = 120,
                Height = 45,
                FontSize = 16,
                Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 81, 181)),
                BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 63, 159)),
                BorderThickness = new Thickness(2),
                Foreground = System.Windows.Media.Brushes.White,
                Cursor = Cursors.Hand
            };

            cancelBtn.Click += (s, ev) => optionsDialog.Close();
            
            quitAppBtn.Click += (s, ev) =>
            {
                optionsDialog.Close();
                Application.Current.Shutdown();
            };

            shutdownBtn.Click += (s, ev) =>
            {
                optionsDialog.Close();
                Application.Current.Shutdown();
                // Shutdown the computer
                System.Diagnostics.Process.Start("shutdown", "/s /t 0");
            };

            cancelBtn.MouseEnter += (s, ev) => cancelBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(230, 230, 230));
            cancelBtn.MouseLeave += (s, ev) => cancelBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(240, 240, 240));
            quitAppBtn.MouseEnter += (s, ev) => quitAppBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(229, 57, 44));
            quitAppBtn.MouseLeave += (s, ev) => quitAppBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
            shutdownBtn.MouseEnter += (s, ev) => shutdownBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(48, 63, 159));
            shutdownBtn.MouseLeave += (s, ev) => shutdownBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(63, 81, 181));

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

            if (int.TryParse(PublicServerPortTextBox.Text, out int port) && port > 0 && port <= 65535)
            {
                try
                {
                    PublicIpStatusText.Text = "正在取得公網IP...";
                    string publicIp = await HttpServer.GetPublicIPAddressAsync();
                    string publicUrl = $"http://{publicIp}:{port}";
                    PublicIpStatusText.Text = $"公網連線網址: {publicUrl}";
                    GenerateAndDisplayPublicQrCode(publicUrl);
                    PublicQrCodeImage.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to generate public QR code", ex);
                    PublicIpStatusText.Text = "無法取得公網IP，請檢查網路連線";
                    PublicQrCodeImage.Source = null;
                    PublicQrCodeImage.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                PublicIpStatusText.Text = "";
                PublicQrCodeImage.Source = null;
                PublicQrCodeImage.Visibility = Visibility.Collapsed;
            }
        }

        private async void GeneratePublicQrCodeForPort(int port)
        {
            if (port <= 0 || port > 65535) return;

            try
            {
                PublicIpStatusText.Text = "正在取得公網IP...";
                string publicIp = await HttpServer.GetPublicIPAddressAsync();
                string publicUrl = $"http://{publicIp}:{port}";
                PublicIpStatusText.Text = $"公網連線網址: {publicUrl}";
                GenerateAndDisplayPublicQrCode(publicUrl);
                PublicQrCodeImage.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to generate public QR code on init", ex);
                PublicIpStatusText.Text = "無法取得公網IP，請檢查網路連線";
                PublicQrCodeImage.Visibility = Visibility.Collapsed;
            }
        }

        private void GenerateAndDisplayPublicQrCode(string url)
        {
            try
            {
                using (var qrGenerator = new QRCodeGenerator())
                using (var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
                using (var qrCode = new QRCode(qrCodeData))
                using (var qrCodeImage = qrCode.GetGraphic(20))
                {
                    PublicQrCodeImage.Source = BitmapToImageSource(qrCodeImage);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to generate public QR code", ex);
                PublicQrCodeImage.Source = null;
            }
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
                Title = "選擇歌庫所在的目錄",
                Multiselect = false
            };
            
            // Set initial path if one is already selected
            string currentPath = SongLibraryDrivePathText.Text;
            if (!string.IsNullOrEmpty(currentPath) && currentPath != "(尚未選擇目錄)" && Directory.Exists(currentPath))
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
    }
}
