# Battery profiles

Battery profiles separate Bluetooth protocol decoding from the way the application presents battery levels. Protocol readers continue to report the values they receive; profiles select the values shown by the tray, device list, and notifications.

## File locations

- Global application settings: `%LOCALAPPDATA%/BT Charge Indicator/settings.json`
- Device profiles: `%LOCALAPPDATA%/BT Charge Indicator/profiles.json`
- Previous valid profile document: `profiles.json.bak`

The profile document has a `schemaVersion` field. The current version is `1`. The application validates a document before using it. Invalid profiles are skipped; an invalid document falls back to the last valid backup, if available. The malformed original file is not overwritten automatically.

## Matching

Each profile may define one or more match groups:
- `containerIds`: Windows device container GUIDs. This is the strongest stable identity match.
- `nameContains`: case-insensitive device name fragments.
- `categories`: Windows device category strings.
- `sources`: battery source enum names, such as `GoogleFastPairAdvertisement`.

Every configured match group must match. Within one group, any listed value may match. Profiles are ranked by specificity: container ID, category, name, then source. An explicit per-device profile assignment takes priority over automatic matching. If nothing matches, the built-in Default profile is used.

Do not use a Bluetooth MAC address as the only profile key. Some devices rotate their address.

## Built-in aggregation modes

- `EarbudsLowest`: lowest known left/right earbud value; case is only a fallback if neither earbud is known.
- `EarbudsAverage`: average of known left/right values; case is only a fallback if neither earbud is known.
- `Left`, `Right`, `Case`: select exactly that component. A missing selected component stays unknown.
- `Lowest`, `Highest`, `Average`: aggregate all known components, including the case.
- `Custom`: use the declarative rule described below.

For devices without component readings, the profile engine uses the device's scalar battery value. Unknown or invalid values are never converted to zero.

## Safe custom rules

A custom rule can select one or more of `Left`, `Right`, `Case`, and `Device`, then apply one of four operations:
- `Minimum`
- `Maximum`
- `Average`
- `FirstAvailable`

The rule is data only. The application does not execute C#, scripts, formulas, or expressions from profile files. Duplicate components, empty selections, unsupported operations, and invalid percentages are rejected or ignored safely.

## Example

The accompanying `examples/profiles.json` contains:
1. A Redmi Buds profile that matches the model name and Fast Pair source, then uses the lowest earbud battery.
2. A custom profile that compares the right earbud with the case.

To edit a profile manually, close the application first, back up `profiles.json`, edit the JSON, and restart the application. A graphical profile editor is planned for a later phase.
