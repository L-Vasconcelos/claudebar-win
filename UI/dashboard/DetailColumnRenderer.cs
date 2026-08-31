using System.Drawing.Drawing2D;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Renders the right column of the unified dashboard: spend bars, chart, and model breakdown.
/// Static, pure GDI+, measure/paint symmetric.
/// </summary>
public static class DetailColumnRenderer
{
    public static int DrawSpendBars(Graphics g, bool draw, int x, int y, int w,
        WindowStats? spend, Strings s, Theme theme, Font smallFont, Brush dim)
    {
        if (draw)
        {
            using var labelFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
            using var lb = new SolidBrush(theme.TextMuted);
            g.DrawString("GASTO ESTIMADO (7D, API-EQUIV)", labelFont, lb, x, y);
        }
        y += Dpi.Scale(16);

        if (spend is null || spend.CostByModel.Count == 0)
        {
            if (draw) g.DrawString(s.Loading, smallFont, dim, x, y);
            return y + Dpi.Scale(20);
        }

        double maxCost = spend.CostByModel.Values.Max();
        var families = spend.CostByModel.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        var slots = DashboardDataView.SeriesAssigner.Assign(families);
        int barH = Dpi.Scale(9);
        int rowH = Dpi.Scale(20);
        int modelW = Dpi.Scale(46);
        int amtW = Dpi.Scale(50);
        int barGap = Dpi.Scale(6);
        using var spendNameFont = new Font(smallFont.FontFamily, 9.5f * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);
        using var spendAmtFont = new Font(Typography.Mono.FontFamily, 9f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);

        foreach (var family in families)
        {
            double cost = spend.CostByModel.GetValueOrDefault(family);
            Color sc = theme.ChartSeries[slots[family] % theme.ChartSeries.Count];
            if (draw)
            {
                using var modelBrush = new SolidBrush(sc);
                string name = DashboardDataView.FamilyLabel(family, s);
                g.DrawString(name, spendNameFont, modelBrush, x, y + 1);

                int barX = x + modelW + barGap;
                int barW = w - modelW - barGap - amtW - barGap;
                using var trackBrush = new SolidBrush(theme.Track);
                Shapes.FillRounded(g, trackBrush, new Rectangle(barX, y + 2, barW, barH), Dpi.Scale(3));
                int fillW = maxCost > 0 ? (int)(barW * cost / maxCost) : 0;
                if (fillW > 1)
                {
                    using var fillBrush = new SolidBrush(sc);
                    Shapes.FillRounded(g, fillBrush, new Rectangle(barX, y + 2, fillW, barH), Dpi.Scale(3));
                }

                string amt = UsageFormat.Money(cost, s.Culture);
                int amtX = x + w - TextMetrics.MeasureWidth(g, amt, spendAmtFont);
                g.DrawString(amt, spendAmtFont, dim, amtX, y + 1, TextMetrics.Typographic);
            }
            y += rowH;
        }
        y += Dpi.Scale(10);
        return y;
    }

    public static int DrawChart(Graphics g, bool draw, int x, int y, int w,
        List<HistoryBucket> chartData, Strings s, Theme theme, Font smallFont, Brush dim)
    {
        if (draw)
        {
            using var sepPen = new Pen(theme.Separator);
            g.DrawLine(sepPen, x, y, x + w, y);
        }
        y += Dpi.Scale(12);

        if (draw)
        {
            using var labelFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
            using var lb = new SolidBrush(theme.TextMuted);
            g.DrawString("USO AO LONGO DO TEMPO", labelFont, lb, x, y);
        }
        y += Dpi.Scale(18);

        int chartH = Dpi.Scale(90);  // auditoria: 100→90
        int n = chartData.Count;
        double max = n > 0 ? chartData.Max(b => b.CostUsd) : 0;

        if (n == 0 || max <= 0)
        {
            if (draw)
            {
                using var mutedBrush = new SolidBrush(theme.TextMuted);
                g.DrawString(s.Loading, smallFont, mutedBrush, x, y + chartH / 2 - 6);
            }
            y += chartH;
        }
        else
        {
            if (draw)
            {
                float X(int i) => n == 1 ? x + w / 2f : x + (float)i * w / (n - 1);
                float Y(double v) => y + chartH - (float)(v / max) * (chartH - Dpi.Scale(10));

                using (var grid = new Pen(theme.Separator, 1f) { DashStyle = DashStyle.Dash })
                    foreach (double frac in new[] { 0.25, 0.5, 0.75 })
                        g.DrawLine(grid, x, Y(max * frac), x + w, Y(max * frac));

                var families = DashboardDataView.ChartFamilies(chartData);
                var slots = DashboardDataView.SeriesAssigner.Assign(families);
                var baseline = new double[n];
                foreach (var family in families)
                {
                    var topArr = new double[n];
                    for (int i = 0; i < n; i++) topArr[i] = baseline[i] + chartData[i].Cost(family);
                    var pts = new List<PointF>(2 * n);
                    for (int i = 0; i < n; i++) pts.Add(new PointF(X(i), Y(topArr[i])));
                    for (int i = n - 1; i >= 0; i--) pts.Add(new PointF(X(i), Y(baseline[i])));
                    Color sc = theme.ChartSeries[slots[family] % theme.ChartSeries.Count];
                    if (pts.Count >= 3)
                    {
                        using var br = new LinearGradientBrush(
                            new PointF(0, y), new PointF(0, y + chartH),
                            Color.FromArgb(180, sc), Color.FromArgb(40, sc));
                        g.FillPolygon(br, pts.ToArray());
                    }
                    if (n >= 2)
                    {
                        using var edge = new Pen(sc, 1.5f);
                        var topLine = new PointF[n];
                        for (int i = 0; i < n; i++) topLine[i] = new PointF(X(i), Y(topArr[i]));
                        g.DrawLines(edge, topLine);
                    }
                    baseline = topArr;
                }

                // X-axis labels (smaller font for chart axis)
                using var muted = new SolidBrush(theme.TextMuted);
                using var axisFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
                int labelEvery = Math.Max(1, n / 5);
                for (int i = 0; i < n; i += labelEvery)
                {
                    var lbl = chartData[i].Label;
                    var lsz = g.MeasureString(lbl, axisFont);
                    float lx = DashboardDataView.AxisLabelX(X(i), lsz.Width, x, x + w);
                    g.DrawString(lbl, axisFont, muted, lx, y + chartH + 2);
                }
            }
            y += chartH;
        }
        y += Dpi.Scale(16);
        return y;
    }

    public static int DrawModelBreakdown(Graphics g, bool draw, int x, int y, int w,
        IReadOnlyList<UsageRecord> records, Strings s, Theme theme, Font smallFont, Brush dim)
    {
        if (draw)
        {
            using var sepPen = new Pen(theme.Separator);
            g.DrawLine(sepPen, x, y, x + w, y);
        }
        y += Dpi.Scale(12);

        if (draw)
        {
            using var labelFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
            using var lb = new SolidBrush(theme.TextMuted);
            g.DrawString("USO POR MODELO", labelFont, lb, x, y);
        }
        y += Dpi.Scale(16);

        var familyGroups = records
            .GroupBy(r => ModelFamily.FromId(r.Model))
            .Where(grp => grp.Key != ModelFamily.Other)
            .OrderByDescending(grp => grp.Count())
            .Take(4)
            .ToList();

        int totalMsgs = records.Count;
        if (familyGroups.Count == 0)
        {
            if (draw) g.DrawString(s.Loading, smallFont, dim, x, y);
            return y + Dpi.Scale(20);
        }

        var familyNames = familyGroups.Select(fg => fg.Key).ToList();
        var slots = DashboardDataView.SeriesAssigner.Assign(familyNames);
        int rowH = Dpi.Scale(26);
        int dotSize = Dpi.Scale(6);

        using var nameFont = new Font(smallFont.FontFamily, 9.5f * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);
        using var subFont = new Font(smallFont.FontFamily, 7.5f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
        using var pctFont = new Font(Typography.Mono.FontFamily, 9.5f * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);

        foreach (var grp in familyGroups)
        {
            Color sc = theme.ChartSeries[slots[grp.Key] % theme.ChartSeries.Count];
            int msgs = grp.Count();
            long tokens = grp.Sum(r => r.TotalTokens);
            int pct = totalMsgs > 0 ? (int)Math.Round(100.0 * msgs / totalMsgs) : 0;

            if (draw)
            {
                using var dotBrush = new SolidBrush(sc);
                g.FillEllipse(dotBrush, x, y + (rowH - dotSize) / 2, dotSize, dotSize);

                int textX = x + dotSize + Dpi.Scale(6);
                using var nameBrush = new SolidBrush(theme.TextPrimary);
                g.DrawString(grp.Key, nameFont, nameBrush, textX, y);

                string sub = $"{UsageStatsService.FormatCount(msgs)} mensagens · {UsageStatsService.FormatTokens(tokens)} tokens";
                using var muted = new SolidBrush(theme.TextMuted);
                g.DrawString(sub, subFont, muted, textX, y + Dpi.Scale(13));

                string pctText = $"{pct}%";
                using var pctBrush = new SolidBrush(sc);
                int pctW = TextMetrics.MeasureWidth(g, pctText, pctFont);
                g.DrawString(pctText, pctFont, pctBrush, x + w - pctW, y + 2, TextMetrics.Typographic);
            }
            y += rowH;
        }

        return y;
    }
}
