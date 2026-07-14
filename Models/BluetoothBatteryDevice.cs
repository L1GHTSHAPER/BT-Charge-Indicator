namespace BTChargeIndicator.Models;

internal sealed record BluetoothBatteryDevice(
    string Id,
    string Name,
    int? BatteryPercent,
    bool? IsConnected,
    string? Address,
    Guid? ContainerId);

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
