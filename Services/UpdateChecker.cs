using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ClaudeBarWin.Services;

public sealed record UpdateInfo(
    string CurrentVersion,
    string LatestTag,
    bool IsNewer,
    string Changelog,
    string? AssetUrl,
    string HtmlUrl);

/// <summary>
/// Checks GitHub Releases for a newer version and downloads the new exe on request.
/// Public API, no auth. The actual replace-the-exe step is left to the user.
/// </summary>
public static class UpdateChecker
{
    private const string LatestApi = "https://api.github.com/repos/Yovancas/claudebar-win/releases/latest";
    public const string RepoUrl = "https://github.com/Yovancas/claudebar-win";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("ClaudeBarWin");
        c.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return c;
    }

    public static string CurrentVersion
    {
        get
        {
            var v = Assembly.GetExecutingAssembly().GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(LatestApi).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string body = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? RepoUrl : RepoUrl;

            string? assetUrl = null;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        assetUrl = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                        break;
                    }
                }

            bool isNewer = false;
            if (Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
                && Version.TryParse(CurrentVersion, out var cur))
                isNewer = latest > cur;

            return new UpdateInfo(CurrentVersion, tag, isNewer, body.Trim(), assetUrl, htmlUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Downloads the new exe to the user's Downloads folder; returns the path.</summary>
    public static async Task<string?> DownloadAsync(string assetUrl, string tag)
    {
        try
        {
            var downloads = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            Directory.CreateDirectory(downloads);
            var safeTag = string.Concat(tag.Split(Path.GetInvalidFileNameChars()));
            var dest = Path.Combine(downloads, $"ClaudeBarWin-{safeTag}.exe");

            using var resp = await Http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            await using var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None);
            await resp.Content.CopyToAsync(fs).ConfigureAwait(false);
            return dest;
        }
        catch
        {
            return null;
        }
    }
}
