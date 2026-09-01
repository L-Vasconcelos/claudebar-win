using System.Drawing.Drawing2D;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Secondary borderless panel that docks to the right of the main DashboardForm.
/// Shows: spend by model (bars), usage chart over time, and model breakdown.
/// </summary>
public sealed class DetailPanel : Form
{
    private Theme _theme = Theme.Dark;
    private Strings _s = new();
    private WindowStats? _spend;
    private List<HistoryBucket> _chartData = new();
    private IReadOnlyList<UsageRecord> _records = Array.Empty<UsageRecord>();
    private string _chartRange = "5h"; // "1h"|"5h"|"24h"|"7d"
    private readonly Dictionary<string, Rectangle> _rangeTabRects = new();
    private Rectangle _closeRect;

    public DetailPanel()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        TopMost = true;
        BackColor = _theme.Background;
    }

    public event Action<string>? ChartRangeChanged;

    public void UpdateData(Theme theme, Strings s, WindowStats? spend,
        List<HistoryBucket> chartData, IReadOnlyList<UsageRecord> records)
    {
        _theme = theme;
        _s = s;
        _spend = spend;
        _chartData = chartData;
        _records = records;
        BackColor = _theme.Background;
        if (IsHandleCreated) BeginInvoke(() => { Relayout(); Invalidate(); });
    }

    public void UpdateChart(List<HistoryBucket> chartData)
    {
        _chartData = chartData;
        if (IsHandleCreated) BeginInvoke(Invalidate);
    }

    /// <summary>Position to the right of the main dashboard, sharing the top edge.</summary>
    public void DockTo(Form main)
    {
        // Use available screen space to the right, min = main width, max = screen edge
        var screen = Screen.FromControl(main).WorkingArea;
        int availW = screen.Right - main.Right;
        int desiredW = Math.Max(main.Width, Dpi.Scale(380));
        Width = Math.Min(desiredW, Math.Max(availW, Dpi.Scale(280)));
        Location = new Point(main.Right, main.Top);
        Relayout();
    }

    private void Relayout()
    {
        if (!IsHandleCreated) return;
        using var g = CreateGraphics();
        int h = LayoutContent(g, draw: false);
        Height = h;
        ApplyRoundedRegion();
    }

    private void ApplyRoundedRegion()
    {
        int radius = Dpi.Scale(12);
        using var path = Shapes.RoundedRectPath(new Rectangle(0, 0, Width, Height), radius);
        Region = new Region(path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        LayoutContent(e.Graphics, draw: true);
        // Border
        using var pen = new Pen(_theme.AccentText, 1f);
        Shapes.DrawRounded(e.Graphics, pen, new Rectangle(0, 0, Width, Height), Dpi.Scale(12));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_closeRect.Contains(e.Location)) { Hide(); return; }
        foreach (var (range, rect) in _rangeTabRects)
        {
            if (rect.Contains(e.Location))
            {
                if (_chartRange != range) { _chartRange = range; ChartRangeChanged?.Invoke(range); }
                Invalidate();
                return;
            }
        }
    }

    private int LayoutContent(Graphics g, bool draw)
    {
        bool scaleFonts = Math.Abs(Dpi.UserScale - 1f) >= 0.001f;
        Font smallFont = scaleFonts ? new Font(Typography.Caption.FontFamily, Typography.Caption.SizeInPoints * Dpi.UserScale, Typography.Caption.Style, GraphicsUnit.Point) : Typography.Caption;
        using var fg = new SolidBrush(_theme.TextPrimary);
        using var dim = new SolidBrush(_theme.TextSecondary);
        using var muted = new SolidBrush(_theme.TextMuted);

        int pad = Dpi.Scale(14);
        int x = pad;
        int y = pad;
        int w = Width - pad * 2;

        // T14: Typography.Title escalado por UserScale para acompanhar o resize.
        using var scaledTitle = scaleFonts ? new Font(Typography.Title.FontFamily, Typography.Title.SizeInPoints * Dpi.UserScale, Typography.Title.Style, GraphicsUnit.Point) : null;
        Font titleFont = scaledTitle ?? Typography.Title;
        // T14: Typography.Mono escalado por UserScale.
        using var scaledMono = scaleFonts ? new Font(Typography.Mono.FontFamily, Typography.Mono.SizeInPoints * Dpi.UserScale, Typography.Mono.Style, GraphicsUnit.Point) : null;
        Font monoFont = scaledMono ?? Typography.Mono;
        // T14: Typography.Caption escalado para DrawSegments.
        using var scaledCaption = scaleFonts ? new Font(Typography.Caption.FontFamily, Typography.Caption.SizeInPoints * Dpi.UserScale, Typography.Caption.Style, GraphicsUnit.Point) : null;
        Font captionFont = scaledCaption ?? Typography.Caption;

        // --- Header: "Detalhes" + ✕ ---
        _closeRect = new Rectangle(Width - Dpi.Scale(26), Dpi.Scale(10), Dpi.Scale(18), Dpi.Scale(18));
        if (draw)
        {
            g.DrawString("Detalhes", titleFont, fg, x, y);
            using var closeFont = new Font("Segoe UI", 11f * Dpi.UserScale, FontStyle.Bold);
            g.DrawString("✕", closeFont, dim, _closeRect.X, _closeRect.Y - 2);
        }
        y += Dpi.Scale(28);

        if (draw)
        {
            using var sepPen = new Pen(_theme.Separator);
            g.DrawLine(sepPen, x, y, x + w, y);
        }
        y += Dpi.Scale(8);

        // --- Spend by model (bars) ---
        y = DrawSpendBars(g, draw, x, y, w, smallFont, monoFont, dim, muted);

        // --- Chart ---
        y = DrawChart(g, draw, x, y, w, smallFont, captionFont, dim, muted);

        // --- Model breakdown ---
        y = DrawModelBreakdown(g, draw, x, y, w, smallFont, dim, muted);

        y += pad;

        if (scaleFonts) smallFont.Dispose();
        return y;
    }

    private int DrawSpendBars(Graphics g, bool draw, int x, int y, int w, Font smallFont, Font monoFont, Brush dim, Brush muted)
    {
        if (draw)
        {
            using var labelFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
            using var lb = new SolidBrush(_theme.TextMuted);
            g.DrawString("GASTO ESTIMADO (7D, API-EQUIV)", labelFont, lb, x, y);
        }
        y += Dpi.Scale(16);

        if (_spend is null || _spend.CostByModel.Count == 0)
        {
            if (draw) g.DrawString(_s.Loading, smallFont, dim, x, y);
            return y + Dpi.Scale(20);
        }

        double maxCost = _spend.CostByModel.Values.Max();
        var seriesAssigner = DashboardDataView.SeriesAssigner;
        var families = _spend.CostByModel.OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
        var slots = seriesAssigner.Assign(families);
        int barH = Dpi.Scale(14);
        int rowH = Dpi.Scale(22);
        int modelW = Dpi.Scale(46);
        int amtW = Dpi.Scale(56);
        int barGap = Dpi.Scale(6);

        foreach (var family in families)
        {
            double cost = _spend.CostByModel.GetValueOrDefault(family);
            Color seriesColor = _theme.ChartSeries[slots[family] % _theme.ChartSeries.Count];
            if (draw)
            {
                // Model name
                using var modelBrush = new SolidBrush(seriesColor);
                string name = DashboardDataView.FamilyLabel(family, _s);
                g.DrawString(name, smallFont, modelBrush, x, y + 1);

                // Bar
                int barX = x + modelW + barGap;
                int barW = w - modelW - barGap - amtW - barGap;
                using var trackBrush = new SolidBrush(_theme.Track);
                Shapes.FillRounded(g, trackBrush, new Rectangle(barX, y + 1, barW, barH), Dpi.Scale(3));
                int fillW = maxCost > 0 ? (int)(barW * cost / maxCost) : 0;
                if (fillW > 1)
                {
                    using var fillBrush = new SolidBrush(seriesColor);
                    Shapes.FillRounded(g, fillBrush, new Rectangle(barX, y + 1, fillW, barH), Dpi.Scale(3));
                }

                // Amount
                string amt = UsageFormat.Money(cost, _s.Culture);
                int amtX = x + w - TextMetrics.MeasureWidth(g, amt, monoFont);
                g.DrawString(amt, monoFont, dim, amtX, y + 1, TextMetrics.Typographic);
            }
            y += rowH;
        }
        y += Dpi.Scale(6);
        return y;
    }

    private int DrawChart(Graphics g, bool draw, int x, int y, int w, Font smallFont, Font captionFont, Brush dim, Brush muted)
    {
        if (draw)
        {
            using var sepPen = new Pen(_theme.Separator);
            g.DrawLine(sepPen, x, y, x + w, y);
        }
        y += Dpi.Scale(8);

        // Header + tabs
        if (draw)
        {
            using var labelFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
            using var lb = new SolidBrush(_theme.TextMuted);
            g.DrawString("USO AO LONGO DO TEMPO", labelFont, lb, x, y);
        }
        // Range tabs right-aligned
        _rangeTabRects.Clear();
        var tabs = new[] { ("1H", "1h"), ("5H", "5h"), ("24H", "24h"), ("7D", "7d") };
        DashboardDataView.DrawSegments(g, draw, captionFont, _theme,
            tabs, _chartRange, x + w, y, rightAlign: true, _rangeTabRects);
        y += Dpi.Scale(20);

        // Chart area
        int chartH = Dpi.Scale(100);
        int n = _chartData.Count;
        double max = n > 0 ? _chartData.Max(b => b.CostUsd) : 0;

        if (n == 0 || max <= 0)
        {
            if (draw) g.DrawString(_s.Loading, smallFont, dim, x, y + chartH / 2 - 6);
            y += chartH;
        }
        else if (draw)
        {
            float X(int i) => n == 1 ? x + w / 2f : x + (float)i * w / (n - 1);
            float Y(double v) => y + chartH - (float)(v / max) * (chartH - Dpi.Scale(10));

            // Grid lines
            using (var grid = new Pen(_theme.Separator, 1f) { DashStyle = DashStyle.Dash })
                foreach (double frac in new[] { 0.25, 0.5, 0.75 })
                    g.DrawLine(grid, x, Y(max * frac), x + w, Y(max * frac));

            // Area fill
            var families = DashboardDataView.ChartFamilies(_chartData);
            var slots = DashboardDataView.SeriesAssigner.Assign(families);
            var baseline = new double[n];
            foreach (var family in families)
            {
                var topArr = new double[n];
                for (int i = 0; i < n; i++) topArr[i] = baseline[i] + _chartData[i].Cost(family);
                var pts = new List<PointF>(2 * n);
                for (int i = 0; i < n; i++) pts.Add(new PointF(X(i), Y(topArr[i])));
                for (int i = n - 1; i >= 0; i--) pts.Add(new PointF(X(i), Y(baseline[i])));
                Color sc = _theme.ChartSeries[slots[family] % _theme.ChartSeries.Count];
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
            y += chartH;

            // X-axis labels
            int labelEvery = Math.Max(1, n / 5);
            for (int i = 0; i < n; i += labelEvery)
            {
                var lbl = _chartData[i].Label;
                var lsz = g.MeasureString(lbl, smallFont);
                float lx = DashboardDataView.AxisLabelX(X(i), lsz.Width, x, x + w);
                g.DrawString(lbl, smallFont, muted, lx, y + 2);
            }
        }
        else
        {
            y += chartH;
        }
        y += Dpi.Scale(16);
        return y;
    }

    private int DrawModelBreakdown(Graphics g, bool draw, int x, int y, int w, Font smallFont, Brush dim, Brush muted)
    {
        if (draw)
        {
            using var sepPen = new Pen(_theme.Separator);
            g.DrawLine(sepPen, x, y, x + w, y);
        }
        y += Dpi.Scale(8);

        if (draw)
        {
            using var labelFont = new Font(smallFont.FontFamily, 8f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
            using var lb = new SolidBrush(_theme.TextMuted);
            g.DrawString("USO POR MODELO", labelFont, lb, x, y);
        }
        y += Dpi.Scale(16);

        // Group records by model family
        var familyGroups = _records
            .GroupBy(r => ModelFamily.FromId(r.Model))
            .Where(g2 => g2.Key != ModelFamily.Other)
            .OrderByDescending(g2 => g2.Count())
            .Take(4)
            .ToList();

        int totalMsgs = _records.Count;
        if (familyGroups.Count == 0)
        {
            if (draw) g.DrawString(_s.Loading, smallFont, dim, x, y);
            return y + Dpi.Scale(20);
        }

        var seriesAssigner = DashboardDataView.SeriesAssigner;
        var familyNames = familyGroups.Select(fg => fg.Key).ToList();
        var slots = seriesAssigner.Assign(familyNames);
        int rowH = Dpi.Scale(32);
        int dotSize = Dpi.Scale(8);

        foreach (var grp in familyGroups)
        {
            Color sc = _theme.ChartSeries[slots[grp.Key] % _theme.ChartSeries.Count];
            int msgs = grp.Count();
            long tokens = grp.Sum(r => r.TotalTokens);
            int pct = totalMsgs > 0 ? (int)Math.Round(100.0 * msgs / totalMsgs) : 0;

            if (draw)
            {
                // Dot
                using var dotBrush = new SolidBrush(sc);
                g.FillEllipse(dotBrush, x, y + (rowH - dotSize) / 2, dotSize, dotSize);

                // Name
                int textX = x + dotSize + Dpi.Scale(6);
                using var nameBrush = new SolidBrush(_theme.TextPrimary);
                using var nameFont = new Font(smallFont.FontFamily, 10f * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);
                g.DrawString(grp.Key, nameFont, nameBrush, textX, y + 1);

                // Sub info
                string sub = $"{UsageStatsService.FormatCount(msgs)} mensagens · {UsageStatsService.FormatTokens(tokens)} tokens";
                using var subFont = new Font(smallFont.FontFamily, 7.5f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
                g.DrawString(sub, subFont, muted, textX, y + Dpi.Scale(14));

                // Percentage (right-aligned)
                string pctText = $"{pct}%";
                using var pctBrush = new SolidBrush(sc);
                using var pctFont = new Font(Typography.Mono.FontFamily, 10f * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);
                int pctW = TextMetrics.MeasureWidth(g, pctText, pctFont);
                g.DrawString(pctText, pctFont, pctBrush, x + w - pctW, y + 4, TextMetrics.Typographic);
            }
            y += rowH;
        }

        return y;
    }
}
