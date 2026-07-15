using System.Reflection;

namespace DisplaySystemTray;

/// <summary>
/// Owns the tray icon and its menu. Left-click and right-click open the same menu.
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly MethodInfo? ShowContextMenuMethod =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly NotifyIcon _trayIcon;
    private readonly ContextMenuStrip _menu;

    public TrayApplicationContext()
    {
        _menu = BuildMenu();
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application, // placeholder until the custom icon lands in M5
            Text = "DisplaySystemTray",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _trayIcon.MouseUp += OnTrayMouseUp;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Extend", null, (_, _) => NotYetImplemented("Extend")));
        menu.Items.Add(new ToolStripMenuItem("Show only on 1", null, (_, _) => NotYetImplemented("Show only on 1")));
        menu.Items.Add(new ToolStripMenuItem("Show only on 2", null, (_, _) => NotYetImplemented("Show only on 2")));
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
        if (ShowContextMenuMethod is not null)
        {
            ShowContextMenuMethod.Invoke(_trayIcon, null);
        }
        else
        {
            _menu.Show(Cursor.Position);
        }
    }

    private void NotYetImplemented(string feature)
    {
        _trayIcon.ShowBalloonTip(3000, "DisplaySystemTray", $"{feature} is not implemented yet.", ToolTipIcon.Info);
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
