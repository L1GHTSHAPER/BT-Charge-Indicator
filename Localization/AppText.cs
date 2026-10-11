using System.Globalization;
using System.Resources;
using BTChargeIndicator.Settings;

namespace BTChargeIndicator.Localization;

internal static class AppText
{
    private static readonly CultureInfo WindowsCulture = CultureInfo.CurrentUICulture;
    private static readonly ResourceManager Resources = new(
        "BTChargeIndicator.Localization.Strings", typeof(AppText).Assembly);

    public static CultureInfo Culture { get; private set; } = ResolveCulture(AppLanguage.Auto, WindowsCulture);

    public static CultureInfo ResolveCulture(AppLanguage language, CultureInfo windowsCulture) =>
        CultureInfo.GetCultureInfo(language switch
        {
            AppLanguage.Russian => "ru",
            AppLanguage.English => "en",
            _ => windowsCulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en"
        });

    public static void Apply(AppLanguage language)
    {
        Culture = ResolveCulture(language, WindowsCulture);
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
    }

    // Look up the selected culture explicitly, including after awaited operations
    // that may have captured the UI culture before the user changed languages.
    public static string Get(string key) => Resources.GetString(key, Culture)
        ?? throw new MissingManifestResourceException($"Missing UI text: {key}");

    public static string Format(string key, params object?[] args) => string.Format(Culture, Get(key), args);
}
