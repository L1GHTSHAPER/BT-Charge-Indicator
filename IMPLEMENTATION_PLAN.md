# Implementation plan

## Goal
Evolve BT Charge Indicator into a configurable Bluetooth battery dashboard while preserving the lightweight native Windows tray app and its existing device/protocol coverage.

The user-selected direction is:
- Versioned JSON device profiles with a GUI profile editor.
- Built-in aggregation choices plus safe declarative custom rules.
- Per-device tray icons and a separate global aggregate.
- A redesigned settings experience.
- The default TWS tray percentage uses left/right earbud charge and does not let a low charging-case value hide healthy earbuds.

## Working rules
- Execute phases in order; do not begin a broad rewrite before the preceding phase is verified.
- Preserve protocol parsers and current user settings.
- Keep all work on `feature/configurable-battery-profiles`.
- Do not force-push or rewrite history. Do not push to the upstream repository.
- Update this plan with actual implementation and test status.
- The remote coding environment cannot run the Windows WinForms build or Bluetooth hardware tests. Use the added Windows CI workflow and clearly report any remaining local-only checks.

## Phase 0 — Audit and baseline
- [x] Inspect repository structure, project settings, models, protocol adapters, tray aggregation, settings persistence, and tests.
- [x] Identify the TWS aggregate path: protocol reading -> `TwsBatteryReading.LowestPercent` -> `BluetoothBatteryDevice.BatteryPercent` -> tray aggregation.
- [x] Confirm the current `LowestPercent` includes the case and the tray icon uses `BatteryPercent`.
- [x] Establish a Windows .NET 9 baseline build and run the existing protocol tests in CI (the initial implementation branch passed; later profile changes are being validated separately).
- [x] Add repository instructions and this implementation plan.
- [x] Add architecture/current implementation/testing notes.
- [x] Add Windows CI for Release build and protocol tests.
- [x] Add regression tests and fix the default TWS primary percentage so the case is excluded whenever an earbud value exists.

Acceptance: the test suite proves L=100/R=100/case=55 produces a primary percentage of 100 while preserving case=55 as a separate component. Build and tests must pass in Windows CI before continuing.

## Phase 1 — Battery domain and aggregation design
- [x] Define a normalized battery snapshot with typed components, percentage, source, connection state, and observation time.
- [x] Keep protocol readings raw and calculate per-device/global display values in separate aggregation/profile engines.
- [x] Add a pure aggregation engine with left, right, case, all-component min/max/average, and earbud-only min/average modes.
- [x] Add safe custom declarative rules and strict selected-component behavior.
- [x] Never coerce unknown values to zero; missing selected components remain unknown.
- [ ] Define a time-based stale-reading policy.
- [x] Add regression tests for known/missing/invalid component values and global aggregation.
- [x] Run the Windows Release build and regression tests in CI.

Acceptance: aggregation logic is deterministic, isolated from WinForms, and fully tested.

## Phase 2 — Versioned JSON configuration and profiles
- [x] Add schema-versioned global settings and per-device profile models.
- [x] Add profile load/save, validation, atomic writes, backup recovery, and additive settings defaults.
- [ ] Add explicit migration handling for future schema changes.
- [x] Implement profile matching priority: explicit device assignment, stable Windows container ID, model/category/protocol hints, then default.
- [x] Avoid depending only on Bluetooth MAC addresses.
- [x] Add safe declarative custom rules; reject unsupported operations and malformed input.
- [x] Add profile import/export with validation and clear errors.
- [x] Keep existing settings and device preferences compatible through additive JSON properties and defaults.

Acceptance: old settings load without loss; invalid profiles cannot crash the app or execute code; matching is predictable and tested.

## Phase 3 — Device profile editor
- [x] Add a native WinForms profile editor consistent with the existing app.
- [x] Edit profile name, matching criteria, and built-in/custom aggregation settings.
- [ ] Add per-profile tooltip fields, tray presentation, and notification thresholds.
- [x] Preview the computed battery value using sample readings.
- [x] Support create, duplicate, rename, delete, import, and export.
- [ ] Add reset-to-default.
- [x] Explain validation failures in plain language.

Acceptance: profiles can be managed without manually editing JSON; preview matches the tested aggregation engine.

## Phase 4 — Multi-device and global aggregation
- [x] Give each device an independent computed battery value and presentation configuration.
- [x] Add per-device tray icon support with lifecycle-safe icon management.
- [x] Add a global icon with configurable aggregation and existing per-device inclusion controls.
- [ ] Define behavior for disconnected, stale, unavailable, hidden, and excluded devices.
- [ ] Ensure one physical device exposed through multiple Windows endpoints is not double-counted.

Acceptance: per-device icons do not affect global aggregation, and global rules are deterministic.

## Phase 5 — Tray, tooltip, and notifications
- [x] Make tray percentage use the profile's computed value.
- [x] Keep tray and per-device tooltips within Windows limits and include component readings.
- [ ] Add configurable tooltip component/timestamp visibility.
- [x] Support independent per-device icon styles with global style inheritance;
  - [ ] Add independent per-device/global labels and warning thresholds.
- [x] Ensure low-battery notification triggers use the profile-computed device percentage.
- [ ] Keep existing icon styles and notification behavior compatible by default.

Acceptance: the tray icon, tooltip, and notifications agree with the active profile.

## Phase 6 — Settings UI redesign
- [x] Audit current menu/dialog interactions and reuse working native components where sensible.
- [x] Build a tabbed settings window with Overview, Devices, Profiles, Tray & Global, Notifications, and Advanced sections.
- [ ] Add search/filter for devices and profiles.
- [ ] Support keyboard navigation, DPI scaling, and high-contrast/system theme behavior.
- [x] Keep localization complete in English and Russian.

Acceptance: all new features are reachable through the UI and remain usable at common Windows scaling levels.

## Phase 7 — Reliability and regression
- [ ] Test connect/disconnect/reconnect, delayed advertisements, partial component data, stale values, and Bluetooth radio changes.
- [ ] Test multiple devices and duplicate Classic/BLE endpoints.
- [ ] Test settings migration, malformed files, import/export, and profile matching.
- [ ] Add a hardware verification matrix for Fast Pair, Nothing/CMF, AirPods, GATT, and DualSense.
- [ ] Review resource disposal and background task cancellation.

Acceptance: Windows CI is green and the hardware matrix records actual results rather than assumptions.

## Phase 8 — Runtime and dependency review
- [ ] Review .NET support lifecycle and Windows API compatibility.
- [ ] Consider a move to a supported LTS runtime only after the application and test environment are verified.
- [ ] Ask before any breaking framework or deployment model change.

Acceptance: runtime decisions are documented and tested on Windows 10/11.

## Phase 9 — Documentation and release
- [x] Update README, configuration/profile examples, and testing/troubleshooting documentation.
- [ ] Update screenshots.
- [ ] Update changelog and version metadata.
- [ ] Build and verify self-contained x64 release output.
- [ ] Review license and upstream attribution.
- [ ] Commit and push coherent changes to the feature branch.
- [ ] Summarize completed work, test evidence, known limits, and local steps.

Acceptance: reproducible build, clear docs, clean diff, and release-ready branch.

## Current status
Phase 0 audit and the TWS regression fix passed Windows CI. Phase 1 includes normalized snapshots, the pure aggregation engine, safe custom rules, and strict missing-component handling. Phase 2 includes versioned profile JSON, validation, backup recovery, automatic matching, per-device assignment, and import/export. The profile editor and tabbed settings window are implemented. Per-device tray icons/styles and configurable global aggregation are implemented; the tray and low-battery notifications use profile-computed values. Windows CI build and regression tests passed on commit 36a582c4; the latest documentation-only changes have a new workflow queued. Stale-reading policy and live Bluetooth hardware verification remain outstanding.
