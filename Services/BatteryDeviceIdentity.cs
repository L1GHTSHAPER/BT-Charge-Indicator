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
            .Select(MergeGroup)
            .OrderByDescending(device => device.IsConnected == true)
            .ThenBy(device => device.BatteryPercent ?? int.MaxValue)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static BluetoothBatteryDevice MergeGroup(
        IGrouping<string, BluetoothBatteryDevice> group)
    {
        var candidates = group.ToArray();
        var preferred = candidates
            .OrderByDescending(device => HasKnownComponents(device.Components))
            .ThenByDescending(device => Normalize(device.BatteryPercent).HasValue)
            .ThenByDescending(device => device.BatterySource != BatteryReadingSource.None)
            .ThenByDescending(device => device.IsConnected == true)
            .First();

        var left = FirstKnown(candidates.Select(device => device.Components?.LeftPercent));
        var right = FirstKnown(candidates.Select(device => device.Components?.RightPercent));
        var casePercent = FirstKnown(candidates.Select(device => device.Components?.CasePercent));
        var components = left.HasValue || right.HasValue || casePercent.HasValue
            ? new BatteryComponents(
                left, right, casePercent,
                candidates.Select(device => device.Components?.LeftCharging).FirstOrDefault(value => value.HasValue),
                candidates.Select(device => device.Components?.RightCharging).FirstOrDefault(value => value.HasValue),
                candidates.Select(device => device.Components?.CaseCharging).FirstOrDefault(value => value.HasValue))
            : null;

        return preferred with
        {
            BatteryPercent = Normalize(preferred.BatteryPercent) ??
                             FirstKnown(candidates.Select(device => device.BatteryPercent)),
            IsConnected = candidates.Any(device => device.IsConnected == true)
                ? true
                : candidates.All(device => device.IsConnected == false) ? false : null,
            ObservedAtUtc = candidates.Max(device => device.ObservedAtUtc),
            Components = components,
            IsCharging = candidates.Any(device => device.IsCharging == true)
                ? true
                : candidates.All(device => device.IsCharging == false) ? false : null,
            Categories = candidates.SelectMany(device => device.Categories)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            BluetoothClassMajor = preferred.BluetoothClassMajor ??
                                  candidates.Select(device => device.BluetoothClassMajor)
                                      .FirstOrDefault(value => value.HasValue)
        };
    }

    private static int? FirstKnown(IEnumerable<int?> values) =>
        values.Select(Normalize).FirstOrDefault(value => value.HasValue);

    private static int? Normalize(int? value) =>
        value is >= 0 and <= 100 ? value : null;

    private static bool HasKnownComponents(BatteryComponents? components) =>
        components is not null &&
        (Normalize(components.LeftPercent).HasValue ||
         Normalize(components.RightPercent).HasValue ||
         Normalize(components.CasePercent).HasValue);
}
