using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace SilenceCutter.Services;

public sealed record UpdateInfo(string Version, string Url);

public sealed class UpdateChecker
{
    private static readonly HttpClient Http = new();
    private static readonly Regex VersionRegex = new(@"\d+(\.\d+){0,3}", RegexOptions.Compiled);

    private readonly string _owner;
    private readonly string _repo;

    public UpdateChecker(string owner, string repo)
    {
        _owner = owner;
        _repo = repo;
    }

    public Version CurrentVersion { get; } = GetCurrentVersion();

    public async Task<UpdateInfo?> CheckLatestReleaseAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{_owner}/{_repo}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("SilenceCutter", CurrentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        using var response = await Http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            Debug.WriteLine($"Update check failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        var release = await response.Content.ReadFromJsonAsync<GitHubRelease>(cancellationToken: ct);
        if (string.IsNullOrWhiteSpace(release?.TagName) || string.IsNullOrWhiteSpace(release.HtmlUrl))
            return null;

        var latestVersion = ParseVersion(release.TagName);
        if (latestVersion is null || latestVersion <= CurrentVersion)
            return null;

        return new UpdateInfo(release.TagName, release.HtmlUrl);
    }

    private static Version GetCurrentVersion()
    {
        var environmentVersion = Environment.GetEnvironmentVariable("SILENCECUTTER_VERSION");
        if (ParseVersion(environmentVersion) is { } versionFromEnvironment)
            return versionFromEnvironment;

        var assembly = Assembly.GetExecutingAssembly();
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        return ParseVersion(informationalVersion) ?? assembly.GetName().Version ?? new Version(0, 0, 0);
    }

    private static Version? ParseVersion(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var match = VersionRegex.Match(value);
        if (!match.Success)
            return null;

        return Version.TryParse(match.Value, out var version) ? version : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; set; }
    }
}
