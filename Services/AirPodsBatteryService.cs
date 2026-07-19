using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace BTChargeIndicator.Services;

internal sealed class AirPodsBatteryService : IDisposable
{
    private const ushort AppleCompanyId = 0x004C;
    private const byte ProximityPairingMessageType = 0x07;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(10);

    private readonly object _syncRoot = new();
    private readonly BluetoothLEAdvertisementWatcher _watcher;
    private readonly Dictionary<ulong, CachedReading> _readings = [];

    private bool _disposed;

    public AirPodsBatteryService()
    {
        _watcher = new BluetoothLEAdvertisementWatcher
        {
            ScanningMode = BluetoothLEScanningMode.Passive
        };
        _watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(
            new BluetoothLEManufacturerData { CompanyId = AppleCompanyId });
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
            // Stopping an already-aborted watcher is harmless for this optional fallback.
        }

        lock (_syncRoot)
        {
            _readings.Clear();
        }
    }

    public async Task<AirPodsBatteryReading?> GetReadingAsync(TimeSpan timeout)
    {
        var cached = GetLatestReading();
        if (cached is not null)
        {
            return cached;
        }

        var completion = new TaskCompletionSource<AirPodsBatteryReading>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void ReadingReceived(AirPodsBatteryReading reading) => completion.TrySetResult(reading);

        ReadingAvailable += ReadingReceived;
        try
        {
            cached = GetLatestReading();
            if (cached is not null)
            {
                return cached;
            }

            EnsureStarted();

            try
            {
                return await completion.Task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                return GetLatestReading();
            }
        }
        finally
        {
            ReadingAvailable -= ReadingReceived;
        }
    }

    internal static AirPodsBatteryReading? ParseAdvertisementPayload(byte[] payload)
    {
        // ManufacturerData.Data starts after Apple's 0x004C company identifier.
        // Battery nibbles are in the first eight bytes of a proximity-pairing message.
        if (payload.Length < 8 || payload[0] != ProximityPairingMessageType)
        {
            return null;
        }

        var isFlipped = (((payload[5] >> 4) & 0x02) == 0);
        var firstPod = DecodeBatteryNibble((payload[6] >> 4) & 0x0F);
        var secondPod = DecodeBatteryNibble(payload[6] & 0x0F);
        var left = isFlipped ? firstPod : secondPod;
        var right = isFlipped ? secondPod : firstPod;
        var chargingCase = DecodeBatteryNibble(payload[7] & 0x0F);

        var knownLevels = new[] { left, right, chargingCase }
            .OfType<int>()
            .ToArray();
        if (knownLevels.Length == 0)
        {
            return null;
        }

        return new AirPodsBatteryReading(
            ReadModelName(payload[3] & 0x0F),
            knownLevels.Min(),
            left,
            right,
            chargingCase);
    }

    private event Action<AirPodsBatteryReading>? ReadingAvailable;

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
            // Bluetooth may be unavailable or access may be denied. The main scan handles both.
        }
    }

    private void Watcher_Received(
        BluetoothLEAdvertisementWatcher sender,
        BluetoothLEAdvertisementReceivedEventArgs args)
    {
        try
        {
            foreach (var manufacturerData in args.Advertisement.ManufacturerData)
            {
                if (manufacturerData.CompanyId != AppleCompanyId || manufacturerData.Data.Length == 0)
                {
                    continue;
                }

                var payload = new byte[manufacturerData.Data.Length];
                using var reader = DataReader.FromBuffer(manufacturerData.Data);
                reader.ReadBytes(payload);

                var reading = ParseAdvertisementPayload(payload);
                if (reading is null)
                {
                    continue;
                }

                lock (_syncRoot)
                {
                    _readings[args.BluetoothAddress] = new CachedReading(
                        reading,
                        DateTimeOffset.UtcNow,
                        args.RawSignalStrengthInDBm);
                    RemoveExpiredReadings();
                }

                ReadingAvailable?.Invoke(reading);
            }
        }
        catch
        {
            // Malformed or concurrently removed advertisements are ignored.
        }
    }

    private AirPodsBatteryReading? GetLatestReading()
    {
        lock (_syncRoot)
        {
            RemoveExpiredReadings();
            if (_readings.Count == 0)
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

    private static int? DecodeBatteryNibble(int value)
    {
        return value <= 10 ? value * 10 : null;
    }

    private static string ReadModelName(int modelCode)
    {
        return modelCode switch
        {
            0x02 => "AirPods (1st gen)",
            0x0F => "AirPods (2nd gen)",
            0x03 => "AirPods (3rd gen)",
            0x0E => "AirPods Pro",
            0x04 => "AirPods Pro 2",
            0x0A => "AirPods Max",
            _ => "AirPods"
        };
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
        AirPodsBatteryReading Reading,
        DateTimeOffset Timestamp,
        short SignalStrength);
}

internal sealed record AirPodsBatteryReading(
    string ModelName,
    int BatteryPercent,
    int? LeftBatteryPercent,
    int? RightBatteryPercent,
    int? CaseBatteryPercent);
