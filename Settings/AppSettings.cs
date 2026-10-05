namespace BTChargeIndicator.Settings;

internal sealed class AppSettings
{
    public int RefreshIntervalSeconds { get; set; } = 60;

    public int LowBatteryThreshold { get; set; } = 20;

    public bool LowBatteryNotifications { get; set; } = true;

    public TrayIconStyle IconStyle { get; set; } = TrayIconStyle.Percentage;

    public bool CheckForUpdatesAutomatically { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public Dictionary<string, DevicePreferences> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public void Normalize()
    {
        RefreshIntervalSeconds = RefreshIntervalSeconds is 30 or 60 or 300
            ? RefreshIntervalSeconds
            : 60;
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 5, 50);
        IconStyle = Enum.IsDefined(IconStyle)
            ? IconStyle
            : TrayIconStyle.Percentage;

        Devices = new Dictionary<string, DevicePreferences>(
            Devices ?? [],
            StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class DevicePreferences
{
    public string? LastKnownName { get; set; }

    public string? CustomName { get; set; }

    public bool IsHidden { get; set; }

    public bool IncludeInTrayIcon { get; set; } = true;

    public bool LowBatteryNotifications { get; set; } = true;
}

internal enum TrayIconStyle
{
    // These values are persisted in settings.json; keep existing IDs stable.
    Percentage = 0,
    Battery = 1,
    Ring = 2,
    SegmentedRing = 3,
    Bars = 4,
    VerticalBattery = 5,
    Minimal = 6,
    Capsule = 7,
    Gradient = 8,
    Gauge = 9
}
