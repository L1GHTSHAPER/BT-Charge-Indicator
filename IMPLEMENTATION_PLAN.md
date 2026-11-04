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
- [ ] Establish a Windows .NET 9 baseline build and run the existing protocol tests in CI.
- [x] Add repository instructions and this implementation plan.
- [x] Add architecture/current implementation/testing notes.
- [x] Add Windows CI for Release build and protocol tests.
- [x] Add regression tests and fix the default TWS primary percentage so the case is excluded whenever an earbud value exists.

Acceptance: the test suite proves L=100/R=100/case=55 produces a primary percentage of 100 while preserving case=55 as a separate component. Build and tests must pass in Windows CI before continuing.

## Phase 1 — Battery domain and aggregation design
- [ ] Define a normalized battery snapshot with component identity, percentage, source, connection state, and observation time.
- [ ] Separate raw protocol readings from derived per-device and global display values.
- [x] Add a pure aggregation engine with left, right, case, all-component min/max/average, and earbud-only min/average modes.\n- [ ] Add safe custom declarative rules and selected-component fallback policy.
- [ ] Define missing/stale/disconnected behavior; never coerce unknown values to zero.
- [x] Add regression tests for known/missing/invalid component values and global aggregation.\n- [ ] Run the Windows CI build/test and extend tests as the profile engine lands.

Acceptance: aggregation logic is deterministic, isolated from WinForms, and fully tested.

## Phase 2 — Versioned JSON configuration and profiles
- [ ] Add schema-versioned global settings and per-device profile models.
- [ ] Add robust load/save, validation, atomic writes, backup/recovery, and migrations from the current settings format.
- [ ] Implement profile matching priority: explicit device assignment, stable Windows identity/container, vendor/model/protocol hints, then default.
- [ ] Avoid depending only on Bluetooth MAC addresses.
- [ ] Add safe declarative custom rules; reject unsupported operations and malformed input.
- [ ] Add import/export with validation and clear errors.
- [ ] Keep existing settings and device preferences compatible.

Acceptance: old settings load without loss; invalid profiles cannot crash the app or execute code; matching is predictable and tested.

## Phase 3 — Device profile editor
- [ ] Design a native WinForms editor consistent with the redesigned app.
- [ ] Edit profile name, matching criteria, included components, aggregation, fallback, tooltip fields, tray presentation, and notification thresholds.
- [ ] Preview the computed battery value using sample readings.
- [ ] Support create, duplicate, rename, delete, import, export, and reset-to-default.
- [ ] Explain validation failures in plain language.

Acceptance: profiles can be managed without manually editing JSON; preview matches the tested aggregation engine.

## Phase 4 — Multi-device and global aggregation
- [ ] Give each device an independent computed battery value and presentation configuration.
- [ ] Add per-device tray icon support with lifecycle-safe icon management.
- [ ] Add a global icon with configurable included devices and aggregation.
- [ ] Define behavior for disconnected, stale, unavailable, hidden, and excluded devices.
- [ ] Ensure one physical device exposed through multiple Windows endpoints is not double-counted.

Acceptance: per-device icons do not affect global aggregation, and global rules are deterministic.

## Phase 5 — Tray, tooltip, and notifications
- [ ] Make tray percentage use the profile's computed value.
- [ ] Make tooltips show configured components and timestamps without exceeding Windows limits.
- [ ] Support independent per-device/global styles, labels, and warning thresholds.
- [ ] Ensure notification triggers use the configured relevant components, not a stale/raw aggregate.
- [ ] Keep existing icon styles and notification behavior compatible by default.

Acceptance: the tray icon, tooltip, and notifications agree with the active profile.

## Phase 6 — Settings UI redesign
- [ ] Audit current menu/dialog interactions and reuse working native components where sensible.
- [ ] Build a clear settings window with Overview, Devices, Profiles, Tray & Global, Notifications, and Advanced sections.
- [ ] Add search/filter for devices and profiles.
- [ ] Support keyboard navigation, DPI scaling, and high-contrast/system theme behavior.
- [ ] Keep localization complete in English and Russian.

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
- [ ] Update README, configuration/profile examples, screenshots, and troubleshooting.
- [ ] Update changelog and version metadata.
- [ ] Build and verify self-contained x64 release output.
- [ ] Review license and upstream attribution.
- [ ] Commit and push coherent changes to the feature branch.
- [ ] Summarize completed work, test evidence, known limits, and local steps.

Acceptance: reproducible build, clear docs, clean diff, and release-ready branch.

## Current status
Phase 0 audit and initial regression fix are committed on the feature branch. Phase 1 has started with a pure aggregation engine and focused tests. Windows CI runs have been queued by GitHub for the latest commits; no completed Windows build/test result or Bluetooth hardware verification is available yet.
