using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

public sealed class WindowStats
{
    public long Input { get; set; }
    public long Output { get; set; }
    public long CacheWrite { get; set; }
    public long CacheRead { get; set; }
    public double CostUsd { get; set; }
    public int Messages { get; set; }
    public Dictionary<string, double> CostByModel { get; } = new();
    public Dictionary<string, long> TokensByModel { get; } = new();

    public long TotalTokens => Input + Output + CacheWrite + CacheRead;
}

public sealed class UsageSnapshot
{
    public WindowStats Session { get; } = new();   // rolling session window (default 5h)
    public WindowStats Week { get; } = new();        // rolling weekly window (default 7d)
    public DateTime GeneratedAtUtc { get; init; } = DateTime.UtcNow;
}

public static class UsageAggregator
{
    public static UsageSnapshot Build(
        IEnumerable<UsageRecord> records,
        DateTime nowUtc,
        TimeSpan sessionWindow,
        TimeSpan weekWindow)
    {
        var snap = new UsageSnapshot { GeneratedAtUtc = nowUtc };
        var sessFrom = nowUtc - sessionWindow;
        var weekFrom = nowUtc - weekWindow;

        foreach (var r in records)
        {
            if (r.TimestampUtc >= weekFrom) Add(snap.Week, r);
            if (r.TimestampUtc >= sessFrom) Add(snap.Session, r);
        }

        return snap;
    }

    private static void Add(WindowStats w, UsageRecord r)
    {
        w.Input += r.InputTokens;
        w.Output += r.OutputTokens;
        w.CacheWrite += r.CacheCreate5mTokens + r.CacheCreate1hTokens;
        w.CacheRead += r.CacheReadTokens;
        w.Messages++;

        var cost = Pricing.CostUsd(r);
        w.CostUsd += cost;

        var key = ModelLabel(r.Model);
        w.CostByModel[key] = w.CostByModel.GetValueOrDefault(key) + cost;
        w.TokensByModel[key] = w.TokensByModel.GetValueOrDefault(key) + r.TotalTokens;
    }

    public static string ModelLabel(string model)
    {
        if (string.IsNullOrEmpty(model)) return "other";
        var m = model.ToLowerInvariant();
        if (m.Contains("opus")) return "Opus";
        if (m.Contains("sonnet")) return "Sonnet";
        if (m.Contains("haiku")) return "Haiku";
        return "other";
    }
}
