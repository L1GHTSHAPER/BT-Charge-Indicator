namespace BTChargeIndicator.Models;

internal sealed record TwsBatteryReading(
    int? LeftPercent,
    int? RightPercent,
    int? CasePercent)
{
    public BatteryComponents Components => new(LeftPercent, RightPercent, CasePercent);

    public int? LowestPercent
    {
        get
        {
            var known = new[] { LeftPercent, RightPercent, CasePercent }
                .OfType<int>()
                .ToArray();
            return known.Length == 0 ? null : known.Min();
        }
    }

    public bool HasKnownLevel => LeftPercent.HasValue || RightPercent.HasValue || CasePercent.HasValue;
}
