using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Secciones plegables del dashboard, reordenadas por prioridad: Cuota → Sesiones → Gasto → Gráfica.
/// Sin estado: recibe Theme/Strings/Config/fuentes/datos y los diccionarios de rects por parámetro.
/// Cada cabecera de sección es clicable ("▸ Título" plegada / "▾ Título" expandida) y registra su
/// rect en <c>sectionRects</c> con clave "quota"/"sessions"/"spend"/"chart". Cada cuerpo conserva la
/// simetría medir(draw=false)/pintar(draw=true): avanza y devuelve <c>y</c> idéntico en ambas ramas.
/// </summary>
public static class DashboardDataView
{
    // --- Datos estáticos del gráfico (movidos de DashboardForm, intactos) ---
    internal static readonly (ChartRange range, string label)[] Tabs =
    {
        (ChartRange.Hour1, "1H"), (ChartRange.Hours5, "5H"), (ChartRange.Day1, "24H"),
        (ChartRange.Week1, "7D"), (ChartRange.Month1, "30D")
    };

    // Stacked-area series (bottom → top) with their colours.
    internal static readonly (string name, Color color)[] Series =
    {
        ("Opus", Color.FromArgb(167, 139, 250)),   // violet
        ("Sonnet", Color.FromArgb(56, 189, 248)),  // sky
        ("Haiku", Color.FromArgb(52, 211, 153)),   // emerald
        ("other", Color.FromArgb(148, 163, 184))   // slate
    };

    internal static double SeriesValue(int s, HistoryBucket b) => s switch
    {
        0 => b.Opus, 1 => b.Sonnet, 2 => b.Haiku, _ => b.Other
    };

    internal const int ChartH = 92;
    internal const int ChartFooter = 32;

    /// <summary>
    /// Dibuja las cuatro secciones plegables en orden de prioridad y devuelve el nuevo y.
    /// Limpia y rellena <c>sectionRects</c>; los rects internos de cada sección (tabs/modos/ventana
    /// %/filas live) se limpian/rellenan según corresponda manteniendo el comportamiento previo.
    /// </summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w,
        AppSnapshot? snap, LiveSessionsView live, AppConfig cfg, Strings s, Theme theme,
        Font labelFont, Font smallFont, Font tabFont,
        string chartMode, ChartRange chartRange, string chartPctWindow,
        List<HistoryBucket> chartData, List<PctPoint> pctData, bool chartLoading,
        Dictionary<string, Rectangle> sectionRects,
        Dictionary<ChartRange, Rectangle> tabRects,
        Dictionary<string, Rectangle> modeRects,
        Dictionary<string, Rectangle> pctWinRects,
        Dictionary<string, Rectangle> liveRowRects)
    {
        sectionRects.Clear();

        using var fg = new SolidBrush(theme.TextPrimary);
        using var dim = new SolidBrush(theme.TextSecondary);

        // 1) Cuota (barras 5h/7d + pace + modelos)
        y = Section(g, draw, "quota", s.SectionQuota, !cfg.CollapsedQuota, x, y, w, theme, labelFont, sectionRects,
            yy => DrawQuotaBody(g, draw, snap, cfg, s, theme, x, yy, w, labelFont, smallFont, fg, dim));

        // 2) Sesiones en vivo (solo lista de instancias; la mascota la pinta la cabecera, no se duplica aquí)
        if (cfg.LiveSessionsEnabled)
        {
            y = Section(g, draw, "sessions", s.SectionSessions, !cfg.CollapsedSessions, x, y, w, theme, labelFont, sectionRects,
                yy => DrawLiveSessionsBody(g, draw, live, s, x, yy, w, smallFont, fg, dim, liveRowRects));
        }
        else { liveRowRects.Clear(); }

        // 3) Gasto estimado por modelo
        if (cfg.ShowSpendEstimate && snap?.Spend is { } spend && spend.CostByModel.Count > 0)
        {
            y = Section(g, draw, "spend", s.SectionSpend, !cfg.CollapsedSpend, x, y, w, theme, labelFont, sectionRects,
                yy => DrawSpendSection(g, draw, snap, s, x, yy, w, labelFont, smallFont, fg, dim));
        }

        // 4) Gráfica de uso
        if (cfg.ShowChart)
        {
            y = Section(g, draw, "chart", s.SectionChart, !cfg.CollapsedChart, x, y, w, theme, labelFont, sectionRects,
                yy => DrawChart(g, draw, x, yy, w, s, theme, cfg, smallFont, tabFont,
                    chartMode, chartRange, chartPctWindow, chartData, pctData, chartLoading,
                    tabRects, modeRects, pctWinRects, dim));
            // Cuando la sección gráfica está plegada, no deben quedar rects fantasma.
            if (cfg.CollapsedChart) { tabRects.Clear(); modeRects.Clear(); pctWinRects.Clear(); }
        }
        else
        {
            tabRects.Clear(); modeRects.Clear(); pctWinRects.Clear();
        }

        return y;
    }

    /// <summary>Cabecera plegable: dibuja "▸/▾ Título", registra su rect y, si está expandida, llama al cuerpo.</summary>
    private static int Section(Graphics g, bool draw, string key, string title, bool expanded,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects, Func<int, int> body)
    {
        var r = new Rectangle(x, y, w, 18);
        rects[key] = r;
        if (draw)
        {
            using var b = new SolidBrush(theme.TextPrimary);
            g.DrawString((expanded ? "▾ " : "▸ ") + title, f, b, x, y);
        }
        y += 22;
        if (expanded) y = body(y);
        return y + 6;
    }

    // ---------------- Cuerpos de sección (movidos de DashboardForm.cs, lógica intacta) ----------------

    /// <summary>Cuota: barra 5h + barra 7d + línea de pace + modelos 7d (Opus/Sonnet).</summary>
    private static int DrawQuotaBody(Graphics g, bool draw, AppSnapshot? snap, AppConfig cfg, Strings s, Theme theme,
        int x, int y, int w, Font labelFont, Font smallFont, Brush fg, Brush dim)
    {
        var usage = snap?.Usage;
        if (usage is null)
        {
            if (draw)
            {
                string msg = snap is null ? s.Loading : UsageFormat.StateMessage(snap.LatestState, s);
                g.DrawString(msg, labelFont, dim, x, y);
            }
            return y + 24;
        }

        y = DrawBar(g, draw, $"{s.SessionWord} (5h)", usage.FiveHour, snap?.PaceFive?.Status, x, y, w, cfg, s, theme, labelFont, smallFont, fg, dim);
        y += 16;
        y = DrawBar(g, draw, $"{s.WeekWord} (7d)", usage.SevenDay, snap?.PaceSeven?.Status, x, y, w, cfg, s, theme, labelFont, smallFont, fg, dim);
        y += 14;

        y = DrawPace(g, draw, snap, theme, x, y, w, smallFont);

        y = DrawModelLine(g, draw, "Opus 7d", usage.SevenDayOpus, x, y, w, smallFont, fg, dim);
        y = DrawModelLine(g, draw, "Sonnet 7d", usage.SevenDaySonnet, x, y, w, smallFont, fg, dim);
        return y;
    }

    /// <summary>Gasto estimado por modelo (cabecera con días + filas modelo/$$).</summary>
    private static int DrawSpendSection(Graphics g, bool draw, AppSnapshot? snap, Strings s,
        int x, int y, int w, Font labelFont, Font smallFont, Brush fg, Brush dim)
    {
        var spend = snap?.Spend;
        if (spend is null || spend.CostByModel.Count == 0) return y;

        if (draw) g.DrawString(string.Format(s.SpendHeaderFormat, snap!.SpendDays), smallFont, dim, x, y);
        y += 18;
        foreach (var kv in spend.CostByModel.OrderByDescending(k => k.Value))
        {
            if (draw)
            {
                g.DrawString(kv.Key, labelFont, fg, x, y);
                string val = $"${kv.Value:0.00}";
                var sz = g.MeasureString(val, Typography.Mono);
                g.DrawString(val, Typography.Mono, dim, x + w - sz.Width, y);
            }
            y += 20;
        }
        return y;
    }

    // Cuerpo de DrawPace de DashboardForm.cs, adaptado a recibir snap/theme/font por parámetro.
    internal static int DrawPace(Graphics g, bool draw, AppSnapshot? snap, Theme theme, int x, int y, int w, Font smallFont)
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

    // Cuerpo de DrawBar de DashboardForm.cs, adaptado a recibir cfg/strings/theme/fuentes por parámetro.
    internal static int DrawBar(Graphics g, bool draw, string label, UsageWindow? win, PaceStatus? pace, int x, int y, int w,
        AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont, Brush fg, Brush dim)
    {
        double util = win?.UtilizationPct ?? 0;
        double clamped = Math.Min(util / 100.0, 1.0);
        // Colour the section by PACE status (better/worse rate); fall back to riesgo gradual.
        Color c = pace is { } ps
            ? (ps == PaceStatus.Critical ? theme.Critical : ps == PaceStatus.Over ? theme.Warn : theme.Ok)
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

        const int barH = 11;
        if (draw)
        {
            using var trackBrush = new SolidBrush(theme.Track);
            Shapes.FillRounded(g, trackBrush, new Rectangle(x, y, w, barH), 5);
            int fw = (int)Math.Round(w * clamped);
            if (fw > 1)
            {
                using var fillBrush = new SolidBrush(c);
                Shapes.FillRounded(g, fillBrush, new Rectangle(x, y, fw, barH), 5);
            }
        }
        y += barH + 3;

        string cd = UsageFormat.Countdown(win?.ResetsAt, s.Resetting);
        if (draw && cd.Length > 0)
            g.DrawString($"{s.ResetsIn} {cd}", smallFont, dim, x, y);
        return y + 14;
    }

    internal static int DrawModelLine(Graphics g, bool draw, string label, UsageWindow? win, int x, int y, int w,
        Font smallFont, Brush fg, Brush dim)
    {
        if (win is null) return y;
        if (draw)
        {
            g.DrawString(label, smallFont, dim, x, y);
            string val = $"{win.UtilizationPct:0.#}%";
            var sz = g.MeasureString(val, Typography.Mono);
            g.DrawString(val, Typography.Mono, fg, x + w - sz.Width, y);
        }
        return y + 16;
    }

    // Cuerpo de DrawLiveSessions SIN su propia cabecera (la pone Section). Solo lista de instancias:
    // la mascota la pinta DashboardHeader, así que NO se vuelve a dibujar aquí (evita duplicado).
    internal static int DrawLiveSessionsBody(Graphics g, bool draw, LiveSessionsView view, Strings s,
        int x, int y, int w, Font smallFont, Brush fg, Brush dim,
        Dictionary<string, Rectangle> liveRowRects)
    {
        liveRowRects.Clear();

        // Lista de instancias
        if (view.Instances.Count == 0)
        {
            if (draw) g.DrawString(s.NoActiveSessions, smallFont, dim, x, y);
            y += 18;
        }
        else
        {
            foreach (var inst in view.Instances)
            {
                var rect = new Rectangle(x, y, w, 16);
                if (draw)
                {
                    g.DrawString(inst.ProjectName, smallFont, fg, x, y);
                    var st = PhaseLabel(s, inst.Phase);
                    var size = g.MeasureString(st, smallFont);
                    g.DrawString(st, smallFont, dim, x + w - size.Width, y);
                }
                liveRowRects[inst.SessionId] = rect;
                y += 18;
            }
        }
        return y;
    }

    internal static string PhaseLabel(Strings s, SessionPhase p) => p switch
    {
        SessionPhase.Idle => s.SessionPhaseIdle,
        SessionPhase.Processing => s.SessionPhaseProcessing,
        SessionPhase.WaitingForApproval => s.SessionPhaseWaitingApproval,
        SessionPhase.WaitingForInput => s.SessionPhaseWaitingInput,
        SessionPhase.Compacting => s.SessionPhaseCompacting,
        _ => s.SessionPhaseIdle,
    };

    // Cuerpo de DrawChart de DashboardForm.cs: título + toggle $/% , tabs de rango, selector 5h/7d y cuerpo.
    internal static int DrawChart(Graphics g, bool draw, int x, int y, int w, Strings s, Theme theme, AppConfig cfg,
        Font smallFont, Font tabFont,
        string chartMode, ChartRange chartRange, string chartPctWindow,
        List<HistoryBucket> chartData, List<PctPoint> pctData, bool chartLoading,
        Dictionary<ChartRange, Rectangle> tabRects, Dictionary<string, Rectangle> modeRects,
        Dictionary<string, Rectangle> pctWinRects, Brush dim)
    {
        bool pct = chartMode == "percent";

        // Title + mode toggle (Spend $ | Quota %)
        if (draw) g.DrawString(s.UsageChart, smallFont, dim, x, y);
        modeRects.Clear();
        DrawSegments(g, draw, tabFont, theme,
            new[] { (s.ChartTabSpend, "spend"), (s.ChartTabPct, "percent") }, chartMode,
            x + w, y - 1, rightAlign: true, modeRects);
        y += 18;

        // Range tabs (left)
        tabRects.Clear();
        int tx = x;
        foreach (var (range, label) in Tabs)
        {
            var sz = g.MeasureString(label, tabFont);
            var rect = new Rectangle(tx, y, (int)sz.Width + 12, 20);
            if (draw)
            {
                bool active = range == chartRange;
                using var bg = new SolidBrush(active ? theme.Accent : theme.BgElevated);
                Shapes.FillRounded(g, bg, rect, 5);
                using var tb = new SolidBrush(active ? ColorMath.Contrast(theme.Accent) : theme.TextPrimary);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, tabFont, tb, rect, sf);
            }
            tabRects[range] = rect;
            tx += rect.Width + 4;
        }
        // Window selector [5h|7d] (right, only in percent mode)
        pctWinRects.Clear();
        if (pct)
            DrawSegments(g, draw, tabFont, theme,
                new[] { ("5h", "5h"), ("7d", "7d") }, chartPctWindow,
                x + w, y, rightAlign: true, pctWinRects);
        y += 26;

        return pct
            ? DrawPercentBody(g, draw, x, y, w, s, theme, cfg, smallFont, chartRange, chartPctWindow, pctData, chartLoading, dim)
            : DrawSpendBody(g, draw, x, y, w, s, smallFont, chartData, chartLoading, dim);
    }

    internal static void DrawSegments(Graphics g, bool draw, Font font, Theme theme,
        (string label, string key)[] segs, string activeKey, int anchorX, int y,
        bool rightAlign, Dictionary<string, Rectangle> rects)
    {
        const int gap = 3, h = 18, padX = 7;
        var widths = segs.Select(seg => (int)g.MeasureString(seg.label, font).Width + padX * 2).ToArray();
        int total = widths.Sum() + gap * (segs.Length - 1);
        int sx = rightAlign ? anchorX - total : anchorX;
        for (int i = 0; i < segs.Length; i++)
        {
            var (label, key) = segs[i];
            var rect = new Rectangle(sx, y, widths[i], h);
            if (draw)
            {
                bool active = key == activeKey;
                using var bg = new SolidBrush(active ? theme.Accent : theme.BgElevated);
                Shapes.FillRounded(g, bg, rect, 4);
                using var tb = new SolidBrush(active ? ColorMath.Contrast(theme.Accent) : theme.TextPrimary);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, tb, rect, sf);
            }
            rects[key] = rect;
            sx += widths[i] + gap;
        }
    }

    internal static int DrawSpendBody(Graphics g, bool draw, int x, int top, int w, Strings s, Font smallFont,
        List<HistoryBucket> chartData, bool chartLoading, Brush dim)
    {
        int bottom = top + ChartH;
        if (chartLoading)
        {
            if (draw) g.DrawString("…", smallFont, dim, x, top + ChartH / 2);
            return bottom + ChartFooter;
        }

        int n = chartData.Count;
        double max = n > 0 ? chartData.Max(b => b.CostUsd) : 0;
        if (n == 0 || max <= 0)
        {
            if (draw) g.DrawString(s.NoData, smallFont, dim, x, top + ChartH / 2 - 6);
            return bottom + ChartFooter;
        }
        if (!draw) return bottom + ChartFooter;

        double total = chartData.Sum(b => b.CostUsd);
        var totalText = $"{s.ChartTotal} ${total:0.00}";
        var totalSz = g.MeasureString(totalText, smallFont);
        g.DrawString(totalText, smallFont, dim, x, top - 1);
        var totalRect = new RectangleF(x, top - 1, totalSz.Width, totalSz.Height);

        float X(int i) => n == 1 ? x + w / 2f : x + (float)i * w / (n - 1);
        float Y(double v) => bottom - (float)(v / max) * (ChartH - 14);

        var baseline = new double[n];
        for (int sIdx = 0; sIdx < Series.Length; sIdx++)
        {
            bool any = false;
            var topArr = new double[n];
            for (int i = 0; i < n; i++)
            {
                double v = SeriesValue(sIdx, chartData[i]);
                if (v > 0) any = true;
                topArr[i] = baseline[i] + v;
            }
            if (any)
            {
                var pts = new List<PointF>(2 * n);
                for (int i = 0; i < n; i++) pts.Add(new PointF(X(i), Y(topArr[i])));
                for (int i = n - 1; i >= 0; i--) pts.Add(new PointF(X(i), Y(baseline[i])));
                using var br = new SolidBrush(Series[sIdx].color);
                if (pts.Count >= 3) g.FillPolygon(br, pts.ToArray());
            }
            baseline = topArr;
        }

        int peakIdx = 0;
        for (int i = 1; i < n; i++)
            if (chartData[i].CostUsd > chartData[peakIdx].CostUsd) peakIdx = i;
        AnnotatePeak(g, smallFont, theme: null, $"{s.ChartPeak} ${max:0.00}", X(peakIdx), Y(max), x, w, top, totalRect, dim);

        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / 8.0));
        for (int i = 0; i < n; i += labelEvery)
        {
            var lbl = chartData[i].Label;
            var lsz = g.MeasureString(lbl, smallFont);
            g.DrawString(lbl, smallFont, dim, X(i) - lsz.Width / 2f, bottom + 2);
        }

        int lx = x, ly = bottom + 16;
        for (int sIdx = 0; sIdx < Series.Length; sIdx++)
        {
            bool any = false;
            for (int i = 0; i < n; i++) if (SeriesValue(sIdx, chartData[i]) > 0) { any = true; break; }
            if (!any) continue;
            using var sw = new SolidBrush(Series[sIdx].color);
            g.FillRectangle(sw, lx, ly + 2, 9, 9);
            g.DrawString(Series[sIdx].name, smallFont, dim, lx + 12, ly);
            lx += 12 + (int)g.MeasureString(Series[sIdx].name, smallFont).Width + 10;
        }
        return bottom + ChartFooter;
    }

    internal static int DrawPercentBody(Graphics g, bool draw, int x, int top, int w, Strings s, Theme theme, AppConfig cfg,
        Font smallFont, ChartRange chartRange, string chartPctWindow, List<PctPoint> pctData, bool chartLoading, Brush dim)
    {
        int bottom = top + ChartH;
        if (chartLoading)
        {
            if (draw) g.DrawString("…", smallFont, dim, x, top + ChartH / 2);
            return bottom + ChartFooter;
        }

        Func<PctPoint, double?> sel = chartPctWindow == "5h" ? p => p.FivePct : p => p.SevenPct;
        var pts = pctData.Where(p => sel(p) is not null)
            .Select(p => (p.TsUtc, v: sel(p)!.Value)).ToList();

        if (pts.Count == 0)
        {
            if (draw) g.DrawString(s.NoData, smallFont, dim, x, top + ChartH / 2 - 6);
            return bottom + ChartFooter;
        }
        if (!draw) return bottom + ChartFooter;

        int n = pts.Count;
        double peak = pts.Max(p => p.v);
        double current = pts[^1].v;

        float X(int i) => n == 1 ? x + w / 2f : x + (float)i * w / (n - 1);
        float Y(double v) => bottom - (float)(Math.Clamp(v, 0, 100) / 100.0) * (ChartH - 14);

        Color status = ColorMath.RiskColor(peak, theme, cfg.WarnThresholdPct, cfg.CriticalThresholdPct);

        // current value (top-left)
        var curText = $"{current:0.#}%";
        var curSz = g.MeasureString(curText, smallFont);
        g.DrawString(curText, smallFont, dim, x, top - 1);
        var curRect = new RectangleF(x, top - 1, curSz.Width, curSz.Height);

        // filled area under the line
        var poly = new List<PointF>(n + 2);
        for (int i = 0; i < n; i++) poly.Add(new PointF(X(i), Y(pts[i].v)));
        poly.Add(new PointF(X(n - 1), bottom));
        poly.Add(new PointF(X(0), bottom));
        using (var fill = new SolidBrush(Color.FromArgb(70, status)))
            if (poly.Count >= 3) g.FillPolygon(fill, poly.ToArray());
        // line on top
        if (n >= 2)
        {
            using var pen = new Pen(status, 1.8f);
            var line = new PointF[n];
            for (int i = 0; i < n; i++) line[i] = new PointF(X(i), Y(pts[i].v));
            g.DrawLines(pen, line);
        }

        // peak annotation
        int peakIdx = 0;
        for (int i = 1; i < n; i++) if (pts[i].v > pts[peakIdx].v) peakIdx = i;
        AnnotatePeak(g, smallFont, theme, $"{s.ChartPeak} {peak:0.#}%", X(peakIdx), Y(peak), x, w, top, curRect, dim);

        // x-axis time labels
        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / 6.0));
        bool longRange = chartRange is ChartRange.Week1 or ChartRange.Month1;
        for (int i = 0; i < n; i += labelEvery)
        {
            string lbl = pts[i].TsUtc.ToLocalTime().ToString(longRange ? "dd/MM" : "HH:mm");
            var lsz = g.MeasureString(lbl, smallFont);
            g.DrawString(lbl, smallFont, dim, X(i) - lsz.Width / 2f, bottom + 2);
        }
        return bottom + ChartFooter;
    }

    // Cuerpo de AnnotatePeak de DashboardForm.cs. El marcador/etiqueta usan Foreground; en el body de
    // gasto no se disponía de theme (usaba _theme.Foreground), así que aceptamos theme nullable y caemos
    // al pincel dim cuando es null para preservar el aspecto sin acoplar de más.
    private static void AnnotatePeak(Graphics g, Font font, Theme? theme, string text, float peakX, float peakY,
        int x, int w, int top, RectangleF avoid, Brush dim)
    {
        using var marker = theme is not null ? new SolidBrush(theme.Foreground) : null;
        Brush mb = marker ?? dim;
        g.FillEllipse(mb, peakX - 2.5f, peakY - 2.5f, 5, 5);
        var psz = g.MeasureString(text, font);
        float pxp = Math.Clamp(peakX - psz.Width / 2f, x, x + w - psz.Width);
        float pyp = Math.Max(top - 2, peakY - psz.Height - 1);
        // Avoid colliding with the top-left label (total / current %): when the peak
        // sits at the top-left, both labels land on the same row and become unreadable.
        var rect = new RectangleF(pxp, pyp, psz.Width, psz.Height);
        if (rect.IntersectsWith(avoid))
        {
            pyp = peakY + 4;                       // drop the peak label below its marker
            rect = new RectangleF(pxp, pyp, psz.Width, psz.Height);
            if (rect.IntersectsWith(avoid))        // still tight → shift right past the label
                pxp = Math.Clamp(avoid.Right + 6, x, x + w - psz.Width);
        }
        g.DrawString(text, font, mb, pxp, pyp);
    }

}
