namespace BTChargeIndicator.UI;

internal static class AppBranding
{
    public static Icon CreateIcon(int size = 32)
    {
        using var stream = typeof(AppBranding).Assembly
            .GetManifestResourceStream("BTChargeIndicator.AppIcon")
            ?? throw new InvalidOperationException("The application icon resource is missing.");
        using var icon = new Icon(stream, new Size(size, size));
        return (Icon)icon.Clone();
    }

    public static Bitmap CreateLogoBitmap(int size)
    {
        using var icon = CreateIcon(size);
        return icon.ToBitmap();
    }
}
