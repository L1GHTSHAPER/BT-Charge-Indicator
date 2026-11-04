namespace BTChargeIndicator.Models;

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
