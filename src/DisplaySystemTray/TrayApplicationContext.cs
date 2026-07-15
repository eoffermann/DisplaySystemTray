using System.Reflection;
using DisplaySystemTray.Config;
using DisplaySystemTray.Display;
using DisplaySystemTray.UI;

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
    private readonly ConfigStore _store;

    public TrayApplicationContext(ConfigStore store)
    {
        _store = store;
        _store.Changed += (_, _) => RebuildMenu();

        _menu = new ContextMenuStrip();
        // Reload right before showing so configurations saved by another process
        // (the CLI, or a second session) appear without restarting the tray app.
        _menu.Opening += (_, _) =>
        {
            _store.ReloadIfChangedExternally();
            RebuildMenu();
        };
        RebuildMenu();

        _trayIcon = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = Program.AppName,
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _trayIcon.MouseUp += OnTrayMouseUp;

        if (_store.LoadWarning is { } warning)
        {
            _trayIcon.ShowBalloonTip(0, Program.AppName, warning, ToolTipIcon.Warning);
        }
    }

    private static Icon LoadTrayIcon()
    {
        // The .ico is embedded so the single-file exe needs no loose resources.
        // Icon(Stream, Size) picks the best-matching image for the tray's DPI.
        try
        {
            using Stream? stream = typeof(TrayApplicationContext).Assembly
                .GetManifestResourceStream("DisplaySystemTray.Resources.app.ico");
            if (stream is not null)
            {
                return new Icon(stream, SystemInformation.SmallIconSize);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // Malformed/missing resource; fall through to the stock icon.
        }

        return SystemIcons.Application;
    }

    private void RebuildMenu()
    {
        _menu.Items.Clear();
        _menu.Items.Add(new ToolStripMenuItem("Extend", null, (_, _) => ApplyMode(DisplayMode.Extend)));
        _menu.Items.Add(new ToolStripMenuItem("Show only on 1", null, (_, _) => ApplyMode(DisplayMode.Internal)));
        _menu.Items.Add(new ToolStripMenuItem("Show only on 2", null, (_, _) => ApplyMode(DisplayMode.External)));
        _menu.Items.Add(new ToolStripSeparator());

        if (_store.Config.Configurations.Count == 0)
        {
            _menu.Items.Add(new ToolStripMenuItem("(no saved configurations)") { Enabled = false });
        }
        else
        {
            foreach (SavedConfiguration saved in _store.Config.Configurations)
            {
                SavedConfiguration captured = saved;
                var item = new ToolStripMenuItem(saved.Name, null, (_, _) => ApplySaved(captured))
                {
                    ToolTipText = saved.MonitorNames.Count > 0
                        ? $"{string.Join(", ", saved.MonitorNames)} — saved {saved.CapturedAt:g}"
                        : $"saved {saved.CapturedAt:g}",
                };
                _menu.Items.Add(item);
            }
        }

        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication()));
    }

    private SettingsForm? _settingsForm;

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }

            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_store);
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
    }

    private void ApplySaved(SavedConfiguration saved)
    {
        try
        {
            DisplayConfigSnapshot.Apply(saved);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _trayIcon.ShowBalloonTip(0, Program.AppName, ex.Message, ToolTipIcon.Error);
        }
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
