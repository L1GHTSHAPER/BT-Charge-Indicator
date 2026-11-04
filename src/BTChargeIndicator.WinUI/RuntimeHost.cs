using BTChargeIndicator.Runtime;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.WinUI;

internal sealed class RuntimeHost : IAsyncDisposable
{
    private readonly BluetoothBatteryScannerAdapter _scanner = new();
    private readonly ApplicationRuntime _runtime;
    private readonly SettingsStore _settingsStore = new();

    public RuntimeHost()
    {
        _runtime = new ApplicationRuntime(_scanner);
    }

    public ApplicationRuntimeState State => _runtime.State;

    public event Action<ApplicationRuntimeState>? StateChanged
    {
        add => _runtime.StateChanged += value;
        remove => _runtime.StateChanged -= value;
    }

    public Task StartAsync()
    {
        var settings = _settingsStore.Load();
        return _runtime.StartAsync(TimeSpan.FromSeconds(settings.RefreshIntervalSeconds));
    }

    public Task RefreshAsync(bool refreshGattBattery = false) =>
        _runtime.RefreshAsync(refreshGattBattery);

    public async ValueTask DisposeAsync()
    {
        await _runtime.DisposeAsync().ConfigureAwait(false);
        _scanner.Dispose();
    }
}
