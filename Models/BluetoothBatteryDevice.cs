namespace BTChargeIndicator.Models;

internal sealed record BluetoothBatteryDevice(
    string Id,
    string Name,
    int? BatteryPercent,
    bool? IsConnected,
    string? Address,
    Guid? ContainerId)
{
    public DateTimeOffset ObservedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    public BatteryReadingSource BatterySource { get; init; }

    public BatteryComponents? Components { get; init; }

    public bool? IsCharging { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    public ushort? BluetoothClassMajor { get; init; }
}

internal sealed record BatteryComponents(
    int? LeftPercent,
    int? RightPercent,
    int? CasePercent,
    bool? LeftCharging = null,
    bool? RightCharging = null,
    bool? CaseCharging = null);

internal enum BatteryReadingSource
{
    None,
    WindowsDeviceProperty,
    BluetoothHfp,
    BluetoothGatt,
    BluetoothGattComponents,
    PlugAndPlay,
    AirPodsAdvertisement,
    GoogleFastPairAdvertisement,
    NothingRfcomm,
    DualSenseHid
}

internal enum BluetoothAvailability
{
    Available,
    TurnedOff,
    NotFound,
    AccessDenied,
    Error
}

internal sealed record BluetoothScanResult(
    IReadOnlyList<BluetoothBatteryDevice> Devices,
    BluetoothAvailability Availability,
    string? ErrorMessage = null);
