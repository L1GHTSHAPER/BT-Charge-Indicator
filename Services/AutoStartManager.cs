using Microsoft.Win32;

namespace BTChargeIndicator.Services;

internal static class AutoStartManager
{
    private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "BT Charge Indicator";

    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, false);
                return key?.GetValue(ValueName) is string value &&
                       string.Equals(value, GetCommandLine(), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, true)
            ?? throw new InvalidOperationException("Не удалось открыть раздел автозапуска Windows.");

        if (enabled)
        {
            key.SetValue(ValueName, GetCommandLine(), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }

    private static string GetCommandLine()
    {
        var executablePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Не удалось определить путь к приложению.");
        return $"\"{executablePath}\"";
    }
}
