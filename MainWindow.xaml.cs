using CrazyKTV_MediaKit.DirectShow.Controls;
using System.ComponentModel;
using System.Diagnostics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Windows.Media;
using System.Threading;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MaterialDesignThemes.Wpf;
using UltimateKtv.Enums;

namespace UltimateKtv
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Master list of all singers. In a real app, this would come from a database.
        private List<string> _allSingers = new List<string>();
        private int _currentPage = 1;
        private int PageSize { get; set; } = 48; // Default 12 rows * 4 columns, changes if visual
        private bool IsVisualSingerStyleEffective { get; set; } = false;
        private int _totalPages;

        // Dictionary to store page state for different filter types
        private Dictionary<string, int> _filterPageStates = new Dictionary<string, int>();
        private string _currentFilterKey = "ShowAll";

        // Current selected singer for song display
        private string _selectedSinger = string.Empty;

        // Song pagination variables
        private List<SongDisplayItem> _allSongs = new List<SongDisplayItem>();
        private int _currentSongPage = 1;
        private int _totalSongPages = 1;
        private bool _isLanguageMode = false;
        private string _currentSongsTitle = string.Empty;

        // Current playing song file path and properties for database lookup
        private string PlayingFilePath = string.Empty;
        private int PlayingFileVolume = 30;
        private int PlayingFileMusicTrack = 0;
        // Store the full song data dictionary for the currently playing song
        // This is needed for properties like ReplayGain
        private Dictionary<string, object?>? _playingSongData = null;
        private const int SongPageSize = 13; // Adjust based on DataGrid display capacity (RowHeight 62，SongListGrid Height 937)
        private const int LanguageSongPageSize = 11; // 語系結果列表 11 筆 (-1 for card style height)
        private int CurrentSongPageSize => _isLanguageMode ? LanguageSongPageSize : SongPageSize;

        // Fields for theme switcher functionality
        private List<string> _themeKeys = new();
        private int _currentThemeIndex = 0;

        // The 'mediaUriElement' field is initialized by the framework.
        // We use '= null!' to suppress the compiler warning (CS8618) for this pattern.
        private MediaUriElement mediaUriElement => MediaHostManager.Instance.Element;

        // Waiting list management

        private bool _isPlayingFromWaitingList = false;
        // Stores the item clicked for context menu actions (we avoid relying on DataGrid.SelectedItem)
        private WaitingListItem? _waitingListContextItem;

        // Waiting list pagination
        private const int WaitingListPageSize = 8; // Maximum 8 rows per page (WaitingListGrid Height 466、RowHeight 46 對應 8 列 + 欄位標題)

        // Pre-loading cache for network access optimization
        private string? _preLoadedSongPath = null;
        private byte[]? _preLoadedFileContent = null; // Entire file content cached in memory
        private long _preLoadedMemoryUsage = 0; // Track memory usage in bytes
        private int _preLoadingCacheCount = 0; // Track number of times file is cached

        // Media control flags
        private bool _isUpdatingProgressSlider = false;
        // Lock to prevent race conditions when transitioning between songs (e.g., Skip vs. MediaEnded)
        private bool _isTransitioningSong = false;
        // Flag to ensure play count is incremented only once per song playback
        private bool _playCountIncrementedForCurrentSong = false;
        // Flag to track if this is the first song played in the session
        private bool _isFirstSongInSession = true;

        // Long press detection for adding to favorites
        private System.Windows.Threading.DispatcherTimer? _longPressTimer;
        private SongDisplayItem? _longPressSongItem;

        private bool _showPreviewInMainWindow = true;
        private CancellationTokenSource? _mediaLoadCts;

        // Mute state management
        private bool _isMuted = false;
        private double _volumeBeforeMute;
        
        // Volume lock state management
        private int _lockedVolume = 60; // Store the locked volume value

        // Video display window for secondary monitor
        private VideoDisplayWindow? _videoDisplayWindow = null;

        // HTTP server for remote control
        private UltimateKtv.HttpServer? _httpServer = null;
        private string _lastKnownIp = string.Empty;
        // Guards the hand-off of _httpServer between the background startup task and MainWindow_Closing
        private readonly object _httpServerLock = new object();
        private bool _isClosing = false;
        // Startup marquee shows {4}=LAN IP:Port, so it waits until the HTTP server has picked its actual port (UI thread only)
        private bool _httpServerStartupDone = false;
        private bool _pendingStartupMarquee = false;

        // Flag to track if web host info marquee is displayed (for single-monitor mode)
        private bool _isWebHostMarqueeDisplayed = false;

        // Timer for cursor auto-hide in single monitor mode
        private DispatcherTimer? _cursorIdleTimer;


        public bool IsSingleMonitorMode { get; private set; } = false;

        // Global dialog semaphore to serialize dialog openings across the window
        private readonly SemaphoreSlim _dialogSemaphore = new(1, 1);

        // Base font sizes captured from resources to avoid compounding adjustments
        private double _baseFuncBtnFontSize;
        private double _baseBottomButtonFontSize;
        private double _baseWaitingListFontSize;
        private double _baseSongListFontSize;
        private double _baseVisualSingerNameFontSize;

        // Cached brushes for active button states to improve performance
        private Brush? _activeButtonBackground;
        private Brush? _activeButtonForeground;

        // For fixed vocal track feature
        private bool _isVocalTrackFixed = false; // Player logic
        private Brush? _fixedButtonBackground; // Player logic
        private int _fixedAudioTrack = -1; // Player logic
        private Storyboard? _vocalBtnFlashStoryboard; // Player logic
        private Storyboard? _repeatBtnFlashStoryboard; // Player logic for loop play

        // For dynamic top 7 filter buttons
        private enum MainFilterMode { Singer, NewSong, Ranking, Generation, Language }
        private List<Button> _filterButtons = new();

        // State tracking for Language filter
        private List<string> _selectedLanguages = new List<string>();
        private string _selectedSingerType = ""; // "", "0", "1", "2", "99"
        private string _selectedWordCountRange = ""; // "", "1-2", "3", ...
        private bool _isDuetOnly = false;
        private bool _isFirstLanguageVisit = true;

        // Configurable number of days for the "New Song" filter
        public int NewSongDays { get; set; } // Loaded from settings

        // Configurable percentage for when to increment the play count
        public int PlayCountUpdatePercentage { get; set; } // Loaded from settings
        
        // Session timestamp for play history (initialized once at app startup)
        private string _sessionTimestampUserId = string.Empty;

        private bool _isDownloadingYoutube = false;
        public bool IsDownloadingYoutube
        {
            get => _isDownloadingYoutube;
            set { if (_isDownloadingYoutube != value) { _isDownloadingYoutube = value; OnPropertyChanged(nameof(IsDownloadingYoutube)); } }
        }

        private double _youtubeDownloadPercentage = 0;
        public double YoutubeDownloadPercentage
        {
            get => _youtubeDownloadPercentage;
            set { if (_youtubeDownloadPercentage != value) { _youtubeDownloadPercentage = value; OnPropertyChanged(nameof(YoutubeDownloadPercentage)); } }
        }

        private bool _isPlayingYoutube = false;
        public bool IsPlayingYoutube
        {
            get => _isPlayingYoutube;
            set { if (_isPlayingYoutube != value) { _isPlayingYoutube = value; OnPropertyChanged(nameof(IsPlayingYoutube)); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private CancellationTokenSource? _youtubeDownloadCts;

        public MainWindow()
        {
            AppLogger.Log("MainWindow constructor started.");
            InitializeComponent();

            // Initialize session timestamp once at app startup
            _sessionTimestampUserId = DateTime.Now.ToString("MM/dd HH:mm");
            AppLogger.Log($"Session timestamp initialized: {_sessionTimestampUserId}");
            
            // Load settings from JSON file first
            AppLogger.Log("Loading settings...");
            SettingsManager.Instance.LoadSettings();
            Loc.LanguageChanged += (_, _) => RefreshFilterButtonTexts();
            ThemeSettingsHandler.LoadSettings();
            ApplyNavMenuOrder();
            ApplySettings();
            AppLogger.Log("Settings loaded and applied.");

            // Initialize performance logging early
            string dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CrazySong.mdb");
            try
            {
                DebugLog("Attempting to initialize database...");
                // Construct the path to the database file in the application's root directory.

                // Use the existing SongDatas class to initialize and load all data from the Access database.
                // This will load songs, singers, etc., into memory for fast access.
                SongDatas.Init("Access", dbPath);
                // Ensure the server cache is built immediately after loading data, before any requests can come in.
                HttpServer.PrepareCachedSingerData();
                
                // Scan for missing singer photos on startup if enabled
                SingerPhotoManager.RecordMissingSinger();

                AppLogger.Log("Database initialized successfully.");
            }
            catch (Exception ex)
            {
                AppLogger.LogError("FATAL: Database initialization failed.", ex);
                // Show a user-friendly error message.
                MessageBox.Show(
                    Loc.T("App.DbLoadFailed", ex.Message, dbPath),
                    Loc.T("App.FatalError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // Force the application to shut down.
                Application.Current.Shutdown();
                return; // Stop further execution of the constructor.
            }

            AppLogger.Log("Initializing UI components...");
            // Initialize with an empty list. Data will be loaded by the user.
            _allSingers = new List<string>();
            _totalPages = 1;

            // Collect the 7 filter buttons into a list for easy management
            _filterButtons = new List<Button>
            {
                FilterButton1, FilterButton2, FilterButton3, FilterButton4,
                FilterButton5, FilterButton6, FilterButton7
            };

            // Set the initial filter mode to "Singer"
            SetupFilterButtons(MainFilterMode.Singer);

            LoadSingerPage(_currentPage);


            // Wire up waiting list pagination buttons
            WaitListPageUp.Click += WaitListPageUp_Click;
            WaitListPageDown.Click += WaitListPageDown_Click;

            // Wire up media control sliders
            VideoProgressSlider.ValueChanged += VideoProgressSlider_ValueChanged;
            VolumeSlider.ValueChanged += VolumeSlider_ValueChanged;

            // Prepare the list of styles for the style switcher button
            InitializeStyleSwitcher();
            // Apply default Material Design style for buttons at startup
            // This sets styles (FunctionButtonStyle, SingerFilterButtonStyle, etc.) to base "MaterialDesignOutlinedButton"
            ApplyDefaultButtonStyleOnStartup();

            // Initialize theme switcher
            InitializeThemeSwitcher();

            // Initialize the singer grid button style property with the default from XAML resources

            // Initialize the media player
            InitializeMediaPlayer();
            AppLogger.Log("Media player initialized.");

            // Initialize the waiting list with 5 empty rows
            InitializeWaitingList();

            // Delay secondary display initialization to ensure main media player is ready
            this.Loaded += MainWindow_Loaded;

            // Register an event handler to gracefully close the media player when the window shuts down.
            // This ensures that file handles and other resources from the library are released.
            this.Closing += MainWindow_Closing; // Signature fixed below
            
            // Handle keydown events for pagination
            this.PreviewKeyDown += MainWindow_PreviewKeyDown;

            // Capture base font sizes directly from settings to ensure accurate values 
            // before applying responsive scaling on resize.
            _baseFuncBtnFontSize = ThemeSettingsHandler.Settings.FuncBtnFontSize;
            _baseBottomButtonFontSize = ThemeSettingsHandler.Settings.BottomButtonFontSize;
            _baseWaitingListFontSize = ThemeSettingsHandler.Settings.WaitingListFontSize;
            _baseSongListFontSize = ThemeSettingsHandler.Settings.SongListFontSize;
            _baseVisualSingerNameFontSize = SettingsManager.Instance.CurrentSettings.VisualSingerNameFontSize;

            // Apply initial responsive font sizing and keep it updated on resize
            this.SizeChanged += (_, __) => ApplyResponsiveFontSizing();
            ApplyResponsiveFontSizing();

            InitializeButtonFlashAnimation(VocalBtn);
            InitializeButtonFlashAnimation(RepeatBtn);

            // Bind the 40-button ItemsControl once to the backing collection
            if (SearchInputGrid != null && SearchInputGrid.ItemsSource == null)
            {
                SearchInputGrid.ItemsSource = _quickWords;
            }

            // Initialize state storage for each quick method
            foreach (QuickMethod method in Enum.GetValues(typeof(QuickMethod)))
            {
                _quickMethodSelectedWords[method] = new List<string>();
                _quickMethodCurrentPage[method] = 1;
                _quickMethodResultsCache[method] = new List<SongDisplayItem>();
            }

            // Set initial state for quick search (will be done in SearchBtnByInput_Click)
            // SetQuickMethod(QuickMethod.Bopomofo, BopomofoListBtn);
            // UpdateSearchWords();

            // Default view (主選單第一項) 在 MainWindow_Loaded 套用主題後才開啟，見 OpenDefaultNavView

            // Initialize digital clock timer
            InitializeDigitalClock();

            // Initialize marquee manager
            MarqueeManager.Instance.Initialize(this, _videoDisplayWindow);

            // Initialize cursor idle timer and movement handlers
            InitializeCursorIdleTimer();
            this.MouseMove += MainWindow_MouseMove;
            this.PreviewMouseMove += MainWindow_MouseMove;
            this.PreviewMouseDown += MainWindow_PreviewMouseDown;
            this.PreviewMouseUp += MainWindow_PreviewMouseUp;

            // Start HTTP server for remote control asynchronously in background to avoid blocking UI startup
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var server = UltimateKtv.HttpServer.StartHttpServer(this, DebugLog);

                    // If the window started closing while the server was starting, stop it right away
                    // (MainWindow_Closing already ran and could not see it)
                    bool closing;
                    lock (_httpServerLock)
                    {
                        closing = _isClosing;
                        if (!closing) _httpServer = server;
                    }
                    if (closing)
                    {
                        server?.Stop();
                        AppLogger.Log("HTTP server stopped (window closed during startup).");
                        return;
                    }

                    string localIp = UltimateKtv.HttpServer.GetLocalIPAddress();
                    Dispatcher.InvokeAsync(() =>
                    {
                        _lastKnownIp = localIp;
                        if (IsSingleMonitorMode && _httpServer != null)
                        {
                            DisplayWebHostInfoMarquee();
                        }
                        OnHttpServerStartupDone();
                    });
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Failed to start HTTP server in background", ex);
                    Dispatcher.InvokeAsync(OnHttpServerStartupDone);
                }
            });

            AppLogger.Log("MainWindow constructor finished.");
        }

        /// <summary>
        /// 開啟主選單排第一個的項目 (觸發 Click 會一併經 NavRail_Click 標示選中)。
        /// 需在 ThemeSettingsHandler.ApplyToResources 之後呼叫，否則頁面取到的是預設主題色。
        /// </summary>
        private void OpenDefaultNavView()
        {
            var firstNavButton = NavMenuPanel.Children.OfType<Button>().FirstOrDefault() ?? FuncBtnBySinger;
            try
            {
                firstNavButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent, firstNavButton));
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"Failed to open default view '{firstNavButton.Name}', falling back to Singer view.", ex);
                try { FuncBtnBySinger_Click(null!, null!); } catch { }
                SetActiveNavButton(FuncBtnBySinger);
            }
        }

        /// <summary>
        /// Initializes the digital clock timer that updates every second.
        /// </summary>
        private void InitializeDigitalClock()
        {
            // Set initial time
            DigitalClockText.Text = DateTime.Now.ToString("HH:mm");

            // Create a timer that ticks every second
            var clockTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            clockTimer.Tick += (s, e) =>
            {
                string newTime = DateTime.Now.ToString("HH:mm");
                if (DigitalClockText.Text != newTime)
                {
                    DigitalClockText.Text = newTime;
                }
            };
            clockTimer.Start();
        }

        private double TryGetDoubleResource(string key, double fallback)
        {
            try
            {
                var obj = TryFindResource(key);
                if (obj is double d) return d;
                if (obj is System.IConvertible c) return c.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { }
            return fallback;
        }

        /// <summary>
        /// Increase fonts slightly on small screens (<= 1366x768) so UI remains readable when scaled to fill.
        /// Uses DynamicResource so changes propagate across controls.
        /// </summary>
        private void ApplyResponsiveFontSizing()
        {
            try
            {
                // Using the window's render size (after Viewbox) is fine as a heuristic
                double w = this.ActualWidth;
                double h = this.ActualHeight;

                // Threshold for small screens
                bool isSmall = (w > 0 && w <= 1366) || (h > 0 && h <= 768);

                // Slight bump for small screens
                double scale = isSmall ? 1.10 : 1.0; // general UI
                double waitListScale = isSmall ? 1.05 : 1.0;   // smaller bump to avoid clipping 9 rows
                double songListScale = isSmall ? 1.05 : 1.0; // smaller bump for song lists

                UpdateFontResource("FuncBtnFontSize", _baseFuncBtnFontSize * scale);
                UpdateFontResource("BottomButtonFontSize", _baseBottomButtonFontSize * scale);
                UpdateFontResource("WaitingListFontSize", _baseWaitingListFontSize * waitListScale);
                UpdateFontResource("SongListFontSize", _baseSongListFontSize * songListScale);
                UpdateFontResource("VisualSingerNameFontSize", _baseVisualSingerNameFontSize * scale);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ApplyResponsiveFontSizing error: {ex.Message}");
                AppLogger.LogError("ApplyResponsiveFontSizing error", ex);
            }
        }

        private void UpdateFontResource(string key, double value)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 1. Update at window level (takes precedence)
                    if (Resources.Contains(key))
                    {
                        Resources[key] = value;
                    }
                    else
                    {
                        Resources.Add(key, value);
                    }

                    // 2. Update at application level (fallback for recycled items in templates)
                    if (Application.Current != null)
                    {
                        if (Application.Current.Resources.Contains(key))
                        {
                            Application.Current.Resources[key] = value;
                        }
                        else
                        {
                            Application.Current.Resources.Add(key, value);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"Error updating font resource {key}", ex);
                }
            });
        }

        /// <summary>
        /// Timestamped debug logging helper that writes to both Debug and Trace, and flushes.
        /// </summary>
        private static void DebugLog(string message)
        {
            try
            {
                var ts = DateTime.Now.ToString("HH:mm:ss.fff");
                var tid = System.Threading.Thread.CurrentThread.ManagedThreadId;
                var line = $"[{ts} T{tid}] {message}";
                Debug.WriteLine(line);
                //System.Diagnostics.Trace.WriteLine(line);
                System.Diagnostics.Trace.Flush();
            }
            catch { /* ignore logging errors */ }
        }

        // Helper: find visual child of type T with optional element name
        private static T? FindVisualChild<T>(DependencyObject? parent, string? name = null) where T : FrameworkElement
        {
            if (parent == null) return null;
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed && (name == null || typed.Name == name))
                {
                    return typed;
                }
                var result = FindVisualChild<T>(child, name);
                if (result != null) return result;
            }
            return null;
        }



        // Helper: find the first visual parent of type T for a given DependencyObject
        private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                var parent = VisualTreeHelper.GetParent(child);
                if (parent is T typed)
                    return typed;
                child = parent;
            }
            return null;
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? depObj) where T : DependencyObject
        {
            if (depObj == null) yield break;
            int count = VisualTreeHelper.GetChildrenCount(depObj);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(depObj, i);
                if (child is T t)
                {
                    yield return t;
                }
                foreach (var childOfChild in FindVisualChildren<T>(child))
                {
                    yield return childOfChild;
                }
            }
        }

        private DataGridRow? _currentlySelectedSongRow = null;
        private DataGrid? _currentlySelectedSongGrid = null;

        /// <summary>
        /// Clears all selection state across all song grids
        /// so that previously clicked rows properly restore their default background.
        /// </summary>
        public void ClearSongGridsSelection()
        {
            // 效能:沒有任何選取時直接返回,避免每次滑鼠按下/放開都走訪三個 DataGrid 的視覺樹
            if (_currentlySelectedSongRow == null && _currentlySelectedSongGrid == null
                && (SongListGrid == null || (SongListGrid.SelectedItem == null && SongListGrid.SelectedIndex < 0))
                && (QuickSongListGrid == null || (QuickSongListGrid.SelectedItem == null && QuickSongListGrid.SelectedIndex < 0))
                && (LanguageSongListGrid == null || (LanguageSongListGrid.SelectedItem == null && LanguageSongListGrid.SelectedIndex < 0)))
            {
                return;
            }

            if (_currentlySelectedSongRow != null)
            {
                _currentlySelectedSongRow.IsSelected = false;
                _currentlySelectedSongRow = null;
            }
            if (_currentlySelectedSongGrid != null)
            {
                _currentlySelectedSongGrid.UnselectAll();
                _currentlySelectedSongGrid.SelectedIndex = -1;
                _currentlySelectedSongGrid.SelectedItem = null;
                _currentlySelectedSongGrid = null;
            }

            ClearDataGridSelection(SongListGrid);
            ClearDataGridSelection(QuickSongListGrid);
            ClearDataGridSelection(LanguageSongListGrid);
        }

        private static void ClearDataGridSelection(DataGrid? dg)
        {
            if (dg == null) return;
            try
            {
                dg.UnselectAll();
                dg.SelectedIndex = -1;
                dg.SelectedItem = null;

                // Explicitly deselect all generated row containers
                for (int i = 0; i < dg.Items.Count; i++)
                {
                    if (dg.ItemContainerGenerator.ContainerFromIndex(i) is DataGridRow r)
                    {
                        r.IsSelected = false;
                    }
                }

                // Also traverse visual tree to catch any other visual row instances
                foreach (var r in FindVisualChildren<DataGridRow>(dg))
                {
                    r.IsSelected = false;
                }
            }
            catch { }
        }

        private void MainWindow_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject dep)
            {
                var grid = FindVisualParent<DataGrid>(dep);
                var row = FindVisualParent<DataGridRow>(dep);

                // If clicking outside song grids, or inside a song grid but not on a row (e.g. headers or empty space)
                if (grid != SongListGrid && grid != QuickSongListGrid && grid != LanguageSongListGrid)
                {
                    ClearSongGridsSelection();
                }
                else if (row == null)
                {
                    ClearSongGridsSelection();
                }
            }
        }

        private void MainWindow_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            // When mouse is released anywhere, ensure song selection is cleared so row background restores
            ClearSongGridsSelection();
        }

        /// <summary>
        /// Safely stop a MediaUriElement instance with logging.
        /// </summary>
        private static void SafeStop(MediaUriElement? element, string name)
        {
            try
            {
                if (element == null)
                {
                    DebugLog($"SafeStop: {name} is null");
                    return;
                }
                element.Stop();
                element.Source = null;
                // must clear position to fix new song's first MediaPositionChanged() still get previous song's media position
                element.MediaPosition = 0;
                DebugLog($"SafeStop: END Stop() on {name}");
            }
            catch (Exception ex)
            {
                DebugLog($"SafeStop error on {name}: {ex.Message}\n{ex}");
                AppLogger.LogError($"SafeStop error on {name}", ex);
            }
        }

        /// <summary>
        /// Enables or disables the main player control buttons to prevent user interaction during transitions.
        /// </summary>
        private void SetPlayerControlsEnabled(bool isEnabled)
        {
            SkipSong.IsEnabled = isEnabled;
            RepeatBtn.IsEnabled = isEnabled;
            PauseBtn.IsEnabled = isEnabled;
            VocalBtn.IsEnabled = isEnabled;
            MusicBtn.IsEnabled = isEnabled;
        }


        /// <summary>
        /// Shows a simple message dialog using MaterialDesign
        /// </summary>
        private async Task ShowMessageDialog(string title, string message)
        {
            var dialogContent = new StackPanel { Margin = new Thickness(20) };

            dialogContent.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 15)
            });

            dialogContent.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            });

            var okButton = new Button
            {
                Content = "OK",
                HorizontalAlignment = HorizontalAlignment.Right,
                MinWidth = 80
            };
            ThemeSettingsHandler.ApplyOutlinedButtonStyle(okButton, 16);

            okButton.Click += (s, args) => DialogHost.CloseDialogCommand.Execute(null, null);
            dialogContent.Children.Add(okButton);

            await ShowDialogSafeAsync(new ContentControl { Content = dialogContent }, "RootDialog");
        }

        private async Task<object?> ShowDialogSafeAsync(object content, string hostId = "RootDialog")
        {
            bool acquired = false;
            try
            {
                // Fast path: if a dialog is already open, skip opening a new one.
                if (DialogHost.IsDialogOpen(hostId))
                    return null;

                await _dialogSemaphore.WaitAsync();
                acquired = true;

                // Double-check after acquiring the semaphore.
                if (DialogHost.IsDialogOpen(hostId))
                    return null;

                return await DialogHost.Show(content, hostId);
            }
            catch (InvalidOperationException)
            {
                // Host already open due to race/rapid clicks; treat as no-op.
                return null;
            }
            finally
            {
                if (acquired)
                    _dialogSemaphore.Release();
            }
        }

        /// <summary>
        /// A generic helper method to filter the main singer list and update the UI.
        /// </summary>
        /// <param name="filterKey">A unique key to identify this filter for page state memory.</param>
        /// <param name="filter">A predicate to apply to the singer data.</param>
        private void FilterAndDisplaySingers(string filterKey, Predicate<Dictionary<string, object?>> filter)
        {
            // 在最愛頁按歌手類型按鈕：直接切到歌星頁的該類型列表 (最愛清單是使用者名稱，不做交集篩選)
            if (SingerPhotoManager.CurrentSubFolder == "FavoriteUser")
            {
                AppLogger.Log($"User action: Singer filter '{filterKey}' from Favorites view, switching to Singer view");
                SetActiveNavButton(FuncBtnBySinger);
            }

            // Ensure we are looking in the correct photo folder
            SingerPhotoManager.CurrentSubFolder = "SingerAvatar";

            // Store current page state for the previous filter
            if (!string.IsNullOrEmpty(_currentFilterKey))
            {
                _filterPageStates[_currentFilterKey] = _currentPage;
            }

            // Update current filter key
            _currentFilterKey = filterKey;

            if (SongDatas.SingerData == null)
            {
                _allSingers = new List<string>();
            }
            else
            {
                // Use the globally sorted data directly.
                _allSingers = SongDatas.SingerData
                    .Where(s => s != null && filter(s))
                    .Select(s => s["Singer_Name"]?.ToString() ?? "")
                    .Where(s => !string.IsNullOrEmpty(s))
                    .Distinct()
                    .ToList();
            }

            _totalPages = (_allSingers.Count == 0) ? 1 : (_allSingers.Count + PageSize - 1) / PageSize;

            // Restore the saved page for this filter, or default to page 1
            if (_filterPageStates.TryGetValue(filterKey, out int savedPage) && savedPage <= _totalPages)
            {
                _currentPage = savedPage;
            }
            else
            {
                _currentPage = 1;
            }

            LoadSingerPage(_currentPage);

            // Always show singer grid when filtering
            ShowSingerGrid();
        }
        /// <summary>
        /// This event handler is called when the main window is fully loaded
        /// </summary>
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("MainWindow_Loaded event triggered.");

            // Prevent system sleep while the app is running
            PowerManagementHelper.PreventSleep();
            
            
            // Apply theme settings to XAML resources (colors, brushes, etc.)
            ThemeSettingsHandler.ApplyToResources(this);
            ApplyResponsiveFontSizing();

            // 主題色套用後才開啟預設頁面：部分頁面 (如 Youtube 點播記錄按鈕) 會直接取用當下的筆刷值
            OpenDefaultNavView();
            
            // Get monitor info first to validate settings before initializing display
            var monitors = VideoDisplayWindow.GetAvailableMonitorsInfo();
            AppLogger.Log($"Detected {monitors.Count} monitor(s).");

            // Validate and auto-correct monitor settings BEFORE initializing display
            bool settingsWereCorrected = SettingsManager.Instance.ValidateAndCorrectMonitorSettings(monitors.Count <= 1);
            
            // Get the current settings (already validated/corrected)
            var settings = SettingsManager.Instance.CurrentSettings;
            
            if (settingsWereCorrected)
            {
                AppLogger.Log($"Monitor settings auto-corrected: ConsoleScreen={settings.ConsoleScreen}, PlayerScreen={settings.PlayerScreen}");
                SettingsManager.Instance.SaveSettings();

                MessageBox.Show(
                    Loc.T("App.ScreenFixed", settings.ConsoleScreen, settings.PlayerScreen),
                    Loc.T("App.ScreenFixedTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }

            // Now initialize display with validated/corrected settings
            IsSingleMonitorMode = InitializeDisplayConfiguration();
            AppLogger.Log("Display configuration initialization completed.");

            // Initialize marquee container sizing
            if (MarqueeContainer != null)
            {
                MarqueeContainer.Width = MediaPlayerContainer.ActualWidth;
                MarqueeContainer.Height = MediaPlayerContainer.ActualHeight;
            }

            // Initialize default media stretch mode (always fullscreen)
            InitializeDefaultMediaStretch();

            // Position main window to its designated monitor
            MoveWindowToMonitor(settings.ConsoleScreen);

            if (IsSingleMonitorMode)
            {
                AppLogger.Log("Single monitor mode detected. Console and Player screens are the same. Web order only mode enabled.");
                this.Visibility = Visibility.Hidden;
                this.WindowState = WindowState.Minimized;
                AppLogger.Log("Console window hidden and moved to correct monitor. Player screen will be displayed only.");

                // Start cursor idle timer in single monitor mode
                _cursorIdleTimer?.Start();
            }


            // Apply cursor limiting if enabled and not in single-monitor mode
            if (settings.LimitCursorOnMain && !IsSingleMonitorMode)
            {
                ApplyCursorLimit();
                AppLogger.Log("Cursor limiting applied at startup.");
            }

            // If random play is enabled, prioritize random play and skip startup welcome marquees.
            // Otherwise, display startup welcome marquee and single-monitor web host info.
            if (!settings.RandomPlayEnabled)
            {
                // Display web host IP/port info on player screen for 10 seconds in single-monitor mode
                if (IsSingleMonitorMode && _httpServer != null)
                {
                    DisplayWebHostInfoMarquee();
                }

                // Display startup welcome marquee on player screen if configured.
                // If the HTTP server is still starting (port may change), defer until it is done.
                if (_httpServerStartupDone)
                    MarqueeAPI.ShowStartupMarquee(MarqueeDisplayDevice.PlayerScreen);
                else
                    _pendingStartupMarquee = true;
            }

            // Trigger random play timer on startup if playlist is empty and random play is enabled
            StartDelayedRandomPlayWhenEmpty();

            // Close the splash screen with a fade out
            App.StartupSplashScreen?.Close(TimeSpan.FromSeconds(0.5));

            // Check for auto-updates in the background
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                // Slight delay to ensure main window is fully rendered before any prompts
                await System.Threading.Tasks.Task.Delay(2000);
                await UltimateKtv.Services.UpdateManager.CheckForUpdatesAsync();
            });

            AppLogger.Log("MainWindow_Loaded event finished.");
        }

        /// <summary>
        /// Called on the UI thread once the background HTTP server startup has finished (success or not).
        /// </summary>
        private void OnHttpServerStartupDone()
        {
            _httpServerStartupDone = true;
            if (_pendingStartupMarquee)
            {
                _pendingStartupMarquee = false;
                MarqueeAPI.ShowStartupMarquee(MarqueeDisplayDevice.PlayerScreen);
            }
        }

        public void ShowFromVideoWindow()
        {
            DebugLog("ShowFromVideoWindow: Making MainWindow visible and maximized.");
            this.Visibility = Visibility.Visible;
            this.WindowState = WindowState.Maximized;
            this.Activate();
        }

        private void MediaPlayerContainer_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && IsSingleMonitorMode && _videoDisplayWindow != null)
            {
                DebugLog("Single monitor mode: Hiding MainWindow and activating VideoDisplayWindow.");
                
                // Reset cursor idle timer to ensure it auto-hides after switching
                ResetCursorIdleTimer();

                this.Visibility = Visibility.Hidden;
                this.WindowState = WindowState.Minimized;

                // Ensure VideoDisplayWindow is activated (it's always open now)
                _videoDisplayWindow.Show();
                _videoDisplayWindow.Activate();
            }
        }

        /// <summary>
        /// This event handler is called just before the window closes.
        /// It's the correct place to release resources, like the media player.
        /// </summary>
        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Stop HTTP server (if it is still starting, the background task will stop it once it sees _isClosing)
            UltimateKtv.HttpServer? server;
            lock (_httpServerLock)
            {
                _isClosing = true;
                server = _httpServer;
                _httpServer = null;
            }
            if (server != null)
            {
                server.Dispose();  // Dispose() calls Stop()
                AppLogger.Log("HTTP server stopped.");
            }

            // Allow system sleep again
            PowerManagementHelper.RestoreSleep();

            // Clean up video display window
            if (_videoDisplayWindow != null)
            { _videoDisplayWindow.ForceClose(); }

            // The settings window is kept hidden for reuse; close it for real so the app can exit
            _settingsWindow?.ForceClose();
            _settingsWindow = null;

            mediaUriElement?.Close();
        }

        /// <summary>
        /// Global mouse wheel handler to simulate PageUp/PageDown for grids under the mouse cursor.
        /// </summary>
        private void Window_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                // Find the control currently under the mouse
                if (e.OriginalSource is not DependencyObject source) return;

                // Check if the mouse is over one of the grid components

                // 1. Waiting List
                if (FindVisualParent<DataGrid>(source) == WaitingListGrid)
                {
                    if (e.Delta > 0) WaitListPageUp_Click(sender, new RoutedEventArgs());
                    else WaitListPageDown_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                // 2. Quick Search Results
                if ((FindVisualParent<DataGrid>(source) == QuickSongListGrid && QuickSongListGrid.Visibility == Visibility.Visible) ||
                    (FindVisualParent<ItemsControl>(source) == YoutubeThumbnailGrid && YoutubeThumbnailGrid.Visibility == Visibility.Visible) ||
                    (FindVisualParent<ItemsControl>(source) == QuickSingerGrid && QuickSingerGrid.Visibility == Visibility.Visible) ||
                    FindVisualParent<Border>(source) == SearchSymbolPanel ||
                    FindVisualParent<Grid>(source) == QuickResultsContainer)
                {
                    if (e.Delta > 0) QuickPageUp_Click(sender, new RoutedEventArgs());
                    else QuickPageDown_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                // 3. Singer Grid (ItemsControl)
                var itemsControl = FindVisualParent<ItemsControl>(source);
                if ((itemsControl == SingerGrid && SingerGrid.Visibility == Visibility.Visible) ||
                    (itemsControl == VisualSingerGrid && VisualSingerGrid.Visibility == Visibility.Visible))
                {
                    if (e.Delta > 0) PageUp_Click(sender, new RoutedEventArgs());
                    else PageDown_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                // 4. Main Song List / Language Song List
                var activeGrid = FindVisualParent<DataGrid>(source);
                if ((activeGrid == SongListGrid && SongListGrid.Visibility == Visibility.Visible) ||
                    (activeGrid == LanguageSongListGrid && LanguageSongListGrid.Visibility == Visibility.Visible))
                {
                    if (e.Delta > 0) PageUp_Click(sender, new RoutedEventArgs());
                    else PageDown_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                // 5. Check for legacy/other grids that might be visible
                var dataGrid = FindVisualParent<DataGrid>(source);
                if (dataGrid != null && dataGrid.Visibility == Visibility.Visible)
                {
                    // If any other DataGrid is visible and under mouse, try global pagination
                    if (dataGrid == LanguageResultGrid ||
                        dataGrid == SearchResultGrid ||
                        dataGrid == NumberResultGrid ||
                        dataGrid == FavoriteResultGrid ||
                        dataGrid == NewSongResultGrid ||
                        dataGrid == RankResultGrid ||
                        dataGrid == DuetResultGrid)
                    {
                        if (e.Delta > 0) PageUp_Click(sender, new RoutedEventArgs());
                        else PageDown_Click(sender, new RoutedEventArgs());
                        e.Handled = true;
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                // Fail silently for UI convenience features
                Debug.WriteLine($"PreviewMouseWheel Error: {ex.Message}");
            }
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Handle Quick Search input (A-Z, 0-9, Space)
            if (SearchInputGrid != null && SearchInputGrid.Visibility == Visibility.Visible)
            {
                if (HandleQuickSearchKeyDown(e.Key))
                {
                    e.Handled = true;
                    return;
                }
            }

            // Handle PageUp/PageDown and Up/Down/Home/End for navigation
            if (e.Key == Key.PageUp || e.Key == Key.PageDown || e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Home || e.Key == Key.End)
            {
                bool isUp = e.Key == Key.PageUp || e.Key == Key.Up;
                bool isDown = e.Key == Key.PageDown || e.Key == Key.Down;
                bool isHome = e.Key == Key.Home;
                bool isEnd = e.Key == Key.End;

                // Priority 1: Quick Search Visibility
                if (QuickResultsContainer != null && QuickResultsContainer.Visibility == Visibility.Visible)
                {
                    if (isHome) 
                    {
                        _quickMethodCurrentPage[_currentQuickMethod] = 1;
                        RefreshQuickResultsPage();
                    }
                    else if (isEnd)
                    {
                        var total = _quickMethodResultsCache.GetValueOrDefault(_currentQuickMethod, new List<SongDisplayItem>()).Count;
                        _quickMethodCurrentPage[_currentQuickMethod] = Math.Max(1, (int)Math.Ceiling(total / (double)QuickSearchPageSize));
                        RefreshQuickResultsPage();
                    }
                    else if (isUp) QuickPageUp_Click(sender, new RoutedEventArgs());
                    else if (isDown) QuickPageDown_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                }

                // Priority 2: Default Main List (Singer, SongList, LanguageSongList)
                if (isHome)
                {
                    if (SongListGrid.Visibility == Visibility.Visible || LanguageSongListGrid.Visibility == Visibility.Visible)
                    {
                        _currentSongPage = 1;
                        LoadSongPage(_currentSongPage);
                    }
                    else
                    {
                        _currentPage = 1;
                        LoadSingerPage(_currentPage);
                    }
                }
                else if (isEnd)
                {
                    if (SongListGrid.Visibility == Visibility.Visible || LanguageSongListGrid.Visibility == Visibility.Visible)
                    {
                        _currentSongPage = _totalSongPages;
                        LoadSongPage(_currentSongPage);
                    }
                    else
                    {
                        _currentPage = _totalPages;
                        LoadSingerPage(_currentPage);
                    }
                }
                else if (isUp) PageUp_Click(sender, new RoutedEventArgs());
                else if (isDown) PageDown_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        /// <summary>
        /// Formats a TimeSpan to MM:SS format
        /// </summary>
        private string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
        }

        /// <summary>
        /// Loads songs for the selected singer and displays them in SongListGrid
        /// </summary>
        /// <param name="singerName">The name of the singer to search for</param>
        private void LoadSongsForSinger(string singerName)
        {
            _currentSongsTitle = singerName;
            _isLanguageMode = false;

            if (SongDatas.SongData == null)
            {
                _allSongs = new List<SongDisplayItem>();
                if (SongListGrid != null) SongListGrid.ItemsSource = _allSongs;
                return;
            }

            var songs = new List<SongDisplayItem>();

            char[] delimiters = { '&', '/', ',', '、', '；', ';', '|' };
            var primarySinger = singerName.Split(delimiters, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? singerName;

            foreach (var song in SongDatas.SongData)
            {
                if (song == null) continue;

                // Get the singer field from the song data
                var songSinger = song.TryGetValue("Song_Singer", out var singerObj) ? singerObj?.ToString() ?? "" : "";

                // Check if the singer name matches (match full target, or primary singer)
                if (SongDatas.ContainsSinger(songSinger, singerName) || (!string.IsNullOrEmpty(primarySinger) && SongDatas.ContainsSinger(songSinger, primarySinger)))
                {
                    var songItem = new SongDisplayItem
                    {
                        SongId = song.TryGetValue("Song_Id", out var idObj) ? idObj?.ToString() ?? "" : "",
                        SongName = song.TryGetValue("Song_SongName", out var nameObj) ? nameObj?.ToString() ?? "" : "",
                        SingerName = songSinger,
                        Language = song.TryGetValue("Song_Lang", out var langObj) ? langObj?.ToString() ?? "" : "",
                        FilePath = song.TryGetValue("FilePath", out var pathObj) ? pathObj?.ToString() ?? "" : "",
                        Song_WordCount = song.TryGetValue("Song_WordCount", out var wcObj) && int.TryParse(wcObj?.ToString(), out int wc) ? wc : 0,
                        Song_PlayCount = song.TryGetValue("Song_PlayCount", out var pcObj) && int.TryParse(pcObj?.ToString(), out int pc) ? pc : 0,
                        Song_CreatDate = song.TryGetValue("Song_CreatDate", out var dateObj) && dateObj is DateTime dt ? dt : (DateTime?)null,
                        Volume = song.TryGetValue("Song_Volume", out var volObj) && int.TryParse(volObj?.ToString(), out int vol) ? vol : 90,
                        AudioTrack = song.TryGetValue("Song_Track", out var trackObj) && int.TryParse(trackObj?.ToString(), out int track) ? track : 0
                    };

                    // Only add if song name is not empty
                    if (!string.IsNullOrEmpty(songItem.SongName))
                    {
                        songs.Add(songItem);
                    }
                }
            }

            // Sort songs based on the selected sort method
            _allSongs = SongDatas.ApplySongSorting(songs, (s, key) => key switch
            {
                "Song_PlayCount" or "PlayCount" => s.Song_PlayCount,
                "Song_CreatDate" => s.Song_CreatDate,
                "Song_WordCount" => s.Song_WordCount,
                "Song_SongName" or "SongName" => s.SongName,
                _ => null
            });

            // Calculate pagination for songs
            _totalSongPages = (_allSongs.Count == 0) ? 1 : (_allSongs.Count + CurrentSongPageSize - 1) / CurrentSongPageSize;
            _currentSongPage = 1; // Reset to first page when loading new singer

            // Load first page of songs
            LoadSongPage(_currentSongPage);
        }

        /// <summary>
        /// Handles clicks on the song grids (QuickSongListGrid, SongListGrid, LanguageSongListGrid).
        /// Determines which column was clicked and performs the appropriate action.
        /// </summary>
        private void SongGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Find the visual element that was clicked
            if (e.OriginalSource is not DependencyObject source) return;

            // 列內的插播/最愛按鈕由按鈕自己處理，不觸發點歌或長按
            if (source is ButtonBase || FindVisualParent<ButtonBase>(source) != null) return;

            // Traverse up the visual tree to find the clicked DataGridCell
            var cell = FindVisualParent<DataGridCell>(source);
            if (cell == null) return;

            // Find the row containing the cell to get the data item
            var row = FindVisualParent<DataGridRow>(cell);
            if (row?.Item is not SongDisplayItem selectedSong) return;

            // Clear any previously selected row across song grids so only the clicked row is active
            ClearSongGridsSelection();

            // Highlight the selected row visually
            row.IsSelected = true;
            _currentlySelectedSongRow = row;
            _currentlySelectedSongGrid = sender as DataGrid;
            if (sender is DataGrid dg)
            {
                dg.SelectedItem = selectedSong;
            }

            // Check if we are in "Singer Search Mode" and clicking on the Quick Search Grid
            if (sender == QuickSongListGrid && _isSingerSearchMode)
            {
                // In Singer Mode, the "SongName" field holds the Singer Name.
                // We treat this click as selecting a singer.
                string singerName = selectedSong.SongName;
                if (!string.IsNullOrEmpty(singerName))
                {
                    OpenSingerFromQuickSearch(singerName);
                    e.Handled = true;
                    return;
                }
            }

            // Start long press timer for adding to favorites
            // Clone: 列表使用固定列，換頁時列的內容會被改掉
            _longPressSongItem = selectedSong.Clone();
            if (_longPressTimer == null)
            {
                _longPressTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                _longPressTimer.Tick += (s, args) =>
                {
                    _longPressTimer?.Stop();
                    if (_longPressSongItem != null)
                    {
                        ShowAddToFavoriteDialog(_longPressSongItem);
                        _longPressSongItem = null;
                        ClearSongGridsSelection();
                    }
                };
            }
            _longPressTimer.Start();

            // Get the column header to identify which column was clicked
            // QuickSongListGrid sets the header via Style so Header might be null, use DisplayIndex 1 as fallback
            string? columnHeader = cell?.Column?.Header?.ToString();

            bool isSingerColumn = (columnHeader == Loc.T("Col.Singer") || cell?.Column?.DisplayIndex == 1);
            if (sender == QuickSongListGrid && _searchMode == SearchMode.Youtube)
            {
                isSingerColumn = false; // YouTube search uses this column for duration
            }

            if (isSingerColumn && !string.IsNullOrWhiteSpace(selectedSong.SingerName))
            {
                // --- Special Action for Singer Column ---
                Debug.WriteLine($"Singer column clicked for singer: {selectedSong.SingerName}");

                // Cancel long press timer and clear item
                _longPressTimer?.Stop();
                _longPressSongItem = null;

                // Reset mode flags
                _isLanguageMode = false;
                _searchMode = SearchMode.Song;

                // Load all songs for the clicked singer
                _selectedSinger = selectedSong.SingerName;
                LoadSongsForSinger(selectedSong.SingerName);

                // Switch the view to the song list
                ShowQuickInputPanels(false); // Hide quick search if it was open
                if (LanguageSongListGrid != null) LanguageSongListGrid.Visibility = Visibility.Collapsed;
                if (LanguageSecondFilterGrid != null) LanguageSecondFilterGrid.Visibility = Visibility.Collapsed;
                if (LanguageWordCountFilterGrid != null) LanguageWordCountFilterGrid.Visibility = Visibility.Collapsed;
                if (SingerGrid != null) SingerGrid.Visibility = Visibility.Collapsed;
                if (VisualSingerGrid != null) VisualSingerGrid.Visibility = Visibility.Collapsed;
                if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = Visibility.Visible;
                if (SongListGrid != null) SongListGrid.Visibility = Visibility.Visible;

                SetupFilterButtons(MainFilterMode.Singer);
                ClearSongGridsSelection();
            }
            else
            {
                // --- Default Action for Other Columns ---
                // The user clicked on any other column (e.g., "Song Name", "Language").
                // The default action is to add the song to the waiting list.
                Debug.WriteLine($"'{columnHeader}' column clicked. Adding song to waiting list: {selectedSong.SongName}");
                // Don't add immediately - wait for mouse up to see if it's a long press
            }

            // Mark the event as handled to prevent the DataGrid's default selection behavior.
            e.Handled = true;
        }

        private void SongGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Stop long press timer
            if (_longPressTimer != null && _longPressTimer.IsEnabled)
            {
                _longPressTimer.Stop();
                
                // If timer was still running, it's a normal click - add to waiting list
                if (_longPressSongItem != null)
                {
                    if (_longPressSongItem.IsYoutube)
                    {
                        DownloadYoutubeVideo(_longPressSongItem);
                    }
                    else
                    {
                        AddSongToWaitingList(_longPressSongItem);
                    }
                    _longPressSongItem = null;
                }
            }

            // Always restore row background / clear selection after click completes
            ClearSongGridsSelection();
        }

        /// <summary>
        /// Caches theme-dependent brushes to avoid expensive lookups during UI updates.
        /// This should be called on startup and whenever the theme changes.
        /// </summary>
        private void CacheActiveButtonBrushes()
        {
            // Use ThemeSettingsHandler brushes directly for consistent theming
            _activeButtonBackground = ThemeSettingsHandler.PrimaryBrush;
            _activeButtonForeground = RowStyleBrushes.ForegroundOn(_activeButtonBackground);
            _fixedButtonBackground = ThemeSettingsHandler.PrimaryBrush;
        }

        /// <summary>
        /// Handles mouse enter on Singer column cells to highlight them.
        /// </summary>
        private void SingerCell_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_searchMode == SearchMode.Youtube) return;
            if (sender is Border border)
            {
                border.Background = TryFindResource("PrimaryHueLightBrush") as Brush ?? Brushes.Gold;
                // Find the TextBlock child and change its foreground
                var viewbox = border.Child as Viewbox;
                if (viewbox?.Child is TextBlock textBlock)
                {
                    textBlock.Foreground = Brushes.Black;
                }
            }
        }

        /// <summary>
        /// Handles mouse leave on Singer column cells to restore default appearance.
        /// </summary>
        private void SingerCell_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_searchMode == SearchMode.Youtube) return;
            if (sender is Border border)
            {
                border.Background = Brushes.Transparent;
                // Find the TextBlock child and restore its foreground
                var viewbox = border.Child as Viewbox;
                if (viewbox?.Child is TextBlock textBlock)
                {
                    textBlock.Foreground = TryFindResource("BrushTextDim") as Brush ?? new SolidColorBrush(Color.FromRgb(148, 163, 184));
                }
            }
        }

        private void FuncBtnByLanguage_Click(object sender, RoutedEventArgs e)
        {
            AppLogger.Log("User action: Navigate to Language view");
            ShowSingerPanels(false);
            ShowQuickInputPanels(false);
            
            // Show the additional filter grids for Language mode
            if (LanguageSecondFilterGrid != null) LanguageSecondFilterGrid.Visibility = Visibility.Visible;
            if (LanguageWordCountFilterGrid != null) LanguageWordCountFilterGrid.Visibility = Visibility.Visible;

            // Ensure the main song content grid and specifically the LanguageSongListGrid is visible for results
            if (SingerSongContentGrid != null) SingerSongContentGrid.Visibility = Visibility.Visible;
            if (LanguageSongListGrid != null) LanguageSongListGrid.Visibility = Visibility.Visible;
            if (SongListGrid != null) SongListGrid.Visibility = Visibility.Collapsed;
            if (SingerGrid != null) SingerGrid.Visibility = Visibility.Collapsed;
            if (VisualSingerGrid != null) VisualSingerGrid.Visibility = Visibility.Collapsed;

            SetupFilterButtons(MainFilterMode.Language);
            
            // Set default filters only on the first visit: Mandarin, Male Singer, Word Count 1-2
            if (_isFirstLanguageVisit)
            {
                _selectedLanguages.Clear();
                _selectedLanguages.Add("國語");
                _selectedSingerType = "0";
                _selectedWordCountRange = "1-2";
                _isDuetOnly = false;
                _isFirstLanguageVisit = false;
            }

            UpdateLanguageFilterButtonHighlights();
            ApplyLanguageCombinedFilter();
            
            // Set focus for keyboard/mouse wheel events
            if (LanguageSongListGrid != null)
            {
                LanguageSongListGrid.Focus();
                Keyboard.Focus(LanguageSongListGrid);
            }
        }

        private void LanguageDuetToggle_Click(object sender, RoutedEventArgs e)
        {
            _isDuetOnly = LanguageDuetToggle?.IsChecked == true;
            if (_isDuetOnly) _selectedSingerType = ""; // Clear singer type when switching to duet mode
            
            UpdateLanguageFilterButtonHighlights();
            ApplyLanguageCombinedFilter();
        }

        private void LanguageSingerType_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string tag = btn?.Tag?.ToString() ?? "";
            
            // If already selected, clear it (Toggle logic)
            if (_selectedSingerType == tag)
            {
                _selectedSingerType = "";
            }
            else
            {
                _selectedSingerType = tag;
            }
            
            UpdateLanguageFilterButtonHighlights();
            ApplyLanguageCombinedFilter();
        }

        private void LanguageWordCount_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            string tag = btn?.Tag?.ToString() ?? "";
            
            // If already selected, clear it (Toggle logic)
            if (_selectedWordCountRange == tag)
            {
                _selectedWordCountRange = "";
            }
            else
            {
                _selectedWordCountRange = tag;
            }
            
            UpdateLanguageFilterButtonHighlights();
            ApplyLanguageCombinedFilter();
        }
    }
}