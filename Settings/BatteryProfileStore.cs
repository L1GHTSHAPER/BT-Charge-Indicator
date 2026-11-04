using System.Text.Json;
using System.Text.Json.Serialization;
using BTChargeIndicator.Services;

namespace BTChargeIndicator.Settings;

/// <summary>
/// Loads and saves user battery profiles in a separately versioned JSON document.
/// Invalid input is never overwritten automatically.
/// </summary>
internal sealed class BatteryProfileStore
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _profilesPath;

    public BatteryProfileStore()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BT Charge Indicator",
            "profiles.json"))
    {
    }

    internal BatteryProfileStore(string profilesPath)
    {
        _profilesPath = profilesPath;
    }

    public IReadOnlyList<BatteryProfile> Load()
    {
        if (!File.Exists(_profilesPath))
        {
            return [BatteryProfileEngine.CreateDefaultProfile()];
        }

        try
        {
            var document = JsonSerializer.Deserialize<BatteryProfileDocument>(
                File.ReadAllText(_profilesPath), JsonOptions);

            if (document is null || document.SchemaVersion != CurrentSchemaVersion ||
                document.Profiles is null || document.Profiles.Count > 100)
            {
                return [BatteryProfileEngine.CreateDefaultProfile()];
            }

            var profiles = new List<BatteryProfile> { BatteryProfileEngine.CreateDefaultProfile() };
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { BatteryProfileEngine.DefaultProfileId };

            foreach (var profile in document.Profiles)
            {
                if (!BatteryProfileEngine.IsValidProfile(profile) ||
                    !ids.Add(profile.Id) ||
                    string.Equals(profile.Id, BatteryProfileEngine.DefaultProfileId, StringComparison.OrdinalIgnoreCase) ||
                    !IsValidMatch(profile.Match))
                {
                    continue;
                }

                profiles.Add(profile);
            }

            return profiles;
        }
        catch
        {
            // Keep the original file intact so a user can repair or recover it.
            return [BatteryProfileEngine.CreateDefaultProfile()];
        }
    }

    public void Save(IEnumerable<BatteryProfile> profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        var normalized = new List<BatteryProfile>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            BatteryProfileEngine.DefaultProfileId
        };

        foreach (var profile in profiles)
        {
            if (profile is null ||
                string.Equals(profile.Id, BatteryProfileEngine.DefaultProfileId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!BatteryProfileEngine.IsValidProfile(profile) ||
                !IsValidMatch(profile.Match) ||
                !ids.Add(profile.Id))
            {
                throw new InvalidDataException($"Invalid or duplicate battery profile: {profile.Id}");
            }

            normalized.Add(profile);
            if (normalized.Count > 99)
            {
                throw new InvalidDataException("A maximum of 99 custom battery profiles is supported.");
            }
        }

        var document = new BatteryProfileDocument
        {
            SchemaVersion = CurrentSchemaVersion,
            Profiles = normalized
        };

        var directory = Path.GetDirectoryName(_profilesPath)
            ?? throw new InvalidOperationException("The profile file must have a parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _profilesPath + ".tmp";
        var backupPath = _profilesPath + ".bak";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(document, JsonOptions));
            if (File.Exists(_profilesPath))
            {
                File.Copy(_profilesPath, backupPath, true);
            }
            File.Move(temporaryPath, _profilesPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool IsValidMatch(BatteryProfileMatch? match)
    {
        if (match is null ||
            match.ContainerIds is null ||
            match.NameContains is null ||
            match.Categories is null ||
            match.Sources is null ||
            match.ContainerIds.Count > 50 ||
            match.NameContains.Count > 50 ||
            match.Categories.Count > 50 ||
            match.Sources.Count > 20)
        {
            return false;
        }

        return match.ContainerIds.All(value => Guid.TryParse(value, out _)) &&
               match.NameContains.All(IsShortNonEmptyText) &&
               match.Categories.All(IsShortNonEmptyText) &&
               match.Sources.All(Enum.IsDefined);
    }

    private static bool IsShortNonEmptyText(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 256;

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
