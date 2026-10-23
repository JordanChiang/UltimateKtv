using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Reflection;
using System.Runtime.Loader;

namespace UltimateKtv
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static Mutex? _instanceMutex;
        public static SplashScreen? StartupSplashScreen { get; private set; }

        public App()
        {
            // Ensure the working directory is set to the application's base directory
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            // 用 ALC.Resolving + LoadFromAssemblyPath 而非 AssemblyResolve + Assembly.LoadFrom：
            // LoadFrom 會掛上 .NET 內建的 LoadFromResolveHandler，在 libs\ 找不到衛星資源組件時丟 FileNotFoundException
            AssemblyLoadContext.Default.Resolving += OnAssemblyResolve;
        }

        /// <summary>
        /// 結束程式 (shutdownWindows 時一併關機)；主控台與手機共用。須在 UI 執行緒呼叫。
        /// 先在背景執行緒停止錄音並等 MP3 寫完：NAudio 的 RecordingStopped 會回呼 UI 執行緒，
        /// 若在 App_Exit 才等就會互卡到逾時。
        /// </summary>
        public static async Task QuitAsync(bool shutdownWindows)
        {
            await Task.Run(() =>
            {
                try
                {
                    RecordingManager.Instance.StopRecording();
                    RecordingManager.Instance.WaitForRecordingToFinish();
                }
                catch (Exception ex)
                {
                    AppLogger.LogError("Error stopping recording before quit", ex);
                }
            });
            Current?.Shutdown();
            if (shutdownWindows) System.Diagnostics.Process.Start("shutdown", "/s /t 0");
        }

        private static Assembly? OnAssemblyResolve(AssemblyLoadContext context, AssemblyName name)
        {
            string assemblyName = name.Name + ".dll";
            
            string codecFolderName = Environment.Is64BitProcess ? "Codec" : "Codec_x86";
            
            // Define all paths to check
            string[] searchPaths = new[]
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "libs", assemblyName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, codecFolderName, assemblyName)
            };

            foreach (var path in searchPaths)
            {
                if (File.Exists(path))
                {
                    return context.LoadFromAssemblyPath(path);
                }
            }

            return null;
        }
    
        protected override void OnStartup(StartupEventArgs e)
        {
            // Check for single instance - prevent app reentry (FIRST THING!)
            // Using initiallyOwned: false ensures mutex is auto-released if process is killed by Task Manager
            const string mutexName = "UltimateKtv_SingleInstanceMutex";
            bool createdNew;
            
            _instanceMutex = new Mutex(false, mutexName, out createdNew);
            
            // Try to acquire the mutex with zero timeout
            // If we can't acquire it, another instance is running
            if (!createdNew || !_instanceMutex.WaitOne(0, false))
            {
                // Another instance is already running
                MessageBox.Show(
                    "UltimateKTV 已經在執行中！\n\nAnother instance of UltimateKTV is already running!",
                    "UltimateKTV",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                
                // Clean up and shutdown
                if (_instanceMutex != null)
                {
                    _instanceMutex.Close();
                    _instanceMutex = null;
                }
                Current.Shutdown();
                return;
            }

            // Show splash screen as soon as possible after single instance check
            StartupSplashScreen = new SplashScreen("logo.jpg");
            StartupSplashScreen.Show(false);

            base.OnStartup(e);
            AppLogger.Log("Application starting up.");
            ResourceMonitor.Start();

            // 介面語言要在任何訊息/視窗出現前載入 (MainWindow 建構時 DynamicResource 即可直接取得字串)
            SettingsManager.Instance.LoadSettings();
            string uiLanguage = SettingsManager.Instance.CurrentSettings.UiLanguage;
            if (!Loc.Initialize(uiLanguage, out var languageProblems))
            {
                // 語言檔有問題 → 維持繁體中文，主視窗出現後再提示
                string message = Loc.FormatProblems(uiLanguage, languageProblems);
                Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
                    MessageBox.Show(message, Loc.T("Common.Hint"), MessageBoxButton.OK, MessageBoxImage.Warning)));
            }

            // Perform essential file checks before proceeding
            if (!CheckRequiredFiles())
            {
                // If checks fail, the app will have already shown a message and will shut down.
                return;
            }

            // Add handler for UI thread exceptions
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;

            // Add handler for background task exceptions
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            // Add handler for process exit
            this.Exit += App_Exit;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            AppLogger.LogError("An unhandled UI exception occurred.", e.Exception);
            MessageBox.Show($"An unexpected error occurred and was logged. The application will now close.\n\nError: {e.Exception.Message}", "Unhandled Error", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
            Application.Current.Shutdown();
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            AppLogger.LogError("An unhandled background task exception occurred.", e.Exception);
            e.SetObserved();
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            try
            {
                // StopRecording was already called in App.QuitAsync before Shutdown().
                // WaitForRecordingToFinish here is a safety net (e.g. if closed by Task Manager).
                RecordingManager.Instance.StopRecording();    // no-op if already stopped
                RecordingManager.Instance.WaitForRecordingToFinish();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("Error stopping recording on exit", ex);
            }

            // Clear pre-loading cache on shutdown
            if (Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ClearPreLoadingCacheOnShutdown();
            }
            
            // Release the single instance mutex
            if (_instanceMutex != null)
            {
                _instanceMutex.ReleaseMutex();
                _instanceMutex.Close();
                _instanceMutex = null;
            }
            
            ResourceMonitor.Stop();
            AppLogger.Log("Application shutting down.");
        }

        /// <summary>
        /// Verifies that all required external files and directories are present.
        /// If a required file is missing, it shows an error message and shuts down the application.
        /// </summary>
        /// <returns>True if all files are found, false otherwise.</returns>
        private bool CheckRequiredFiles()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string codecFolderName = Environment.Is64BitProcess ? "Codec" : "Codec_x86";
            string codecDirectory = Path.Combine(baseDirectory, codecFolderName);

            var requiredFiles = new[]
            {
                Path.Combine(codecDirectory, "CrazyKTV_MediaKit.dll"),
                Path.Combine(codecDirectory, "DirectShowLib-2005.dll")
            };

            // 1. Check for the Codec directory itself
            if (!Directory.Exists(codecDirectory) || !Directory.EnumerateFileSystemEntries(codecDirectory).Any())
            {
                string errorMessage = Loc.T("App.CodecMissing");
                AppLogger.LogError(errorMessage, null);
                StartupSplashScreen?.Close(TimeSpan.Zero);
                MessageBox.Show(errorMessage, "Missing Components", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
                return false;
            }

            // 2. Check for specific required DLLs inside the Codec directory
            foreach (var file in requiredFiles)
            {
                if (!File.Exists(file))
                {
                    string errorMessage = Loc.T("App.FileMissing", Path.GetFileName(file));
                    AppLogger.LogError(errorMessage, null);
                    StartupSplashScreen?.Close(TimeSpan.Zero);
                    MessageBox.Show(errorMessage, Loc.T("App.FileMissingTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return false;
                }
            }

            return true;
        }
    }
}
