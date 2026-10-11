using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using BTChargeIndicator.Localization;
using BTChargeIndicator.Services;
using BTChargeIndicator.Settings;

internal static class LocalizationTests
{
    public static async Task RunAsync()
    {
        foreach (var culture in new[] { "ru-RU", "ru-BY", "en-US", "en-GB", "de-DE", "ja-JP" })
        {
            var windowsCulture = CultureInfo.GetCultureInfo(culture);
            Check(AppText.ResolveCulture(AppLanguage.Auto, windowsCulture).Name ==
                  (culture.StartsWith("ru-", StringComparison.Ordinal) ? "ru" : "en"), "Windows language fallback");
            Check(AppText.ResolveCulture(AppLanguage.English, windowsCulture).Name == "en", "English override");
            Check(AppText.ResolveCulture(AppLanguage.Russian, windowsCulture).Name == "ru", "Russian override");
        }

        var oldSettings = JsonSerializer.Deserialize<AppSettings>(
            """{"IconStyle":8,"Devices":{"headset":{"CustomName":"Мои наушники"}}}""")!;
        oldSettings.Normalize();
        Check(oldSettings.Language == AppLanguage.Auto && oldSettings.IconStyle == TrayIconStyle.Gradient,
            "Existing settings default to automatic language and retain tray style");
        foreach (var language in Enum.GetValues<AppLanguage>())
        {
            oldSettings.Language = language;
            var restored = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(oldSettings))!;
            restored.Normalize();
            Check(restored.Language == language && restored.Devices["HEADSET"].CustomName == "Мои наушники",
                "Language persists without translating device names");
        }
        oldSettings.Language = (AppLanguage)999;
        oldSettings.Normalize();
        Check(oldSettings.Language == AppLanguage.Auto, "Invalid saved language defaults to automatic");

        var resources = new ResourceManager("BTChargeIndicator.Localization.Strings", typeof(AppText).Assembly);
        var english = Entries(resources.GetResourceSet(CultureInfo.InvariantCulture, true, false)!);
        var russian = Entries(resources.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false)!);
        Check(english.Count > 0 && english.Keys.ToHashSet().SetEquals(russian.Keys),
            "Both language catalogs are complete without resource fallback");
        foreach (var (key, value) in english)
        {
            Check(!string.IsNullOrWhiteSpace(value) && !string.IsNullOrWhiteSpace(russian[key]), "Nonempty translation: " + key);
            Check(Arguments(value).SequenceEqual(Arguments(russian[key])), "Matching format arguments: " + key);
            // Parsing every template catches malformed braces, including strings not used in the current device state.
            CompositeFormat.Parse(value);
            CompositeFormat.Parse(russian[key]);
        }

        foreach (var language in new[] { AppLanguage.English, AppLanguage.Russian })
        {
            AppText.Apply(language);
            Check(AppText.Get("ConnectionCaption") == (language == AppLanguage.English ? "CONNECTED" : "ПОДКЛЮЧЕНО"),
                "Connection card uses selected language");
            Check(AppText.Format("ConnectionBattery", 76).Contains("76%", StringComparison.Ordinal), "Formatted battery level");
            Check(AppText.Format("UpdatedAt", new DateTime(2026, 10, 11, 14, 5, 0)).EndsWith("14:05", StringComparison.Ordinal),
                "Time template is valid in both languages");
            foreach (var snooze in new[] { true, false })
            {
                var xml = XDocument.Parse(DesktopNotificationService.BuildToastXml(
                    AppText.Get("LowBatteryTitle"), "Мои <наушники> & \"Buds\": 15%", AppText.Get("OpenBluetooth"),
                    "ms-settings:bluetooth?x=1&y=2", snooze));
                Check(xml.Root!.Attribute("duration")!.Value == "short", "Normal Windows notification timeout retained");
                Check(xml.Descendants("text").Last().Value == "Мои <наушники> & \"Buds\": 15%", "Device names are XML escaped");
                var actions = xml.Descendants("action").ToArray();
                Check(actions.Single(action => action.Attribute("arguments")!.Value == "dismiss").Attribute("content")!.Value ==
                      (language == AppLanguage.English ? "Dismiss" : "Закрыть"), "Localized dismiss button");
                Check(actions.Single(action => action.Attribute("activationType")!.Value == "protocol").Attribute("arguments")!.Value ==
                      "ms-settings:bluetooth?x=1&y=2", "Action URI remains intact");
                Check(xml.Descendants("input").Any() == snooze, "Snooze appears only on low battery notifications");
                if (snooze)
                {
                    var selections = xml.Descendants("selection").ToArray();
                    Check(selections.Select(item => item.Attribute("id")!.Value).SequenceEqual(new[] { "15", "60", "180" }),
                        "Snooze durations retained");
                    Check(selections[1].Attribute("content")!.Value == (language == AppLanguage.English ? "1 hour" : "1 час"),
                        "Localized snooze duration");
                    Check(actions.Single(action => action.Attribute("arguments")!.Value == "snooze").Attribute("content")!.Value ==
                          (language == AppLanguage.English ? "Remind me later" : "Напомнить позже"), "Localized snooze action");
                }
            }
        }

        AppText.Apply(AppLanguage.Russian);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var capturedOperation = Task.Run(async () =>
        {
            await resume.Task;
            return AppText.Get("Connected");
        });
        AppText.Apply(AppLanguage.English);
        resume.SetResult();
        Check(await capturedOperation == "Connected", "Pending operations honor a language change despite captured culture");
        AppText.Apply(AppLanguage.Auto);
        Console.WriteLine("Localization tests passed.");
    }

    private static Dictionary<string, string> Entries(ResourceSet set) => set.Cast<DictionaryEntry>()
        .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);

    private static int[] Arguments(string value) => Regex.Matches(value, @"\{(\d+)(?:[^}]*)\}")
        .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture)).Order().ToArray();

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("Failed: " + message);
    }
}
