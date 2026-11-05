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
        // 待播清單畫面最後一次顯示的內容
        private WaitListView _waitListView = new(Array.Empty<Guid>(), 1, "");
        // 每首新加入 (或插播移到第一首) 的歌在那次變動前的清單畫面 = 這首歌的「點歌前」。
        // 在清單實際變動時記下：點歌前要先非同步檢查檔案，期間別處點的歌可能先加入。只留最近幾筆
        private readonly Dictionary<Guid, (WaitListView View, DateTime At)> _viewBeforeSong = new();
        private readonly Queue<Guid> _viewBeforeOrder = new();
        private const int MaxViewBeforeEntries = 32;
        // 記下後多久內點歌動畫還會用它 (點歌前檢查檔案的時間；更舊的多半是刪歌等其他變動留下的)
        private static readonly TimeSpan ViewBeforeLifetime = TimeSpan.FromSeconds(10);
        // 還在飛的點歌標籤 (依抵達時間排序)；「已點 N 首」顯示最早一筆的點歌前數字，每抵達一首更新一次
        private readonly List<OrderFlight> _flights = new();
        private DispatcherTimer? _flightTimer;
        private string _latestPageInfo = "";
        // 清單剛加入新歌、點歌動畫還沒登記飛行前 (同一輪 UI 工作稍後) 先顯示加入前的數字，避免新數字閃一下
        private string? _pageInfoBeforeAdded;
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

        /// <summary>更新待播清單下方的「已點 N 首 · 頁數」；點歌標籤飛行期間顯示點歌前的數字，抵達時才更新。</summary>
        private void SetWaitListPageInfo(string text)
        {
            _latestPageInfo = text;
            ShowFlightPageInfo();
        }

        private void ShowFlightPageInfo() =>
            WaitListPageInfo.Text = _flights.Count > 0 ? _flights[0].PageInfoBefore : _pageInfoBeforeAdded ?? _latestPageInfo;

        /// <summary>
        /// 待播清單畫面顯示的內容：清單中歌的 Id (依順序)、目前頁數、「已點 N 首 · 頁數」。
        /// 點歌動畫用它還原點歌前的樣子，也用它判斷飛行期間清單是否又變了。
        /// </summary>
        private sealed record WaitListView(Guid[] Ids, int Page, string PageInfo)
        {
            public bool SameItems(WaitListView other) => Page == other.Page && Ids.AsSpan().SequenceEqual(other.Ids);

            /// <summary>目前頁第 row 列的歌；空列為 Guid.Empty。</summary>
            public Guid IdAtRow(int row, int pageSize)
            {
                int i = (Page - 1) * pageSize + row;
                return i >= 0 && i < Ids.Length ? Ids[i] : Guid.Empty;
            }
        }

        /// <summary>
        /// 一次點歌的飛行。Rows 是這次動畫隱藏/位移的列與當時該列的歌：飛行期間那些列換成別首歌
        /// (插播、切歌、刪歌、換頁)，效果就對不上，要提早結束。
        /// </summary>
        private sealed record OrderFlight(MicroFx.Flight Fx, DateTime ArrivesAt, string PageInfoBefore, (int Row, Guid Id)[] Rows);

        /// <summary>UpdateWaitingListDisplay 更新畫面後呼叫。</summary>
        private void OnWaitListShown(WaitListView view)
        {
            var previous = _waitListView;
            _waitListView = view;
            if (view.SameItems(previous)) return;

            if (!view.Ids.AsSpan().SequenceEqual(previous.Ids) && RememberViewBefore(previous, view) && MicroFxEnabled)
            {
                // 點歌動畫在同一輪 UI 工作稍後才登記 (OrderSongAsync)；之前先維持加入前的數字。
                // 沒有動畫的加入 (隨機播放等) 在背景優先權時恢復顯示最新數字
                _pageInfoBeforeAdded ??= previous.PageInfo;
                ShowFlightPageInfo();
                Dispatcher.BeginInvoke(() =>
                {
                    _pageInfoBeforeAdded = null;
                    ShowFlightPageInfo();
                }, DispatcherPriority.Background);
            }
            EndFlightsNotMatching(view);
        }

        /// <summary>記下這次變動新加入的歌 (與插播移到第一首的歌) 的「點歌前」畫面；回傳是否有新加入的歌。</summary>
        private bool RememberViewBefore(WaitListView previous, WaitListView view)
        {
            bool added = false;
            for (int i = 0; i < view.Ids.Length; i++)
            {
                var id = view.Ids[i];
                int before = Array.IndexOf(previous.Ids, id);
                if (before >= 0 && !(i == 0 && before > 0)) continue;
                added |= before < 0;

                if (!_viewBeforeSong.ContainsKey(id)) _viewBeforeOrder.Enqueue(id);
                _viewBeforeSong[id] = (previous, DateTime.Now);
            }
            while (_viewBeforeOrder.Count > MaxViewBeforeEntries) _viewBeforeSong.Remove(_viewBeforeOrder.Dequeue());
            return added;
        }

        /// <summary>
        /// 點歌動畫碰到的列換成別首歌時：提早結束那一筆 (列恢復原狀、標籤與閃光淡出)。
        /// 其他仍對得上的飛行繼續，它們用到的列不動。
        /// </summary>
        private void EndFlightsNotMatching(WaitListView view)
        {
            var ended = _flights.FindAll(f => !Array.TrueForAll(f.Rows, r => view.IdAtRow(r.Row, WaitingListPageSize) == r.Id));
            if (ended.Count == 0) return;

            _flights.RemoveAll(ended.Contains);
            var stillUsed = _flights.SelectMany(f => f.Rows).Select(r => r.Row).ToHashSet();
            foreach (var flight in ended)
            {
                foreach (var (row, _) in flight.Rows)
                {
                    if (!stillUsed.Contains(row) && WaitingListGrid.ItemContainerGenerator.ContainerFromIndex(row) is UIElement container)
                    {
                        MicroFx.StopRowEffects(container);
                    }
                }
                flight.Fx.Cancel();
            }
            ShowFlightPageInfo();
        }

        /// <summary>待播清單目前頁的前 count 列 (固定的列容器，換頁只換內容)。</summary>
        private List<UIElement> WaitingListRows(int count) =>
            Enumerable.Range(0, count)
                .Select(i => WaitingListGrid.ItemContainerGenerator.ContainerFromIndex(i))
                .OfType<UIElement>()
                .ToList();

        private void AddFlight(OrderFlight flight)
        {
            _flights.Add(flight);
            _flights.Sort((a, b) => a.ArrivesAt.CompareTo(b.ArrivesAt));
            ShowFlightPageInfo();
            ScheduleNextArrival();
        }

        /// <summary>在最早一筆抵達時更新「已點 N 首」，再排下一筆。</summary>
        private void ScheduleNextArrival()
        {
            _flightTimer?.Stop();
            if (_flights.Count == 0) return;

            var wait = _flights[0].ArrivesAt - DateTime.Now;
            _flightTimer = new DispatcherTimer { Interval = wait > TimeSpan.Zero ? wait : TimeSpan.Zero };
            _flightTimer.Tick += (s, _) =>
            {
                ((DispatcherTimer)s!).Stop();
                var now = DateTime.Now;
                _flights.RemoveAll(f => f.ArrivesAt <= now);
                ShowFlightPageInfo();
                ScheduleNextArrival();
            };
            _flightTimer.Start();
        }

        /// <summary>
        /// 點歌成功後：歌名從按下的位置 (主控台點的 YouTube 用點影片時的位置；手機點歌從畫面右側) 飛到待播清單中這首歌的位置，
        /// 飛行期間清單維持點歌前的樣子，抵達時新歌出現並亮一下；插播時被擠下去的列同時往下滑一格。
        /// 飛行期間碰到的列換成別首歌就提早結束 (OnWaitListShown)。
        /// </summary>
        /// <param name="fallbackBefore">點歌前的畫面 (呼叫端在檢查檔案前記下)；清單變動時沒記到這首歌時才用。</param>
        private void AnimateSongOrdered(QueuedSong song, OrderSource source, bool toFirst, WaitListView fallbackBefore, Point? origin)
        {
            if (!MicroFxEnabled || source == OrderSource.Random || MicroFxLayer.ActualWidth <= 0) return;

            try
            {
                var waitingSongs = _playback.Snapshot;
                // 插播已在清單中的歌時，第 1 首是原本那筆 (Id 不是這次新建的 song)
                int index = toFirst
                    ? (waitingSongs.Count > 0 ? 0 : -1)
                    : waitingSongs.ToList().FindIndex(q => q.Id == song.Id);
                Guid songId = index >= 0 ? waitingSongs[index].Id : song.Id;
                var before = _viewBeforeSong.Remove(songId, out var remembered) && DateTime.Now - remembered.At < ViewBeforeLifetime
                    ? remembered.View
                    : fallbackBefore;

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
                    double rowHeight = WaitingListGrid.RowHeight;
                    target = new Rect(grid.Left, grid.Bottom - rowHeight, grid.Width, rowHeight);
                }

                // 飛出點：指定的位置 (主控台點的 YouTube) > 一般點歌剛按下的位置 > 畫面右側 (手機點歌)
                Point from = origin
                    ?? (source == OrderSource.User ? RecentPressPoint() : null)
                    ?? new Point(MicroFxLayer.ActualWidth + 40, target.Top + target.Height / 2);

                var fx = MicroFx.FlyTo(MicroFxLayer, song.SongName, from, target);

                // 這次動畫要隱藏/位移的列
                int affectedRows = 0;
                if (targetRow != null)
                {
                    if (!toFirst)
                    {
                        affectedRows = -1; // 只有新歌那一列
                    }
                    else
                    {
                        // 這首歌原本在清單中的位置 (新點的歌為 -1)：只有第 1 列到原位置之間的歌往後移一格，之後的列沒動；
                        // 目前頁沒有歌的空列不算 (之後在清單尾端加歌不會被當成這次動畫對不上)
                        int oldIndex = Array.IndexOf(before.Ids, songId);
                        int filledRows = Math.Min(waitingSongs.Count, WaitingListPageSize);
                        affectedRows = oldIndex == 0 ? 0 // 本來就是第 1 首，清單沒變
                            : Math.Min(oldIndex < 0 ? WaitingListPageSize : oldIndex + 1, filledRows);
                    }
                }

                (int Row, Guid Id)[] rows = affectedRows switch
                {
                    -1 => new[] { (index % WaitingListPageSize, songId) },
                    > 0 => Enumerable.Range(0, affectedRows).Select(r => (r, _waitListView.IdAtRow(r, WaitingListPageSize))).ToArray(),
                    _ => Array.Empty<(int, Guid)>()
                };
                AddFlight(new OrderFlight(fx, DateTime.Now + MicroFx.FlyDuration, before.PageInfo, rows));

                if (affectedRows == -1) MicroFx.RevealAfterFly(targetRow!);
                else if (affectedRows > 0) MicroFx.SlideDownAfterFly(WaitingListRows(affectedRows), target.Height);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Micro interaction animation failed", ex);
            }
        }
    }
}
