namespace ClaudeBarWin.Services;

public static class UsageFormat
{
    /// <summary>"2h 13m", "1d 4h", "45m 12s", or the localized "resetting…" label.</summary>
    public static string Countdown(DateTimeOffset? resetsAt, string resettingLabel)
    {
        if (resetsAt is null) return "";
        var span = resetsAt.Value - DateTimeOffset.Now;
        if (span <= TimeSpan.Zero) return resettingLabel;
        if (span.TotalDays >= 1) return $"{(int)span.TotalDays}d {span.Hours}h";
        if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes}m";
        return $"{span.Minutes}m {span.Seconds}s";
    }

    /// <summary>Hora local absoluta del reset en formato "ddd HH:mm" (p.ej. "mar 18:42"); "" si es null.</summary>
    public static string ResetAbsolute(DateTimeOffset? resetsAt)
        => resetsAt is { } r ? r.ToLocalTime().ToString("ddd HH:mm") : "";

    public static string StateMessage(UsageFetchState state, Strings s) => state switch
    {
        UsageFetchState.NoCredentials => s.StateNoCredentials,
        UsageFetchState.AuthExpired => s.StateAuthExpired,
        UsageFetchState.RateLimited => s.StateRateLimited,
        UsageFetchState.NetworkError => s.StateNetworkError,
        _ => ""
    };
}
