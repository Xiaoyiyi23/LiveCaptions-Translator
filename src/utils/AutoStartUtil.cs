using Microsoft.Win32;

namespace LiveCaptionsTranslator.utils
{
    /// <summary>
    /// Registers/unregisters per-user autostart via the HKCU Run key.
    /// Autostart launches the app minimized to the tray.
    /// </summary>
    public static class AutoStartUtil
    {
        private const string RUN_KEY_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string VALUE_NAME = "LiveCaptionsTranslator";

        public static void SetEnabled(bool enabled)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(RUN_KEY_PATH, writable: true);
                if (enabled)
                    key?.SetValue(VALUE_NAME, $"\"{Environment.ProcessPath}\" --minimized");
                else
                    key?.DeleteValue(VALUE_NAME, throwOnMissingValue: false);
            }
            catch (Exception ex)
            {
                FileLogger.Error($"Failed to {(enabled ? "enable" : "disable")} autostart", ex);
            }
        }
    }
}
