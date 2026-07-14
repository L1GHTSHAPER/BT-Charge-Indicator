using BTChargeIndicator.UI;

namespace BTChargeIndicator;

internal static class Program
{
    private const string MutexName = @"Local\BTChargeIndicator.SingleInstance";

    [STAThread]
    private static void Main()
    {
        using var instanceMutex = new Mutex(true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                "BT Charge Indicator уже запущен. Иконка приложения находится в системном трее.",
                "BT Charge Indicator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
