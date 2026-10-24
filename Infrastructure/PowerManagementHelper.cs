using System;
using System.Runtime.InteropServices;

namespace UltimateKtv
{
    public static class PowerManagementHelper
    {
        // Define the execution state flags
        [Flags]
        public enum ExecutionState : uint
        {
            ES_AWAYMODE_REQUIRED = 0x00000040,
            ES_CONTINUOUS = 0x80000000,
            ES_DISPLAY_REQUIRED = 0x00000002,
            ES_SYSTEM_REQUIRED = 0x00000001
            // Legacy flag, should not be used.
            // ES_USER_PRESENT = 0x00000004
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

        /// <summary>
        /// Prevents the system from entering sleep mode and keeps the display on.
        /// </summary>
        public static void PreventSleep()
        {
            // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED
            // Prevents system sleep and display turn-off.
            SetThreadExecutionState(ExecutionState.ES_CONTINUOUS | ExecutionState.ES_SYSTEM_REQUIRED | ExecutionState.ES_DISPLAY_REQUIRED);
            AppLogger.Log("PowerManagement: Sleep mode prevented (System + Display).");
        }

        /// <summary>
        /// Allows the system to enter sleep mode normally.
        /// </summary>
        public static void RestoreSleep()
        {
            // ES_CONTINUOUS
            // Clears the flags, restoring default behavior.
            SetThreadExecutionState(ExecutionState.ES_CONTINUOUS);
            AppLogger.Log("PowerManagement: Sleep mode restored.");
        }
    }
}
