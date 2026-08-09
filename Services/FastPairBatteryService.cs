using BTChargeIndicator.Models;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace BTChargeIndicator.Services;

/// <summary>
/// Reads the optional left/right/case battery extension from Google Fast Pair
/// service-data advertisements. Providers normally advertise it briefly when
/// the case is opened, so recent readings are cached between regular scans.
/// </summary>
internal sealed class FastPairBatteryService : IDisposable
{
    private const ushort FastPairServiceId = 0xFE2C;
    private const byte ServiceData16BitUuid = 0x16;
    private const int ShowBatteryType = 0x03;
    private const int HideBatteryType = 0x04;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(2);

    private readonly object _syncRoot = new();
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly Dictionary<ulong, CachedReading> _readings = [];
    private bool _disposed;

    public FastPairBatteryService()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Passive
        };
        _watcher.Received += Watcher_Received;
    }

    public void SetEnabled(bool enabled)
    {
        if (_disposed)
        {
            return;
        }

        if (enabled)
        {
            EnsureStarted();
            return;
        }

        try
        {
            if (_watcher.Status == BluetoothLEAdvertisementWatcherStatus.Started)
            {
                _watcher.Stop();
            }
        }
        catch
        {
            // This is an optional source and a watcher can already be aborted.
        }

        lock (_syncRoot)
        {
            _readings.Clear();
        }
    }

    public FastPairBatteryReading? GetLatestReading(string? bluetoothAddress = null)
    {
        lock (_syncRoot)
        {
            RemoveExpiredReadings();
            if (_readings.Count == 0)
            {
                return null;
            }

            if (TryParseAddress(bluetoothAddress, out var address) &&
                _readings.TryGetValue(address, out var exact))
            {
                return exact.Reading;
            }

            if (!string.IsNullOrWhiteSpace(bluetoothAddress))
            {
                return null;
            }

            var newestTimestamp = _readings.Values.Max(candidate => candidate.Timestamp);
            return _readings.Values
                .Where(candidate => candidate.Timestamp >= newestTimestamp - TimeSpan.FromSeconds(5))
                .OrderByDescending(candidate => candidate.SignalStrength)
                .ThenByDescending(candidate => candidate.Timestamp)
                .First()
                .Reading;
        }
    }

    internal static FastPairBatteryReading? ParseServiceData(byte[] data)
    {
        // AD type 0x16 starts with the little-endian 16-bit service UUID.
        if (data.Length < 8 || data[0] != (FastPairServiceId & 0xFF) || data[1] != (FastPairServiceId >> 8))
        {
            return null;
        }

        var payload = data.AsSpan(2);
        if (payload[0] != 0x00)
        {
            return null;
        }

        // Battery information is the final field in Account Data. Its header is
        // 0bLLLLTTTT; certified TWS providers send L=3 and T=show/hide.
        var batteryHeaderOffset = payload.Length - 4;
        var header = payload[batteryHeaderOffset];
        var count = header >> 4;
        var type = header & 0x0F;
        if (count != 3 || type is not (ShowBatteryType or HideBatteryType))
        {
            return null;
        }

        var left = DecodeBatteryValue(payload[batteryHeaderOffset + 1]);
        var right = DecodeBatteryValue(payload[batteryHeaderOffset + 2]);
        var chargingCase = DecodeBatteryValue(payload[batteryHeaderOffset + 3]);
        if (!left.HasValue && !right.HasValue && !chargingCase.HasValue)
        {
            return null;
        }

        return new FastPairBatteryReading(
            new TwsBatteryReading(left, right, chargingCase),
            type == ShowBatteryType);
    }

    private static int? DecodeBatteryValue(byte value)
    {
        var percentage = value & 0x7F;
        return percentage <= 100 ? percentage : null;
    }

    private void Watcher_Received(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            foreach (var section in args.Advertisement.DataSections)
            {
                if (section.DataType != ServiceData16BitUuid || section.Data.Length == 0)
                {
                    continue;
                }

                var data = new byte[section.Data.Length];
                using var reader = DataReader.FromBuffer(section.Data);
                reader.ReadBytes(data);

                var parsed = ParseServiceData(data);
                if (parsed is null)
                {
                    continue;
                }

                lock (_syncRoot)
                {
                    if (parsed.ShouldShow)
                    {
                        _readings[args.BluetoothAddress] = new CachedReading(
                            parsed,
                            DateTimeOffset.UtcNow,
                            args.RawSignalStrengthInDBm);
                    }
                    else
                    {
                        _readings.Remove(args.BluetoothAddress);
                    }

                    RemoveExpiredReadings();
                }
            }
        }
        catch
        {
            // Ignore malformed or concurrently removed advertisements.
        }
    }

    private void EnsureStarted()
    {
        if (_disposed || _watcher.Status is BluetoothLEAdvertisementWatcherStatus.Started or BluetoothLEAdvertisementWatcherStatus.Stopping)
        {
            return;
        }

        try
        {
            _watcher.Start();
        }
        catch
        {
            // The main scan reports Bluetooth availability and access errors.
        }
    }

    private void RemoveExpiredReadings()
    {
        var oldestAllowed = DateTimeOffset.UtcNow - CacheLifetime;
        var expired = _readings
            .Where(candidate => candidate.Value.Timestamp < oldestAllowed)
            .Select(candidate => candidate.Key)
            .ToArray();

        foreach (var address in expired)
        {
            _readings.Remove(address);
        }
    }

    private static bool TryParseAddress(string? value, out ulong address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace(":", string.Empty).Replace("-", string.Empty);
        return ulong.TryParse(
            normalized,
            System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture,
            out address);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watcher.Received -= Watcher_Received;
        try
        {
            _watcher.Stop();
        }
        catch
        {
            // The watcher can already be stopped or aborted during shutdown.
        }
    }

    private sealed record CachedReading(
        FastPairBatteryReading Reading,
        DateTimeOffset Timestamp,
        short SignalStrength);
}

internal sealed record FastPairBatteryReading(
    TwsBatteryReading Battery,
    bool ShouldShow);
