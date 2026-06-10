using System.Globalization;
using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

public enum ChartRange
{
    Hour1,   // last 1 hour  → 12 × 5 min
    Hours5,  // last 5 hours → 10 × 30 min
    Day1,    // last 24 hours → 24 × 1 h
    Week1,   // last 7 days  → 7 × 1 day
    Month1   // last 30 days → 30 × 1 day
}

public sealed record HistoryBucket(
    DateTime StartLocal, string Label,
    double Opus, double Sonnet, double Haiku, double Other)
{
    public double CostUsd => Opus + Sonnet + Haiku + Other;
}

/// <summary>
/// Buckets local transcript usage (cost-equivalent $) into a rolling window ending NOW,
/// subdivided into bars. Each range shows the *last* hour / 5h / 24h / week / month.
/// Source is the JSONL parser — the only thing with history (the API is a snapshot).
/// </summary>
public static class UsageHistory
{
    private static (TimeSpan sub, int count) Spec(ChartRange r) => r switch
    {
        ChartRange.Hour1 => (TimeSpan.FromMinutes(5), 12),
        ChartRange.Hours5 => (TimeSpan.FromMinutes(30), 10),
        ChartRange.Day1 => (TimeSpan.FromHours(1), 24),
        ChartRange.Week1 => (TimeSpan.FromDays(1), 7),
        ChartRange.Month1 => (TimeSpan.FromDays(1), 30),
        _ => (TimeSpan.FromMinutes(30), 10)
    };

    /// <summary>How far back the parser must read to fill the window (+ a margin).</summary>
    public static TimeSpan Lookback(ChartRange r)
    {
        var (sub, count) = Spec(r);
        return sub * count + sub;
    }

    /// <summary>
    /// <paramref name="culture"/>: cultura de FORMATO de las etiquetas del eje X ("ddd"/"dd/MM"), la del
    /// idioma elegido en la UI (T2: con Language=en salían "lun"/"mié" de la CurrentCulture del SO).
    /// </summary>
    public static List<HistoryBucket> Build(IEnumerable<UsageRecord> records, ChartRange range, DateTime nowUtc,
        CultureInfo culture)
    {
        var (sub, count) = Spec(range);
        var opus = new double[count];
        var sonnet = new double[count];
        var haiku = new double[count];
        var other = new double[count];

        foreach (var rec in records)
        {
            var age = nowUtc - rec.TimestampUtc;
            if (age < TimeSpan.Zero) continue;
            int stepsAgo = (int)(age.Ticks / sub.Ticks); // 0 = current (newest) bucket
            int idx = count - 1 - stepsAgo;
            if (idx < 0 || idx >= count) continue;

            double cost = Pricing.CostUsd(rec);
            switch (UsageAggregator.ModelLabel(rec.Model))
            {
                case "Opus": opus[idx] += cost; break;
                case "Sonnet": sonnet[idx] += cost; break;
                case "Haiku": haiku[idx] += cost; break;
                default: other[idx] += cost; break;
            }
        }

        var list = new List<HistoryBucket>(count);
        for (int i = 0; i < count; i++)
        {
            var startLocal = (nowUtc - sub * (count - i)).ToLocalTime();
            list.Add(new HistoryBucket(startLocal, Label(range, startLocal, culture), opus[i], sonnet[i], haiku[i], other[i]));
        }
        return list;
    }

    private static string Label(ChartRange r, DateTime start, CultureInfo ci) => r switch
    {
        ChartRange.Hour1 => start.ToString("HH:mm", ci),
        ChartRange.Hours5 => start.ToString("HH:mm", ci),
        ChartRange.Day1 => start.ToString("HH'h'", ci),
        ChartRange.Week1 => start.ToString("ddd", ci),
        ChartRange.Month1 => start.ToString("dd/MM", ci),
        _ => start.ToString("dd/MM", ci)
    };
}
