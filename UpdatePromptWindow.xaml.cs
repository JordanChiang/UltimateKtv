using System.Windows;

namespace UltimateKtv
{
    public partial class UpdatePromptWindow : Window
    {
        public bool DoUpdate { get; private set; } = false;

        public UpdatePromptWindow(string currentVersion, string newVersion, string releaseNotes)
        {
            InitializeComponent();
            CurrentVersionText.Text = currentVersion;
            NewVersionText.Text = newVersion;
            ReleaseNotesText.Text = string.IsNullOrWhiteSpace(releaseNotes) ? "無詳細更新說明" : releaseNotes;
        }

        private void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            DoUpdate = true;
            this.Close();
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            DoUpdate = false;
            this.Close();
        }
    }
}
