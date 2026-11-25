using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    public partial class MainWindow
    {
        /// <summary>
        /// 歌曲列表列內的「♥」按鈕：加入最愛
        /// </summary>
        private void SongFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            // 縮圖格的 ♥ 在 ThumbnailButton 的範本裡：Click 會冒泡到外層，不擋下會同時點歌
            e.Handled = true;
            if ((sender as FrameworkElement)?.DataContext is not SongDisplayItem songItem || string.IsNullOrEmpty(songItem.SongId)) return;
            AppLogger.Log($"User action: Favorite button clicked for song '{songItem.SongName}'");
            // Clone: 列表與縮圖格使用固定列，換頁時列的內容會被改掉
            ShowAddToFavoriteDialog(songItem.Clone());
            ClearSongGridsSelection();
        }

        /// <summary>
        /// 加入最愛視窗的「歌曲: 歌名」：歌名用主題色加粗，和標籤、下面的歌手/來源分得清楚 (一般歌與 YouTube 歌相同)。
        /// </summary>
        private TextBlock CreateFavoriteSongLabel(string songName)
        {
            var label = new TextBlock
            {
                FontSize = (double)FindResource("FontSizeS"),
                Margin = new Thickness(0, 0, 0, 5),
                TextWrapping = TextWrapping.Wrap
            };
            // 語言檔的格式是「歌曲: {0}」；翻譯沒有 {0} 時整句照舊顯示，不上色
            string template = Loc.T("Fav.SongLabel");
            int index = template.IndexOf("{0}", StringComparison.Ordinal);
            if (index < 0)
            {
                label.Text = Loc.T("Fav.SongLabel", songName);
                return label;
            }
            var name = new System.Windows.Documents.Run(songName) { FontWeight = FontWeights.SemiBold };
            name.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,
                TryFindResource("PrimaryHueMidBrush") != null ? "PrimaryHueMidBrush" : "PrimaryBrush");
            label.Inlines.Add(new System.Windows.Documents.Run(template[..index]));
            label.Inlines.Add(name);
            label.Inlines.Add(new System.Windows.Documents.Run(template[(index + 3)..]));
            return label;
        }

        /// <summary>
        /// Shows a dialog to select which user to add the song to their favorites
        /// </summary>
        private async void ShowAddToFavoriteDialog(SongDisplayItem songItem)
        {
            try
            {
                // Get list of users, excluding internal User_Ids
                var users = GetUserListForFavorites();

                if (users.Count == 0)
                {
                    DebugLog("No users available for adding to favorites");
                    AppLogger.Log("No users available for adding to favorites");
                    return;
                }

                // Create dialog content
                // 固定寬度：只設 MinWidth 時對話框量內容不限寬，長歌名 (YouTube 標題) 不會換行而把視窗撐寬
                var dialogContent = new StackPanel { Margin = new Thickness(20), Width = 400 };

                dialogContent.Children.Add(new TextBlock
                {
                    Text = Loc.T("Song.AddFavorite"),
                    FontSize = (double)FindResource("FontSizeM"),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 15)
                });

                dialogContent.Children.Add(CreateFavoriteSongLabel(songItem.SongName));

                // YouTube 歌的歌手欄放的是長度/頻道，改顯示來源與長度
                string secondLine = songItem.IsYoutube
                    ? Loc.T("Fav.YoutubeSource", string.IsNullOrEmpty(songItem.Duration) ? Loc.T("Youtube.UnknownLength") : songItem.Duration)
                    : Loc.T("Fav.SingerLabel", songItem.SingerName);
                dialogContent.Children.Add(new TextBlock
                {
                    Text = secondLine,
                    FontSize = (double)FindResource("FontSizeS"),
                    Margin = new Thickness(0, 0, 0, 15),
                    TextWrapping = TextWrapping.Wrap
                });

                dialogContent.Children.Add(new TextBlock
                {
                    Text = Loc.T("Fav.SelectUser"),
                    FontSize = (double)FindResource("FontSizeM"),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                // Create a styled ListBox for user selection with borders
                var userListBox = new ListBox
                {
                    ItemsSource = users,
                    DisplayMemberPath = "UserName",
                    FontSize = (double)FindResource("FontSizeL"),
                    Height = 300,
                    Margin = new Thickness(0, 0, 0, 15),
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(5)
                };

                // Try to find theme brushes, fallback to solid colors if not found
                var borderBrush = TryFindResource("PrimaryHueMidBrush") as System.Windows.Media.Brush 
                    ?? (System.Windows.Media.Brush)FindResource("PrimaryBrush");
                var dividerBrush = TryFindResource("MaterialDesignDivider") as System.Windows.Media.Brush
                    ?? (System.Windows.Media.Brush)FindResource("BrushBorderDefault");

                userListBox.BorderBrush = borderBrush;

                // Create a custom ItemContainerStyle for better visibility
                var itemStyle = new Style(typeof(ListBoxItem));
                itemStyle.Setters.Add(new Setter(ListBoxItem.PaddingProperty, new Thickness(10, 8, 10, 8)));
                itemStyle.Setters.Add(new Setter(ListBoxItem.MarginProperty, new Thickness(2)));
                itemStyle.Setters.Add(new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(1)));
                itemStyle.Setters.Add(new Setter(ListBoxItem.BorderBrushProperty, dividerBrush));
                
                userListBox.ItemContainerStyle = itemStyle;

                dialogContent.Children.Add(userListBox);

                // Button panel
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var cancelButton = new Button
                {
                    Content = Loc.T("Common.Cancel"),
                    Width = 100,
                    Height = 40                    
                };
                ThemeSettingsHandler.ApplyOutlinedButtonStyle(cancelButton, 18);

                var confirmButton = new Button
                {
                    Content = Loc.T("Common.OK"),
                    Width = 100,
                    Height = 40,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                ThemeSettingsHandler.ApplyOutlinedButtonStyle(confirmButton, 18);

                cancelButton.Click += (s, args) => DialogHost.CloseDialogCommand.Execute(null, null);
                
                confirmButton.Click += (s, args) =>
                {
                    if (userListBox.SelectedItem is UserInfo selectedUser)
                    {
                        DialogHost.CloseDialogCommand.Execute(null, null);
                        if (songItem.IsYoutube)
                            AddYoutubeToUserFavorites(songItem, selectedUser.UserId, selectedUser.UserName);
                        else
                            AddSongToUserFavorites(songItem.SongId, selectedUser.UserId, selectedUser.UserName);
                    }
                    else
                    {
                        DebugLog("No user selected in add to favorites dialog");
                    }
                };
                buttonPanel.Children.Add(confirmButton);
                buttonPanel.Children.Add(cancelButton);                
                dialogContent.Children.Add(buttonPanel);

                await ShowDialogSafeAsync(new ContentControl { Content = dialogContent }, "RootDialog");
            }
            catch (Exception ex)
            {
                DebugLog($"Error showing add to favorite dialog: {ex.Message}");
                AppLogger.LogError("Failed to show add to favorite dialog", ex);
            }
            finally
            {
                ClearSongGridsSelection();
            }
        }

        /// <summary>
        /// Gets a list of users for favorites, excluding internal User_Ids
        /// </summary>
        private List<UserInfo> GetUserListForFavorites()
        {
            if (SongDatas.FavoriteUserData == null)
                return new List<UserInfo>();

            var users = new List<UserInfo>();

            foreach (System.Data.DataRow row in SongDatas.FavoriteUserData.Rows)
            {
                var userId = row["User_Id"]?.ToString() ?? "";
                var userName = row["User_Name"]?.ToString() ?? "";

                if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(userName))
                    continue;

                // Exclude internal User_Ids
                if (IsInternalUserId(userId))
                    continue;

                users.Add(new UserInfo { UserId = userId, UserName = userName });
            }

            return users.OrderBy(u => u.UserName).ToList();
        }

        /// <summary>
        /// Gets a list of user names for favorites (for use in UserOptionsWindow).
        /// Uses the same filtering logic as GetUserListForFavorites.
        /// </summary>
        public List<string> GetFavoriteUserNames()
        {
            return GetUserListForFavorites().Select(u => u.UserName).ToList();
        }

        /// <summary>
        /// Checks if a User_Id is internal (should not be shown to users)
        /// </summary>
        private bool IsInternalUserId(string userId)
        {
            // Exclude "****" (today's play)
            if (userId == "****")
                return true;

            // Exclude "####" (last play)
            if (userId == "####")
                return true;

            // Exclude timestamp format "MM/dd HH:mm"
            if (userId.Length == 11 && userId[2] == '/' && userId[5] == ' ' && userId[8] == ':')
                return true;

            return false;
        }

        /// <summary>
        /// Adds a song to a user's favorites
        /// </summary>
        private void AddSongToUserFavorites(string songId, string userId, string userName)
        {
            try
            {
                // Check if song already exists in user's favorites by querying the database directly
                bool alreadyExists = SongLibrary.IsFavorite(userId, songId);

                DebugLog($"AddSongToUserFavorites: Checking if Song_Id={songId} exists for User_Id={userId}, alreadyExists={alreadyExists}");

                if (alreadyExists)
                {
                    DebugLog($"Song {songId} already exists in user {userId} ({userName}) favorites");
                    AppLogger.Log($"Song {songId} already in favorites for user {userName}");
                    
                    // Show feedback to user
                    MarqueeAPI.ShowText(Loc.T("Fav.AlreadyIn", userName), MarqueeDisplayDevice.ConsoleScreen);
                    return;
                }

                // Insert the new favorite
                bool added = SongLibrary.AddFavorite(userId, songId);

                DebugLog($"AddSongToUserFavorites: added={added}");

                if (added)
                {
                    DebugLog($"Added song {songId} to user {userId} ({userName}) favorites");
                    AppLogger.Log($"Successfully added song {songId} to {userName}'s favorites");
                    
                    // Reload favorite data to refresh in-memory cache
                    SongDatas.ReloadFavoriteData();
                    
                    // Show success feedback to user
                    MarqueeAPI.ShowText(Loc.T("Fav.Added", userName), MarqueeDisplayDevice.ConsoleScreen);
                }
                else
                {
                    DebugLog($"Failed to add song {songId} to user {userId} favorites - no rows affected");
                    AppLogger.LogError($"Failed to add song {songId} to {userName}'s favorites", null);
                    
                    // Show failure feedback to user
                    MarqueeAPI.ShowText(Loc.T("Fav.AddFailed"), MarqueeDisplayDevice.ConsoleScreen);
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Error adding song to favorites: {ex.Message}");
                AppLogger.LogError($"Failed to add song to favorites: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// YouTube 歌加入使用者的最愛：以影片 ID 記錄 (點唱紀錄的編號會被清除，不能用)，歌名與長度一起存。
        /// </summary>
        private void AddYoutubeToUserFavorites(SongDisplayItem song, string userId, string userName)
        {
            try
            {
                string videoId = YoutubeVideoId.Resolve(song.YoutubeId, song.SongId, song.FilePath);
                if (!YoutubeVideoId.IsValid(videoId))
                {
                    AppLogger.Log($"YouTube favorite: no video ID for '{song.SongName}' (SongId={song.SongId}), skipped.");
                    MarqueeAPI.ShowText(Loc.T("Fav.AddFailed"), MarqueeDisplayDevice.ConsoleScreen);
                    return;
                }

                if (SongLibrary.IsYoutubeFavorite(userId, videoId))
                {
                    AppLogger.Log($"YouTube {videoId} already in favorites for user {userName}");
                    MarqueeAPI.ShowText(Loc.T("Fav.AlreadyIn", userName), MarqueeDisplayDevice.ConsoleScreen);
                    return;
                }

                if (SongLibrary.AddYoutubeFavorite(userId, videoId, song.SongName, SongDisplayItem.ParseDurationSeconds(song.Duration)))
                {
                    AppLogger.Log($"Successfully added YouTube {videoId} to {userName}'s favorites");
                    SongDatas.ReloadFavoriteData();
                    MarqueeAPI.ShowText(Loc.T("Fav.Added", userName), MarqueeDisplayDevice.ConsoleScreen);
                }
                else
                {
                    AppLogger.LogError($"Failed to add YouTube {videoId} to {userName}'s favorites", null);
                    MarqueeAPI.ShowText(Loc.T("Fav.AddFailed"), MarqueeDisplayDevice.ConsoleScreen);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to add YouTube song to favorites: {ex.Message}", ex);
                MarqueeAPI.ShowText(Loc.T("Fav.AddFailed"), MarqueeDisplayDevice.ConsoleScreen);
            }
        }

        /// <summary>
        /// Helper class to hold user information
        /// </summary>
        private class UserInfo
        {
            public string UserId { get; set; } = "";
            public string UserName { get; set; } = "";
        }
    }
}
