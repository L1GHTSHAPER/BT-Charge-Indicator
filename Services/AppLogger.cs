namespace BTChargeIndicator.Services;

internal static class AppLogger
{
    private static readonly object SyncRoot = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BT Charge Indicator",
        "app.log");

    public static void Error(string message, Exception exception)
    {
        try
        {
            lock (SyncRoot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(
                    LogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}" +
                    $"{exception}" +
                    $"{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Ошибка журнала не должна влиять на работу приложения.
        }
    }

}
