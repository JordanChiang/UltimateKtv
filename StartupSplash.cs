using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace UltimateKtv
{
    /// <summary>
    /// 啟動畫面。執行目錄有 logo.jpg (使用者自行放置的自訂 logo) 就顯示它，否則用內嵌的 logo.jpg。
    /// WPF 的 SplashScreen 只能讀組件資源，外部檔改用相同做法的原生分層視窗：
    /// UpdateLayeredWindow 一次畫好，主視窗建構期間 UI 執行緒忙碌也不需要重繪。
    /// </summary>
    public sealed class StartupSplash
    {
        public const string LogoFileName = "logo.jpg";

        private SplashScreen? _builtIn;
        private IntPtr _hwnd;
        private IntPtr _memDc;
        private IntPtr _bitmap;
        private IntPtr _oldBitmap;
        private bool _closing;

        private StartupSplash() { }

        public static StartupSplash Show()
        {
            var splash = new StartupSplash();
            string customLogo = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LogoFileName);
            if (File.Exists(customLogo))
            {
                try
                {
                    splash.ShowLayered(customLogo);
                    return splash;
                }
                catch (Exception ex)
                {
                    splash.Release();
                    AppLogger.LogError($"無法顯示自訂啟動畫面 {customLogo}，改用內建 logo。", ex);
                }
            }

            splash._builtIn = new SplashScreen(LogoFileName);
            splash._builtIn.Show(false);
            return splash;
        }

        /// <summary>關閉啟動畫面；fadeout 期間逐漸淡出。重複呼叫無作用。</summary>
        public void Close(TimeSpan fadeout)
        {
            if (_closing) return;
            _closing = true;

            if (_builtIn != null)
            {
                _builtIn.Close(fadeout);
                return;
            }
            if (_hwnd == IntPtr.Zero) return;

            if (fadeout <= TimeSpan.Zero)
            {
                Release();
                return;
            }

            DateTime start = DateTime.UtcNow;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            timer.Tick += (_, _) =>
            {
                double progress = (DateTime.UtcNow - start).TotalMilliseconds / fadeout.TotalMilliseconds;
                if (progress >= 1)
                {
                    timer.Stop();
                    Release();
                    return;
                }
                var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = (byte)(255 * (1 - progress)), AlphaFormat = AC_SRC_ALPHA };
                UpdateLayeredWindow(_hwnd, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, ULW_ALPHA);
            };
            timer.Start();
        }

        private void ShowLayered(string path)
        {
            BitmapSource image;
            using (var stream = new MemoryStream(File.ReadAllBytes(path)))
            {
                image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            }

            // 比螢幕大的圖等比例縮小
            int screenWidth = GetSystemMetrics(SM_CXSCREEN);
            int screenHeight = GetSystemMetrics(SM_CYSCREEN);
            double scale = Math.Min(1.0, Math.Min((double)screenWidth / image.PixelWidth, (double)screenHeight / image.PixelHeight));
            if (scale < 1.0)
                image = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            image = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);

            int width = image.PixelWidth;
            int height = image.PixelHeight;
            int stride = width * 4;

            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // 負值 = 由上而下，與 WPF CopyPixels 一致
                biPlanes = 1,
                biBitCount = 32,
            };

            IntPtr screenDc = GetDC(IntPtr.Zero);
            try
            {
                _memDc = CreateCompatibleDC(screenDc);
                _bitmap = CreateDIBSection(screenDc, ref header, 0, out IntPtr bits, IntPtr.Zero, 0);
                if (_memDc == IntPtr.Zero || _bitmap == IntPtr.Zero)
                    throw new InvalidOperationException("CreateDIBSection 失敗");
                image.CopyPixels(Int32Rect.Empty, bits, stride * height, stride);
                _oldBitmap = SelectObject(_memDc, _bitmap);

                _hwnd = CreateWindowEx(WS_EX_LAYERED | WS_EX_TOOLWINDOW, "Static", "UltimateKTV", WS_POPUP,
                    0, 0, width, height, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                if (_hwnd == IntPtr.Zero)
                    throw new InvalidOperationException("CreateWindowEx 失敗");

                var position = new POINT { X = (screenWidth - width) / 2, Y = (screenHeight - height) / 2 };
                var size = new SIZE { cx = width, cy = height };
                var source = new POINT();
                var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
                if (!UpdateLayeredWindow(_hwnd, screenDc, ref position, ref size, _memDc, ref source, 0, ref blend, ULW_ALPHA))
                    throw new InvalidOperationException("UpdateLayeredWindow 失敗");
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
            }

            ShowWindow(_hwnd, SW_SHOWNOACTIVATE);
        }

        private void Release()
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_memDc != IntPtr.Zero)
            {
                if (_oldBitmap != IntPtr.Zero) SelectObject(_memDc, _oldBitmap);
                DeleteDC(_memDc);
                _memDc = IntPtr.Zero;
            }
            if (_bitmap != IntPtr.Zero)
            {
                DeleteObject(_bitmap);
                _bitmap = IntPtr.Zero;
            }
        }

        #region Win32
        private const int SM_CXSCREEN = 0;
        private const int SM_CYSCREEN = 1;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_EX_LAYERED = 0x00080000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int ULW_ALPHA = 2;
        private const byte AC_SRC_OVER = 0;
        private const byte AC_SRC_ALPHA = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [StructLayout(LayoutKind.Sequential)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName, int dwStyle,
            int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        // 只改透明度 (淡出) 時位置、大小、來源 DC 都傳 null
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, IntPtr psize,
            IntPtr hdcSrc, IntPtr pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER pbmi, uint usage,
            out IntPtr ppvBits, IntPtr hSection, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr ho);
        #endregion
    }
}
