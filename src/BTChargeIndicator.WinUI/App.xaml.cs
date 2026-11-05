using Microsoft.UI.Xaml;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.WinUI;

public partial class App : Application
{
    private static readonly string InstanceScope = Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            Path.GetFullPath(AppContext.BaseDirectory))));
    private static readonly string InstanceMutexName = $@"Local\BTChargeIndicator.WinUI.{InstanceScope}.Mutex";
    private static readonly string ShowExistingEventName = $@"Local\BTChargeIndicator.WinUI.{InstanceScope}.ShowExisting";
    private Window? _mainWindow;
    private Mutex? _instanceMutex;
    private EventWaitHandle? _showExistingEvent;

    internal static RuntimeHost? Runtime { get; private set; }

    internal static event Action? LanguageChanged;

    internal static void ApplyLanguage(AppLanguage language)
    {
        WinUIText.Apply(language);
        LanguageChanged?.Invoke();
    }

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _instanceMutex = new Mutex(true, InstanceMutexName, out var createdNew);
        var ownsMutex = createdNew;
        if (!createdNew)
        {
            try { ownsMutex = _instanceMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsMutex = true; }
        }

        if (!ownsMutex)
        {
            SignalRunningInstance();
            Environment.Exit(0);
            return;
        }

        _showExistingEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowExistingEventName);
        Runtime = new RuntimeHost(() => _mainWindow?.Activate(), () => _mainWindow?.Close());
        ApplyLanguage(Runtime.Language);
        _mainWindow = new MainWindow();
        Runtime.AttachMainWindow(_mainWindow);
        StartExistingInstanceListener(_mainWindow.DispatcherQueue, _showExistingEvent);
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
        _showExistingEvent?.Dispose();
        _instanceMutex?.ReleaseMutex();
        _instanceMutex?.Dispose();
        _showExistingEvent = null;
        _instanceMutex = null;
        Exit();
    }

    private static void SignalRunningInstance()
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(ShowExistingEventName);
                showEvent.Set();
                return;
            }
            catch (WaitHandleCannotBeOpenedException) { Thread.Sleep(50); }
        }
    }

    private static void StartExistingInstanceListener(
        Microsoft.UI.Dispatching.DispatcherQueue dispatcher,
        EventWaitHandle showExistingEvent)
    {
        _ = Task.Run(() =>
        {
            try
            {
                while (true)
                {
                    showExistingEvent.WaitOne();
                    dispatcher.TryEnqueue(() => Runtime?.ShowMainWindow());
                }
            }
            catch (ObjectDisposedException) { }
        });
    }
}
