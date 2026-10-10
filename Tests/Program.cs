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

var tracker = new BluetoothDeviceChangeTracker();
var connectedProperty = BluetoothBatteryService.ConnectedProperty;
var batteryProperty = BluetoothBatteryService.BatteryLifeProperty;
var hfpProperty = BluetoothBatteryService.BluetoothBatteryProperty;
Assert(tracker.Add("headset", new Dictionary<string, object>
{
    [connectedProperty] = false,
    [batteryProperty] = 30
}) == BluetoothDeviceChangeKind.None, "Initial enumeration is coalesced");
Assert(tracker.Add("mouse", new Dictionary<string, object>
{
    [connectedProperty] = true
}) == BluetoothDeviceChangeKind.None, "Initially connected device is part of baseline");
Assert(tracker.CompleteEnumeration() == BluetoothDeviceChangeKind.Refresh,
    "Enumeration completion refreshes startup state");
Assert(tracker.Update("HEADSET", new Dictionary<string, object>
{
    [connectedProperty] = true
}) == BluetoothDeviceChangeKind.Connected, "Reconnect triggers a fresh reading and retry");
Assert(tracker.Update("headset", new Dictionary<string, object>
{
    [connectedProperty] = true,
    [batteryProperty] = 30
}) == BluetoothDeviceChangeKind.None, "Duplicate connection and battery reports are ignored");
Assert(tracker.Update("headset", new Dictionary<string, object>
{
    [batteryProperty] = 90
}) == BluetoothDeviceChangeKind.Refresh, "Delayed battery report triggers refresh");
Assert(tracker.Update("headset", new Dictionary<string, object>
{
    [connectedProperty] = true
}) == BluetoothDeviceChangeKind.None, "Partial battery update preserves connection state");
Assert(tracker.Update("headset", new Dictionary<string, object>
{
    [hfpProperty] = 85
}) == BluetoothDeviceChangeKind.Refresh, "HFP battery updates trigger refresh");
Assert(tracker.Update("headset", new Dictionary<string, object>
{
    [hfpProperty] = 85,
    ["System.ItemNameDisplay"] = "New name"
}) == BluetoothDeviceChangeKind.None, "Unrelated metadata and duplicate HFP updates are ignored");
Assert(tracker.Update("headset", new Dictionary<string, object>
{
    [connectedProperty] = false
}) == BluetoothDeviceChangeKind.Refresh, "Disconnect removes device from tray calculation");
Assert(tracker.Update("headset", new Dictionary<string, object>
{
    [connectedProperty] = true
}) == BluetoothDeviceChangeKind.Connected, "Subsequent reconnect triggers another retry");
Assert(tracker.Update("missing", new Dictionary<string, object>
{
    [connectedProperty] = true
}) == BluetoothDeviceChangeKind.None, "Unknown device update is ignored");
Assert(tracker.Remove("headset") == BluetoothDeviceChangeKind.Refresh,
    "Removed paired device triggers refresh");
Assert(tracker.Remove("headset") == BluetoothDeviceChangeKind.None,
    "Duplicate removal is ignored");
Assert(tracker.Add("new headset", new Dictionary<string, object>
{
    [connectedProperty] = true
}) == BluetoothDeviceChangeKind.Connected, "New connected device triggers reading and retry");
Assert(tracker.Add("new keyboard", new Dictionary<string, object>()) == BluetoothDeviceChangeKind.Refresh,
    "New paired device with unknown connection triggers refresh");
Assert(tracker.Update("new keyboard", new Dictionary<string, object>
{
    [connectedProperty] = true
}) == BluetoothDeviceChangeKind.Connected, "Unknown to connected transition triggers retry");

var enumeratingTracker = new BluetoothDeviceChangeTracker();
enumeratingTracker.Add("headset", new Dictionary<string, object> { [connectedProperty] = false });
Assert(enumeratingTracker.Update("headset", new Dictionary<string, object>
{
    [connectedProperty] = true
}) == BluetoothDeviceChangeKind.Connected, "Connection during initial enumeration is not lost");

Console.WriteLine("Protocol parser and Bluetooth device change tests passed.");

if (args.Contains("--watch", StringComparer.OrdinalIgnoreCase))
{
    using var watcher = new BluetoothDeviceWatcher();
    watcher.Changed += change => Console.WriteLine($"Bluetooth device change: {change}");
    watcher.Start();
    await Task.Delay(TimeSpan.FromSeconds(10));
    Console.WriteLine("Bluetooth device monitoring completed.");
}

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
