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

    /// <summary>
    /// Luminancia relativa a la que negro y blanco contrastan IGUAL sobre el fondo:
    /// <c>(1.05)/(L+0.05) = (L+0.05)/0.05 ⇒ L = √(1.05·0.05) − 0.05 ≈ 0.1791</c>.
    /// Por encima gana el negro; por debajo, el blanco.
    /// </summary>
    private const double TextFlipLuminance = 0.1791287;

    /// <summary>
    /// Color de texto legible sobre <paramref name="bg"/>: el lado (negro/blanco) que más contrasta
    /// según la luminancia relativa WCAG (<see cref="RelativeLuminance"/>, T6a). La heurística antigua
    /// (luma 0.299/0.587/0.114, umbral 140) elegía BLANCO sobre el verde CLI #00D959 (1.9:1, ilegible)
    /// y sobre los rellenos Warn/Ok oscuros; con el punto de cruce WCAG (~0.179) el lado elegido nunca
    /// contrasta peor que el descartado (negro 11.1:1 sobre el verde CLI).
    /// </summary>
    public static Color Contrast(Color bg)
        => RelativeLuminance(bg) > TextFlipLuminance ? Color.Black : Color.White;

    /// <summary>
    /// Ratio de contraste WCAG 2.x entre dos colores: <c>(L1 + 0.05) / (L2 + 0.05)</c> con el más
    /// claro arriba (rango 1..21). AA pide ≥ 4.5:1 para texto pequeño. Simétrico. Lo usa el test del
    /// tema claro para garantizar que el gris tenue y el verde de éxito son legibles sobre el fondo.
    /// </summary>
    public static double ContrastRatio(Color a, Color b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    /// <summary>Luminancia relativa WCAG (sRGB linearizado). 0 = negro, 1 = blanco.</summary>
    public static double RelativeLuminance(Color c)
    {
        static double Chan(int v)
        {
            double s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Chan(c.R) + 0.7152 * Chan(c.G) + 0.0722 * Chan(c.B);
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
