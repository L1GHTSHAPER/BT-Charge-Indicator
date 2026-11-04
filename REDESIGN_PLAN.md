# WinUI redesign implementation plan

## Vision

Evolve BT Charge Indicator into a modern Windows 11 Bluetooth battery management application. This is a major application redesign, not a visual refresh of the existing WinForms dialogs.

The current tested application remains the behavioral reference while the new application is developed. Preserve Bluetooth protocol coverage, battery/profile semantics, settings compatibility, and tray behavior. Do not remove the legacy implementation until the replacement has passed regression and physical-device verification.

## User decisions

- UI framework: WinUI 3.
- Visual direction: Windows 11 Fluent Design, with light/dark/system themes.
- Distribution: both portable/unpackaged and packaged MSIX.
- Keep the Bluetooth acquisition and battery aggregation behavior; refactor boundaries rather than rewrite protocol readers.
- Keep system tray functionality.
- Windows Widgets are in scope.
- Third-party Windows Quick Settings tiles are out of scope because there is no supported public API for them.

## Branch and safety strategy

- Redesign work is isolated on `feature/winui-redesign`, created from the verified Phase 1 commit `c5e0e23f808f61f279f66321ba559c5465c5deb2`.
- The existing `feature/configurable-battery-profiles` branch and its open PR remain untouched.
- Do not merge to `main`, push to upstream, force-push, rewrite history, or delete branches.
- Keep the old WinForms application buildable until the new application reaches feature parity and passes hardware checks.
- Make each milestone a coherent commit with tests and updated documentation.

## Phase 0 — Repository audit and redesign baseline

- [x] Inspect current branch, latest commit, open PR, project layout, entry point, settings persistence, battery models/engines, Bluetooth service, tray lifecycle, and test project.
- [x] Confirm the current app is a single WinForms `.NET 10` executable.
- [x] Identify `TrayApplicationContext` as a major coupling point: it owns scan scheduling, device state, notifications, tray icons, menus, and settings-window orchestration.
- [x] Confirm the battery aggregation/profile engines are already separated into testable services.
- [x] Create an isolated redesign branch from the tested Phase 1 commit.
- [x] Review current Microsoft guidance for unpackaged WinUI 3 deployment, Windows App SDK runtime options, and Windows Widgets provider requirements.

Acceptance: redesign scope, branch safety, current boundaries, and migration constraints are documented.

### Phase 0 findings — deployment constraints

- WinUI 3 unpackaged apps require the Windows App SDK runtime. The portable distribution must either bundle the runtime installer or use Windows App SDK self-contained deployment; self-contained output is larger and the publisher must carry runtime servicing with app updates.
- A single-file EXE is possible only for unpackaged, self-contained WinUI 3 apps (Windows App SDK 1.5+), and it extracts dependencies to a temporary directory at first launch. For this project, a portable ZIP/folder distribution is the safer initial target; do not promise a zero-extraction single binary.
- Unpackaged apps do not have package identity and cannot register a Win32 Windows Widget provider. Microsoft currently supports desktop Win32 widget providers only as packaged apps, registered through the package manifest.
- Therefore, MSIX will be the full-featured distribution with Widgets. The portable build will share the same core/runtime and WinUI dashboard/tray features, but Widgets will be explicitly unavailable. This is a supported product split, not a silent runtime failure.
- Keep the two deployment modes as separate publish configurations from one codebase. Evaluate framework-dependent MSIX versus self-contained MSIX after build-size and installation tests.

References: [Windows App SDK deployment architecture](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/deployment-architecture); [Unpackaged WinUI 3 distribution](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app); [Windows Widget providers](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/widget-service-providers); [C# widget provider walkthrough](https://learn.microsoft.com/en-us/windows/apps/develop/widgets/implement-widget-provider-cs).

## Phase 1 — Application architecture and shared runtime

Goal: separate application behavior and lifetime from WinForms presentation without changing user-visible behavior.

- [ ] Define the new solution/project structure. Proposed boundaries:
  - `BTChargeIndicator.Core`: device snapshots, battery components, aggregation, profiles, application settings contracts, and presentation-independent policies.
  - `BTChargeIndicator.Platform.Windows`: Bluetooth/WinRT acquisition, Windows device watchers, notifications/platform integrations, and Windows-specific adapters.
  - `BTChargeIndicator.App`: WinUI 3 desktop application and navigation.
  - `BTChargeIndicator.Tray` (or a shared host adapter): tray icon rendering, context menus, and lifetime integration, chosen after prototyping the WinUI/unpackaged host constraints.
  - Existing WinForms project retained temporarily as a legacy host or reference during migration.
- [ ] Establish a single application runtime/service container that owns device discovery, refresh scheduling, current device snapshots, profile resolution, notifications, and settings.
- [ ] Publish immutable device-state updates to presentation clients; do not let UI pages call Bluetooth protocol readers directly.
- [ ] Define cancellation, shutdown, single-instance, and resource-disposal ownership.
- [ ] Keep all Bluetooth I/O asynchronous and bounded by existing timeouts.
- [ ] Preserve `settings.json`, `profiles.json`, persisted enum values, and stable device identity. Add explicit migrations only when needed.
- [ ] Extract/refactor only where required by project boundaries; avoid a broad mechanical rewrite.
- [ ] Add architecture tests or focused service tests proving the runtime can be exercised without WinUI controls.
- [ ] Keep legacy WinForms build and tests green during extraction.

Acceptance:
- Bluetooth discovery and profile/aggregation logic can run without a WinForms UI.
- Runtime has one clear owner for scanning and device state.
- Existing settings load without loss.
- Legacy application still builds and behaves as before.

## Phase 2 — WinUI 3 application foundation

- [ ] Add a WinUI 3 desktop project using a supported Windows App SDK version selected after current Microsoft documentation review.
- [ ] Implement application startup, shutdown, single-instance activation, dependency setup, and navigation shell.
- [ ] Create Fluent Design theme resources, system/light/dark theme switching, typography, spacing, icons, and reusable controls.
- [ ] Build sidebar navigation and window layout with responsive behavior.
- [ ] Add localization infrastructure and migrate English/Russian resources without breaking persisted language selection.
- [ ] Add accessible keyboard navigation, high-contrast behavior, and DPI/display scaling support.
- [ ] Establish UI automation or view-model tests where practical.

Acceptance: a stable WinUI shell launches in the intended deployment modes and consumes the shared runtime without duplicating Bluetooth scanning.

## Phase 3 — Device dashboard and device management

- [ ] Build a dashboard with connected, disconnected, and unavailable device states.
- [ ] Show device battery, component battery (left/right/case), source, last update, and connection status without inventing missing values.
- [ ] Add device detail view with diagnostics, raw/source details, and profile selection.
- [ ] Add device actions: hide/show, include/exclude from global aggregate, assign profile, show individual tray icon, and open Windows Bluetooth settings.
- [ ] Add customizable card density/layout and sorting/filtering.
- [ ] Handle loading, no-device, Bluetooth-off, permission-denied, and scan-error states.
- [ ] Ensure partial endpoint observations are merged by the existing stable identity rules.

Acceptance: dashboard reflects the same computed battery values as the current tray and diagnostics paths.

## Phase 4 — Settings and profile experience

- [ ] Rebuild settings as navigable pages: General, Devices, Battery & Profiles, Notifications, Tray, Appearance, Widgets, and Advanced.
- [ ] Preserve profile CRUD, duplicate, rename, delete, import/export, matching criteria, aggregation modes, custom declarative rules, and preview.
- [ ] Preserve Apply / Save / Cancel semantics with isolated edit snapshots.
- [ ] Add settings search and clear validation/error messages.
- [ ] Add tooltip visibility, stale-reading policy, per-profile notification thresholds, and remaining documented follow-up options only with explicit defaults and tests.
- [ ] Preserve existing JSON settings and profile files; add backup-safe migrations and round-trip tests.

Acceptance: every existing user setting remains available or has a documented replacement; no silent settings reset.

## Phase 5 — Tray experience redesign

- [ ] Preserve the global aggregate icon and independent per-device icons.
- [ ] Rebuild tray context menus around shared device actions and current runtime state.
- [ ] Add tray customization where supported: icon style, text/percentage, font, foreground/background, transparency, and warning states.
- [ ] Define technical limits for NotifyIcon rendering and generated icon sizes; avoid promises that Windows does not support.
- [ ] Ensure tray icons update from runtime state and are disposed reliably.
- [ ] Verify tray behavior when the dashboard is closed, logged out, app is restarted, or devices disconnect.
- [ ] Keep tray-only startup available.

Acceptance: the tray remains fully functional independently of the dashboard window.

## Phase 6 — Windows Widgets

- [x] Review the current Microsoft Widgets provider model and package identity requirements before implementation.
- [ ] Implement a widget provider only through supported APIs.
- [ ] Expose a minimal, privacy-conscious device battery snapshot to the widget without duplicating Bluetooth discovery.
- [ ] Support multiple widget instances/configuration where the platform allows it.
- [ ] Define refresh behavior and stale/offline states; do not imply real-time updates beyond platform refresh limits.
- [ ] Verify MSIX registration, widget discovery, activation, updates, and removal.
- [ ] Keep Widgets optional; the portable app must remain usable without them if the platform requires package identity.

Acceptance: widget data matches dashboard/tray state and works only through documented Windows integration.

## Phase 7 — Portable and MSIX distribution

- [x] Select a portable ZIP/folder distribution using Windows App SDK self-contained deployment as the initial candidate; confirm size and behavior in the prototype.
- [ ] Build and test both portable/unpackaged and MSIX variants.
- [ ] Define app identity, package manifest, capabilities, startup behavior, protocol activation, and upgrade behavior.
- [ ] Ensure settings/profiles remain in a stable user data location across install modes and upgrades.
- [ ] Verify clean install, upgrade, uninstall, portable extraction, and portable removal.
- [ ] Keep optional packaged-only features clearly unavailable in portable mode rather than failing silently.

Acceptance: both distribution formats have documented installation, update, data retention, and uninstall behavior.

## Phase 8 — Migration, regression, and hardware verification

- [ ] Run existing protocol and regression tests against the shared code.
- [ ] Add tests for runtime lifecycle, event delivery, settings migration, and duplicate endpoint handling.
- [ ] Verify connect/disconnect/reconnect, partial component data, delayed advertisements, Bluetooth radio changes, and scan cancellation.
- [ ] Execute the hardware matrix for Fast Pair (including Redmi Buds 6 Pro), Nothing/CMF, AirPods, GATT, HFP/PnP, and DualSense where hardware is available.
- [ ] Compare old and new app outputs for the same devices and settings.
- [ ] Verify tray-only startup, dashboard open/close, sleep/resume, and multiple displays/scaling.
- [ ] Review resource disposal, background task cancellation, logs, and privacy.
- [ ] Remove legacy WinForms code only after explicit parity approval and successful verification; otherwise keep it as a fallback build.

Acceptance: Windows CI is green, manual hardware results are recorded, and no known regression remains in supported features.

## Phase 9 — Documentation and release

- [ ] Update README, screenshots, architecture, build, troubleshooting, packaging, and migration documentation.
- [ ] Update CI to build/test/publish both deployment variants.
- [ ] Review license and upstream attribution.
- [ ] Bump version metadata only for a release candidate.
- [ ] Produce portable and MSIX artifacts and verify both on Windows.
- [ ] Summarize completed work, test evidence, known limits, and upgrade path.

Acceptance: reproducible builds, verified installers/artifacts, complete docs, and an explicit release decision.

## Current redesign status

Phase 0 audit and documentation are complete. The redesign branch contains only the two planning documents so far; no application code has been changed. The deployment decision is now explicit: MSIX is the full-featured package with Widgets, while portable/unpackaged uses a self-contained folder/ZIP build and does not offer Widgets. Next: prototype WinUI 3 startup in both deployment modes, then begin Phase 1 extraction in small, buildable steps.
