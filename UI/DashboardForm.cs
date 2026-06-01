using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Borderless popup near the tray. Shows real 5h/7d quota, per-model weekly limits,
/// local spend estimate, service health, and an integrated usage chart. The window
/// height auto-fits whichever sections are enabled. Theme/position/sticky/on-top configurable.
/// </summary>
public sealed class DashboardForm : Form
{
    // Datos estáticos del gráfico (Tabs / Series / SeriesValue) movidos a DashboardDataView (Task 5).

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

    // Live sessions (mascot + instance list)
    private Func<LiveSessionsView>? _liveProvider;
    private LiveSessionsView _liveView = new();
    private int _mascotFrame;
    private ChartRange _chartRange = ChartRange.Hours5;
    private string _chartMode = "spend";       // "spend" | "percent"
    private string _chartPctWindow = "7d";     // "5h" | "7d"
    private List<HistoryBucket> _chartData = new();
    private List<PctPoint> _pctData = new();
    private bool _chartLoading;
    private readonly Dictionary<ChartRange, Rectangle> _tabRects = new();
    private readonly Dictionary<string, Rectangle> _modeRects = new();   // "spend"/"percent"
    private readonly Dictionary<string, Rectangle> _pctWinRects = new(); // "5h"/"7d"
    private readonly Dictionary<string, Rectangle> _liveRowRects = new(); // sessionId → row

    public event Action<Point>? Moved;
    public event Action<string>? ChartModeChanged;
    public event Action<string>? ChartWindowChanged;
    public event Action<string>? SessionClicked;

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
        _tick.Tick += (_, _) => { if (Visible) { _mascotFrame++; Invalidate(); } };
    }

    public void SetHistoryProvider(Func<ChartRange, Task<List<HistoryBucket>>> provider) => _historyProvider = provider;
    public void SetPercentProvider(Func<ChartRange, Task<List<PctPoint>>> provider) => _pctProvider = provider;
    public void SetLiveSessionsProvider(Func<LiveSessionsView> provider) => _liveProvider = provider;

    /// <summary>Llamar cuando cambien las sesiones en vivo (desde el hilo de UI vía BeginInvoke).</summary>
    public void OnLiveSessionsChanged()
    {
        if (_liveProvider is not null) _liveView = _liveProvider();
        Relayout();
        Invalidate();
    }

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
        if (_cfg.LiveSessionsEnabled && _liveProvider is not null) _liveView = _liveProvider();
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

        foreach (var (id, rect) in _liveRowRects)
        {
            if (rect.Contains(e.Location)) { SessionClicked?.Invoke(id); return; }
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
            || _pctWinRects.Values.Any(r => r.Contains(e.Location))
            || _liveRowRects.Values.Any(r => r.Contains(e.Location));
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

        if (_cfg.LiveSessionsEnabled)
        {
            y += 8;
            y = DrawLiveSessions(g, draw, x, y, w, smallFont, fg, dim);
        }
        else { _liveRowRects.Clear(); }

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

    // ---------- section drawing: bodies movidos a UI/dashboard/DashboardDataView.cs (Task 5) ----------
    // Estos métodos quedan como finos puentes hacia el renderer sin estado, para no duplicar la lógica
    // de dibujo. Task 7 reconectará LayoutContent directamente a DashboardDataView/DashboardHeader.

    private int DrawPace(Graphics g, bool draw, int x, int y, int w, Font smallFont)
        => DashboardDataView.DrawPace(g, draw, _snap, _theme, x, y, w, smallFont);

    private int DrawBar(Graphics g, bool draw, string label, UsageWindow? win, PaceStatus? pace, int x, int y, int w,
        Font labelFont, Font smallFont, Brush fg, Brush dim)
        => DashboardDataView.DrawBar(g, draw, label, win, pace, x, y, w, _cfg, _s, _theme, labelFont, smallFont, fg, dim);

    private int DrawModelLine(Graphics g, bool draw, string label, UsageWindow? win, int x, int y, int w,
        Font smallFont, Brush fg, Brush dim)
        => DashboardDataView.DrawModelLine(g, draw, label, win, x, y, w, smallFont, fg, dim);

    private int DrawChart(Graphics g, bool draw, int x, int y, int w, Font smallFont, Font tabFont, Brush fg, Brush dim)
        => DashboardDataView.DrawChart(g, draw, x, y, w, _s, _theme, _cfg, smallFont, tabFont,
            _chartMode, _chartRange, _chartPctWindow, _chartData, _pctData, _chartLoading,
            _tabRects, _modeRects, _pctWinRects, dim);

    private int DrawLiveSessions(Graphics g, bool draw, int x, int y, int w, Font smallFont, Brush fg, Brush dim)
    {
        // Cabecera de sección (en la vista de datos actual; en v0.3 la pondrá Section).
        if (draw)
            g.DrawString(_s.LiveSessionsTitle, smallFont, dim, x, y);
        y += 18;

        using var mono = new Font("Consolas", 11f, FontStyle.Regular, GraphicsUnit.Point);
        return DashboardDataView.DrawLiveSessionsBody(g, draw, _liveView, _cfg, _s, _theme, _mascotFrame,
            x, y, w, smallFont, mono, fg, dim, _liveRowRects);
    }
}
