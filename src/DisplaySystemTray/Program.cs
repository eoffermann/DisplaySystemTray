namespace DisplaySystemTray;

internal static class Program
{
    private const string SingleInstanceMutexName = "DisplaySystemTray_SingleInstance";

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

        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
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
        return 0;
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
