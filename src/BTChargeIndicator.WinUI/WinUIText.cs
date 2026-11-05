using System.Globalization;
using System.Resources;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.WinUI;

internal static class WinUIText
{
    private static readonly CultureInfo WindowsCulture = CultureInfo.CurrentUICulture;
    private static readonly ResourceManager Resources = new(
        "BTChargeIndicator.WinUI.Localization.Strings", typeof(WinUIText).Assembly);

    public static CultureInfo Culture { get; private set; } = Resolve(AppLanguage.Auto);

    public static void Apply(AppLanguage language)
    {
        Culture = Resolve(language);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }

    public static string Get(string key) => Resources.GetString(key, Culture)
        ?? throw new MissingManifestResourceException($"Missing WinUI text: {key}");

    public static string Format(string key, params object?[] args) =>
        string.Format(Culture, Get(key), args);

    private static CultureInfo Resolve(AppLanguage language) => CultureInfo.GetCultureInfo(language switch
    {
        AppLanguage.Russian => "ru",
        AppLanguage.English => "en",
        _ => WindowsCulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en"
    });
}
