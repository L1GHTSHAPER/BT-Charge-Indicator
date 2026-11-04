# Testing guide

## Automated tests
Run from a Windows machine with the .NET 9 SDK:

```powershell
dotnet build .\BTChargeIndicator.csproj --configuration Release
dotnet run --project .\Tests\BTChargeIndicator.ProtocolTests.csproj --configuration Release
```

The protocol test executable covers Fast Pair and Nothing/CMF parsing, device change tracking, connection notifications, and localization. New domain/profile tests should be added here or in a dedicated test project if that improves isolation.

## CI
The GitHub Actions workflow `.github/workflows/windows-ci.yml` runs a Release build and the protocol test executable on Windows.

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
