using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using UltimateKtv;

namespace StyleSimulator
{
    public class StyleSettingsModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #region File Paths
        public static string SettingsPath { get; set; } = FindConfigFile("settings.json");
        public static string ThemeSettingsPath { get; set; } = FindThemeConfigFile();

        // 向下相容屬性別名
        public static string TextSettingsPath
        {
            get => ThemeSettingsPath;
            set => ThemeSettingsPath = value;
        }

        public static string FindThemeConfigFile()
        {
            string path = FindConfigFile("themesettings.json");
            if (File.Exists(path)) return path;

            string legacyPath = FindConfigFile("textsettings.json");
            if (File.Exists(legacyPath)) return legacyPath;

            return path;
        }

        public static string FindConfigFile(string fileName)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] searchPaths = new[]
            {
                Path.Combine(baseDir, "..", "..", "..", "..", "bin", "x64", "Debug", "net8.0-windows", fileName),
                Path.Combine(baseDir, "..", "..", "..", "..", "bin", "x86", "Release", "net8.0-windows", fileName),
                Path.Combine(baseDir, "..", "..", "..", "..", fileName),
                Path.Combine(baseDir, fileName),
                Path.Combine(baseDir, "..", fileName),
                Path.Combine(baseDir, "..", "..", fileName),
                Path.Combine(baseDir, "..", "..", "..", fileName)
            };

            foreach (var path in searchPaths)
            {
                try
                {
                    string fullPath = Path.GetFullPath(path);
                    if (File.Exists(fullPath))
                        return fullPath;
                }
                catch { }
            }

            return Path.Combine(baseDir, fileName);
        }
        #endregion

        // Underlying full AppSettings and ThemeSettings instances
        private AppSettings _appSettings = new AppSettings();
        private ThemeSettings _themeSettings = new ThemeSettings();

        #region Settings.json Properties (Playback Marquee & Notifications)
        public string MarqueeTextString1
        {
            get => _appSettings.MarqueeTextString1;
            set { _appSettings.MarqueeTextString1 = value; OnPropertyChanged(); }
        }

        public string MarqueeTextString2
        {
            get => _appSettings.MarqueeTextString2;
            set { _appSettings.MarqueeTextString2 = value; OnPropertyChanged(); }
        }

        public string MarqueeTextString3
        {
            get => _appSettings.MarqueeTextString3;
            set { _appSettings.MarqueeTextString3 = value; OnPropertyChanged(); }
        }

        public string MarqueeTextStartup
        {
            get => _appSettings.MarqueeTextStartup;
            set { _appSettings.MarqueeTextStartup = value; OnPropertyChanged(); }
        }

        public string MarqueeTextFontFamily
        {
            get => _appSettings.MarqueeTextFontFamily;
            set { _appSettings.MarqueeTextFontFamily = value; OnPropertyChanged(); }
        }

        public string MarqueeTextFontWeight
        {
            get => _appSettings.MarqueeTextFontWeight;
            set { _appSettings.MarqueeTextFontWeight = value; OnPropertyChanged(); }
        }

        public int MarqueeTextFontSize
        {
            get => _appSettings.MarqueeTextFontSize;
            set { _appSettings.MarqueeTextFontSize = value; OnPropertyChanged(); }
        }

        public int MarqueeTextFontStrokeThickness
        {
            get => _appSettings.MarqueeTextFontStrokeThickness;
            set { _appSettings.MarqueeTextFontStrokeThickness = value; OnPropertyChanged(); }
        }

        public string MarqueeTextFillColor
        {
            get => _appSettings.MarqueeTextFillColor;
            set { _appSettings.MarqueeTextFillColor = value; OnPropertyChanged(); }
        }

        public string MarqueeTextStrokeColor
        {
            get => _appSettings.MarqueeTextStrokeColor;
            set { _appSettings.MarqueeTextStrokeColor = value; OnPropertyChanged(); }
        }

        public int MarqueeTextTimeHold
        {
            get => _appSettings.MarqueeTextTimeHold;
            set { _appSettings.MarqueeTextTimeHold = value; OnPropertyChanged(); }
        }

        public int MarqueeTextTimeDuration
        {
            get => _appSettings.MarqueeTextTimeDuration;
            set { _appSettings.MarqueeTextTimeDuration = value; OnPropertyChanged(); }
        }

        public int MarqueeTextPlayCount
        {
            get => _appSettings.MarqueeTextPlayCount;
            set { _appSettings.MarqueeTextPlayCount = value; OnPropertyChanged(); }
        }

        public string MarqueeSongAddedString
        {
            get => _appSettings.MarqueeSongAddedString;
            set { _appSettings.MarqueeSongAddedString = value; OnPropertyChanged(); }
        }

        public string MarqueeSongAddedFontFamily
        {
            get => _appSettings.MarqueeSongAddedFontFamily;
            set { _appSettings.MarqueeSongAddedFontFamily = value; OnPropertyChanged(); }
        }

        public int MarqueeSongAddedFontSize
        {
            get => _appSettings.MarqueeSongAddedFontSize;
            set { _appSettings.MarqueeSongAddedFontSize = value; OnPropertyChanged(); }
        }

        public string MarqueeSongAddedFillColor
        {
            get => _appSettings.MarqueeSongAddedFillColor;
            set { _appSettings.MarqueeSongAddedFillColor = value; OnPropertyChanged(); }
        }

        public int MarqueeSongAddedTimeDuration
        {
            get => _appSettings.MarqueeSongAddedTimeDuration;
            set { _appSettings.MarqueeSongAddedTimeDuration = value; OnPropertyChanged(); }
        }

        public string MarqueeBroadcastFontFamily
        {
            get => _appSettings.MarqueeBroadcastFontFamily;
            set { _appSettings.MarqueeBroadcastFontFamily = value; OnPropertyChanged(); }
        }

        public int MarqueeBroadcastFontSize
        {
            get => _appSettings.MarqueeBroadcastFontSize;
            set { _appSettings.MarqueeBroadcastFontSize = value; OnPropertyChanged(); }
        }

        public string MarqueeBroadcastFillColor
        {
            get => _appSettings.MarqueeBroadcastFillColor;
            set { _appSettings.MarqueeBroadcastFillColor = value; OnPropertyChanged(); }
        }

        public int MarqueeBroadcastTimeDuration
        {
            get => _appSettings.MarqueeBroadcastTimeDuration;
            set { _appSettings.MarqueeBroadcastTimeDuration = value; OnPropertyChanged(); }
        }

        public string HttpServerIp
        {
            get => _appSettings.HttpServerIp;
            set { _appSettings.HttpServerIp = value; OnPropertyChanged(); }
        }

        public int HttpServerPort
        {
            get => _appSettings.HttpServerPort;
            set { _appSettings.HttpServerPort = value; OnPropertyChanged(); }
        }

        public int PublicServerPort
        {
            get => _appSettings.PublicServerPort;
            set { _appSettings.PublicServerPort = value; OnPropertyChanged(); }
        }
        #endregion

        #region ThemeSettings.json Properties (UI Fonts & Colors)
        public string UiFontFamily
        {
            get => _themeSettings.FontFamily;
            set { _themeSettings.FontFamily = value; OnPropertyChanged(); }
        }

        public double SongListFontSize
        {
            get => _themeSettings.SongListFontSize;
            set { _themeSettings.SongListFontSize = value; OnPropertyChanged(); }
        }

        public double WaitingListFontSize
        {
            get => _themeSettings.WaitingListFontSize;
            set { _themeSettings.WaitingListFontSize = value; OnPropertyChanged(); }
        }

        public double FuncBtnFontSize
        {
            get => _themeSettings.FuncBtnFontSize;
            set { _themeSettings.FuncBtnFontSize = value; OnPropertyChanged(); }
        }

        public double BottomButtonFontSize
        {
            get => _themeSettings.BottomButtonFontSize;
            set { _themeSettings.BottomButtonFontSize = value; OnPropertyChanged(); }
        }

        public double WebHostInfoFontSize
        {
            get => _themeSettings.WebHostInfoFontSize;
            set { _themeSettings.WebHostInfoFontSize = value; OnPropertyChanged(); }
        }

        public string PrimaryColor
        {
            get => _themeSettings.PrimaryColor;
            set { _themeSettings.PrimaryColor = value; OnPropertyChanged(); }
        }


        public string DataGridHeaderBgColor
        {
            get => _themeSettings.DataGridColumnHeaderBackgroundColor;
            set { _themeSettings.DataGridColumnHeaderBackgroundColor = value; OnPropertyChanged(); }
        }

        public string DataGridColumnHeaderForegroundColor
        {
            get => _themeSettings.DataGridColumnHeaderForegroundColor;
            set { _themeSettings.DataGridColumnHeaderForegroundColor = value; OnPropertyChanged(); }
        }

        public string AnnouncementForegroundColor
        {
            get => _themeSettings.AnnouncementForegroundColor;
            set { _themeSettings.AnnouncementForegroundColor = value; OnPropertyChanged(); }
        }

        public string StaticTextForegroundColor
        {
            get => _themeSettings.StaticTextForegroundColor;
            set { _themeSettings.StaticTextForegroundColor = value; OnPropertyChanged(); }
        }

        public string WebHostInfoForegroundColor
        {
            get => _themeSettings.WebHostInfoForegroundColor;
            set { _themeSettings.WebHostInfoForegroundColor = value; OnPropertyChanged(); }
        }


        public string PrimaryLightColor
        {
            get => _themeSettings.PrimaryLightColor;
            set { _themeSettings.PrimaryLightColor = value; OnPropertyChanged(); }
        }

        public string AccentColor
        {
            get => _themeSettings.AccentColor;
            set { _themeSettings.AccentColor = value; OnPropertyChanged(); }
        }

        public int SongListRowStyle
        {
            get => _themeSettings.SongListRowStyle;
            set { _themeSettings.SongListRowStyle = value; OnPropertyChanged(); }
        }
        #endregion

        #region Load and Save Methods
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true
        };

        public static StyleSettingsModel Load() => LoadAll();

        public static StyleSettingsModel LoadAll(string? settingsFile = null, string? themeSettingsFile = null)
        {
            var model = new StyleSettingsModel();
            model.LoadSettingsJson(settingsFile ?? SettingsPath);
            model.LoadThemeSettingsJson(themeSettingsFile ?? ThemeSettingsPath);
            return model;
        }

        public void LoadSettingsJson(string filePath)
        {
            SettingsPath = filePath;
            if (!File.Exists(filePath)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                if (loaded != null)
                {
                    _appSettings = loaded;
                    OnPropertyChanged(null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings.json: {ex.Message}");
            }
        }

        public void LoadThemeSettingsJson(string filePath)
        {
            ThemeSettingsPath = filePath;
            if (!File.Exists(filePath)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<ThemeSettings>(json, _jsonOptions);
                if (loaded != null)
                {
                    _themeSettings = loaded;
                    OnPropertyChanged(null);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load themesettings.json: {ex.Message}");
            }
        }

        public void LoadTextSettingsJson(string filePath) => LoadThemeSettingsJson(filePath);

        public bool ApplyThemeSettingsFromJson(string json)
        {
            try
            {
                int currentSongListRowStyle = _themeSettings.SongListRowStyle;
                var loaded = JsonSerializer.Deserialize<ThemeSettings>(json, _jsonOptions);
                if (loaded != null)
                {
                    // 若 JSON 中未明確指定 SongListRowStyle（例如色彩主題範本），保留當前設定
                    if (!json.Contains("SongListRowStyle", StringComparison.OrdinalIgnoreCase))
                    {
                        loaded.SongListRowStyle = currentSongListRowStyle;
                    }

                    _themeSettings = loaded;
                    OnPropertyChanged(null);
                    return true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse theme settings json: {ex.Message}");
            }
            return false;
        }

        public bool ApplyTextSettingsFromJson(string json) => ApplyThemeSettingsFromJson(json);

        public void ApplyThemeSettingsPreset(string filePath)
        {
            if (!File.Exists(filePath)) return;

            try
            {
                string json = File.ReadAllText(filePath);
                ApplyThemeSettingsFromJson(json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load theme settings preset: {ex.Message}");
            }
        }

        public void ApplyTextSettingsPreset(string filePath) => ApplyThemeSettingsPreset(filePath);

        public void ResetSettingsToDefaults()
        {
            var defaults = new AppSettings();
            MarqueeTextString1 = defaults.MarqueeTextString1;
            MarqueeTextString2 = defaults.MarqueeTextString2;
            MarqueeTextString3 = defaults.MarqueeTextString3;
            MarqueeTextStartup = defaults.MarqueeTextStartup;
            MarqueeTextFontFamily = defaults.MarqueeTextFontFamily;
            MarqueeTextFontWeight = defaults.MarqueeTextFontWeight;
            MarqueeTextFontSize = defaults.MarqueeTextFontSize;
            MarqueeTextFontStrokeThickness = defaults.MarqueeTextFontStrokeThickness;
            MarqueeTextFillColor = defaults.MarqueeTextFillColor;
            MarqueeTextStrokeColor = defaults.MarqueeTextStrokeColor;
            MarqueeTextTimeHold = defaults.MarqueeTextTimeHold;
            MarqueeTextTimeDuration = defaults.MarqueeTextTimeDuration;
            MarqueeTextPlayCount = defaults.MarqueeTextPlayCount;

            MarqueeSongAddedString = defaults.MarqueeSongAddedString;
            MarqueeSongAddedFontFamily = defaults.MarqueeSongAddedFontFamily;
            MarqueeSongAddedFontSize = defaults.MarqueeSongAddedFontSize;
            MarqueeSongAddedFillColor = defaults.MarqueeSongAddedFillColor;
            MarqueeSongAddedTimeDuration = defaults.MarqueeSongAddedTimeDuration;

            MarqueeBroadcastFontFamily = defaults.MarqueeBroadcastFontFamily;
            MarqueeBroadcastFontSize = defaults.MarqueeBroadcastFontSize;
            MarqueeBroadcastFillColor = defaults.MarqueeBroadcastFillColor;
            MarqueeBroadcastTimeDuration = defaults.MarqueeBroadcastTimeDuration;

            HttpServerIp = defaults.HttpServerIp;
            HttpServerPort = defaults.HttpServerPort;
            PublicServerPort = defaults.PublicServerPort;
        }

        public void ResetThemeSettingsToDefaults()
        {
            var defaults = new ThemeSettings();
            UiFontFamily = defaults.FontFamily;
            SongListFontSize = defaults.SongListFontSize;
            WaitingListFontSize = defaults.WaitingListFontSize;
            FuncBtnFontSize = defaults.FuncBtnFontSize;
            BottomButtonFontSize = defaults.BottomButtonFontSize;
            WebHostInfoFontSize = defaults.WebHostInfoFontSize;
            PrimaryColor = defaults.PrimaryColor;
            PrimaryLightColor = defaults.PrimaryLightColor;
            AccentColor = defaults.AccentColor;
            DataGridHeaderBgColor = defaults.DataGridColumnHeaderBackgroundColor;
            DataGridColumnHeaderForegroundColor = defaults.DataGridColumnHeaderForegroundColor;
            AnnouncementForegroundColor = defaults.AnnouncementForegroundColor;
            StaticTextForegroundColor = defaults.StaticTextForegroundColor;
            WebHostInfoForegroundColor = defaults.WebHostInfoForegroundColor;
            SongListRowStyle = defaults.SongListRowStyle;
        }

        public void ResetTextSettingsToDefaults() => ResetThemeSettingsToDefaults();

        public bool SaveSettingsJson(string? path = null)
        {
            try
            {
                string targetPath = path ?? SettingsPath;
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                };
                string json = JsonSerializer.Serialize(_appSettings, options);

                foreach (var prop in typeof(AppSettings).GetProperties())
                {
                    var descAttr = prop.GetCustomAttribute<DescriptionAttribute>();
                    if (descAttr != null && !string.IsNullOrWhiteSpace(descAttr.Description))
                    {
                        string search = $"  \"{prop.Name}\":";
                        string replace = $"  // {descAttr.Description}{Environment.NewLine}{search}";
                        json = json.Replace(search, replace);
                    }
                }

                File.WriteAllText(targetPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SaveThemeSettingsJson(string? path = null)
        {
            try
            {
                string targetPath = path ?? ThemeSettingsPath;
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
                };
                string json = JsonSerializer.Serialize(_themeSettings, options);

                foreach (var prop in typeof(ThemeSettings).GetProperties())
                {
                    var descAttr = prop.GetCustomAttribute<DescriptionAttribute>();
                    if (descAttr != null && !string.IsNullOrWhiteSpace(descAttr.Description))
                    {
                        string search = $"  \"{prop.Name}\":";
                        string replace = $"  // {descAttr.Description}{Environment.NewLine}{search}";
                        json = json.Replace(search, replace);
                    }
                }

                File.WriteAllText(targetPath, json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public bool SaveTextSettingsJson(string? path = null) => SaveThemeSettingsJson(path);
        #endregion
    }
}
