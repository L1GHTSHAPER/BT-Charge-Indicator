namespace BTChargeIndicator.Models;

internal sealed record BluetoothBatteryDevice(
    string Id,
    string Name,
    int? BatteryPercent,
    bool? IsConnected,
    string? Address,
    Guid? ContainerId)
{
    public BatteryReadingSource BatterySource { get; init; }

    public BatteryComponents? Components { get; init; }

    public IReadOnlyList<string> Categories { get; init; } = [];

    public ushort? BluetoothClassMajor { get; init; }
}

internal sealed record BatteryComponents(
    int? LeftPercent,
    int? RightPercent,
    int? CasePercent);

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
