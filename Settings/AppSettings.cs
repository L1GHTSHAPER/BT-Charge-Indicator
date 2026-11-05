using BTChargeIndicator.Models;

namespace BTChargeIndicator.Settings;

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;

    public AppLanguage Language { get; set; } = AppLanguage.Auto;

    public int RefreshIntervalSeconds { get; set; } = 60;

    public int LowBatteryThreshold { get; set; } = 20;

    public bool LowBatteryNotifications { get; set; } = true;

    public bool ConnectionNotifications { get; set; } = true;

    public TrayIconStyle IconStyle { get; set; } = TrayIconStyle.Percentage;

    public BatteryAggregationMode GlobalAggregationMode { get; set; } = BatteryAggregationMode.Lowest;

    public string? PreferredTrayDeviceKey { get; set; }

    public bool CheckForUpdatesAutomatically { get; set; } = true;

    public DateTimeOffset? LastUpdateCheckUtc { get; set; }

    public Dictionary<string, DevicePreferences> Devices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AppSettings DeepClone()
    {
        var clone = new AppSettings
        {
            SchemaVersion = SchemaVersion,
            Language = Language,
            RefreshIntervalSeconds = RefreshIntervalSeconds,
            LowBatteryThreshold = LowBatteryThreshold,
            LowBatteryNotifications = LowBatteryNotifications,
            ConnectionNotifications = ConnectionNotifications,
            IconStyle = IconStyle,
            GlobalAggregationMode = GlobalAggregationMode,
            PreferredTrayDeviceKey = PreferredTrayDeviceKey,
            CheckForUpdatesAutomatically = CheckForUpdatesAutomatically,
            LastUpdateCheckUtc = LastUpdateCheckUtc
        };
        foreach (var (key, value) in Devices)
        {
            if (value is null) continue;
            clone.Devices[key] = new DevicePreferences
            {
                LastKnownName = value.LastKnownName,
                CustomName = value.CustomName,
                ProfileId = value.ProfileId,
                BatteryMode = value.BatteryMode,
                ShowIndividualTrayIcon = value.ShowIndividualTrayIcon,
                IndividualTrayIconStyle = value.IndividualTrayIconStyle,
                IsHidden = value.IsHidden,
                IncludeInTrayIcon = value.IncludeInTrayIcon,
                LowBatteryNotifications = value.LowBatteryNotifications,
                ConnectionNotifications = value.ConnectionNotifications
            };
        }
        return clone;
    }

    public void Normalize()
    {
        SchemaVersion = 1;
        Language = Enum.IsDefined(Language) ? Language : AppLanguage.Auto;
        RefreshIntervalSeconds = RefreshIntervalSeconds is 30 or 60 or 300
            ? RefreshIntervalSeconds
            : 60;
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 5, 50);
        IconStyle = Enum.IsDefined(IconStyle)
            ? IconStyle
            : TrayIconStyle.Percentage;
        GlobalAggregationMode = GlobalAggregationMode is BatteryAggregationMode.Lowest
            or BatteryAggregationMode.Highest or BatteryAggregationMode.Average
            ? GlobalAggregationMode
            : BatteryAggregationMode.Lowest;

        Devices = new Dictionary<string, DevicePreferences>(
            Devices ?? [],
            StringComparer.OrdinalIgnoreCase);
        foreach (var key in Devices.Keys.ToArray())
        {
            var preferences = Devices[key];
            if (preferences is null)
            {
                Devices[key] = new DevicePreferences();
                continue;
            }

            if (preferences.IndividualTrayIconStyle.HasValue &&
                !Enum.IsDefined(preferences.IndividualTrayIconStyle.Value))
            {
                preferences.IndividualTrayIconStyle = null;
            }
            if (preferences.BatteryMode.HasValue && !Enum.IsDefined(preferences.BatteryMode.Value))
            {
                preferences.BatteryMode = null;
            }
        }
    }
}

internal enum AppLanguage
{
    // Persisted in settings.json; keep these IDs stable.
    Auto = 0,
    Russian = 1,
    English = 2
}

internal sealed class DevicePreferences
{
    public string? LastKnownName { get; set; }

    public string? CustomName { get; set; }

    public string? ProfileId { get; set; }

    public BatteryAggregationMode? BatteryMode { get; set; }

    public bool ShowIndividualTrayIcon { get; set; }

    public TrayIconStyle? IndividualTrayIconStyle { get; set; }

    public bool IsHidden { get; set; }

    public bool IncludeInTrayIcon { get; set; } = true;

    public bool LowBatteryNotifications { get; set; } = true;

    public bool ConnectionNotifications { get; set; } = true;
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
