using BTChargeIndicator.Localization;

namespace BTChargeIndicator.UI;

internal static class TextPrompt
{
    public static string? Show(string title, string label, string initialValue)
    {
        using var icon = AppBranding.CreateIcon();
        using var form = new Form
        {
            Text = title,
            Icon = icon,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(440, 140),
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false
        };

        var promptLabel = new Label
        {
            Text = label,
            AutoSize = true,
            MaximumSize = new Size(416, 0),
            Location = new Point(12, 12)
        };
        var textBox = new TextBox
        {
            Text = initialValue,
            Location = new Point(12, 56),
            Size = new Size(416, 23)
        };
        var okButton = new Button
        {
            Text = AppText.Get("Save"),
            DialogResult = DialogResult.OK,
            Location = new Point(252, 98),
            Size = new Size(84, 28)
        };
        var cancelButton = new Button
        {
            Text = AppText.Get("Cancel"),
            DialogResult = DialogResult.Cancel,
            Location = new Point(344, 98),
            Size = new Size(84, 28)
        };

        form.Controls.AddRange([promptLabel, textBox, okButton, cancelButton]);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        form.Shown += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };

        return form.ShowDialog() == DialogResult.OK
            ? textBox.Text.Trim()
            : null;
    }
}
