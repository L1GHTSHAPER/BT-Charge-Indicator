# Target architecture — WinUI redesign

## Goals

- Modern WinUI 3 desktop experience.
- Preserve the current Bluetooth protocol support and battery/profile behavior.
- Keep tray operation independent of whether the dashboard window is open.
- Support portable/unpackaged and packaged MSIX distribution with clearly documented feature differences.
- MSIX is required for the Windows desktop Widgets provider; portable mode must not claim Widgets support.
- Keep Windows Widgets as an optional supported integration; do not use private Quick Settings APIs.

## Current state

The current application is one WinForms executable targeting `net10.0-windows10.0.19041.0`. `Program.Main` loads settings, applies localization, creates a named mutex, and starts `TrayApplicationContext`.

`TrayApplicationContext` still hosts several legacy responsibilities:
- application lifetime and single-instance behavior;
- device watcher lifecycle and change-event coalescing;
- notification orchestration;
- profile resolution and battery aggregation;
- notification orchestration;
- global and per-device NotifyIcon lifecycle;
- tray menus and settings-window activation.

The Bluetooth acquisition implementation remains in `BluetoothBatteryService` and the existing protocol-specific services. The pure battery aggregation and profile engines are reused through `BTChargeIndicator.Core`. Core also contains the presentation-independent connection tracker and `ApplicationRuntime`. `BluetoothBatteryScannerAdapter` adapts the existing acquisition service to the runtime scanner contract without changing protocol readers. The legacy WinForms tray host is now wired to this runtime: it delegates initial, periodic, manual, and watcher-triggered scans to the runtime and consumes published snapshots on the UI thread. Its old scan timer and scan lock have been removed. The device watcher and 500 ms UI timer remain only to coalesce device-change signals. This integration passed Windows CI and was manually tested from the generated artifact.

## Target boundaries

### Core domain and application behavior

Owns presentation-independent types and policies:
- normalized device snapshots and typed battery components;
- device identity and endpoint merge policy;
- battery aggregation and profile resolution;
- settings/profile contracts and validation;
- global/per-device inclusion policy;
- application state and commands.

Core must not reference WinForms or WinUI types.

### Windows platform services

Owns Windows-specific acquisition and integration:
- Bluetooth WinRT APIs and protocol adapters;
- device watchers and connection tracking;
- Windows notifications and Bluetooth settings launch;
- OS/version capability checks;
- platform-specific tray integration if it cannot be shared cleanly.

Keep WinRT operations asynchronous, cancellable, and bounded. Protocol readers return observations; they do not decide display policy.

### Application runtime

A single runtime instance owns:
- Bluetooth scanning and refresh schedule;
- current immutable device snapshots;
- profile and aggregation calculations;
- connection/low-battery notification decisions;
- settings and profile persistence;
- lifecycle, cancellation, and shutdown.

The runtime publishes immutable state snapshots to subscribers. The current Core runtime provides startup, periodic refresh, explicit refresh, interval updates, state publication, and shutdown. It is now the scan/state owner for the legacy WinForms host. The next host will be WinUI 3; it must consume the same runtime rather than creating another scan loop. Additional stress testing of concurrent refresh, cancellation, and shutdown during real Bluetooth activity remains part of Phase 1 hardening.

### WinUI 3 presentation

The new WinUI 3 application now exists at `src/BTChargeIndicator.WinUI` as a separate unpackaged host referencing Core. Its initial NavigationView shell and Dashboard/Settings placeholders are buildable, but it is not yet connected to Bluetooth acquisition. The Windows scanner adapter is still compiled into the legacy WinForms host; the next extraction will move the acquisition implementation behind a reusable Windows platform project. During the transition, the existing WinForms executable remains buildable and serves as the known-good fallback. Do not move or duplicate protocol readers into the WinUI project.

Owns:
- navigation and window lifecycle;
- dashboard/device details;
- settings and profile editor;
- appearance, localization presentation, and accessibility;
- view models and UI-specific state.

No Bluetooth protocol code or persistence policy should live in XAML code-behind.

### Tray surface

The tray surface is a separate presentation client of the runtime. It must keep working when the main window is hidden or closed. It can share the runtime and domain models, but should not depend on dashboard view models or page lifetime.

### Widget provider

The Widgets provider consumes a minimal read-only state projection from the runtime. It must not duplicate Bluetooth scanning or hold Bluetooth device handles.

Microsoft's current Win32 widget provider model requires a packaged desktop app registered through the package manifest. The portable/unpackaged distribution therefore cannot register the widget provider. The MSIX variant will include the provider; the portable variant will omit it and show a clear capability explanation in settings. Use the documented C# provider path unless a technical limitation requires a separate native host.

## State flow

`Windows Bluetooth APIs / protocol adapters -> normalized observations -> application runtime -> profile + aggregation -> immutable device state -> WinUI dashboard / tray / notifications / Widgets`

The runtime is the only owner of device discovery. All presentation surfaces must agree on the same computed battery value.

## Persistence and compatibility

- Keep existing `%LOCALAPPDATA%/BT Charge Indicator/settings.json` and `profiles.json` paths unless a tested migration plan requires otherwise.
- Preserve persisted enum numeric values and existing device preference keys.
- Read old settings before writing any new schema.
- Use explicit schema migrations, atomic writes, and backup recovery.
- Do not store Bluetooth addresses in new diagnostic logs or widget payloads unless essential.
- Keep portable and MSIX modes on the same per-user data location unless package virtualization proves a documented exception; verify this in tests.

## Proposed project layout

The final project split is subject to a small Windows App SDK deployment prototype. Candidate layout:

- `src/BTChargeIndicator.Core`
- `src/BTChargeIndicator.Platform.Windows`
- `src/BTChargeIndicator.Runtime` (only if a real dependency boundary emerges)
- `src/BTChargeIndicator.WinUI` (created; initial shell only)
- `src/BTChargeIndicator.Tray` (only if a separate tray host/assembly is technically justified)
- `legacy/BTChargeIndicator.WinForms` (temporary during migration)
- `Tests/BTChargeIndicator.Tests`

Avoid creating assemblies only for naming purity. A project boundary is justified when it enforces a real dependency boundary or enables a deployment target.

## Deployment model

### MSIX distribution

- Full-featured WinUI 3 app with package identity.
- Includes the registered Windows Widgets provider.
- Choose framework-dependent or self-contained Windows App SDK deployment after measuring package size, installation behavior, and servicing.
- Package identity-dependent integrations must be declared and tested in the manifest.

### Portable/unpackaged distribution

- Same WinUI dashboard and shared runtime, delivered as a folder/ZIP.
- Initial candidate: Windows App SDK self-contained deployment so users do not need a separate runtime installation.
- Output will be larger; publisher must include runtime servicing in application updates.
- No package identity, so no Win32 Windows Widgets provider. Do not attempt registry/COM hacks to bypass this restriction.
- Do not advertise a zero-extraction single EXE. Microsoft's supported single-file mode extracts dependencies at first launch and is limited to unpackaged self-contained deployment.

The two distributions must use the same application data location and migration logic where possible. Verify settings/profile persistence across switching between portable and MSIX editions.

## Key architectural decisions still open

1. Whether the tray host lives in the WinUI process or a small companion process. Decide after a prototype verifies tray lifetime when the window is closed in both package modes.
2. Whether the MSIX uses framework-dependent or self-contained Windows App SDK deployment. Decide after testing package size, installation, and servicing.
3. Whether the widget provider can run in-process with the shared runtime or needs a small out-of-process provider host. Decide from the documented C# provider activation model and a prototype.
4. Whether to keep WinForms as a buildable legacy host or extract its tray host temporarily. Do not delete it until parity is demonstrated.

## Non-goals

- No virtual battery driver.
- No third-party Quick Settings tile through private APIs, Explorer injection, or undocumented hooks.
- No duplicate Bluetooth scanning in the dashboard, tray, and widget.
- No arbitrary user scripts for battery rules.
- No telemetry.
