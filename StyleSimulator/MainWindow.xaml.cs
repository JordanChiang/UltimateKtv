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
using Microsoft.Win32;

namespace StyleSimulator
{
    public partial class MainWindow : Window
    {
        private StyleSettingsModel _model = new StyleSettingsModel();
        private bool _isInitializing = true;

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
            Loaded += MainWindow_Loaded;
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

            TxtPrimaryColor.Text = _model.PrimaryColor;
            TxtBrightBorderColor.Text = _model.BrightBorderColor;
            TxtDataGridHeaderBg.Text = _model.DataGridHeaderBgColor;
            TxtAnnouncementFg.Text = _model.AnnouncementForegroundColor;

            _isInitializing = false;
            UpdateColorPreviews();
            UpdateLabels();
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

            _model.PrimaryColor = TxtPrimaryColor.Text.Trim();
            _model.BrightBorderColor = TxtBrightBorderColor.Text.Trim();
            _model.DataGridHeaderBgColor = TxtDataGridHeaderBg.Text.Trim();
            _model.AnnouncementForegroundColor = TxtAnnouncementFg.Text.Trim();
        }

        private void UpdateLabels()
        {
            if (LblFontSize != null) LblFontSize.Text = $"{SldFontSize.Value:0} 像素";
            if (LblStrokeThickness != null) LblStrokeThickness.Text = $"{SldStrokeThickness.Value:0} 像素";
            if (LblSpeed != null) LblSpeed.Text = $"{SldSpeed.Value:0} 像素/秒";
            if (LblHoldTime != null) LblHoldTime.Text = $"{SldHoldTime.Value:0.#} 秒";
            if (LblPlayCount != null) LblPlayCount.Text = $"{SldPlayCount.Value:0} 次";

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

            if (RectPrimaryColor != null) RectPrimaryColor.Background = ParseBrush(TxtPrimaryColor.Text, Brushes.Orange);
            if (RectBrightBorderColor != null) RectBrightBorderColor.Background = ParseBrush(TxtBrightBorderColor.Text, Brushes.Gold);
            if (RectDataGridHeaderBg != null) RectDataGridHeaderBg.Background = ParseBrush(TxtDataGridHeaderBg.Text, Brushes.DarkSlateGray);
            if (RectAnnouncementFg != null) RectAnnouncementFg.Background = ParseBrush(TxtAnnouncementFg.Text, Brushes.White);
        }

        private void UpdateLivePreviews()
        {
            var uiFont = (CboUiFontFamily.SelectedItem as FontDisplayItem)?.FontFamily ?? new FontFamily("Microsoft JhengHei");

            if (TxtSongRow1 != null) { TxtSongRow1.FontFamily = uiFont; TxtSongRow1.FontSize = SldSongListFontSize.Value; }
            if (TxtSongRow2 != null) { TxtSongRow2.FontFamily = uiFont; TxtSongRow2.FontSize = SldSongListFontSize.Value; }
            if (TxtSongRow3 != null) { TxtSongRow3.FontFamily = uiFont; TxtSongRow3.FontSize = SldSongListFontSize.Value; }
            if (TxtSongRow4 != null) { TxtSongRow4.FontFamily = uiFont; TxtSongRow4.FontSize = SldSongListFontSize.Value; }

            if (TxtWaitRow1 != null) { TxtWaitRow1.FontFamily = uiFont; TxtWaitRow1.FontSize = SldWaitingListFontSize.Value; }
            if (TxtWaitRow2 != null) { TxtWaitRow2.FontFamily = uiFont; TxtWaitRow2.FontSize = SldWaitingListFontSize.Value; }
            if (TxtWaitRow3 != null) { TxtWaitRow3.FontFamily = uiFont; TxtWaitRow3.FontSize = SldWaitingListFontSize.Value; }

            if (BtnFunc1 != null) { BtnFunc1.FontFamily = uiFont; BtnFunc1.FontSize = SldFuncBtnFontSize.Value; BtnFunc1.Background = ParseBrush(TxtPrimaryColor.Text, Brushes.Indigo); }
            if (BtnFunc2 != null) { BtnFunc2.FontFamily = uiFont; BtnFunc2.FontSize = SldFuncBtnFontSize.Value; BtnFunc2.Background = ParseBrush(TxtPrimaryColor.Text, Brushes.Indigo); }
            if (BtnFunc3 != null) { BtnFunc3.FontFamily = uiFont; BtnFunc3.FontSize = SldFuncBtnFontSize.Value; BtnFunc3.Background = ParseBrush(TxtPrimaryColor.Text, Brushes.Indigo); }
            if (BtnFunc4 != null) { BtnFunc4.FontFamily = uiFont; BtnFunc4.FontSize = SldFuncBtnFontSize.Value; BtnFunc4.Background = ParseBrush(TxtPrimaryColor.Text, Brushes.Indigo); }

            if (BtnBottom1 != null) { BtnBottom1.FontFamily = uiFont; BtnBottom1.FontSize = SldBottomButtonFontSize.Value; }
            if (BtnBottom2 != null) { BtnBottom2.FontFamily = uiFont; BtnBottom2.FontSize = SldBottomButtonFontSize.Value; }
            if (BtnBottom3 != null) { BtnBottom3.FontFamily = uiFont; BtnBottom3.FontSize = SldBottomButtonFontSize.Value; }
            if (BtnBottom4 != null) { BtnBottom4.FontFamily = uiFont; BtnBottom4.FontSize = SldBottomButtonFontSize.Value; }

            if (BorderSongList != null) BorderSongList.BorderBrush = ParseBrush(TxtBrightBorderColor.Text, Brushes.Gold);
            if (BorderHeader != null) BorderHeader.BorderBrush = ParseBrush(TxtPrimaryColor.Text, Brushes.Orange);
            if (BorderGridHeader != null) BorderGridHeader.Background = ParseBrush(TxtDataGridHeaderBg.Text, Brushes.DarkSlateGray);
            if (TxtPreviewAnnouncement != null) { TxtPreviewAnnouncement.FontFamily = uiFont; TxtPreviewAnnouncement.Foreground = ParseBrush(TxtAnnouncementFg.Text, Brushes.White); }

            if (TxtPreviewStatic != null)
            {
                TxtPreviewStatic.FontFamily = uiFont;
                TxtPreviewStatic.FontSize = SldWebHostInfoFontSize.Value;
                TxtPreviewStatic.Text = $"網路點歌: {TxtMockLanIp.Text}";
            }

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

        private void ParamChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;
            UpdateLabels();
            UpdateColorPreviews();
            UpdateSettingsFromUI();
            UpdateLivePreviews();

            if (CboConfigFileType != null && CboConfigFileType.SelectedIndex == 0 &&
                CardPlayback != null && CardPlayback.Visibility == Visibility.Visible &&
                !_isSongAddedActive)
            {
                string textToPlay = !string.IsNullOrEmpty(_lastMarqueeText) ? _lastMarqueeText : GetFormattedSong1();
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

        private void RectPrimaryColor_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtPrimaryColor);
        private void RectBrightBorderColor_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtBrightBorderColor);
        private void RectDataGridHeaderBg_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtDataGridHeaderBg);
        private void RectAnnouncementFg_MouseDown(object sender, MouseButtonEventArgs e) => PickColor(TxtAnnouncementFg);
        #endregion

        #region String Formatting Helpers
        private string GetFormattedSong1()
        {
            return FormatMarqueeString(TxtTemplate1.Text, TxtMockSong.Text, TxtMockSinger.Text, TxtMockNextSong.Text, TxtMockNextSinger.Text, TxtMockLanIp.Text, TxtMockWanIp.Text, TxtMockOrderedBy.Text.Trim());
        }

        private string GetFormattedSong2()
        {
            return FormatMarqueeString(TxtTemplate2.Text, TxtMockSong.Text, TxtMockSinger.Text, "", "", TxtMockLanIp.Text, TxtMockWanIp.Text, TxtMockOrderedBy.Text.Trim());
        }

        private string GetFormattedSong3()
        {
            return FormatMarqueeString(TxtTemplate3.Text, TxtMockSong.Text, TxtMockSinger.Text, "", "", TxtMockLanIp.Text, TxtMockWanIp.Text, TxtMockOrderedBy.Text.Trim());
        }

        private string GetFormattedStartup()
        {
            return FormatMarqueeString(TxtTemplateStartup.Text, "", "", "", "", TxtMockLanIp.Text, TxtMockWanIp.Text, "");
        }

        private string GetFormattedSongAdded()
        {
            return FormatMarqueeString(TxtSongAddedTemplate.Text, TxtMockSong.Text, TxtMockSinger.Text, "", "", TxtMockLanIp.Text, TxtMockWanIp.Text, TxtMockOrderedBy.Text.Trim());
        }

        private string FormatMarqueeString(string template, string song, string singer, string nextSong, string nextSinger, string lanIp, string wanIp, string orderedBy)
        {
            if (string.IsNullOrEmpty(template)) return "";

            if (string.IsNullOrWhiteSpace(orderedBy) || orderedBy == "本機" || orderedBy == "隨機播放")
            {
                template = System.Text.RegularExpressions.Regex.Replace(template, @"[，,、\s]*點歌[人者]?[：:]\s*\{6\}", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                template = template.Replace("{6}", "");
            }
            else
            {
                template = template.Replace("{6}", orderedBy);
            }

            return template
                .Replace("{0}", song ?? "")
                .Replace("{1}", singer ?? "")
                .Replace("{2}", nextSong ?? "")
                .Replace("{3}", nextSinger ?? "")
                .Replace("{4}", lanIp ?? "")
                .Replace("{5}", wanIp ?? "");
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
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(0);
            TxtTemplate1.Focus();
            StartMarquee(GetFormattedSong1());
        }

        private void BtnPlaySong2_Click(object sender, RoutedEventArgs e)
        {
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(0);
            TxtTemplate2.Focus();
            StartMarquee(GetFormattedSong2());
        }

        private void BtnPlaySong3_Click(object sender, RoutedEventArgs e)
        {
            CboConfigFileType.SelectedIndex = 0;
            SelectMarqueeCategory(0);
            TxtTemplate3.Focus();
            StartMarquee(GetFormattedSong3());
        }

        private void BtnPlayStartup_Click(object sender, RoutedEventArgs e)
        {
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
                MessageBox.Show("點歌介面字級與主題色彩已回復為預設值！\n(如欲永久生效請點擊「儲存目前檔」)", "已回復預設", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            bool isSettings = CboConfigFileType.SelectedIndex == 0;
            string currentPath = isSettings ? StyleSettingsModel.SettingsPath : StyleSettingsModel.TextSettingsPath;

            string initialDir = AppDomain.CurrentDomain.BaseDirectory;
            if (File.Exists(currentPath))
            {
                try
                {
                    string? dir = Path.GetDirectoryName(currentPath);
                    if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                    {
                        initialDir = dir;
                    }
                }
                catch { }
            }

            var dlg = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
                Title = isSettings ? "選擇 settings.json" : "選擇 textsettings.json",
                FileName = isSettings ? "settings.json" : "textsettings.json",
                InitialDirectory = initialDir
            };

            if (dlg.ShowDialog() == true)
            {
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
