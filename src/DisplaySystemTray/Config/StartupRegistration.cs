using Microsoft.Win32;

namespace DisplaySystemTray.Config;

/// <summary>
/// Manages the per-user "start with Windows" registration via the HKCU Run key.
/// No admin rights required; the value is the quoted path of this executable.
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    // Quoted so a path with spaces cannot be misparsed (or hijacked) at logon.
    private static string RegistrationValue =>
        $"\"{Environment.ProcessPath ?? Application.ExecutablePath}\"";

    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(Program.AppName) is not string registered)
        {
            return false;
        }

        // Self-heal: this is an "unzip anywhere" app, so a moved exe would leave
        // a stale path that silently fails at logon while the checkbox still
        // reads enabled. Re-point the value at the running executable.
        if (!string.Equals(registered, RegistrationValue, StringComparison.OrdinalIgnoreCase))
        {
            key.SetValue(Program.AppName, RegistrationValue);
        }

        return true;
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        if (enabled)
        {
            key.SetValue(Program.AppName, RegistrationValue);
        }
        else
        {
            key.DeleteValue(Program.AppName, throwOnMissingValue: false);
        }
    }
}
