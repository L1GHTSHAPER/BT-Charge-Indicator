# Architecture notes

## Current architecture
The application is a single .NET 9 WinForms executable. Bluetooth acquisition lives primarily in `Services/BluetoothBatteryService.cs`, with protocol-specific services for AirPods, Google Fast Pair, Nothing/CMF, and DualSense. Raw device data is represented by `BluetoothBatteryDevice` and `BatteryComponents`. `TrayApplicationContext` owns scanning, global and per-device tray icons, notifications, and menu orchestration. `SettingsForm` and `BatteryProfileEditorForm` own the tabbed settings and profile editing experiences. `SettingsStore` serializes `AppSettings` to LocalAppData.

## Current battery flow
1. Windows and protocol-specific services discover/read a device.
2. Protocol adapters return a scalar percentage and, when available, left/right/case components.
3. TWS values are represented by `TwsBatteryReading`.
4. `BluetoothBatteryService.ApplyComponentReading` copies `TwsBatteryReading.LowestPercent` to `BluetoothBatteryDevice.BatteryPercent`.
5. `BatteryProfileEngine` resolves the active profile and computes each device's display percentage.
6. The global tray icon aggregates connected, included devices using the selected global minimum, maximum, or average mode; if none are connected, it falls back to known included devices.

The former TWS `LowestPercent` calculation included the charging case. The default now prefers the lowest known left/right earbud value and only falls back to the case when neither earbud value is known. Component values remain intact for tooltips and diagnostics.

## Target architecture
Keep acquisition separate from policy:
- **Providers:** Bluetooth/Windows/protocol readers; return raw observations only.
- **Domain model:** normalized device snapshots and typed battery components with source and observation time.
- **Aggregation engine:** pure, testable per-device and global aggregation.
- **Profile engine:** validates and resolves versioned JSON profiles and safe declarative rules.
- **Presentation:** tray icons, tooltip, notifications, and settings UI consume computed values only.
- **Persistence:** versioned settings/profile store with migration and atomic writes.

Do not move protocol decoding into UI code. Do not let a protocol adapter choose a user's preferred aggregation rule.

## Aggregation engine (initial implementation)
`Models/BatteryAggregationMode.cs` defines stable aggregation modes and
`Services/BatteryAggregationEngine.cs` contains pure per-device and global
aggregation helpers. Earbud-only modes use left/right values and only fall back
to the case when no earbud value is available. All-component modes intentionally
include the case. Invalid percentages and unknown values are ignored rather than
being treated as zero. The engine is not yet wired to the profile or tray UI;
that integration belongs to later phases.

## Profile persistence and rules
`Settings/BatteryProfile.cs` defines profile matching criteria; `Models/BatteryAggregationRule.cs` defines typed component selections and declarative aggregation operations. `Settings/BatteryProfileStore.cs` stores a schema-versioned `profiles.json` beside the existing settings file, writes atomically, keeps a previous valid `.bak`, and falls back to that backup if the current document cannot be read. The rule evaluator only accepts known component identifiers and a fixed operation enum; it never evaluates arbitrary user code.

Each device can store an explicit `ProfileId` in the existing settings document. Automatic matching uses Windows container GUIDs, categories, name fragments, and protocol source values. Bluetooth MAC addresses are not used as the only identity.

## Normalized observations
`BluetoothBatteryDevice` now records the observation timestamp. `BatterySnapshot.FromDevice` creates an immutable normalized view containing typed component readings, the scalar reading, source, connection state, and observation time. Invalid percentages are omitted rather than coerced to zero. The snapshot deliberately excludes Bluetooth addresses; profile matching and saved preferences use the Windows container ID where available.

## Settings UI and per-device tray icons
The main `NotifyIcon` remains the global aggregate. Optional per-device `NotifyIcon` instances are keyed by the stable device key, use their own icon style (or inherit the global style), and are disposed when disabled, removed, or when the application exits. Each per-device icon has a compact context menu for details, profile management, Bluetooth settings, and hiding that icon.

The tabbed settings form edits a cloned settings object and only applies it after Save. Cancel discards its pending changes. It exposes per-device inclusion/profile/icon controls and global aggregation/style, notifications, language, refresh, update, and startup settings.
