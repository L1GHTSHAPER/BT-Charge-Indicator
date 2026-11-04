namespace BTChargeIndicator.Models;

internal sealed record TwsBatteryReading(
    int? LeftPercent,
    int? RightPercent,
    int? CasePercent)
{
    public BatteryComponents Components => new(LeftPercent, RightPercent, CasePercent);

    // The primary TWS value represents the earbuds, not the charging case.
    // If neither earbud reports a level, retain a useful case-only fallback.
    public int? LowestPercent
    {
        get
        {
            var knownEarbuds = new[] { LeftPercent, RightPercent }
                .OfType<int>()
                .ToArray();

            if (knownEarbuds.Length > 0)
            {
                return knownEarbuds.Min();
            }

            return CasePercent;
        }
    }

    public bool HasKnownLevel => LeftPercent.HasValue || RightPercent.HasValue || CasePercent.HasValue;
}
