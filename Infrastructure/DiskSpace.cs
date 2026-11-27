using System;
using System.IO;

namespace UltimateKtv
{
    /// <summary>
    /// 磁碟剩餘空間檢查：錄音與 YouTube 下載在目的磁碟剩餘空間不到 <see cref="MinFreeGB"/> GB 時不動作，
    /// 避免把系統碟塞滿。
    /// </summary>
    internal static class DiskSpace
    {
        public const int MinFreeGB = 10;
        private const double BytesPerGB = 1024.0 * 1024.0 * 1024.0;
        private const long MinFreeBytes = MinFreeGB * 1024L * 1024 * 1024;

        /// <summary>
        /// path (檔案或資料夾的完整路徑) 所在磁碟剩餘空間不到 <see cref="MinFreeGB"/> GB 時回傳 true。
        /// 查不到 (例如網路路徑) 時寫 log 並視為足夠，不擋使用者。
        /// </summary>
        /// <param name="logTag">查詢失敗時 log 的前綴 (例如 Recording、YouTube)。</param>
        public static bool IsLow(string path, string logTag, out string drive, out double freeGB)
        {
            drive = Path.GetPathRoot(path) ?? path;
            freeGB = 0;
            try
            {
                long freeBytes = new DriveInfo(drive).AvailableFreeSpace;
                freeGB = freeBytes / BytesPerGB;
                return freeBytes < MinFreeBytes;
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[{logTag}] Could not check disk space for '{path}', proceeding anyway.", ex);
                return false;
            }
        }
    }

    /// <summary>目的磁碟剩餘空間不足 (<see cref="DiskSpace.MinFreeGB"/> GB)，所以沒有執行。Message 是給使用者看的文字。</summary>
    public sealed class LowDiskSpaceException : IOException
    {
        public LowDiskSpaceException(string message) : base(message) { }
    }
}
