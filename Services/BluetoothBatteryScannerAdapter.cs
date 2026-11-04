using BTChargeIndicator.Models;
using BTChargeIndicator.Runtime;

namespace BTChargeIndicator.Services;

/// <summary>
/// Bridges the existing Windows Bluetooth acquisition service to the shared runtime.
/// The protocol readers and scan behavior remain owned by BluetoothBatteryService.
/// </summary>
internal sealed class BluetoothBatteryScannerAdapter : IBluetoothBatteryScanner, IDisposable
{
    private readonly BluetoothBatteryService _bluetoothBatteryService = new();
    private bool _disposed;

    public Task<BluetoothScanResult> ScanAsync(bool refreshGattBattery = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _bluetoothBatteryService.ScanAsync(refreshGattBattery);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _bluetoothBatteryService.Dispose();
    }
}
