using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Estimated USD-equivalent pricing per 1M tokens (public Anthropic API list prices).
/// Used to normalise usage across models/cache into a single comparable number.
/// This is a *cost-equivalent estimate*, not what a subscription actually charges.
/// </summary>
public static class Pricing
{
    private readonly record struct Rate(
        double Input, double Output, double CacheWrite5m, double CacheWrite1h, double CacheRead);

    // USD per 1,000,000 tokens
    private static readonly Rate Opus = new(15.0, 75.0, 18.75, 30.0, 1.50);
    private static readonly Rate Sonnet = new(3.0, 15.0, 3.75, 6.0, 0.30);
    private static readonly Rate Haiku = new(1.0, 5.0, 1.25, 2.0, 0.10);
    private static readonly Rate Zero = new(0, 0, 0, 0, 0);

    public static double CostUsd(UsageRecord r)
    {
        var rate = RateFor(r.Model);
        return r.InputTokens / 1_000_000.0 * rate.Input
             + r.OutputTokens / 1_000_000.0 * rate.Output
             + r.CacheCreate5mTokens / 1_000_000.0 * rate.CacheWrite5m
             + r.CacheCreate1hTokens / 1_000_000.0 * rate.CacheWrite1h
             + r.CacheReadTokens / 1_000_000.0 * rate.CacheRead;
    }

    private static Rate RateFor(string model)
    {
        if (string.IsNullOrEmpty(model)) return Zero;
        var m = model.ToLowerInvariant();
        if (m.Contains("opus")) return Opus;
        if (m.Contains("sonnet")) return Sonnet;
        if (m.Contains("haiku")) return Haiku;
        return Zero; // <synthetic> / unknown models cost nothing
    }
}
