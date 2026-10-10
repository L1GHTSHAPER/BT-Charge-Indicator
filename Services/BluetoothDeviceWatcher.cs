using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BTChargeIndicator.Services;

internal sealed class BluetoothDeviceWatcher : IDisposable
{
    private static readonly string[] RequestedProperties =
    [
        BluetoothBatteryService.ConnectedProperty,
        BluetoothBatteryService.BatteryLifeProperty,
        BluetoothBatteryService.BluetoothBatteryProperty
    ];

    private readonly object _syncRoot = new();
    private readonly Dictionary<DeviceWatcher, BluetoothDeviceChangeTracker> _trackers = [];
    private bool _disposed;

    public event Action<BluetoothDeviceChangeKind>? Changed;

    public void Start()
    {
        StartWatcher(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
        StartWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
    }

    private void StartWatcher(string selector)
    {
        DeviceWatcher? watcher = null;
        try
        {
            watcher = DeviceInformation.CreateWatcher(
                selector, RequestedProperties, DeviceInformationKind.AssociationEndpoint);
            watcher.Added += Watcher_Added;
            watcher.Updated += Watcher_Updated;
            watcher.Removed += Watcher_Removed;
            watcher.EnumerationCompleted += Watcher_EnumerationCompleted;
            lock (_syncRoot)
            {
                _trackers.Add(watcher, new BluetoothDeviceChangeTracker());
            }
            watcher.Start();
        }
        catch (Exception ex)
        {
            AppLogger.Error("Bluetooth device monitoring could not be started.", ex);
            if (watcher is not null)
            {
                lock (_syncRoot)
                {
                    _trackers.Remove(watcher);
                }
                StopWatcher(watcher);
            }
        }
    }

    private void Watcher_Added(DeviceWatcher sender, DeviceInformation device) =>
        ApplyChange(sender, tracker => tracker.Add(device.Id, device.Properties));

    private void Watcher_Updated(DeviceWatcher sender, DeviceInformationUpdate update) =>
        ApplyChange(sender, tracker => tracker.Update(update.Id, update.Properties));

    private void Watcher_Removed(DeviceWatcher sender, DeviceInformationUpdate update) =>
        ApplyChange(sender, tracker => tracker.Remove(update.Id));

    private void Watcher_EnumerationCompleted(DeviceWatcher sender, object args) =>
        ApplyChange(sender, tracker => tracker.CompleteEnumeration());

    private void ApplyChange(
        DeviceWatcher watcher, Func<BluetoothDeviceChangeTracker, BluetoothDeviceChangeKind> update)
    {
        BluetoothDeviceChangeKind change;
        lock (_syncRoot)
        {
            if (_disposed || !_trackers.TryGetValue(watcher, out var tracker))
            {
                return;
            }
            change = update(tracker);
        }

        if (change != BluetoothDeviceChangeKind.None)
        {
            Changed?.Invoke(change);
        }
    }

    private void StopWatcher(DeviceWatcher watcher)
    {
        watcher.Added -= Watcher_Added;
        watcher.Updated -= Watcher_Updated;
        watcher.Removed -= Watcher_Removed;
        watcher.EnumerationCompleted -= Watcher_EnumerationCompleted;
        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Error("Bluetooth device monitoring could not be stopped.", ex);
        }
    }

    public void Dispose()
    {
        DeviceWatcher[] watchers;
        lock (_syncRoot)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            watchers = _trackers.Keys.ToArray();
            _trackers.Clear();
        }
        foreach (var watcher in watchers)
        {
            StopWatcher(watcher);
        }
    }
}

[Flags]
internal enum BluetoothDeviceChangeKind
{
    None = 0,
    Refresh = 1,
    Connected = Refresh | 2
}

// Watcher updates contain only changed properties. Keep the previous values so
// duplicate notifications and unrelated metadata do not trigger repeated scans.
internal sealed class BluetoothDeviceChangeTracker
{
    private readonly Dictionary<string, DeviceState> _devices = new(StringComparer.OrdinalIgnoreCase);
    private bool _enumerationCompleted;

    public BluetoothDeviceChangeKind Add(string id, IReadOnlyDictionary<string, object> properties)
    {
        var state = ReadState(properties);
        _devices[id] = state;
        return !_enumerationCompleted
            ? BluetoothDeviceChangeKind.None
            : state.Connected is true
                ? BluetoothDeviceChangeKind.Connected
                : BluetoothDeviceChangeKind.Refresh;
    }

    public BluetoothDeviceChangeKind Update(string id, IReadOnlyDictionary<string, object> properties)
    {
        if (!_devices.TryGetValue(id, out var previous))
        {
            return BluetoothDeviceChangeKind.None;
        }

        var current = ReadState(properties, previous);
        _devices[id] = current;
        return current == previous
            ? BluetoothDeviceChangeKind.None
            : current.Connected is true && previous.Connected is not true
                ? BluetoothDeviceChangeKind.Connected
                : BluetoothDeviceChangeKind.Refresh;
    }

    public BluetoothDeviceChangeKind Remove(string id) =>
        _devices.Remove(id) ? BluetoothDeviceChangeKind.Refresh : BluetoothDeviceChangeKind.None;

    public BluetoothDeviceChangeKind CompleteEnumeration()
    {
        _enumerationCompleted = true;
        return BluetoothDeviceChangeKind.Refresh;
    }

    private static DeviceState ReadState(
        IReadOnlyDictionary<string, object> properties, DeviceState? previous = null) => new(
            ReadProperty(properties, BluetoothBatteryService.ConnectedProperty, previous?.Connected),
            ReadProperty(properties, BluetoothBatteryService.BatteryLifeProperty, previous?.Battery),
            ReadProperty(properties, BluetoothBatteryService.BluetoothBatteryProperty, previous?.HfpBattery));

    private static object? ReadProperty(
        IReadOnlyDictionary<string, object> properties, string key, object? previous) =>
        properties.TryGetValue(key, out var value) ? value : previous;

    private sealed record DeviceState(object? Connected, object? Battery, object? HfpBattery);
}
