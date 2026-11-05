using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using UltimateKtv.Playback;

namespace UltimateKtv
{
    /// <summary>
    /// 微互動 (themesettings.json 的 VisualEffects) 與主畫面的連接：
    /// 點歌成功後找出歌名要飛去的位置 (待播清單中那一列 / 清單底部 / 正在播放)。動畫本身在 Themes/MicroFx.cs。
    /// 飛行標籤與閃光畫在最上層的 MicroFxLayer (不接收滑鼠/觸控)。
    /// </summary>
    public partial class MainWindow
    {
        // 按下後多久內點歌成功仍算「從按下的位置飛出」(點歌前會先非同步檢查檔案)
        private static readonly TimeSpan PressOriginWindow = TimeSpan.FromSeconds(3);

        private Point? _lastPressPoint;
        private DateTime _lastPressTime;
        // 點歌飛行期間暫停更新「已點 N 首」：計時器在跑時，新的文字先存在 _heldPageInfo，抵達時才顯示
        private DispatcherTimer? _pageInfoHold;
        private string _heldPageInfo = "";
        // 在主控台點的 YouTube 影片 (影片 ID → 點的位置)：要等下載完才點歌，那時才用這個位置當飛出點
        private readonly Dictionary<string, Point> _youtubeOrderOrigins = new();

        private static bool MicroFxEnabled => ThemeSettingsHandler.Settings.VisualEffects;

        /// <summary>記下最後按下的位置 (觸控會轉成滑鼠事件)，作為點歌飛行標籤的起點。</summary>
        private void InitializeMicroFx()
        {
            AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
            {
                _lastPressPoint = e.GetPosition(MicroFxLayer);
                _lastPressTime = DateTime.Now;
            }), handledEventsToo: true);
        }

        /// <summary>剛點的位置；超過 PressOriginWindow 視為不是這次點歌按的。</summary>
        private Point? RecentPressPoint() =>
            _lastPressPoint is Point p && DateTime.Now - _lastPressTime < PressOriginWindow ? p : null;

        /// <summary>主控台點 YouTube 影片時記下點的位置，下載完點歌時歌名從這裡飛出 (與一般點歌相同)。</summary>
        private void RememberYoutubeOrderOrigin(string videoId)
        {
            if (videoId.Length > 0 && RecentPressPoint() is Point p) _youtubeOrderOrigins[videoId] = p;
        }

        /// <summary>取出並移除這部影片記下的位置；手機點的影片沒有記錄，回傳 null (從畫面右側飛入)。</summary>
        private Point? TakeYoutubeOrderOrigin(string videoId)
        {
            if (!_youtubeOrderOrigins.Remove(videoId, out var p)) return null;
            return p;
        }

        /// <summary>更新待播清單下方的「已點 N 首 · 頁數」；點歌動畫飛行期間先保留，抵達時才顯示。</summary>
        private void SetWaitListPageInfo(string text)
        {
            if (_pageInfoHold != null)
            {
                _heldPageInfo = text;
                return;
            }
            WaitListPageInfo.Text = text;
        }

        /// <summary>點歌後先顯示點歌前的文字，歌名抵達時換成最新的 (連續點歌時以最後一次抵達為準)。</summary>
        private void HoldWaitListPageInfo(string previous)
        {
            if (_pageInfoHold == null) _heldPageInfo = WaitListPageInfo.Text;
            else _pageInfoHold.Stop();

            WaitListPageInfo.Text = previous;
            _pageInfoHold = new DispatcherTimer { Interval = MicroFx.FlyDuration };
            _pageInfoHold.Tick += (s, _) =>
            {
                ((DispatcherTimer)s!).Stop();
                _pageInfoHold = null;
                WaitListPageInfo.Text = _heldPageInfo;
            };
            _pageInfoHold.Start();
        }

        /// <summary>
        /// 點歌成功後：歌名從按下的位置 (主控台點的 YouTube 用點影片時的位置；手機點歌從畫面右側) 飛到待播清單中這首歌的位置，
        /// 飛行期間清單維持點歌前的樣子，抵達時新歌出現並亮一下；插播時其他列同時往下滑一格。
        /// </summary>
        private void AnimateSongOrdered(QueuedSong song, OrderSource source, bool toFirst, string pageInfoBefore, Point? origin)
        {
            if (!MicroFxEnabled || source == OrderSource.Random || MicroFxLayer.ActualWidth <= 0) return;

            try
            {
                var queue = _playback.Snapshot;
                int index = toFirst ? 0 : queue.ToList().FindIndex(q => q.Id == song.Id);
                if (index >= queue.Count) index = -1;

                // 目標：待播清單目前頁上的那一列；不在目前頁就飛到清單底部；已直接開播就飛到「正在播放」
                DataGridRow? targetRow = null;
                Rect target;
                if (index < 0)
                {
                    target = MicroFx.Bounds(NowPlayingBorder, MicroFxLayer);
                }
                else if (index / WaitingListPageSize + 1 == _currentWaitingListPage
                    && WaitingListGrid.ItemContainerGenerator.ContainerFromIndex(index % WaitingListPageSize) is DataGridRow row)
                {
                    targetRow = row;
                    target = MicroFx.Bounds(row, MicroFxLayer);
                }
                else
                {
                    var grid = MicroFx.Bounds(WaitingListGrid, MicroFxLayer);
                    target = new Rect(grid.Left, grid.Bottom - 46, grid.Width, 46);
                }

                // 飛出點：指定的位置 (主控台點的 YouTube) > 一般點歌剛按下的位置 > 畫面右側 (手機點歌)
                Point from = origin
                    ?? (source == OrderSource.User ? RecentPressPoint() : null)
                    ?? new Point(MicroFxLayer.ActualWidth + 40, target.Top + target.Height / 2);

                MicroFx.FlyTo(MicroFxLayer, song.SongName, from, target);
                HoldWaitListPageInfo(pageInfoBefore);

                if (targetRow == null) return;
                if (toFirst)
                {
                    var rows = Enumerable.Range(0, WaitingListPageSize)
                        .Select(i => WaitingListGrid.ItemContainerGenerator.ContainerFromIndex(i))
                        .OfType<UIElement>()
                        .ToList();
                    MicroFx.SlideDownAfterFly(rows, target.Height);
                }
                else
                {
                    MicroFx.RevealAfterFly(targetRow);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Micro interaction animation failed", ex);
            }
        }
    }
}
