# Testing guide

## Automated tests
Run from a Windows machine with the .NET 10 SDK:

```powershell
dotnet build .\BTChargeIndicator.csproj --configuration Release
dotnet run --project .\Tests\BTChargeIndicator.ProtocolTests.csproj --configuration Release
```

The protocol test executable covers Fast Pair and Nothing/CMF parsing, device change tracking, connection notifications, and localization. New domain/profile tests should be added here or in a dedicated test project if that improves isolation.

## CI
The GitHub Actions workflow `.github/workflows/windows-ci.yml` runs a Release build and the protocol test executable on Windows, then publishes a self-contained x64 executable as the `BTChargeIndicator-win-x64` artifact. Download it from the successful workflow run's **Artifacts** section. Artifacts are retained for 14 days.

## Manual hardware checks
1. Pair and connect a supported Bluetooth device.
2. Confirm the device appears and reports a source and connection state.
3. For TWS devices, verify left/right/case values independently.
4. Confirm a low case does not lower the default primary earbud percentage when left/right are known.
5. Disconnect/reconnect and verify stale readings are not presented as fresh.
6. Check tray tooltip, low-battery notification, and global aggregation.
7. Repeat after Bluetooth radio off/on and application restart.

## Redmi Buds 6 Pro regression scenario
Expected input: left=100, right=100, case=55.
Expected default primary percentage: 100.
Expected component display: L 100%, R 100%, case 55%.
The case must remain visible as a separate component.

## Profile and tray regression checks
1. Open **Manage battery profiles** from the tray menu.
2. Create a profile, set name/category/source match criteria, and choose an aggregation mode.
3. Preview the sample values (left=100, right=100, case=55).
4. Create a custom rule, select components and an operation, and confirm the preview.
5. Export the profile, import it under a different ID, and save.
6. Assign the profile to a device through its device submenu.
7. Enable that device's separate tray icon and select an individual icon style.
8. Change the global aggregation mode and verify the main tray icon updates independently.
9. Verify the separate icon disappears when disabled and that exiting the application disposes all tray icons.
10. Corrupt `profiles.json` while a valid `.bak` exists; verify the application recovers the backup without overwriting the malformed file.

## Settings window checks
1. Open **Settings** from the main tray menu.
2. Confirm Overview reports paired, connected, and custom-profile counts.
3. Change a device's global inclusion, individual tray icon, profile, and icon style.
4. Change global aggregation, global icon style, and low-battery threshold.
5. Change language, refresh interval, automatic updates, and startup option.
6. Press **Apply** and confirm the running tray icon, menus, notifications, and refresh interval update without closing Settings.
7. Make another change, press **Cancel**, and confirm the last applied values remain active while newer edits are discarded.
8. Reopen Settings, make changes, and press **Save changes**; confirm they are applied and the window closes.
9. Reopen Settings, make changes, and close with **Cancel** or the window close button; confirm unapplied edits are discarded.
10. Open the profile editor from the Profiles tab, create a profile, return, and confirm device profile selectors refresh.
11. Test the window at 100%, 150%, and 200% display scaling and with keyboard-only navigation.

## Individual tray context menu checks
1. Enable a separate tray icon for a Bluetooth device.
2. Right-click that icon and verify it exposes the same device-specific actions as the main tray icon's device submenu, including diagnostics and individual tray style.
3. Open **Individual tray icon style** and verify every style has the same image preview as the global style menu.
4. Select a style, reopen the menu, and confirm the selected style is checked.
5. Select **Use global style** and confirm the global style is inherited and the device-specific override is cleared.

## Hardware matrix
Use [HARDWARE_VERIFICATION.md](HARDWARE_VERIFICATION.md) to record physical device checks. Do not mark a protocol or device verified based only on parser fixtures or CI.
