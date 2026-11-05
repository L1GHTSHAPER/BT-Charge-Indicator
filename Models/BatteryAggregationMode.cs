namespace BTChargeIndicator.Models;

/// <summary>
/// Defines how a battery percentage is derived from the available component readings.
/// Numeric values are persisted in JSON profiles; do not reorder or reuse them.
/// </summary>
internal enum BatteryAggregationMode
{
    Lowest = 0,
    Highest = 1,
    Average = 2,
    Left = 3,
    Right = 4,
    Case = 5,
    EarbudsLowest = 6,
    EarbudsAverage = 7,
    Custom = 8
}
