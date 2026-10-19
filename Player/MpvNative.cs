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
