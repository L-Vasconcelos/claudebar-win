using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Borderless popup near the tray. Shows real 5h/7d quota, per-model weekly limits,
/// local spend estimate, service health, and an integrated usage chart. The window
/// height auto-fits whichever sections are enabled. Theme/position/sticky/on-top configurable.
/// </summary>
public sealed class DashboardForm : Form
{
    private static readonly (ChartRange range, string label)[] Tabs =
    {
        (ChartRange.Hour1, "1H"), (ChartRange.Hours5, "5H"), (ChartRange.Day1, "24H"),
        (ChartRange.Week1, "7D"), (ChartRange.Month1, "30D")
    };

    // Stacked-area series (bottom → top) with their colours.
    private static readonly (string name, Color color)[] Series =
    {
        ("Opus", Color.FromArgb(167, 139, 250)),   // violet
        ("Sonnet", Color.FromArgb(56, 189, 248)),  // sky
        ("Haiku", Color.FromArgb(52, 211, 153)),   // emerald
        ("other", Color.FromArgb(148, 163, 184))   // slate
    };

    private static double SeriesValue(int s, HistoryBucket b) => s switch
    {
        0 => b.Opus, 1 => b.Sonnet, 2 => b.Haiku, _ => b.Other
    };

    private AppSnapshot? _snap;
    private AppConfig _cfg = new();
    private PlanInfo _plan = new("", "");
    private Strings _s = new();
    private Theme _theme = Theme.Dark;
    private readonly System.Windows.Forms.Timer _tick;

    private DateTime _shownAtUtc = DateTime.MinValue;
    private bool _sticky;
    private bool _menuOpen;
    private string _appliedPlacement = "";

    private bool _dragging;
    private Point _dragOffset;
    private Rectangle _closeRect;

    // Chart
    private Func<ChartRange, Task<List<HistoryBucket>>>? _historyProvider;
    private Func<ChartRange, Task<List<PctPoint>>>? _pctProvider;
    private ChartRange _chartRange = ChartRange.Hours5;
    private string _chartMode = "spend";       // "spend" | "percent"
    private string _chartPctWindow = "7d";     // "5h" | "7d"
    private List<HistoryBucket> _chartData = new();
    private List<PctPoint> _pctData = new();
    private bool _chartLoading;
    private readonly Dictionary<ChartRange, Rectangle> _tabRects = new();
    private readonly Dictionary<string, Rectangle> _modeRects = new();   // "spend"/"percent"
    private readonly Dictionary<string, Rectangle> _pctWinRects = new(); // "5h"/"7d"

    public event Action<Point>? Moved;
    public event Action<string>? ChartModeChanged;
    public event Action<string>? ChartWindowChanged;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    public DashboardForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(340, 380);
        BackColor = _theme.Background;
        DoubleBuffered = true;
        TopMost = true;
        Padding = new Padding(18);

        _tick = new System.Windows.Forms.Timer { Interval = 1000 };
        _tick.Tick += (_, _) => { if (Visible) Invalidate(); };
    }

    public void SetHistoryProvider(Func<ChartRange, Task<List<HistoryBucket>>> provider) => _historyProvider = provider;
    public void SetPercentProvider(Func<ChartRange, Task<List<PctPoint>>> provider) => _pctProvider = provider;

    /// <summary>Show the same config menu on right-click, without the auto-hide closing it.</summary>
    public void AttachContextMenu(ContextMenuStrip menu)
    {
        ContextMenuStrip = menu;
        menu.Opening += (_, _) => _menuOpen = true;
        menu.Closed += (_, _) => { _menuOpen = false; _shownAtUtc = DateTime.UtcNow; };
    }

    public void UpdateData(AppSnapshot snap, AppConfig cfg, PlanInfo plan)
    {
        _snap = snap;
        _cfg = cfg;
        _plan = plan;
        _s = Localization.ForConfig(cfg);
        _theme = ThemeResolver.Resolve(cfg);
        _sticky = cfg.DashboardSticky;
        TopMost = cfg.DashboardAlwaysOnTop;
        Opacity = Math.Clamp(cfg.DashboardOpacity, 0.3, 1.0);
        _chartMode = cfg.ChartMode;
        _chartPctWindow = cfg.ChartPctWindow;
        BackColor = _theme.Background;

        if (IsHandleCreated)
            BeginInvoke(() =>
            {
                Relayout();
                Invalidate();
                if (Visible && cfg.ShowChart) _ = ReloadChart();
            });
    }

    public void ShowConfigured(AppConfig cfg)
    {
        _cfg = cfg;
        _s = Localization.ForConfig(cfg);
        _theme = ThemeResolver.Resolve(cfg);
        _sticky = cfg.DashboardSticky;
        TopMost = cfg.DashboardAlwaysOnTop;
        Opacity = Math.Clamp(cfg.DashboardOpacity, 0.3, 1.0);
        _chartMode = cfg.ChartMode;
        _chartPctWindow = cfg.ChartPctWindow;
        BackColor = _theme.Background;
        Relayout();
        _appliedPlacement = PlacementKey(cfg);
        _shownAtUtc = DateTime.UtcNow;
        Show();
        BringToFront();
        Activate();
        SetForegroundWindow(Handle);
        _tick.Start();
        if (cfg.ShowChart) _ = ReloadChart();
    }

    /// <summary>For offline rendering (render-test): set everything synchronously and size to fit.</summary>
    public void PrepareForRender(AppSnapshot snap, AppConfig cfg, PlanInfo plan,
        List<HistoryBucket> buckets, List<PctPoint> pct, ChartRange range)
    {
        _snap = snap;
        _cfg = cfg;
        _plan = plan;
        _s = Localization.ForConfig(cfg);
        _theme = ThemeResolver.Resolve(cfg);
        BackColor = _theme.Background;
        _chartMode = cfg.ChartMode;
        _chartPctWindow = cfg.ChartPctWindow;
        _chartData = buckets;
        _pctData = pct;
        _chartRange = range;
        _chartLoading = false;
        _ = Handle;
        using var g = CreateGraphics();
        Height = LayoutContent(g, draw: false);
    }

    private async Task ReloadChart()
    {
        if (_chartMode == "percent")
        {
            if (_pctProvider is null) return;
            _chartLoading = _pctData.Count == 0;
            if (_chartLoading && IsHandleCreated) BeginInvoke(Invalidate);
            try { _pctData = await _pctProvider(_chartRange); }
            catch { _pctData = new(); }
        }
        else
        {
            if (_historyProvider is null) return;
            _chartLoading = _chartData.Count == 0;
            if (_chartLoading && IsHandleCreated) BeginInvoke(Invalidate);
            try { _chartData = await _historyProvider(_chartRange); }
            catch { _chartData = new(); }
        }
        _chartLoading = false;
        if (IsHandleCreated) BeginInvoke(() => { Relayout(); Invalidate(); });
    }

    // ---------- placement & auto-size ----------

    private void Relayout()
    {
        if (!IsHandleCreated) return;
        int needed;
        using (var g = CreateGraphics())
            needed = LayoutContent(g, draw: false);
        if (Height != needed) Height = needed;
        PlaceWindow(_cfg);
        _appliedPlacement = PlacementKey(_cfg);
    }

    private static string PlacementKey(AppConfig cfg) => $"{cfg.DashboardPosition}|{cfg.DashboardX}|{cfg.DashboardY}";

    private void PlaceWindow(AppConfig cfg)
    {
        var wa = Screen.PrimaryScreen!.WorkingArea;
        const int m = 8;
        Location = cfg.DashboardPosition switch
        {
            "BottomLeft" => new Point(wa.Left + m, wa.Bottom - Height - m),
            "TopRight" => new Point(wa.Right - Width - m, wa.Top + m),
            "TopLeft" => new Point(wa.Left + m, wa.Top + m),
            "Center" => new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 2),
            "Custom" when cfg.DashboardX >= 0 && cfg.DashboardY >= 0 => ClampToScreen(new Point(cfg.DashboardX, cfg.DashboardY)),
            _ => new Point(wa.Right - Width - m, wa.Bottom - Height - m)
        };
    }

    private Point ClampToScreen(Point p)
    {
        var screen = Screen.FromPoint(p) ?? Screen.PrimaryScreen!;
        var wa = screen.WorkingArea;
        int x = Math.Clamp(p.X, wa.Left, Math.Max(wa.Left, wa.Right - Width));
        int y = Math.Clamp(p.Y, wa.Top, Math.Max(wa.Top, wa.Bottom - Height));
        return new Point(x, y);
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (!Visible) _tick.Stop();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_menuOpen) return; // config menu is open over the panel
        if (_sticky) return;
        if ((DateTime.UtcNow - _shownAtUtc).TotalMilliseconds < 600) return;
        Hide();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CS_DROPSHADOW = 0x00020000;
            var cp = base.CreateParams;
            cp.ClassStyle |= CS_DROPSHADOW;
            return cp;
        }
    }

    // ---------- input ----------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_closeRect.Contains(e.Location)) { Hide(); return; }

        // Mode toggle ($ / %)
        foreach (var (mode, rect) in _modeRects)
        {
            if (rect.Contains(e.Location))
            {
                if (_chartMode != mode)
                {
                    _chartMode = mode;
                    _chartData = new(); _pctData = new();
                    ChartModeChanged?.Invoke(mode);
                    _ = ReloadChart();
                }
                return;
            }
        }

        // Percent window selector (5h / 7d)
        foreach (var (win, rect) in _pctWinRects)
        {
            if (rect.Contains(e.Location))
            {
                if (_chartPctWindow != win)
                {
                    _chartPctWindow = win;
                    ChartWindowChanged?.Invoke(win);
                    if (IsHandleCreated) BeginInvoke(Invalidate); // same data, different series
                }
                return;
            }
        }

        foreach (var (range, rect) in _tabRects)
        {
            if (rect.Contains(e.Location))
            {
                if (_chartRange != range) { _chartRange = range; _chartData = new(); _pctData = new(); _ = ReloadChart(); }
                return;
            }
        }

        _dragging = true;
        _dragOffset = new Point(Cursor.Position.X - Location.X, Cursor.Position.Y - Location.Y);
        Cursor = Cursors.SizeAll;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
            return;
        }
        // Hand over interactive elements (close ✕, tabs, mode/window toggles); normal arrow elsewhere.
        bool overClickable = _closeRect.Contains(e.Location)
            || _tabRects.Values.Any(r => r.Contains(e.Location))
            || _modeRects.Values.Any(r => r.Contains(e.Location))
            || _pctWinRects.Values.Any(r => r.Contains(e.Location));
        Cursor = overClickable ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        Cursor = Cursors.Default;
        if (!_dragging) return;
        _dragging = false;
        // Remember the dragged spot locally so a later chart change (Relayout/PlaceWindow)
        // doesn't snap the panel back to the old configured position.
        _cfg.DashboardPosition = "Custom";
        _cfg.DashboardX = Location.X;
        _cfg.DashboardY = Location.Y;
        _appliedPlacement = $"Custom|{Location.X}|{Location.Y}";
        Moved?.Invoke(Location);
    }

    // ---------- paint / layout ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        LayoutContent(e.Graphics, draw: true);
    }

    /// <summary>Walks the sections top-to-bottom. Returns the required window height.</summary>
    private int LayoutContent(Graphics g, bool draw)
    {
        using var titleFont = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var planFont = new Font("Segoe UI", 8.5f);
        using var labelFont = new Font("Segoe UI", 9.5f);
        using var smallFont = new Font("Segoe UI", 8f);
        using var tabFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var fg = new SolidBrush(_theme.Foreground);
        using var dim = new SolidBrush(_theme.Dim);

        int x = Padding.Left;
        int y = Padding.Top;
        int w = Width - Padding.Horizontal;

        if (draw)
        {
            g.DrawString("ClaudeBar", titleFont, fg, x, y);
            g.DrawString(_plan.Display, planFont, dim, x, y + 24);
            _closeRect = new Rectangle(Width - 26, 10, 18, 18);
            using var closeFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            g.DrawString("✕", closeFont, dim, _closeRect.X, _closeRect.Y - 2);

            // Service health — top-right, on the plan line.
            if (_cfg.ShowHealth && _snap?.Health is { } h)
            {
                (string hl, Color hc) = h.Level switch
                {
                    HealthLevel.Operational => (_s.HealthOk, _theme.Ok),
                    HealthLevel.Degraded => (_s.HealthDegraded, _theme.Warn),
                    HealthLevel.Outage => (_s.HealthOutage, _theme.Critical),
                    _ => (h.Description, _theme.Dim)
                };
                var hsz = g.MeasureString(hl, planFont);
                float hx = x + w - hsz.Width;
                using var hb = new SolidBrush(hc);
                g.FillEllipse(hb, hx - 13, y + 24 + 4, 8, 8);
                g.DrawString(hl, planFont, dim, hx, y + 24);
            }
        }
        y += 50;

        var usage = _snap?.Usage;
        if (usage is null)
        {
            if (draw)
            {
                string msg = _snap is null ? _s.Loading : UsageFormat.StateMessage(_snap.LatestState, _s);
                g.DrawString(msg, labelFont, dim, x, y);
            }
            y += 24;
            return y + 14;
        }

        y = DrawBar(g, draw, $"{_s.SessionWord} (5h)", usage.FiveHour, _snap?.PaceFive?.Status, x, y, w, labelFont, smallFont, fg, dim);
        y += 16;
        y = DrawBar(g, draw, $"{_s.WeekWord} (7d)", usage.SevenDay, _snap?.PaceSeven?.Status, x, y, w, labelFont, smallFont, fg, dim);
        y += 14;

        y = DrawPace(g, draw, x, y, w, smallFont);

        y = DrawModelLine(g, draw, "Opus 7d", usage.SevenDayOpus, x, y, w, smallFont, fg, dim);
        y = DrawModelLine(g, draw, "Sonnet 7d", usage.SevenDaySonnet, x, y, w, smallFont, fg, dim);

        if (_cfg.ShowSpendEstimate && _snap?.Spend is { } spend && spend.CostByModel.Count > 0)
        {
            y += 8;
            if (draw) g.DrawString(string.Format(_s.SpendHeaderFormat, _snap.SpendDays), smallFont, dim, x, y);
            y += 18;
            foreach (var kv in spend.CostByModel.OrderByDescending(k => k.Value))
            {
                if (draw)
                {
                    g.DrawString(kv.Key, labelFont, fg, x, y);
                    string val = $"${kv.Value:0.00}";
                    var sz = g.MeasureString(val, labelFont);
                    g.DrawString(val, labelFont, dim, x + w - sz.Width, y);
                }
                y += 20;
            }
        }

        if (_cfg.ShowChart)
        {
            y += 6;
            y = DrawChart(g, draw, x, y, w, smallFont, tabFont, fg, dim);
        }
        else
        {
            _tabRects.Clear();
        }

        // footer
        y += 4;
        if (draw)
        {
            string footer;
            if (_snap is not null && _snap.LatestState != UsageFetchState.Ok)
                footer = $"⚠ {UsageFormat.StateMessage(_snap.LatestState, _s)} · {_s.PreviousDataFooter}";
            else
            {
                string hint = _sticky ? _s.HintPinnedClose : _s.HintClickToHide;
                footer = $"{_s.UpdatedAt} {_snap!.UsageAtUtc.ToLocalTime():HH:mm:ss} · {hint}";
            }
            g.DrawString(footer, smallFont, dim, x, y);
        }
        return y + 18;
    }

    private int DrawPace(Graphics g, bool draw, int x, int y, int w, Font smallFont)
    {
        var pf = _snap?.PaceFive;
        var ps = _snap?.PaceSeven;
        if (pf is null && ps is null) return y;
        if (!draw) return y + 18;

        var worst = (PaceStatus)Math.Max((int)(pf?.Status ?? PaceStatus.Ok), (int)(ps?.Status ?? PaceStatus.Ok));
        Color c = worst == PaceStatus.Critical ? _theme.Critical
                : worst == PaceStatus.Over ? _theme.Warn : _theme.Ok;

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

    private int DrawBar(Graphics g, bool draw, string label, UsageWindow? win, PaceStatus? pace, int x, int y, int w,
        Font labelFont, Font smallFont, Brush fg, Brush dim)
    {
        double util = win?.UtilizationPct ?? 0;
        double clamped = Math.Min(util / 100.0, 1.0);
        // Colour the section by PACE status (better/worse rate); fall back to level threshold.
        Color c = pace is { } ps
            ? (ps == PaceStatus.Critical ? _theme.Critical : ps == PaceStatus.Over ? _theme.Warn : _theme.Ok)
            : (util >= _cfg.CriticalThresholdPct ? _theme.Critical
                : util >= _cfg.WarnThresholdPct ? _theme.Warn : _theme.Ok);

        if (draw)
        {
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
            using var trackBrush = new SolidBrush(_theme.Track);
            FillRounded(g, trackBrush, new Rectangle(x, y, w, barH), 5);
            int fw = (int)Math.Round(w * clamped);
            if (fw > 1)
            {
                using var fillBrush = new SolidBrush(c);
                FillRounded(g, fillBrush, new Rectangle(x, y, fw, barH), 5);
            }
        }
        y += barH + 3;

        string cd = UsageFormat.Countdown(win?.ResetsAt, _s.Resetting);
        if (draw && cd.Length > 0)
            g.DrawString($"{_s.ResetsIn} {cd}", smallFont, dim, x, y);
        return y + 14;
    }

    private int DrawModelLine(Graphics g, bool draw, string label, UsageWindow? win, int x, int y, int w,
        Font smallFont, Brush fg, Brush dim)
    {
        if (win is null) return y;
        if (draw)
        {
            g.DrawString(label, smallFont, dim, x, y);
            string val = $"{win.UtilizationPct:0.#}%";
            var sz = g.MeasureString(val, smallFont);
            g.DrawString(val, smallFont, fg, x + w - sz.Width, y);
        }
        return y + 16;
    }

    private const int ChartH = 92;
    private const int ChartFooter = 32;

    private int DrawChart(Graphics g, bool draw, int x, int y, int w, Font smallFont, Font tabFont, Brush fg, Brush dim)
    {
        bool pct = _chartMode == "percent";

        // Title + mode toggle (Spend $ | Quota %)
        if (draw) g.DrawString(_s.UsageChart, smallFont, dim, x, y);
        _modeRects.Clear();
        DrawSegments(g, draw, tabFont,
            new[] { (_s.ChartTabSpend, "spend"), (_s.ChartTabPct, "percent") }, _chartMode,
            x + w, y - 1, rightAlign: true, _modeRects);
        y += 18;

        // Range tabs (left)
        _tabRects.Clear();
        int tx = x;
        foreach (var (range, label) in Tabs)
        {
            var sz = g.MeasureString(label, tabFont);
            var rect = new Rectangle(tx, y, (int)sz.Width + 12, 20);
            if (draw)
            {
                bool active = range == _chartRange;
                using var bg = new SolidBrush(active ? _theme.Ok : _theme.Track);
                FillRounded(g, bg, rect, 5);
                using var tb = new SolidBrush(active ? Pick(_theme.Background) : _theme.Foreground);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, tabFont, tb, rect, sf);
            }
            _tabRects[range] = rect;
            tx += rect.Width + 4;
        }
        // Window selector [5h|7d] (right, only in percent mode)
        _pctWinRects.Clear();
        if (pct)
            DrawSegments(g, draw, tabFont,
                new[] { ("5h", "5h"), ("7d", "7d") }, _chartPctWindow,
                x + w, y, rightAlign: true, _pctWinRects);
        y += 26;

        return pct
            ? DrawPercentBody(g, draw, x, y, w, smallFont, dim)
            : DrawSpendBody(g, draw, x, y, w, smallFont, dim);
    }

    private void DrawSegments(Graphics g, bool draw, Font font,
        (string label, string key)[] segs, string activeKey, int anchorX, int y,
        bool rightAlign, Dictionary<string, Rectangle> rects)
    {
        const int gap = 3, h = 18, padX = 7;
        var widths = segs.Select(s => (int)g.MeasureString(s.label, font).Width + padX * 2).ToArray();
        int total = widths.Sum() + gap * (segs.Length - 1);
        int sx = rightAlign ? anchorX - total : anchorX;
        for (int i = 0; i < segs.Length; i++)
        {
            var (label, key) = segs[i];
            var rect = new Rectangle(sx, y, widths[i], h);
            if (draw)
            {
                bool active = key == activeKey;
                using var bg = new SolidBrush(active ? _theme.Ok : _theme.Track);
                FillRounded(g, bg, rect, 4);
                using var tb = new SolidBrush(active ? Pick(_theme.Background) : _theme.Foreground);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(label, font, tb, rect, sf);
            }
            rects[key] = rect;
            sx += widths[i] + gap;
        }
    }

    private int DrawSpendBody(Graphics g, bool draw, int x, int top, int w, Font smallFont, Brush dim)
    {
        int bottom = top + ChartH;
        if (_chartLoading)
        {
            if (draw) g.DrawString("…", smallFont, dim, x, top + ChartH / 2);
            return bottom + ChartFooter;
        }

        int n = _chartData.Count;
        double max = n > 0 ? _chartData.Max(b => b.CostUsd) : 0;
        if (n == 0 || max <= 0)
        {
            if (draw) g.DrawString(_s.NoData, smallFont, dim, x, top + ChartH / 2 - 6);
            return bottom + ChartFooter;
        }
        if (!draw) return bottom + ChartFooter;

        double total = _chartData.Sum(b => b.CostUsd);
        g.DrawString($"{_s.ChartTotal} ${total:0.00}", smallFont, dim, x, top - 1);

        float X(int i) => n == 1 ? x + w / 2f : x + (float)i * w / (n - 1);
        float Y(double v) => bottom - (float)(v / max) * (ChartH - 14);

        var baseline = new double[n];
        for (int sIdx = 0; sIdx < Series.Length; sIdx++)
        {
            bool any = false;
            var topArr = new double[n];
            for (int i = 0; i < n; i++)
            {
                double v = SeriesValue(sIdx, _chartData[i]);
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
            if (_chartData[i].CostUsd > _chartData[peakIdx].CostUsd) peakIdx = i;
        AnnotatePeak(g, smallFont, $"{_s.ChartPeak} ${max:0.00}", X(peakIdx), Y(max), x, w, top);

        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / 8.0));
        for (int i = 0; i < n; i += labelEvery)
        {
            var lbl = _chartData[i].Label;
            var lsz = g.MeasureString(lbl, smallFont);
            g.DrawString(lbl, smallFont, dim, X(i) - lsz.Width / 2f, bottom + 2);
        }

        int lx = x, ly = bottom + 16;
        for (int sIdx = 0; sIdx < Series.Length; sIdx++)
        {
            bool any = false;
            for (int i = 0; i < n; i++) if (SeriesValue(sIdx, _chartData[i]) > 0) { any = true; break; }
            if (!any) continue;
            using var sw = new SolidBrush(Series[sIdx].color);
            g.FillRectangle(sw, lx, ly + 2, 9, 9);
            g.DrawString(Series[sIdx].name, smallFont, dim, lx + 12, ly);
            lx += 12 + (int)g.MeasureString(Series[sIdx].name, smallFont).Width + 10;
        }
        return bottom + ChartFooter;
    }

    private int DrawPercentBody(Graphics g, bool draw, int x, int top, int w, Font smallFont, Brush dim)
    {
        int bottom = top + ChartH;
        if (_chartLoading)
        {
            if (draw) g.DrawString("…", smallFont, dim, x, top + ChartH / 2);
            return bottom + ChartFooter;
        }

        Func<PctPoint, double?> sel = _chartPctWindow == "5h" ? p => p.FivePct : p => p.SevenPct;
        var pts = _pctData.Where(p => sel(p) is not null)
            .Select(p => (p.TsUtc, v: sel(p)!.Value)).ToList();

        if (pts.Count == 0)
        {
            if (draw) g.DrawString(_s.NoData, smallFont, dim, x, top + ChartH / 2 - 6);
            return bottom + ChartFooter;
        }
        if (!draw) return bottom + ChartFooter;

        int n = pts.Count;
        double peak = pts.Max(p => p.v);
        double current = pts[^1].v;

        float X(int i) => n == 1 ? x + w / 2f : x + (float)i * w / (n - 1);
        float Y(double v) => bottom - (float)(Math.Clamp(v, 0, 100) / 100.0) * (ChartH - 14);

        Color status = peak >= _cfg.CriticalThresholdPct ? _theme.Critical
                     : peak >= _cfg.WarnThresholdPct ? _theme.Warn
                     : _theme.Ok;

        // current value (top-left)
        g.DrawString($"{current:0.#}%", smallFont, dim, x, top - 1);

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
        AnnotatePeak(g, smallFont, $"{_s.ChartPeak} {peak:0.#}%", X(peakIdx), Y(peak), x, w, top);

        // x-axis time labels
        int labelEvery = Math.Max(1, (int)Math.Ceiling(n / 6.0));
        bool longRange = _chartRange is ChartRange.Week1 or ChartRange.Month1;
        for (int i = 0; i < n; i += labelEvery)
        {
            string lbl = pts[i].TsUtc.ToLocalTime().ToString(longRange ? "dd/MM" : "HH:mm");
            var lsz = g.MeasureString(lbl, smallFont);
            g.DrawString(lbl, smallFont, dim, X(i) - lsz.Width / 2f, bottom + 2);
        }
        return bottom + ChartFooter;
    }

    private void AnnotatePeak(Graphics g, Font font, string text, float peakX, float peakY, int x, int w, int top)
    {
        using var marker = new SolidBrush(_theme.Foreground);
        g.FillEllipse(marker, peakX - 2.5f, peakY - 2.5f, 5, 5);
        var psz = g.MeasureString(text, font);
        float pxp = Math.Clamp(peakX - psz.Width / 2f, x, x + w - psz.Width);
        g.DrawString(text, font, marker, pxp, Math.Max(top - 2, peakY - psz.Height - 1));
    }

    private Color Pick(Color bg)
        => (bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114) < 140 ? Color.White : Color.Black;

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
