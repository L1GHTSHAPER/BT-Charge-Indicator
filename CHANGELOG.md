# Changelog

All notable changes to BT Charge Indicator are documented here.

## 1.0.2 - 2026-07-14

- Added HFP/PnP battery detection for Bluetooth headphones and earbuds.
- Added battery readings for devices such as CMF Buds Pro 2.
- Combined Windows device properties, PnP data, and BLE GATT Battery Service.
- Fixed paired Bluetooth devices being incorrectly filtered out on Win32.
- Fixed invalid property queries that could prevent device enumeration.
- Added timeouts for unresponsive BLE GATT devices.

## 1.0.0 - 2026-07-14

- Initial Windows system tray application.
- Bluetooth Classic and BLE device discovery.
- Dynamic battery percentage tray icon.
- Configurable refresh interval, low battery notifications, and Windows startup.
