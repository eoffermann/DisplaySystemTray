using DisplaySystemTray.Config;
using DisplaySystemTray.Display;

namespace DisplaySystemTray.UI;

/// <summary>
/// Manages saved display configurations: save the current one under a name,
/// rename, re-capture (update from current), and delete. All mutations go
/// through the shared <see cref="ConfigStore"/>, which also drives the tray menu.
/// </summary>
internal sealed class SettingsForm : Form
{
    private readonly ConfigStore _store;
    private readonly ListView _list;
    private readonly Button _renameButton;
    private readonly Button _updateButton;
    private readonly Button _deleteButton;

    public SettingsForm(ConfigStore store)
    {
        _store = store;

        Text = $"{Program.AppName} Settings";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 340);
        Size = new Size(680, 400);

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            ShowItemToolTips = true,
        };
        _list.Columns.Add("Name", 200);
        _list.Columns.Add("Displays", 240);
        _list.Columns.Add("Saved", 150);
        _list.SelectedIndexChanged += (_, _) => UpdateButtonStates();
        _list.DoubleClick += (_, _) => RenameSelected();

        var saveButton = MakeButton("Save current as…", (_, _) => SaveCurrentAs());
        _renameButton = MakeButton("Rename…", (_, _) => RenameSelected());
        _updateButton = MakeButton("Update from current", (_, _) => UpdateFromCurrent());
        _deleteButton = MakeButton("Delete", (_, _) => DeleteSelected());
        var closeButton = MakeButton("Close", (_, _) => Close());

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right,
            FlowDirection = FlowDirection.TopDown,
            Width = 170,
            Padding = new Padding(8),
            WrapContents = false,
        };
        buttons.Controls.AddRange([saveButton, _renameButton, _updateButton, _deleteButton, closeButton]);

        Controls.Add(_list);
        Controls.Add(buttons);
        CancelButton = closeButton;

        _store.Changed += OnStoreChanged;
        RefreshList();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.Changed -= OnStoreChanged;
        }

        base.Dispose(disposing);
    }

    private static Button MakeButton(string text, EventHandler onClick)
    {
        var button = new Button { Text = text, Width = 150, Height = 32, Margin = new Padding(0, 0, 0, 8) };
        button.Click += onClick;
        return button;
    }

    private void OnStoreChanged(object? sender, EventArgs e) => RefreshList();

    private void RefreshList()
    {
        Guid? selectedId = SelectedConfiguration()?.Id;

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (SavedConfiguration saved in _store.Config.Configurations)
        {
            var item = new ListViewItem([
                saved.Name,
                string.Join(", ", saved.MonitorNames),
                saved.CapturedAt.ToString("g"),
            ])
            {
                Tag = saved,
                Selected = saved.Id == selectedId,
            };
            _list.Items.Add(item);
        }

        _list.EndUpdate();
        UpdateButtonStates();
    }

    private SavedConfiguration? SelectedConfiguration() =>
        _list.SelectedItems.Count > 0 ? _list.SelectedItems[0].Tag as SavedConfiguration : null;

    private void UpdateButtonStates()
    {
        bool hasSelection = _list.SelectedItems.Count > 0;
        _renameButton.Enabled = hasSelection;
        _updateButton.Enabled = hasSelection;
        _deleteButton.Enabled = hasSelection;
    }

    private void SaveCurrentAs()
    {
        using var dialog = new TextPromptDialog(
            "Save current configuration",
            "Name for the current display configuration:",
            $"Configuration {_store.Config.Configurations.Count + 1}");
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            _store.Add(DisplayConfigSnapshot.Capture(dialog.Value));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowError($"Could not capture the current configuration: {ex.Message}");
        }
    }

    private void RenameSelected()
    {
        if (SelectedConfiguration() is not { } saved)
        {
            return;
        }

        using var dialog = new TextPromptDialog("Rename configuration", "New name:", saved.Name);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.Value == saved.Name)
        {
            return;
        }

        try
        {
            saved.Name = dialog.Value;
            _store.Update(saved);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowError($"Could not rename: {ex.Message}");
        }
    }

    private void UpdateFromCurrent()
    {
        if (SelectedConfiguration() is not { } saved)
        {
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            $"Replace \"{saved.Name}\" with the current display configuration?",
            Program.AppName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            SavedConfiguration recaptured = DisplayConfigSnapshot.Capture(saved.Name);
            recaptured.Id = saved.Id; // same entry, new content
            _store.Update(recaptured);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowError($"Could not update from current: {ex.Message}");
        }
    }

    private void DeleteSelected()
    {
        if (SelectedConfiguration() is not { } saved)
        {
            return;
        }

        DialogResult confirm = MessageBox.Show(
            this,
            $"Delete \"{saved.Name}\"?",
            Program.AppName,
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            _store.Remove(saved.Id);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowError($"Could not delete: {ex.Message}");
        }
    }

    private void ShowError(string message) =>
        MessageBox.Show(this, message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
}
