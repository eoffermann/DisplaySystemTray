using Microsoft.Win32;

namespace DisplaySystemTray.Config;

/// <summary>
/// Manages the per-user "start with Windows" registration via the HKCU Run key.
/// No admin rights required; the value is the quoted path of this executable.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(Program.AppName) is string;
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            // Quote the exact path so a path with spaces cannot be misparsed
            // (or hijacked) at logon.
            key.SetValue(Program.AppName, $"\"{Application.ExecutablePath}\"");
        }
        else
        {
            key.DeleteValue(Program.AppName, throwOnMissingValue: false);
        }
    }
}
