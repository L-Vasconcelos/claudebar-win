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

        // T13a: cap de presentación — con más de ModelFamily.MaxFamilies familias en la ventana,
        // las menores se funden en "Otros" para que el panel/leyenda no crezcan sin límite.
        CapFamilies(snap.Session);
        CapFamilies(snap.Week);

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

        // T13a: familia DINÁMICA derivada del id (claude-fable-5 → "Fable"), no lista fija — antes
        // ModelLabel hacía Contains(opus/sonnet/haiku) y cualquier modelo nuevo caía en "other".
        var key = ModelFamily.FromId(r.Model);
        w.CostByModel[key] = w.CostByModel.GetValueOrDefault(key) + cost;
        w.TokensByModel[key] = w.TokensByModel.GetValueOrDefault(key) + r.TotalTokens;
    }

    /// <summary>
    /// Funde en <see cref="ModelFamily.Other"/> las familias que no entran en el cap (las de menor
    /// gasto), manteniendo coherentes gasto y tokens — no se pierde nada, solo se agrupa.
    /// </summary>
    private static void CapFamilies(WindowStats w)
    {
        var keep = ModelFamily.Keep(w.CostByModel);
        if (keep.Count == w.CostByModel.Count) return;

        foreach (var key in w.CostByModel.Keys.Where(k => k != ModelFamily.Other && !keep.Contains(k)).ToList())
        {
            w.CostByModel[ModelFamily.Other] =
                w.CostByModel.GetValueOrDefault(ModelFamily.Other) + w.CostByModel[key];
            w.TokensByModel[ModelFamily.Other] =
                w.TokensByModel.GetValueOrDefault(ModelFamily.Other) + w.TokensByModel.GetValueOrDefault(key);
            w.CostByModel.Remove(key);
            w.TokensByModel.Remove(key);
        }
    }
}
