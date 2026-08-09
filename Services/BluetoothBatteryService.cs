using BTChargeIndicator.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Storage.Streams;

namespace BTChargeIndicator.Services;

internal sealed class BluetoothBatteryService : IDisposable
{
    private const string BatteryLifeProperty = "System.Devices.BatteryLife";
    private const string BluetoothBatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string AddressProperty = "System.Devices.Aep.DeviceAddress";
    private const string AepContainerIdProperty = "System.Devices.Aep.ContainerId";
    private const string AepCategoryProperty = "System.Devices.Aep.Category";
    private const string BluetoothClassMajorProperty = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string DeviceContainerIdProperty = "System.Devices.ContainerId";
    private const string PresentProperty = "System.Devices.Present";

    private static readonly string[] RequestedProperties =
    [
        BatteryLifeProperty,
        BluetoothBatteryProperty,
        ConnectedProperty,
        AddressProperty,
        AepContainerIdProperty,
        AepCategoryProperty,
        BluetoothClassMajorProperty
    ];

    private static readonly string[] PnpBatteryProperties =
    [
        BluetoothBatteryProperty,
        DeviceContainerIdProperty,
        PresentProperty
    ];

    private readonly AirPodsBatteryService _airPodsBatteryService = new();
    private readonly FastPairBatteryService _fastPairBatteryService = new();
    private readonly NothingBatteryService _nothingBatteryService = new();

    public async Task<BluetoothScanResult> ScanAsync()
    {
        var radioState = await GetBluetoothAvailabilityAsync();
        if (radioState is BluetoothAvailability.TurnedOff or BluetoothAvailability.NotFound)
        {
            _airPodsBatteryService.SetEnabled(false);
            _fastPairBatteryService.SetEnabled(false);
            return new BluetoothScanResult([], radioState);
        }

        // Fast Pair battery data is normally advertised for only a few seconds
        // when a TWS case opens. Keep the watcher alive so the next regular
        // refresh can use a packet that arrived between scans.
        _fastPairBatteryService.SetEnabled(true);

        try
        {
            var classicTask = FindPairedDevicesAsync(
                BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                useGattFallback: false);
            var lowEnergyTask = FindPairedDevicesAsync(
                BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                useGattFallback: true);
            var pnpBatteryTask = FindPnpBatteryReadingsAsync();
            var dualSenseBatteryTask = DualSenseBatteryService.FindReadingsAsync();

            await Task.WhenAll(classicTask, lowEnergyTask, pnpBatteryTask, dualSenseBatteryTask);
            var devices = MergeDuplicates(classicTask.Result.Concat(lowEnergyTask.Result));
            devices = ApplyPnpBatteryReadings(devices, pnpBatteryTask.Result);
            devices = ApplyDualSenseBatteryReadings(devices, dualSenseBatteryTask.Result);

            var needsAirPodsFallback = devices.Any(device =>
                IsLikelyAirPods(device) &&
                device.IsConnected != false &&
                device.Components is null);
            _airPodsBatteryService.SetEnabled(needsAirPodsFallback);

            if (needsAirPodsFallback)
            {
                var airPodsReading = await _airPodsBatteryService.GetReadingAsync(TimeSpan.FromSeconds(2));
                if (airPodsReading is not null)
                {
                    devices = ApplyAirPodsBatteryReading(devices, airPodsReading);
                }
            }

            var nothingCandidates = devices
                .Where(device =>
                    IsLikelyNothingOrCmf(device) &&
                    device.IsConnected != false &&
                    device.Components is null)
                .ToArray();
            if (nothingCandidates.Length > 0)
            {
                var reads = nothingCandidates.Select(async device => new NothingDeviceReading(
                    device.Id,
                    await _nothingBatteryService.GetReadingAsync(
                        device.Address,
                        TimeSpan.FromSeconds(4))));
                devices = ApplyNothingBatteryReadings(devices, await Task.WhenAll(reads));
            }

            var needsFastPairFallback = devices.Any(device =>
                IsLikelyTws(device) &&
                device.IsConnected != false &&
                device.Components is null);
            if (needsFastPairFallback)
            {
                devices = ApplyFastPairBatteryReading(devices);
            }

            return new BluetoothScanResult(devices, BluetoothAvailability.Available);
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLogger.Error("Bluetooth access was denied.", ex);
            return new BluetoothScanResult([], BluetoothAvailability.AccessDenied, ex.Message);
        }
        catch (Exception ex)
        {
            AppLogger.Error("Bluetooth scan failed.", ex);
            return new BluetoothScanResult([], BluetoothAvailability.Error, ex.Message);
        }
    }

    private static async Task<BluetoothAvailability> GetBluetoothAvailabilityAsync()
    {
        try
        {
            var radios = await Radio.GetRadiosAsync();
            var bluetoothRadios = radios.Where(radio => radio.Kind == RadioKind.Bluetooth).ToArray();

            if (bluetoothRadios.Length == 0)
            {
                return BluetoothAvailability.NotFound;
            }

            return bluetoothRadios.Any(radio => radio.State == RadioState.On)
                ? BluetoothAvailability.Available
                : BluetoothAvailability.TurnedOff;
        }
        catch (UnauthorizedAccessException)
        {
            return BluetoothAvailability.AccessDenied;
        }
        catch
        {
            // Некоторые адаптеры не публикуют Radio, но всё ещё доступны через DeviceInformation.
            return BluetoothAvailability.Available;
        }
    }

    private static async Task<IReadOnlyList<BluetoothBatteryDevice>> FindPairedDevicesAsync(
        string selector,
        bool useGattFallback)
    {
        var information = await DeviceInformation.FindAllAsync(selector, RequestedProperties);

        // Селектор уже ограничен сопряжёнными устройствами. У некоторых Win32-приложений
        // Windows при этом ошибочно возвращает Pairing.IsPaired=false, поэтому повторная
        // проверка этого флага скрывала весь найденный список.
        var devices = information
            .Where(device => !string.IsNullOrWhiteSpace(device.Name))
            .Select(CreateDevice)
            .ToArray();

        if (!useGattFallback)
        {
            return devices;
        }

        var reads = devices.Select(async device =>
        {
            // Не инициируем GATT-подключение к выключенным устройствам. Это может задержать
            // всё обновление на десятки секунд, если Windows не знает состояние соединения.
            if (device.IsConnected != true ||
                device.BatteryPercent is not null && !IsLikelyTws(device))
            {
                return device;
            }

            GattBatteryReading? battery;
            try
            {
                battery = await TryReadGattBatteryAsync(device.Id)
                    .WaitAsync(TimeSpan.FromSeconds(4));
            }
            catch (TimeoutException)
            {
                battery = null;
            }

            return device with
            {
                BatteryPercent = battery?.BatteryPercent ?? device.BatteryPercent,
                Components = battery?.Components ?? device.Components,
                BatterySource = battery is null
                    ? device.BatterySource
                    : battery.Components is null
                        ? BatteryReadingSource.BluetoothGatt
                        : BatteryReadingSource.BluetoothGattComponents
            };
        });

        return await Task.WhenAll(reads);
    }

    private static IReadOnlyList<BluetoothBatteryDevice> MergeDuplicates(
        IEnumerable<BluetoothBatteryDevice> devices)
    {
        return devices
            .GroupBy(
                device => !string.IsNullOrWhiteSpace(device.Address)
                    ? device.Address
                    : device.Name,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(device => device.Components is not null)
                .ThenByDescending(device => device.BatteryPercent.HasValue)
                .ThenByDescending(device => device.IsConnected == true)
                .First())
            .OrderByDescending(device => device.IsConnected == true)
            .ThenBy(device => device.BatteryPercent ?? int.MaxValue)
            .ThenBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static async Task<IReadOnlyList<PnpBatteryReading>> FindPnpBatteryReadingsAsync()
    {
        var information = await DeviceInformation.FindAllAsync(
            string.Empty,
            PnpBatteryProperties,
            DeviceInformationKind.Device);

        return information
            .Select(device => new PnpBatteryReading(
                device.Name.Trim(),
                ReadPercentage(device, BluetoothBatteryProperty),
                ReadGuid(device, DeviceContainerIdProperty),
                ReadBoolean(device, PresentProperty)))
            .Where(reading => reading.BatteryPercent.HasValue && reading.IsPresent != false)
            .ToArray();
    }

    private static IReadOnlyList<BluetoothBatteryDevice> ApplyPnpBatteryReadings(
        IReadOnlyList<BluetoothBatteryDevice> devices,
        IReadOnlyList<PnpBatteryReading> readings)
    {
        return devices.Select(device =>
        {
            if (device.BatteryPercent.HasValue)
            {
                return device;
            }

            var reading = readings.FirstOrDefault(candidate =>
                device.ContainerId.HasValue &&
                candidate.ContainerId.HasValue &&
                device.ContainerId.Value == candidate.ContainerId.Value);

            reading ??= readings.FirstOrDefault(candidate =>
                candidate.Name.StartsWith(device.Name, StringComparison.CurrentCultureIgnoreCase));

            return reading is null
                ? device
                : device with
                {
                    BatteryPercent = reading.BatteryPercent,
                    IsConnected = device.IsConnected ?? true,
                    BatterySource = BatteryReadingSource.PlugAndPlay
                };
        }).ToArray();
    }

    private static IReadOnlyList<BluetoothBatteryDevice> ApplyDualSenseBatteryReadings(
        IReadOnlyList<BluetoothBatteryDevice> devices,
        IReadOnlyList<DualSenseBatteryReading> readings)
    {
        if (readings.Count == 0)
        {
            return devices;
        }

        var likelyDualSenseDevices = devices.Count(IsLikelyDualSense);

        return devices.Select(device =>
        {
            if (device.BatteryPercent.HasValue)
            {
                return device;
            }

            var reading = readings.FirstOrDefault(candidate =>
                device.ContainerId.HasValue &&
                candidate.ContainerId.HasValue &&
                device.ContainerId.Value == candidate.ContainerId.Value);

            // Some Bluetooth stacks do not expose the same container ID for the AEP and HID
            // interfaces. A name-based fallback is safe when exactly one controller is present.
            if (reading is null && readings.Count == 1 && likelyDualSenseDevices == 1 && IsLikelyDualSense(device))
            {
                reading = readings[0];
            }

            return reading is null
                ? device
                : device with
                {
                    BatteryPercent = reading.BatteryPercent,
                    IsConnected = true,
                    BatterySource = BatteryReadingSource.DualSenseHid
                };
        }).ToArray();
    }

    private static bool IsLikelyDualSense(BluetoothBatteryDevice device)
    {
        return device.Name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
               device.Name.Equals("Wireless Controller", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<BluetoothBatteryDevice> ApplyAirPodsBatteryReading(
        IReadOnlyList<BluetoothBatteryDevice> devices,
        AirPodsBatteryReading reading)
    {
        var candidates = devices
            .Where(device =>
                IsLikelyAirPods(device) &&
                device.IsConnected != false &&
                device.Components is null)
            .ToArray();

        if (candidates.Length != 1)
        {
            return devices;
        }

        var targetId = candidates[0].Id;
        return devices.Select(device => device.Id == targetId
            ? device with
            {
                BatteryPercent = reading.BatteryPercent,
                IsConnected = true,
                BatterySource = BatteryReadingSource.AirPodsAdvertisement,
                Components = new BatteryComponents(
                    reading.LeftBatteryPercent,
                    reading.RightBatteryPercent,
                    reading.CaseBatteryPercent)
            }
            : device).ToArray();
    }

    private static bool IsLikelyAirPods(BluetoothBatteryDevice device)
    {
        return device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
               device.Name.Contains("Air Pods", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<BluetoothBatteryDevice> ApplyNothingBatteryReadings(
        IReadOnlyList<BluetoothBatteryDevice> devices,
        IEnumerable<NothingDeviceReading> readings)
    {
        var values = readings
            .Where(value => value.Reading is not null)
            .ToDictionary(value => value.Id, value => value.Reading!, StringComparer.OrdinalIgnoreCase);

        return devices.Select(device => values.TryGetValue(device.Id, out var reading)
            ? ApplyComponentReading(device, reading, BatteryReadingSource.NothingRfcomm)
            : device).ToArray();
    }

    private IReadOnlyList<BluetoothBatteryDevice> ApplyFastPairBatteryReading(
        IReadOnlyList<BluetoothBatteryDevice> devices)
    {
        var candidates = devices
            .Where(device =>
                IsLikelyTws(device) &&
                device.IsConnected != false &&
                device.Components is null)
            .ToArray();

        var exactMatches = candidates
            .Select(device => new
            {
                Device = device,
                Reading = _fastPairBatteryService.GetLatestReading(device.Address)
            })
            .Where(match => match.Reading?.ShouldShow == true)
            .ToArray();

        BluetoothBatteryDevice? target;
        FastPairBatteryReading? reading;
        if (exactMatches.Length == 1)
        {
            target = exactMatches[0].Device;
            reading = exactMatches[0].Reading;
        }
        else if (exactMatches.Length == 0 && candidates.Length == 1)
        {
            target = candidates[0];
            reading = _fastPairBatteryService.GetLatestReading();
        }
        else
        {
            return devices;
        }

        if (reading?.ShouldShow != true)
        {
            return devices;
        }

        return devices.Select(device => device.Id == target.Id
            ? ApplyComponentReading(
                device,
                reading.Battery,
                BatteryReadingSource.GoogleFastPairAdvertisement)
            : device).ToArray();
    }

    private static BluetoothBatteryDevice ApplyComponentReading(
        BluetoothBatteryDevice device,
        TwsBatteryReading reading,
        BatteryReadingSource source)
    {
        return device with
        {
            BatteryPercent = reading.LowestPercent ?? device.BatteryPercent,
            IsConnected = true,
            BatterySource = source,
            Components = reading.Components
        };
    }

    private static bool IsLikelyNothingOrCmf(BluetoothBatteryDevice device)
    {
        return device.Name.Contains("Nothing", StringComparison.OrdinalIgnoreCase) ||
               device.Name.Contains("CMF", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyTws(BluetoothBatteryDevice device)
    {
        var name = device.Name;
        return IsLikelyAirPods(device) ||
               IsLikelyNothingOrCmf(device) ||
               device.BluetoothClassMajor == 4 ||
               device.Categories.Any(category =>
                   category.Contains("Audio", StringComparison.OrdinalIgnoreCase) ||
                   category.Contains("Headphone", StringComparison.OrdinalIgnoreCase) ||
                   category.Contains("Headset", StringComparison.OrdinalIgnoreCase)) ||
               name.Contains("Buds", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Earbuds", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("FreeBuds", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("TWS", StringComparison.OrdinalIgnoreCase) ||
               name.StartsWith("WF-", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("LinkBuds", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("Pixel Buds", StringComparison.OrdinalIgnoreCase);
    }

    private static BluetoothBatteryDevice CreateDevice(DeviceInformation device)
    {
        var battery = ReadBatteryProperty(device);
        return new BluetoothBatteryDevice(
            device.Id,
            device.Name.Trim(),
            battery.Percentage,
            ReadConnectionState(device),
            ReadString(device, AddressProperty),
            ReadGuid(device, AepContainerIdProperty))
        {
            BatterySource = battery.Source,
            Categories = ReadStrings(device, AepCategoryProperty),
            BluetoothClassMajor = ReadUInt16(device, BluetoothClassMajorProperty)
        };
    }

    private static BatteryReading ReadBatteryProperty(DeviceInformation device)
    {
        var standardBattery = ReadPercentage(device, BatteryLifeProperty);
        if (standardBattery.HasValue)
        {
            return new BatteryReading(standardBattery, BatteryReadingSource.WindowsDeviceProperty);
        }

        var hfpBattery = ReadPercentage(device, BluetoothBatteryProperty);
        return new BatteryReading(
            hfpBattery,
            hfpBattery.HasValue ? BatteryReadingSource.BluetoothHfp : BatteryReadingSource.None);
    }

    private static int? ReadPercentage(DeviceInformation device, string propertyName)
    {
        if (!device.Properties.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        try
        {
            var percentage = Convert.ToInt32(value);
            return percentage is >= 0 and <= 100 ? percentage : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool? ReadConnectionState(DeviceInformation device)
    {
        if (device.Properties.TryGetValue(ConnectedProperty, out var connected) && connected is bool value)
        {
            return value;
        }

        return null;
    }

    private static string? ReadString(DeviceInformation device, string propertyName)
    {
        return device.Properties.TryGetValue(propertyName, out var value)
            ? value?.ToString()
            : null;
    }

    private static Guid? ReadGuid(DeviceInformation device, string propertyName)
    {
        if (!device.Properties.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        return value is Guid guid && guid != Guid.Empty ? guid : null;
    }

    private static IReadOnlyList<string> ReadStrings(DeviceInformation device, string propertyName)
    {
        if (!device.Properties.TryGetValue(propertyName, out var value) || value is null)
        {
            return [];
        }

        return value switch
        {
            string single when !string.IsNullOrWhiteSpace(single) => [single],
            IEnumerable<string> many => many.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
            _ => []
        };
    }

    private static ushort? ReadUInt16(DeviceInformation device, string propertyName)
    {
        if (!device.Properties.TryGetValue(propertyName, out var value) || value is null)
        {
            return null;
        }

        try
        {
            return Convert.ToUInt16(value);
        }
        catch
        {
            return null;
        }
    }

    private static bool? ReadBoolean(DeviceInformation device, string propertyName)
    {
        return device.Properties.TryGetValue(propertyName, out var value) && value is bool result
            ? result
            : null;
    }

    private static async Task<GattBatteryReading?> TryReadGattBatteryAsync(string deviceId)
    {
        try
        {
            using var device = await BluetoothLEDevice.FromIdAsync(deviceId);
            if (device is null)
            {
                return null;
            }

            var servicesResult = await device.GetGattServicesForUuidAsync(
                GattServiceUuids.Battery,
                BluetoothCacheMode.Cached);

            if (servicesResult.Status != GattCommunicationStatus.Success)
            {
                return null;
            }

            var levels = new List<GattComponentReading>();
            foreach (var service in servicesResult.Services)
            {
                using (service)
                {
                    var characteristicsResult = await service.GetCharacteristicsForUuidAsync(
                        GattCharacteristicUuids.BatteryLevel,
                        BluetoothCacheMode.Cached);

                    if (characteristicsResult.Status != GattCommunicationStatus.Success)
                    {
                        continue;
                    }

                    foreach (var characteristic in characteristicsResult.Characteristics)
                    {
                        var readResult = await characteristic.ReadValueAsync(BluetoothCacheMode.Uncached);
                        if (readResult.Status != GattCommunicationStatus.Success || readResult.Value.Length == 0)
                        {
                            continue;
                        }

                        using var reader = DataReader.FromBuffer(readResult.Value);
                        var percentage = reader.ReadByte();
                        if (percentage <= 100)
                        {
                            levels.Add(new GattComponentReading(
                                percentage,
                                ClassifyGattComponent(characteristic.UserDescription)));
                        }
                    }
                }
            }

            if (levels.Count == 0)
            {
                return null;
            }

            var components = CreateGattComponents(levels);
            var aggregate = components is null
                ? levels.Min(level => level.Percentage)
                : new TwsBatteryReading(
                        components.LeftPercent,
                        components.RightPercent,
                        components.CasePercent)
                    .LowestPercent ?? levels.Min(level => level.Percentage);
            return new GattBatteryReading(aggregate, components);
        }
        catch
        {
            // Не все устройства разрешают стороннее чтение GATT; это нормальная ситуация.
        }

        return null;
    }

    private static BatteryComponentKind ClassifyGattComponent(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return BatteryComponentKind.Unknown;
        }

        if (description.Contains("left", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("лев", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryComponentKind.Left;
        }

        if (description.Contains("right", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("прав", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryComponentKind.Right;
        }

        if (description.Contains("case", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("box", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("кейс", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("cradle", StringComparison.OrdinalIgnoreCase))
        {
            return BatteryComponentKind.Case;
        }

        return BatteryComponentKind.Unknown;
    }

    private static BatteryComponents? CreateGattComponents(IReadOnlyList<GattComponentReading> readings)
    {
        var left = readings.FirstOrDefault(reading => reading.Kind == BatteryComponentKind.Left)?.Percentage;
        var right = readings.FirstOrDefault(reading => reading.Kind == BatteryComponentKind.Right)?.Percentage;
        var chargingCase = readings.FirstOrDefault(reading => reading.Kind == BatteryComponentKind.Case)?.Percentage;

        // Some BAS 1.1 devices expose three service instances without textual
        // descriptors. The spec orders no instances, so do not guess left/right.
        return left.HasValue || right.HasValue || chargingCase.HasValue
            ? new BatteryComponents(left, right, chargingCase)
            : null;
    }

    private sealed record PnpBatteryReading(
        string Name,
        int? BatteryPercent,
        Guid? ContainerId,
        bool? IsPresent);

    private sealed record BatteryReading(
        int? Percentage,
        BatteryReadingSource Source);

    private sealed record GattBatteryReading(
        int BatteryPercent,
        BatteryComponents? Components);

    private sealed record NothingDeviceReading(
        string Id,
        TwsBatteryReading? Reading);

    private sealed record GattComponentReading(
        int Percentage,
        BatteryComponentKind Kind);

    private enum BatteryComponentKind
    {
        Unknown,
        Left,
        Right,
        Case
    }

    public void Dispose()
    {
        _airPodsBatteryService.Dispose();
        _fastPairBatteryService.Dispose();
    }
}
