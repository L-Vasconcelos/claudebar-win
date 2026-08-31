using ClaudeBarWin.Models;

namespace ClaudeBarWin.Services;

/// <summary>
/// Snapshot of aggregated usage stats computed from local transcript records.
/// All fields are pre-computed and ready to display.
/// </summary>
public sealed record UsageStats(
    int Sessions,
    int Messages,
    long TotalTokens,
    int ActiveDays,
    int CurrentStreak,
    int LongestStreak,
    int PeakHour,
    string FavoriteModel,
    /// <summary>Per-day token counts for the heatmap (last 30 days, index 0 = oldest).</summary>
    IReadOnlyList<(DateTime Date, long Tokens)> DailyActivity);

/// <summary>
/// Computes the 8 overview indicators + daily heatmap data from transcript records.
/// Pure computation, no I/O — receives records already parsed by <see cref="TranscriptParser"/>.
/// </summary>
public static class UsageStatsService
{
    /// <summary>
    /// Computes stats for a given time window. Pass <c>null</c> sinceUtc for "all time".
    /// </summary>
    public static UsageStats Compute(IReadOnlyList<UsageRecord> allRecords, DateTime? sinceUtc = null)
    {
        var records = sinceUtc is { } since
            ? allRecords.Where(r => r.TimestampUtc >= since).ToList()
            : allRecords.ToList();

        if (records.Count == 0)
            return new UsageStats(0, 0, 0, 0, 0, 0, 0, "—", Array.Empty<(DateTime, long)>());

        // Messages = number of records (each is an assistant turn)
        int messages = records.Count;

        // Total tokens
        long totalTokens = records.Sum(r => r.TotalTokens);

        // Sessions: group by (date, project folder implied by gaps > 30min)
        // Simplified: count distinct 30-min windows of activity
        var sorted = records.OrderBy(r => r.TimestampUtc).ToList();
        int sessions = 1;
        for (int i = 1; i < sorted.Count; i++)
        {
            if ((sorted[i].TimestampUtc - sorted[i - 1].TimestampUtc).TotalMinutes > 30)
                sessions++;
        }

        // Active days: distinct dates (local time)
        var activeDates = new HashSet<DateTime>(
            records.Select(r => r.TimestampUtc.ToLocalTime().Date));
        int activeDays = activeDates.Count;

        // Streaks (current + longest): consecutive calendar days with activity
        var sortedDates = activeDates.OrderBy(d => d).ToList();
        int currentStreak = 0;
        int longestStreak = 0;
        if (sortedDates.Count > 0)
        {
            int streak = 1;
            longestStreak = 1;
            for (int i = 1; i < sortedDates.Count; i++)
            {
                if ((sortedDates[i] - sortedDates[i - 1]).Days == 1)
                {
                    streak++;
                    longestStreak = Math.Max(longestStreak, streak);
                }
                else
                {
                    streak = 1;
                }
            }
            // Current streak: count back from today
            var today = DateTime.Now.Date;
            var check = today;
            currentStreak = 0;
            while (activeDates.Contains(check))
            {
                currentStreak++;
                check = check.AddDays(-1);
            }
            // If no activity today, check if yesterday was the last active day
            if (currentStreak == 0 && activeDates.Contains(today.AddDays(-1)))
            {
                check = today.AddDays(-1);
                while (activeDates.Contains(check))
                {
                    currentStreak++;
                    check = check.AddDays(-1);
                }
            }
        }

        // Peak hour: hour of day (local) with most messages
        var hourCounts = new int[24];
        foreach (var r in records)
            hourCounts[r.TimestampUtc.ToLocalTime().Hour]++;
        int peakHour = Array.IndexOf(hourCounts, hourCounts.Max());

        // Favorite model (by message count, using family name)
        string favoriteModel = records
            .GroupBy(r => ModelFamily.FromId(r.Model))
            .Where(g => g.Key != ModelFamily.Other)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .FirstOrDefault() ?? "—";

        // Daily activity heatmap (last 30 days)
        var today2 = DateTime.Now.Date;
        var heatmap = new List<(DateTime Date, long Tokens)>(30);
        for (int i = 29; i >= 0; i--)
        {
            var day = today2.AddDays(-i);
            long dayTokens = records
                .Where(r => r.TimestampUtc.ToLocalTime().Date == day)
                .Sum(r => r.TotalTokens);
            heatmap.Add((day, dayTokens));
        }

        return new UsageStats(
            sessions, messages, totalTokens, activeDays,
            currentStreak, longestStreak, peakHour, favoriteModel,
            heatmap);
    }

    /// <summary>Formats a token count for display: "1.8B", "16M", "420k", "89".</summary>
    public static string FormatTokens(long tokens)
    {
        if (tokens >= 1_000_000_000) return $"{tokens / 1_000_000_000.0:0.#}B";
        if (tokens >= 1_000_000) return $"{tokens / 1_000_000.0:0.#}M";
        if (tokens >= 1_000) return $"{tokens / 1_000.0:0.#}k";
        return tokens.ToString();
    }

    /// <summary>Formats a message count: "26.4k", "1.2k", "89".</summary>
    public static string FormatCount(int count)
    {
        if (count >= 1_000_000) return $"{count / 1_000_000.0:0.#}M";
        if (count >= 100_000) return $"{count / 1_000.0:0}k";
        if (count >= 1_000) return $"{count / 1_000.0:0.#}k";
        return count.ToString();
    }

    /// <summary>
    /// Fun fact comparing token usage to well-known texts. Returns null if tokens are too low.
    /// </summary>
    public static string? FunFact(long totalTokens, Strings s)
    {
        // Approximate token counts for well-known texts
        (string name, long tokens)[] refs =
        {
            ("Harry Potter (saga)", 1_100_000),
            ("The Lord of the Rings", 576_000),
            ("War and Peace", 580_000),
            ("The Bible", 783_000),
            ("Wikipedia (EN)", 4_400_000_000),
        };

        if (totalTokens < 10_000) return null;

        foreach (var (name, refTokens) in refs.OrderBy(r => r.tokens))
        {
            double ratio = (double)totalTokens / refTokens;
            if (ratio >= 1.5)
                return string.Format(s.FunFactFormat, $"~{ratio:0}×", name);
        }

        return null;
    }
}
