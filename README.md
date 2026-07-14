# Bluetooth Battery Indicator for Windows

[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows)](https://github.com/L1GHTSHAPER/BT-Charge-Indicator)
[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Release](https://img.shields.io/github/v/release/L1GHTSHAPER/BT-Charge-Indicator)](https://github.com/L1GHTSHAPER/BT-Charge-Indicator/releases/latest)
[![License](https://img.shields.io/github/license/L1GHTSHAPER/BT-Charge-Indicator)](LICENSE)

**BT Charge Indicator** is a lightweight Windows system tray app that displays the battery percentage of connected Bluetooth devices: wireless headphones, earbuds, headsets, game controllers, keyboards, mice, speakers, and other accessories.

It works natively on Windows 10 and Windows 11 and reads battery levels through Windows device properties, Bluetooth HFP/PnP data, and the standard Bluetooth Low Energy GATT Battery Service.

## Download

Download the latest ready-to-run Windows executable from [GitHub Releases](https://github.com/L1GHTSHAPER/BT-Charge-Indicator/releases/latest). The app is portable and does not require installation.

## Features

- battery percentage directly in the Windows notification area;
- all paired Bluetooth devices in one tray menu;
- support for Bluetooth Classic and Bluetooth Low Energy (BLE) devices;
- HFP/PnP battery reading for wireless headphones and earbuds;
- BLE GATT Battery Service fallback;
- automatic refresh every 30 seconds, 1 minute, or 5 minutes;
- low battery notifications;
- optional startup with Windows;
- quick access to Windows Bluetooth settings;
- native C# WinForms app with no web browser or background server;
- no telemetry, accounts, or network access.

## Supported devices

BT Charge Indicator can display battery information for devices that report their charge to Windows, including many:

- Bluetooth headphones, headsets, and TWS earbuds;
- Xbox and other Bluetooth game controllers;
- wireless mice and keyboards;
- portable speakers;
- BLE accessories that implement the standard Battery Service.

Battery reporting depends on the device firmware and Windows driver. Accessories that use a proprietary battery protocol may appear with `—` instead of a percentage.

## Usage

1. Download and run `BTChargeIndicator.exe`.
2. Find the colored percentage icon in the Windows system tray.
3. Left-click the icon for a quick battery summary.
4. Right-click it for the full device list and settings.
5. Double-click it to open Windows Bluetooth settings.

The tray icon shows the lowest known battery percentage among currently connected devices, helping you notice the accessory that needs charging first.

## Build from source

Requirements:

- Windows 10 or Windows 11;
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

Create a self-contained single-file build:

```powershell
.\build.ps1
```

The executable will be created at `dist\BTChargeIndicator.exe`.

Create a smaller framework-dependent build:

```powershell
.\build.ps1 -Portable
```

## How battery detection works

The app combines several Windows APIs because different Bluetooth accessories expose their charge differently:

1. `System.Devices.BatteryLife` for standard Windows device battery data;
2. the Windows HFP/PnP battery property used by many Bluetooth headsets and earbuds;
3. the Bluetooth LE GATT Battery Service for compatible BLE devices.

This approach supports more devices than relying on a single Bluetooth API.

## Русский

**BT Charge Indicator** — приложение для отображения заряда Bluetooth-устройств в системном трее Windows 10/11. Поддерживаются беспроводные наушники, TWS-гарнитуры, геймпады, мыши, клавиатуры, колонки и BLE-аксессуары.

Скачайте готовый файл на странице [последнего релиза](https://github.com/L1GHTSHAPER/BT-Charge-Indicator/releases/latest), запустите его и откройте меню правым щелчком по значку в трее. Установка не требуется.

## Privacy

BT Charge Indicator works locally. It does not collect analytics, send device information, or connect to external services.

## Contributing

Bug reports and pull requests are welcome. When reporting a device that does not show its battery, include the Windows version, device model, connection state, and whether Windows Settings displays a percentage.

## License

Distributed under the [MIT License](LICENSE).

---

Keywords: Windows Bluetooth battery monitor, Bluetooth battery level, Bluetooth headphones battery, earbuds battery percentage, BLE battery indicator, Windows 11 system tray battery app, Bluetooth device charge monitor, C# WinForms Bluetooth.
