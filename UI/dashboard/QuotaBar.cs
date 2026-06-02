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
    /// </summary>
    public static int Draw(Graphics g, bool draw, string label, UsageWindow? win, PaceResult? pace, int x, int y, int w,
        AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont, Brush fg, Brush dim)
    {
        double util = win?.UtilizationPct ?? 0;
        double clamped = Math.Min(util / 100.0, 1.0);
        // Colour the section by PACE status (better/worse rate); fall back to riesgo gradual.
        Color c = pace is { } ps
            ? (ps.Status == PaceStatus.Critical ? theme.Critical : ps.Status == PaceStatus.Over ? theme.Warn : theme.Ok)
            : ColorMath.RiskColor(util, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);

        if (draw)
        {
            g.DrawString(label, labelFont, fg, x, y);
            string right = $"{util:0.#}%";
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
            // tapadas por las esquinas redondeadas. Neutro (theme.Separator), nunca Accent.
            using (var tickPen = new Pen(theme.Separator, 1f))
            {
                int wx = QuotaBarGeometry.TickX(x, w, cfg.WarnThresholdPct);
                int cx = QuotaBarGeometry.TickX(x, w, cfg.CriticalThresholdPct);
                g.DrawLine(tickPen, wx, y, wx, y + BarH - 1);
                g.DrawLine(tickPen, cx, y, cx, y + BarH - 1);
            }

            // Pace marker: "dónde deberías ir" según el ritmo ideal. Sobresale 2px arriba/abajo y
            // lleva un ▾ encima. Color theme.TextMuted (neutro). Solo cuando hay pace.
            if (pace is { } pm)
            {
                int mx = QuotaBarGeometry.MarkerX(x, w, pm.IdealPct);
                using var markerPen = new Pen(theme.TextMuted, 2f);
                g.DrawLine(markerPen, mx, y - 2, mx, y + BarH + 1);
                using var markerBrush = new SolidBrush(theme.TextMuted);
                var tri = new[]
                {
                    new Point(mx - 3, y - 5),
                    new Point(mx + 3, y - 5),
                    new Point(mx, y - 2),
                };
                g.FillPolygon(markerBrush, tri);
            }
        }
        y += BarH + 3;

        string cd = UsageFormat.Countdown(win?.ResetsAt, s.Resetting);
        if (draw && cd.Length > 0)
        {
            // "resetea en 2h 13m · mar 18:42" — relativo (countdown) + hora local absoluta.
            string abs = UsageFormat.ResetAbsolute(win?.ResetsAt);
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
}
