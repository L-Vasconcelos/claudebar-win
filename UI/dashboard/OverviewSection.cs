using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Draws the "Overview" (Visão Geral) section: 8 stat cards in a 4×2 grid + 30-day heatmap + fun fact.
/// Pure GDI+ rendering, DPI-aware, follows the measure/paint symmetry (draw=false returns same y).
/// </summary>
public static class OverviewSection
{
    // Layout constants (design px at 96 DPI, scaled by Dpi.Scale)
    private static int CardPad => Dpi.Scale(6);
    private static int CardH => Dpi.Scale(52);
    private static int CardRadius => Dpi.Scale(8);
    private static int GridGap => Dpi.Scale(6);
    private static int HeatCellSize => Dpi.Scale(14);
    private static int HeatGap => Dpi.Scale(3);
    private static int SectionPad => Dpi.Scale(6);

    /// <summary>
    /// Draws the full overview section and returns the new y.
    /// </summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w,
        UsageStats? stats, Strings s, Theme theme, Font smallFont)
    {
        if (stats is null)
        {
            if (draw)
            {
                using var dim = new SolidBrush(theme.TextSecondary);
                g.DrawString(s.Loading, smallFont, dim, x, y);
            }
            return y + Dpi.Scale(20);
        }

        // --- 4×2 stat cards grid ---
        int cols = 4;
        int cardW = (w - GridGap * (cols - 1)) / cols;

        var cards = new (string label, string value, bool accent)[]
        {
            (s.OverviewSessions, stats.Sessions.ToString("N0"), false),
            (s.OverviewMessages, UsageStatsService.FormatCount(stats.Messages), false),
            (s.OverviewTokens, UsageStatsService.FormatTokens(stats.TotalTokens), false),
            (s.OverviewActiveDays, stats.ActiveDays.ToString(), false),
            (s.OverviewCurrentStreak, $"{stats.CurrentStreak}d", true),
            (s.OverviewLongestStreak, $"{stats.LongestStreak}d", true),
            (s.OverviewPeakHour, $"{stats.PeakHour}h", false),
            (s.OverviewFavoriteModel, stats.FavoriteModel, true),
        };

        for (int row = 0; row < 2; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int idx = row * cols + col;
                int cx = x + col * (cardW + GridGap);
                int cy = y + row * (CardH + GridGap);
                var card = cards[idx];
                if (draw)
                    DrawStatCard(g, cx, cy, cardW, CardH, card.label, card.value, card.accent, theme, smallFont);
            }
        }
        y += 2 * CardH + GridGap + Dpi.Scale(10);

        // --- Heatmap ---
        y = DrawHeatmap(g, draw, x, y, w, stats, s, theme, smallFont);

        return y;
    }

    private static void DrawStatCard(Graphics g, int x, int y, int w, int h,
        string label, string value, bool accent, Theme theme, Font smallFont)
    {
        // Card background
        var rect = new Rectangle(x, y, w, h);
        using (var bg = new SolidBrush(theme.BgElevated))
            Shapes.FillRounded(g, bg, rect, CardRadius);
        using (var border = new Pen(theme.Separator))
            Shapes.DrawRounded(g, border, rect, CardRadius);

        // Label (top, centered, muted — auto-elide if too wide)
        using var labelBrush = new SolidBrush(theme.TextMuted);
        using var labelFont = new Font(smallFont.FontFamily, 7.5f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
        string shownLabel = label;
        var labelSz = g.MeasureString(shownLabel, labelFont);
        if (labelSz.Width > w - CardPad * 2)
            shownLabel = TextWrap.FitLine(label, 0, w - CardPad * 2, 0, t => g.MeasureString(t, labelFont).Width);
        labelSz = g.MeasureString(shownLabel, labelFont);
        float labelX = x + (w - labelSz.Width) / 2;
        g.DrawString(shownLabel, labelFont, labelBrush, labelX, y + CardPad);

        // Value (bottom, centered, bold — auto-shrink font to fit)
        Color valueColor = accent ? theme.Accent : theme.TextPrimary;
        using var valueBrush = new SolidBrush(valueColor);
        float maxValueW = w - CardPad * 2;
        float basePt = 11f;
        Font valueFont = new Font(Typography.Mono.FontFamily, basePt * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);
        var valueSz = g.MeasureString(value, valueFont);
        // Shrink font if value doesn't fit
        while (valueSz.Width > maxValueW && basePt > 7f)
        {
            valueFont.Dispose();
            basePt -= 0.5f;
            valueFont = new Font(Typography.Mono.FontFamily, basePt * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);
            valueSz = g.MeasureString(value, valueFont);
        }
        float valueX = x + (w - valueSz.Width) / 2;
        float valueY = y + h - CardPad - valueSz.Height;
        g.DrawString(value, valueFont, valueBrush, valueX, valueY);
        valueFont.Dispose();
    }

    private static int DrawHeatmap(Graphics g, bool draw, int x, int y, int w,
        UsageStats stats, Strings s, Theme theme, Font smallFont)
    {
        // Label
        if (draw)
        {
            using var labelBrush = new SolidBrush(theme.TextMuted);
            using var labelFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
            g.DrawString(s.OverviewHeatmapLabel, labelFont, labelBrush, x, y);
        }
        y += Dpi.Scale(14);

        // Compute grid dimensions: fit as many columns as the width allows
        int cellSize = HeatCellSize;
        int gap = HeatGap;
        int cols = Math.Max(1, (w + gap) / (cellSize + gap));
        int days = stats.DailyActivity.Count;
        int rows = (int)Math.Ceiling((double)days / cols);

        // Find max for normalization
        long maxTokens = stats.DailyActivity.Count > 0
            ? stats.DailyActivity.Max(d => d.Tokens)
            : 0;

        if (draw && maxTokens > 0)
        {
            for (int i = 0; i < days; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int cx = x + col * (cellSize + gap);
                int cy = y + row * (cellSize + gap);

                var (_, tokens) = stats.DailyActivity[i];
                Color cellColor = HeatColor(tokens, maxTokens, theme);
                using var brush = new SolidBrush(cellColor);
                Shapes.FillRounded(g, brush, new Rectangle(cx, cy, cellSize, cellSize), Dpi.Scale(3));
            }
        }
        else if (draw)
        {
            // No data: draw empty grid
            for (int i = 0; i < 30; i++)
            {
                int col = i % cols;
                int row = i / cols;
                int cx = x + col * (cellSize + gap);
                int cy = y + row * (cellSize + gap);
                using var brush = new SolidBrush(theme.Track);
                Shapes.FillRounded(g, brush, new Rectangle(cx, cy, cellSize, cellSize), Dpi.Scale(3));
            }
        }
        y += rows * (cellSize + gap);

        // Legend: "menos □□□□□□ mais"
        if (draw)
        {
            int legendCellSize = Dpi.Scale(10);
            int legendGap = Dpi.Scale(3);
            using var mutedBrush = new SolidBrush(theme.TextMuted);
            using var legendFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);

            string less = "menos";
            string more = "mais";
            var lessSz = g.MeasureString(less, legendFont);
            var moreSz = g.MeasureString(more, legendFont);
            int legendW = (int)lessSz.Width + legendGap + 6 * (legendCellSize + legendGap) + (int)moreSz.Width;
            int lx = x + w - legendW;

            g.DrawString(less, legendFont, mutedBrush, lx, y);
            lx += (int)lessSz.Width + legendGap;

            // 6 levels: empty, l1..l5
            double[] levels = { 0, 0.1, 0.3, 0.5, 0.7, 1.0 };
            foreach (var level in levels)
            {
                Color c = level <= 0 ? theme.Track : HeatColorFromLevel(level, theme);
                using var cb = new SolidBrush(c);
                Shapes.FillRounded(g, cb, new Rectangle(lx, y + 1, legendCellSize, legendCellSize), Dpi.Scale(2));
                lx += legendCellSize + legendGap;
            }
            g.DrawString(more, legendFont, mutedBrush, lx, y);
        }
        y += Dpi.Scale(14);
        return y;
    }

    /// <summary>Maps a token count to a heatmap color (5 levels of opacity on theme.Ok).</summary>
    private static Color HeatColor(long tokens, long max, Theme theme)
    {
        if (tokens <= 0 || max <= 0) return theme.Track;
        double ratio = (double)tokens / max;
        return HeatColorFromLevel(ratio, theme);
    }

    private static Color HeatColorFromLevel(double level, Theme theme)
    {
        int alpha = level switch
        {
            < 0.2 => 50,
            < 0.4 => 90,
            < 0.6 => 140,
            < 0.8 => 190,
            _ => 240
        };
        return Color.FromArgb(alpha, theme.Ok);
    }
}
