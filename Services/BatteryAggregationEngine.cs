using BTChargeIndicator.Models;

namespace BTChargeIndicator.Services;

/// <summary>
/// Pure battery aggregation logic shared by device profiles, tray presentation,
/// notifications, and global multi-device summaries.
/// </summary>
internal static class BatteryAggregationEngine
{
    public static int? Aggregate(
        BatteryComponents? components,
        BatteryAggregationMode mode,
        int? fallback = null)
    {
        if (components is null)
        {
            return mode is BatteryAggregationMode.Left or BatteryAggregationMode.Right
                or BatteryAggregationMode.Case or BatteryAggregationMode.Custom
                ? null
                : Normalize(fallback);
        }

        return mode switch
        {
            BatteryAggregationMode.Left => Normalize(components.LeftPercent),
            BatteryAggregationMode.Right => Normalize(components.RightPercent),
            BatteryAggregationMode.Case => Normalize(components.CasePercent),
            BatteryAggregationMode.Lowest => AggregateKnown(
                [components.LeftPercent, components.RightPercent, components.CasePercent],
                static values => values.Min(), fallback),
            BatteryAggregationMode.Highest => AggregateKnown(
                [components.LeftPercent, components.RightPercent, components.CasePercent],
                static values => values.Max(), fallback),
            BatteryAggregationMode.Average => AggregateKnown(
                [components.LeftPercent, components.RightPercent, components.CasePercent],
                static values => (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero), fallback),
            BatteryAggregationMode.EarbudsLowest => AggregateEarbuds(
                components.LeftPercent,
                components.RightPercent,
                Normalize(fallback) ?? Normalize(components.CasePercent),
                static values => values.Min()),
            BatteryAggregationMode.EarbudsAverage => AggregateEarbuds(
                components.LeftPercent,
                components.RightPercent,
                Normalize(fallback) ?? Normalize(components.CasePercent),
                static values => (int)Math.Round(values.Average(), MidpointRounding.AwayFromZero)),
            _ => Normalize(fallback)
        };
    }

    public static int? AggregateValues(
        IEnumerable<int?> percentages,
        BatteryAggregationMode mode)
    {
        ArgumentNullException.ThrowIfNull(percentages);

        var known = percentages
            .Select(Normalize)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        if (known.Length == 0)
        {
            return null;
        }

        return mode switch
        {
            BatteryAggregationMode.Highest => known.Max(),
            BatteryAggregationMode.Average => (int)Math.Round(
                known.Average(),
                MidpointRounding.AwayFromZero),
            _ => known.Min()
        };
    }

    private static int? AggregateEarbuds(
        int? left,
        int? right,
        int? fallback,
        Func<int[], int> aggregate)
    {
        var known = new[] { Normalize(left), Normalize(right) }
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return known.Length > 0 ? aggregate(known) : Normalize(fallback);
    }

    private static int? AggregateKnown(
        IEnumerable<int?> values,
        Func<int[], int> aggregate,
        int? fallback)
    {
        var known = values
            .Select(Normalize)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        return known.Length == 0 ? Normalize(fallback) : aggregate(known);
    }


    private static int? Normalize(int? value) =>
        value is >= 0 and <= 100 ? value : null;
}
