using BTChargeIndicator.UI;
using BTChargeIndicator.Localization;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator;

internal static class Program
{
    private const string MutexName = @"Local\BTChargeIndicator.SingleInstance";

    [STAThread]
    private static void Main()
    {
        AppText.Apply(new SettingsStore().Load().Language);
        using var instanceMutex = new Mutex(true, MutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            MessageBox.Show(
                AppText.Get("AlreadyRunning"),
                "BT Charge Indicator",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext());
    }
}
