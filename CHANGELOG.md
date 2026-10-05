# Changelog

All notable changes to BT Charge Indicator are documented here.

## 1.5.0 - 2026-10-05

- Added eight tray icon styles: ring, segmented ring, bars, vertical battery, minimal numbers, capsule, gradient, and gauge, bringing the total to ten.
- Added icon previews alongside style names in the tray menu.
- All styles follow the configured low-battery warning threshold and distinguish unavailable readings from an empty battery.
- Preserved saved style selections, including the original percentage and battery styles.

## 1.4.1 - 2026-10-05

- Fixed low-battery notification banners staying on screen until manually dismissed; they now use the standard Windows timeout while retaining snooze and action buttons.

## 1.4.0 - 2026-08-09

- Added separate left, right, and charging case levels for Nothing and CMF earbuds over their RFCOMM companion protocol.
- Added generic Google Fast Pair battery advertisement support for compatible TWS earbuds.
- Added support for labeled multiple-instance BLE GATT Battery Services.
- Component levels now appear in quick summaries, diagnostics, tooltips, and low-battery notifications.
- Added protocol parser tests for Fast Pair and current/legacy Nothing battery frames.

## 1.3.0 - 2026-08-09

- Fixed missing check marks for low-battery notifications and Windows startup settings.
- Added a configurable low-battery threshold that also controls the tray icon warning color.
- Added per-device renaming, visibility, tray icon, and notification settings.
- Added separate left, right, and charging case levels for supported AirPods readings.
- Added device diagnostics with connection, battery source, address, and container details.
- Added native Windows notifications with snooze and action buttons, with a tray balloon fallback.
- Added optional automatic and manual GitHub release checks.

## 1.2.0 - 2026-07-19

- Added AirPods battery detection from Apple Continuity Bluetooth LE advertisements.
- Enlarged percentage digits for better tray readability.
- Added a persistent tray icon style option: percentage or smartphone-style battery.

## 1.1.0 - 2026-07-19

- Added battery detection for Sony PlayStation DualSense and DualSense Edge controllers over Bluetooth HID.

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
