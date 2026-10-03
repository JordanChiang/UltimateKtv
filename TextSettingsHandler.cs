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
    /// Handles reading and processing static text settings from JSON file.
    /// Settings are loaded once at startup and cached for application use.
    /// </summary>
    public static class TextSettingsHandler
    {
        private static readonly string _settingsFilePath;
        private static TextSettings _settings = null!;
        private static bool _isLoaded = false;

        static TextSettingsHandler()
        {
            _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "textsettings.json");
        }

        /// <summary>
        /// Gets the current text settings. Loads from file if not already loaded.
        /// </summary>
        public static TextSettings Settings
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
        /// Loads text settings from the JSON file. Creates default file if not exists.
        /// </summary>
        public static void LoadSettings()
        {
            AppLogger.Log($"Loading text settings from: {_settingsFilePath}");

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
                    _settings = JsonSerializer.Deserialize<TextSettings>(json, options) ?? new TextSettings();
                    SaveSettings(); // Update the physical file to inject comments and missing schema keys
                    AppLogger.Log("Text settings loaded successfully.");
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to load textsettings.json. Using defaults.", ex);
                    _settings = new TextSettings();
                    SaveSettings();
                }
            }
            else
            {
                AppLogger.Log("textsettings.json not found. Creating with default values.");
                _settings = new TextSettings();
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
                foreach (var prop in typeof(TextSettings).GetProperties())
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
                AppLogger.Log("Text settings file saved and updated.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to save textsettings.json.", ex);
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
                // 若為預設值 "#FF4A4A4A"、空值或未特別自訂，使用深藍科技卡片微光漸層 (#D1121826 -> #D91C263C)
                if (string.IsNullOrWhiteSpace(hex) || string.Equals(hex, "#FF4A4A4A", StringComparison.OrdinalIgnoreCase))
                {
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
        /// Gets the resolved Cyber Active Gradient Brush (40% alpha -> 25% darkened alpha).
        /// </summary>
        public static LinearGradientBrush CyberActiveGradientBrush
        {
            get
            {
                var c = ResolvedAccentColor;
                var startColor = System.Windows.Media.Color.FromArgb(102, c.R, c.G, c.B); // ~40%
                var endColor = System.Windows.Media.Color.FromArgb(64, (byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55)); // ~25% darkened
                var gradient = new LinearGradientBrush
                {
                    StartPoint = new System.Windows.Point(0, 0),
                    EndPoint = new System.Windows.Point(1, 1)
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
                AppLogger.Log("Applying TextSettings to XAML resources...");

                // Dynamic Cyber Accent Brushes & Colors
                var accentColor = ResolvedAccentColor;
                var accentBrush = AccentBrush;
                var accentDimBrush = AccentDimBrush;
                var cyberGradient = CyberActiveGradientBrush;

                // Window-level resources (defined in MainWindow.xaml Resources section)
                window.Resources["DataGridColumnHeaderBackground"] = DataGridColumnHeaderBackground;
                window.Resources["DataGridColumnHeaderForeground"] = DataGridColumnHeaderForeground;
                window.Resources["PrimaryBrush"] = PrimaryBrush;

                // Window-level cyber accent resources
                window.Resources["ColorAccentCyan"] = accentColor;
                window.Resources["BrushAccentCyan"] = accentBrush;
                window.Resources["BrushAccentCyanDim"] = accentDimBrush;
                window.Resources["BrushCyberActiveGradient"] = cyberGradient;

                // Dynamic UI Font Family
                var uiFont = FontFamily;
                window.FontFamily = uiFont;
                window.Resources["UiFontFamily"] = uiFont;

                // UI Font Size Overrides (must be double for WPF FontSize dependency properties)
                window.Resources["FuncBtnFontSize"] = (double)Settings.FuncBtnFontSize;
                window.Resources["BottomButtonFontSize"] = (double)Settings.BottomButtonFontSize;
                window.Resources["WaitingListFontSize"] = (double)Settings.WaitingListFontSize;
                window.Resources["SongListFontSize"] = (double)Settings.SongListFontSize;

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
                    appResources["PrimaryHueLightBrush"] = PrimaryLightBrush;
                    appResources["PrimaryHueMidBrush"] = PrimaryBrush;
                    appResources["PrimaryHueMidForegroundBrush"] = Brushes.White;

                    // Cyber Accent resources at application level
                    appResources["ColorAccentCyan"] = accentColor;
                    appResources["BrushAccentCyan"] = accentBrush;
                    appResources["BrushAccentCyanDim"] = accentDimBrush;
                    appResources["BrushCyberActiveGradient"] = cyberGradient;
                }

                // Singer grid button background (Window-level resource)
                window.Resources["SingerButtonBackground"] = PrimaryBrush;

                // Apply Material Design theme using PaletteHelper
                ApplyMaterialDesignTheme();

                AppLogger.Log("TextSettings applied to XAML resources successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to apply TextSettings to XAML resources.", ex);
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

