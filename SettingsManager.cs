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
    /// <summary>
    /// Manages loading and saving of application settings from a JSON file.
    /// </summary>
    public sealed class SettingsManager
    {
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
                    var options = new JsonSerializerOptions
                    {
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    CurrentSettings = JsonSerializer.Deserialize<AppSettings>(json, options) ?? new AppSettings();
                    
                    bool needsForceSave = false;

                    // Check for missing properties to handle new items in old files
                    try
                    {
                        var docOptions = new JsonDocumentOptions
                        {
                            CommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true
                        };
                        using (JsonDocument doc = JsonDocument.Parse(json, docOptions))
                        {
                            var root = doc.RootElement;
                            PropertyInfo[] properties = typeof(AppSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance);
                            foreach (var prop in properties)
                            {
                                if (!root.TryGetProperty(prop.Name, out _))
                                {
                                    AppLogger.Log($"New setting item found: {prop.Name}. Updating settings file to include default values.");
                                    needsForceSave = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("Error checking for missing properties in settings.json.", ex);
                    }

                    // Validate and correct AudioAmplify range (100-500)
                    if (CurrentSettings.AudioAmplify < 100 || CurrentSettings.AudioAmplify > 500)
                    {
                        AppLogger.Log($"AudioAmplify value {CurrentSettings.AudioAmplify} is out of range. Correcting to default 200.");
                        CurrentSettings.AudioAmplify = 200;
                        needsForceSave = true;
                    }

                    // Validate and correct YoutubeSearchCount range (10-100)
                    if (CurrentSettings.YoutubeSearchCount < 10 || CurrentSettings.YoutubeSearchCount > 100)
                    {
                        AppLogger.Log($"YoutubeSearchCount value {CurrentSettings.YoutubeSearchCount} is out of range. Correcting to default 50.");
                        CurrentSettings.YoutubeSearchCount = 50;
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
                    AppLogger.LogError("Failed to deserialize settings.json. Falling back to defaults.", ex);
                    // If deserialization fails, fall back to default settings and save them.
                    CurrentSettings = new AppSettings();
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
                        sb.AppendLine($"  // {descriptionAttribute.Description}");
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