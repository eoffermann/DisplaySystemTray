using System.Security.Principal;

namespace DisplaySystemTray;

internal static class Program
{

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return Cli.Run(args);
        }

        // DPI awareness and visual styles must be configured before ANY UI is
        // shown, including the "already running" dialog below.
        ApplicationConfiguration.Initialize();

        using Mutex? singleInstance = TryAcquireSingleInstance();
        if (singleInstance is null)
        {
            MessageBox.Show(
                "DisplaySystemTray is already running. Look for its icon in the system tray.",
                "DisplaySystemTray",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 0;
        }

        // A tray app has no console; without these handlers an exception would
        // silently kill the process and the icon would just vanish.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => ShowFatalError(e.ExceptionObject as Exception);

        using var context = new TrayApplicationContext();
        Application.Run(context);

        try
        {
            singleInstance.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned on this thread (e.g. abandoned-mutex acquisition edge); the
            // handle close below releases it anyway.
        }

        return 0;
    }

    /// <summary>
    /// Acquires the per-user single-instance mutex, or returns null if another
    /// instance is running (or the name is inaccessible).
    /// </summary>
    private static Mutex? TryAcquireSingleInstance()
    {
        // Session-local (Local\) and per-user-SID name: processes in other sessions
        // or sandboxes cannot collide with - or squat - this user's mutex.
        string userSid;
        using (var identity = WindowsIdentity.GetCurrent())
        {
            userSid = identity.User?.Value ?? identity.Name.Replace('\\', '_');
        }

        Mutex? mutex = null;
        try
        {
            mutex = new Mutex(initiallyOwned: false, $@"Local\DisplaySystemTray_{userSid}");

            // A short wait (instead of a createdNew snapshot) closes the race where
            // a quick exit-and-relaunch sees the dying previous instance and quits,
            // leaving the user with no instance at all.
            if (mutex.WaitOne(TimeSpan.FromSeconds(2)))
            {
                return mutex;
            }

            mutex.Dispose();
            return null;
        }
        catch (AbandonedMutexException)
        {
            // Previous instance died without releasing; the wait succeeded and we own it.
            return mutex;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or WaitHandleCannotBeOpenedException or IOException)
        {
            // The name exists but cannot be opened (squatted, or a different object
            // type). Treat as "already running" instead of crashing at startup.
            mutex?.Dispose();
            return null;
        }
    }

    private static void ShowFatalError(Exception? ex)
    {
        MessageBox.Show(
            $"DisplaySystemTray hit an unexpected error:\n\n{ex?.Message ?? "(unknown)"}",
            "DisplaySystemTray",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
