using System;
using System.Configuration;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Reflection;

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

            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        private Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            string assemblyName = new AssemblyName(args.Name).Name + ".dll";
            
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
                    return Assembly.LoadFrom(path);
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
                string errorMessage = "'Codec' 目錄找不到或裏面是空的!!";
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
                    string errorMessage = $"必要的檔案 {Path.GetFileName(file)} 找不到!! ";
                    AppLogger.LogError(errorMessage, null);
                    StartupSplashScreen?.Close(TimeSpan.Zero);
                    MessageBox.Show(errorMessage, "檔案遺失", MessageBoxButton.OK, MessageBoxImage.Error);
                    Application.Current.Shutdown();
                    return false;
                }
            }

            return true;
        }
    }
}
