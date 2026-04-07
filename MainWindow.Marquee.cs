using System.Windows.Media;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        #region Marquee Control Methods

        /// <summary>
        /// Shows a marquee with the specified parameters
        /// </summary>
        /// <param name="text">The text to display</param>
        /// <param name="color">Text color brush</param>
        /// <param name="fontFamily">Font family for the text</param>
        /// <param name="fontSize">Font size</param>
        /// <param name="repeatCount">Number of times to repeat the animation</param>
        /// <param name="position">Position of the marquee (top or bottom)</param>
        /// <param name="speed">Animation speed in pixels per second</param>
        /// <param name="displayDevice">Target display device (0 = main window, 1+ = secondary displays)</param>
        public void ShowMarquee(string text, Brush color, FontFamily fontFamily, double fontSize,
            int repeatCount, MarqueePosition position, double speed, int displayDevice)
        {
            MarqueeManager.Instance.ShowMarquee(text, color, fontFamily, fontSize, repeatCount, position, speed, displayDevice);
        }

        /// <summary>
        /// Shows a marquee with default styling
        /// </summary>
        /// <param name="text">The text to display</param>
        /// <param name="displayDevice">Target display device (0 = main window, 1+ = secondary displays)</param>
        public void ShowMarquee(string text, int displayDevice = 0)
        {
            ShowMarquee(text, TextSettingsHandler.MarqueeForeground, TextSettingsHandler.FontFamily, 
                TextSettingsHandler.Settings.MarqueeFontSize, TextSettingsHandler.Settings.MarqueeRepeatCount, 
                MarqueePosition.Bottom, TextSettingsHandler.Settings.MarqueeSpeed, displayDevice);
        }

        /// <summary>
        /// Stops the marquee on the specified display device
        /// </summary>
        /// <param name="displayDevice">Target display device (0 = main window, 1+ = secondary displays)</param>
        public void StopMarquee(int displayDevice = 0)
        {
            MarqueeManager.Instance.StopMarquee(displayDevice);
        }

        /// <summary>
        /// Stops all active marquees
        /// </summary>
        public void StopAllMarquees()
        {
            MarqueeManager.Instance.StopAllMarquees();
        }

        #endregion
    }
}