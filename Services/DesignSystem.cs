namespace ClaudeBarWin.Services;

/// <summary>Espaciado en rejilla de 8pt (múltiplos de 4). Sustituye los offsets ad-hoc.</summary>
public static class Spacing
{
    public const int Xs = 4;
    public const int Sm = 8;
    public const int Md = 12;
    public const int Lg = 16;
    public const int Xl = 24;
    public const int Xxl = 32;
}

/// <summary>Helpers de color: interpolación lineal y color de cuota por riesgo.</summary>
public static class ColorMath
{
    /// <summary>Interpola por canal ARGB. t se recorta a [0,1].</summary>
    public static Color Lerp(Color a, Color b, double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);
        int L(int x, int y) => (int)Math.Round(x + (y - x) * t);
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }

    /// <summary>
    /// Color de cuota interpolado de forma continua: Ok→Warn hasta el umbral 'warn',
    /// Warn→Critical hasta 'crit', y Critical a partir de ahí. pct se recorta a [0,100].
    /// </summary>
    public static Color RiskColor(double pct, Theme t, double warn, double crit)
    {
        pct = Math.Clamp(pct, 0.0, 100.0);
        if (warn <= 0) warn = 70;
        if (crit <= warn) crit = Math.Max(warn + 1, 90);
        if (pct >= crit) return t.Critical;
        if (pct <= warn) return Lerp(t.Ok, t.Warn, pct / warn);
        return Lerp(t.Warn, t.Critical, (pct - warn) / (crit - warn));
    }
}

/// <summary>
/// Fuentes del sistema de diseño: una familia (Segoe UI Variable) en 4 pasos + mono para números.
/// Cacheadas estáticamente (viven toda la app); con fallback si la familia no está instalada.
/// </summary>
public static class Typography
{
    public static readonly Font Hero    = Ui("Segoe UI Variable Display", 28f, FontStyle.Bold);
    public static readonly Font Title   = Ui("Segoe UI Variable Text", 15f, FontStyle.Bold);
    public static readonly Font Body    = Ui("Segoe UI Variable Text", 12f, FontStyle.Regular);
    public static readonly Font Caption = Ui("Segoe UI Variable Text", 11f, FontStyle.Regular);
    public static readonly Font Mono    = MonoFont(12f);

    // Crea la fuente pedida; si el sistema sustituye por otra familia (no instalada), cae a "Segoe UI".
    private static Font Ui(string family, float size, FontStyle style)
    {
        try
        {
            var f = new Font(family, size, style);
            if (f.Name.StartsWith("Segoe UI", StringComparison.OrdinalIgnoreCase)) return f;
            f.Dispose();
        }
        catch { }
        return new Font("Segoe UI", size, style);
    }

    private static Font MonoFont(float size)
    {
        foreach (var family in new[] { "Cascadia Mono", "Consolas" })
        {
            try
            {
                var f = new Font(family, size, FontStyle.Regular, GraphicsUnit.Point);
                if (f.Name.Equals(family, StringComparison.OrdinalIgnoreCase)) return f;
                f.Dispose();
            }
            catch { }
        }
        return new Font("Consolas", size, FontStyle.Regular, GraphicsUnit.Point);
    }
}
