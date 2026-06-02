using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Motion;

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

    // ---- Motion (F3): reloj bajo demanda + fade de apertura ----
    // Stopwatch monótono (inmune a cambios de hora) propiedad del form: la única fuente de
    // tiempo del motor. Cada tick pasa el delta a los AnimatedValue. El MotionScheduler decide
    // la cadencia del _tick (33 ms si algo anima + panel visible; 1000 ms countdown; parar si oculto).
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly MotionScheduler _scheduler = new();
    private double _lastTickMs;                 // marca del último Advance, para el delta
    private double _openedAtMs;                 // elapsed del clock al abrir el panel (stagger, Tarea 4)
    private double _targetOpacity = 1.0;        // opacidad de destino (config); la animada va por _fadeOpacity
    private readonly AnimatedValue _fadeOpacity = new(1.0); // fade 0→objetivo al abrir (OutQuad, FadeMs)

    private DateTime _shownAtUtc = DateTime.MinValue;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow; // para no marcar stale durante la 1ª fetch
    private bool _sticky;
    private bool _menuOpen;
    private string _appliedPlacement = "";

    private bool _dragging;
    private Point _dragOffset;
    private Rectangle _closeRect;

    // View mode (v0.3): "data" muestra cabecera + secciones; "settings" muestra el panel de ajustes.
    private string _viewMode = "data"; // "data" | "settings"
    private Rectangle _gearRect, _backRect;
    private readonly Dictionary<string, Rectangle> _sectionRects = new();   // "quota"/"sessions"/"spend"/"chart"
    private readonly Dictionary<string, Rectangle> _settingsRects = new();  // clave de acción ("toggle:X"/"theme:dark"/…)

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

    /// <summary>Emitido cuando el panel de ajustes cambia un valor: el host lo aplica vía MutateConfig.</summary>
    public event Action<Action<AppConfig>>? SettingsChanged;

    /// <summary>Emitido cuando se clica una fila del panel cuya clave NO es mutación simple de config
    /// (claves "special:*", p.ej. "special:importtheme"/"special:hooktoggle"). El host las maneja.</summary>
    public event Action<string>? SpecialActionRequested;

    /// <summary>Cambia a la vista de ajustes (⚙). Reajusta el alto del popup y repinta.</summary>
    public void ShowSettings() { _viewMode = "settings"; Relayout(); Invalidate(); }

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

        _tick = new System.Windows.Forms.Timer { Interval = Motion.SlowTickMs };
        _tick.Tick += (_, _) => OnMotionTick();
    }

    /// <summary>
    /// Latido del motor. Bajo demanda: integra el tiempo transcurrido en los AnimatedValue
    /// activos, ajusta el intervalo del timer vía <see cref="MotionScheduler"/> (33 ms si algo
    /// anima, 1000 ms si solo countdown, parar si oculto) y solo repinta si algo cambió. Con el
    /// panel oculto no hace trabajo (invariante CPU 24/7).
    /// </summary>
    private void OnMotionTick()
    {
        if (!Visible) { _tick.Stop(); return; }

        double now = _clock.Elapsed.TotalMilliseconds;
        double delta = now - _lastTickMs;
        _lastTickMs = now;
        if (delta < 0) delta = 0;

        bool wasAnimating = _fadeOpacity.IsAnimating;
        if (wasAnimating)
        {
            _fadeOpacity.Advance(delta);
            ApplyFadeOpacity();
        }

        // El countdown del footer (UpdatedAt · hace N min) cambia cada minuto: en cadencia lenta
        // repintamos igual para refrescarlo, como hacía el 1 Hz de antes. El mascotFrame sigue
        // su 1 Hz para mantener la vida actual de la mascota (su animación rica llega en Tarea 5).
        bool animating = _fadeOpacity.IsAnimating;
        if (!animating) _mascotFrame++;

        // Repinta si hubo cambio de animación o es un tick de countdown.
        Invalidate();

        // Reajusta la cadencia para el siguiente latido (bajo demanda).
        int interval = _scheduler.DesiredIntervalMs(visible: true, animating: animating);
        if (interval <= 0) { _tick.Stop(); return; }
        if (_tick.Interval != interval) _tick.Interval = interval;
    }

    /// <summary>Traslada el valor animado del fade a la propiedad <see cref="Form.Opacity"/>.</summary>
    private void ApplyFadeOpacity()
    {
        double v = Math.Clamp(_fadeOpacity.Value, 0.0, 1.0);
        if (Math.Abs(Opacity - v) > 0.001) Opacity = v;
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
        // No pisar el fade de apertura: si hay un fade en vuelo, solo guardamos el objetivo;
        // la opacidad se fija directa únicamente cuando no hay animación de fade.
        _targetOpacity = Math.Clamp(cfg.DashboardOpacity, 0.3, 1.0);
        if (!_fadeOpacity.IsAnimating) Opacity = _targetOpacity;
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
        _targetOpacity = Math.Clamp(cfg.DashboardOpacity, 0.3, 1.0);
        _chartMode = cfg.ChartMode;
        _chartPctWindow = cfg.ChartPctWindow;
        BackColor = _theme.Background;
        Relayout();
        _appliedPlacement = PlacementKey(cfg);
        _shownAtUtc = DateTime.UtcNow;

        // Fade de apertura: arranca por debajo del objetivo y sube a él con OutQuad en FadeMs.
        // _openedAtMs marca el instante de apertura (lo usa el stagger de la Tarea 4).
        _openedAtMs = _clock.Elapsed.TotalMilliseconds;
        _lastTickMs = _openedAtMs;
        _fadeOpacity.Set(0.0, 0);                                        // asienta en 0 (start del fade)
        _fadeOpacity.Set(_targetOpacity, Motion.FadeMs, Easing.OutQuad); // anima 0→objetivo
        Opacity = 0.0;

        Show();
        BringToFront();
        Activate();
        SetForegroundWindow(Handle);
        _tick.Interval = Motion.FastTickMs; // arranca rápido para que el fade sea fluido
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
        // Invariante CPU 24/7: con el panel oculto, parar el reloj de animación (0 trabajo extra).
        // Al mostrarse, ShowConfigured ya arranca el tick a la cadencia rápida para el fade.
        if (!Visible) _tick.Stop();
    }

    /// <summary>
    /// Esc cierra el panel (F3): el dismiss por foco ya existía (OnDeactivate→Hide); Esc lo añade
    /// como atajo. Click fuera / ✕ siguen funcionando.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            Hide();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        if (_menuOpen) return; // config menu is open over the panel
        if (_viewMode == "settings") return; // no autocerrar mientras se ajusta
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

        // ⚙ → abrir ajustes (solo en la vista de datos).
        if (_viewMode == "data" && _gearRect.Contains(e.Location))
        {
            _viewMode = "settings"; Relayout(); Invalidate(); return;
        }

        // Vista de ajustes: ‹ vuelve a datos; cada fila clicada emite su mutación. Sin drag aquí.
        if (_viewMode == "settings")
        {
            if (_backRect.Contains(e.Location)) { _viewMode = "data"; Relayout(); Invalidate(); return; }
            foreach (var (key, r) in _settingsRects)
            {
                if (r.Contains(e.Location))
                {
                    if (DashboardSettingsView.ActionFor(key) is { } a) SettingsChanged?.Invoke(a);
                    else SpecialActionRequested?.Invoke(key); // claves "special:*": diálogo/instalador en el host
                    Invalidate();
                    return;
                }
            }
            return; // dentro de ajustes, fuera de rects: no arrastrar
        }

        // Cabeceras de sección plegables (vista de datos): alternar Collapsed* vía SettingsChanged.
        foreach (var (key, r) in _sectionRects)
        {
            if (r.Contains(e.Location))
            {
                SettingsChanged?.Invoke(ToggleSection(key));
                Relayout(); Invalidate();
                return;
            }
        }

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
        // Hand over interactive elements (close ✕, ⚙/‹, section headers, tabs, mode/window toggles,
        // settings rows); normal arrow elsewhere.
        bool overClickable = _viewMode == "settings"
            ? _closeRect.Contains(e.Location)
                || _backRect.Contains(e.Location)
                || _settingsRects.Values.Any(r => r.Contains(e.Location))
            : _closeRect.Contains(e.Location)
                || _gearRect.Contains(e.Location)
                || _sectionRects.Values.Any(r => r.Contains(e.Location))
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
        // Fuentes del sistema de diseño (estáticas y compartidas: NO se hace Dispose de ellas).
        var titleFont = Typography.Title;
        var planFont = Typography.Caption;
        var labelFont = Typography.Body;
        var smallFont = Typography.Caption;
        var mono = Typography.Mono;
        // Los tabs de la gráfica quieren peso bold para legibilidad de las píldoras: fuente local desechable.
        using var tabFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        using var fg = new SolidBrush(_theme.TextPrimary);
        using var dim = new SolidBrush(_theme.TextSecondary);

        int x = Padding.Left;
        int y = Padding.Top;
        int w = Width - Padding.Horizontal;

        // Chrome común a ambas vistas: título + plan + botón cerrar.
        if (draw)
        {
            g.DrawString("ClaudeBar", titleFont, fg, x, y);
            g.DrawString(_plan.Display, planFont, dim, x, y + 24);
            _closeRect = new Rectangle(Width - 26, 10, 18, 18);
            using var closeFont = new Font("Segoe UI", 11f, FontStyle.Bold);
            g.DrawString("✕", closeFont, dim, _closeRect.X, _closeRect.Y - 2);
        }
        y += 50;

        // ----- Vista de ajustes -----
        if (_viewMode == "settings")
        {
            if (draw)
            {
                using var bb = new SolidBrush(_theme.TextSecondary);
                g.DrawString("‹ " + _s.Settings, labelFont, bb, x, y);
            }
            _backRect = new Rectangle(x, y, 80, 20);
            y += 24;
            y = DashboardSettingsView.Draw(g, draw, x, y, w, _cfg, _s, _theme, labelFont, smallFont, _settingsRects);
            return y + 18;
        }

        // ----- Vista de datos: cabecera de un vistazo + secciones plegables -----
        _settingsRects.Clear();
        _backRect = Rectangle.Empty;

        y = DashboardHeader.Draw(g, draw, x, y, w,
            _snap, _liveView, _cfg, _s, _theme, _mascotFrame,
            labelFont, smallFont, mono, ref _gearRect);

        y = DashboardDataView.Draw(g, draw, x, y, w,
            _snap, _liveView, _cfg, _s, _theme,
            labelFont, smallFont, tabFont,
            _chartMode, _chartRange, _chartPctWindow,
            _chartData, _pctData, _chartLoading,
            _sectionRects, _tabRects, _modeRects, _pctWinRects, _liveRowRects);

        // footer: "Actualizado · hace N min · pista", con marcador stale si el dato envejece
        y += 4;
        // ¿el dato está desfasado? No marcar durante los primeros RefreshSeconds tras arrancar (1ª fetch).
        bool grace = (DateTime.UtcNow - _startedAtUtc) < TimeSpan.FromSeconds(Math.Max(15, _cfg.RefreshSeconds));
        bool stale = _snap is not null
            && _snap.LatestState == UsageFetchState.Ok
            && !grace
            && UsageFormat.IsStale(_snap.UsageAtUtc, _cfg.RefreshSeconds);
        if (draw)
        {
            float fx = x;
            if (stale)
            {
                using var warn = new SolidBrush(_theme.Warn);
                string mark = $"⚠ {_s.StaleLabel} · ";
                g.DrawString(mark, smallFont, warn, fx, y);
                fx += g.MeasureString(mark, smallFont).Width;
            }

            string footer;
            if (_snap is not null && _snap.LatestState != UsageFetchState.Ok)
                footer = $"⚠ {UsageFormat.StateMessage(_snap.LatestState, _s)} · {_s.PreviousDataFooter}";
            else if (_snap is not null)
            {
                string hint = _sticky ? _s.HintPinnedClose : _s.HintClickToHide;
                footer = $"{_s.UpdatedAt} · {UsageFormat.Relative(_snap.UsageAtUtc, _s)} · {hint}";
            }
            else footer = _s.Loading;
            g.DrawString(footer, smallFont, dim, fx, y);
        }
        y += (int)Math.Ceiling(smallFont.GetHeight(g)) + Spacing.Xs;

        // Sello de privacidad honesto (siempre visible, neutro).
        if (draw)
        {
            using var muted = new SolidBrush(_theme.TextMuted);
            g.DrawString(_s.LocalSeal, smallFont, muted, x, y);
        }
        return y + 18;
    }

    /// <summary>Traduce la clave de una sección plegable clicada a la mutación de config (Collapsed*).</summary>
    private static Action<AppConfig> ToggleSection(string sectionKey) => sectionKey switch
    {
        "quota" => c => c.CollapsedQuota = !c.CollapsedQuota,
        "sessions" => c => c.CollapsedSessions = !c.CollapsedSessions,
        "spend" => c => c.CollapsedSpend = !c.CollapsedSpend,
        "chart" => c => c.CollapsedChart = !c.CollapsedChart,
        _ => _ => { },
    };
}
