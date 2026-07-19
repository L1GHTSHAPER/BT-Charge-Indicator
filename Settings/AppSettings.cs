namespace BTChargeIndicator.Settings;

internal sealed class AppSettings
{
    public int RefreshIntervalSeconds { get; set; } = 60;

    public int LowBatteryThreshold { get; set; } = 20;

    public bool LowBatteryNotifications { get; set; } = true;

    public TrayIconStyle IconStyle { get; set; } = TrayIconStyle.Percentage;

    public void Normalize()
    {
        RefreshIntervalSeconds = RefreshIntervalSeconds is 30 or 60 or 300
            ? RefreshIntervalSeconds
            : 60;
        LowBatteryThreshold = Math.Clamp(LowBatteryThreshold, 5, 50);
        IconStyle = Enum.IsDefined(IconStyle)
            ? IconStyle
            : TrayIconStyle.Percentage;
    }
}

internal enum TrayIconStyle
{
    Percentage,
    Battery
}
