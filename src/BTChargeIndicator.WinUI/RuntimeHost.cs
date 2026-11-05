using BTChargeIndicator.Runtime;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;
using BTChargeIndicator.Models;

namespace BTChargeIndicator.WinUI;

internal sealed class RuntimeHost : IAsyncDisposable
{
    private readonly BluetoothBatteryScannerAdapter _scanner = new();
    private readonly BluetoothDeviceWatcher _deviceWatcher = new();
    private readonly ApplicationRuntime _runtime;
    private readonly SettingsStore _settingsStore = new();
    private readonly DeviceConnectionTracker _connectionTracker = new(BatteryDeviceIdentity.GetKey);
    private readonly WinUiNotificationService _notificationService = new();
    private readonly WinUiTrayService _trayService;
    private readonly HashSet<string> _lowBatteryNotifications = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _notificationLock = new();
    private readonly object _eventRefreshLock = new();
    private System.Threading.Timer? _eventRefreshTimer;
    private bool _disposed;

    public RuntimeHost(Action activateWindow, Action exitApp)
    {
        _trayService = new WinUiTrayService(activateWindow, exitApp);
        _runtime = new ApplicationRuntime(_scanner);
        _runtime.StateChanged += Runtime_StateChanged;
        _deviceWatcher.Changed += DeviceWatcher_Changed;
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
        _deviceWatcher.Start();
        return _runtime.StartAsync(TimeSpan.FromSeconds(settings.RefreshIntervalSeconds));
    }

    public Task RefreshAsync(bool refreshGattBattery = false) =>
        _runtime.RefreshAsync(refreshGattBattery);

    public int RefreshIntervalSeconds => _settingsStore.Load().RefreshIntervalSeconds;

    public void AttachMainWindow(Microsoft.UI.Xaml.Window window) => _trayService.AttachWindow(window);

    public void ShowMainWindow() => _trayService.ShowMainWindow();

    public IReadOnlyList<BluetoothBatteryDevice> Devices => _runtime.State.Devices;

    public AppSettings LoadSettings() => _settingsStore.Load();

    public int LowBatteryThreshold => _settingsStore.Load().LowBatteryThreshold;

    public AppLanguage Language => _settingsStore.Load().Language;

    public async Task UpdateRefreshIntervalAsync(int seconds)
    {
        var settings = _settingsStore.Load();
        settings.RefreshIntervalSeconds = seconds;
        _settingsStore.Save(settings);
        await _runtime.UpdateRefreshIntervalAsync(TimeSpan.FromSeconds(settings.RefreshIntervalSeconds));
    }

    public void UpdateLowBatteryThreshold(int threshold)
    {
        var settings = _settingsStore.Load();
        settings.LowBatteryThreshold = threshold;
        _settingsStore.Save(settings);
    }

    public void UpdateNotificationSettings(bool lowBatteryNotifications, bool connectionNotifications)
    {
        var settings = _settingsStore.Load();
        settings.LowBatteryNotifications = lowBatteryNotifications;
        settings.ConnectionNotifications = connectionNotifications;
        _settingsStore.Save(settings);
        ProcessNotifications(_runtime.State);
    }

    public void UpdateLanguage(AppLanguage language)
    {
        var settings = _settingsStore.Load();
        settings.Language = language;
        _settingsStore.Save(settings);
    }

    public void UpdateDeviceTrayPreferences(BluetoothBatteryDevice device, bool includeInCombinedIcon,
        bool showIndividualIcon, BatteryAggregationMode? batteryMode, TrayIconStyle? iconStyle)
    {
        var settings = _settingsStore.Load();
        var key = BatteryDeviceIdentity.GetKey(device);
        settings.Devices.TryGetValue(key, out var preferences);
        preferences ??= new DevicePreferences();
        preferences.LastKnownName = device.Name;
        preferences.IncludeInTrayIcon = includeInCombinedIcon;
        preferences.ShowIndividualTrayIcon = showIndividualIcon;
        preferences.BatteryMode = batteryMode;
        preferences.IndividualTrayIconStyle = iconStyle;
        settings.Devices[key] = preferences;
        _settingsStore.Save(settings);
        _trayService.SettingsChanged();
    }

    public void UpdatePreferredTrayDevice(string? deviceKey)
    {
        var settings = _settingsStore.Load();
        settings.PreferredTrayDeviceKey = deviceKey;
        _settingsStore.Save(settings);
        _trayService.SettingsChanged();
    }

    public void UpdateDevicePreferences(
        IEnumerable<(BluetoothBatteryDevice Device, string? CustomName, bool IsHidden)> updates)
    {
        var settings = _settingsStore.Load();
        foreach (var update in updates)
        {
            var key = BatteryDeviceIdentity.GetKey(update.Device);
            settings.Devices.TryGetValue(key, out var preferences);
            preferences ??= new DevicePreferences();
            preferences.LastKnownName = update.Device.Name;
            preferences.CustomName = string.IsNullOrWhiteSpace(update.CustomName)
                ? null
                : update.CustomName.Trim();
            preferences.IsHidden = update.IsHidden;
            settings.Devices[key] = preferences;
        }

        _settingsStore.Save(settings);
    }

    public void UpdateDeviceNotificationPreferences(
        BluetoothBatteryDevice device,
        bool lowBatteryNotifications,
        bool connectionNotifications)
    {
        var settings = _settingsStore.Load();
        var key = BatteryDeviceIdentity.GetKey(device);
        settings.Devices.TryGetValue(key, out var preferences);
        preferences ??= new DevicePreferences();
        preferences.LastKnownName = device.Name;
        preferences.LowBatteryNotifications = lowBatteryNotifications;
        preferences.ConnectionNotifications = connectionNotifications;
        settings.Devices[key] = preferences;
        _settingsStore.Save(settings);
        ProcessNotifications(_runtime.State);
    }

    public void SetDevicesHidden(IEnumerable<BluetoothBatteryDevice> devices, bool isHidden)
    {
        var settings = _settingsStore.Load();
        foreach (var device in devices)
        {
            var key = BatteryDeviceIdentity.GetKey(device);
            settings.Devices.TryGetValue(key, out var preferences);
            preferences ??= new DevicePreferences();
            preferences.LastKnownName = device.Name;
            preferences.IsHidden = isHidden;
            settings.Devices[key] = preferences;
        }

        _settingsStore.Save(settings);
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _deviceWatcher.Changed -= DeviceWatcher_Changed;
        _deviceWatcher.Dispose();
        lock (_eventRefreshLock)
        {
            _eventRefreshTimer?.Dispose();
            _eventRefreshTimer = null;
        }
        _runtime.StateChanged -= Runtime_StateChanged;
        await _runtime.DisposeAsync().ConfigureAwait(false);
        _scanner.Dispose();
        _notificationService.Dispose();
        _trayService.Dispose();
    }

    private void DeviceWatcher_Changed(BluetoothDeviceChangeKind change)
    {
        if (change == BluetoothDeviceChangeKind.None || _disposed)
        {
            return;
        }

        // AEP watchers can report several related property changes together.
        // Coalesce them into one full scan so connection notifications use the
        // same device data and comparison logic as scheduled refreshes.
        lock (_eventRefreshLock)
        {
            _eventRefreshTimer ??= new Timer(_ => _ = RefreshAfterDeviceChangeAsync());
            _eventRefreshTimer.Change(TimeSpan.FromMilliseconds(500), Timeout.InfiniteTimeSpan);
        }
    }

    private async Task RefreshAfterDeviceChangeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            await _runtime.RefreshAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException) when (_disposed)
        {
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bluetooth event refresh failed: {ex}");
        }
    }

    private void Runtime_StateChanged(ApplicationRuntimeState state)
    {
        if (!state.IsScanning && state.LastUpdatedUtc.HasValue)
        {
            _trayService.Refresh(state.Devices);
            ProcessNotifications(state);
        }
    }

    private void ProcessNotifications(ApplicationRuntimeState state)
    {
        lock (_notificationLock)
        {
            var settings = _settingsStore.Load();
            var devices = BatteryDeviceIdentity.Deduplicate(state.Devices);
            var connectionChanges = _connectionTracker.Observe(devices, state.Availability);
            if (settings.ConnectionNotifications)
            {
                foreach (var change in connectionChanges)
                {
                    settings.Devices.TryGetValue(BatteryDeviceIdentity.GetKey(change.Device), out var preferences);
                    if (preferences?.IsHidden == true || preferences?.ConnectionNotifications == false)
                    {
                        continue;
                    }

                    var name = preferences?.CustomName ?? change.Device.Name;
                    var title = WinUIText.Get(change.IsConnected ? "DeviceConnectedTitle" : "DeviceDisconnectedTitle");
                    var detail = FormatBatteryDetails(change.Device);
                    var message = WinUIText.Format(change.IsConnected ? "DeviceConnectedMessage" : "DeviceDisconnectedMessage", name);
                    if (change.IsConnected && !string.IsNullOrEmpty(detail)) message += " · " + detail;
                    _notificationService.Show(title, message);
                }
            }

            var currentlyLow = devices
                .Where(device => device.IsConnected == true && GetBatteryLevels(device)
                    .Any(percent => percent <= settings.LowBatteryThreshold))
                .ToDictionary(BatteryDeviceIdentity.GetKey, StringComparer.OrdinalIgnoreCase);
            _lowBatteryNotifications.RemoveWhere(key => !currentlyLow.ContainsKey(key));
            if (!settings.LowBatteryNotifications)
            {
                return;
            }

            var newLowDevices = currentlyLow
                .Where(pair => !_lowBatteryNotifications.Contains(pair.Key))
                .Where(pair => !settings.Devices.TryGetValue(pair.Key, out var preferences) ||
                               preferences.LowBatteryNotifications)
                .ToArray();
            foreach (var device in newLowDevices)
            {
                _lowBatteryNotifications.Add(device.Key);
            }

            if (newLowDevices.Length > 0)
            {
                var details = string.Join(", ", newLowDevices.Select(pair =>
                {
                    settings.Devices.TryGetValue(pair.Key, out var preferences);
                    return $"{preferences?.CustomName ?? pair.Value.Name}: {FormatBatteryDetails(pair.Value)}";
                }));
                _notificationService.Show(WinUIText.Get("LowBatteryNotificationTitle"), details);
            }
        }
    }

    private static IEnumerable<int> GetBatteryLevels(BluetoothBatteryDevice device)
    {
        if (device.Components is { } components)
        {
            var found = false;
            foreach (var value in new[] { components.LeftPercent, components.RightPercent, components.CasePercent })
                if (value is int known) { found = true; yield return known; }
            if (!found && device.BatteryPercent is int fallback) yield return fallback;
        }
        else if (device.BatteryPercent is int value) yield return value;
    }

    private static string FormatBatteryDetails(BluetoothBatteryDevice device)
    {
        var parts = new List<string>();
        if (device.Components is { } components)
        {
            Add("L", components.LeftPercent, components.LeftCharging);
            Add("R", components.RightPercent, components.RightCharging);
            Add("Case", components.CasePercent, components.CaseCharging);
            if (parts.Count == 0 && device.BatteryPercent is int fallback)
                parts.Add($"Battery {fallback}%{(device.IsCharging == true ? " · Charging" : string.Empty)}");
        }
        else if (device.BatteryPercent is int percent)
        {
            parts.Add($"Battery {percent}%{(device.IsCharging == true ? " · Charging" : string.Empty)}");
        }
        else if (device.IsCharging == true) parts.Add("Charging");
        return string.Join(" · ", parts);

        void Add(string label, int? value, bool? charging)
        {
            if (value is int level) parts.Add($"{label} {level}%{(charging == true ? " (charging)" : string.Empty)}");
        }
    }
}
