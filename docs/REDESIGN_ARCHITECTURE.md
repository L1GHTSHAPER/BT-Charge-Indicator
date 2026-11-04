# Target architecture — WinUI redesign

## Goals

- Modern WinUI 3 desktop experience.
- Preserve the current Bluetooth protocol support and battery/profile behavior.
- Keep tray operation independent of whether the dashboard window is open.
- Support portable/unpackaged and packaged MSIX distribution, subject to verified Windows App SDK constraints.
- Keep Windows Widgets as an optional supported integration; do not use private Quick Settings APIs.

## Current state

The current application is one WinForms executable targeting `net10.0-windows10.0.19041.0`. `Program.Main` loads settings, applies localization, creates a named mutex, and starts `TrayApplicationContext`.

`TrayApplicationContext` currently combines several responsibilities:
- application lifetime and single-instance behavior;
- Bluetooth scan scheduling and refresh;
- device list and connection state;
- profile resolution and battery aggregation;
- notification orchestration;
- global and per-device NotifyIcon lifecycle;
- tray menus and settings-window activation.

The Bluetooth acquisition implementation is in `BluetoothBatteryService` and protocol-specific services. The pure battery aggregation and profile engines are already isolated enough to preserve and reuse.

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

The runtime publishes state changes to subscribers. UI surfaces consume snapshots and issue commands; they never start their own Bluetooth scan loops.

### WinUI 3 presentation

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

The Widgets provider consumes a minimal read-only state projection from the runtime. It must not duplicate Bluetooth scanning or hold Bluetooth device handles. Package identity and activation constraints must be validated before choosing the transport and project layout.

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
- `src/BTChargeIndicator.Runtime`
- `src/BTChargeIndicator.WinUI`
- `src/BTChargeIndicator.Tray` (only if a separate tray host/assembly is technically justified)
- `legacy/BTChargeIndicator.WinForms` (temporary during migration)
- `Tests/BTChargeIndicator.Tests`

Avoid creating assemblies only for naming purity. A project boundary is justified when it enforces a real dependency boundary or enables a deployment target.

## Key architectural decisions still open

1. Whether the tray host lives in the WinUI process or a small companion process. Decide after a prototype verifies tray lifetime when the window is closed and unpackaged deployment behavior.
2. Whether Windows App SDK runtime is framework-dependent or self-contained for each artifact. Decide after testing installer size, update behavior, and portable extraction.
3. Whether Widgets share the app process or use a provider/companion activation model. Decide from current Microsoft provider requirements.
4. Whether to keep WinForms as a buildable legacy host or extract its tray host temporarily. Do not delete it until parity is demonstrated.

## Non-goals

- No virtual battery driver.
- No third-party Quick Settings tile through private APIs, Explorer injection, or undocumented hooks.
- No duplicate Bluetooth scanning in the dashboard, tray, and widget.
- No arbitrary user scripts for battery rules.
- No telemetry.
