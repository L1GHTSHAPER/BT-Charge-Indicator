using BTChargeIndicator.Settings;

namespace BTChargeIndicator.Models;

/// <summary>
/// Immutable, normalized view of one device observation. It intentionally excludes
/// Bluetooth addresses and keeps component readings separate from display policy.
/// </summary>
internal sealed record BatterySnapshot(
    string DeviceId,
    string Name,
    int? DevicePercent,
    bool? IsConnected,
    BatteryReadingSource Source,
    DateTimeOffset ObservedAtUtc,
    IReadOnlyList<BatteryComponentValue> Components)
{
    public static BatterySnapshot FromDevice(BluetoothBatteryDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        var values = new List<BatteryComponentValue>();
        Add(values, BatteryComponentKind.Left, device.Components?.LeftPercent);
        Add(values, BatteryComponentKind.Right, device.Components?.RightPercent);
        Add(values, BatteryComponentKind.Case, device.Components?.CasePercent);
        Add(values, BatteryComponentKind.Device, device.BatteryPercent);

        return new BatterySnapshot(
            device.Id,
            device.Name,
            Normalize(device.BatteryPercent),
            device.IsConnected,
            device.BatterySource,
            device.ObservedAtUtc,
            values.AsReadOnly());
    }

    public int? GetComponent(BatteryComponentKind component) =>
        Components.FirstOrDefault(value => value.Component == component)?.Percent;

    private static void Add(
        ICollection<BatteryComponentValue> values,
        BatteryComponentKind component,
        int? percentage)
    {
        if (Normalize(percentage) is int valid)
        {
            values.Add(new BatteryComponentValue(component, valid));
        }
    }

    private static int? Normalize(int? value) =>
        value is >= 0 and <= 100 ? value : null;
}

internal sealed record BatteryComponentValue(BatteryComponentKind Component, int Percent);
