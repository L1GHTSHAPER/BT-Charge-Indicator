using BTChargeIndicator.Services;
using Windows.Devices.Bluetooth;

var fastPairShow = FastPairBatteryService.ParseServiceData(
    [0x2C, 0xFE, 0x00, 0xAA, 0xBB, 0x33, 0x57, 0x41, 0x7F]);
Assert(fastPairShow is not null && fastPairShow.ShouldShow, "Fast Pair show packet");
Assert(fastPairShow!.Battery.LeftPercent == 87, "Fast Pair left battery");
Assert(fastPairShow.Battery.RightPercent == 65, "Fast Pair right battery");
Assert(fastPairShow.Battery.CasePercent is null, "Fast Pair unknown case");

var fastPairHide = FastPairBatteryService.ParseServiceData(
    [0x2C, 0xFE, 0x00, 0xAA, 0xBB, 0x34, 0xD7, 0xC1, 0xBC]);
Assert(fastPairHide is not null && !fastPairHide.ShouldShow, "Fast Pair hide packet");
Assert(fastPairHide!.Battery.LeftPercent == 87, "Fast Pair charging flag");
Assert(fastPairHide.Battery.CasePercent == 60, "Fast Pair case battery");

var response = NothingBatteryService.BuildFrame(
    0x4007,
    [3, 2, 85, 3, 90, 4, 60],
    1);
var fragmented = new List<byte>(response[..6]);
var partial = NothingBatteryService.ParseIncoming(fragmented);
Assert(partial.Battery is null && fragmented.Count == 6, "Nothing fragmented frame retained");
fragmented.AddRange(response[6..]);
var nothing = NothingBatteryService.ParseIncoming(fragmented);
Assert(nothing.Battery?.LeftPercent == 85, "Nothing left battery");
Assert(nothing.Battery?.RightPercent == 90, "Nothing right battery");
Assert(nothing.Battery?.CasePercent == 60, "Nothing case battery");
Assert(fragmented.Count == 0, "Nothing frame consumed");

var legacy = new List<byte>([0x03, 0x03, 0x00, 0x03, 70, 80, 50]);
var legacyReading = NothingBatteryService.ParseIncoming(legacy).Battery;
Assert(legacyReading?.LeftPercent == 70, "Nothing legacy left battery");
Assert(legacyReading?.RightPercent == 80, "Nothing legacy right battery");
Assert(legacyReading?.CasePercent == 50, "Nothing legacy case battery");

Console.WriteLine("Protocol parser tests passed.");

var deviceArgument = Array.IndexOf(args, "--device");
if (deviceArgument >= 0 && deviceArgument + 1 < args.Length)
{
    var service = new NothingBatteryService();
    var live = await service.GetReadingAsync(args[deviceArgument + 1], TimeSpan.FromSeconds(8));
    Console.WriteLine(live is null
        ? "Live Nothing/CMF reading: unavailable"
        : $"Live Nothing/CMF reading: L={live.LeftPercent}% R={live.RightPercent}% case={live.CasePercent}%");

    if (live is null && ulong.TryParse(
            args[deviceArgument + 1],
            System.Globalization.NumberStyles.HexNumber,
            null,
            out var address))
    {
        using var device = await BluetoothDevice.FromBluetoothAddressAsync(address);
        Console.WriteLine(device is null
            ? "BluetoothDevice: unavailable"
            : $"BluetoothDevice: {device.Name}; connection={device.ConnectionStatus}");
        if (device is not null)
        {
            var services = await device.GetRfcommServicesAsync(BluetoothCacheMode.Uncached);
            Console.WriteLine($"RFCOMM enumeration: {services.Error}; count={services.Services.Count}");
            foreach (var item in services.Services)
            {
                Console.WriteLine(
                    $"  {item.ServiceId.Uuid:D}; service={item.ConnectionServiceName}; host={item.ConnectionHostName}");
                item.Dispose();
            }
        }
    }
}

if (args.Contains("--scan", StringComparer.OrdinalIgnoreCase))
{
    using var scanner = new BluetoothBatteryService();
    var result = await scanner.ScanAsync();
    Console.WriteLine($"Bluetooth scan: {result.Availability}; devices={result.Devices.Count}");
    foreach (var device in result.Devices)
    {
        Console.WriteLine(
            $"  {device.Name}: {device.BatteryPercent}%; connected={device.IsConnected}; " +
            $"source={device.BatterySource}; components={device.Components}");
    }
}

static void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Failed: {name}");
    }
}
