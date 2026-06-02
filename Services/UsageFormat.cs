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

    /// <summary>Antigüedad de un dato UTC en texto relativo localizado ("hace 5 min" / "hace 30 s").
    /// Espejo de <see cref="Countdown"/>. Normaliza Kind=Unspecified como UTC.</summary>
    public static string Relative(DateTime utc, Strings s)
    {
        var span = DateTime.UtcNow - AsUtc(utc);
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        string ago =
            span.TotalDays >= 1 ? $"{(int)span.TotalDays} d"
            : span.TotalHours >= 1 ? $"{(int)span.TotalHours} h"
            : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes} min"
            : $"{(int)span.TotalSeconds} s";
        return string.Format(s.AgoFormat, ago);
    }

    /// <summary>El dato se considera "envejecido" cuando supera 3× la frecuencia de refresco.
    /// Normaliza Kind=Unspecified como UTC.</summary>
    public static bool IsStale(DateTime utc, int refreshSeconds)
        => DateTime.UtcNow - AsUtc(utc) > TimeSpan.FromSeconds(3 * refreshSeconds);

    /// <summary>Normaliza un DateTime a UTC: Unspecified se asume ya en UTC; Local se convierte.</summary>
    private static DateTime AsUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc)
    };

    public static string StateMessage(UsageFetchState state, Strings s) => state switch
    {
        UsageFetchState.NoCredentials => s.StateNoCredentials,
        UsageFetchState.AuthExpired => s.StateAuthExpired,
        UsageFetchState.RateLimited => s.StateRateLimited,
        UsageFetchState.NetworkError => s.StateNetworkError,
        _ => ""
    };
}
