using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace UltimateKtv
{
    /// <summary>
    /// Handles reading and processing UI theme and visual settings from JSON file.
    /// Settings are loaded once at startup and cached for application use.
    /// </summary>
    public static class ThemeSettingsHandler
    {
        private static readonly string _settingsFilePath;
        private static readonly string _legacySettingsFilePath;
        private static ThemeSettings _settings = null!;
        private static bool _isLoaded = false;

        static ThemeSettingsHandler()
        {
            _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "themesettings.json");
            _legacySettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "textsettings.json");
        }

        /// <summary>
        /// Gets the current theme settings. Loads from file if not already loaded.
        /// </summary>
        public static ThemeSettings Settings
        {
            get
            {
                if (!_isLoaded)
                {
                    LoadSettings();
                }
                return _settings;
            }
        }

        /// <summary>
        /// Loads theme settings from the JSON file. Creates default file if not exists.
        /// </summary>
        public static void LoadSettings()
        {
            AppLogger.Log($"Loading theme settings from: {_settingsFilePath}");

            // 向下相容機制：若 themesettings.json 不存在，但存在舊版 textsettings.json，自動平滑遷移
            if (!File.Exists(_settingsFilePath) && File.Exists(_legacySettingsFilePath))
            {
                try
                {
                    AppLogger.Log($"Migrating legacy {_legacySettingsFilePath} to {_settingsFilePath}...");
                    File.Copy(_legacySettingsFilePath, _settingsFilePath, true);
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to migrate legacy textsettings.json to themesettings.json.", ex);
                }
            }

            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var options = new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    _settings = JsonSerializer.Deserialize<ThemeSettings>(json, options) ?? new ThemeSettings();
                    SaveSettings(); // Update the physical file to inject comments and missing schema keys
                    AppLogger.Log("Theme settings loaded successfully.");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to load themesettings.json. Using defaults.", ex);
                    _settings = new ThemeSettings();
                    SaveSettings();
                }
            }
            else
            {
                AppLogger.Log("themesettings.json not found. Creating with default values.");
                _settings = new ThemeSettings();
                SaveSettings();
            }

            _isLoaded = true;
        }

        /// <summary>
        /// Saves settings to the JSON file, preserving values and injecting descriptions.
        /// </summary>
        public static void SaveSettings()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                };
                string json = JsonSerializer.Serialize(_settings, options);

                // Inject property descriptions as JSON comments using Reflection
                foreach (var prop in typeof(ThemeSettings).GetProperties())
                {
                    var descAttr = (System.ComponentModel.DescriptionAttribute?)Attribute.GetCustomAttribute(prop, typeof(System.ComponentModel.DescriptionAttribute));
                    if (descAttr != null && !string.IsNullOrWhiteSpace(descAttr.Description))
                    {
                        string search = $"  \"{prop.Name}\":";
                        string replace = $"  // {descAttr.Description}{Environment.NewLine}{search}";
                        json = json.Replace(search, replace);
                    }
                }

                File.WriteAllText(_settingsFilePath, json);
                AppLogger.Log("Theme settings file saved and updated.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to save themesettings.json.", ex);
            }
        }

        #region Helper Methods - Color Parsing

        /// <summary>
        /// Parses a hex color string to a SolidColorBrush.
        /// </summary>
        /// <param name="hexColor">Color in hex format (e.g., "#FFFFFF" or "#80FFFFFF" for alpha)</param>
        /// <param name="fallback">Fallback brush if parsing fails</param>
        /// <returns>SolidColorBrush from hex color</returns>
        public static SolidColorBrush ParseBrush(string hexColor, SolidColorBrush? fallback = null)
        {
            try
            {
                var color = (Color)ColorConverter.ConvertFromString(hexColor);
                return new SolidColorBrush(color);
            }
            catch
            {
                return fallback ?? Brushes.White;
            }
        }

        /// <summary>
        /// Parses a hex color string to a Color.
        /// </summary>
        /// <param name="hexColor">Color in hex format</param>
        /// <param name="fallback">Fallback color if parsing fails</param>
        /// <returns>Color from hex string</returns>
        public static Color ParseColor(string hexColor, Color? fallback = null)
        {
            try
            {
                return (Color)ColorConverter.ConvertFromString(hexColor);
            }
            catch
            {
                return fallback ?? Colors.White;
            }
        }

        #endregion

        #region Convenience Properties - Pre-parsed Brushes

        /// <summary>
        /// Gets the announcement foreground brush.
        /// </summary>
        public static Brush AnnouncementForeground => ParseBrush(Settings.AnnouncementForegroundColor, Brushes.White);

        /// <summary>
        /// Gets the static text foreground brush.
        /// </summary>
        public static Brush StaticTextForeground => ParseBrush(Settings.StaticTextForegroundColor, Brushes.White);

        /// <summary>
        /// Gets the web host info foreground brush.
        /// </summary>
        public static Brush WebHostInfoForeground => ParseBrush(Settings.WebHostInfoForegroundColor, Brushes.White);

        /// <summary>
        /// Gets the configured font family.
        /// </summary>
        public static FontFamily FontFamily => new FontFamily(Settings.FontFamily);

        #endregion



        #region Convenience Properties - UI Brushes

        /// <summary>
        /// Gets the DataGrid column header background brush (橫向科技微漸層，支援自訂顏色演算).
        /// </summary>
        public static Brush DataGridColumnHeaderBackground
        {
            get
            {
                var hex = Settings.DataGridColumnHeaderBackgroundColor;
                // 若為預設舊值 "#FF4A4A4A"、空值或未特別自訂，依主要顏色演算微光漸層 (若無則使用深藍科技微光漸層)
                if (string.IsNullOrWhiteSpace(hex) || string.Equals(hex, "#FF4A4A4A", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(Settings.PrimaryColor))
                    {
                        var primary = ParseColor(Settings.PrimaryColor, System.Windows.Media.Colors.Goldenrod);
                        var startCol = Color.FromArgb(
                            255,
                            (byte)(primary.R * 0.7),
                            (byte)(primary.G * 0.7),
                            (byte)(primary.B * 0.7));
                        var endCol = Color.FromArgb(
                            255,
                            (byte)Math.Min(255, primary.R * 0.95 + 10),
                            (byte)Math.Min(255, primary.G * 0.95 + 10),
                            (byte)Math.Min(255, primary.B * 0.95 + 10));
                        return CreateHeaderGradient(startCol, endCol);
                    }

                    return CreateHeaderGradient(
                        Color.FromArgb(0xD1, 0x12, 0x18, 0x26),
                        Color.FromArgb(0xD9, 0x1C, 0x26, 0x3C));
                }

                // 若有自訂顏色，則依該基準色演算左至右微漸層 (起點略暗加強質感，終點略亮呈現微光)
                var baseColor = ParseColor(hex, Color.FromRgb(74, 74, 74));
                var startColor = Color.FromArgb(
                    baseColor.A,
                    (byte)(baseColor.R * 0.82),
                    (byte)(baseColor.G * 0.82),
                    (byte)(baseColor.B * 0.82));
                var endColor = Color.FromArgb(
                    baseColor.A,
                    (byte)Math.Min(255, baseColor.R * 1.18 + 15),
                    (byte)Math.Min(255, baseColor.G * 1.18 + 15),
                    (byte)Math.Min(255, baseColor.B * 1.18 + 15));

                return CreateHeaderGradient(startColor, endColor);
            }
        }

        private static LinearGradientBrush CreateHeaderGradient(Color startColor, Color endColor)
        {
            var gradient = new LinearGradientBrush
            {
                StartPoint = new System.Windows.Point(0, 0),
                EndPoint = new System.Windows.Point(1, 0)
            };
            gradient.GradientStops.Add(new GradientStop(startColor, 0.0));
            gradient.GradientStops.Add(new GradientStop(endColor, 1.0));
            gradient.Freeze();
            return gradient;
        }

        /// <summary>
        /// Gets the DataGrid column header foreground brush.
        /// </summary>
        public static Brush DataGridColumnHeaderForeground => ParseBrush(Settings.DataGridColumnHeaderForegroundColor, Brushes.White);


        /// <summary>
        /// Gets the primary theme brush.
        /// </summary>
        public static Brush PrimaryBrush => ParseBrush(Settings.PrimaryColor, new SolidColorBrush(Color.FromRgb(103, 58, 183)));

        /// <summary>
        /// Gets the primary mid-tone brush. Merged with PrimaryBrush for backwards compatibility.
        /// </summary>
        public static Brush PrimaryMidBrush => PrimaryBrush;

        /// <summary>
        /// Gets the primary dark brush. Merged with PrimaryBrush for backwards compatibility.
        /// </summary>
        public static Brush PrimaryDarkBrush => PrimaryBrush;

        /// <summary>
        /// Gets the primary light brush.
        /// </summary>
        public static Brush PrimaryLightBrush => ParseBrush(Settings.PrimaryLightColor, new SolidColorBrush(Color.FromRgb(179, 157, 219)));

        #endregion

        #region Dynamic Cyber Accent Brushes & Colors

        /// <summary>
        /// Gets the resolved Accent/Glow color. If AccentColor is configured, uses it; otherwise derives from PrimaryColor.
        /// </summary>
        public static System.Windows.Media.Color ResolvedAccentColor
        {
            get
            {
                var primary = ParseColor(Settings.PrimaryColor, System.Windows.Media.Colors.Goldenrod);
                if (!string.IsNullOrWhiteSpace(Settings.AccentColor))
                {
                    return ParseColor(Settings.AccentColor, primary);
                }
                return primary;
            }
        }

        /// <summary>
        /// Gets the resolved Accent Brush.
        /// </summary>
        public static SolidColorBrush AccentBrush
        {
            get
            {
                var brush = new SolidColorBrush(ResolvedAccentColor);
                brush.Freeze();
                return brush;
            }
        }

        /// <summary>
        /// Gets the resolved Accent Dim Brush (15% opacity for hover backgrounds).
        /// </summary>
        public static SolidColorBrush AccentDimBrush
        {
            get
            {
                var c = ResolvedAccentColor;
                var dim = System.Windows.Media.Color.FromArgb((byte)(255 * 0.15), c.R, c.G, c.B);
                var brush = new SolidColorBrush(dim);
                brush.Freeze();
                return brush;
            }
        }

        /// <summary>
        /// Gets the resolved Cyber Active Gradient Brush (matching the prominent "歌星" selected appearance, tuned warm and comfortable).
        /// </summary>
        public static LinearGradientBrush CyberActiveGradientBrush
        {
            get
            {
                var c = ResolvedAccentColor;
                // High-visibility rich warm amber gradient (subtly toned down from max brightness)
                var startColor = System.Windows.Media.Color.FromArgb(230, (byte)(c.R * 0.94), (byte)(c.G * 0.94), (byte)(c.B * 0.94)); // ~90% opacity, softly toned
                var endColor = System.Windows.Media.Color.FromArgb(215, (byte)(c.R * 0.58), (byte)(c.G * 0.55), (byte)(c.B * 0.52)); // ~84% opacity deep tone
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1)
                };
                gradient.GradientStops.Add(new GradientStop(startColor, 0.0));
                gradient.GradientStops.Add(new GradientStop(endColor, 1.0));
                gradient.Freeze();
                return gradient;
            }
        }

        /// <summary>
        /// Gets the resolved Cyber Button Gradient Brush for default button background (all buttons).
        /// </summary>
        public static LinearGradientBrush CyberButtonGradientBrush
        {
            get
            {
                var c = ResolvedAccentColor;
                var startColor = System.Windows.Media.Color.FromArgb(120, c.R, c.G, c.B); // ~47% translucent glass gradient
                var endColor = System.Windows.Media.Color.FromArgb(50, (byte)(c.R * 0.45), (byte)(c.G * 0.45), (byte)(c.B * 0.45)); // ~20% darkened
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1)
                };
                gradient.GradientStops.Add(new GradientStop(startColor, 0.0));
                gradient.GradientStops.Add(new GradientStop(endColor, 1.0));
                gradient.Freeze();
                return gradient;
            }
        }

        #endregion

        #region Apply Settings to XAML Resources

        /// <summary>
        /// Applies the text settings to XAML resources at startup.
        /// Call this method during MainWindow.Loaded event.
        /// </summary>
        /// <param name="window">The MainWindow instance whose resources will be updated</param>
        public static void ApplyToResources(System.Windows.Window window)
        {
            try
            {
                AppLogger.Log("Applying ThemeSettings to XAML resources...");

                // Dynamic Cyber Accent Brushes & Colors
                var accentColor = ResolvedAccentColor;
                var accentBrush = AccentBrush;
                var accentDimBrush = AccentDimBrush;
                var cyberGradient = CyberActiveGradientBrush;
                var cyberButtonGradient = CyberButtonGradientBrush;

                // Window-level resources (defined in MainWindow.xaml Resources section)
                window.Resources["DataGridColumnHeaderBackground"] = DataGridColumnHeaderBackground;
                window.Resources["DataGridColumnHeaderForeground"] = DataGridColumnHeaderForeground;
                window.Resources["PrimaryBrush"] = PrimaryBrush;
                window.Resources["PrimaryLightBrush"] = PrimaryLightBrush;

                // Window-level cyber accent resources
                window.Resources["ColorAccentCyan"] = accentColor;
                window.Resources["BrushAccentCyan"] = accentBrush;
                window.Resources["BrushAccentCyanDim"] = accentDimBrush;
                window.Resources["BrushCyberActiveGradient"] = cyberGradient;
                window.Resources["BrushCyberButtonGradient"] = cyberButtonGradient;

                // Dynamic UI Font Family
                var uiFont = FontFamily;
                window.FontFamily = uiFont;
                window.Resources["UiFontFamily"] = uiFont;

                // UI Font Size Overrides (must be double for WPF FontSize dependency properties)
                window.Resources["FuncBtnFontSize"] = (double)Settings.FuncBtnFontSize;
                window.Resources["BottomButtonFontSize"] = (double)Settings.BottomButtonFontSize;
                window.Resources["WaitingListFontSize"] = (double)Settings.WaitingListFontSize;
                window.Resources["SongListFontSize"] = (double)Settings.SongListFontSize;

                // Dynamic Theme-calculated Brushes for Option 2 (Capsule) & Option 3 (AccentStrip)
                var primaryCol = ParseColor(Settings.PrimaryColor, System.Windows.Media.Colors.Goldenrod);
                var primaryLightCol = ParseColor(Settings.PrimaryLightColor, System.Windows.Media.Color.FromRgb(255, 224, 130));
                var capsuleBorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(56, primaryCol.R, primaryCol.G, primaryCol.B));
                capsuleBorderBrush.Freeze();
                var capsuleHoverBgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(56, primaryCol.R, primaryCol.G, primaryCol.B));
                capsuleHoverBgBrush.Freeze();
                var capsuleHoverBorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(204, primaryCol.R, primaryCol.G, primaryCol.B));
                capsuleHoverBorderBrush.Freeze();
                var capsuleActiveBgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(76, primaryCol.R, primaryCol.G, primaryCol.B));
                capsuleActiveBgBrush.Freeze();
                var capsuleActiveHoverBgBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(85, primaryCol.R, primaryCol.G, primaryCol.B));
                capsuleActiveHoverBgBrush.Freeze();
                // Option 3 AccentStrip 專用立體金屬漸層筆刷 (Active, Hover, Inactive, Glow)
                var accentStripActiveBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(primaryCol, 0.0),
                        new GradientStop(System.Windows.Media.Color.FromArgb(255,
                            (byte)Math.Min(255, primaryLightCol.R + 25),
                            (byte)Math.Min(255, primaryLightCol.G + 25),
                            (byte)Math.Min(255, primaryLightCol.B + 25)), 0.4),
                        new GradientStop(primaryLightCol, 0.6),
                        new GradientStop(System.Windows.Media.Color.FromArgb(255,
                            (byte)(primaryCol.R * 0.75),
                            (byte)(primaryCol.G * 0.75),
                            (byte)(primaryCol.B * 0.75)), 1.0)
                    }
                };
                accentStripActiveBrush.Freeze();

                var accentStripHoverBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(primaryLightCol, 0.0),
                        new GradientStop(System.Windows.Media.Colors.White, 0.45),
                        new GradientStop(primaryLightCol, 1.0)
                    }
                };
                accentStripHoverBrush.Freeze();

                var accentStripInactiveBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(System.Windows.Media.Color.FromArgb(180, 84, 110, 122), 0.0),
                        new GradientStop(System.Windows.Media.Color.FromArgb(220, 207, 216, 220), 0.45),
                        new GradientStop(System.Windows.Media.Color.FromArgb(190, 144, 164, 174), 0.6),
                        new GradientStop(System.Windows.Media.Color.FromArgb(180, 55, 71, 79), 1.0)
                    }
                };
                accentStripInactiveBrush.Freeze();

                var accentStripGlowBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(System.Windows.Media.Color.FromArgb(80, primaryCol.R, primaryCol.G, primaryCol.B), 0.0),
                        new GradientStop(System.Windows.Media.Color.FromArgb(24, primaryCol.R, primaryCol.G, primaryCol.B), 0.5),
                        new GradientStop(System.Windows.Media.Color.FromArgb(0, primaryCol.R, primaryCol.G, primaryCol.B), 1.0)
                    }
                };
                accentStripGlowBrush.Freeze();

                var textSelectedGradientBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(System.Windows.Media.Color.FromArgb(255, 255, 253, 231), 0.0),
                        new GradientStop(primaryLightCol, 0.35),
                        new GradientStop(primaryCol, 0.75),
                        new GradientStop(System.Windows.Media.Color.FromArgb(255,
                            (byte)(primaryCol.R * 0.8),
                            (byte)(primaryCol.G * 0.8),
                            (byte)(primaryCol.B * 0.8)), 1.0)
                    }
                };
                textSelectedGradientBrush.Freeze();

                window.Resources["BrushCapsuleRowBorder"] = capsuleBorderBrush;
                window.Resources["BrushCapsuleHoverBg"] = capsuleHoverBgBrush;
                window.Resources["BrushCapsuleHoverBorder"] = capsuleHoverBorderBrush;
                window.Resources["BrushCapsuleActiveBg"] = capsuleActiveBgBrush;
                window.Resources["BrushCapsuleActiveBorder"] = PrimaryBrush;
                window.Resources["BrushCapsuleActiveHoverBg"] = capsuleActiveHoverBgBrush;
                window.Resources["BrushAccentStripActive"] = accentStripActiveBrush;
                window.Resources["BrushAccentStripHover"] = accentStripHoverBrush;
                window.Resources["BrushAccentStripInactive"] = accentStripInactiveBrush;
                window.Resources["BrushAccentStripGlow"] = accentStripGlowBrush;
                window.Resources["BrushTextSelectedGradient"] = textSelectedGradientBrush;

                // Option 4 Inward Glow (朝內微光) 專用筆刷 (未選取時純色無橫線，選取/懸停平滑無斷點)
                var innerGlowDefaultBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 27, 36));
                innerGlowDefaultBrush.Freeze();

                var innerGlowHoverBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(System.Windows.Media.Color.FromArgb(96, primaryLightCol.R, primaryLightCol.G, primaryLightCol.B), 0.0),
                        new GradientStop(System.Windows.Media.Color.FromArgb(24, primaryLightCol.R, primaryLightCol.G, primaryLightCol.B), 1.0)
                    }
                };
                innerGlowHoverBrush.Freeze();

                var innerGlowActiveBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(System.Windows.Media.Color.FromArgb(160, primaryCol.R, primaryCol.G, primaryCol.B), 0.0),
                        new GradientStop(System.Windows.Media.Color.FromArgb(48, primaryCol.R, primaryCol.G, primaryCol.B), 1.0)
                    }
                };
                innerGlowActiveBrush.Freeze();

                var innerGlowActiveHoverBrush = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(0, 1),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(System.Windows.Media.Color.FromArgb(192, primaryLightCol.R, primaryLightCol.G, primaryLightCol.B), 0.0),
                        new GradientStop(System.Windows.Media.Color.FromArgb(64, primaryLightCol.R, primaryLightCol.G, primaryLightCol.B), 1.0)
                    }
                };
                innerGlowActiveHoverBrush.Freeze();

                // Option 4 inward glow dynamic brushes (從外框向內擴散約 4~5px，核心維持深黑背景)
                var innerGlowRim1Brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(115, primaryCol.R, primaryCol.G, primaryCol.B));
                var innerGlowRim2Brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, primaryCol.R, primaryCol.G, primaryCol.B));
                var innerGlowRim3Brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(20, primaryCol.R, primaryCol.G, primaryCol.B));
                var innerGlowHoverRim1Brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(90, primaryLightCol.R, primaryLightCol.G, primaryLightCol.B));
                var innerGlowHoverRim2Brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(40, primaryLightCol.R, primaryLightCol.G, primaryLightCol.B));
                var innerGlowHoverRim3Brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(16, primaryLightCol.R, primaryLightCol.G, primaryLightCol.B));

                innerGlowRim1Brush.Freeze();
                innerGlowRim2Brush.Freeze();
                innerGlowRim3Brush.Freeze();
                innerGlowHoverRim1Brush.Freeze();
                innerGlowHoverRim2Brush.Freeze();
                innerGlowHoverRim3Brush.Freeze();

                window.Resources["BrushInnerGlowDefault"] = innerGlowDefaultBrush;
                window.Resources["BrushInnerGlowHover"] = innerGlowHoverBrush;
                window.Resources["BrushInnerGlowActive"] = innerGlowActiveBrush;
                window.Resources["BrushInnerGlowActiveHover"] = innerGlowActiveHoverBrush;
                window.Resources["BrushInnerGlowRim1"] = innerGlowRim1Brush;
                window.Resources["BrushInnerGlowRim2"] = innerGlowRim2Brush;
                window.Resources["BrushInnerGlowRim3"] = innerGlowRim3Brush;
                window.Resources["BrushInnerGlowHoverRim1"] = innerGlowHoverRim1Brush;
                window.Resources["BrushInnerGlowHoverRim2"] = innerGlowHoverRim2Brush;
                window.Resources["BrushInnerGlowHoverRim3"] = innerGlowHoverRim3Brush;
                window.Resources["ColorRowGlowActive"] = primaryCol;
                window.Resources["ColorRowGlowHover"] = primaryLightCol;

                // Singer grid button background (Window-level resource)
                window.Resources["SingerButtonBackground"] = PrimaryBrush;

                // Application-level resources (Material Design brushes and font size fallbacks are defined at App level)
                // These must be set at Application.Resources to override DynamicResource lookups
                var appResources = System.Windows.Application.Current?.Resources;
                if (appResources != null)
                {
                    appResources["UiFontFamily"] = uiFont;
                    appResources["FuncBtnFontSize"] = (double)Settings.FuncBtnFontSize;
                    appResources["BottomButtonFontSize"] = (double)Settings.BottomButtonFontSize;
                    appResources["WaitingListFontSize"] = (double)Settings.WaitingListFontSize;
                    appResources["SongListFontSize"] = (double)Settings.SongListFontSize;

                    // Primary theme color family (used by Material Design buttons, borders, highlights)
                    appResources["PrimaryBrush"] = PrimaryBrush;
                    appResources["PrimaryLightBrush"] = PrimaryLightBrush;
                    appResources["PrimaryHueLightBrush"] = PrimaryLightBrush;
                    appResources["PrimaryHueMidBrush"] = PrimaryBrush;
                    appResources["PrimaryHueMidForegroundBrush"] = Brushes.White;

                    // Cyber Accent resources at application level
                    appResources["ColorAccentCyan"] = accentColor;
                    appResources["BrushAccentCyan"] = accentBrush;
                    appResources["BrushAccentCyanDim"] = accentDimBrush;
                    appResources["BrushCyberActiveGradient"] = cyberGradient;
                    appResources["BrushCyberButtonGradient"] = cyberButtonGradient;

                    // Option 2 & 3 dynamic theme brushes at application level
                    appResources["BrushCapsuleRowBorder"] = capsuleBorderBrush;
                    appResources["BrushCapsuleHoverBg"] = capsuleHoverBgBrush;
                    appResources["BrushCapsuleHoverBorder"] = capsuleHoverBorderBrush;
                    appResources["BrushCapsuleActiveBg"] = capsuleActiveBgBrush;
                    appResources["BrushCapsuleActiveBorder"] = PrimaryBrush;
                    appResources["BrushCapsuleActiveHoverBg"] = capsuleActiveHoverBgBrush;
                    appResources["BrushAccentStripActive"] = accentStripActiveBrush;
                    appResources["BrushAccentStripHover"] = accentStripHoverBrush;
                    appResources["BrushAccentStripInactive"] = accentStripInactiveBrush;
                    appResources["BrushAccentStripGlow"] = accentStripGlowBrush;
                    appResources["BrushTextSelectedGradient"] = textSelectedGradientBrush;

                    // Option 4 inward glow dynamic brushes at application level
                    appResources["BrushInnerGlowDefault"] = innerGlowDefaultBrush;
                    appResources["BrushInnerGlowHover"] = innerGlowHoverBrush;
                    appResources["BrushInnerGlowActive"] = innerGlowActiveBrush;
                    appResources["BrushInnerGlowActiveHover"] = innerGlowActiveHoverBrush;
                    appResources["BrushInnerGlowRim1"] = innerGlowRim1Brush;
                    appResources["BrushInnerGlowRim2"] = innerGlowRim2Brush;
                    appResources["BrushInnerGlowRim3"] = innerGlowRim3Brush;
                    appResources["BrushInnerGlowHoverRim1"] = innerGlowHoverRim1Brush;
                    appResources["BrushInnerGlowHoverRim2"] = innerGlowHoverRim2Brush;
                    appResources["BrushInnerGlowHoverRim3"] = innerGlowHoverRim3Brush;
                    appResources["ColorRowGlowActive"] = primaryCol;
                    appResources["ColorRowGlowHover"] = primaryLightCol;
                }

                // Song list row style selection (1=FloatingCard, 2=Capsule, 3=AccentStrip, 4=Glow)
                int rowStyleOpt = Settings.SongListRowStyle;
                string rowStyleKey = rowStyleOpt switch
                {
                    2 => "SongListRowStyle_Capsule",
                    3 => "SongListRowStyle_AccentStrip",
                    4 => "SongListRowStyle_Glow",
                    _ => "SongListRowStyle_FloatingCard"
                };

                var targetWindow = (window as MainWindow) ?? (System.Windows.Application.Current?.MainWindow as MainWindow);
                if (targetWindow != null)
                {
                    AppLogger.Log($"[RowStyleLog] Target MainWindow found. Window.Resources contains '{rowStyleKey}': {targetWindow.Resources.Contains(rowStyleKey)}");
                    AppLogger.Log($"[RowStyleLog] BrushCyberButtonGradient in Window: {targetWindow.Resources["BrushCyberButtonGradient"]}");

                    if (targetWindow.Resources.Contains(rowStyleKey) && targetWindow.Resources[rowStyleKey] is System.Windows.Style targetRowStyle)
                    {
                        targetWindow.Resources["FloatingDataGridRowStyle"] = targetRowStyle;
                        AppLogger.Log($"[RowStyleLog] Applied {rowStyleKey} to FloatingDataGridRowStyle. Triggers={targetRowStyle.Triggers.Count}, Setters={targetRowStyle.Setters.Count}");
                        if (targetWindow.SongListGrid != null) { targetWindow.SongListGrid.RowStyle = targetRowStyle; AppLogger.Log("[RowStyleLog] Set SongListGrid.RowStyle"); }
                        if (targetWindow.WaitingListGrid != null) { targetWindow.WaitingListGrid.RowStyle = targetRowStyle; AppLogger.Log("[RowStyleLog] Set WaitingListGrid.RowStyle"); }
                        if (targetWindow.QuickSongListGrid != null) { targetWindow.QuickSongListGrid.RowStyle = targetRowStyle; AppLogger.Log("[RowStyleLog] Set QuickSongListGrid.RowStyle"); }
                        if (targetWindow.LanguageSongListGrid != null) { targetWindow.LanguageSongListGrid.RowStyle = targetRowStyle; AppLogger.Log("[RowStyleLog] Set LanguageSongListGrid.RowStyle"); }
                    }
                    else
                    {
                        AppLogger.LogError($"[RowStyleLog] FAILED to find style '{rowStyleKey}' in MainWindow.Resources!", null);
                    }
                }
                else
                {
                    AppLogger.Log($"[RowStyleLog] Skipping DataGrid row style for non-MainWindow ({window.GetType().Name})");
                }

                var gridLines = (rowStyleOpt == 0)
                    ? System.Windows.Controls.DataGridGridLinesVisibility.Horizontal
                    : System.Windows.Controls.DataGridGridLinesVisibility.None;
                window.Resources["DataGridLinesVisibility"] = gridLines;

                // Apply Material Design theme using PaletteHelper
                ApplyMaterialDesignTheme();

                AppLogger.Log("ThemeSettings applied to XAML resources successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to apply ThemeSettings to XAML resources.", ex);
            }
        }

        /// <summary>
        /// Applies the theme colors to Material Design controls using PaletteHelper.
        /// This ensures toggle buttons, sliders, and other MD controls use the custom theme.
        /// </summary>
        private static void ApplyMaterialDesignTheme()
        {
            try
            {
                var paletteHelper = new MaterialDesignThemes.Wpf.PaletteHelper();
                var theme = paletteHelper.GetTheme();

                // Parse the primary color from settings
                var primaryColor = ParseColor(Settings.PrimaryColor, System.Windows.Media.Colors.Goldenrod);
                
                // Set the primary color for Material Design
                theme.SetPrimaryColor(primaryColor);
                
                paletteHelper.SetTheme(theme);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to apply Material Design theme.", ex);
            }
        }

        /// <summary>
        /// Parses a hex color string to a Color object.
        /// </summary>
        private static System.Windows.Media.Color ParseColor(string hexColor, System.Windows.Media.Color fallback)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(hexColor))
                    return fallback;
                
                var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hexColor);
                return color;
            }
            catch
            {
                return fallback;
            }
        }

        /// <summary>
        /// Applies the outlined button style to a programmatically created button.
        /// Sets BorderBrush and Foreground to the theme color, and Background to transparent.
        /// </summary>
        /// <param name="button">The button to style</param>
        /// <param name="fontSize">Optional font size (default is 18)</param>
        public static void ApplyOutlinedButtonStyle(System.Windows.Controls.Button button, double fontSize = 16)
        {
            var themeColor = PrimaryBrush;
            button.BorderBrush = themeColor;
            button.Foreground = themeColor;
            button.Background = System.Windows.Media.Brushes.Transparent;
            button.BorderThickness = new System.Windows.Thickness(1);
            button.FontSize = fontSize;
            button.Padding = new System.Windows.Thickness(10, 0, 10, 0);
            button.VerticalContentAlignment = System.Windows.VerticalAlignment.Center;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        }

        #endregion
    }
}

