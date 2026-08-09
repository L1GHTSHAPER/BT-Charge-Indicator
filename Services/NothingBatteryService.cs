using System.Buffers.Binary;
using BTChargeIndicator.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

namespace BTChargeIndicator.Services;

/// <summary>
/// Reads component battery levels from Nothing and CMF earbuds over their
/// RFCOMM companion-app protocol. The protocol is shared across several Nothing
/// and CMF generations; unsupported firmware simply times out and falls back to
/// the regular Windows battery value.
/// </summary>
internal sealed class NothingBatteryService
{
    private const byte StartOfFrame = 0x55;
    private const ushort HostControlWithCrc = 0x0160;
    private const ushort GetProtocolVersion = 0xC001;
    private const ushort SetActivated = 0xF001;
    private const ushort GetBattery = 0xC007;
    private const ushort BatteryEvent = 0xE001;

    private static readonly Guid PreferredServiceUuid = new("DF21FE2C-2515-4FDB-8886-F12C4D67927C");
    private static readonly Guid SerialPortUuid = RfcommServiceId.SerialPort.Uuid;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromSeconds(45);

    private readonly object _syncRoot = new();
    private readonly Dictionary<ulong, CachedReading> _cache = [];

    public async Task<TwsBatteryReading?> GetReadingAsync(
        string? bluetoothAddress,
        TimeSpan timeout)
    {
        if (!TryParseAddress(bluetoothAddress, out var address))
        {
            return null;
        }

        lock (_syncRoot)
        {
            if (_cache.TryGetValue(address, out var cached) &&
                DateTimeOffset.UtcNow - cached.Timestamp <= CacheLifetime)
            {
                return cached.Reading;
            }
        }

        try
        {
            var reading = await ReadFromDeviceAsync(address, timeout).WaitAsync(timeout);
            if (reading is not null)
            {
                lock (_syncRoot)
                {
                    _cache[address] = new CachedReading(reading, DateTimeOffset.UtcNow);
                }
            }

            return reading;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<TwsBatteryReading?> ReadFromDeviceAsync(
        ulong address,
        TimeSpan timeout)
    {
        using var device = await BluetoothDevice.FromBluetoothAddressAsync(address);
        if (device is null)
        {
            return null;
        }

        var result = await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
        if (result.Error != BluetoothError.Success)
        {
            return null;
        }

        var services = result.Services
            .Where(service => IsCandidateService(service.ServiceId.Uuid))
            .OrderBy(service => ServicePriority(service.ServiceId.Uuid))
            .ToArray();

        foreach (var service in services)
        {
            using (service)
            {
                var reading = await TryReadServiceAsync(service, timeout);
                if (reading is not null)
                {
                    return reading;
                }
            }
        }

        return null;
    }

    private static bool IsCandidateService(Guid uuid)
    {
        if (uuid == PreferredServiceUuid || uuid == SerialPortUuid)
        {
            return true;
        }

        // Vendor-specific RFCOMM services are also tried for product generations
        // that do not expose the common BES service UUID. Explicitly skip BESOTA.
        var text = uuid.ToString("D");
        return !text.Equals("66666666-6666-6666-6666-666666666666", StringComparison.OrdinalIgnoreCase) &&
               !text.EndsWith("-0000-1000-8000-00805f9b34fb", StringComparison.OrdinalIgnoreCase);
    }

    private static int ServicePriority(Guid uuid)
    {
        if (uuid == PreferredServiceUuid)
        {
            return 0;
        }

        if (uuid == SerialPortUuid)
        {
            return 1;
        }

        return 2;
    }

    private static async Task<TwsBatteryReading?> TryReadServiceAsync(
        RfcommDeviceService service,
        TimeSpan timeout)
    {
        using var socket = new StreamSocket();
        try
        {
            var connectTimeout = TimeSpan.FromMilliseconds(Math.Min(timeout.TotalMilliseconds, 1500));
            await socket.ConnectAsync(
                    service.ConnectionHostName,
                    service.ConnectionServiceName,
                    SocketProtectionLevel.BluetoothEncryptionAllowNullAuthentication)
                .AsTask()
                .WaitAsync(connectTimeout);

            using var writer = new DataWriter(socket.OutputStream);
            using var reader = new DataReader(socket.InputStream)
            {
                InputStreamOptions = InputStreamOptions.Partial
            };

            byte sequence = 1;
            var probe = BuildFrame(GetProtocolVersion, [], sequence++);
            var legacyProbe = new byte[] { 0x02, 0x03, 0x00, 0x00 };
            writer.WriteBytes(probe.Concat(legacyProbe).ToArray());
            await writer.StoreAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));

            var buffer = new List<byte>();
            var deadline = DateTimeOffset.UtcNow + timeout;
            var activationSent = false;
            var batteryQuerySent = false;
            var activationSentAt = DateTimeOffset.MinValue;

            while (DateTimeOffset.UtcNow < deadline)
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                var readWindow = remaining < TimeSpan.FromMilliseconds(500)
                    ? remaining
                    : TimeSpan.FromMilliseconds(500);

                try
                {
                    var loaded = await reader.LoadAsync(256).AsTask().WaitAsync(readWindow);
                    if (loaded > 0)
                    {
                        var bytes = new byte[loaded];
                        reader.ReadBytes(bytes);
                        buffer.AddRange(bytes);
                    }
                }
                catch (TimeoutException)
                {
                    // Use the timeout tick to send a battery fallback after activation.
                }

                var parsed = ParseIncoming(buffer);
                if (parsed.Battery is not null)
                {
                    return parsed.Battery;
                }

                if (parsed.ProtocolVersionReceived && !activationSent)
                {
                    writer.WriteBytes(BuildFrame(SetActivated, [], sequence++));
                    await writer.StoreAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
                    activationSent = true;
                    activationSentAt = DateTimeOffset.UtcNow;
                }

                if (!batteryQuerySent &&
                    (parsed.ActivationAcknowledged ||
                     activationSent && DateTimeOffset.UtcNow - activationSentAt >= TimeSpan.FromMilliseconds(700)))
                {
                    writer.WriteBytes(BuildFrame(GetBattery, [], sequence++));
                    await writer.StoreAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
                    batteryQuerySent = true;
                }

            }
        }
        catch
        {
            // Unsupported or busy companion services are expected and skipped.
        }

        return null;
    }

    internal static byte[] BuildFrame(ushort command, byte[] payload, byte sequence)
    {
        var frame = new byte[8 + payload.Length + 2];
        frame[0] = StartOfFrame;
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(1), HostControlWithCrc);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(3), command);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(5), (ushort)payload.Length);
        frame[7] = sequence;
        payload.CopyTo(frame, 8);
        BinaryPrimitives.WriteUInt16LittleEndian(
            frame.AsSpan(8 + payload.Length),
            ComputeCrc16(frame.AsSpan(0, 8 + payload.Length)));
        return frame;
    }

    internal static NothingParseResult ParseIncoming(List<byte> buffer)
    {
        var protocolVersionReceived = false;
        var activationAcknowledged = false;

        while (buffer.Count > 0)
        {
            if (buffer[0] == StartOfFrame)
            {
                if (buffer.Count < 8)
                {
                    break;
                }

                var bytes = buffer.ToArray();
                var control = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(1, 2));
                var rawCommand = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(3, 2));
                var payloadLength = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(5, 2));
                var crcLength = (control & 0x20) != 0 ? 2 : 0;
                var totalLength = 8 + payloadLength + crcLength;
                if (buffer.Count < totalLength)
                {
                    break;
                }

                var payload = bytes.AsSpan(8, payloadLength);
                var command = (ushort)(rawCommand | 0x8000);
                if (command == GetProtocolVersion)
                {
                    protocolVersionReceived = true;
                }
                else if (command == SetActivated)
                {
                    activationAcknowledged = true;
                }
                else if (command is GetBattery or BatteryEvent)
                {
                    var battery = ParseBatteryPayload(payload);
                    buffer.RemoveRange(0, totalLength);
                    if (battery is not null)
                    {
                        return new NothingParseResult(
                            battery,
                            protocolVersionReceived,
                            activationAcknowledged);
                    }

                    continue;
                }

                buffer.RemoveRange(0, totalLength);
                continue;
            }

            if (buffer[0] == 0x03)
            {
                if (buffer.Count < 4)
                {
                    break;
                }

                var payloadLength = (buffer[2] << 8) | buffer[3];
                if (buffer.Count < 4 + payloadLength)
                {
                    break;
                }

                if (buffer[1] == 0x03 && payloadLength >= 2)
                {
                    var left = DecodeLegacyPercentage(buffer[4]);
                    var right = DecodeLegacyPercentage(buffer[5]);
                    var chargingCase = payloadLength >= 3 ? DecodeLegacyPercentage(buffer[6]) : null;
                    buffer.RemoveRange(0, 4 + payloadLength);
                    var reading = new TwsBatteryReading(left, right, chargingCase);
                    if (reading.HasKnownLevel)
                    {
                        return new NothingParseResult(
                            reading,
                            protocolVersionReceived,
                            activationAcknowledged);
                    }

                    continue;
                }

                buffer.RemoveRange(0, 4 + payloadLength);
                continue;
            }

            buffer.RemoveAt(0);
        }

        return new NothingParseResult(
            null,
            protocolVersionReceived,
            activationAcknowledged);
    }

    private static TwsBatteryReading? ParseBatteryPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 3)
        {
            return null;
        }

        int? left = null;
        int? right = null;
        int? chargingCase = null;
        var count = payload[0];

        for (var index = 1; index + 1 < payload.Length && index < 1 + count * 2; index += 2)
        {
            var type = payload[index];
            var percentage = payload[index + 1] & 0x7F;
            if (percentage > 100)
            {
                continue;
            }

            switch (type)
            {
                case 2:
                    left = percentage;
                    break;
                case 3:
                    right = percentage;
                    break;
                case 4:
                    chargingCase = percentage;
                    break;
            }
        }

        var reading = new TwsBatteryReading(left, right, chargingCase);
        return reading.HasKnownLevel ? reading : null;
    }

    private static int? DecodeLegacyPercentage(byte value) => value <= 100 ? value : null;

    private static ushort ComputeCrc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 1) != 0 ? (crc >> 1) ^ 0xA001 : crc >> 1);
            }
        }

        return crc;
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

    private sealed record CachedReading(
        TwsBatteryReading Reading,
        DateTimeOffset Timestamp);
}

internal sealed record NothingParseResult(
    TwsBatteryReading? Battery,
    bool ProtocolVersionReceived,
    bool ActivationAcknowledged);
