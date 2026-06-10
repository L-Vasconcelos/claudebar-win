using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeBarWin.Services;

public sealed record UsageWindow(double UtilizationPct, DateTimeOffset? ResetsAt);

public sealed class RealUsage
{
    public UsageWindow? FiveHour { get; init; }
    public UsageWindow? SevenDay { get; init; }
    public UsageWindow? SevenDayOpus { get; init; }
    public UsageWindow? SevenDaySonnet { get; init; }
    public bool ExtraUsageEnabled { get; init; }

    public double MaxUtilization =>
        Math.Max(FiveHour?.UtilizationPct ?? 0, SevenDay?.UtilizationPct ?? 0);
}

public enum UsageFetchState
{
    Ok,
    NoCredentials,
    AuthExpired,
    RateLimited,
    NetworkError
}

public sealed class UsageResult
{
    public UsageFetchState State { get; init; }
    public RealUsage? Usage { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Reads real Claude Code quota from Anthropic's OAuth usage endpoint using the
/// locally stored OAuth token. Mirrors the approach of CodeZeno/Claude-Code-Usage-Monitor.
/// The token is used only to read the user's own usage; it is never stored or transmitted
/// anywhere else.
/// </summary>
public sealed class UsageApiClient
{
    private const string UsageUrl = "https://api.anthropic.com/api/oauth/usage";
    private const string Beta = "oauth-2025-04-20";

    // OAuth token refresh (Claude Code's official public client_id).
    private const string TokenUrl = "https://platform.claude.com/v1/oauth/token";
    private const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";
    private const string Scopes = "user:profile user:inference user:sessions:claude_code";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private DateTime _cooldownUntilUtc = DateTime.MinValue;

    public async Task<UsageResult> FetchAsync()
    {
        // Back off while rate-limited: serve last-good data without hitting the API.
        if (DateTime.UtcNow < _cooldownUntilUtc)
            return new UsageResult { State = UsageFetchState.RateLimited };

        var creds = ReadCreds();
        if (creds is null)
            return new UsageResult { State = UsageFetchState.NoCredentials };

        // If the cached token is expired, refresh it (OAuth endpoint first, CLI as fallback), then re-read.
        if (IsExpired(creds.Value.ExpiresAt))
        {
            if (!await TryOAuthRefreshAsync().ConfigureAwait(false))
                TryCliRefresh();
            creds = ReadCreds() ?? creds;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, UsageUrl);
            req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {creds.Value.AccessToken}");
            req.Headers.TryAddWithoutValidation("anthropic-beta", Beta);

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (resp.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return new UsageResult { State = UsageFetchState.AuthExpired };

            if ((int)resp.StatusCode == 429)
            {
                int retrySec = 300; // default backoff when no Retry-After (matches ccstatusline)
                if (resp.Headers.RetryAfter?.Delta is { } d)
                    retrySec = Math.Max(retrySec, (int)d.TotalSeconds);
                else if (resp.Headers.TryGetValues("retry-after", out var vals)
                         && int.TryParse(vals.FirstOrDefault(), out var ra))
                    retrySec = Math.Max(retrySec, ra);
                _cooldownUntilUtc = DateTime.UtcNow.AddSeconds(retrySec);
                return new UsageResult { State = UsageFetchState.RateLimited };
            }

            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            return new UsageResult { State = UsageFetchState.Ok, Usage = Parse(json) };
        }
        catch (Exception ex)
        {
            return new UsageResult { State = UsageFetchState.NetworkError, Error = ex.Message };
        }
    }

    private readonly record struct Creds(string AccessToken, long? ExpiresAt);

    private static string CredPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", ".credentials.json");

    private static Creds? ReadCreds()
    {
        try
        {
            var path = CredPath();
            if (!File.Exists(path)) return null;

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out var o)) return null;
            if (!o.TryGetProperty("accessToken", out var t)) return null;

            var token = t.GetString();
            if (string.IsNullOrEmpty(token)) return null;

            long? exp = o.TryGetProperty("expiresAt", out var e) && e.ValueKind == JsonValueKind.Number
                ? e.GetInt64()
                : null;
            return new Creds(token, exp);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsExpired(long? expiresAtMs)
    {
        if (expiresAtMs is null) return false;
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return nowMs >= expiresAtMs.Value - 60_000; // 60s clock skew
    }

    /// <summary>
    /// Refreshes the token directly via the OAuth endpoint and writes the new tokens
    /// back to ~/.claude/.credentials.json (preserving the rest of the file). Only runs
    /// when the stored token is already expired, so it coexists with Claude Code.
    /// The write goes through <see cref="CredentialsWriter.WriteAtomic"/>: tmp+Replace
    /// atómico y sin pisar un refresh concurrente más nuevo (P0 auditoría 2026-06-10).
    /// </summary>
    private static async Task<bool> TryOAuthRefreshAsync()
    {
        try
        {
            var path = CredPath();
            if (!File.Exists(path)) return false;

            var root = JsonNode.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false))?.AsObject();
            var oauth = root?["claudeAiOauth"]?.AsObject();
            var refreshToken = oauth?["refreshToken"]?.GetValue<string>();
            if (string.IsNullOrEmpty(refreshToken)) return false;

            var payload = new
            {
                grant_type = "refresh_token",
                refresh_token = refreshToken,
                client_id = ClientId,
                scope = Scopes
            };
            using var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = await Http.PostAsync(TokenUrl, content).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return false;

            var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;

            string? accessToken = r.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            if (string.IsNullOrEmpty(accessToken)) return false;

            string newRefresh = r.TryGetProperty("refresh_token", out var nr) && nr.GetString() is { } s ? s : refreshToken;
            long expiresIn = r.TryGetProperty("expires_in", out var ei) && ei.ValueKind == JsonValueKind.Number
                ? ei.GetInt64() : 3600;

            oauth!["accessToken"] = accessToken;
            oauth["refreshToken"] = newRefresh;
            oauth["expiresAt"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + expiresIn * 1000;

            var write = CredentialsWriter.WriteAtomic(
                path, root!.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            // SkippedNewerOnDisk también es éxito: otro proceso ya dejó un token más fresco
            // en disco y FetchAsync re-lee el archivo justo después de este método.
            return write is CredentialsWriteResult.Written or CredentialsWriteResult.SkippedNewerOnDisk;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Runs `claude -p .` headless to force Claude Code's internal OAuth refresh,
    /// which rewrites ~/.claude/.credentials.json. Best-effort, capped at 30s.
    /// </summary>
    private static void TryCliRefresh()
    {
        try
        {
            var claude = ResolveClaudePath();
            if (claude is null) return;

            bool isCmd = claude.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase);
            var psi = new ProcessStartInfo
            {
                FileName = isCmd ? "cmd.exe" : claude,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
            if (isCmd)
            {
                psi.ArgumentList.Add("/c");
                psi.ArgumentList.Add(claude);
            }
            psi.ArgumentList.Add("-p");
            psi.ArgumentList.Add(".");
            psi.Environment.Remove("CLAUDECODE");
            psi.Environment.Remove("CLAUDE_CODE_ENTRYPOINT");

            using var proc = Process.Start(psi);
            if (proc is null) return;
            if (!proc.WaitForExit(30_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
            }
        }
        catch
        {
            // best-effort
        }
    }

    private static string? ResolveClaudePath()
    {
        var local = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "bin", "claude.exe");
        if (File.Exists(local)) return local;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            try
            {
                foreach (var name in new[] { "claude.exe", "claude.cmd" })
                {
                    var p = Path.Combine(dir.Trim(), name);
                    if (File.Exists(p)) return p;
                }
            }
            catch
            {
                // skip malformed PATH entries
            }
        }
        return null;
    }

    private static RealUsage Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new RealUsage
        {
            FiveHour = ParseWindow(root, "five_hour"),
            SevenDay = ParseWindow(root, "seven_day"),
            SevenDayOpus = ParseWindow(root, "seven_day_opus"),
            SevenDaySonnet = ParseWindow(root, "seven_day_sonnet"),
            ExtraUsageEnabled = root.TryGetProperty("extra_usage", out var eu)
                && eu.ValueKind == JsonValueKind.Object
                && eu.TryGetProperty("is_enabled", out var en)
                && en.ValueKind == JsonValueKind.True
        };
    }

    private static UsageWindow? ParseWindow(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind != JsonValueKind.Object)
            return null;

        double util = el.TryGetProperty("utilization", out var u) && u.ValueKind == JsonValueKind.Number
            ? u.GetDouble()
            : 0;

        DateTimeOffset? reset = null;
        if (el.TryGetProperty("resets_at", out var r) && r.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(r.GetString(), out var dto))
            reset = dto;

        return new UsageWindow(util, reset);
    }
}

/// <summary>Combined snapshot consumed by the UI: live quota + optional local spend estimate.</summary>
public sealed class AppSnapshot
{
    public RealUsage? Usage { get; init; }          // last good live data
    public UsageFetchState LatestState { get; init; }
    public DateTime UsageAtUtc { get; init; }
    public WindowStats? Spend { get; init; }        // local ccusage-style estimate (optional)
    public int SpendDays { get; init; }
    public HealthStatus? Health { get; init; }      // Anthropic service status (optional)
    public PaceResult? PaceFive { get; init; }      // pace for the 5h window
    public PaceResult? PaceSeven { get; init; }     // pace for the 7d window
}
