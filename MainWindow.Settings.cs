using MaterialDesignThemes.Wpf;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        // Fields for the style switcher functionality, initialized to prevent null warnings.
        private List<string> _buttonStyleKeys = new();
        private int _currentStyleIndex = -1;

        /// <summary>
        /// Initializes the list of style keys for the style switcher demo.
        /// </summary>
        private void InitializeStyleSwitcher()
        {
            // A list of common button styles from the MaterialDesignInXamlToolkit package.
            // This assumes the Material Design resources are loaded in App.xaml.
            _buttonStyleKeys = new List<string>
            {
                "MaterialDesignRaisedButton",
                "MaterialDesignFlatButton",
                "MaterialDesignOutlinedButton",
                //"MaterialDesignRaisedDarkButton",
                //"MaterialDesignRaisedLightButton",
                //"MaterialDesignFloatingActionButton",
                "MaterialDesignPaperSecondaryDarkButton"
                //"MaterialDesignToolButton"
            };
        }

        /// <summary>
        /// Initializes the list of theme options for the theme switcher.
        /// </summary>
        private void InitializeThemeSwitcher()
        {
            _themeKeys = new List<string>
            {
                "Light",
                "Dark"
            };
        }

        /// <summary>
        /// Applies the default Material Design button style at startup.
        /// Sets the style to "MaterialDesignOutlinedButton" by leveraging Button8_Click logic.
        /// </summary>
        private void ApplyDefaultButtonStyleOnStartup()
        {
            try
            {
                // Ensure the style keys are initialized
                if (_buttonStyleKeys == null || _buttonStyleKeys.Count == 0) return;

                // ButtonStyle_Click increments index before use. We want index 2 (Outlined), so preset to 1.
                // _buttonStyleKeys: [0]=Raised, [1]=Flat, [2]=Outlined, [3]=PaperSecondaryDark
                _currentStyleIndex = 1;

                // Invoke the existing handler to apply styles to all relevant keys and update Button8's content.
                ButtonStyle_Click(null!, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyDefaultButtonStyleOnStartup error: {ex.Message}");
                AppLogger.LogError("ApplyDefaultButtonStyleOnStartup error", ex);
            }
        }

        private void LightThemeButton_Click(object sender, RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(MaterialDesignThemes.Wpf.BaseTheme.Light);
            paletteHelper.SetTheme(theme);

            // Recache brushes as the theme/style has changed
            CacheActiveButtonBrushes();
        }

        private void DarkThemeButton_Click(object sender, RoutedEventArgs e)
        {
            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(MaterialDesignThemes.Wpf.BaseTheme.Dark);
            paletteHelper.SetTheme(theme);

            // Recache brushes as the theme/style has changed
            CacheActiveButtonBrushes();
        }

        private void FuncBtnSetting_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Open settings window");
            // Show settings as a modal dialog window
            var settingsWindow = new UserOptionsWindow(this)
            {
                Owner = this
            };
            
            // ShowDialog() blocks interaction with parent window automatically
            settingsWindow.ShowDialog();
        }

        public void ShowPreview()
        {
            _showPreviewInMainWindow = true;
            if (MediaPlayerContainer.Children.Count > 0 && MediaPlayerContainer.Children[0] is Rectangle preview)
            {
                preview.Visibility = Visibility.Visible;
            }
        }

        public void HidePreview()
        {
            _showPreviewInMainWindow = false;
            if (MediaPlayerContainer.Children.Count > 0 && MediaPlayerContainer.Children[0] is Rectangle preview)
            {
                preview.Visibility = Visibility.Collapsed;
            }
        }

        public void SetMediaStretch(Stretch stretchMode)
        {
            if (mediaUriElement != null)
            {
                mediaUriElement.Stretch = stretchMode;
            }
        }

        public void SetAudioAmplify(int value)
        {
            if (mediaUriElement != null)
            {
                // Ensure value is within valid range (100-500)
                int clampedValue = Math.Max(100, Math.Min(500, value));
                mediaUriElement.AudioAmplify = clampedValue;
            }
        }

        public void InitializeDefaultMediaStretch()
        {
            // Set default stretch mode to Fill (always fullscreen)
            SetMediaStretch(Stretch.Fill);
        }

        public bool IsMediaFullScreen()
        {
            return mediaUriElement?.Stretch == Stretch.Fill;
        }

        private void ThemeSubButton_Click(object sender, RoutedEventArgs e)
        {
            // Cycle to the next theme in the list
            _currentThemeIndex = (_currentThemeIndex + 1) % _themeKeys.Count;
            string themeKey = _themeKeys[_currentThemeIndex];

            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();

            switch (themeKey)
            {
                case "Light":
                    theme.SetBaseTheme(MaterialDesignThemes.Wpf.BaseTheme.Light);
                    break;
                case "Dark":
                    theme.SetBaseTheme(MaterialDesignThemes.Wpf.BaseTheme.Dark);
                    break;
            }

            paletteHelper.SetTheme(theme);

            // Recache brushes as the theme/style has changed
            CacheActiveButtonBrushes();
        }

        private void PreviewSubButton_Click(object sender, RoutedEventArgs e)
        {
            _showPreviewInMainWindow = !_showPreviewInMainWindow;

            // This only has an effect if we are in dual-display mode and have a preview rectangle.
            if (MediaPlayerContainer.Children.Count > 0 && MediaPlayerContainer.Children[0] is Rectangle preview)
            {
                preview.Visibility = _showPreviewInMainWindow ? Visibility.Visible : Visibility.Collapsed;
            }

            // Update button text to give user feedback
            if (sender is Button btn)
            {
                btn.Content = _showPreviewInMainWindow ? "隱藏預覽" : "顯示預覽";
            }
        }

        private void VolumeSubButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for volume settings functionality
            MessageBox.Show("音量設定功能 - 待實現", "音量設定", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ButtonStyle_Click(object sender, RoutedEventArgs e)
        {
            // Cycle to the next style in the list
            _currentStyleIndex = (_currentStyleIndex + 1) % _buttonStyleKeys.Count;
            string styleKey = _buttonStyleKeys[_currentStyleIndex];

            // Find the new base style from application resources
            if (this.TryFindResource(styleKey) is not Style baseMdStyle)
            {
                return; // Exit if the style resource doesn't exist
            }

            // List of style keys to update
            var styleKeysToUpdate = new[]
            {
                "FunctionButtonStyle",
                "SecondFilterButtonStyle",
                "PlayerControlButtonStyle",
                "LargePlayerControlButtonStyle",
                "PaginationButtonStyle",
                "SingerGridButtonStyle",
                "QuickWordButtonStyle"
            };

            foreach (var key in styleKeysToUpdate)
            {
                if (FindResource(key) is Style originalStyle)
                {
                    var newStyle = new Style(originalStyle.TargetType, baseMdStyle);
                    foreach (var setter in originalStyle.Setters.OfType<Setter>())
                    {
                        newStyle.Setters.Add(setter);
                    }
                    this.Resources[key] = newStyle;
                }
            }

            // Recache brushes as the theme/style has changed
            CacheActiveButtonBrushes();
        }
    }
}