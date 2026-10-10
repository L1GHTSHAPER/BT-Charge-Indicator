using BTChargeIndicator.Models;

namespace BTChargeIndicator.Services;

internal sealed record DeviceConnectionChange(BluetoothBatteryDevice Device, bool IsConnected);

// Successful scans combine Classic/BLE endpoints. Startup establishes a quiet
// baseline; an unknown connection reading preserves the previous known state.
internal sealed class DeviceConnectionTracker(Func<BluetoothBatteryDevice, string> getKey)
{
    private Dictionary<string, BluetoothBatteryDevice> _previous = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public IReadOnlyList<DeviceConnectionChange> Observe(
        IReadOnlyList<BluetoothBatteryDevice> devices, BluetoothAvailability availability)
    {
        if (availability is BluetoothAvailability.Error or BluetoothAvailability.AccessDenied) return [];
        var current = devices.GroupBy(getKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group =>
            {
                var device = group.OrderByDescending(item => item.IsConnected is true)
                    .ThenByDescending(item => item.BatteryPercent.HasValue).First();
                bool? connected = group.Any(item => item.IsConnected is true) ? true
                    : group.Any(item => item.IsConnected is false) ? false : null;
                if (connected is null && _previous.TryGetValue(group.Key, out var previous))
                    connected = previous.IsConnected;
                return device with { IsConnected = connected };
            }, StringComparer.OrdinalIgnoreCase);

        var changes = new List<DeviceConnectionChange>();
        if (_initialized)
        {
            foreach (var (key, device) in current)
            {
                _previous.TryGetValue(key, out var previous);
                if (device.IsConnected is true && previous?.IsConnected is not true)
                    changes.Add(new DeviceConnectionChange(device, true));
                else if (device.IsConnected is false && previous?.IsConnected is true)
                    changes.Add(new DeviceConnectionChange(device, false));
            }
            foreach (var (key, device) in _previous)
                if (device.IsConnected is true && !current.ContainsKey(key))
                    changes.Add(new DeviceConnectionChange(device, false));
        }
        _previous = current;
        _initialized = true;
        return changes;
    }
}
