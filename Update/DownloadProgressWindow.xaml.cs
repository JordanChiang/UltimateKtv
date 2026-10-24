using System;
using System.Windows;

namespace UltimateKtv
{
    public partial class DownloadProgressWindow : Window
    {
        public DownloadProgressWindow()
        {
            InitializeComponent();
        }

        public void ReportProgress(double percentage, string statusMessage)
        {
            Dispatcher.Invoke(() =>
            {
                DownloadProgressBar.Value = percentage;
                PercentageText.Text = $"{percentage:0.0}%";
                if (!string.IsNullOrEmpty(statusMessage))
                {
                    StatusText.Text = statusMessage;
                }
            });
        }
    }
}
