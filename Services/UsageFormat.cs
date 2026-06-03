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
    public static string Relative(DateTime utc, Strings s) => RelativeAt(utc, DateTime.UtcNow, s);

    /// <summary>
    /// Como <see cref="Relative(DateTime, Strings)"/> pero con el instante "ahora" <b>inyectable</b>
    /// (<paramref name="nowUtc"/>) en vez de leer el reloj. Lo usa <see cref="FooterLayout"/> para que
    /// la guardia de re-layout y el pintado del footer compartan el MISMO texto de forma determinista
    /// (testeable sin reloj real).
    /// </summary>
    public static string RelativeAt(DateTime utc, DateTime nowUtc, Strings s)
    {
        var span = AsUtc(nowUtc) - AsUtc(utc);
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
    public static bool IsStale(DateTime utc, int refreshSeconds) => IsStaleAt(utc, DateTime.UtcNow, refreshSeconds);

    /// <summary>
    /// Como <see cref="IsStale"/> pero con el instante "ahora" <b>inyectable</b> (<paramref name="nowUtc"/>):
    /// la guardia de re-layout del footer congela un "ahora" común por tick para que medir y pintar
    /// decidan lo mismo de forma determinista.
    /// </summary>
    public static bool IsStaleAt(DateTime utc, DateTime nowUtc, int refreshSeconds)
        => AsUtc(nowUtc) - AsUtc(utc) > TimeSpan.FromSeconds(3 * refreshSeconds);

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
