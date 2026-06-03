using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
using ClaudeBarWin.Services.Motion;

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
    /// <param name="mascotBounceOffsetY">
    /// Bote de atención (px ≥ 0): desplaza el dibujo de la mascota hacia arriba dentro de su celda
    /// (vía <c>TranslateTransform</c>). NO cambia el <c>y</c> de layout (medir == pintar). Solo en la
    /// pasada de pintado; con reduce-motion el llamante pasa 0.
    /// </param>
    /// <param name="celebration">
    /// Destello de celebración in-panel (p.ej. "✓ cuota renovada") cuando una ventana se resetea.
    /// <c>null</c> = sin destello. NO toca el sistema de notificaciones (eso es F4).
    /// </param>
    public static int Draw(Graphics g, bool draw, int x, int y, int w,
                           AppSnapshot? snap, LiveSessionsView live, AppConfig cfg, Strings s, Theme theme,
                           MascotState mascot, Mood mascotMood, Font labelFont, Font smallFont, Font mono,
                           ref Rectangle gearRect,
                           MotionState? motion = null, bool reduceMotion = false,
                           int mascotBounceOffsetY = 0, string? celebration = null)
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

        // 1b) destello de celebración de reset ("✓ cuota renovada"): chip in-panel breve en la fila
        //     superior, a la izquierda del ⚙. Solo en la pasada de pintado y SIN reservar alto (esa
        //     fila solo aloja el ⚙) → no rompe el invariante medir/pintar. NO es una notificación (F4).
        if (draw && !string.IsNullOrEmpty(celebration))
        {
            string flash = "✓ " + celebration;
            var fsz = g.MeasureString(flash, smallFont);
            int chipW = (int)Math.Ceiling(fsz.Width) + Spacing.Sm * 2;
            int chipH = (int)Math.Ceiling(fsz.Height) + Spacing.Xs;
            var chip = new Rectangle(gear.X - chipW - Spacing.Sm, y + (gearSize - chipH) / 2, chipW, chipH);
            if (chip.X > x)
            {
                using var chipBg = new SolidBrush(Color.FromArgb(36, theme.Ok));
                Shapes.FillRounded(g, chipBg, chip, Spacing.Sm);
                using var okBrush = new SolidBrush(theme.Ok);
                g.DrawString(flash, smallFont, okBrush,
                    chip.X + Spacing.Sm, chip.Y + (chipH - (int)Math.Ceiling(fsz.Height)) / 2);
            }
        }

        // 2) mascota a la izquierda (si ShowMascot): sprite (frame del animador) + verbo localizado
        //    JUGUETÓN con elipsis bajo la mascota. El verbo reserva su alto en AMBAS pasadas
        //    (medir/pintar) para no romper el invariante de layout.
        //    T0: la visibilidad se DESACOPLA de LiveSessionsEnabled (antes `live && mascota`, el bug):
        //    con ShowMascot on y live off se ve un gato Idle estático (ambiente). Las puertas de
        //    ANIMACIÓN siguen exigiendo live on (bote en SyncBounce, fast-tick/mascotAlive en
        //    DashboardForm; aquí el bounce llega ya gateado y, con draw=false, se fuerza a 0).
        int top = y + 18;
        var mascotSize = MascotSprite.ParseSize(cfg.MascotSize);
        int textX = x;
        int mascotH = 0;
        if (cfg.ShowMascot)
        {
            // Bote de atención: desplaza SOLO el dibujo de la mascota hacia arriba (px ≥ 0) dentro de
            // su celda. El offset se mide con draw=false como 0 (no se aplica transform), así medir y
            // pintar reservan el MISMO alto (invariante de layout). Hacia arriba = y negativo.
            int bounce = draw ? mascotBounceOffsetY : 0;
            // try/finally (fix F3 minor, mismo patrón que DashboardDataView.Section): el pop del transform
            // del bote ocurre siempre, aunque MascotRenderer.Draw lance, para no contaminar frames siguientes.
            if (bounce != 0) g.TranslateTransform(0, -bounce);
            Size sz;
            try
            {
                sz = MascotRenderer.Draw(g, draw, x, top, live.GlobalPhase,
                    mascotSize, mascot, theme, mono, mascotMood);
            }
            finally
            {
                if (bounce != 0) g.TranslateTransform(0, bounce);
            }

            // Verbo + elipsis (p.ej. "thinking…"). Alto reservado siempre (medir == pintar).
            string verb = MascotAnimator.Verb(s, live.GlobalPhase, mascot.VerbIndex);
            int verbH = 0;
            if (!string.IsNullOrEmpty(verb))
            {
                verbH = (int)Math.Ceiling(smallFont.GetHeight(g));
                if (draw)
                {
                    using var vb = new SolidBrush(theme.TextMuted);
                    g.DrawString(verb + "…", smallFont, vb, x, top + sz.Height + 2);
                }
            }

            textX = x + sz.Width + 12;
            mascotH = sz.Height + (verbH > 0 ? verbH + 2 : 0);
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
            // Override eased del %/ancho de la barra crítica (color por objetivo). null si no hay motion.
            double? dCrit = motion is not null ? motion.Display("num:crit", crit.UtilizationPct, reduceMotion) : (double?)null;
            ty = QuotaBar.Draw(g, draw, critLabel, crit, critPace, textX, ty, w - (textX - x), cfg, s, theme, labelFont, smallFont, fg, muted, dCrit);
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
