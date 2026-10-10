using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.UI;

internal static class TrayIconRenderer
{
    public static Icon Create(int? percentage, TrayIconStyle style, int lowBatteryThreshold)
    {
        using var bitmap = CreateBitmap(percentage, style, lowBatteryThreshold);
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

    public static Bitmap CreateBitmap(int? percentage, TrayIconStyle style, int lowBatteryThreshold)
    {
        var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            Draw(graphics, percentage, style, lowBatteryThreshold);
            return bitmap;
        }
        catch
        {
            bitmap.Dispose();
            throw;
        }
    }

    private static void Draw(Graphics graphics, int? percentage, TrayIconStyle style, int lowBatteryThreshold)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        percentage = percentage is int rawValue ? Math.Clamp(rawValue, 0, 100) : null;
        var accent = percentage switch
        {
            null => Color.FromArgb(105, 112, 122),
            int value when value <= lowBatteryThreshold => Color.FromArgb(220, 55, 55),
            int value when value <= Math.Min(lowBatteryThreshold + 20, 100) => Color.FromArgb(235, 145, 35),
            _ => Color.FromArgb(32, 158, 92)
        };

        switch (style)
        {
            case TrayIconStyle.Battery:
                DrawTile(graphics, accent);
                DrawBattery(graphics, percentage);
                break;
            case TrayIconStyle.Ring:
                DrawRing(graphics, percentage, accent, segmented: false);
                break;
            case TrayIconStyle.SegmentedRing:
                DrawRing(graphics, percentage, accent, segmented: true);
                break;
            case TrayIconStyle.Bars:
                DrawBars(graphics, percentage, accent);
                break;
            case TrayIconStyle.VerticalBattery:
                DrawVerticalBattery(graphics, percentage, accent);
                break;
            case TrayIconStyle.Minimal:
                DrawMinimal(graphics, percentage, accent);
                break;
            case TrayIconStyle.Capsule:
                DrawCapsule(graphics, percentage, accent);
                break;
            case TrayIconStyle.Gradient:
                DrawGradient(graphics, percentage, accent);
                break;
            case TrayIconStyle.Gauge:
                DrawGauge(graphics, percentage, accent);
                break;
            default:
                DrawTile(graphics, accent);
                DrawPercentage(graphics, percentage);
                break;
        }

    }

    private static readonly Color Surface = Color.FromArgb(28, 34, 45);
    private static readonly Color Track = Color.FromArgb(83, 94, 111);

    private static void DrawTile(Graphics graphics, Color color)
    {
        using var path = RoundedRectangle(new RectangleF(0.5f, 0.5f, 31, 31), 6);
        using var fill = new SolidBrush(color);
        using var border = new Pen(Color.FromArgb(115, Color.Black), 1.2f);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
    }

    private static void DrawRing(Graphics graphics, int? percentage, Color accent, bool segmented)
    {
        using var surface = new SolidBrush(Surface);
        graphics.FillEllipse(surface, 0.5f, 0.5f, 31, 31);
        var bounds = new RectangleF(3, 3, 26, 26);
        using var track = new Pen(Track, 3.5f);
        using var level = new Pen(accent, 3.5f);
        if (segmented)
        {
            const int count = 12;
            var sweep = (percentage ?? 0) * 3.6f;
            for (var index = 0; index < count; index++)
            {
                var start = -90 + index * 30;
                graphics.DrawArc(track, bounds, start, 22);
                var filledSweep = Math.Min(22, sweep - index * 30);
                if (filledSweep > 0)
                {
                    graphics.DrawArc(level, bounds, start, filledSweep);
                }
            }
        }
        else
        {
            graphics.DrawEllipse(track, bounds);
            level.StartCap = LineCap.Round;
            level.EndCap = LineCap.Round;
            if (percentage is > 0)
            {
                graphics.DrawArc(level, bounds, -90, percentage.Value * 3.6f);
            }
        }

        DrawText(graphics, percentage, new RectangleF(7, 7, 18, 18),
            percentage == 0 ? accent : Color.White, 16);
    }

    private static void DrawBars(Graphics graphics, int? percentage, Color accent)
    {
        DrawTile(graphics, Surface);
        using var track = new SolidBrush(Track);
        using var level = new SolidBrush(accent);
        for (var index = 0; index < 5; index++)
        {
            var height = 6 + index * 3;
            var bounds = new RectangleF(3 + index * 5.5f, 23 - height, 4, height);
            using var path = RoundedRectangle(bounds, 1);
            graphics.FillPath(track, path);
            var fraction = Math.Clamp((percentage ?? 0) / 20f - index, 0, 1);
            if (fraction > 0)
            {
                var state = graphics.Save();
                graphics.SetClip(new RectangleF(bounds.Left, bounds.Bottom - height * fraction,
                    bounds.Width, height * fraction));
                graphics.FillPath(level, path);
                graphics.Restore(state);
            }
        }

        // A separate baseline makes 0% distinguishable from an unavailable reading.
        using var baseline = new SolidBrush(accent);
        graphics.FillRectangle(baseline, 3, 27, 26, 2);
        if (percentage is null)
        {
            DrawText(graphics, null, new RectangleF(7, 2, 18, 22), Color.White, 20);
        }
    }

    private static void DrawVerticalBattery(Graphics graphics, int? percentage, Color accent)
    {
        using var body = RoundedRectangle(new RectangleF(6, 4.5f, 20, 26), 4);
        using var surface = new SolidBrush(Surface);
        using var terminal = new SolidBrush(accent);
        using var outline = new Pen(accent, 2);
        using var track = new SolidBrush(Track);
        graphics.FillRectangle(terminal, 12, 0.5f, 8, 3);
        graphics.FillPath(surface, body);
        graphics.DrawPath(outline, body);
        var inner = new RectangleF(9, 8, 14, 19);
        using var innerPath = RoundedRectangle(inner, 2);
        graphics.FillPath(track, innerPath);
        if (percentage is int value && value > 0)
        {
            var height = inner.Height * value / 100f;
            var state = graphics.Save();
            graphics.SetClip(new RectangleF(inner.Left, inner.Bottom - height, inner.Width, height));
            graphics.FillPath(terminal, innerPath);
            graphics.Restore(state);
        }
        else if (percentage is null)
        {
            DrawText(graphics, null, inner, Color.White, 18);
        }
    }

    private static void DrawMinimal(Graphics graphics, int? percentage, Color accent)
    {
        // A dark halo preserves colored digits against light taskbars.
        var text = percentage?.ToString() ?? "?";
        using var format = CenteredFormat();
        using var font = CreateFittedFont(graphics, text, format, 28, 29, 30);
        using var path = new GraphicsPath();
        path.AddString(text, font.FontFamily, (int)font.Style, font.Size,
            new RectangleF(0, -0.5f, 32, 33), format);
        using var halo = new Pen(Surface, 2.5f) { LineJoin = LineJoin.Round };
        using var fill = new SolidBrush(accent);
        graphics.DrawPath(halo, path);
        graphics.FillPath(fill, path);
    }

    private static void DrawCapsule(Graphics graphics, int? percentage, Color accent)
    {
        using var path = RoundedRectangle(new RectangleF(0.75f, 3, 30.5f, 26), 12);
        using var surface = new SolidBrush(Surface);
        using var border = new Pen(accent, 1.5f);
        graphics.FillPath(surface, path);
        graphics.DrawPath(border, path);
        DrawText(graphics, percentage, new RectangleF(2, 4, 28, 21), Color.White, 23);
        using var track = new Pen(Track, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var level = new Pen(accent, 2) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawLine(track, 9, 25, 23, 25);
        if (percentage is > 0)
        {
            graphics.DrawLine(level, 9, 25, 9 + 14 * percentage.Value / 100f, 25);
        }
    }

    private static void DrawGradient(Graphics graphics, int? percentage, Color accent)
    {
        using var path = RoundedRectangle(new RectangleF(0.5f, 0.5f, 31, 31), 9);
        var highlight = Color.FromArgb(
            Math.Min(255, accent.R + 45), Math.Min(255, accent.G + 45), Math.Min(255, accent.B + 45));
        var shade = Color.FromArgb(accent.R / 2, accent.G / 2, accent.B / 2);
        using var fill = new LinearGradientBrush(new RectangleF(0, 0, 32, 32), highlight, shade, 65);
        using var border = new Pen(Color.FromArgb(110, Color.White), 1);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
        DrawPercentage(graphics, percentage);
    }

    private static void DrawGauge(Graphics graphics, int? percentage, Color accent)
    {
        DrawTile(graphics, Surface);
        var bounds = new RectangleF(4, 4, 24, 24);
        using var track = new Pen(Track, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var level = new Pen(accent, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.DrawArc(track, bounds, 135, 270);
        if (percentage is > 0)
        {
            graphics.DrawArc(level, bounds, 135, percentage.Value * 2.7f);
        }
        using var dot = new SolidBrush(accent);
        graphics.FillEllipse(dot, 14, 27, 4, 4);
        DrawText(graphics, percentage, new RectangleF(7, 8, 18, 17), Color.White, 16);
    }

    private static void DrawPercentage(Graphics graphics, int? percentage)
    {
        DrawText(graphics, percentage, new RectangleF(0, -0.5f, 32, 33), Color.White, 27);
    }

    private static StringFormat CenteredFormat()
    {
        var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.Alignment = StringAlignment.Center;
        format.LineAlignment = StringAlignment.Center;
        return format;
    }

    private static void DrawText(Graphics graphics, int? percentage, RectangleF bounds, Color color, float maxSize)
    {
        var text = percentage?.ToString() ?? "?";
        using var format = CenteredFormat();
        using var font = CreateFittedFont(graphics, text, format, maxSize,
            Math.Min(bounds.Width, 30.5f), Math.Min(bounds.Height, 30.5f));
        using var textBrush = new SolidBrush(color);
        graphics.DrawString(text, font, textBrush, bounds, format);
    }

    private static Font CreateFittedFont(Graphics graphics, string text, StringFormat format,
        float maxSize, float maxWidth, float maxHeight)
    {
        for (var fontSize = maxSize; fontSize >= 8f; fontSize -= 0.5f)
        {
            var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
            var measured = graphics.MeasureString(text, font, PointF.Empty, format);
            if (measured.Width <= maxWidth && measured.Height <= maxHeight)
            {
                return font;
            }

            font.Dispose();
        }

        return new Font("Segoe UI", 8, FontStyle.Bold, GraphicsUnit.Pixel);
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
