# Bluetooth hardware verification matrix

Automated tests cover parser behavior, aggregation, profile matching, settings compatibility, JSON recovery, and endpoint deduplication. The checks below require physical Windows Bluetooth hardware and must only be marked verified after an actual run.

| Device / protocol | Checks | Status | Notes |
|---|---|---|---|
| Redmi Buds 6 Pro / Google Fast Pair | L/R/case parsing; L=100%, R=100%, case=55% produces primary=100%; case remains in details | Not yet hardware-verified | User-provided diagnostic values define the regression scenario |
| Nothing / CMF RFCOMM | Separate left/right/case readings; reconnect and fragmented-frame handling | Not yet hardware-verified | Parser tests run in CI |
| Apple AirPods / Continuity BLE | Left/right/case readings and charging-state transitions | Not yet hardware-verified | Existing protocol support must remain unchanged |
| Standard BLE GATT Battery Service | Single battery and labeled multiple instances | Not yet hardware-verified | Existing GATT discovery path must remain unchanged |
| Bluetooth HFP / PnP | Scalar headset battery and delayed battery updates | Not yet hardware-verified | Existing Windows property path must remain unchanged |
| Sony DualSense / Bluetooth HID | Battery reading and reconnect behavior | Not yet hardware-verified | Existing HID path must remain unchanged |
| Two or more Bluetooth devices | Per-device icons, individual styles, global min/max/average | Not yet hardware-verified | Automated aggregation tests are available |
| One device with Classic and BLE endpoints | Merge component readings; avoid duplicate global counting and duplicate icons | Not yet hardware-verified | Automated container-identity deduplication test is available |
| Settings and profile editor | Save/cancel, profile import/export, restart persistence, DPI scaling | Not yet hardware-verified | Manual procedure is in docs/TESTING.md |

## Redmi Buds 6 Pro acceptance scenario

1. Connect the earbuds to a Windows 10/11 machine.
2. Open device diagnostics and confirm left, right, and case readings.
3. Confirm the default tray percentage is the lowest known earbud value, not the case.
4. Confirm the tooltip still shows the case independently.
5. Create a profile selecting Case and confirm the tray percentage changes to the case value.
6. Restore the Default profile and confirm the earbud percentage returns.

Do not mark any row as verified based only on parser fixtures or CI.
