using System.Reflection;
using DisplaySystemTray.Display;

namespace DisplaySystemTray;

/// <summary>
/// Owns the tray icon and its menu. Left-click and right-click open the same menu.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private static MethodInfo? _showContextMenuMethod = ResolveShowContextMenu();

    private static MethodInfo? ResolveShowContextMenu()
    {
        // Reflection into WinForms internals must never be able to take the app
        // down - resolve defensively and fall back to ContextMenuStrip.Show.
        try
        {
            return typeof(NotifyIcon).GetMethod(
                "ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic, Type.EmptyTypes);
        }
        catch (Exception ex) when (ex is AmbiguousMatchException or ArgumentException)
        {
            return null;
        }
    }

    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;

    public TrayApplicationContext()
    {
        _menu = BuildMenu();
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // placeholder until the custom icon lands in M5
            Text = Program.AppName,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _trayIcon.MouseUp += OnTrayMouseUp;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Extend", null, (_, _) => ApplyMode(DisplayMode.Extend)));
        menu.Items.Add(new ToolStripMenuItem("Show only on 1", null, (_, _) => ApplyMode(DisplayMode.Internal)));
        menu.Items.Add(new ToolStripMenuItem("Show only on 2", null, (_, _) => ApplyMode(DisplayMode.External)));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => NotYetImplemented("Settings")));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));
        return menu;
    }

    private void OnTrayMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        // NotifyIcon only auto-opens ContextMenuStrip on right-click. Invoking the
        // same internal path for left-click keeps positioning and click-away
        // dismissal identical; the fallback lacks proper dismissal but still works.
        if (_showContextMenuMethod is { } showContextMenu)
        {
            try
            {
                showContextMenu.Invoke(_trayIcon, null);
                return;
            }
            catch (Exception)
            {
                // WinForms internals changed shape; stop using reflection this run.
                _showContextMenuMethod = null;
            }
        }

        _menu.Show(Cursor.Position);
    }

    private void ApplyMode(DisplayMode mode)
    {
        try
        {
            DisplayTopology.Apply(mode);
        }
        catch (Exception ex)
        {
            // First arg (timeout) has been ignored by Windows since Vista; pass 0.
            _trayIcon.ShowBalloonTip(0, Program.AppName, $"Could not switch displays: {ex.Message}", ToolTipIcon.Error);
        }
    }

    private void NotYetImplemented(string feature)
    {
        _trayIcon.ShowBalloonTip(0, Program.AppName, $"{feature} is not implemented yet.", ToolTipIcon.Info);
    }

    /// <summary>
    /// Best-effort tray icon removal when the process is about to die. May be
    /// called from a non-UI thread; failures are irrelevant at that point.
    /// </summary>
    public void PrepareForFatalExit()
    {
        try
        {
            _trayIcon.Visible = false;
        }
        catch
        {
            // Process is terminating; nothing useful to do.
        }
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }
}
