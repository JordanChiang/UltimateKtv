using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace UltimateKtv
{
    /// <summary>
    /// 以 D3D9Ex 可鎖定渲染目標當 D3DImage 的後備緩衝，把 CPU 記憶體中的畫面送進 WPF。
    /// 與 CrazyKTV_MediaKit 的 DirectShow 預覽同樣走 D3DImage：實測每格更新時 UI 輸入延遲約 0ms；
    /// InteropBitmap.Invalidate 每格更新則會讓 UI 執行緒卡在等渲染執行緒，輸入延遲累積到數秒。
    /// 只用到少數 D3D9 COM 方法，直接以 vtable 呼叫，不另外加套件。
    /// </summary>
    internal sealed class D3DImageSurface : IDisposable
    {
        private readonly int _width, _height;
        private IntPtr _d3d, _device, _surface;
        private readonly HwndSource _hiddenWindow; // D3D9 裝置需要一個視窗 handle

        public D3DImage Image { get; } = new D3DImage();

        private D3DImageSurface(int width, int height, HwndSource hiddenWindow)
        {
            _width = width;
            _height = height;
            _hiddenWindow = hiddenWindow;
        }

        /// <summary>建立失敗 (沒有 D3D9Ex、遠端桌面等) 時回傳 null，呼叫端改用其他方式顯示。</summary>
        public static D3DImageSurface? TryCreate(int width, int height)
        {
            var hwnd = new HwndSource(new HwndSourceParameters("UltimateKtvPreviewD3D") { Width = 1, Height = 1, WindowStyle = 0 });
            var s = new D3DImageSurface(width, height, hwnd);
            try
            {
                s.CreateDevice();
                s.CreateSurface();
                s.Image.IsFrontBufferAvailableChanged += s.OnFrontBufferAvailableChanged;
                s.AttachBackBuffer();
                return s;
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[mpv-preview] D3DImage surface unavailable; falling back to InteropBitmap", ex);
                s.Dispose();
                return null;
            }
        }

        /// <summary>把 src (B,G,R,X 每像素 4 bytes) 複製到表面並通知 WPF 更新。必須在 UI 執行緒呼叫。</summary>
        public void Present(IntPtr src, int srcStride)
        {
            if (_surface == IntPtr.Zero || !Image.IsFrontBufferAvailable) return;
            Image.Lock();
            try
            {
                var locked = new D3DLOCKED_RECT();
                if (Call<LockRectFn>(_surface, 13)(_surface, ref locked, IntPtr.Zero, 0) < 0) return;
                try
                {
                    int rowBytes = _width * 4;
                    for (int y = 0; y < _height; y++)
                        CopyMemory(locked.pBits + y * locked.Pitch, src + y * srcStride, (UIntPtr)(uint)rowBytes);
                }
                finally
                {
                    Call<UnlockRectFn>(_surface, 14)(_surface);
                }
                Image.AddDirtyRect(new Int32Rect(0, 0, _width, _height));
            }
            finally
            {
                Image.Unlock();
            }
        }

        private void CreateDevice()
        {
            Check(Direct3DCreate9Ex(D3D_SDK_VERSION, out _d3d), "Direct3DCreate9Ex");
            var pp = new D3DPRESENT_PARAMETERS
            {
                BackBufferWidth = 1, BackBufferHeight = 1, BackBufferFormat = 0, BackBufferCount = 1,
                SwapEffect = 1 /* DISCARD */, hDeviceWindow = _hiddenWindow.Handle, Windowed = 1,
                PresentationInterval = 0x80000000 /* IMMEDIATE */
            };
            // IDirect3D9Ex::CreateDeviceEx (vtable 20)
            Check(Call<CreateDeviceExFn>(_d3d, 20)(_d3d, 0, 1 /* HAL */, _hiddenWindow.Handle,
                0x40 | 0x04 | 0x02 /* HARDWARE_VERTEXPROCESSING | MULTITHREADED | FPU_PRESERVE */,
                ref pp, IntPtr.Zero, out _device), "CreateDeviceEx");
        }

        private void CreateSurface()
        {
            // IDirect3DDevice9::CreateRenderTarget (vtable 28)，X8R8G8B8、可鎖定
            Check(Call<CreateRenderTargetFn>(_device, 28)(_device, (uint)_width, (uint)_height, 22 /* D3DFMT_X8R8G8B8 */,
                0, 0, 1, out _surface, IntPtr.Zero), "CreateRenderTarget");
        }

        private void AttachBackBuffer()
        {
            Image.Lock();
            Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, _surface);
            Image.Unlock();
        }

        private void OnFrontBufferAvailableChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // 裝置遺失 (鎖定畫面、切換顯示卡等) 後恢復：重新掛上後備緩衝
            if (Image.IsFrontBufferAvailable && _surface != IntPtr.Zero) AttachBackBuffer();
        }

        public void Dispose()
        {
            Image.IsFrontBufferAvailableChanged -= OnFrontBufferAvailableChanged;
            if (_surface != IntPtr.Zero)
            {
                Image.Lock();
                Image.SetBackBuffer(D3DResourceType.IDirect3DSurface9, IntPtr.Zero);
                Image.Unlock();
                Marshal.Release(_surface);
                _surface = IntPtr.Zero;
            }
            if (_device != IntPtr.Zero) { Marshal.Release(_device); _device = IntPtr.Zero; }
            if (_d3d != IntPtr.Zero) { Marshal.Release(_d3d); _d3d = IntPtr.Zero; }
            _hiddenWindow.Dispose();
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0) throw new InvalidOperationException($"{what} failed: 0x{hr:X8}");
        }

        private static T Call<T>(IntPtr comObject, int slot) where T : Delegate
        {
            IntPtr vtable = Marshal.ReadIntPtr(comObject);
            return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(vtable, slot * IntPtr.Size));
        }

        private const uint D3D_SDK_VERSION = 32;

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DPRESENT_PARAMETERS
        {
            public uint BackBufferWidth, BackBufferHeight, BackBufferFormat, BackBufferCount;
            public uint MultiSampleType, MultiSampleQuality, SwapEffect;
            public IntPtr hDeviceWindow;
            public int Windowed, EnableAutoDepthStencil;
            public uint AutoDepthStencilFormat, Flags, FullScreen_RefreshRateInHz, PresentationInterval;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct D3DLOCKED_RECT
        {
            public int Pitch;
            public IntPtr pBits;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateDeviceExFn(IntPtr self, uint adapter, int deviceType, IntPtr focusWindow, uint behaviorFlags,
            ref D3DPRESENT_PARAMETERS pp, IntPtr fullscreenMode, out IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int CreateRenderTargetFn(IntPtr self, uint width, uint height, uint format, uint multiSample,
            uint multiSampleQuality, int lockable, out IntPtr surface, IntPtr sharedHandle);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LockRectFn(IntPtr self, ref D3DLOCKED_RECT locked, IntPtr rect, uint flags);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int UnlockRectFn(IntPtr self);

        [DllImport("d3d9.dll")]
        private static extern int Direct3DCreate9Ex(uint sdkVersion, out IntPtr d3d);

        [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
        private static extern void CopyMemory(IntPtr dest, IntPtr src, UIntPtr count);

    }
}
