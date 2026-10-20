using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UltimateKtv
{
    /// <summary>
    /// libmpv C API 的最小封裝。以 NativeLibrary 動態載入，DLL 不存在時不影響程式 (IsAvailable = false)。
    /// DLL 搜尋順序：Codec (x64) / Codec_x86 (x86) 資料夾，再來是程式目錄。
    /// </summary>
    internal static class MpvNative
    {
        private static readonly string[] DllNames = { "libmpv-2.dll", "mpv-2.dll", "mpv-1.dll" };

        public const int FORMAT_STRING = 1;
        public const int FORMAT_FLAG = 3;
        public const int FORMAT_INT64 = 4;
        public const int FORMAT_DOUBLE = 5;

        public const int EVENT_SHUTDOWN = 1;
        public const int EVENT_START_FILE = 6;
        public const int EVENT_END_FILE = 7;
        public const int EVENT_FILE_LOADED = 8;
        public const int EVENT_PROPERTY_CHANGE = 22;

        public const int END_FILE_REASON_EOF = 0;
        public const int END_FILE_REASON_STOP = 2;
        public const int END_FILE_REASON_QUIT = 3;
        public const int END_FILE_REASON_ERROR = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_event
        {
            public int event_id;
            public int error;
            public ulong reply_userdata;
            public IntPtr data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_event_property
        {
            public IntPtr name;
            public int format;
            public IntPtr data;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_event_end_file
        {
            public int reason;
            public int error;
            public long playlist_entry_id;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr CreateFn();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int InitializeFn(IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void TerminateDestroyFn(IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int SetOptionStringFn(IntPtr ctx, IntPtr name, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int SetPropertyStringFn(IntPtr ctx, IntPtr name, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int GetPropertyFn(IntPtr ctx, IntPtr name, int format, IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr GetPropertyStringFn(IntPtr ctx, IntPtr name);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int CommandFn(IntPtr ctx, IntPtr args);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int ObservePropertyFn(IntPtr ctx, ulong reply, IntPtr name, int format);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr WaitEventFn(IntPtr ctx, double timeout);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void WakeupFn(IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void FreeFn(IntPtr data);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate IntPtr ErrorStringFn(int error);

        // ---- render API (軟體渲染，給主視窗預覽用) ----
        public const int RENDER_PARAM_INVALID = 0;
        public const int RENDER_PARAM_API_TYPE = 1;
        public const int RENDER_PARAM_BLOCK_FOR_TARGET_TIME = 12;
        public const int RENDER_PARAM_SW_SIZE = 17;
        public const int RENDER_PARAM_SW_FORMAT = 18;
        public const int RENDER_PARAM_SW_STRIDE = 19;
        public const int RENDER_PARAM_SW_POINTER = 20;
        public const ulong RENDER_UPDATE_FRAME = 1;

        [StructLayout(LayoutKind.Sequential)]
        public struct mpv_render_param
        {
            public int type;
            public IntPtr data;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int RenderContextCreateFn(out IntPtr res, IntPtr mpv, IntPtr parameters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void RenderUpdateCallback(IntPtr cbCtx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void RenderContextSetUpdateCallbackFn(IntPtr ctx, RenderUpdateCallback? callback, IntPtr cbCtx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate ulong RenderContextUpdateFn(IntPtr ctx);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate int RenderContextRenderFn(IntPtr ctx, IntPtr parameters);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] public delegate void RenderContextFreeFn(IntPtr ctx);

        public static RenderContextCreateFn? RenderContextCreate;
        public static RenderContextSetUpdateCallbackFn? RenderContextSetUpdateCallback;
        public static RenderContextUpdateFn? RenderContextUpdate;
        public static RenderContextRenderFn? RenderContextRender;
        public static RenderContextFreeFn? RenderContextFree;

        /// <summary>render API 是否可用 (舊版 libmpv 可能沒有)。</summary>
        public static bool IsRenderApiAvailable { get; private set; }

        public static CreateFn? Create;
        public static InitializeFn? Initialize;
        public static TerminateDestroyFn? TerminateDestroy;
        public static SetOptionStringFn? SetOptionString;
        public static SetPropertyStringFn? SetPropertyString;
        public static GetPropertyFn? GetProperty;
        public static GetPropertyStringFn? GetPropertyString;
        public static CommandFn? Command;
        public static ObservePropertyFn? ObserveProperty;
        public static WaitEventFn? WaitEvent;
        public static WakeupFn? Wakeup;
        public static FreeFn? Free;
        public static ErrorStringFn? ErrorString;

        private static readonly object _loadLock = new();
        private static bool _loadAttempted;

        public static bool IsAvailable { get; private set; }
        public static string? LoadedPath { get; private set; }

        /// <summary>只要找得到 libmpv DLL 就回傳路徑 (不載入)，供設定頁顯示狀態。</summary>
        public static string? FindDll()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string codecDir = Path.Combine(baseDir, Environment.Is64BitProcess ? "Codec" : "Codec_x86");
            foreach (var dir in new[] { codecDir, baseDir })
            {
                foreach (var name in DllNames)
                {
                    string path = Path.Combine(dir, name);
                    if (File.Exists(path)) return path;
                }
            }
            return null;
        }

        public static bool EnsureLoaded()
        {
            lock (_loadLock)
            {
                if (_loadAttempted) return IsAvailable;
                _loadAttempted = true;

                string? path = FindDll();
                if (path == null)
                {
                    AppLogger.Log("[mpv] libmpv DLL not found (Codec / Codec_x86 / app folder).");
                    return false;
                }

                try
                {
                    if (!NativeLibrary.TryLoad(path, out IntPtr lib))
                    {
                        AppLogger.Log($"[mpv] Failed to load {path} (wrong bitness or missing dependency?).");
                        return false;
                    }

                    Create = Get<CreateFn>(lib, "mpv_create");
                    Initialize = Get<InitializeFn>(lib, "mpv_initialize");
                    TerminateDestroy = Get<TerminateDestroyFn>(lib, "mpv_terminate_destroy");
                    SetOptionString = Get<SetOptionStringFn>(lib, "mpv_set_option_string");
                    SetPropertyString = Get<SetPropertyStringFn>(lib, "mpv_set_property_string");
                    GetProperty = Get<GetPropertyFn>(lib, "mpv_get_property");
                    GetPropertyString = Get<GetPropertyStringFn>(lib, "mpv_get_property_string");
                    Command = Get<CommandFn>(lib, "mpv_command");
                    ObserveProperty = Get<ObservePropertyFn>(lib, "mpv_observe_property");
                    WaitEvent = Get<WaitEventFn>(lib, "mpv_wait_event");
                    Wakeup = Get<WakeupFn>(lib, "mpv_wakeup");
                    Free = Get<FreeFn>(lib, "mpv_free");
                    ErrorString = Get<ErrorStringFn>(lib, "mpv_error_string");

                    LoadedPath = path;
                    IsAvailable = true;
                    AppLogger.Log($"[mpv] Loaded {path}");

                    try
                    {
                        RenderContextCreate = Get<RenderContextCreateFn>(lib, "mpv_render_context_create");
                        RenderContextSetUpdateCallback = Get<RenderContextSetUpdateCallbackFn>(lib, "mpv_render_context_set_update_callback");
                        RenderContextUpdate = Get<RenderContextUpdateFn>(lib, "mpv_render_context_update");
                        RenderContextRender = Get<RenderContextRenderFn>(lib, "mpv_render_context_render");
                        RenderContextFree = Get<RenderContextFreeFn>(lib, "mpv_render_context_free");
                        IsRenderApiAvailable = true;
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError("[mpv] render API not available; main window preview disabled", ex);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.LogError($"[mpv] Failed to bind libmpv exports from {path}", ex);
                    IsAvailable = false;
                }
                return IsAvailable;
            }
        }

        private static T Get<T>(IntPtr lib, string name) where T : Delegate
        {
            return Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(lib, name));
        }

        public static string Error(int code)
        {
            if (ErrorString == null) return code.ToString();
            return $"{code} ({PtrToUtf8(ErrorString(code))})";
        }

        public static string? PtrToUtf8(IntPtr p) => p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);

        // ---------- 共用呼叫包裝 (主播放與預覽實例共用) ----------

        public static int SetOptionUtf8(IntPtr ctx, string name, string value)
        {
            IntPtr n = Utf8(name), v = Utf8(value);
            try { return SetOptionString!(ctx, n, v); }
            finally { Marshal.FreeCoTaskMem(n); Marshal.FreeCoTaskMem(v); }
        }

        // ---- 診斷：KTV_MPV_TRACE=1 時記錄超過 15ms 的同步呼叫 ----
        public static readonly bool Trace = Environment.GetEnvironmentVariable("KTV_MPV_TRACE") == "1";
        public static void TraceCall(string what, long startTicks)
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (ms > 15) AppLogger.Log($"[mpv-trace] {ms:F0}ms {what} thread={System.Threading.Thread.CurrentThread.Name ?? System.Threading.Thread.CurrentThread.ManagedThreadId.ToString()}");
        }

        public static int SetPropertyUtf8(IntPtr ctx, string name, string value)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            IntPtr n = Utf8(name), v = Utf8(value);
            try { return SetPropertyString!(ctx, n, v); }
            finally { Marshal.FreeCoTaskMem(n); Marshal.FreeCoTaskMem(v); if (Trace) TraceCall("set " + name, t0); }
        }

        public static string? GetPropertyUtf8(IntPtr ctx, string name)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            IntPtr n = Utf8(name);
            try
            {
                IntPtr p = GetPropertyString!(ctx, n);
                if (p == IntPtr.Zero) return null;
                string? s = PtrToUtf8(p);
                Free!(p);
                return s;
            }
            finally { Marshal.FreeCoTaskMem(n); if (Trace) TraceCall("get " + name, t0); }
        }

        public static int CommandUtf8(IntPtr ctx, params string[] args)
        {
            long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var ptrs = new IntPtr[args.Length + 1];
            IntPtr arr = IntPtr.Zero;
            try
            {
                for (int i = 0; i < args.Length; i++) ptrs[i] = Utf8(args[i]);
                arr = Marshal.AllocCoTaskMem(IntPtr.Size * ptrs.Length);
                Marshal.Copy(ptrs, 0, arr, ptrs.Length);
                return Command!(ctx, arr);
            }
            finally
            {
                foreach (var p in ptrs) if (p != IntPtr.Zero) Marshal.FreeCoTaskMem(p);
                if (arr != IntPtr.Zero) Marshal.FreeCoTaskMem(arr);
                if (Trace && args.Length > 0 && args[0] != "overlay-add") TraceCall("cmd " + args[0], t0);
            }
        }

        /// <summary>配置 UTF-8 字串，呼叫端負責 Marshal.FreeCoTaskMem。</summary>
        public static IntPtr Utf8(string s)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(s);
            IntPtr p = Marshal.AllocCoTaskMem(bytes.Length + 1);
            Marshal.Copy(bytes, 0, p, bytes.Length);
            Marshal.WriteByte(p, bytes.Length, 0);
            return p;
        }
    }
}
