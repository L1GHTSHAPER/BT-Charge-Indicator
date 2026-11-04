using BTChargeIndicator.Models;

namespace BTChargeIndicator.Settings;

/// <summary>
/// A versioned, user-editable policy for matching a Bluetooth device and presenting its battery.
/// </summary>
internal sealed class BatteryProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "New profile";

    public BatteryProfileMatch Match { get; set; } = new();

    public BatteryAggregationMode AggregationMode { get; set; } = BatteryAggregationMode.EarbudsLowest;

    public BatteryAggregationRule? CustomRule { get; set; }
}

internal sealed class BatteryProfileMatch
{
    public List<string> ContainerIds { get; set; } = [];

    public List<string> NameContains { get; set; } = [];

    public List<string> Categories { get; set; } = [];

    public List<BatteryReadingSource> Sources { get; set; } = [];
}

/// <summary>
/// Safe, declarative custom aggregation. It can only select known battery components
/// and apply a built-in operation; it never evaluates user-provided code.
/// </summary>
internal sealed class BatteryAggregationRule
{
    public List<BatteryComponentKind> Components { get; set; } =
        [BatteryComponentKind.Left, BatteryComponentKind.Right];

    public BatteryAggregationOperation Operation { get; set; } = BatteryAggregationOperation.Minimum;
}

internal enum BatteryComponentKind
{
    Left = 0,
    Right = 1,
    Case = 2,
    Device = 3
}

internal enum BatteryAggregationOperation
{
    Minimum = 0,
    Maximum = 1,
    Average = 2,
    FirstAvailable = 3
}

internal sealed class BatteryProfileDocument
{
    public int SchemaVersion { get; set; } = BatteryProfileStore.CurrentSchemaVersion;

    public List<BatteryProfile> Profiles { get; set; } = [];
}
