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
    private const string DeviceContainerIdProperty = "System.Devices.ContainerId";
    private const string PresentProperty = "System.Devices.Present";

    private static readonly string[] RequestedProperties =
    [
        BatteryLifeProperty,
        BluetoothBatteryProperty,
        ConnectedProperty,
        AddressProperty,
        AepContainerIdProperty
    ];

    private static readonly string[] PnpBatteryProperties =
    [
        BluetoothBatteryProperty,
        DeviceContainerIdProperty,
        PresentProperty
    ];

    private readonly AirPodsBatteryService _airPodsBatteryService = new();

    public async Task<BluetoothScanResult> ScanAsync()
    {
        var radioState = await GetBluetoothAvailabilityAsync();
        if (radioState is BluetoothAvailability.TurnedOff or BluetoothAvailability.NotFound)
        {
            _airPodsBatteryService.SetEnabled(false);
            return new BluetoothScanResult([], radioState);
        }

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
                !device.BatteryPercent.HasValue);
            _airPodsBatteryService.SetEnabled(needsAirPodsFallback);

            if (needsAirPodsFallback)
            {
                var airPodsReading = await _airPodsBatteryService.GetReadingAsync(TimeSpan.FromSeconds(2));
                if (airPodsReading is not null)
                {
                    devices = ApplyAirPodsBatteryReading(devices, airPodsReading);
                }
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
            .Select(device => new BluetoothBatteryDevice(
                device.Id,
                device.Name.Trim(),
                ReadBatteryProperty(device),
                ReadConnectionState(device),
                ReadString(device, AddressProperty),
                ReadGuid(device, AepContainerIdProperty)))
            .ToArray();

        if (!useGattFallback)
        {
            return devices;
        }

        var reads = devices.Select(async device =>
        {
            // Не инициируем GATT-подключение к выключенным устройствам. Это может задержать
            // всё обновление на десятки секунд, если Windows не знает состояние соединения.
            if (device.BatteryPercent is not null || device.IsConnected != true)
            {
                return device;
            }

            int? battery;
            try
            {
                battery = await TryReadGattBatteryAsync(device.Id)
                    .WaitAsync(TimeSpan.FromSeconds(4));
            }
            catch (TimeoutException)
            {
                battery = null;
            }

            return device with { BatteryPercent = battery };
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
                .OrderByDescending(device => device.BatteryPercent.HasValue)
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
                    IsConnected = device.IsConnected ?? true
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
                    IsConnected = true
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
                !device.BatteryPercent.HasValue)
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
                IsConnected = true
            }
            : device).ToArray();
    }

    private static bool IsLikelyAirPods(BluetoothBatteryDevice device)
    {
        return device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
               device.Name.Contains("Air Pods", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ReadBatteryProperty(DeviceInformation device)
    {
        return ReadPercentage(device, BatteryLifeProperty)
            ?? ReadPercentage(device, BluetoothBatteryProperty);
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

    private static bool? ReadBoolean(DeviceInformation device, string propertyName)
    {
        return device.Properties.TryGetValue(propertyName, out var value) && value is bool result
            ? result
            : null;
    }

    private static async Task<int?> TryReadGattBatteryAsync(string deviceId)
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
                        return percentage <= 100 ? percentage : null;
                    }
                }
            }
        }
        catch
        {
            // Не все устройства разрешают стороннее чтение GATT; это нормальная ситуация.
        }

        return null;
    }

    private sealed record PnpBatteryReading(
        string Name,
        int? BatteryPercent,
        Guid? ContainerId,
        bool? IsPresent);

    public void Dispose()
    {
        _airPodsBatteryService.Dispose();
    }
}
