using BTChargeIndicator.Models;

namespace BTChargeIndicator.Runtime;

/// <summary>
/// A read-only snapshot of the latest Bluetooth acquisition state.
/// Collections are copied by the runtime before publication.
/// </summary>
internal sealed record ApplicationRuntimeState(
    IReadOnlyList<BluetoothBatteryDevice> Devices,
    BluetoothAvailability Availability,
    string? ErrorMessage,
    DateTimeOffset? LastUpdatedUtc,
    bool IsScanning)
{
    public static ApplicationRuntimeState Initial { get; } =
        new([], BluetoothAvailability.Available, null, null, false);
}

/// <summary>
/// Platform adapter implemented by the Windows Bluetooth acquisition layer.
/// </summary>
internal interface IBluetoothBatteryScanner
{
    Task<BluetoothScanResult> ScanAsync(bool refreshGattBattery = false);
}

/// <summary>
/// Single owner for Bluetooth acquisition scheduling and published device state.
/// Presentation clients observe StateChanged and request refreshes through this contract.
/// </summary>
internal interface IApplicationRuntime
{
    ApplicationRuntimeState State { get; }

    event Action<ApplicationRuntimeState>? StateChanged;

    Task StartAsync(TimeSpan refreshInterval);

    Task UpdateRefreshIntervalAsync(TimeSpan refreshInterval);

    Task RefreshAsync(bool refreshGattBattery = false);

    Task StopAsync();
}
