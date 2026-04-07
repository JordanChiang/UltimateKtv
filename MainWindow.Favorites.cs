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
        /// Handles right click on song grid to show add to favorites dialog
        /// </summary>
        private void SongGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is DataGrid grid)
            {
                var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
                if (row != null && row.Item is SongDisplayItem songItem)
                {
                    if (songItem.IsYoutube)
                    {
                        DebugLog("Ignoring right-click add to favorites for YouTube song.");
                        return;
                    }
                    grid.SelectedItem = songItem;
                    ShowAddToFavoriteDialog(songItem);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Context menu handler for adding to favorites
        /// </summary>
        private void AddToFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (SongListGrid.SelectedItem is SongDisplayItem songItem)
            {
                if (songItem.IsYoutube)
                {
                    MessageBox.Show("YouTube 歌曲無法加入最愛。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                AppLogger.Log($"User action: Add to favorite clicked for song '{songItem.SongName}'");
                ShowAddToFavoriteDialog(songItem);
            }
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
                var dialogContent = new StackPanel { Margin = new Thickness(20), MinWidth = 400 };

                dialogContent.Children.Add(new TextBlock
                {
                    Text = "加入最愛",
                    FontSize = 24,
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 0, 0, 15)
                });

                dialogContent.Children.Add(new TextBlock
                {
                    Text = $"歌曲: {songItem.SongName}",
                    FontSize = 18,
                    Margin = new Thickness(0, 0, 0, 5),
                    TextWrapping = TextWrapping.Wrap
                });

                dialogContent.Children.Add(new TextBlock
                {
                    Text = $"歌手: {songItem.SingerName}",
                    FontSize = 18,
                    Margin = new Thickness(0, 0, 0, 15),
                    TextWrapping = TextWrapping.Wrap
                });

                dialogContent.Children.Add(new TextBlock
                {
                    Text = "選擇使用者:",
                    FontSize = 22,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 10)
                });

                // Create a styled ListBox for user selection with borders
                var userListBox = new ListBox
                {
                    ItemsSource = users,
                    DisplayMemberPath = "UserName",
                    FontSize = 28,
                    Height = 300,
                    Margin = new Thickness(0, 0, 0, 15),
                    BorderThickness = new Thickness(2),
                    Padding = new Thickness(5)
                };

                // Try to find theme brushes, fallback to solid colors if not found
                var borderBrush = TryFindResource("PrimaryHueMidBrush") as System.Windows.Media.Brush 
                    ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243));
                var dividerBrush = TryFindResource("MaterialDesignDivider") as System.Windows.Media.Brush
                    ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 224, 224));

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
                    Content = "取消",
                    Width = 100,
                    Height = 40                    
                };
                TextSettingsHandler.ApplyOutlinedButtonStyle(cancelButton, 18);

                var confirmButton = new Button
                {
                    Content = "確定",
                    Width = 100,
                    Height = 40,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                TextSettingsHandler.ApplyOutlinedButtonStyle(confirmButton, 18);

                cancelButton.Click += (s, args) => DialogHost.CloseDialogCommand.Execute(null, null);
                
                confirmButton.Click += (s, args) =>
                {
                    if (userListBox.SelectedItem is UserInfo selectedUser)
                    {
                        DialogHost.CloseDialogCommand.Execute(null, null);
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
                string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrazySong.mdb");

                // Check if song already exists in user's favorites by querying the database directly
                string checkQuery = $"SELECT COUNT(*) FROM ktv_Favorite WHERE User_Id = '{userId}' AND Song_Id = '{songId}'";
                var result = DbHelper.Access.GetDataTable(dbPath, checkQuery, null);
                bool alreadyExists = result.Rows.Count > 0 && Convert.ToInt32(result.Rows[0][0]) > 0;

                DebugLog($"AddSongToUserFavorites: Checking if Song_Id={songId} exists for User_Id={userId}, alreadyExists={alreadyExists}");

                if (alreadyExists)
                {
                    DebugLog($"Song {songId} already exists in user {userId} ({userName}) favorites");
                    AppLogger.Log($"Song {songId} already in favorites for user {userName}");
                    
                    // Show feedback to user
                    MarqueeAPI.ShowText($"歌曲已在 {userName} 的最愛中", MarqueeDisplayDevice.ConsoleScreen);
                    return;
                }

                // Insert the new favorite
                string insertQuery = $"INSERT INTO ktv_Favorite (User_Id, Song_Id) VALUES ('{userId}', '{songId}')";
                int rowsAffected = DbHelper.Access.ExecuteNonQuery(dbPath, insertQuery, null);

                DebugLog($"AddSongToUserFavorites: INSERT executed, rowsAffected={rowsAffected}");

                if (rowsAffected > 0)
                {
                    DebugLog($"Added song {songId} to user {userId} ({userName}) favorites");
                    AppLogger.Log($"Successfully added song {songId} to {userName}'s favorites");
                    
                    // Reload favorite data to refresh in-memory cache
                    SongDatas.ReloadFavoriteData(dbPath);
                    
                    // Show success feedback to user
                    MarqueeAPI.ShowText($"已加入 {userName} 的最愛", MarqueeDisplayDevice.ConsoleScreen);
                }
                else
                {
                    DebugLog($"Failed to add song {songId} to user {userId} favorites - no rows affected");
                    AppLogger.LogError($"Failed to add song {songId} to {userName}'s favorites", null);
                    
                    // Show failure feedback to user
                    MarqueeAPI.ShowText("加入最愛失敗", MarqueeDisplayDevice.ConsoleScreen);
                }
            }
            catch (Exception ex)
            {
                DebugLog($"Error adding song to favorites: {ex.Message}");
                AppLogger.LogError($"Failed to add song to favorites: {ex.Message}", ex);
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
