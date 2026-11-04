namespace BTChargeIndicator.Services;

internal static class PlatformLogger
{
    private static readonly object SyncRoot = new();

    public static void Error(string message, Exception exception)
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BT Charge Indicator",
                "app.log");
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}" +
                    $"{exception}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never interrupt Bluetooth acquisition.
        }
    }
}
