using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UltimateKtv;

namespace StyleSimulator
{
    /// <summary>
    /// 模擬畫面的動態效果 (themesettings.json 的 VisualEffects)，與主程式共用 AmbientBackground / MicroFx：
    /// 背景光暈與半透明面板隨開關即時切換；在模擬畫面上點主選單換色、按暫停/切歌、點歌或插播可看到對應動畫。
    /// </summary>
    public partial class MainWindow
    {
        private const int DemoSongRows = 13;
        private const int DemoWaitRows = 8;

        private bool? _surfacesTranslucent;
        private (Color Accent, Color Light)? _ambientColors;
        private bool _visualFxInteractionsInitialized;
        private string _demoNavKey = "New";
        private bool _demoPaused;
        private bool _demoWaitLoaded;
        private readonly List<(string Song, string Singer)> _demoWait = new();

        private bool VisualEffectsOn => ChkVisualEffects?.IsChecked == true;

        /// <summary>
        /// 依開關切換半透明表面筆刷與背景圖層。列表風格會用 FindResource 取表面筆刷，
        /// 所以要在 UpdateLivePreviews 套用列表風格之前呼叫。
        /// </summary>
        private void ApplyVisualEffectsPreview(Color accent, Color light)
        {
            InitVisualFxInteractions();
            bool on = VisualEffectsOn;

            // 主程式在啟動時把這些筆刷換成半透明；模擬器用視窗資源蓋過 (關閉時移除，回到 Themes/Colors.xaml 原值)
            if (_surfacesTranslucent != on)
            {
                _surfacesTranslucent = on;
                var translucent = AmbientBackground.TranslucentSurfaces(key => Application.Current.TryFindResource(key) as Brush);
                foreach (var kv in translucent)
                {
                    if (on) Resources[kv.Key] = kv.Value;
                    else Resources.Remove(kv.Key);
                }
                BorderSongList.Background = (Brush)FindResource("BrushBgCard");
                BorderWaitList.Background = (Brush)FindResource("BrushBgCard");
            }

            if (!on)
            {
                if (_ambientColors != null) AmbientLayer.Stop();
                _ambientColors = null;
            }
            else if (_ambientColors != (accent, light))
            {
                AmbientLayer.Stop();
                AmbientLayer.Start(accent, light);
                AmbientLayer.SetCategory(_demoNavKey);
                AmbientLayer.SetPlayback(_demoPaused ? AmbientPlayback.Paused : AmbientPlayback.Playing);
                _ambientColors = (accent, light);
            }
        }

        private void InitVisualFxInteractions()
        {
            if (_visualFxInteractionsInitialized) return;
            _visualFxInteractionsInitialized = true;

            // 主選單：拖曳排序時不會觸發 Click，只有真的點擊才換色
            NavMenuPanel.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
            {
                if (e.OriginalSource is not Button { Tag: string key }) return;
                _demoNavKey = key;
                if (VisualEffectsOn) AmbientLayer.SetCategory(key);
            }));

            BtnPause.Click += (_, _) =>
            {
                _demoPaused = !_demoPaused;
                BtnPause.Content = _demoPaused ? "繼續" : "暫停";
                if (VisualEffectsOn) AmbientLayer.SetPlayback(_demoPaused ? AmbientPlayback.Paused : AmbientPlayback.Playing);
            };
            BtnSkipSong.Click += (_, _) =>
            {
                if (!VisualEffectsOn) return;
                if (_demoPaused) BtnPause.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                AmbientLayer.Sweep();
            };


            var rows = SongRowBorders();
            for (int i = 0; i < rows.Count; i++)
            {
                int rowIndex = i;
                // 點列 = 點歌 (按鈕會自己處理滑鼠，點按鈕不會走到這裡)
                rows[i].MouseLeftButtonDown += (_, e) => DemoOrderSong(rowIndex, e.GetPosition(MicroFxLayer), insertFirst: false);
                rows[i].AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler((_, e) =>
                {
                    if ((e.OriginalSource as Button)?.Content as string == "插播")
                    {
                        DemoOrderSong(rowIndex, Mouse.GetPosition(MicroFxLayer), insertFirst: true);
                    }
                }));
            }
        }

        /// <summary>
        /// 點歌示範：歌名飛到模擬待播清單 (插播飛到第一位、其他列往下滑)。
        /// 與主程式相同：飛行期間清單維持點歌前的樣子，抵達時新歌才出現；清單不會清空，
        /// 超過一頁 (8 首) 時飛到清單底部，只更新頁數。
        /// </summary>
        private void DemoOrderSong(int songRow, Point from, bool insertFirst)
        {
            if (!VisualEffectsOn) return;

            var songText = (FindName($"TxtSongRow{songRow + 1}") as TextBlock)?.Text ?? "";
            var singerText = (FindName($"TxtSingerRow{songRow + 1}") as TextBlock)?.Text ?? "";
            if (string.IsNullOrEmpty(songText)) return;

            if (!_demoWaitLoaded)
            {
                _demoWaitLoaded = true;
                _demoWait.AddRange(Enumerable.Range(1, DemoWaitRows)
                    .Select(i => ((FindName($"TxtWaitSong{i}") as TextBlock)?.Text ?? "", (FindName($"TxtWaitSinger{i}") as TextBlock)?.Text ?? ""))
                    .Where(w => !string.IsNullOrEmpty(w.Item1)));
            }

            int index = insertFirst ? 0 : _demoWait.Count;
            _demoWait.Insert(index, (songText, singerText));
            for (int i = 0; i < DemoWaitRows; i++)
            {
                if (FindName($"TxtWaitSong{i + 1}") is TextBlock song) song.Text = i < _demoWait.Count ? _demoWait[i].Song : "";
                if (FindName($"TxtWaitSinger{i + 1}") is TextBlock singer) singer.Text = i < _demoWait.Count ? _demoWait[i].Singer : "";
            }
            // 「已點 N 首」與主程式一樣等歌名抵達才更新
            int pages = (_demoWait.Count + DemoWaitRows - 1) / DemoWaitRows;
            string pageInfo = $"已點 {_demoWait.Count} 首 · 1/{pages} 頁";
            var pageInfoTimer = new DispatcherTimer { Interval = MicroFx.FlyDuration };
            pageInfoTimer.Tick += (s, _) =>
            {
                ((DispatcherTimer)s!).Stop();
                TxtWaitPageInfo.Text = pageInfo;
            };
            pageInfoTimer.Start();

            var waitRows = WaitRowBorders();
            var target = MicroFx.Bounds(waitRows[System.Math.Min(index, DemoWaitRows - 1)], MicroFxLayer);
            MicroFx.FlyTo(MicroFxLayer, songText, from, target);
            if (insertFirst) MicroFx.SlideDownAfterFly(waitRows.Take(_demoWait.Count).ToList<UIElement>(), target.Height);
            else if (index < DemoWaitRows) MicroFx.RevealAfterFly(waitRows[index]);
        }

        private List<Border> SongRowBorders() =>
            Enumerable.Range(1, DemoSongRows).Select(i => FindName($"BorderSongRow{i}")).OfType<Border>().ToList();

        private List<Border> WaitRowBorders() =>
            Enumerable.Range(1, DemoWaitRows).Select(i => FindName($"BorderWaitRow{i}")).OfType<Border>().ToList();
    }
}
