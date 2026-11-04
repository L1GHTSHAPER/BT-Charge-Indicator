# Current implementation audit

## Repository snapshot
- Application: C# WinForms, `net9.0-windows10.0.19041.0`.
- Nullable reference types and implicit usings are enabled.
- Existing protocol test project: `Tests/BTChargeIndicator.ProtocolTests.csproj`, executable test harness in `Tests/Program.cs`.
- Existing settings are JSON at `%LOCALAPPDATA%/BT Charge Indicator/settings.json`.
- Existing UI is primarily tray menus and dialogs owned by `UI/TrayApplicationContext.cs`.

## Existing capabilities to preserve
- Windows device properties and Bluetooth HFP/PnP.
- BLE GATT Battery Service and labeled multiple instances.
- Apple Continuity, Google Fast Pair, Nothing/CMF RFCOMM, and DualSense HID.
- Device connection watcher, duplicate endpoint merging, connection cards, native low-battery notifications, localization, update checks, and ten tray icon styles.

## Findings
- `TwsBatteryReading.LowestPercent` originally selected the minimum of left, right, and case.
- `BluetoothBatteryService.ApplyComponentReading` uses that value for the device's primary `BatteryPercent`.
- Labeled GATT components use the same calculation.
- `TrayApplicationContext.UpdateTrayIcon` takes the lowest scalar percentage among included connected devices.
- Tooltips and diagnostics already preserve and display left/right/case separately.
- Existing per-device preferences include custom name, hidden state, global tray inclusion, and low-battery notification toggle.
- The current settings model has no profile schema, profile editor, per-device tray icon, or configurable global aggregation.

## Known verification limitation
The repository connector can inspect and update GitHub files but cannot execute the Windows WinForms build or Bluetooth APIs. The new Windows CI workflow is intended to provide build/test evidence. Live Redmi Buds 6 Pro verification still requires a Windows machine with the earbuds connected.
