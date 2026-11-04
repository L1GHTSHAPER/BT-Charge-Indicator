# Current implementation audit

## Repository snapshot
- Application: C# WinForms, `net9.0-windows10.0.19041.0`.
- Nullable reference types and implicit usings are enabled.
- Automated tests: `Tests/BTChargeIndicator.ProtocolTests.csproj`, executable test harness in `Tests/Program.cs`.
- Global settings: `%LOCALAPPDATA%/BT Charge Indicator/settings.json`.
- Versioned device profiles: `%LOCALAPPDATA%/BT Charge Indicator/profiles.json`, with a previous-valid `.bak`.
- UI entry point: `UI/TrayApplicationContext.cs`; settings window: `UI/SettingsForm.cs`; profile editor: `UI/BatteryProfileEditorForm.cs`.

## Existing capabilities preserved
- Windows device properties and Bluetooth HFP/PnP.
- BLE GATT Battery Service and labeled multiple instances.
- Apple Continuity, Google Fast Pair, Nothing/CMF RFCOMM, and DualSense HID.
- Device connection watcher, duplicate endpoint merging, connection cards, native low-battery notifications, localization, update checks, and ten tray icon styles.

## Implemented battery behavior
- `TwsBatteryReading.LowestPercent` now selects the lowest known left/right earbud value and uses the case only when neither earbud value is known.
- Left/right/case values remain separate for tooltips and diagnostics.
- `BatteryAggregationEngine` provides pure per-device/global aggregation and ignores unknown or invalid values.
- `BatteryProfileEngine` resolves explicit assignments, Windows container IDs, model/category/protocol hints, then the built-in default.
- Custom aggregation rules are declarative and restricted to known components and built-in operations.
- `BatteryProfileStore` validates schema-versioned JSON, writes atomically, and recovers from the previous valid backup.
- The global tray icon supports minimum, maximum, and average aggregation across included devices.
- Selected devices can have separate tray icons and individual icon styles.
- Low-battery notifications use the profile-computed primary percentage.
- Device preferences are keyed by Windows container ID when available; older address-based entries are migrated.
- `BatteryDeviceIdentity` merges known component values across endpoints sharing a stable container identity, so the same physical device is not counted twice.

## UI
- The tray menu retains quick settings and provides access to the tabbed settings window.
- The settings window has Overview, Devices, Profiles, Tray & Global, Notifications, and Advanced tabs.
- The profile editor supports matching criteria, built-in/custom aggregation, preview, CRUD, import, and export.
- English and Russian resource catalogs are kept in sync.

## Remaining work
- Add a time-based stale-reading policy and configurable tooltip component/timestamp visibility.
- Add profile-specific tray/notification options and richer per-device labels.
- Complete keyboard, DPI, and high-contrast manual UI checks.
- Verify per-device icons and the Redmi Buds 6 Pro scenario on real Windows Bluetooth hardware.
