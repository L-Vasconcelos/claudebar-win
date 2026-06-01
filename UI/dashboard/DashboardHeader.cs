using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>Cabecera de un vistazo: mascota + estado servicio + cuota crítica + pace. Sin estado.</summary>
public static class DashboardHeader
{
    /// <summary>Dibuja la cabecera y devuelve el nuevo y. Registra el rect del ⚙ en gearRect.</summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w,
                           AppSnapshot? snap, LiveSessionsView live, AppConfig cfg, Strings s, Theme theme,
                           int mascotFrame, Font labelFont, Font smallFont, Font mono,
                           ref Rectangle gearRect)
    {
        // 1) botón ⚙ arriba a la derecha (siempre)
        var gear = new Rectangle(x + w - 18, y, 16, 16);
        gearRect = gear;
        if (draw)
        {
            using var b = new SolidBrush(theme.Dim);
            g.DrawString("⚙", smallFont, b, gear.X, gear.Y); // ⚙
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
                _ => (h.Description, theme.Dim)
            };
            if (draw)
            {
                using var dot = new SolidBrush(hc);
                g.FillEllipse(dot, textX, ty + 4, 8, 8);
                using var b = new SolidBrush(theme.Dim);
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
            var critPace = isFive ? snap?.PaceFive?.Status : snap?.PaceSeven?.Status;
            string critLabel = isFive ? $"{s.SessionWord} (5h)" : $"{s.WeekWord} (7d)";
            ty = DrawCriticalBar(g, draw, crit, critPace, critLabel, textX, ty, w - (textX - x), cfg, s, theme, labelFont, smallFont);
        }

        // pace: línea "↗ 5h X% · 7d Y% ⚠ETA" reusando lo de DrawPace
        ty = DrawPaceLine(g, draw, snap, textX, ty, w - (textX - x), theme, smallFont);

        int bottom = Math.Max(ty, top + mascotH);
        // separador
        if (draw) { using var p = new Pen(theme.Track); g.DrawLine(p, x, bottom + 4, x + w, bottom + 4); }
        return bottom + 10;
    }

    private static UsageWindow? PickCritical(UsageWindow? a, UsageWindow? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a.UtilizationPct >= b.UtilizationPct ? a : b;
    }

    // Cuerpo de DrawBar de DashboardForm.cs adaptado a recibir theme/strings/fonts por parámetro.
    // Avanza y de forma idéntica en draw=false/draw=true (la simetría medir/pintar se preserva).
    private static int DrawCriticalBar(Graphics g, bool draw, UsageWindow? win, PaceStatus? pace, string label, int x, int y, int w,
                                       AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont)
    {
        double util = win?.UtilizationPct ?? 0;
        double clamped = Math.Min(util / 100.0, 1.0);
        // Colour the section by PACE status (better/worse rate); fall back to level threshold.
        Color c = pace is { } ps
            ? (ps == PaceStatus.Critical ? theme.Critical : ps == PaceStatus.Over ? theme.Warn : theme.Ok)
            : (util >= cfg.CriticalThresholdPct ? theme.Critical
                : util >= cfg.WarnThresholdPct ? theme.Warn : theme.Ok);

        if (draw)
        {
            using var fg = new SolidBrush(theme.Foreground);
            g.DrawString(label, labelFont, fg, x, y);
            string right = $"{util:0.#}%";
            var sz = g.MeasureString(right, labelFont);
            using var valBrush = new SolidBrush(c);
            g.DrawString(right, labelFont, valBrush, x + w - sz.Width, y);
        }
        y += 22;

        const int barH = 11;
        if (draw)
        {
            using var trackBrush = new SolidBrush(theme.Track);
            FillRounded(g, trackBrush, new Rectangle(x, y, w, barH), 5);
            int fw = (int)Math.Round(w * clamped);
            if (fw > 1)
            {
                using var fillBrush = new SolidBrush(c);
                FillRounded(g, fillBrush, new Rectangle(x, y, fw, barH), 5);
            }
        }
        y += barH + 3;

        string cd = UsageFormat.Countdown(win?.ResetsAt, s.Resetting);
        if (draw && cd.Length > 0)
        {
            using var dim = new SolidBrush(theme.Dim);
            g.DrawString($"{s.ResetsIn} {cd}", smallFont, dim, x, y);
        }
        return y + 14;
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

    // Copia de FillRounded de DashboardForm.cs (helper de dibujo de barras redondeadas).
    private static void FillRounded(Graphics g, Brush b, Rectangle r, int radius)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        radius = Math.Min(radius, Math.Min(r.Width, r.Height) / 2);
        if (radius <= 1) { g.FillRectangle(b, r); return; }
        int d = radius * 2;
        using var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(b, path);
    }
}
