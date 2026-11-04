# Repository instructions for AI coding agents

## Start here
1. Read `IMPLEMENTATION_PLAN.md` completely.
2. Read the relevant files under `docs/` before changing architecture or behavior.
3. Check the current branch, working tree, recent commits, and open changes. Never overwrite unrelated user work.
4. Work on `feature/configurable-battery-profiles` unless the maintainer explicitly selects another branch.

## Project
BT Charge Indicator is a native Windows 10/11 system-tray application written in C# WinForms. It reads Bluetooth battery data through Windows device properties, HFP/PnP, BLE GATT, Apple Continuity, Google Fast Pair, Nothing/CMF RFCOMM, and Sony DualSense HID.

## Non-negotiable behavior
- Preserve existing Bluetooth discovery and protocol support unless a tested replacement is required.
- Protocol adapters decode raw readings. They must not decide application-wide presentation policy.
- Keep component readings separate: left earbud, right earbud, charging case, and future component types.
- Never silently treat an unknown or missing battery value as 0%.
- The default TWS primary percentage is the lowest known earbud level (left/right). The case is shown separately and is excluded from that primary percentage when either earbud level is known. If neither earbud level is known, a case-only reading may be used as a fallback until a profile overrides it.
- Keep per-device aggregation separate from global aggregation across devices.
- A device profile must be able to select the displayed component(s), aggregation method, tooltip details, tray style, and notification behavior.
- Keep the user's requested configurable behavior data-driven. JSON profiles must be versioned, validated, safely migrated, and edited through the app UI.
- Custom rules must be declarative and constrained to known components and operations. Do not execute arbitrary C#, scripts, expressions, or downloaded code.
- Per-device icons and the global aggregate must have independent settings and clear inclusion rules.
- Do not make a virtual battery driver or pretend headphones are a laptop battery.

## Engineering standards
- Follow the existing WinForms/.NET style unless a documented technical reason justifies a change.
- Keep Bluetooth I/O asynchronous and bounded by timeouts. Do not block the UI thread.
- Dispose timers, icons, bitmaps, device handles, event subscriptions, and other native resources.
- Validate all user-editable settings and profile input. Write files atomically and preserve recoverability on malformed JSON.
- Avoid logging Bluetooth MAC addresses or other unnecessary identifiers. Do not add telemetry.
- Add focused tests for aggregation, profile matching, serialization/migration, missing/stale values, and multi-device behavior.
- Preserve existing persisted enum numeric values and settings compatibility.
- Keep English and Russian localization in sync when adding user-facing text.

## Workflow
- Implement one phase at a time, following the plan's acceptance criteria.
- Before each phase, inspect relevant code and identify compatibility risks.
- After each meaningful change, run the most relevant tests and a Release build on Windows.
- Update `IMPLEMENTATION_PLAN.md` and testing documentation with actual results. Never claim a test passed unless it was run.
- Review the complete diff for regressions, accidental binary changes, secrets, and unrelated edits.
- Do not force-push, rewrite history, delete branches, or use destructive Git commands.
- Commit coherent, tested changes with clear messages. Push only the project branch; never push directly to upstream `L1GHTSHAPER/BT-Charge-Indicator`.
- If the Windows build or hardware verification cannot be performed in the current environment, record that limitation and provide exact local verification steps.
