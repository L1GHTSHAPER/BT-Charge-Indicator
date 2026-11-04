using Microsoft.UI.Xaml;

namespace BTChargeIndicator.WinUI;

public partial class App : Application
{
    private Window? _mainWindow;

    internal static RuntimeHost? Runtime { get; private set; }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Runtime = new RuntimeHost();
        _mainWindow = new MainWindow();
        _mainWindow.Closed += MainWindow_Closed;
        _mainWindow.Activate();
        _ = StartRuntimeAsync();
    }

    private async Task StartRuntimeAsync()
    {
        try
        {
            if (Runtime is not null)
            {
                await Runtime.StartAsync();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bluetooth runtime startup failed: {ex}");
        }
    }

    private async void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (Runtime is not null)
        {
            await Runtime.DisposeAsync();
            Runtime = null;
        }
    }
}
