using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        // Pitch control state
        private int _currentPitchLevel = 0; // -8 to +8
        private bool _isPitchFixed = false;
        private Button? _currentPitchDisplayBtn = null;
        private Button? _fixToggleBtn = null;

        /// <summary>
        /// Handles the pitch control button click to show the pitch adjustment popup
        /// </summary>
        private async void PitchControlBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppLogger.Log($"User action: Open pitch control dialog (current level: {_currentPitchLevel}, fixed: {_isPitchFixed})");
                // Create the pitch control dialog
                var dialogContent = CreatePitchControlDialog();
                
                // Show the dialog
                await ShowDialogSafeAsync(dialogContent, "RootDialog");
            }
            catch (Exception ex)
            {
                DebugLog($"Error showing pitch control dialog: {ex.Message}");
                AppLogger.LogError("Error showing pitch control dialog", ex);
            }
        }

        /// <summary>
        /// Creates the pitch control dialog with 7 buttons (Cyber Aesthetic)
        /// </summary>
        private FrameworkElement CreatePitchControlDialog()
        {
            var dialogContainer = new Border
            {
                Background = (Brush)FindResource("BrushBgSecondary"),
                BorderBrush = (Brush)FindResource("BrushBorderDefault"),
                BorderThickness = new Thickness(1),
                CornerRadius = (CornerRadius)FindResource("RadiusLarge"),
                Padding = new Thickness(24),
                MinWidth = 320
            };

            var dialogPanel = new StackPanel();
            dialogContainer.Child = dialogPanel;

            // Title
            var title = new TextBlock
            {
                Text = Loc.T("Pitch.Title"),
                FontSize = (double)FindResource("FontSizeM"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 16),
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = (Brush)FindResource("BrushAccent")
            };
            dialogPanel.Children.Add(title);

            // Button 1: Current pitch display banner - high contrast vibrant cyber style
            _currentPitchDisplayBtn = new Button
            {
                Content = GetPitchDisplayText(),
                Height = 52,
                Margin = new Thickness(0, 0, 0, 14),
                Style = (Style)FindResource("CyberButton"),
                IsHitTestVisible = false, // Non-clickable display banner without dimming
                Focusable = false,
                FontSize = (double)FindResource("FontSizeM"),
                Foreground = (Brush)FindResource("BrushOnAccentActive"),
                FontWeight = FontWeights.Bold,
                Background = (Brush)FindResource("BrushCyberActiveGradient"),
                BorderBrush = (Brush)FindResource("BrushAccent"),
                BorderThickness = new Thickness(1.5),
                Opacity = 1.0,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = (Color)FindResource("ColorAccent"),
                    BlurRadius = (double)FindResource("GlowBlurActive"),
                    ShadowDepth = 0,
                    Opacity = (double)FindResource("GlowOpacityActive")
                }
            };
            dialogPanel.Children.Add(_currentPitchDisplayBtn);

            // Button 2: 升調 (Pitch Up)
            var pitchUpBtn = new Button
            {
                Content = Loc.T("Pitch.Up"),
                Height = 48,
                Margin = new Thickness(0, 0, 0, 10),
                Style = (Style)FindResource("CyberButton"),
                FontSize = (double)FindResource("FontSizeS")
            };
            pitchUpBtn.Click += (s, e) => AdjustPitch(1);
            dialogPanel.Children.Add(pitchUpBtn);

            // Button 3: 原調 (Original Pitch)
            var originalPitchBtn = new Button
            {
                Content = Loc.T("Pitch.Original"),
                Height = 48,
                Margin = new Thickness(0, 0, 0, 10),
                Style = (Style)FindResource("CyberButton"),
                FontSize = (double)FindResource("FontSizeS")
            };
            originalPitchBtn.Click += (s, e) => SetOriginalPitch();
            dialogPanel.Children.Add(originalPitchBtn);

            // Button 4: 降調 (Pitch Down)
            var pitchDownBtn = new Button
            {
                Content = Loc.T("Pitch.Down"),
                Height = 48,
                Margin = new Thickness(0, 0, 0, 10),
                Style = (Style)FindResource("CyberButton"),
                FontSize = (double)FindResource("FontSizeS")
            };
            pitchDownBtn.Click += (s, e) => AdjustPitch(-1);
            dialogPanel.Children.Add(pitchDownBtn);

            // Button 5: 男調 (Male Pitch)
            var malePitchBtn = new Button
            {
                Content = Loc.T("Pitch.Male"),
                Height = 48,
                Margin = new Thickness(0, 0, 0, 10),
                Style = (Style)FindResource("CyberButton"),
                FontSize = (double)FindResource("FontSizeS")
            };
            malePitchBtn.Click += (s, e) => SetMalePitch();
            dialogPanel.Children.Add(malePitchBtn);

            // Button 6: 女調 (Female Pitch)
            var femalePitchBtn = new Button
            {
                Content = Loc.T("Pitch.Female"),
                Height = 48,
                Margin = new Thickness(0, 0, 0, 10),
                Style = (Style)FindResource("CyberButton"),
                FontSize = (double)FindResource("FontSizeS")
            };
            femalePitchBtn.Click += (s, e) => SetFemalePitch();
            dialogPanel.Children.Add(femalePitchBtn);

            // Button 7: 固定/解除 (Fix/Unfix toggle)
            _fixToggleBtn = new Button
            {
                Content = _isPitchFixed ? Loc.T("Pitch.Unfix") : Loc.T("Pitch.Fix"),
                Height = 48,
                Margin = new Thickness(0, 0, 0, 16),
                Style = (Style)FindResource(_isPitchFixed ? "CyberAccentGoldButton" : "CyberButton"),
                FontSize = (double)FindResource("FontSizeS")
            };
            _fixToggleBtn.Click += (s, e) => TogglePitchFix();
            dialogPanel.Children.Add(_fixToggleBtn);

            // Close button
            var closeBtn = new Button
            {
                Content = Loc.T("Common.Close"),
                Height = 42,
                Style = (Style)FindResource("CyberButton"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                FontSize = (double)FindResource("FontSizeS")
            };
            closeBtn.Click += (s, e) => DialogHost.CloseDialogCommand.Execute(null, null);
            dialogPanel.Children.Add(closeBtn);

            return dialogContainer;
        }

        /// <summary>
        /// Gets the display text for current pitch level
        /// </summary>
        private string GetPitchDisplayText()
        {
            if (_currentPitchLevel == 0)
                return Loc.T("Pitch.CurrentOriginal");
            else if (_currentPitchLevel > 0)
                return Loc.T("Pitch.Current", "+" + _currentPitchLevel);
            else
                return Loc.T("Pitch.Current", _currentPitchLevel);
        }

        /// <summary>
        /// Adjusts pitch by the specified amount
        /// </summary>
        public void AdjustPitch(int adjustment)
        {
            if (mediaUriElement == null || !mediaUriElement.IsPlaying)
            {
                AppLogger.Log($"User action: Pitch adjust {adjustment:+0;-0} failed - no song playing");
                MarqueeAPI.ShowCornerText(Loc.T("Pitch.PlayFirst"), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
                return;
            }

            int newLevel = _currentPitchLevel + adjustment;
            
            // Clamp to range -8 to +8 silently (no error message)
            if (newLevel < -8 || newLevel > 8)
            {
                AppLogger.Log($"User action: Pitch adjust {adjustment:+0;-0} clamped at limit (attempted: {newLevel}, current: {_currentPitchLevel})");
                // Keep current level, just update display
                UpdatePitchDialogDisplay();
                return;
            }

            AppLogger.Log($"User action: Pitch adjusted from {_currentPitchLevel} to {newLevel} (adjustment: {adjustment:+0;-0})");
            _currentPitchLevel = newLevel;
            ApplyPitchChange();
            
            // Show notification
            MarqueeAPI.ShowCornerText(Loc.T("Pitch.Marquee", GetPitchDisplayText()), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
            
            // Update the dialog display without closing
            UpdatePitchDialogDisplay();
        }

        /// <summary>
        /// Sets pitch to original (0)
        /// </summary>
        public void SetOriginalPitch()
        {
            if (mediaUriElement == null || !mediaUriElement.IsPlaying)
            {
                AppLogger.Log("User action: Set original pitch failed - no song playing");
                MarqueeAPI.ShowCornerText(Loc.T("Pitch.PlayFirst"), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
                return;
            }

            AppLogger.Log($"User action: Set pitch to original (from {_currentPitchLevel} to 0)");
            _currentPitchLevel = 0;
            ApplyPitchChange();
            MarqueeAPI.ShowCornerText(Loc.T("Pitch.MarqueeOriginal"), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
            UpdatePitchDialogDisplay();
        }

        /// <summary>
        /// Sets pitch to male voice (+4)
        /// </summary>
        private void SetMalePitch()
        {
            if (mediaUriElement == null || !mediaUriElement.IsPlaying)
            {
                AppLogger.Log("User action: Set male pitch failed - no song playing");
                MarqueeAPI.ShowCornerText(Loc.T("Pitch.PlayFirst"), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
                return;
            }

            AppLogger.Log($"User action: Set pitch to male voice (from {_currentPitchLevel} to -4)");
            _currentPitchLevel = -4;
            ApplyPitchChange();
            MarqueeAPI.ShowCornerText(Loc.T("Pitch.MarqueeMale"), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
            UpdatePitchDialogDisplay();
        }

        /// <summary>
        /// Sets pitch to female voice (-4)
        /// </summary>
        private void SetFemalePitch()
        {
            if (mediaUriElement == null || !mediaUriElement.IsPlaying)
            {
                AppLogger.Log("User action: Set female pitch failed - no song playing");
                MarqueeAPI.ShowCornerText(Loc.T("Pitch.PlayFirst"), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
                return;
            }

            AppLogger.Log($"User action: Set pitch to female voice (from {_currentPitchLevel} to +4)");
            _currentPitchLevel = 4;
            ApplyPitchChange();
            MarqueeAPI.ShowCornerText(Loc.T("Pitch.MarqueeFemale"), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
            UpdatePitchDialogDisplay();
        }
        
        /// <summary>
        /// Updates the pitch dialog display to reflect current state
        /// </summary>
        private void UpdatePitchDialogDisplay()
        {
            // Update the current pitch display button if it exists
            if (_currentPitchDisplayBtn != null)
            {
                _currentPitchDisplayBtn.Content = GetPitchDisplayText();
            }
        }

        /// <summary>
        /// Toggles the pitch fix state
        /// </summary>
        private void TogglePitchFix()
        {
            _isPitchFixed = !_isPitchFixed;
            
            AppLogger.Log($"User action: Pitch fix toggled to {(_isPitchFixed ? "FIXED" : "UNFIXED")} at level {_currentPitchLevel}");
            
            if (_fixToggleBtn != null)
            {
                _fixToggleBtn.Content = _isPitchFixed ? Loc.T("Pitch.Unfix") : Loc.T("Pitch.Fix");
                _fixToggleBtn.Style = (Style)FindResource(_isPitchFixed ? "CyberAccentGoldButton" : "CyberButton");
            }
            
            if (_isPitchFixed)
            {
                MarqueeAPI.ShowCornerText(Loc.T("Pitch.Fixed", _currentPitchLevel), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
            }
            else
            {
                MarqueeAPI.ShowCornerText(Loc.T("Pitch.Unfixed"), 3, MarqueePosition.BottomRight, 38, MarqueeDisplayDevice.PlayerScreen);
            }
        }

        /// <summary>
        /// Applies the current pitch level to the media player
        /// Uses the formula: pitch = 1.06^level for semitone adjustments
        /// </summary>
        private void ApplyPitchChange()
        {
            if (mediaUriElement == null) return;

            try
            {
                // Calculate pitch multiplier
                // Each semitone is approximately 1.059463 (2^(1/12))
                // Using 1.06 as an approximation for easier calculation
                double pitchMultiplier = Math.Pow(1.059463, _currentPitchLevel);
                
                mediaUriElement.AudioPitch = Math.Round(pitchMultiplier, 4, MidpointRounding.AwayFromZero);
                
                AppLogger.Log($"Pitch applied: level={_currentPitchLevel}, multiplier={mediaUriElement.AudioPitch:F4}");
                DebugLog($"Pitch adjusted to level {_currentPitchLevel}, multiplier: {mediaUriElement.AudioPitch}");
            }
            catch (Exception ex)
            {
                DebugLog($"Error applying pitch change: {ex.Message}");
                AppLogger.LogError("Error applying pitch change", ex);
            }
        }

        /// <summary>
        /// Resets pitch when a new song starts (unless pitch is fixed)
        /// Call this from MediaOpened event
        /// </summary>
        private void ResetPitchForNewSong()
        {
            if (!_isPitchFixed)
            {
                _currentPitchLevel = 0;
                if (mediaUriElement != null)
                {
                    mediaUriElement.AudioPitch = 1.0;
                }
            }
            else
            {
                AppLogger.Log($"Pitch fixed mode: reapplying level {_currentPitchLevel} for new song");
                // If pitch is fixed, reapply the current pitch level
                ApplyPitchChange();
            }
        }
    }
}
