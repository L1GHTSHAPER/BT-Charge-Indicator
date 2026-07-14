using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace BTChargeIndicator.UI;

internal static class TrayIconRenderer
{
    public static Icon Create(int? percentage)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var background = percentage switch
        {
            <= 20 => Color.FromArgb(220, 55, 55),
            <= 40 => Color.FromArgb(235, 145, 35),
            null => Color.FromArgb(105, 112, 122),
            _ => Color.FromArgb(32, 158, 92)
        };

        using var path = RoundedRectangle(new RectangleF(1, 1, 30, 30), 7);
        using var fill = new SolidBrush(background);
        using var border = new Pen(Color.FromArgb(95, Color.Black), 1);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        var text = percentage?.ToString() ?? "?";
        var fontSize = text.Length switch
        {
            >= 3 => 11f,
            2 => 13f,
            _ => 15f
        };

        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(text, font, textBrush, new RectangleF(1, 0, 30, 31), format);

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
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

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(nint handle);
}
