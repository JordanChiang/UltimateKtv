using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Win32;

namespace StyleSimulator
{
    public partial class MainWindow : Window
    {
        private StyleSettingsModel _model = new StyleSettingsModel();
        private bool _isInitializing = true;
        private int _currentTemplateIndex = 0; // 0: Song1, 1: Song2, 2: Song3, 3: Startup

        // Visual elements on player canvas
        private OutlinedTextBlock? _marqueeText;
        private OutlinedTextBlock? _songAddedText;
        private OutlinedTextBlock? _broadcastText;

        // Animation state
        private Storyboard? _currentStoryboard;
        private DispatcherTimer? _songAddedTimer;
        private DispatcherTimer? _broadcastTimer;
        private Queue<Action> _playbackQueue = new Queue<Action>();
        private bool _isSongAddedActive = false;
        private bool _isBroadcastActive = false;

        public MainWindow()
        {
            InitializeComponent();
            AdjustWindowSizeToScreen();
            Loaded += MainWindow_Loaded;
        }

        private void AdjustWindowSizeToScreen()
        {
            try
            {
                var workArea = SystemParameters.WorkArea;
                // If work area is constrained (such as 1080p screen at 125% or 150% scaling, or lower-res displays)
                if (workArea.Width <= 1536 || workArea.Height <= 864)
                {
                    WindowState = WindowState.Maximized;
                    // Provide safe fallback dimensions if user later un-maximizes (restores)
                    Width = Math.Min(1360, workArea.Width * 0.95);
                    Height = Math.Min(760, workArea.Height * 0.95);
                }
                else
                {
                    // For high-res or 100% scale displays (e.g. 1920x1080 at 100%, 1440p, 4K)
                    Width = Math.Min(1600, workArea.Width * 0.92);
                    Height = Math.Min(920, workArea.Height * 0.92);
                    Left = workArea.Left + (workArea.Width - Width) / 2;
                    Top = workArea.Top + (workArea.Height - Height) / 2;
                }
            }
            catch
            {
                // Fallback in case SystemParameters.WorkArea fails
                Width = 1400;
                Height = 820;
            }
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitFontLists();
            InitCanvasElements();

            // Load settings
            _model = StyleSettingsModel.Load();
            UpdateFilePathDisplay();

            ApplySettingsToUI();
            _isInitializing = false;

            // Update previews
            UpdateLivePreviews();
            SwitchActiveView(0);
            SelectMarqueeCategory(0);

            // Start an initial demo marquee
            StartMarquee(GetFormattedSong1(), false);
        }

        public class FontDisplayItem
        {
            public string DisplayName { get; set; } = "";
            public FontFamily FontFamily { get; set; } = null!;
            public string SourceName => FontFamily.Source;

            public override string ToString() => DisplayName;
        }

        public class FontWeightDisplayItem
        {
            public string DisplayName { get; set; } = "";
            public string Value { get; set; } = "";
            public FontWeight FontWeight { get; set; }

            public override string ToString() => DisplayName;
        }

        private static readonly Dictionary<string, string> WellKnownFontNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Microsoft JhengHei", "微軟正黑體" },
            { "Microsoft JhengHei UI", "微軟正黑體 UI" },
            { "Microsoft YaHei", "微軟雅黑" },
            { "Microsoft YaHei UI", "微軟雅黑 UI" },
            { "PMingLiU", "新細明體" },
            { "MingLiU", "細明體" },
            { "DFKai-SB", "標楷體" },
            { "SimSun", "宋體" },
            { "NSimSun", "新宋體" },
            { "SimHei", "黑體" },
            { "KaiTi", "楷體" },
            { "FangSong", "仿宋" },
            { "Malgun Gothic", "Malgun Gothic" },
            { "Meiryo", "Meiryo" },
            { "Meiryo UI", "Meiryo UI" },
            { "Yu Gothic", "Yu Gothic" },
            { "Yu Gothic UI", "Yu Gothic UI" }
        };

        private static string GetFontDisplayName(FontFamily fontFamily)
        {
            if (WellKnownFontNames.TryGetValue(fontFamily.Source, out var mappedName))
            {
                return mappedName;
            }

            var currentLang = System.Windows.Markup.XmlLanguage.GetLanguage(System.Globalization.CultureInfo.CurrentUICulture.IetfLanguageTag);
            var zhTwLang = System.Windows.Markup.XmlLanguage.GetLanguage("zh-TW");
            var zhHkLang = System.Windows.Markup.XmlLanguage.GetLanguage("zh-HK");
            var zhCnLang = System.Windows.Markup.XmlLanguage.GetLanguage("zh-CN");
            var enLang = System.Windows.Markup.XmlLanguage.GetLanguage("en-US");

            if (fontFamily.FamilyNames.TryGetValue(currentLang, out string? name) && !string.IsNullOrWhiteSpace(name))
                return name;
            if (fontFamily.FamilyNames.TryGetValue(zhTwLang, out name) && !string.IsNullOrWhiteSpace(name))
                return name;
            if (fontFamily.FamilyNames.TryGetValue(zhHkLang, out name) && !string.IsNullOrWhiteSpace(name))
                return name;
            if (fontFamily.FamilyNames.TryGetValue(zhCnLang, out name) && !string.IsNullOrWhiteSpace(name))
                return name;
            if (fontFamily.FamilyNames.TryGetValue(enLang, out name) && !string.IsNullOrWhiteSpace(name))
                return name;

            return fontFamily.Source;
        }

        private void InitFontLists()
        {
            var list = new List<FontDisplayItem>();
            var addedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var font in Fonts.SystemFontFamilies)
            {
                if (string.IsNullOrWhiteSpace(font.Source)) continue;
                if (!addedSources.Add(font.Source)) continue;

                string displayName = GetFontDisplayName(font);
                list.Add(new FontDisplayItem
                {
                    DisplayName = displayName,
                    FontFamily = font
                });
            }

            // Sort fonts: Traditional Chinese & English fonts
            var sortedFonts = list.OrderBy(f => f.DisplayName, StringComparer.CurrentCulture).ToList();

            CboFontFamily.ItemsSource = sortedFonts;
            CboSongAddedFontFamily.ItemsSource = sortedFonts;
            CboBroadcastFontFamily.ItemsSource = sortedFonts;
            CboUiFontFamily.ItemsSource = sortedFonts;

            var weights = new List<FontWeightDisplayItem>
            {
                new FontWeightDisplayItem { DisplayName = "標準", Value = "Normal", FontWeight = FontWeights.Normal },
                new FontWeightDisplayItem { DisplayName = "中等", Value = "Medium", FontWeight = FontWeights.Medium },
                new FontWeightDisplayItem { DisplayName = "半粗體", Value = "SemiBold", FontWeight = FontWeights.SemiBold },
                new FontWeightDisplayItem { DisplayName = "粗體", Value = "Bold", FontWeight = FontWeights.Bold },
                new FontWeightDisplayItem { DisplayName = "特粗體", Value = "ExtraBold", FontWeight = FontWeights.ExtraBold },
                new FontWeightDisplayItem { DisplayName = "極粗體", Value = "Black", FontWeight = FontWeights.Black }
            };
            CboFontWeight.ItemsSource = weights;
        }

        private void InitCanvasElements()
        {
            // 1. Top Row: Marquee Text (on 1920x1080 Full HD Canvas)
            _marqueeText = new OutlinedTextBlock
            {
                FontSize = 72,
                Text = "",
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 8,
                FontWeight = FontWeights.Bold
            };
            MarqueeCanvas.Children.Add(_marqueeText);
            Canvas.SetTop(_marqueeText, 0);
            Canvas.SetLeft(_marqueeText, -9999);

            // 2. Top Row: Song Added Notification (Shares same line with Marquee, mutually exclusive)
            _songAddedText = new OutlinedTextBlock
            {
                FontSize = 48,
                Text = "",
                Fill = Brushes.Gold,
                Stroke = Brushes.Black,
                StrokeThickness = 6,
                FontWeight = FontWeights.Bold,
                Opacity = 0
            };
            MarqueeCanvas.Children.Add(_songAddedText);
            Canvas.SetTop(_songAddedText, 0);
            Canvas.SetLeft(_songAddedText, 0);

            // 3. Bottom Right: Broadcast Notification (Independent, can coexist with top line)
            _broadcastText = new OutlinedTextBlock
            {
                FontSize = 48,
                Text = "",
                Fill = Brushes.Cyan,
                Stroke = Brushes.Black,
                StrokeThickness = 6,
                FontWeight = FontWeights.Bold,
                Opacity = 0
            };
            MarqueeCanvas.Children.Add(_broadcastText);
            Canvas.SetBottom(_broadcastText, 35);
            Canvas.SetRight(_broadcastText, 45);
        }

        private void UpdateFilePathDisplay()
        {
            if (CboConfigFileType.SelectedIndex == 0)
            {
                TxtActiveFilePath.Text = StyleSettingsModel.SettingsPath;
            }
            else
            {
                TxtActiveFilePath.Text = StyleSettingsModel.TextSettingsPath;
            }
        }

        private void CboConfigFileType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;

            int index = CboConfigFileType.SelectedIndex;
            UpdateFilePathDisplay();
            SwitchActiveView(index);
        }

        private void SwitchActiveView(int index)
        {
            if (index == 0) // settings.json
            {
                MarqueeTestBar.Visibility = Visibility.Visible;
                PlayerScreenView.Visibility = Visibility.Visible;
                UiInfoBar.Visibility = Visibility.Collapsed;
                KtvUiView.Visibility = Visibility.Collapsed;

                SettingsJsonGrid.Visibility = Visibility.Visible;
                TextSettingsGrid.Visibility = Visibility.Collapsed;
            }
            else // textsettings.json
            {
                MarqueeTestBar.Visibility = Visibility.Collapsed;
                PlayerScreenView.Visibility = Visibility.Collapsed;
                UiInfoBar.Visibility = Visibility.Visible;
                KtvUiView.Visibility = Visibility.Visible;

                SettingsJsonGrid.Visibility = Visibility.Collapsed;
                TextSettingsGrid.Visibility = Visibility.Visible;
            }
        }

        public void SelectMarqueeCategory(int categoryIndex)
        {
            // 0: Playback & Startup, 1: SongAdded, 2: Broadcast, 3: MockData
            CardPlayback.Visibility = categoryIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            CardSongAdded.Visibility = categoryIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
            CardBroadcast.Visibility = categoryIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            CardMockData.Visibility = categoryIndex == 3 ? Visibility.Visible : Visibility.Collapsed;

            // Highlight nav buttons
            SetNavButtonStyle(BtnNavPlayback, categoryIndex == 0);
            SetNavButtonStyle(BtnNavSongAdded, categoryIndex == 1);
            SetNavButtonStyle(BtnNavBroadcast, categoryIndex == 2);
            SetNavButtonStyle(BtnNavMockData, categoryIndex == 3);
        }

        private void SetNavButtonStyle(Button btn, bool isActive)
        {
            if (btn == null) return;
            if (isActive)
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3F51B5"));
                btn.Foreground = Brushes.White;
                btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#82B1FF"));
            }
            else
            {
                btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#232332"));
                btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#AAAAAA"));
                btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3A3A4C"));
            }
        }

        private void BtnNavPlayback_Click(object sender, RoutedEventArgs e) => SelectMarqueeCategory(0);
        private void BtnNavSongAdded_Click(object sender, RoutedEventArgs e) => SelectMarqueeCategory(1);
        private void BtnNavBroadcast_Click(object sender, RoutedEventArgs e) => SelectMarqueeCategory(2);
        private void BtnNavMockData_Click(object sender, RoutedEventArgs e) => SelectMarqueeCategory(3);

        private void ApplySettingsToUI()
        {
            _isInitializing = true;

            // settings.json - Playback Marquee
            SelectComboBoxFont(CboFontFamily, _model.MarqueeTextFontFamily);
            SelectComboBoxFontWeight(CboFontWeight, _model.MarqueeTextFontWeight);

            SldFontSize.Value = Math.Max(12, Math.Min(128, _model.MarqueeTextFontSize));
            SldStrokeThickness.Value = Math.Max(0, Math.Min(32, _model.MarqueeTextFontStrokeThickness));
            SldSpeed.Value = Math.Max(100, Math.Min(1000, _model.MarqueeTextTimeDuration));
            SldHoldTime.Value = Math.Max(0, Math.Min(5.0, _model.MarqueeTextTimeHold / 1000.0));
            SldPlayCount.Value = Math.Max(0, Math.Min(10, _model.MarqueeTextPlayCount));

            TxtFillColor.Text = _model.MarqueeTextFillColor;
            TxtStrokeColor.Text = _model.MarqueeTextStrokeColor;

            TxtTemplate1.Text = _model.MarqueeTextString1;
            TxtTemplate2.Text = _model.MarqueeTextString2;
            TxtTemplate3.Text = _model.MarqueeTextString3;
            TxtTemplateStartup.Text = _model.MarqueeTextStartup;

            // settings.json - Notifications
            TxtSongAddedTemplate.Text = _model.MarqueeSongAddedString;
            SelectComboBoxFont(CboSongAddedFontFamily, _model.MarqueeSongAddedFontFamily);
            SldSongAddedFontSize.Value = Math.Max(12, Math.Min(128, _model.MarqueeSongAddedFontSize));
            TxtSongAddedFillColor.Text = _model.MarqueeSongAddedFillColor;
            SldSongAddedDuration.Value = Math.Max(0, Math.Min(60, _model.MarqueeSongAddedTimeDuration));

            SelectComboBoxFont(CboBroadcastFontFamily, _model.MarqueeBroadcastFontFamily);
            SldBroadcastFontSize.Value = Math.Max(12, Math.Min(128, _model.MarqueeBroadcastFontSize));
            TxtBroadcastFillColor.Text = _model.MarqueeBroadcastFillColor;
            SldBroadcastDuration.Value = Math.Max(0, Math.Min(60, _model.MarqueeBroadcastTimeDuration));

            // textsettings.json - UI Fonts & Colors
            SelectComboBoxFont(CboUiFontFamily, _model.UiFontFamily);
            SldSongListFontSize.Value = Math.Max(16, Math.Min(72, _model.SongListFontSize));
            SldWaitingListFontSize.Value = Math.Max(12, Math.Min(60, _model.WaitingListFontSize));
            SldFuncBtnFontSize.Value = Math.Max(14, Math.Min(64, _model.FuncBtnFontSize));
            SldBottomButtonFontSize.Value = Math.Max(14, Math.Min(64, _model.BottomButtonFontSize));
            SldWebHostInfoFontSize.Value = Math.Max(12, Math.Min(60, _model.WebHostInfoFontSize));

            TxtAnnouncementFg.Text = _model.AnnouncementForegroundColor;
            TxtStaticTextFg.Text = _model.StaticTextForegroundColor;
            TxtWebHostInfoFg.Text = _model.WebHostInfoForegroundColor;
            TxtDataGridHeaderFg.Text = _model.DataGridColumnHeaderForegroundColor;

            TxtPrimaryColor.Text = _model.PrimaryColor;
            TxtPrimaryLightColor.Text = _model.PrimaryLightColor;
            TxtDataGridHeaderBg.Text = _model.DataGridHeaderBgColor;

            bool isAutoAccent = string.IsNullOrWhiteSpace(_model.AccentColor);
            if (ChkAutoAccentColor != null) ChkAutoAccentColor.IsChecked = isAutoAccent;
            if (TxtAccentColor != null)
            {
                TxtAccentColor.IsEnabled = !isAutoAccent;
                TxtAccentColor.Text = isAutoAccent ? _model.PrimaryColor : _model.AccentColor;
            }

            // settings.json - 點歌網址 (內部 IP, port)
            string localIp = !string.IsNullOrWhiteSpace(_model.HttpServerIp) && _model.HttpServerIp != "0.0.0.0"
                ? _model.HttpServerIp
                : GetLocalIPAddress();
            int port = _model.HttpServerPort > 0 ? _model.HttpServerPort : 8080;
            TxtMockLanIp.Text = $"{localIp}:{port}";

            if (_model.PublicServerPort > 0)
            {
                string wanIp = "220.222.1.1";
                if (!string.IsNullOrWhiteSpace(TxtMockWanIp.Text))
                {
                    var wanParts = TxtMockWanIp.Text.Split(':');
                    if (wanParts.Length > 0 && !string.IsNullOrWhiteSpace(wanParts[0]))
                        wanIp = wanParts[0];
                }
                TxtMockWanIp.Text = $"{wanIp}:{_model.PublicServerPort}";
            }

            _isInitializing = false;
            UpdateColorPreviews();
            UpdateLabels();
        }

        private static string GetLocalIPAddress()
        {
            try
            {
                string localIP = "127.0.0.1";
                foreach (var item in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (item.OperationalStatus == OperationalStatus.Up)
                    {
                        foreach (var ip in item.GetIPProperties().UnicastAddresses)
                        {
                            if (ip.Address.AddressFamily == AddressFamily.InterNetwork &&
                                !ip.Address.ToString().StartsWith("169.") &&
                                !ip.Address.ToString().StartsWith("127.") &&
                                !ip.Address.ToString().StartsWith("192.168.56.") &&
                                !ip.Address.ToString().StartsWith("172.16."))
                            {
                                return ip.Address.ToString();
                            }
                        }
                    }
                }
                return localIP;
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        private void SelectComboBoxFont(ComboBox cbo, string fontName)
        {
            if (cbo.ItemsSource is not List<FontDisplayItem> list || list.Count == 0) return;

            if (!string.IsNullOrWhiteSpace(fontName))
            {
                // 1. Exact match with DisplayName
                var match = list.FirstOrDefault(x => x.DisplayName.Equals(fontName, StringComparison.OrdinalIgnoreCase));
                if (match != null) { cbo.SelectedItem = match; return; }

                // 2. Exact match with Source / Font Name
                match = list.FirstOrDefault(x => x.SourceName.Equals(fontName, StringComparison.OrdinalIgnoreCase));
                if (match != null) { cbo.SelectedItem = match; return; }

                // 3. Check alias mapping
                if (WellKnownFontNames.TryGetValue(fontName, out var alias) ||
                    WellKnownFontNames.FirstOrDefault(kv => kv.Value.Equals(fontName, StringComparison.OrdinalIgnoreCase)).Key is { } key && (alias = key) != null)
                {
                    match = list.FirstOrDefault(x => x.DisplayName.Equals(alias, StringComparison.OrdinalIgnoreCase) || x.SourceName.Equals(alias, StringComparison.OrdinalIgnoreCase));
                    if (match != null) { cbo.SelectedItem = match; return; }
                }

                // 4. Partial contains match
                match = list.FirstOrDefault(x => x.DisplayName.Contains(fontName, StringComparison.OrdinalIgnoreCase) || x.SourceName.Contains(fontName, StringComparison.OrdinalIgnoreCase));
                if (match != null) { cbo.SelectedItem = match; return; }
            }

            // Default fallback: Try to select "微軟正黑體"
            var defaultMatch = list.FirstOrDefault(x => x.DisplayName.Contains("微軟正黑體") || x.SourceName.Contains("Microsoft JhengHei"));
            cbo.SelectedItem = defaultMatch ?? list[0];
        }

        private void SelectComboBoxFontWeight(ComboBox cbo, string weightName)
        {
            if (cbo.ItemsSource is not List<FontWeightDisplayItem> list || list.Count == 0) return;

            if (!string.IsNullOrWhiteSpace(weightName))
            {
                var match = list.FirstOrDefault(x =>
                    x.Value.Equals(weightName, StringComparison.OrdinalIgnoreCase) ||
                    x.DisplayName.Equals(weightName, StringComparison.OrdinalIgnoreCase));
                if (match != null) { cbo.SelectedItem = match; return; }

                if (weightName.Equals("Normal", StringComparison.OrdinalIgnoreCase) || weightName.Equals("一般", StringComparison.OrdinalIgnoreCase))
                    match = list.FirstOrDefault(x => x.Value == "Normal");
                else if (weightName.Equals("Bold", StringComparison.OrdinalIgnoreCase))
                    match = list.FirstOrDefault(x => x.Value == "Bold");

                if (match != null) { cbo.SelectedItem = match; return; }
            }

            cbo.SelectedItem = list.FirstOrDefault(x => x.Value == "Bold") ?? list[0];
        }

        private void UpdateSettingsFromUI()
        {
            // Settings.json
            if (CboFontFamily.SelectedItem is FontDisplayItem ff) _model.MarqueeTextFontFamily = ff.DisplayName;
            if (CboFontWeight.SelectedItem is FontWeightDisplayItem fwItem) _model.MarqueeTextFontWeight = fwItem.Value;
            _model.MarqueeTextFontSize = (int)Math.Round(SldFontSize.Value);
            _model.MarqueeTextFontStrokeThickness = (int)Math.Round(SldStrokeThickness.Value);
            _model.MarqueeTextFillColor = TxtFillColor.Text.Trim();
            _model.MarqueeTextStrokeColor = TxtStrokeColor.Text.Trim();
            _model.MarqueeTextTimeDuration = (int)Math.Round(SldSpeed.Value);
            _model.MarqueeTextTimeHold = (int)Math.Round(SldHoldTime.Value * 1000.0);
            _model.MarqueeTextPlayCount = (int)Math.Round(SldPlayCount.Value);

            _model.MarqueeTextString1 = TxtTemplate1.Text;
            _model.MarqueeTextString2 = TxtTemplate2.Text;
            _model.MarqueeTextString3 = TxtTemplate3.Text;
            _model.MarqueeTextStartup = TxtTemplateStartup.Text;

            _model.MarqueeSongAddedString = TxtSongAddedTemplate.Text;
            if (CboSongAddedFontFamily.SelectedItem is FontDisplayItem saff) _model.MarqueeSongAddedFontFamily = saff.DisplayName;
            _model.MarqueeSongAddedFontSize = (int)Math.Round(SldSongAddedFontSize.Value);
            _model.MarqueeSongAddedFillColor = TxtSongAddedFillColor.Text.Trim();
            _model.MarqueeSongAddedTimeDuration = (int)Math.Round(SldSongAddedDuration.Value);

            if (CboBroadcastFontFamily.SelectedItem is FontDisplayItem bff) _model.MarqueeBroadcastFontFamily = bff.DisplayName;
            _model.MarqueeBroadcastFontSize = (int)Math.Round(SldBroadcastFontSize.Value);
            _model.MarqueeBroadcastFillColor = TxtBroadcastFillColor.Text.Trim();
            _model.MarqueeBroadcastTimeDuration = (int)Math.Round(SldBroadcastDuration.Value);

            // Textsettings.json
            if (CboUiFontFamily.SelectedItem is FontDisplayItem uiff) _model.UiFontFamily = uiff.DisplayName;
            _model.SongListFontSize = (int)Math.Round(SldSongListFontSize.Value);
            _model.WaitingListFontSize = (int)Math.Round(SldWaitingListFontSize.Value);
            _model.FuncBtnFontSize = (int)Math.Round(SldFuncBtnFontSize.Value);
            _model.BottomButtonFontSize = (int)Math.Round(SldBottomButtonFontSize.Value);
            _model.WebHostInfoFontSize = (int)Math.Round(SldWebHostInfoFontSize.Value);

            _model.AnnouncementForegroundColor = TxtAnnouncementFg.Text.Trim();
            _model.StaticTextForegroundColor = TxtStaticTextFg.Text.Trim();
            _model.WebHostInfoForegroundColor = TxtWebHostInfoFg.Text.Trim();
            _model.DataGridColumnHeaderForegroundColor = TxtDataGridHeaderFg.Text.Trim();

            _model.PrimaryColor = TxtPrimaryColor.Text.Trim();
            _model.PrimaryLightColor = TxtPrimaryLightColor.Text.Trim();
            _model.DataGridHeaderBgColor = TxtDataGridHeaderBg.Text.Trim();
            if (TxtAccentColor != null)
            {
                _model.AccentColor = (ChkAutoAccentColor?.IsChecked == true) ? "" : TxtAccentColor.Text.Trim();
            }

            if (!string.IsNullOrWhiteSpace(TxtMockLanIp.Text))
            {
                var parts = TxtMockLanIp.Text.Trim().Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int p))
                {
                    _model.HttpServerPort = p;
                }
            }
            if (!string.IsNullOrWhiteSpace(TxtMockWanIp.Text))
            {
                var parts = TxtMockWanIp.Text.Trim().Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int p))
                {
                    _model.PublicServerPort = p;
                }
            }
        }

        private void UpdateLabels()
        {
            if (LblFontSize != null) LblFontSize.Text = $"{SldFontSize.Value:0} 像素";
            if (LblStrokeThickness != null) LblStrokeThickness.Text = $"{SldStrokeThickness.Value:0} 像素";
            if (LblSpeed != null) LblSpeed.Text = $"{SldSpeed.Value:0} 像素/秒";
            if (LblHoldTime != null) LblHoldTime.Text = $"{SldHoldTime.Value:0.#} 秒";
            if (LblPlayCount != null) LblPlayCount.Text = SldPlayCount.Value <= 0 ? "0 次 (關閉)" : $"{SldPlayCount.Value:0} 次";

            if (LblSongAddedFontSize != null) LblSongAddedFontSize.Text = $"{SldSongAddedFontSize.Value:0} 像素";
            if (LblSongAddedDuration != null) LblSongAddedDuration.Text = $"{SldSongAddedDuration.Value:0} 秒";

            if (LblBroadcastFontSize != null) LblBroadcastFontSize.Text = $"{SldBroadcastFontSize.Value:0} 像素";
            if (LblBroadcastDuration != null) LblBroadcastDuration.Text = $"{SldBroadcastDuration.Value:0} 秒";

            if (LblSongListFontSize != null) LblSongListFontSize.Text = $"{SldSongListFontSize.Value:0} 像素";
            if (LblWaitingListFontSize != null) LblWaitingListFontSize.Text = $"{SldWaitingListFontSize.Value:0} 像素";
            if (LblFuncBtnFontSize != null) LblFuncBtnFontSize.Text = $"{SldFuncBtnFontSize.Value:0} 像素";
            if (LblBottomButtonFontSize != null) LblBottomButtonFontSize.Text = $"{SldBottomButtonFontSize.Value:0} 像素";
            if (LblWebHostInfoFontSize != null) LblWebHostInfoFontSize.Text = $"{SldWebHostInfoFontSize.Value:0} 像素";
        }

        private void UpdateColorPreviews()
        {
            if (RectFillColor != null) RectFillColor.Background = ParseBrush(TxtFillColor.Text, Brushes.White);
            if (RectStrokeColor != null) RectStrokeColor.Background = ParseBrush(TxtStrokeColor.Text, Brushes.Black);
            if (RectSongAddedFillColor != null) RectSongAddedFillColor.Background = ParseBrush(TxtSongAddedFillColor.Text, Brushes.Gold);
            if (RectBroadcastFillColor != null) RectBroadcastFillColor.Background = ParseBrush(TxtBroadcastFillColor.Text, Brushes.Cyan);

            if (RectAnnouncementFg != null) RectAnnouncementFg.Background = ParseBrush(TxtAnnouncementFg.Text, Brushes.White);
            if (RectStaticTextFg != null) RectStaticTextFg.Background = ParseBrush(TxtStaticTextFg.Text, Brushes.White);
            if (RectWebHostInfoFg != null) RectWebHostInfoFg.Background = ParseBrush(TxtWebHostInfoFg.Text, Brushes.White);
            if (RectDataGridHeaderFg != null) RectDataGridHeaderFg.Background = ParseBrush(TxtDataGridHeaderFg.Text, Brushes.White);

            if (RectPrimaryColor != null) RectPrimaryColor.Background = ParseBrush(TxtPrimaryColor.Text, Brushes.Orange);
            if (RectAccentColor != null) RectAccentColor.Background = ParseBrush(TxtAccentColor.Text, Brushes.Cyan);
            if (RectPrimaryLightColor != null) RectPrimaryLightColor.Background = ParseBrush(TxtPrimaryLightColor.Text, Brushes.LightYellow);
            if (RectDataGridHeaderBg != null) RectDataGridHeaderBg.Background = ParseBrush(TxtDataGridHeaderBg.Text, Brushes.DarkSlateGray);
        }

        private void UpdateLivePreviews()
        {
            var uiFont = (CboUiFontFamily.SelectedItem as FontDisplayItem)?.FontFamily ?? new FontFamily("Microsoft JhengHei");
            var primaryBrush = ParseBrush(TxtPrimaryColor.Text, Brushes.Indigo);
            var primaryLightBrush = ParseBrush(TxtPrimaryLightColor.Text, Brushes.LightYellow);
            var headerBgHex = TxtDataGridHeaderBg.Text?.Trim();
            Brush gridHeaderBgBrush;
            if (string.IsNullOrWhiteSpace(headerBgHex) || string.Equals(headerBgHex, "#FF4A4A4A", StringComparison.OrdinalIgnoreCase))
            {
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(0xD1, 0x12, 0x18, 0x26), 0.0),
                        new GradientStop(Color.FromArgb(0xD9, 0x1C, 0x26, 0x3C), 1.0)
                    }
                };
                grad.Freeze();
                gridHeaderBgBrush = grad;
            }
            else
            {
                var baseCol = ParseColor(headerBgHex, Color.FromRgb(74, 74, 74));
                var grad = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(baseCol.A, (byte)(baseCol.R * 0.82), (byte)(baseCol.G * 0.82), (byte)(baseCol.B * 0.82)), 0.0),
                        new GradientStop(Color.FromArgb(baseCol.A, (byte)Math.Min(255, baseCol.R * 1.18 + 15), (byte)Math.Min(255, baseCol.G * 1.18 + 15), (byte)Math.Min(255, baseCol.B * 1.18 + 15)), 1.0)
                    }
                };
                grad.Freeze();
                gridHeaderBgBrush = grad;
            }
            var gridHeaderFgBrush = ParseBrush(TxtDataGridHeaderFg.Text, Brushes.White);
            var announcementFgBrush = ParseBrush(TxtAnnouncementFg.Text, Brushes.White);
            var staticTextFgBrush = ParseBrush(TxtStaticTextFg.Text, Brushes.White);
            var webHostInfoFgBrush = ParseBrush(TxtWebHostInfoFg.Text, Brushes.White);

            Color primaryColor = ParseColor(TxtPrimaryColor.Text, Color.FromRgb(0, 240, 255));
            Color accentColor = ParseColor(TxtAccentColor?.Text, primaryColor);
            if (accentColor.A == 0) accentColor = primaryColor;

            var accentBrush = new SolidColorBrush(accentColor);
            var accentDimBrush = new SolidColorBrush(Color.FromArgb(38, accentColor.R, accentColor.G, accentColor.B));
            var cyberActiveGradient = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(0, 1),
                GradientStops = new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(102, accentColor.R, accentColor.G, accentColor.B), 0.0),
                    new GradientStop(Color.FromArgb(64, (byte)(accentColor.R * 0.55), (byte)(accentColor.G * 0.55), (byte)(accentColor.B * 0.55)), 1.0)
                }
            };
            accentBrush.Freeze();
            accentDimBrush.Freeze();
            cyberActiveGradient.Freeze();

            // Update Window Resource Dictionary for DynamicResource bindings
            this.Resources["PrimaryBrush"] = primaryBrush;
            this.Resources["PrimaryLightBrush"] = primaryLightBrush;
            this.Resources["DataGridHeaderBgBrush"] = gridHeaderBgBrush;
            this.Resources["DataGridHeaderFgBrush"] = gridHeaderFgBrush;
            this.Resources["AnnouncementFgBrush"] = announcementFgBrush;
            this.Resources["StaticTextFgBrush"] = staticTextFgBrush;
            this.Resources["WebHostInfoFgBrush"] = webHostInfoFgBrush;
            this.Resources["ColorAccentCyan"] = accentColor;
            this.Resources["BrushAccentCyan"] = accentBrush;
            this.Resources["BrushAccentCyanDim"] = accentDimBrush;
            this.Resources["BrushCyberActiveGradient"] = cyberActiveGradient;
            this.Resources["UiFontFamily"] = uiFont;
            var appRes = Application.Current?.Resources;
            if (appRes != null)
            {
                appRes["UiFontFamily"] = uiFont;
            }

            // 13 Song List rows (matching updated screenshot)
            TextBlock?[] songRowNames = {
                TxtSongRow1, TxtSongRow2, TxtSongRow3, TxtSongRow4, TxtSongRow5,
                TxtSongRow6, TxtSongRow7, TxtSongRow8, TxtSongRow9, TxtSongRow10,
                TxtSongRow11, TxtSongRow12, TxtSongRow13
            };
            TextBlock?[] songRowSingers = {
                TxtSingerRow1, TxtSingerRow2, TxtSingerRow3, TxtSingerRow4, TxtSingerRow5,
                TxtSingerRow6, TxtSingerRow7, TxtSingerRow8, TxtSingerRow9, TxtSingerRow10,
                TxtSingerRow11, TxtSingerRow12, TxtSingerRow13
            };
            TextBlock?[] songRowLangs = {
                TxtLangRow1, TxtLangRow2, TxtLangRow3, TxtLangRow4, TxtLangRow5,
                TxtLangRow6, TxtLangRow7, TxtLangRow8, TxtLangRow9, TxtLangRow10,
                TxtLangRow11, TxtLangRow12, TxtLangRow13
            };

            for (int i = 0; i < songRowNames.Length; i++)
            {
                if (songRowNames[i] != null)
                {
                    songRowNames[i]!.FontFamily = uiFont;
                    songRowNames[i]!.FontSize = SldSongListFontSize.Value;
                    // In UltimateKtv (MainWindow.xaml & MainWindow.Converters.cs), non-Mandarin songs use LightSkyBlue
                    bool isNonMandarin = songRowLangs[i] != null && !string.Equals(songRowLangs[i]!.Text?.Trim(), "國語", StringComparison.Ordinal);
                    songRowNames[i]!.Foreground = isNonMandarin ? Brushes.LightSkyBlue : Brushes.White;
                }
                if (songRowSingers[i] != null) { songRowSingers[i]!.FontFamily = uiFont; songRowSingers[i]!.FontSize = SldSongListFontSize.Value; }
                if (songRowLangs[i] != null) { songRowLangs[i]!.FontFamily = uiFont; songRowLangs[i]!.FontSize = SldSongListFontSize.Value; }
            }

            // Waiting List rows & pagination
            TextBlock?[] waitSongs = { TxtWaitSong1, TxtWaitSong2, TxtWaitSong3 };
            TextBlock?[] waitSingers = { TxtWaitSinger1, TxtWaitSinger2, TxtWaitSinger3 };
            for (int i = 0; i < waitSongs.Length; i++)
            {
                if (waitSongs[i] != null) { waitSongs[i]!.FontFamily = uiFont; waitSongs[i]!.FontSize = SldWaitingListFontSize.Value; }
                if (waitSingers[i] != null) { waitSingers[i]!.FontFamily = uiFont; waitSingers[i]!.FontSize = SldWaitingListFontSize.Value; }
            }
            if (TxtWaitPageInfo != null)
            {
                TxtWaitPageInfo.FontFamily = uiFont;
                TxtWaitPageInfo.FontSize = SldWaitingListFontSize.Value;
                TxtWaitPageInfo.Foreground = staticTextFgBrush;
            }
            if (BorderWaitHeader != null) BorderWaitHeader.Background = gridHeaderBgBrush;
            if (TxtWaitHeaderCol1 != null) { TxtWaitHeaderCol1.FontFamily = uiFont; TxtWaitHeaderCol1.FontSize = SldWaitingListFontSize.Value; TxtWaitHeaderCol1.Foreground = gridHeaderFgBrush; }
            if (TxtWaitHeaderCol2 != null) { TxtWaitHeaderCol2.FontFamily = uiFont; TxtWaitHeaderCol2.FontSize = SldWaitingListFontSize.Value; TxtWaitHeaderCol2.Foreground = gridHeaderFgBrush; }

            var cyberBg = (Brush)FindResource("BrushBgSecondary");

            // 8 Top Function Buttons (FuncBtn5 "新進" is active with CyberActiveGradient, all outlined buttons use PrimaryColor)
            Button?[] funcBtns = { FuncBtn1, FuncBtn2, FuncBtn3, FuncBtn4, FuncBtn5, FuncBtn6, FuncBtn7, FuncBtn8 };
            foreach (var b in funcBtns)
            {
                if (b != null)
                {
                    b.FontFamily = uiFont;
                    b.FontSize = SldFuncBtnFontSize.Value;
                    if (b == FuncBtn5)
                    {
                        // Active Mode Button: uses CyberActiveGradient background & White text with accent border
                        b.Background = cyberActiveGradient;
                        b.Foreground = Brushes.White;
                        b.BorderBrush = accentBrush;
                        b.BorderThickness = new Thickness(1.5);
                        b.FontWeight = FontWeights.Bold;
                        b.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accentColor, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 };
                    }
                    else
                    {
                        b.Background = cyberBg;
                        b.Foreground = primaryBrush;
                        b.BorderBrush = primaryBrush;
                        b.BorderThickness = new Thickness(1);
                        b.FontWeight = FontWeights.SemiBold;
                        b.Effect = null;
                    }
                }
            }

            // 5 Category Filter Buttons (FilterBtn1 "國語-單曲" is active with CyberActiveGradient & White text)
            Button?[] filterBtns = { FilterBtn1, FilterBtn2, FilterBtn3, FilterBtn4, FilterBtn5 };
            foreach (var b in filterBtns)
            {
                if (b != null)
                {
                    b.FontFamily = uiFont;
                    b.FontSize = SldFuncBtnFontSize.Value;
                    if (b == FilterBtn1)
                    {
                        // Selected Category Tab: uses CyberActiveGradient background & White text with accent border
                        b.Background = cyberActiveGradient;
                        b.Foreground = Brushes.White;
                        b.BorderBrush = accentBrush;
                        b.BorderThickness = new Thickness(1.5);
                        b.FontWeight = FontWeights.Bold;
                        b.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accentColor, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 };
                    }
                    else
                    {
                        b.Background = cyberBg;
                        b.Foreground = primaryBrush;
                        b.BorderBrush = primaryBrush;
                        b.BorderThickness = new Thickness(1);
                        b.FontWeight = FontWeights.SemiBold;
                        b.Effect = null;
                    }
                }
            }

            // Bottom Player Control & Pagination Buttons (Outlined with PrimaryColor, only BtnMusic "伴唱" is active)
            Button?[] bottomBtns = { BtnPause, BtnRepeat, BtnVocal, BtnMusic, BtnPageUp, BtnPageDown };
            foreach (var b in bottomBtns)
            {
                if (b != null)
                {
                    b.FontFamily = uiFont;
                    b.FontSize = SldBottomButtonFontSize.Value;
                    if (b == BtnMusic)
                    {
                        // Active toggle: only "伴唱" is pressed
                        b.Background = cyberActiveGradient;
                        b.Foreground = Brushes.White;
                        b.BorderBrush = accentBrush;
                        b.BorderThickness = new Thickness(1.5);
                        b.FontWeight = FontWeights.Bold;
                        b.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = accentColor, BlurRadius = 16, ShadowDepth = 0, Opacity = 0.6 };
                    }
                    else
                    {
                        b.Background = cyberBg;
                        b.Foreground = primaryBrush;
                        b.BorderBrush = primaryBrush;
                        b.BorderThickness = new Thickness(1);
                        b.FontWeight = FontWeights.SemiBold;
                        b.Effect = null;
                    }
                }
            }

            // Skip Song Button: Matches LargePlayerControlButtonStyle (uses primaryBrush and textsettings.json)
            if (BtnSkipSong != null)
            {
                BtnSkipSong.FontFamily = uiFont;
                BtnSkipSong.FontSize = SldBottomButtonFontSize.Value;
                BtnSkipSong.Background = cyberBg;
                BtnSkipSong.Foreground = primaryBrush;
                BtnSkipSong.BorderBrush = primaryBrush;
                BtnSkipSong.BorderThickness = new Thickness(1);
                BtnSkipSong.FontWeight = FontWeights.SemiBold;
                BtnSkipSong.Effect = null;
            }

            // Waiting List Pagination Buttons: Font size 26
            if (BtnWaitPageUp != null)
            {
                BtnWaitPageUp.FontFamily = uiFont;
                BtnWaitPageUp.FontSize = 26;
                BtnWaitPageUp.BorderBrush = primaryBrush;
                BtnWaitPageUp.Foreground = primaryBrush;
                BtnWaitPageUp.Background = cyberBg;
            }
            if (BtnWaitPageDown != null)
            {
                BtnWaitPageDown.FontFamily = uiFont;
                BtnWaitPageDown.FontSize = 26;
                BtnWaitPageDown.BorderBrush = primaryBrush;
                BtnWaitPageDown.Foreground = primaryBrush;
                BtnWaitPageDown.Background = cyberBg;
            }

            // Bottom Right Pagination Buttons: Center text alignment
            if (BtnPageUp != null) { BtnPageUp.Padding = new Thickness(0); BtnPageUp.HorizontalContentAlignment = HorizontalAlignment.Center; BtnPageUp.VerticalContentAlignment = VerticalAlignment.Center; }
            if (BtnPageDown != null) { BtnPageDown.Padding = new Thickness(0); BtnPageDown.HorizontalContentAlignment = HorizontalAlignment.Center; BtnPageDown.VerticalContentAlignment = VerticalAlignment.Center; }

            if (IconLock != null) IconLock.Foreground = primaryBrush;
            if (IconMenu != null) IconMenu.Foreground = primaryBrush;
            if (VideoProgressSliderPreview != null) VideoProgressSliderPreview.Foreground = primaryBrush;
            if (VolumeSliderPreview != null) VolumeSliderPreview.Foreground = primaryBrush;

            if (TxtGlobalPageInfo != null)
            {
                TxtGlobalPageInfo.FontFamily = uiFont;
                TxtGlobalPageInfo.FontSize = SldBottomButtonFontSize.Value;
                TxtGlobalPageInfo.Foreground = staticTextFgBrush;
            }

            if (TxtClockIcon != null) TxtClockIcon.Foreground = staticTextFgBrush;
            if (TxtClockTime != null) { TxtClockTime.FontFamily = uiFont; TxtClockTime.Foreground = staticTextFgBrush; }

            // Frames & Accents
            if (BorderKtvFrame != null) BorderKtvFrame.BorderBrush = primaryBrush;
            if (BorderHeader != null) BorderHeader.BorderBrush = primaryBrush;
            if (BorderSongList != null) BorderSongList.BorderBrush = primaryBrush;
            if (BorderWaitList != null) BorderWaitList.BorderBrush = primaryBrush;
            if (BorderGridHeader != null) BorderGridHeader.Background = gridHeaderBgBrush;

            // Row 1 Singer Highlight (showcases PrimaryLightColor)
            if (BorderSingerRow1 != null) BorderSingerRow1.Background = primaryLightBrush;
            if (TxtSingerRow1 != null) TxtSingerRow1.Foreground = Brushes.Black;

            if (TxtGridHeaderCol1 != null) { TxtGridHeaderCol1.FontFamily = uiFont; TxtGridHeaderCol1.FontSize = SldSongListFontSize.Value; TxtGridHeaderCol1.Foreground = gridHeaderFgBrush; }
            if (TxtGridHeaderCol2 != null) { TxtGridHeaderCol2.FontFamily = uiFont; TxtGridHeaderCol2.FontSize = SldSongListFontSize.Value; TxtGridHeaderCol2.Foreground = gridHeaderFgBrush; }
            if (TxtGridHeaderCol3 != null) { TxtGridHeaderCol3.FontFamily = uiFont; TxtGridHeaderCol3.FontSize = SldSongListFontSize.Value; TxtGridHeaderCol3.Foreground = gridHeaderFgBrush; }

            if (TxtPreviewAnnouncement != null) { TxtPreviewAnnouncement.FontFamily = uiFont; TxtPreviewAnnouncement.Foreground = announcementFgBrush; }
            if (TxtPreviewWebHostInfo != null) { TxtPreviewWebHostInfo.FontFamily = uiFont; TxtPreviewWebHostInfo.Foreground = webHostInfoFgBrush; TxtPreviewWebHostInfo.FontSize = SldWebHostInfoFontSize.Value; }
            if (TxtPreviewStaticText != null) { TxtPreviewStaticText.FontFamily = uiFont; TxtPreviewStaticText.Foreground = staticTextFgBrush; }

            // Update live marquee running properties
            if (_marqueeText != null)
            {
                if (CboFontFamily.SelectedItem is FontDisplayItem ff) _marqueeText.FontFamily = ff.FontFamily;
                _marqueeText.FontWeight = ParseFontWeight(CboFontWeight.SelectedItem);
                _marqueeText.FontSize = SldFontSize.Value;
                _marqueeText.StrokeThickness = SldStrokeThickness.Value;
                _marqueeText.Fill = ParseBrush(TxtFillColor.Text, Brushes.White);
                _marqueeText.Stroke = ParseBrush(TxtStrokeColor.Text, Brushes.Black);
            }

            if (_songAddedText != null && _isSongAddedActive)
            {
                if (CboSongAddedFontFamily.SelectedItem is FontDisplayItem saff) _songAddedText.FontFamily = saff.FontFamily;
                _songAddedText.FontSize = SldSongAddedFontSize.Value;
                _songAddedText.Fill = ParseBrush(TxtSongAddedFillColor.Text, Brushes.Gold);
                _songAddedText.StrokeThickness = Math.Max(2, SldSongAddedFontSize.Value / 8.0);
                _songAddedText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                double textWidth = _songAddedText.DesiredSize.Width;
                double canvasWidth = MarqueeCanvas.Width > 0 ? MarqueeCanvas.Width : (MarqueeCanvas.ActualWidth > 0 ? MarqueeCanvas.ActualWidth : 1920);
                Canvas.SetLeft(_songAddedText, Math.Max(20, (canvasWidth - textWidth) / 2));
            }

            if (_broadcastText != null && _isBroadcastActive)
            {
                if (CboBroadcastFontFamily.SelectedItem is FontDisplayItem bff) _broadcastText.FontFamily = bff.FontFamily;
                _broadcastText.FontSize = SldBroadcastFontSize.Value;
                _broadcastText.Fill = ParseBrush(TxtBroadcastFillColor.Text, Brushes.Cyan);
                _broadcastText.StrokeThickness = Math.Max(2, SldBroadcastFontSize.Value / 8.0);
            }
        }

        private Brush ParseBrush(string colorStr, Brush fallback)
        {
            if (string.IsNullOrWhiteSpace(colorStr)) return fallback;
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(colorStr);
                return new SolidColorBrush(color);
            }
            catch
            {
                return fallback;
            }
        }

        private Color ParseColor(string? colorStr, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(colorStr)) return fallback;
            try
            {
                return (Color)ColorConverter.ConvertFromString(colorStr.Trim());
            }
            catch
            {
                return fallback;
            }
        }

        private FontWeight ParseFontWeight(object? item)
        {
            if (item is FontWeightDisplayItem fwItem) return fwItem.FontWeight;
            if (item is string weightStr)
            {
                switch (weightStr.Trim())
                {
                    case "Medium": case "中等": return FontWeights.Medium;
                    case "SemiBold": case "半粗體": case "中黑": return FontWeights.SemiBold;
                    case "Bold": case "粗體": return FontWeights.Bold;
                    case "ExtraBold": case "特粗體": case "超粗體": return FontWeights.ExtraBold;
                    case "Black": case "極粗體": case "重黑": return FontWeights.Black;
                    default: return FontWeights.Normal;
                }
            }
            return FontWeights.Normal;
        }

        private bool _isHarmonizing = false;

        private void TxtPrimaryColor_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;

            if (ChkAutoAccentColor != null && ChkAutoAccentColor.IsChecked == true && TxtAccentColor != null)
            {
                TxtAccentColor.Text = TxtPrimaryColor.Text.Trim();
            }

            if (ChkAutoHarmonize != null && ChkAutoHarmonize.IsChecked == true && !_isHarmonizing)
            {
                HarmonizePalette(TxtPrimaryColor.Text.Trim(), false);
            }

            ParamChanged(sender, e);
        }

        private void BtnHarmonize_Click(object sender, RoutedEventArgs e)
        {
            HarmonizePalette(TxtPrimaryColor.Text.Trim(), true);
        }

        private void HarmonizePalette(string primaryHex, bool forceNotify)
        {
            if (string.IsNullOrWhiteSpace(primaryHex)) return;

            Color primaryColor;
            try
            {
                primaryColor = (Color)ColorConverter.ConvertFromString(primaryHex);
            }
            catch
            {
                return;
            }

            _isHarmonizing = true;
            try
            {
                ColorToHsl(primaryColor, out double h, out double s, out double l);

                // Derive complementary shades
                // Light: soft high-lightness pastel (85% lightness, gentle saturation) for singer tag
                var lightColor = HslToColor(h, Math.Clamp(s * 0.45, 0.15, 0.6), Math.Clamp(0.85, 0.75, 0.92));
                // DataGrid header bg: deep slate tone tinted with primary hue
                var gridHeaderBgColor = HslToColor(h, Math.Min(0.5, s * 0.5), Math.Clamp(l * 0.55, 0.18, 0.38));
                // Static text: subtle soft tint or light contrast
                var staticTextFgColor = HslToColor(h, Math.Clamp(s * 0.3, 0.1, 0.4), Math.Clamp(0.88, 0.8, 0.94));

                if (TxtPrimaryLightColor != null) TxtPrimaryLightColor.Text = ColorToHex(lightColor);
                if (TxtDataGridHeaderBg != null) TxtDataGridHeaderBg.Text = ColorToHex(gridHeaderBgColor);
                if (TxtStaticTextFg != null) TxtStaticTextFg.Text = ColorToHex(staticTextFgColor);
                if (ChkAutoAccentColor != null && ChkAutoAccentColor.IsChecked == true && TxtAccentColor != null)
                {
                    TxtAccentColor.Text = primaryHex;
                }
            }
            finally
            {
                _isHarmonizing = false;
            }

            UpdateLabels();
            UpdateColorPreviews();
            UpdateSettingsFromUI();
            UpdateLivePreviews();

            if (forceNotify)
            {
                MessageBox.Show($"已依主要色彩 [{primaryHex}] 完成全套色階調配！\n(包含淺色標籤與標題列底色)", "調配成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private static string ColorToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

        private static void ColorToHsl(Color c, out double h, out double s, out double l)
        {
            double r = c.R / 255.0;
            double g = c.G / 255.0;
            double b = c.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            l = (max + min) / 2.0;

            if (delta == 0)
            {
                h = 0;
                s = 0;
            }
            else
            {
                s = l > 0.5 ? delta / (2.0 - max - min) : delta / (max + min);

                if (max == r)
                    h = ((g - b) / delta) + (g < b ? 6 : 0);
                else if (max == g)
                    h = ((b - r) / delta) + 2;
                else
                    h = ((r - g) / delta) + 4;

                h /= 6.0;
            }
        }

        private static Color HslToColor(double h, double s, double l)
        {
            double r, g, b;

            if (s == 0)
            {
                r = g = b = l;
            }
            else
            {
                double q = l < 0.5 ? l * (1.0 + s) : l + s - l * s;
                double p = 2.0 * l - q;
                r = HueToRgb(p, q, h + 1.0 / 3.0);
                g = HueToRgb(p, q, h);
                b = HueToRgb(p, q, h - 1.0 / 3.0);
            }

            return Color.FromRgb(
                (byte)Math.Clamp((int)Math.Round(r * 255), 0, 255),
                (byte)Math.Clamp((int)Math.Round(g * 255), 0, 255),
                (byte)Math.Clamp((int)Math.Round(b * 255), 0, 255));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6.0) return p + (q - p) * 6.0 * t;
            if (t < 1.0 / 2.0) return q;
            if (t < 2.0 / 3.0) return p + (q - p) * (2.0 / 3.0 - t) * 6.0;
            return p;
        }

        private string GetCurrentFormattedMarqueeText()
        {
            return _currentTemplateIndex switch
            {
                0 => GetFormattedSong1(),
                1 => GetFormattedSong2(),
                2 => GetFormattedSong3(),
                3 => GetFormattedStartup(),
                _ => GetFormattedSong1()
            };
        }

        private void TxtTemplate_GotFocus(object sender, RoutedEventArgs e)
        {
            if (_isInitializing) return;

            int newIndex = _currentTemplateIndex;
            if (ReferenceEquals(sender, TxtTemplate1)) newIndex = 0;
            else if (ReferenceEquals(sender, TxtTemplate2)) newIndex = 1;
            else if (ReferenceEquals(sender, TxtTemplate3)) newIndex = 2;
            else if (ReferenceEquals(sender, TxtTemplateStartup)) newIndex = 3;

            if (newIndex != _currentTemplateIndex)
            {
                _currentTemplateIndex = newIndex;
                if (CboConfigFileType != null && CboConfigFileType.SelectedIndex == 0 &&
                    CardPlayback != null && CardPlayback.Visibility == Visibility.Visible &&
                    !_isSongAddedActive)
                {
                    string textToPlay = GetCurrentFormattedMarqueeText();
                    if (!string.IsNullOrWhiteSpace(textToPlay))
                    {
                        StartMarquee(textToPlay, false);
                    }
                }
            }
        }

        private void ParamChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;

            if (ReferenceEquals(sender, TxtTemplate1)) _currentTemplateIndex = 0;
            else if (ReferenceEquals(sender, TxtTemplate2)) _currentTemplateIndex = 1;
            else if (ReferenceEquals(sender, TxtTemplate3)) _currentTemplateIndex = 2;
            else if (ReferenceEquals(sender, TxtTemplateStartup)) _currentTemplateIndex = 3;

            UpdateLabels();
            UpdateColorPreviews();
            UpdateSettingsFromUI();
            UpdateLivePreviews();

            if (CboConfigFileType != null && CboConfigFileType.SelectedIndex == 0 &&
                CardPlayback != null && CardPlayback.Visibility == Visibility.Visible &&
                !_isSongAddedActive)
            {
                string textToPlay = GetCurrentFormattedMarqueeText();
                if (!string.IsNullOrWhiteSpace(textToPlay))
                {
                    StartMarquee(textToPlay, false);
                }
            }
        }

        #region Native Win32 ChooseColor Picker (Zero external dependency)
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct CHOOSECOLOR
        {
            public int lStructSize;
            public IntPtr hwndOwner;
            public IntPtr hInstance;
            public int rgbResult;
            public IntPtr lpCustColors;
            public int Flags;
            public IntPtr lCustData;
            public IntPtr lpfnHook;
            public IntPtr lpTemplateName;
        }

        [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool ChooseColor(ref CHOOSECOLOR cc);

        private const int CC_FULLOPEN = 0x00000002;
        private const int CC_RGBINIT = 0x00000001;
        private static readonly int[] _customColors = new int[16];
        private static readonly GCHandle _customColorsHandle = GCHandle.Alloc(_customColors, GCHandleType.Pinned);

        private void PickColor(TextBox targetTextBox)
        {
            var helper = new WindowInteropHelper(this);
            var cc = new CHOOSECOLOR();
            cc.lStructSize = Marshal.SizeOf(typeof(CHOOSECOLOR));
            cc.hwndOwner = helper.Handle;
            cc.lpCustColors = _customColorsHandle.AddrOfPinnedObject();
            cc.Flags = CC_FULLOPEN | CC_RGBINIT;

            try
            {
                if (!string.IsNullOrWhiteSpace(targetTextBox.Text))
                {
                    var c = (Color)ColorConverter.ConvertFromString(targetTextBox.Text.Trim());
                    cc.rgbResult = (c.R) | (c.G << 8) | (c.B << 16);
                }
            }
            catch { }

            if (ChooseColor(ref cc))
            {
                byte r = (byte)(cc.rgbResult & 0xFF);
                byte g = (byte)((cc.rgbResult >> 8) & 0xFF);
                byte b = (byte)((cc.rgbResult >> 16) & 0xFF);
                targetTextBox.Text = $"#{r:X2}{g:X2}{b:X2}";
            }
        }

        private void RectFillColor_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtFillColor);
        private void RectStrokeColor_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtStrokeColor);
        private void RectSongAddedFillColor_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtSongAddedFillColor);
        private void RectBroadcastFillColor_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtBroadcastFillColor);

        private void RectAnnouncementFg_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtAnnouncementFg);
        private void RectStaticTextFg_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtStaticTextFg);
        private void RectWebHostInfoFg_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtWebHostInfoFg);
        private void RectDataGridHeaderFg_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtDataGridHeaderFg);

        private void RectPrimaryColor_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtPrimaryColor);
        private void RectAccentColor_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (ChkAutoAccentColor.IsChecked == true)
            {
                ChkAutoAccentColor.IsChecked = false;
                TxtAccentColor.IsEnabled = true;
            }
            PickColor(TxtAccentColor);
        }
        private void ChkAutoAccentColor_Click(object sender, RoutedEventArgs e)
        {
            if (ChkAutoAccentColor.IsChecked == true)
            {
                TxtAccentColor.IsEnabled = false;
                TxtAccentColor.Text = TxtPrimaryColor.Text.Trim();
            }
            else
            {
                TxtAccentColor.IsEnabled = true;
            }
            ParamChanged(sender, e);
        }
        private void RectPrimaryLightColor_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtPrimaryLightColor);
        private void RectDataGridHeaderBg_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtDataGridHeaderBg);
        #endregion

        #region String Formatting Helpers
        private string GetFormattedSong1()
        {
            return FormatMarqueeString(TxtTemplate1.Text, TxtMockSong.Text, TxtMockSinger.Text, TxtMockNextSong.Text, TxtMockNextSinger.Text, TxtMockLanIp.Text, TxtMockWanIp.Text, TxtMockOrderedBy.Text.Trim(), TxtMockNextOrderedBy?.Text.Trim() ?? "");
        }

        private string GetFormattedSong2()
        {
            return FormatMarqueeString(TxtTemplate2.Text, TxtMockSong.Text, TxtMockSinger.Text, "", "", TxtMockLanIp.Text, TxtMockWanIp.Text, TxtMockOrderedBy.Text.Trim(), "");
        }

        private string GetFormattedSong3()
        {
            return FormatMarqueeString(TxtTemplate3.Text, TxtMockSong.Text, TxtMockSinger.Text, "", "", TxtMockLanIp.Text, TxtMockWanIp.Text, TxtMockOrderedBy.Text.Trim(), "");
        }

        private string GetFormattedStartup()
        {
            return FormatMarqueeString(TxtTemplateStartup.Text, "", "", "", "", TxtMockLanIp.Text, TxtMockWanIp.Text, "", "");
        }

        private string GetFormattedSongAdded()
        {
            return FormatMarqueeString(TxtSongAddedTemplate.Text, TxtMockSong.Text, TxtMockSinger.Text, "", "", TxtMockLanIp.Text, TxtMockWanIp.Text, TxtMockOrderedBy.Text.Trim(), "");
        }

        private string FormatMarqueeString(string template, string song, string singer, string nextSong, string nextSinger, string lanIp, string wanIp, string orderedBy, string nextOrderedBy = "")
        {
            if (string.IsNullOrEmpty(template)) return "";

            string displayOrderedBy = (!string.IsNullOrWhiteSpace(orderedBy) && orderedBy != "本機" && orderedBy != "隨機播放" && orderedBy != "網路點歌") ? orderedBy : "";
            string displayNextOrderedBy = (!string.IsNullOrWhiteSpace(nextOrderedBy) && nextOrderedBy != "本機" && nextOrderedBy != "隨機播放" && nextOrderedBy != "網路點歌") ? nextOrderedBy : "";

            return template
                .Replace("{0}", song ?? "")
                .Replace("{1}", singer ?? "")
                .Replace("{2}", nextSong ?? "")
                .Replace("{3}", nextSinger ?? "")
                .Replace("{4}", lanIp ?? "")
                .Replace("{5}", wanIp ?? "")
                .Replace("{6}", displayOrderedBy)
                .Replace("{7}", displayNextOrderedBy);
        }
        #endregion

        #region Animation Engine
        private string _lastMarqueeText = "";

        private void StartMarquee(string text, bool queued = false)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                TxtSimStatus.Text = "狀態: 範本文字為空";
                return;
            }

            _lastMarqueeText = text;

            // Mutual exclusivity: If Song Added notification is currently showing on the same top line, queue the marquee!
            if (_isSongAddedActive)
            {
                _playbackQueue.Enqueue(() => StartMarquee(text, false));
                TxtSimStatus.Text = $"狀態: 點播提示顯示中 (同排)，跑馬燈已排入佇列 (佇列數: {_playbackQueue.Count})";
                return;
            }

            StopMarqueeOnly();
            if (_marqueeText == null) return;

            UpdateSettingsFromUI();

            if (CboFontFamily.SelectedItem is FontDisplayItem ff) _marqueeText.FontFamily = ff.FontFamily;
            _marqueeText.FontWeight = ParseFontWeight(CboFontWeight.SelectedItem);
            _marqueeText.FontSize = SldFontSize.Value;
            _marqueeText.StrokeThickness = SldStrokeThickness.Value;
            _marqueeText.Fill = ParseBrush(TxtFillColor.Text, Brushes.White);
            _marqueeText.Stroke = ParseBrush(TxtStrokeColor.Text, Brushes.Black);
            _marqueeText.Text = text;

            _marqueeText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = _marqueeText.DesiredSize.Width;
            double canvasWidth = MarqueeCanvas.Width > 0 ? MarqueeCanvas.Width : (MarqueeCanvas.ActualWidth > 0 ? MarqueeCanvas.ActualWidth : 1920);

            double speed = Math.Max(100, SldSpeed.Value);
            double holdSeconds = Math.Max(0, SldHoldTime.Value);
            int playCount = (int)Math.Round(SldPlayCount.Value);

            if (playCount <= 0)
            {
                TxtSimStatus.Text = "狀態: 跑馬燈播放次數設為 0 (關閉)";
                TxtAnimInfo.Text = "";
                return;
            }

            // Continuous smooth scroll from right offscreen to left offscreen
            double totalDistance = canvasWidth + textWidth + 70;
            double scrollSeconds = Math.Max(0.1, totalDistance / speed);
            double totalCycleSeconds = scrollSeconds + holdSeconds;

            var animation = new DoubleAnimationUsingKeyFrames();
            // Start off-screen right
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(canvasWidth + 35, KeyTime.FromTimeSpan(TimeSpan.Zero)));

            // Smooth linear scroll across the full screen to off-screen left without mid-way pauses
            var tScroll = TimeSpan.FromSeconds(scrollSeconds);
            animation.KeyFrames.Add(new LinearDoubleKeyFrame(-textWidth - 35, KeyTime.FromTimeSpan(tScroll)));

            if (holdSeconds > 0)
            {
                // Hold off-screen before starting next repetition cycle
                var tTotal = TimeSpan.FromSeconds(totalCycleSeconds);
                animation.KeyFrames.Add(new DiscreteDoubleKeyFrame(-textWidth - 35, KeyTime.FromTimeSpan(tTotal)));
            }

            animation.RepeatBehavior = new RepeatBehavior(playCount);

            Storyboard.SetTarget(animation, _marqueeText);
            Storyboard.SetTargetProperty(animation, new PropertyPath(Canvas.LeftProperty));

            _currentStoryboard = new Storyboard();
            _currentStoryboard.Children.Add(animation);

            var activeSb = _currentStoryboard;
            activeSb.Completed += (s, args) =>
            {
                if (_currentStoryboard == activeSb)
                {
                    _currentStoryboard = null;
                    if (_marqueeText != null)
                    {
                        Canvas.SetLeft(_marqueeText, -9999);
                    }
                    TxtSimStatus.Text = "狀態: 跑馬燈播放完畢";
                    TxtAnimInfo.Text = "";
                    CheckAndRunQueue();
                }
            };

            TxtSimStatus.Text = $"狀態: 正在播放跑馬燈 (速度: {speed:0} 像素/秒, 重複: {playCount} 次)";
            TxtAnimInfo.Text = $"參數: 單次時長 {scrollSeconds:0.1}秒 + 輪播間隔 {holdSeconds:0.1}秒 (文字寬度 {textWidth:0}像素)";

            _currentStoryboard.Begin();
        }

        private void ShowSongAddedNotification(string text, FontFamily? font, double fontSize, string colorHex, double durationSeconds)
        {
            if (_songAddedText == null) return;
            if (string.IsNullOrWhiteSpace(text) || durationSeconds <= 0)
            {
                TxtSimStatus.Text = "狀態: 【點播成功提示】未啟用或時間為 0";
                return;
            }

            // Mutual exclusivity on top row: If marquee is running, stop it so they don't overlap
            if (_currentStoryboard != null)
            {
                StopMarqueeOnly();
            }

            _isSongAddedActive = true;
            if (_songAddedTimer != null)
            {
                _songAddedTimer.Stop();
            }

            _songAddedText.FontFamily = font ?? new FontFamily("Microsoft JhengHei");
            _songAddedText.FontSize = fontSize;
            _songAddedText.Fill = ParseBrush(colorHex, Brushes.Gold);
            _songAddedText.Stroke = Brushes.Black;
            _songAddedText.StrokeThickness = Math.Max(2, fontSize / 8.0);
            _songAddedText.Text = text;
            _songAddedText.Opacity = 1.0;

            _songAddedText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double textWidth = _songAddedText.DesiredSize.Width;
            double canvasWidth = MarqueeCanvas.Width > 0 ? MarqueeCanvas.Width : (MarqueeCanvas.ActualWidth > 0 ? MarqueeCanvas.ActualWidth : 1920);
            Canvas.SetLeft(_songAddedText, Math.Max(20, (canvasWidth - textWidth) / 2));
            Canvas.SetTop(_songAddedText, 0);

            TxtSimStatus.Text = $"狀態: 正在顯示【點播成功提示】(同排互斥, 持續 {durationSeconds:0} 秒)";
            TxtAnimInfo.Text = $"提示: {text}";

            _songAddedTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(durationSeconds)
            };
            _songAddedTimer.Tick += (s, args) =>
            {
                _songAddedTimer.Stop();
                _songAddedText.Opacity = 0;
                _isSongAddedActive = false;
                TxtSimStatus.Text = "狀態: 【點播成功提示】顯示完畢";
                TxtAnimInfo.Text = "";
                CheckAndRunQueue();
            };
            _songAddedTimer.Start();
        }

        private void ShowBroadcastNotification(string text, FontFamily? font, double fontSize, string colorHex, double durationSeconds)
        {
            if (_broadcastText == null) return;
            if (string.IsNullOrWhiteSpace(text) || durationSeconds <= 0)
            {
                TxtSimStatus.Text = "狀態: 【系統廣播】未啟用或時間為 0";
                return;
            }

            // Independent: Broadcast is located at bottom right and does NOT interrupt top row (Marquee or Song Added)
            _isBroadcastActive = true;
            if (_broadcastTimer != null)
            {
                _broadcastTimer.Stop();
            }

            _broadcastText.FontFamily = font ?? new FontFamily("Microsoft JhengHei");
            _broadcastText.FontSize = fontSize;
            _broadcastText.Fill = ParseBrush(colorHex, Brushes.Cyan);
            _broadcastText.Stroke = Brushes.Black;
            _broadcastText.StrokeThickness = Math.Max(2, fontSize / 8.0);
            _broadcastText.Text = text;
            _broadcastText.Opacity = 1.0;

            TxtSimStatus.Text = $"狀態: 正在顯示【系統廣播】(右下角獨立, 持續 {durationSeconds:0} 秒)";
            TxtAnimInfo.Text = $"廣播: {text}";

            _broadcastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(durationSeconds)
            };
            _broadcastTimer.Tick += (s, args) =>
            {
                _broadcastTimer.Stop();
                _broadcastText.Opacity = 0;
                _isBroadcastActive = false;
                TxtSimStatus.Text = "狀態: 【系統廣播】顯示完畢";
                TxtAnimInfo.Text = "";
            };
            _broadcastTimer.Start();
        }

        private void CheckAndRunQueue()
        {
            if (_isSongAddedActive) return;
            if (_playbackQueue.Count > 0)
            {
                var action = _playbackQueue.Dequeue();
                action.Invoke();
            }
        }

        private void StopMarqueeOnly()
        {
            if (_currentStoryboard != null)
            {
                var sb = _currentStoryboard;
                _currentStoryboard = null;
                sb.Stop();
                sb.Remove();
            }
            if (_marqueeText != null)
            {
                Canvas.SetLeft(_marqueeText, -9999);
            }
        }

        private void StopMarquee()
        {
            StopMarqueeOnly();

            if (_songAddedTimer != null)
            {
                _songAddedTimer.Stop();
                _songAddedTimer = null;
            }
            if (_songAddedText != null)
            {
                _songAddedText.Opacity = 0;
            }
            _isSongAddedActive = false;

            if (_broadcastTimer != null)
            {
                _broadcastTimer.Stop();
                _broadcastTimer = null;
            }
            if (_broadcastText != null)
            {
                _broadcastText.Opacity = 0;
            }
            _isBroadcastActive = false;

            _playbackQueue.Clear();
            TxtSimStatus.Text = "狀態: 動畫已停止";
            TxtAnimInfo.Text = "";
        }
        #endregion

        #region Button Handlers & Navigation
        private void BtnPlaySong1_Click(object sender, RoutedEventArgs e)
        {
            _currentTemplateIndex = 0;
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(0);
            TxtTemplate1.Focus();
            StartMarquee(GetFormattedSong1());
        }

        private void BtnPlaySong2_Click(object sender, RoutedEventArgs e)
        {
            _currentTemplateIndex = 1;
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(0);
            TxtTemplate2.Focus();
            StartMarquee(GetFormattedSong2());
        }

        private void BtnPlaySong3_Click(object sender, RoutedEventArgs e)
        {
            _currentTemplateIndex = 2;
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(0);
            TxtTemplate3.Focus();
            StartMarquee(GetFormattedSong3());
        }

        private void BtnPlayStartup_Click(object sender, RoutedEventArgs e)
        {
            _currentTemplateIndex = 3;
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(0);
            TxtTemplateStartup.Focus();
            StartMarquee(GetFormattedStartup());
        }

        private void BtnSongAdded_Click(object sender, RoutedEventArgs e)
        {
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(1);
            TxtSongAddedTemplate.Focus();

            var text = GetFormattedSongAdded();
            var font = (CboSongAddedFontFamily.SelectedItem as FontDisplayItem)?.FontFamily;
            double size = SldSongAddedFontSize.Value;
            string color = TxtSongAddedFillColor.Text;
            double duration = SldSongAddedDuration.Value;

            ShowSongAddedNotification(text, font, size, color, duration);
        }

        private void BtnBroadcast_Click(object sender, RoutedEventArgs e)
        {
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(2);

            var text = "🔊 85";
            var font = (CboBroadcastFontFamily.SelectedItem as FontDisplayItem)?.FontFamily;
            double size = SldBroadcastFontSize.Value;
            string color = TxtBroadcastFillColor.Text;
            double duration = SldBroadcastDuration.Value;

            ShowBroadcastNotification(text, font, size, color, duration);
        }

        private void BtnQueueTest_Click(object sender, RoutedEventArgs e)
        {
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(1);

            // 1. Trigger Song Added at the top line
            var songAddedText = GetFormattedSongAdded();
            var songAddedFont = (CboSongAddedFontFamily.SelectedItem as FontDisplayItem)?.FontFamily;
            ShowSongAddedNotification(songAddedText, songAddedFont, SldSongAddedFontSize.Value, TxtSongAddedFillColor.Text, SldSongAddedDuration.Value);

            // 2. Queue Marquee for the same top line (will wait until Song Added finishes)
            StartMarquee(GetFormattedSong1(), true);

            // 3. Trigger Broadcast at bottom right (runs concurrently with top line!)
            var broadcastFont = (CboBroadcastFontFamily.SelectedItem as FontDisplayItem)?.FontFamily;
            ShowBroadcastNotification("📢 [廣播] 系統音量: 85%  |  音調: 原調 (0)", broadcastFont, SldBroadcastFontSize.Value, TxtBroadcastFillColor.Text, SldBroadcastDuration.Value);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            StopMarquee();
        }

        private void BtnResetSettingsDefaults_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("確定要將 settings.json (跑馬燈與提示) 所有設定回復為原廠預設值嗎？", "確認回復預設值", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _model.ResetSettingsToDefaults();
                ApplySettingsToUI();
                UpdateLivePreviews();
                MessageBox.Show("跑馬燈與提示設定已回復為預設值！\n(如欲永久生效請點擊「儲存目前檔」)", "已回復預設", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnResetTextSettingsDefaults_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("確定要將 textsettings.json (點歌介面字級與主題色彩) 所有設定回復為原廠預設值嗎？", "確認回復預設值", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                _model.ResetTextSettingsToDefaults();
                ApplySettingsToUI();
                UpdateLivePreviews();
                if (CboThemePresets != null) CboThemePresets.SelectedIndex = 0;
                MessageBox.Show("點歌介面字級與主題色彩已回復為預設值！\n(如欲永久生效請點擊「儲存目前檔」)", "已回復預設", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        public static string? FindSampleSettingsDirectory()
        {
            string? dir = AppDomain.CurrentDomain.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                string candidate = Path.Combine(dir, "SampleSettings");
                if (Directory.Exists(candidate)) return candidate;
                string? parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir) break;
                dir = parent;
            }
            return null;
        }

        private void CboThemePresets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || CboThemePresets == null || CboThemePresets.SelectedItem is not ComboBoxItem item) return;
            string? tag = item.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            if (tag == "DEFAULT")
            {
                _model.ResetTextSettingsToDefaults();
                ApplySettingsToUI();
                UpdateLivePreviews();
                return;
            }

            // 1. 優先從組件內建資源讀取 (Embedded Resource)
            string resourceName = $"StyleSimulator.SampleSettings.{tag}";
            var assembly = typeof(MainWindow).Assembly;
            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        if (_model.ApplyTextSettingsFromJson(json))
                        {
                            ApplySettingsToUI();
                            UpdateLivePreviews();
                            return;
                        }
                    }
                }
            }

            // 2. 若無內建資源，回退搜尋外部 SampleSettings 資料夾
            string? sampleDir = FindSampleSettingsDirectory();
            if (sampleDir != null)
            {
                string targetPath = Path.Combine(sampleDir, tag);
                if (File.Exists(targetPath))
                {
                    _model.ApplyTextSettingsPreset(targetPath);
                    ApplySettingsToUI();
                    UpdateLivePreviews();
                    return;
                }
            }

            MessageBox.Show($"找不到主題範本資源或檔案:\n{tag}", "主題載入失敗", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Windows API 對話框 ClientGuid：讓 Windows Shell 自動獨立記憶此模擬器上次瀏覽的資料夾與偏好
        private static readonly Guid StyleSimulatorFileDialogGuid = new Guid("8A4C6251-5D2B-4BC2-9118-E9E67B92C23C");

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            bool isSettings = CboConfigFileType.SelectedIndex == 0;
            string expectedFileName = isSettings ? "settings.json" : "textsettings.json";
            string otherFileName = isSettings ? "textsettings.json" : "settings.json";

            // 使用 Windows API 內建對話框機制：
            // 不指定 InitialDirectory，並指派 ClientGuid，Windows 會自動記住上次使用者瀏覽開啟的資料夾
            var dlg = new OpenFileDialog
            {
                Filter = isSettings
                    ? "設定檔 (settings.json)|settings.json"
                    : "UI設定檔 (textsettings.json)|textsettings.json",
                Title = isSettings ? "選擇 settings.json (跑馬燈/提示)" : "選擇 textsettings.json (UI介面)",
                FileName = expectedFileName,
                ClientGuid = StyleSimulatorFileDialogGuid,
                RestoreDirectory = false
            };

            if (dlg.ShowDialog() == true)
            {
                string selectedFileName = Path.GetFileName(dlg.FileName);
                if (!string.Equals(selectedFileName, expectedFileName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show($"目前模式為【{(isSettings ? "跑馬燈/提示" : "UI介面")}】，僅能載入「{expectedFileName}」！\n\n您選取的檔案為: {selectedFileName}\n為避免設定內容不相容或儲存時覆寫損壞，載入已取消。",
                                    "檔案名稱不符合", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 若同一目錄下也存在另一份設定檔，且目前另一檔案仍指向模擬器自身預設目錄，自動一併對齊載入
                string? selectedDir = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrEmpty(selectedDir))
                {
                    string otherFilePath = Path.Combine(selectedDir, otherFileName);
                    if (File.Exists(otherFilePath))
                    {
                        string otherCurrentPath = isSettings ? StyleSettingsModel.TextSettingsPath : StyleSettingsModel.SettingsPath;
                        string myBaseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
                        if (otherCurrentPath.StartsWith(myBaseDir, StringComparison.OrdinalIgnoreCase))
                        {
                            if (isSettings)
                            {
                                _model.LoadTextSettingsJson(otherFilePath);
                            }
                            else
                            {
                                _model.LoadSettingsJson(otherFilePath);
                            }
                        }
                    }
                }

                if (isSettings)
                {
                    _model.LoadSettingsJson(dlg.FileName);
                }
                else
                {
                    _model.LoadTextSettingsJson(dlg.FileName);
                }
                UpdateFilePathDisplay();
                ApplySettingsToUI();
                UpdateLivePreviews();
                MessageBox.Show($"成功載入設定檔:\n{dlg.FileName}", "載入成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnReload_Click(object sender, RoutedEventArgs e)
        {
            if (CboConfigFileType.SelectedIndex == 0)
            {
                _model.LoadSettingsJson(StyleSettingsModel.SettingsPath);
            }
            else
            {
                _model.LoadTextSettingsJson(StyleSettingsModel.TextSettingsPath);
            }
            UpdateFilePathDisplay();
            ApplySettingsToUI();
            UpdateLivePreviews();
            MessageBox.Show("已重新載入設定檔！", "載入成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSaveCurrent_Click(object sender, RoutedEventArgs e)
        {
            UpdateSettingsFromUI();
            bool isSettings = CboConfigFileType.SelectedIndex == 0;
            bool ok = isSettings ? _model.SaveSettingsJson() : _model.SaveTextSettingsJson();
            string path = isSettings ? StyleSettingsModel.SettingsPath : StyleSettingsModel.TextSettingsPath;

            if (ok)
            {
                MessageBox.Show($"已成功儲存至:\n{path}", "儲存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"儲存失敗:\n{path}", "儲存失敗", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveAll_Click(object sender, RoutedEventArgs e)
        {
            UpdateSettingsFromUI();
            bool ok1 = _model.SaveSettingsJson();
            bool ok2 = _model.SaveTextSettingsJson();

            if (ok1 && ok2)
            {
                MessageBox.Show($"雙設定檔已成功儲存！\n1) {StyleSettingsModel.SettingsPath}\n2) {StyleSettingsModel.TextSettingsPath}", "全部儲存成功", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"儲存時發生錯誤 (settings: {ok1}, textsettings: {ok2})", "儲存提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        #endregion
    }
}
