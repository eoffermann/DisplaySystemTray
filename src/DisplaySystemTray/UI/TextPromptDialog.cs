namespace DisplaySystemTray.UI;

/// <summary>Small modal dialog asking for a single line of text (e.g. a configuration name).</summary>
internal sealed class TextPromptDialog : Form
{
    private readonly TextBox _textBox;

    /// <summary>The entered text, trimmed.</summary>
    public string Value => _textBox.Text.Trim();

    public TextPromptDialog(string title, string prompt, string initialValue = "")
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(384, 116);

        var label = new Label
        {
            Text = prompt,
            Location = new Point(12, 12),
            AutoSize = true,
        };

        _textBox = new TextBox
        {
            Location = new Point(12, 36),
            Width = 360,
            Text = initialValue,
        };
        _textBox.SelectAll();

        var ok = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            Location = new Point(216, 76),
            Size = new Size(75, 28),
        };

        var cancel = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            Location = new Point(297, 76),
            Size = new Size(75, 28),
        };

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.AddRange([label, _textBox, ok, cancel]);

        // An empty name is never valid; block OK instead of letting callers deal with it.
        FormClosing += (_, e) =>
        {
            if (DialogResult == DialogResult.OK && Value.Length == 0)
            {
                e.Cancel = true;
                _textBox.Focus();
            }
        };
    }
}
