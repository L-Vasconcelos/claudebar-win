using ClaudeBarWin.Config;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Barra de cuota unificada (cuerpo + cabecera). Sustituye a las gemelas casi idénticas
/// <c>DashboardDataView.DrawBar</c> y <c>DashboardHeader.DrawCriticalBar</c>: una sola rutina
/// que ambas llaman, para que toda señal nueva (pace marker, ticks de umbral) se escriba una vez.
/// Conserva la simetría medir(draw=false)/pintar(draw=true): avanza y devuelve el mismo <c>y</c>.
/// </summary>
public static class QuotaBar
{
    private const int BarH = 11;
    private const int BarRadius = 5;

    /// <summary>
    /// Dibuja etiqueta + % + barra + línea de reset y devuelve el nuevo y.
    /// El color sigue el criterio de F1: PaceStatus→Ok/Warn/Critical, con fallback a <see cref="ColorMath.RiskColor"/>.
    ///
    /// <para>F3 (tween): <paramref name="displayUtil"/> es el override <b>eased</b> de la utilización para
    /// el <b>ancho de relleno y el número</b>; el <b>color se calcula con la utilización objetivo</b>
    /// (<c>win.UtilizationPct</c>) para que no parpadee de color durante el tween. Si es <c>null</c>
    /// (render-test, reduce-motion, cabecera sin motion) ⇒ comportamiento idéntico a hoy (usa <c>util</c>).</para>
    /// </summary>
    public static int Draw(Graphics g, bool draw, string label, UsageWindow? win, PaceResult? pace, int x, int y, int w,
        AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont, Brush fg, Brush dim,
        double? displayUtil = null)
    {
        double util = win?.UtilizationPct ?? 0;
        // Número y ancho usan el valor eased (si lo hay); el color usa SIEMPRE el objetivo (sin arcoíris).
        double shown = displayUtil ?? util;
        double clamped = Math.Min(shown / 100.0, 1.0);
        // Colour the section by PACE status (better/worse rate); fall back to riesgo gradual.
        Color c = pace is { } ps
            ? (ps.Status == PaceStatus.Critical ? theme.Critical : ps.Status == PaceStatus.Over ? theme.Warn : theme.Ok)
            : ColorMath.RiskColor(util, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);
        // Estado por forma (a11y, daltónicos): mismo mapeo color↔forma que el tray.
        UsageStatus status = pace is { } pst
            ? (pst.Status == PaceStatus.Critical ? UsageStatus.Critical
               : pst.Status == PaceStatus.Over ? UsageStatus.Warn : UsageStatus.Ok)
            : util >= cfg.CriticalThresholdPct ? UsageStatus.Critical
              : util >= cfg.WarnThresholdPct ? UsageStatus.Warn : UsageStatus.Ok;

        if (draw)
        {
            g.DrawString(label, labelFont, fg, x, y);
            // Glifo de forma de 1 carácter + % a la derecha, ambos en el color de estado.
            string glyph = Tray.ShapeGlyph(Tray.ShapeFor(status));
            // El número usa el valor eased (shown); el glifo/estado va por el objetivo (no parpadea).
            // % con la cultura del idioma elegido (T2): "12.5%" en inglés, "12,5%" en español.
            string right = $"{glyph} {UsageFormat.Percent(shown, s.Culture)}";
            var sz = g.MeasureString(right, Typography.Mono);
            using var valBrush = new SolidBrush(c);
            g.DrawString(right, Typography.Mono, valBrush, x + w - sz.Width, y);
        }
        y += 22;

        if (draw)
        {
            using var trackBrush = new SolidBrush(theme.Track);
            Shapes.FillRounded(g, trackBrush, new Rectangle(x, y, w, BarH), BarRadius);
            int fw = (int)Math.Round(w * clamped);
            if (fw > 1)
            {
                using var fillBrush = new SolidBrush(c);
                Shapes.FillRounded(g, fillBrush, new Rectangle(x, y, fw, BarH), BarRadius);
            }

            // Ticks de umbral: muescas finas (1px) en Warn/Critical, tras el relleno para no quedar
            // tapadas por las esquinas redondeadas. Neutro (theme.TickOnTrack — Separator era ≈ Track
            // en los 3 temas y el tick desaparecía, T3b), nunca Accent.
            using (var tickPen = new Pen(theme.TickOnTrack, 1f))
            {
                int wx = QuotaBarGeometry.TickX(x, w, cfg.WarnThresholdPct);
                int cx = QuotaBarGeometry.TickX(x, w, cfg.CriticalThresholdPct);
                g.DrawLine(tickPen, wx, y, wx, y + BarH - 1);
                g.DrawLine(tickPen, cx, y, cx, y + BarH - 1);
            }

            // Pace marker: "dónde deberías ir" según el ritmo ideal. Sobresale MarkerOvershoot (2px)
            // arriba/abajo y lleva un ▾ clampado a esa misma altura: el triángulo antiguo subía hasta
            // y-5 e invadía los descendentes de la fila etiqueta/% (T3c). Color theme.TextMuted
            // (neutro). Solo cuando hay pace.
            if (pace is { } pm)
            {
                int mx = QuotaBarGeometry.MarkerX(x, w, pm.IdealPct);
                using var markerPen = new Pen(theme.TextMuted, 2f);
                g.DrawLine(markerPen, mx, y - QuotaBarGeometry.MarkerOvershoot, mx, y + BarH + 1);
                using var markerBrush = new SolidBrush(theme.TextMuted);
                g.FillPolygon(markerBrush, QuotaBarGeometry.PaceTriangle(mx, y));
            }
        }
        y += BarH + 3;

        string cd = UsageFormat.Countdown(win?.ResetsAt, s.Resetting);
        if (draw && cd.Length > 0)
        {
            // "resetea en 2h 13m · mar 18:42" — relativo (countdown) + hora local absoluta, con el
            // día abreviado del idioma elegido (T2: la UI inglesa mostraba "jue 02:12").
            string abs = UsageFormat.ResetAbsolute(win?.ResetsAt, s.Culture);
            string line = abs.Length > 0 ? $"{s.ResetsIn} {cd} · {abs}" : $"{s.ResetsIn} {cd}";
            g.DrawString(line, smallFont, dim, x, y);
        }
        return y + 14;
    }
}

/// <summary>
/// Geometría pura y testeable de la barra de cuota: proyección de un porcentaje al eje X de la barra.
/// Sin estado ni dependencias de GDI+; usada por el pace marker y los ticks de umbral (Tarea 3).
/// </summary>
public static class QuotaBarGeometry
{
    /// <summary>
    /// X (px) del marcador para un porcentaje <paramref name="pct"/> dentro de la barra que arranca en
    /// <paramref name="x"/> con ancho <paramref name="w"/>. Recorta el resultado a [x, x+w].
    /// </summary>
    public static int MarkerX(int x, int w, double pct)
    {
        double p = Math.Clamp(pct, 0.0, 100.0);
        return x + (int)Math.Round(w * p / 100.0);
    }

    /// <summary>X (px) de un tick de umbral; comparte la proyección de <see cref="MarkerX"/>.</summary>
    public static int TickX(int x, int w, double thresholdPct) => MarkerX(x, w, thresholdPct);

    /// <summary>
    /// Overshoot máximo del pace marker por encima de la barra (px): el mismo que ya usa la línea
    /// vertical. Es el techo del clamp del ▾ (T3c): nada del marcador sube más que esto.
    /// </summary>
    public const int MarkerOvershoot = 2;

    /// <summary>
    /// Puntos del ▾ del pace marker, clampados a la fila de la barra (T3c): la base queda en
    /// <c>barY - MarkerOvershoot</c> (donde ya arrancaba la línea) y la punta entra 1px en la barra,
    /// apuntando hacia abajo. El triángulo antiguo (base en barY-5) invadía la fila del label.
    /// </summary>
    public static Point[] PaceTriangle(int mx, int barY) => new[]
    {
        new Point(mx - 3, barY - MarkerOvershoot),
        new Point(mx + 3, barY - MarkerOvershoot),
        new Point(mx, barY + 1),
    };

    /// <summary>
    /// Ancho del track de una fila compacta (mini-cuota con el % right-aligned en la MISMA banda
    /// vertical): el track se corta <paramref name="gap"/> px antes del texto para no tacharlo (T3a).
    /// Nunca negativo (locales largos / panel estrecho ⇒ el track desaparece antes que pisar el texto).
    /// </summary>
    public static int CompactTrackWidth(int w, int rightTextW, int gap) => Math.Max(0, w - rightTextW - gap);
}
