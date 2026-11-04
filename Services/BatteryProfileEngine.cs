using BTChargeIndicator.Models;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.Services;

/// <summary>
/// Resolves a device profile and calculates the device's effective battery percentage.
/// Matching is deterministic and does not depend on a Bluetooth MAC address.
/// </summary>
internal static class BatteryProfileEngine
{
    public const string DefaultProfileId = "default";

    public static BatteryProfile CreateDefaultProfile() => new()
    {
        Id = DefaultProfileId,
        Name = "Default",
        AggregationMode = BatteryAggregationMode.EarbudsLowest
    };

    public static BatteryProfile Resolve(
        BluetoothBatteryDevice device,
        IEnumerable<BatteryProfile> profiles,
        string? explicitlyAssignedProfileId = null)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(profiles);

        var available = profiles
            .Where(IsValidProfile)
            .ToArray();

        if (!string.IsNullOrWhiteSpace(explicitlyAssignedProfileId))
        {
            var explicitlyAssigned = available.FirstOrDefault(profile =>
                string.Equals(profile.Id, explicitlyAssignedProfileId, StringComparison.OrdinalIgnoreCase));
            if (explicitlyAssigned is not null)
            {
                return explicitlyAssigned;
            }
        }

        return available
            .Where(profile => !string.Equals(profile.Id, DefaultProfileId, StringComparison.OrdinalIgnoreCase))
            .Select(profile => (Profile: profile, Score: MatchScore(device, profile.Match)))
            .Where(candidate => candidate.Score > 0)
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Profile.Id, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.Profile)
            .FirstOrDefault()
            ?? available.FirstOrDefault(profile =>
                string.Equals(profile.Id, DefaultProfileId, StringComparison.OrdinalIgnoreCase))
            ?? CreateDefaultProfile();
    }

    public static int? GetBatteryPercent(
        BluetoothBatteryDevice device,
        BatteryProfile profile)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(profile);

        if (device.Components is null)
        {
            return Normalize(device.BatteryPercent);
        }

        if (profile.AggregationMode == BatteryAggregationMode.Custom)
        {
            return AggregateCustom(device, profile.CustomRule) ?? Normalize(device.BatteryPercent);
        }

        return BatteryAggregationEngine.Aggregate(
            device.Components,
            profile.AggregationMode,
            device.BatteryPercent);
    }

    public static bool IsValidProfile(BatteryProfile? profile)
    {
        if (profile is null ||
            string.IsNullOrWhiteSpace(profile.Id) ||
            string.IsNullOrWhiteSpace(profile.Name) ||
            profile.Id.Length > 80 ||
            profile.Name.Length > 80 ||
            !Enum.IsDefined(profile.AggregationMode))
        {
            return false;
        }

        if (profile.AggregationMode == BatteryAggregationMode.Custom)
        {
            return IsValidRule(profile.CustomRule);
        }

        return profile.CustomRule is null || IsValidRule(profile.CustomRule);
    }

    public static bool IsValidRule(BatteryAggregationRule? rule) =>
        rule is not null &&
        Enum.IsDefined(rule.Operation) &&
        rule.Components is { Count: > 0 and <= 4 } &&
        rule.Components.All(Enum.IsDefined) &&
        rule.Components.Distinct().Count() == rule.Components.Count;

    private static int MatchScore(BluetoothBatteryDevice device, BatteryProfileMatch? match)
    {
        if (match is null)
        {
            return 0;
        }

        var score = 0;
        var hasCriteria = false;

        if (match.ContainerIds.Count > 0)
        {
            hasCriteria = true;
            if (!device.ContainerId.HasValue ||
                !match.ContainerIds.Any(id => Guid.TryParse(id, out var parsed) && parsed == device.ContainerId))
            {
                return 0;
            }
            score += 100;
        }

        if (match.Categories.Count > 0)
        {
            hasCriteria = true;
            if (!match.Categories.Any(category =>
                    device.Categories.Contains(category, StringComparer.OrdinalIgnoreCase)))
            {
                return 0;
            }
            score += 30;
        }

        if (match.NameContains.Count > 0)
        {
            hasCriteria = true;
            if (!match.NameContains.Any(value =>
                    device.Name.Contains(value, StringComparison.OrdinalIgnoreCase)))
            {
                return 0;
            }
            score += 20;
        }

        if (match.Sources.Count > 0)
        {
            hasCriteria = true;
            if (!match.Sources.Contains(device.BatterySource))
            {
                return 0;
            }
            score += 10;
        }

        return hasCriteria ? score : 0;
    }

    private static int? AggregateCustom(BluetoothBatteryDevice device, BatteryAggregationRule? rule)
    {
        if (!IsValidRule(rule) || device.Components is null)
        {
            return null;
        }

        var values = rule!.Components
            .Select(component => component switch
            {
                BatteryComponentKind.Left => device.Components.LeftPercent,
                BatteryComponentKind.Right => device.Components.RightPercent,
                BatteryComponentKind.Case => device.Components.CasePercent,
                BatteryComponentKind.Device => device.BatteryPercent,
                _ => null
            })
            .Select(Normalize)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();

        if (values.Length == 0)
        {
            return null;
        }

        return rule.Operation switch
        {
            BatteryAggregationOperation.Minimum => values.Min(),
            BatteryAggregationOperation.Maximum => values.Max(),
            BatteryAggregationOperation.Average => (int)Math.Round(
                values.Average(), MidpointRounding.AwayFromZero),
            BatteryAggregationOperation.FirstAvailable => values[0],
            _ => null
        };
    }

    private static int? Normalize(int? value) =>
        value is >= 0 and <= 100 ? value : null;
}
