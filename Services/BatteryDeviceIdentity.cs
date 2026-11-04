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
                .OrderByDescending(device => HasKnownComponents(device.Components))
                .ThenByDescending(device => device.BatteryPercent is >= 0 and <= 100)
                .ThenByDescending(device => device.IsConnected == true)
                .First())
            .OrderByDescending(device => device.IsConnected == true)
            .ThenBy(device => device.BatteryPercent ?? int.MaxValue)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static bool HasKnownComponents(BatteryComponents? components) =>
        components is not null &&
        (components.LeftPercent is >= 0 and <= 100 ||
         components.RightPercent is >= 0 and <= 100 ||
         components.CasePercent is >= 0 and <= 100);
}
