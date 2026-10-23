using System;
using System.IO;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace UltimateKtv
{
    /// <summary>設定視窗按「套用」後的變更：Previous 為套用前的設定複本，Current 為目前設定。</summary>
    public sealed class SettingsChangedEventArgs : EventArgs
    {
        public SettingsChangedEventArgs(AppSettings previous, AppSettings current)
        {
            Previous = previous;
            Current = current;
        }

        public AppSettings Previous { get; }
        public AppSettings Current { get; }
    }

    /// <summary>
    /// Manages loading and saving of application settings from a JSON file.
    /// </summary>
    public sealed class SettingsManager
    {
        /// <summary>設定已套用並存檔 (UI 執行緒)；需要依設定調整狀態的地方訂閱這個事件，不由設定視窗逐一呼叫。</summary>
        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        /// <summary>
        /// 存檔並通知訂閱者。previous 是修改 CurrentSettings 之前用 <see cref="AppSettings.Clone"/> 取得的複本。
        /// </summary>
        public void ApplyChanges(AppSettings previous)
        {
            SaveSettings();
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, CurrentSettings));
        }

        private static readonly Lazy<SettingsManager> _lazyInstance = new Lazy<SettingsManager>(() => new SettingsManager());
        public static SettingsManager Instance => _lazyInstance.Value;

        private readonly string _settingsFilePath;
        public AppSettings CurrentSettings { get; private set; }

        private SettingsManager()
        {
            _settingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");
            CurrentSettings = new AppSettings(); // Start with defaults
        }

        /// <summary>
        /// Loads settings from the JSON file. If the file doesn't exist, it creates one with default values.
        /// </summary>
        public void LoadSettings()
        {
            AppLogger.Log($"Attempting to load settings from: {_settingsFilePath}");
            if (File.Exists(_settingsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_settingsFilePath);

                    // 容錯載入：寫錯的項目只退回該項預設值，其他設定保留
                    var result = SettingsJson.Load<AppSettings>(json);
                    CurrentSettings = result.Value;
                    bool needsForceSave = result.NeedsSave;

                    if (result.SyntaxError)
                    {
                        AppLogger.Log("settings.json cannot be parsed (syntax error or empty). All settings fall back to defaults.");
                    }
                    if (result.InvalidItems.Count > 0)
                    {
                        AppLogger.Log($"settings.json invalid items reset to defaults: {string.Join(", ", result.InvalidItems)}");
                    }
                    if (result.MisnamedItems.Count > 0)
                    {
                        AppLogger.Log($"settings.json item names corrected (case): {string.Join(", ", result.MisnamedItems)}");
                    }
                    if (!result.SyntaxError && result.MissingItems.Count > 0)
                    {
                        AppLogger.Log($"New setting items found: {string.Join(", ", result.MissingItems)}. Updating settings file to include default values.");
                    }
                    if (result.HasProblems)
                    {
                        // 存檔會覆蓋原本寫錯的內容，先留一份備份
                        string? backup = SettingsJson.Backup(_settingsFilePath);
                        if (backup != null) AppLogger.Log($"Original settings.json backed up to {backup}");
                    }

                    // 超出有效範圍的設定拉回範圍內最接近的值
                    var corrected = CurrentSettings.Normalize();
                    if (corrected.Count > 0)
                    {
                        AppLogger.Log($"settings.json values out of range corrected: {string.Join(", ", corrected)}");
                        needsForceSave = true;
                    }

                    // Validate UiLanguage (unsupported value falls back to zh-TW)
                    if (!Loc.IsSupported(CurrentSettings.UiLanguage))
                    {
                        AppLogger.Log($"UiLanguage value '{CurrentSettings.UiLanguage}' is not supported. Correcting to {Loc.DefaultCulture}.");
                        CurrentSettings.UiLanguage = Loc.DefaultCulture;
                        needsForceSave = true;
                    }

                    if (needsForceSave)
                    {
                        SaveSettings(); // Save corrected or new values
                    }
                    
                    AppLogger.Log("Settings loaded successfully from file.");
                }
                catch (Exception ex)
                {
                    // 讀檔失敗 (例如檔案被鎖住)：本次使用預設值；存檔前先備份原檔
                    AppLogger.LogError("Failed to read settings.json. Falling back to defaults.", ex);
                    CurrentSettings = new AppSettings();
                    SettingsJson.Backup(_settingsFilePath);
                    SaveSettings();
                }
            }
            else
            {
                AppLogger.Log("settings.json not found. Creating a new one with default values.");
                // File doesn't exist, so create it with default settings.
                CurrentSettings = new AppSettings();
                SaveSettings();
            }
        }

        /// <summary>
        /// Saves the current settings to the JSON file with comments.
        /// </summary>
        public void SaveSettings()
        {
            try
            {
                AppLogger.Log("Saving current settings to file.");
                var corrected = CurrentSettings.Normalize();
                if (corrected.Count > 0) AppLogger.Log($"Settings out of range corrected before save: {string.Join(", ", corrected)}");
                
                var sb = new StringBuilder();
                sb.AppendLine("{");

                PropertyInfo[] properties = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                var valueOptions = new JsonSerializerOptions
                {
                    Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                    WriteIndented = false // We handle indentation manually
                };

                for (int i = 0; i < properties.Length; i++)
                {
                    var prop = properties[i];
                    
                    // Add description as comment if exists
                    var descriptionAttribute = prop.GetCustomAttribute<DescriptionAttribute>();
                    if (descriptionAttribute != null)
                    {
                        // 說明依介面語言輸出 (語言檔沒有時用 [Description] 的繁中原文)
                        sb.AppendLine($"  // {Loc.Find("SettingDesc." + prop.Name) ?? descriptionAttribute.Description}");
                    }

                    // Serialize the value
                    string jsonValue = JsonSerializer.Serialize(prop.GetValue(CurrentSettings), valueOptions);
                    
                    // Construct the property line
                    sb.Append($"  \"{prop.Name}\": {jsonValue}");

                    // Add comma if not the last property
                    if (i < properties.Length - 1)
                    {
                        sb.AppendLine(",");
                    }
                    else
                    {
                        sb.AppendLine("");
                    }
                    
                    // Add an empty line for readability between properties
                    if (i < properties.Length - 1)
                    {
                        sb.AppendLine("");
                    }
                }

                sb.AppendLine("}");
                
                File.WriteAllText(_settingsFilePath, sb.ToString());
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Failed to save settings.json.", ex);
            }
        }

        /// <summary>
        /// Validates and auto-corrects monitor settings to prevent conflicts.
        /// Returns true if settings were modified (and correction was needed, not just validation).
        /// </summary>
        /// <param name="isSingleMonitor">If true, allows ConsoleScreen and PlayerScreen to be the same (single monitor mode).</param>
        public bool ValidateAndCorrectMonitorSettings(bool isSingleMonitor = false)
        {
            bool wasModified = false;
            
            // Get available monitors
            var monitors = VideoDisplayWindow.GetAvailableMonitorsInfo();
            int monitorCount = monitors.Count;
            
            // If only one monitor, single-monitor mode is the correct state
            if (monitorCount <= 1)
            {
                if (CurrentSettings.ConsoleScreen != 1 || CurrentSettings.PlayerScreen != 1)
                {
                    AppLogger.Log("Single monitor detected. Setting both ConsoleScreen and PlayerScreen to 1.");
                    CurrentSettings.ConsoleScreen = 1;
                    CurrentSettings.PlayerScreen = 1;
                    wasModified = true;
                }
                // If already set to 1,1 in single monitor mode, no correction needed
                return wasModified;
            }
            
            // Multi-monitor system: ensure monitor indices are within valid range
            if (CurrentSettings.ConsoleScreen < 1 || CurrentSettings.ConsoleScreen > monitorCount)
            {
                AppLogger.Log($"Invalid ConsoleScreen setting detected: {CurrentSettings.ConsoleScreen}. Valid range is 1 to {monitorCount}. Auto-correcting.");
                CurrentSettings.ConsoleScreen = Math.Min(2, monitorCount); // Prefer monitor 2 if available, else 1
                wasModified = true;
            }
            
            if (CurrentSettings.PlayerScreen < 1 || CurrentSettings.PlayerScreen > monitorCount)
            {
                AppLogger.Log($"Invalid PlayerScreen setting detected: {CurrentSettings.PlayerScreen}. Valid range is 1 to {monitorCount}. Auto-correcting.");
                CurrentSettings.PlayerScreen = 1; // Default to monitor 1
                wasModified = true;
            }
            
            // In multi-monitor system, if user intentionally set both to same screen, allow it (single-monitor mode)
            // Only auto-correct if the settings are invalid (out of range), not if they're the same
            // This allows users to choose single-monitor mode even with multiple monitors available
            
            return wasModified;
        }
    }
}