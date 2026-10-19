using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace UltimateKtv
{
    /// <summary>
    /// 給 libmpv 繪圖用的原生子視窗 (以 mpv 的 wid 選項嵌入)。
    /// HWND 在整個程式生命週期只建立一次：WPF 把元素搬到別的視窗時只做 SetParent，
    /// 不會銷毀視窗，因此 mpv 不需重建影像輸出。
    /// 限制 (HwndHost airspace)：VisualBrush 預覽抓不到畫面，WPF 元素也無法疊在影片上。
    /// </summary>
    internal sealed class MpvVideoHost : HwndHost
    {
        private const string ClassName = "UltimateKtvMpvHost";
        private const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;
        private const int WM_NCHITTEST = 0x0084, HTTRANSPARENT = -1;
        private const int BLACK_BRUSH = 4;

        private static WndProcDelegate? _wndProc; // 保持參考，避免被 GC
        private static IntPtr _parkingWindow;

        public IntPtr VideoHwnd { get; }

        public MpvVideoHost()
        {
            EnsureClassAndParking();
            VideoHwnd = CreateWindowEx(0, ClassName, "", WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                0, 0, 16, 16, _parkingWindow, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (VideoHwnd == IntPtr.Zero)
                throw new InvalidOperationException($"CreateWindowEx failed: {Marshal.GetLastWin32Error()}");
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            SetParent(VideoHwnd, hwndParent.Handle);
            return new HandleRef(this, VideoHwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            // 不銷毀，停到隱藏的 parking 視窗，之後可再接回
            SetParent(VideoHwnd, _parkingWindow);
        }

        private static void EnsureClassAndParking()
        {
            if (_parkingWindow != IntPtr.Zero) return;

            _wndProc = WndProc;
            var wc = new WNDCLASSEX
            {
                cbSize = Marshal.SizeOf<WNDCLASSEX>(),
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                hbrBackground = GetStockObject(BLACK_BRUSH),
                lpszClassName = ClassName
            };
            RegisterClassEx(ref wc);

            _parkingWindow = CreateWindowEx(0, ClassName, "", WS_POPUP, 0, 0, 16, 16,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
        {
            // 讓滑鼠/觸控事件穿透到 WPF 父視窗 (游標自動隱藏、點擊等)
            if (msg == WM_NCHITTEST) return new IntPtr(HTTRANSPARENT);
            return DefWindowProc(hwnd, msg, wParam, lParam);
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate IntPtr WndProcDelegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WNDCLASSEX
        {
            public int cbSize;
            public int style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string? lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style,
            int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("gdi32.dll")]
        private static extern IntPtr GetStockObject(int obj);
    }
}
