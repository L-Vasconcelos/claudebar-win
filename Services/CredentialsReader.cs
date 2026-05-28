using System.Text.Json;

namespace ClaudeBarWin.Services;

public sealed record PlanInfo(string SubscriptionType, string RateLimitTier)
{
    public string Display
    {
        get
        {
            if (string.IsNullOrEmpty(SubscriptionType)) return "Plan desconocido";

            string sub = char.ToUpperInvariant(SubscriptionType[0]) + SubscriptionType[1..];
            string tier = RateLimitTier switch
            {
                var t when t.Contains("max_20x") => "Max 20x",
                var t when t.Contains("max_5x") => "Max 5x",
                var t when t.Contains("pro") => "Pro",
                _ => string.Empty
            };

            return string.IsNullOrEmpty(tier) ? sub : $"{sub} · {tier}";
        }
    }
}

/// <summary>
/// Reads ONLY the non-secret plan fields from ~/.claude/.credentials.json.
/// Never reads, stores, logs, or transmits access/refresh tokens.
/// </summary>
public static class CredentialsReader
{
    public static PlanInfo Read()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", ".credentials.json");

            if (!File.Exists(path)) return new PlanInfo("", "");

            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("claudeAiOauth", out var o))
            {
                string sub = o.TryGetProperty("subscriptionType", out var s) ? s.GetString() ?? "" : "";
                string tier = o.TryGetProperty("rateLimitTier", out var t) ? t.GetString() ?? "" : "";
                return new PlanInfo(sub, tier);
            }
        }
        catch
        {
            // ignore — plan label is cosmetic
        }

        return new PlanInfo("", "");
    }
}
