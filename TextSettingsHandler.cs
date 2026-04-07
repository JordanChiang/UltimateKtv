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
        /// Gets the marquee foreground brush.
        /// </summary>
        public static Brush MarqueeForeground => ParseBrush(Settings.MarqueeForegroundColor, Brushes.White);


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
        /// Gets the song added message foreground brush.
        /// </summary>
        public static Brush SongAddedForeground => ParseBrush(Settings.SongAddedForegroundColor, Brushes.LightGreen);

        /// <summary>
        /// Gets the configured font family.
        /// </summary>
        public static FontFamily FontFamily => new FontFamily(Settings.FontFamily);

        #endregion



        #region Convenience Properties - UI Brushes

        /// <summary>
        /// Gets the DataGrid column header background brush.
        /// </summary>
        public static Brush DataGridColumnHeaderBackground => ParseBrush(Settings.DataGridColumnHeaderBackgroundColor, new SolidColorBrush(Color.FromRgb(74, 74, 74)));

        /// <summary>
        /// Gets the DataGrid column header foreground brush.
        /// </summary>
        public static Brush DataGridColumnHeaderForeground => ParseBrush(Settings.DataGridColumnHeaderForegroundColor, Brushes.White);

        /// <summary>
        /// Gets the bright border brush.
        /// </summary>
        public static Brush BrightBorderBrush => ParseBrush(Settings.BrightBorderColor, Brushes.Gold);

        /// <summary>
        /// Gets the primary theme brush.
        /// </summary>
        public static Brush PrimaryBrush => ParseBrush(Settings.PrimaryColor, new SolidColorBrush(Color.FromRgb(103, 58, 183)));

        /// <summary>
        /// Gets the primary mid-tone brush.
        /// </summary>
        public static Brush PrimaryMidBrush => ParseBrush(Settings.PrimaryMidColor, new SolidColorBrush(Color.FromRgb(126, 87, 194)));

        /// <summary>
        /// Gets the primary dark brush.
        /// </summary>
        public static Brush PrimaryDarkBrush => ParseBrush(Settings.PrimaryDarkColor, new SolidColorBrush(Color.FromRgb(81, 45, 168)));

        /// <summary>
        /// Gets the primary light brush.
        /// </summary>
        public static Brush PrimaryLightBrush => ParseBrush(Settings.PrimaryLightColor, new SolidColorBrush(Color.FromRgb(179, 157, 219)));

        /// <summary>
        /// Gets the primary foreground brush (text on primary-colored elements).
        /// </summary>
        public static Brush PrimaryForegroundBrush => ParseBrush(Settings.PrimaryForegroundColor, Brushes.White);


        #endregion

        #region Pitch Control Dialog Brushes

        /// <summary>
        /// Gets the pitch dialog background brush.
        /// </summary>
        public static Brush PitchDialogBackgroundBrush => ParseBrush(Settings.PitchDialogBackgroundColor, new SolidColorBrush(Color.FromRgb(66, 66, 66)));

        /// <summary>
        /// Gets the pitch dialog foreground brush.
        /// </summary>
        public static Brush PitchDialogForegroundBrush => ParseBrush(Settings.PitchDialogForegroundColor, Brushes.White);

        /// <summary>
        /// Gets the pitch dialog button background brush. Falls back to PrimaryMidBrush if not set.
        /// </summary>
        public static Brush PitchDialogButtonBackgroundBrush => 
            !string.IsNullOrWhiteSpace(Settings.PitchDialogButtonBackgroundColor) 
                ? ParseBrush(Settings.PitchDialogButtonBackgroundColor, null) ?? PrimaryMidBrush
                : PrimaryMidBrush;

        /// <summary>
        /// Gets the pitch dialog button foreground brush. Falls back to PrimaryForegroundBrush if not set.
        /// </summary>
        public static Brush PitchDialogButtonForegroundBrush => 
            !string.IsNullOrWhiteSpace(Settings.PitchDialogButtonForegroundColor) 
                ? ParseBrush(Settings.PitchDialogButtonForegroundColor, null) ?? PrimaryForegroundBrush
                : PrimaryForegroundBrush;

        /// <summary>
        /// Gets the pitch dialog button border brush. Falls back to PrimaryMidBrush if not set.
        /// </summary>
        public static Brush PitchDialogButtonBorderBrush => 
            !string.IsNullOrWhiteSpace(Settings.PitchDialogButtonBorderColor) 
                ? ParseBrush(Settings.PitchDialogButtonBorderColor, null) ?? PrimaryMidBrush
                : PrimaryMidBrush;

        /// <summary>
        /// Gets the pitch display button background brush. Falls back to PrimaryMidBrush if not set.
        /// </summary>
        public static Brush PitchDisplayButtonBackgroundBrush => 
            !string.IsNullOrWhiteSpace(Settings.PitchDisplayButtonBackgroundColor) 
                ? ParseBrush(Settings.PitchDisplayButtonBackgroundColor, null) ?? PrimaryMidBrush
                : PrimaryMidBrush;

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

                // Window-level resources (defined in MainWindow.xaml Resources section)
                window.Resources["DataGridColumnHeaderBackground"] = DataGridColumnHeaderBackground;
                window.Resources["DataGridColumnHeaderForeground"] = DataGridColumnHeaderForeground;
                window.Resources["BrightBorderBrush"] = BrightBorderBrush;

                // UI Font Size Overrides
                window.Resources["FuncBtnFontSize"] = Settings.FuncBtnFontSize;
                window.Resources["BottomButtonFontSize"] = Settings.BottomButtonFontSize;
                window.Resources["WaitingListFontSize"] = Settings.WaitingListFontSize;
                window.Resources["SongListFontSize"] = Settings.SongListFontSize;

                // Application-level resources (Material Design brushes are defined at App level)
                // These must be set at Application.Resources to override DynamicResource lookups
                var appResources = System.Windows.Application.Current.Resources;
                
                // Primary theme color family (used by buttons, borders, highlights)
                appResources["PrimaryBrush"] = PrimaryBrush;
                appResources["PrimaryHueLightBrush"] = PrimaryLightBrush;
                appResources["PrimaryHueMidBrush"] = PrimaryMidBrush;
                appResources["PrimaryHueDarkBrush"] = PrimaryDarkBrush;
                appResources["PrimaryHueMidForegroundBrush"] = PrimaryForegroundBrush;
                appResources["PrimaryHueLightForegroundBrush"] = PrimaryForegroundBrush;
                appResources["PrimaryHueDarkForegroundBrush"] = PrimaryForegroundBrush;

                // Singer grid button colors (these are Window-level resources)
                window.Resources["SingerButtonBackground"] = PrimaryMidBrush;
                window.Resources["SingerButtonBorderBrush"] = PrimaryDarkBrush;
                window.Resources["SingerButtonForeground"] = PrimaryForegroundBrush;

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
                var primaryColor = ParseColor(Settings.PrimaryMidColor, System.Windows.Media.Colors.Goldenrod);
                
                // Set the primary color for Material Design
                theme.SetPrimaryColor(primaryColor);
                
                paletteHelper.SetTheme(theme);
                AppLogger.Log($"Material Design theme primary color set to: {Settings.PrimaryMidColor}");
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
        public static void ApplyOutlinedButtonStyle(System.Windows.Controls.Button button, double fontSize = 18)
        {
            var themeColor = PrimaryMidBrush;
            button.BorderBrush = themeColor;
            button.Foreground = themeColor;
            button.Background = System.Windows.Media.Brushes.Transparent;
            button.BorderThickness = new System.Windows.Thickness(1);
            button.FontSize = fontSize;
            button.Padding = new System.Windows.Thickness(8, 5, 8, 5);
        }

        #endregion
    }
}

