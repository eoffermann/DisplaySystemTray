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

    /// <summary>
    /// Pure query: true only when autostart points at THIS executable. A value
    /// registered to some other path (the exe moved, or this is a stray copy)
    /// reads as disabled, so the user re-enables explicitly and the write happens
    /// on that action - never as a silent side effect of opening Settings, which
    /// would let any transiently-run copy capture logon persistence.
    /// </summary>
    public static bool IsEnabled()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(Program.AppName) is string registered
            && string.Equals(registered, RegistrationValue, StringComparison.OrdinalIgnoreCase);
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
