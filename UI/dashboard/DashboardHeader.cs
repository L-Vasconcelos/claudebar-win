using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>Cabecera de un vistazo: mascota + estado servicio + cuota crítica + pace. Sin estado.</summary>
public static class DashboardHeader
{
    // Glifo de engranaje de la fuente de iconos del sistema (Segoe MDL2 Assets, Win10+): nítido y vectorial,
    // a diferencia del carácter de texto "⚙" que sale diminuto/borroso. Cacheado (se crea una sola vez).
    private const string GearGlyph = ""; // MDL2 "Settings"
    private static readonly Font GearIconFont = new("Segoe MDL2 Assets", 15f, GraphicsUnit.Pixel);
    private static readonly StringFormat CenterFormat = new()
    {
        Alignment = StringAlignment.Center,
        LineAlignment = StringAlignment.Center
    };

    /// <summary>Dibuja la cabecera y devuelve el nuevo y. Registra el rect del ⚙ en gearRect.</summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w,
                           AppSnapshot? snap, LiveSessionsView live, AppConfig cfg, Strings s, Theme theme,
                           int mascotFrame, Font labelFont, Font smallFont, Font mono,
                           ref Rectangle gearRect)
    {
        // 1) botón ⚙ arriba a la derecha (siempre): botón con fondo redondeado + glifo nítido,
        //    para que se lea bien y parezca clicable (antes era un "⚙" diminuto y atenuado, sin fondo).
        const int gearSize = 24;
        var gear = new Rectangle(x + w - gearSize, y, gearSize, gearSize);
        gearRect = gear;
        if (draw)
        {
            using (var bgBrush = new SolidBrush(theme.BgElevated))
                Shapes.FillRounded(g, bgBrush, gear, 7);
            using var glyphBrush = new SolidBrush(theme.TextPrimary);
            g.DrawString(GearGlyph, GearIconFont, glyphBrush, gear, CenterFormat);
        }

        // 2) mascota a la izquierda (si ShowMascot y LiveSessionsEnabled)
        int top = y + 18;
        var mascotSize = MascotSprite.ParseSize(cfg.MascotSize);
        int textX = x;
        int mascotH = 0;
        if (cfg.LiveSessionsEnabled && cfg.ShowMascot)
        {
            var sz = MascotRenderer.Draw(g, draw, x, top, live.GlobalPhase,
                mascotSize, mascotFrame, theme, mono);
            textX = x + sz.Width + 12;
            mascotH = sz.Height;
        }

        // 3) a la derecha: estado servicio + cuota crítica + pace
        int ty = top;
        if (cfg.ShowHealth && snap?.Health is { } h)
        {
            (string hl, Color hc) = h.Level switch
            {
                HealthLevel.Operational => (s.HealthOk, theme.Ok),
                HealthLevel.Degraded => (s.HealthDegraded, theme.Warn),
                HealthLevel.Outage => (s.HealthOutage, theme.Critical),
                _ => (h.Description, theme.TextSecondary)
            };
            if (draw)
            {
                using var dot = new SolidBrush(hc);
                g.FillEllipse(dot, textX, ty + 4, 8, 8);
                using var b = new SolidBrush(theme.TextSecondary);
                g.DrawString(hl, smallFont, b, textX + 12, ty);
            }
        }
        ty += 16;

        // cuota crítica = la de mayor utilización entre 5h/7d
        var w5 = snap?.Usage?.FiveHour; var w7 = snap?.Usage?.SevenDay;
        var crit = PickCritical(w5, w7);
        if (crit is not null)
        {
            // ventana elegida → etiqueta + pace para colorear la barra igual que el dashboard.
            bool isFive = ReferenceEquals(crit, w5);
            var critPace = isFive ? snap?.PaceFive : snap?.PaceSeven;
            string critLabel = isFive ? $"{s.SessionWord} (5h)" : $"{s.WeekWord} (7d)";
            // Delegar en la barra unificada; la cabecera construye sus pinceles fg/muted como antes.
            using var fg = new SolidBrush(theme.TextPrimary);
            using var muted = new SolidBrush(theme.TextMuted);
            ty = QuotaBar.Draw(g, draw, critLabel, crit, critPace, textX, ty, w - (textX - x), cfg, s, theme, labelFont, smallFont, fg, muted);
        }

        // pace: línea "↗ 5h X% · 7d Y% ⚠ETA" reusando lo de DrawPace
        ty = DrawPaceLine(g, draw, snap, textX, ty, w - (textX - x), theme, smallFont);

        int bottom = Math.Max(ty, top + mascotH);
        // separador
        if (draw) { using var p = new Pen(theme.Separator); g.DrawLine(p, x, bottom + 4, x + w, bottom + 4); }
        return bottom + 10;
    }

    private static UsageWindow? PickCritical(UsageWindow? a, UsageWindow? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.UtilizationPct >= b.UtilizationPct ? a : b;
    }

    // Cuerpo de DrawPace de DashboardForm.cs adaptado a recibir snap/theme/font por parámetro.
    private static int DrawPaceLine(Graphics g, bool draw, AppSnapshot? snap, int x, int y, int w,
                                    Theme theme, Font smallFont)
    {
        var pf = snap?.PaceFive;
        var ps = snap?.PaceSeven;
        if (pf is null && ps is null) return y;
        if (!draw) return y + 18;

        var worst = (PaceStatus)Math.Max((int)(pf?.Status ?? PaceStatus.Ok), (int)(ps?.Status ?? PaceStatus.Ok));
        Color c = worst == PaceStatus.Critical ? theme.Critical
                : worst == PaceStatus.Over ? theme.Warn : theme.Ok;

        string text = "↗ ";
        if (pf is not null) text += $"5h {pf.PaceRatio * 100:0}%";
        if (ps is not null) text += (pf is not null ? " · " : "") + $"7d {ps.PaceRatio * 100:0}%";

        var exa = new[] { pf, ps }
            .Where(p => p is { ExhaustsBeforeReset: true, EtaUtc: not null })
            .OrderBy(p => p!.EtaUtc).FirstOrDefault();
        if (exa is not null)
            text += $"   ⚠ {exa.EtaUtc!.Value.ToLocalTime():ddd HH:mm}";

        using var br = new SolidBrush(c);
        g.DrawString(text, smallFont, br, x, y);
        return y + 18;
    }

}
