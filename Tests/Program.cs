using System.Text.Json;
using BTChargeIndicator.Settings;
using BTChargeIndicator.Services;
using BTChargeIndicator.Models;
using Windows.Devices.Bluetooth;

await LocalizationTests.RunAsync();

var redmiDevice = new BluetoothBatteryDevice(
    "windows-device-id",
    "Redmi Buds 6 Pro",
    55,
    true,
    null,
    Guid.Parse("863f1b84-9ff7-508a-aa4a-7126cc525e46"))
{
    BatterySource = BatteryReadingSource.GoogleFastPairAdvertisement,
    Categories = ["Communication.Headset.Bluetooth"],
    Components = new BatteryComponents(100, 100, 55),
    ObservedAtUtc = new DateTimeOffset(2026, 11, 4, 12, 30, 0, TimeSpan.Zero)
};
var normalizedSnapshot = BatterySnapshot.FromDevice(redmiDevice);
Assert(normalizedSnapshot.DevicePercent == 55 &&
       normalizedSnapshot.GetComponent(BatteryComponentKind.Left) == 100 &&
       normalizedSnapshot.GetComponent(BatteryComponentKind.Case) == 55,
    "Normalized snapshots preserve the scalar and typed component values");
Assert(normalizedSnapshot.ObservedAtUtc == redmiDevice.ObservedAtUtc &&
       normalizedSnapshot.Source == BatteryReadingSource.GoogleFastPairAdvertisement &&
       normalizedSnapshot.IsConnected == true,
    "Normalized snapshots preserve observation time, source, and connection state");

var classicEndpoint = redmiDevice with
{
    Id = "classic-endpoint",
    Address = "AA:BB:CC:DD:EE:01",
    BatteryPercent = 55,
    Components = new BatteryComponents(100, null, 55)
};
var bleEndpoint = redmiDevice with
{
    Id = "ble-endpoint",
    Address = "AA:BB:CC:DD:EE:02",
    BatteryPercent = 100,
    Components = new BatteryComponents(null, 100, null)
};
var physicalDevices = BatteryDeviceIdentity.Deduplicate([classicEndpoint, bleEndpoint]);
Assert(physicalDevices.Count == 1 &&
       physicalDevices[0].Components is { LeftPercent: 100, RightPercent: 100, CasePercent: 55 },
    "Endpoints with the same Windows container are merged once with all known components preserved");
var invalidSnapshot = BatterySnapshot.FromDevice(redmiDevice with
{
    BatteryPercent = 150,
    Components = new BatteryComponents(-1, 101, null)
});
Assert(invalidSnapshot.DevicePercent is null && invalidSnapshot.Components.Count == 0,
    "Normalized snapshots discard invalid percentages instead of treating them as valid readings");
var defaultProfile = BatteryProfileEngine.CreateDefaultProfile();
var redmiProfile = new BatteryProfile
{
    Id = "redmi-buds",
    Name = "Redmi Buds",
    Match = new BatteryProfileMatch
    {
        NameContains = ["Redmi Buds"],
        Sources = [BatteryReadingSource.GoogleFastPairAdvertisement]
    },
    AggregationMode = BatteryAggregationMode.EarbudsLowest
};
var caseProfile = new BatteryProfile
{
    Id = "case-priority",
    Name = "Case priority",
    AggregationMode = BatteryAggregationMode.Case
};
var resolvedRedmiProfile = BatteryProfileEngine.Resolve(
    redmiDevice, [defaultProfile, redmiProfile]);
Assert(resolvedRedmiProfile.Id == "redmi-buds",
    "Device profile matching uses model and protocol hints");
Assert(BatteryProfileEngine.GetBatteryPercent(redmiDevice, resolvedRedmiProfile) == 100,
    "Default Redmi profile uses earbud battery and ignores the case");
var invalidScalarCaseDevice = redmiDevice with
{
    BatteryPercent = 150,
    Components = new BatteryComponents(null, null, 45)
};
Assert(BatteryProfileEngine.GetBatteryPercent(invalidScalarCaseDevice, defaultProfile) == 45,
    "Earbud profiles fall back to a valid case reading when the scalar value is invalid");
var scalarOnlyDevice = redmiDevice with
{
    BatteryPercent = 78,
    Components = null
};
Assert(BatteryProfileEngine.GetBatteryPercent(scalarOnlyDevice, defaultProfile) == 78,
    "Default aggregation retains scalar battery support for non-component devices");
Assert(BatteryProfileEngine.GetBatteryPercent(scalarOnlyDevice, leftOnlyProfile) is null,
    "A left-only profile does not invent a left reading for scalar-only devices");
Assert(BatteryProfileEngine.Resolve(
        redmiDevice, [defaultProfile, redmiProfile, caseProfile], "case-priority").Id == "case-priority",
    "Explicit device profile assignment overrides automatic matching");
Assert(BatteryProfileEngine.GetBatteryPercent(redmiDevice, caseProfile) == 55,
    "A selected case profile can display the charging case");

var missingLeftDevice = redmiDevice with
{
    Components = new BatteryComponents(null, 100, 55)
};
var leftOnlyProfile = new BatteryProfile
{
    Id = "left-only",
    Name = "Left only",
    AggregationMode = BatteryAggregationMode.Left
};
Assert(BatteryProfileEngine.GetBatteryPercent(missingLeftDevice, leftOnlyProfile) is null,
    "A missing explicitly selected component stays unknown instead of falling back to another component");

var containerSpecificProfile = new BatteryProfile
{
    Id = "container-specific",
    Name = "Container-specific",
    Match = new BatteryProfileMatch
    {
        ContainerIds = ["863f1b84-9ff7-508a-aa4a-7126cc525e46"]
    },
    AggregationMode = BatteryAggregationMode.Case
};
Assert(BatteryProfileEngine.Resolve(
        redmiDevice, [defaultProfile, redmiProfile, containerSpecificProfile]).Id == "container-specific",
    "Stable Windows container identity outranks model and protocol hints");

var legacySettings = JsonSerializer.Deserialize<AppSettings>(
    "{\"Language\":2,\"RefreshIntervalSeconds\":300,\"Devices\":{\"address:AA:BB\":{\"CustomName\":\"My buds\"}}}")!;
legacySettings.Normalize();
Assert(legacySettings.GlobalAggregationMode == BatteryAggregationMode.Lowest &&
       legacySettings.Devices["address:AA:BB"].CustomName == "My buds",
    "Legacy settings keep their values and receive defaults for new profile settings");

var customRuleProfile = new BatteryProfile
{
    Id = "custom-earbud-case",
    Name = "Custom earbud and case",
    AggregationMode = BatteryAggregationMode.Custom,
    CustomRule = new BatteryAggregationRule
    {
        Components = [BatteryComponentKind.Left, BatteryComponentKind.Case],
        Operation = BatteryAggregationOperation.Minimum
    }
};
Assert(BatteryProfileEngine.IsValidProfile(customRuleProfile),
    "Safe custom aggregation rule validates");
Assert(BatteryProfileEngine.GetBatteryPercent(redmiDevice, customRuleProfile) == 55,
    "Custom aggregation selects only configured components");
customRuleProfile.CustomRule!.Components.Add(BatteryComponentKind.Left);
Assert(!BatteryProfileEngine.IsValidProfile(customRuleProfile),
    "Custom aggregation rejects duplicate components");

var profileTempDirectory = Path.Combine(Path.GetTempPath(), "BTChargeIndicatorTests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(profileTempDirectory);
try
{
    var profilePath = Path.Combine(profileTempDirectory, "profiles.json");
    var profileStore = new BatteryProfileStore(profilePath);
    profileStore.Save([redmiProfile, caseProfile]);
    profileStore.Save([redmiProfile, caseProfile, new BatteryProfile
    {
        Id = "custom-test",
        Name = "Custom test",
        AggregationMode = BatteryAggregationMode.Custom,
        CustomRule = new BatteryAggregationRule
        {
            Components = [BatteryComponentKind.Right, BatteryComponentKind.Case],
            Operation = BatteryAggregationOperation.Maximum
        }
    }]);
    var loadedProfiles = profileStore.Load();
    Assert(loadedProfiles.Count == 4,
        "Profile store round-trips custom profiles and supplies the built-in default");
    Assert(loadedProfiles.Any(profile => profile.Id == "redmi-buds"),
        "Profile store preserves matching criteria");
    Assert(loadedProfiles.Any(profile => profile.Id == "custom-test" &&
        profile.CustomRule?.Components.SequenceEqual(
            [BatteryComponentKind.Right, BatteryComponentKind.Case]) == true),
        "Profile store round-trips declarative custom rules");

    File.WriteAllText(profilePath, "{ invalid json");
    var recoveredProfiles = profileStore.Load();
    Assert(recoveredProfiles.Count == 3 && recoveredProfiles.Any(profile => profile.Id == "redmi-buds"),
        "Malformed profile JSON recovers the previous valid backup");
    Assert(File.ReadAllText(profilePath) == "{ invalid json",
        "Malformed profile JSON is preserved for manual recovery");
    Assert(File.Exists(profilePath + ".bak"),
        "Profile store keeps a backup of the previous valid document");
}
finally
{
    Directory.Delete(profileTempDirectory, true);
}


var aggregationComponents = new BatteryComponents(100, 80, 15);
Assert(BatteryAggregationEngine.Aggregate(aggregationComponents, BatteryAggregationMode.EarbudsLowest) == 80,
    "Earbuds minimum excludes the case");
Assert(BatteryAggregationEngine.Aggregate(aggregationComponents, BatteryAggregationMode.EarbudsAverage) == 90,
    "Earbuds average excludes the case");
Assert(BatteryAggregationEngine.Aggregate(aggregationComponents, BatteryAggregationMode.Lowest) == 15,
    "All-components minimum includes the case when explicitly selected");
Assert(BatteryAggregationEngine.Aggregate(aggregationComponents, BatteryAggregationMode.Highest) == 100,
    "All-components maximum includes all known components");
Assert(BatteryAggregationEngine.Aggregate(aggregationComponents, BatteryAggregationMode.Case) == 15,
    "Case-only aggregation selects the case");
Assert(BatteryAggregationEngine.Aggregate(new BatteryComponents(null, 72, 25), BatteryAggregationMode.EarbudsLowest) == 72,
    "Earbuds aggregation uses the one known earbud");
Assert(BatteryAggregationEngine.Aggregate(new BatteryComponents(null, null, 42), BatteryAggregationMode.EarbudsLowest) == 42,
    "Earbuds aggregation falls back to case-only readings");
Assert(BatteryAggregationEngine.Aggregate(new BatteryComponents(null, null, null), BatteryAggregationMode.EarbudsLowest) is null,
    "Unknown components remain unavailable");
Assert(BatteryAggregationEngine.Aggregate(new BatteryComponents(150, -1, 45), BatteryAggregationMode.EarbudsLowest) == 45,
    "Invalid component values are ignored and case fallback is used");
Assert(BatteryAggregationEngine.AggregateValues([90, null, 60, 80], BatteryAggregationMode.Lowest) == 60,
    "Global minimum ignores missing values");
Assert(BatteryAggregationEngine.AggregateValues([90, null, 60, 80], BatteryAggregationMode.Average) == 77,
    "Global average ignores missing values and rounds predictably");
Assert(BatteryAggregationEngine.AggregateValues([null, null], BatteryAggregationMode.Lowest) is null,
    "Global aggregation returns unknown when every device is unknown");


var earbudsWithCase = new TwsBatteryReading(100, 100, 55);
Assert(earbudsWithCase.LowestPercent == 100,
    "TWS primary percentage ignores a lower charging-case value");
Assert(earbudsWithCase.Components.CasePercent == 55,
    "TWS case percentage remains available as a separate component");

var oneEarbudWithCase = new TwsBatteryReading(null, 72, 25);
Assert(oneEarbudWithCase.LowestPercent == 72,
    "TWS primary percentage uses the known earbud when the other is unavailable");

var caseOnly = new TwsBatteryReading(null, null, 42);
Assert(caseOnly.LowestPercent == 42,
    "TWS case-only reading remains a fallback when no earbud level is known");

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

var notifications = new DeviceConnectionTracker(device => device.Address ?? device.Id);
var available = BluetoothAvailability.Available;
Assert(notifications.Observe([], BluetoothAvailability.Error).Count == 0, "Failed startup scan is quiet");
Assert(notifications.Observe([ConnectionDevice("headset", true)], available).Count == 0,
    "Already-connected startup devices do not produce popups");
Assert(notifications.Observe([ConnectionDevice("headset", true, 85)], available).Count == 0,
    "Battery updates do not produce connection popups");
Assert(notifications.Observe([ConnectionDevice("headset", null)], available).Count == 0,
    "Unknown connection reading preserves known state");
Assert(notifications.Observe([], BluetoothAvailability.Error).Count == 0,
    "Scan failure does not disconnect devices");
Assert(notifications.Observe([], BluetoothAvailability.AccessDenied).Count == 0,
    "Access denial does not disconnect devices");
var disconnected = notifications.Observe([ConnectionDevice("headset", false)], available);
Assert(disconnected.Count == 1 && !disconnected[0].IsConnected,
    "Known disconnect produces one popup after unknown and failed readings");
Assert(notifications.Observe([ConnectionDevice("headset", false)], available).Count == 0,
    "Repeated disconnected scan is quiet");
var reconnected = notifications.Observe([ConnectionDevice("headset", true)], available);
Assert(reconnected.Count == 1 && reconnected[0].IsConnected, "Reconnect produces one popup");
var removed = notifications.Observe([], available);
Assert(removed.Count == 1 && !removed[0].IsConnected && removed[0].Device.Name == "headset",
    "Disappearing connected device preserves its name in one disconnect popup");
Assert(notifications.Observe([], available).Count == 0, "Repeated empty scan is quiet");

var dualEndpointNotifications = new DeviceConnectionTracker(device => device.Address ?? device.Id);
dualEndpointNotifications.Observe([], available);
var dualConnected = dualEndpointNotifications.Observe([
    ConnectionDevice("classic", true, address: "AA:BB"),
    ConnectionDevice("ble", false, address: "aa:bb")], available);
Assert(dualConnected.Count == 1 && dualConnected[0].IsConnected,
    "Classic and BLE endpoints produce one physical device popup");
Assert(dualEndpointNotifications.Observe([
    ConnectionDevice("classic", false, address: "AA:BB"),
    ConnectionDevice("ble", true, address: "aa:bb")], available).Count == 0,
    "One connected endpoint prevents a false disconnect popup");
var radioOff = dualEndpointNotifications.Observe([], BluetoothAvailability.TurnedOff);
Assert(radioOff.Count == 1 && !radioOff[0].IsConnected, "Radio off produces one physical disconnect popup");

Console.WriteLine("Protocol parser, Bluetooth device change, and connection notification tests passed.");

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

static BluetoothBatteryDevice ConnectionDevice(string id, bool? connected, int? battery = 76, string? address = null) =>
    new(id, id, battery, connected, address, null);

static void Assert(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Failed: {name}");
    }
}
