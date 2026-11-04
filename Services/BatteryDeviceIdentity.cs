using BTChargeIndicator.Models;

namespace BTChargeIndicator.Services;

/// <summary>
/// Stable identity and duplicate endpoint handling shared by tray, settings, and aggregation.
/// </summary>
internal static class BatteryDeviceIdentity
{
    public static string GetKey(BluetoothBatteryDevice device)
    {
        if (device.ContainerId.HasValue)
        {
            return $"container:{device.ContainerId.Value:D}";
        }

        if (!string.IsNullOrWhiteSpace(device.Address))
        {
            return $"address:{device.Address.Trim()}";
        }

        return $"id:{device.Id}";
    }

    public static IReadOnlyList<BluetoothBatteryDevice> Deduplicate(
        IEnumerable<BluetoothBatteryDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        return devices
            .GroupBy(GetKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(device => device.Components is not null)
                .ThenByDescending(device => device.BatteryPercent.HasValue)
                .ThenByDescending(device => device.IsConnected == true)
                .First())
            .OrderByDescending(device => device.IsConnected == true)
            .ThenBy(device => device.BatteryPercent ?? int.MaxValue)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }
}
