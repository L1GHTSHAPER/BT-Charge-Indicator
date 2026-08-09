using System.Net.Http.Headers;
using System.Text.Json;

namespace BTChargeIndicator.Services;

internal sealed class UpdateService : IDisposable
{
    private const string LatestReleaseApiUrl =
        "https://api.github.com/repos/L1GHTSHAPER/BT-Charge-Indicator/releases/latest";

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(10)
    };

    public UpdateService()
    {
        _httpClient.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("BTChargeIndicator", Application.ProductVersion));
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo?> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(LatestReleaseApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tag = root.GetProperty("tag_name").GetString();
        var url = root.GetProperty("html_url").GetString();

        if (string.IsNullOrWhiteSpace(tag) ||
            string.IsNullOrWhiteSpace(url) ||
            !TryParseVersion(tag, out var version))
        {
            return null;
        }

        return new ReleaseInfo(tag, version, url);
    }

    public static bool IsNewerThanCurrent(ReleaseInfo release)
    {
        return TryParseVersion(Application.ProductVersion, out var current) &&
               release.Version > current;
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var normalized = value.Trim().TrimStart('v', 'V');
        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        return Version.TryParse(normalized, out version!);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}

internal sealed record ReleaseInfo(
    string Tag,
    Version Version,
    string Url);
