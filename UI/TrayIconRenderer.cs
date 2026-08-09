using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.UI;

internal static class TrayIconRenderer
{
    public static Icon Create(int? percentage, TrayIconStyle style, int lowBatteryThreshold)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var background = percentage switch
        {
            null => Color.FromArgb(105, 112, 122),
            int value when value <= lowBatteryThreshold => Color.FromArgb(220, 55, 55),
            int value when value <= Math.Min(lowBatteryThreshold + 20, 100) => Color.FromArgb(235, 145, 35),
            _ => Color.FromArgb(32, 158, 92)
        };

        using var path = RoundedRectangle(new RectangleF(0.5f, 0.5f, 31, 31), 6);
        using var fill = new SolidBrush(background);
        using var border = new Pen(Color.FromArgb(115, Color.Black), 1.2f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);

        if (style == TrayIconStyle.Battery)
        {
            DrawBattery(graphics, percentage);
        }
        else
        {
            DrawPercentage(graphics, percentage);
        }

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

    private static void DrawPercentage(Graphics graphics, int? percentage)
    {
        var text = percentage?.ToString() ?? "?";
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = StringAlignment.Center;
        format.LineAlignment = StringAlignment.Center;

        using var font = CreateFittedFont(graphics, text, format);
        using var textBrush = new SolidBrush(Color.White);
        graphics.DrawString(text, font, textBrush, new RectangleF(0, -0.5f, 32, 33), format);
    }

    private static Font CreateFittedFont(Graphics graphics, string text, StringFormat format)
    {
        for (var fontSize = 27f; fontSize >= 11f; fontSize -= 0.5f)
        {
            var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, font, PointF.Empty, format);
            if (measured.Width <= 30.5f && measured.Height <= 30.5f)
            {
                return font;
            }

            font.Dispose();
        }

        return new Font("Segoe UI", 11, FontStyle.Bold, GraphicsUnit.Pixel);
    }

    private static void DrawBattery(Graphics graphics, int? percentage)
    {
        var bodyBounds = new RectangleF(3.5f, 8, 23, 16);
        var innerBounds = new RectangleF(7, 11.5f, 16, 9);

        using var bodyPath = RoundedRectangle(bodyBounds, 2.5f);
        using var outline = new Pen(Color.White, 2.2f)
        {
            LineJoin = LineJoin.Round
        };
        using var emptyBrush = new SolidBrush(Color.FromArgb(90, Color.Black));
        using var levelBrush = new SolidBrush(Color.White);

        graphics.FillPath(emptyBrush, bodyPath);
        graphics.DrawPath(outline, bodyPath);
        graphics.FillRectangle(levelBrush, new RectangleF(27.5f, 12, 2.5f, 8));

        if (percentage is int value)
        {
            var fillWidth = innerBounds.Width * Math.Clamp(value, 0, 100) / 100f;
            if (fillWidth > 0)
            {
                graphics.FillRectangle(
                    levelBrush,
                    new RectangleF(innerBounds.Left, innerBounds.Top, fillWidth, innerBounds.Height));
            }
        }
        else
        {
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("?", font, levelBrush, bodyBounds, format);
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
