using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BTChargeIndicator.UI;

internal sealed class ConnectionToastForm : Form
{
    internal const int CardWidth = 372;
    internal const int CardHeight = 112;
    private const double Duration = 4.0;
    private readonly System.Windows.Forms.Timer _animationTimer = new() { Interval = 16 };
    private readonly Stopwatch _clock = new();
    private readonly bool _animate = ClientAnimationsEnabled();
    private Point _anchor;
    private bool _dismissed;
    private double _dismissedAt;

    public ConnectionNotification Notification { get; private set; }

    public ConnectionToastForm(ConnectionNotification notification)
    {
        Notification = notification;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(21, 21, 25);
        ClientSize = new Size(CardWidth, CardHeight);
        DoubleBuffered = true;
        Opacity = _animate ? 0 : 1;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);
        _animationTimer.Tick += (_, _) => AdvanceAnimation();
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00000080 | 0x08000000; // Tool window, no activation.
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021) // WM_MOUSEACTIVATE
        {
            message.Result = 3; // MA_NOACTIVATE: receive clicks without taking focus.
            return;
        }
        base.WndProc(ref message);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        PositionCard();
        _clock.Restart();
        _animationTimer.Start();
        AdvanceAnimation();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        PositionCard();
    }

    private void PositionCard()
    {
        var scale = DeviceDpi / 96f;
        ClientSize = new Size((int)Math.Round(CardWidth * scale), (int)Math.Round(CardHeight * scale));
        var area = Screen.FromControl(this).WorkingArea;
        var gap = (int)Math.Round(16 * scale);
        _anchor = new Point(area.Right - Width - gap, area.Bottom - Height - gap);
        Location = _anchor;
        using var outline = ConnectionToastRenderer.RoundedRectangle(new RectangleF(0, 0, Width, Height), 20 * scale);
        var previous = Region;
        Region = new Region(outline);
        previous?.Dispose();
    }

    public void UpdateNotification(ConnectionNotification notification)
    {
        Notification = notification;
        _dismissed = false;
        _clock.Restart();
        Invalidate();
    }

    private void AdvanceAnimation()
    {
        var elapsed = _clock.Elapsed.TotalSeconds;
        var ending = _dismissed ? elapsed - _dismissedAt : elapsed - (Duration - 0.28);
        if (ending >= 0.28) { Close(); return; }
        var enter = Math.Clamp(elapsed / 0.30, 0, 1);
        var exit = Math.Clamp(ending / 0.28, 0, 1);
        var eased = 1 - Math.Pow(1 - enter, 3);
        Opacity = _animate ? Math.Clamp(eased * (1 - exit), 0, 1) : 1;
        var offset = _animate ? (1 - eased) * 24 + exit * exit * 12 : 0;
        Location = new Point(_anchor.X, _anchor.Y + (int)Math.Round(offset * DeviceDpi / 96));
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = IsCloseButton(e.Location) ? Cursors.Hand : Cursors.Default;
    }

    private bool IsCloseButton(Point location) => location.X >= Width - 38 * DeviceDpi / 96 &&
                                                        location.Y <= 38 * DeviceDpi / 96;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left && IsCloseButton(e.Location) && !_dismissed)
        {
            _dismissed = true;
            _dismissedAt = _clock.Elapsed.TotalSeconds;
            if (!_animate) Close();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.ScaleTransform(DeviceDpi / 96f, DeviceDpi / 96f);
        var progress = _animate ? (float)Math.Clamp(_clock.Elapsed.TotalSeconds / 0.65, 0, 1) : 1;
        var remaining = (float)Math.Clamp(1 - _clock.Elapsed.TotalSeconds / Duration, 0, 1);
        ConnectionToastRenderer.Draw(e.Graphics, Notification, progress, remaining);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animationTimer.Dispose();
        base.Dispose(disposing);
    }

    private static bool ClientAnimationsEnabled()
    {
        var enabled = true;
        return !SystemParametersInfo(0x1042, 0, ref enabled, 0) || enabled;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint parameter,
        [MarshalAs(UnmanagedType.Bool)] ref bool value, uint flags);
}

internal static class ConnectionToastRenderer
{
    private static readonly Color Purple = Color.FromArgb(168, 85, 247);
    private static readonly Color Surface = Color.FromArgb(21, 21, 25);

    internal static Bitmap CreatePreview(ConnectionNotification notification, float progress = 1, float remaining = 1)
    {
        var bitmap = new Bitmap(ConnectionToastForm.CardWidth, ConnectionToastForm.CardHeight);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        Draw(graphics, notification, progress, remaining);
        return bitmap;
    }

    internal static void Draw(Graphics graphics, ConnectionNotification notification, float progress, float remaining)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using var card = RoundedRectangle(new RectangleF(0.5f, 0.5f, 371, 111), 20);
        using var background = new SolidBrush(Surface);
        using var border = new Pen(Color.FromArgb(56, 51, 65), 1);
        graphics.FillPath(background, card);
        graphics.DrawPath(border, card);
        using var tile = RoundedRectangle(new RectangleF(16, 23, 58, 58), 18);
        using var tileFill = new SolidBrush(notification.IsConnected ? Color.FromArgb(39, 29, 52) : Color.FromArgb(35, 35, 41));
        graphics.FillPath(tileFill, tile);
        var iconState = graphics.Save();
        var iconScale = 0.86f + 0.14f * progress;
        graphics.TranslateTransform(45, 52);
        graphics.ScaleTransform(iconScale, iconScale);
        using var symbol = new Pen(notification.IsConnected ? Purple : Color.FromArgb(145, 143, 153), 3.6f)
        { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        graphics.DrawLines(symbol, new PointF[] { new(-9, -10), new(10, 6), new(0, 16), new(0, -16), new(10, -6), new(-9, 10) });
        graphics.Restore(iconState);
        using var badge = new SolidBrush(notification.IsConnected ? Purple : Color.FromArgb(83, 80, 94));
        graphics.FillEllipse(background, 57, 65, 22, 22);
        graphics.FillEllipse(badge, 60, 68, 16, 16);
        using var mark = new Pen(Color.White, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        if (notification.IsConnected)
        {
            if (progress > 0.25f) graphics.DrawLine(mark, 64, 76, 67, 79);
            if (progress > 0.55f) graphics.DrawLine(mark, 67, 79, 72, 73);
        }
        else
        {
            graphics.DrawLine(mark, 65, 73, 71, 79);
            graphics.DrawLine(mark, 71, 73, 65, 79);
        }
        using var caption = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        using var name = new Font("Segoe UI", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        using var detail = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        using var accent = new SolidBrush(notification.IsConnected ? Purple : Color.FromArgb(162, 159, 174));
        using var white = new SolidBrush(Color.White);
        using var gray = new SolidBrush(Color.FromArgb(155, 152, 167));
        using var format = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        graphics.DrawString(notification.IsConnected ? "ПОДКЛЮЧЕНО" : "ОТКЛЮЧЕНО", caption, accent, 91, 22);
        graphics.DrawString(notification.DeviceName, name, white, new RectangleF(89, 40, 241, 25), format);
        var description = notification.IsConnected && notification.BatteryPercent is int battery
            ? $"Bluetooth · заряд {battery}%" : "Bluetooth-устройство";
        graphics.DrawString(description, detail, gray, 91, 70);
        using var close = new Pen(Color.FromArgb(145, 141, 155), 1.3f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(close, 344, 17, 350, 23);
        graphics.DrawLine(close, 350, 17, 344, 23);
        using var track = new Pen(Color.FromArgb(44, 40, 52), 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var timer = new Pen(notification.IsConnected ? Purple : Color.FromArgb(116, 108, 135), 2)
        { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(track, 22, 101, 350, 101);
        if (remaining > 0) graphics.DrawLine(timer, 22, 101, 22 + 328 * Math.Clamp(remaining, 0, 1), 101);
    }

    internal static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
