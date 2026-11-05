# Phase 1 extraction map

This document records the concrete seams found in the current implementation. It is intended to guide incremental refactoring, not to prescribe a blind file move.

## Current composition root

`Program.Main` currently:
- loads settings and applies language before creating the application context;
- enforces a named single-instance mutex;
- initializes WinForms and starts `TrayApplicationContext`.

`UI/TrayApplicationContext.cs` is approximately 1,433 lines and is the main migration risk. It currently owns:
- `BluetoothBatteryService`, `BluetoothDeviceWatcher`, `DeviceConnectionTracker`;
- scan scheduling, device-change coalescing, refresh locking, and retry timing;
- current devices, Bluetooth availability, last error/update time;
- profile resolution and effective battery calculations;
- connection and low-battery notification decisions;
- global and per-device `NotifyIcon` lifecycle;
- menu construction, device actions, settings/profile dialogs;
- update checks and process exit/disposal.

Do not move this class wholesale into a new project. Extract behavior in small steps and leave the WinForms context as an adapter until the new runtime is tested.

## Existing reusable components

### Candidate Core code

These are presentation-independent or close to it and should be moved only after dependency checks:
- `Models/BatteryAggregationMode.cs`
- `Models/BatteryAggregationRule.cs`
- `Models/BatterySnapshot.cs`
- `Models/BluetoothBatteryDevice.cs` (review Windows-specific metadata and identity fields)
- `Models/TwsBatteryReading.cs`
- `Services/BatteryAggregationEngine.cs`
- `Services/BatteryProfileEngine.cs`
- `Services/BatteryDeviceIdentity.cs`
- `Settings/BatteryProfile.cs`
- `Settings/AppSettings.cs` (split platform enums/preferences if needed)
- profile validation/matching contracts

Keep these free of `System.Windows.Forms`, `System.Drawing`, WinUI/XAML, and Windows UI types.

### Candidate Windows platform services

These rely on Windows APIs and should remain platform-specific:
- `Services/BluetoothBatteryService.cs`
- protocol services: AirPods, Fast Pair, Nothing/CMF, DualSense;
- `Services/BluetoothDeviceWatcher.cs`;
- Windows notifications and Bluetooth settings launch;
- `Services/AutoStartManager.cs`.

The Bluetooth reader should return observations; it must not own a UI timer, tray object, dashboard, or view model.

### Candidate application/runtime services

Extract behind interfaces and test with fakes:
- scan scheduling and refresh orchestration;
- current device state and immutable state publication;
- profile resolution and effective battery computation;
- connection transition tracking;
- low-battery notification policy;
- settings/profile persistence orchestration;
- update-check policy.

Potential interfaces (names are provisional):
- `IBluetoothBatteryReader.ScanAsync(bool refreshGattBattery, CancellationToken)`
- `IDeviceChangeSource`
- `ISettingsRepository` and `IProfileRepository`
- `INotificationSink`
- `IUpdateChecker`
- `IApplicationRuntime` with `StartAsync`, `RefreshAsync`, `StateChanged`, and `StopAsync`.

Avoid exposing mutable WinForms or WinUI types through these interfaces. Publish immutable snapshots and status.

## Presentation adapters to retain temporarily

- `UI/TrayApplicationContext.cs`: keep as the legacy WinForms host; progressively delegate scanning/state/policy to runtime.
- `UI/SettingsForm.cs` and `UI/BatteryProfileEditorForm.cs`: legacy-only presentation; use as behavioral references.
- `UI/TrayIconRenderer.cs`: currently depends on System.Drawing and settings enum. Initially keep in a Windows tray/presentation adapter; later decide whether to extract a rendering abstraction or retain it in the tray host.
- `UI/ConnectionNotificationPresenter.cs` and toast forms: presentation-specific.
- `Localization/AppText.cs`: separate resource lookup from UI-specific formatting if shared by WinUI.

## High-risk seams to verify before moving code

1. `BluetoothBatteryDevice` and source/availability enums may be used by WinForms and protocol services. Move shared types first or use a temporary compatibility project reference; do not duplicate models.
2. `UpdateService` uses `Application.ProductVersion`, which ties it to WinForms. Replace that dependency with injected product/version metadata before moving it.
3. `DesktopNotificationService` may depend on WinForms/application identity and COM/WinRT activation. Keep it in the Windows platform adapter until its requirements are isolated.
4. `TrayIconRenderer` uses GDI+/System.Drawing and returns native `Icon`; do not put it in Core.
5. `BluetoothDeviceWatcher` callbacks arrive from worker threads. Runtime must marshal state notifications safely and coalesce changes without depending on a WinForms timer.
6. `SettingsStore.Save` currently swallows errors. The new runtime should expose persistence failures to UI while preserving the existing resilience policy and backup/recovery behavior.
7. The existing settings and profile paths are under LocalAppData. Verify MSIX and unpackaged processes resolve to the same intended user data path before shipping.
8. A dashboard window closing must not stop the tray/runtime. Application shutdown must be an explicit command, not an implicit window-close side effect.

## Phase 1 implementation order

1. Add a solution file and establish project references while leaving the current WinForms project unchanged.
2. Create a shared contracts/domain project and move only proven UI-free models and pure engines. Keep namespaces stable initially where possible.
3. Add tests that reference the shared project directly; preserve all current regression tests.
4. Introduce reader, repositories, notification, and update interfaces with adapters around existing implementations.
5. Extract a runtime service that owns scan cadence, current device snapshots, profile computation, and state events. Start with the same refresh interval and event coalescing behavior.
6. Change the legacy WinForms context to consume the runtime. Verify it still behaves as before.
7. Add a minimal WinUI 3 host that consumes the same runtime state. Do not build the full dashboard until both hosts can run against the same runtime.
8. Prototype MSIX and unpackaged startup, tray lifetime, and data-path behavior before finalizing the host split.

## Definition of done for Phase 1

- Shared domain/aggregation/profile code has no UI-framework references.
- Exactly one runtime owns Bluetooth scans and device state.
- Both the legacy host and WinUI host consume the same runtime contracts.
- Closing/hiding the WinUI window does not stop tray operation.
- Existing protocol/regression tests pass, and the legacy WinForms project still builds.
- Settings/profile compatibility is verified by round-trip tests.
- No protocol reader has been rewritten solely to satisfy the UI redesign.
