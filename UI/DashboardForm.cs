using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeBarWin.Config;
using ClaudeBarWin.Models;
using ClaudeBarWin.Services;
using ClaudeBarWin.Services.Mascot;
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

    // Tween de números/barras (Tarea 2): AnimatedValue por clave ("bar:5h"/"bar:7d"/"num:crit"/"pace").
    // UpdateData hace SetTarget(target); el paint muestrea Display(); el tick hace Advance(delta). El color
    // de las barras va por el objetivo (no parpadea); solo el ancho/número deslizan. La cabecera y las
    // secciones lo reciben como sampler opcional. Con reduce-motion (Tarea 7) el Display devuelve el target.
    private readonly MotionState _motion = new();
    private bool _reduceMotion;                  // gate único de reduce-motion (Tarea 7 lo enchufa a config; default OFF)

    // Vida de la mascota (Tarea 5): el MascotAnimator (puro) elige frame/blink/spinner/verbo a partir
    // del tiempo en la fase actual + una semilla estable; el MascotMood (puro, histéresis+decay)
    // reacciona a eventos (atención/reset/procesado largo). El reloj de fase se reinicia al cambiar
    // GlobalPhase. Con reduce-motion el animador devuelve frame base (sin spinner/jitter) por elapsed 0.
    private readonly MascotMood _mascotMood = new();
    private SessionPhase _mascotPhase = SessionPhase.Idle;
    private double _mascotPhaseStartMs;          // elapsed del clock al entrar en la fase actual
    private double _lastMoodUpdateMs;            // marca del último Update del humor (para el delta)
    private const int MascotSeed = 0x5EED;       // semilla determinista del jitter (estable por proceso)

    // Bote de atención + celebración de reset (Tarea 6). El bote (Bounce, OutBack) se dispara al entrar
    // en una fase que pide atención (WaitingFor*) y se RE-dispara cada BounceRepeatEveryMs mientras
    // persista. La celebración (ResetDetector en UpdateData) compara el ResetsAt/utilización previo y
    // nuevo de 5h/7d; al detectar un reset enciende el humor Happy y un destello "✓ cuota renovada"
    // in-panel durante CelebrationMs. Ambos son elapsed-driven; con reduce-motion no se aplican.
    private readonly ResetDetector _resetDetector = new();
    private double _bounceStartMs = double.NegativeInfinity; // elapsed del último disparo del bote (−∞ = inactivo)
    private double _celebrationUntilMs = double.NegativeInfinity; // elapsed hasta el que dura el destello
    private bool _resetPending;                  // hay un reset detectado sin "consumir" en el humor todavía

    /// <summary>Tiempo (ms) en la fase actual de la mascota (elapsed crudo del reloj de fase).</summary>
    private double MascotElapsedMs() =>
        _renderOverride is { } o ? o.MascotElapsedMs
        : _clock.Elapsed.TotalMilliseconds - _mascotPhaseStartMs;

    /// <summary>Humor vigente de la mascota: el override del render-test manda si está presente.</summary>
    private Mood MascotMoodCurrent() =>
        _renderOverride is { MascotMood: { } m } ? m : _mascotMood.Current;

    /// <summary>
    /// Muestrea el estado del animador para la fase global vigente. La puerta única de reduce-motion
    /// se PROPAGA al animador (<c>reduceMotion: _reduceMotion</c>), que colapsa al frame base estático
    /// (sin spinner ni jitter). No se confía en elapsed=0 para suprimir el spinner: <c>Sample(Processing,
    /// 0)</c> aún devolvería el glifo <c>SpinnerSequence[0]</c>, así que el gate va por el flag.
    /// </summary>
    private MascotState SampleMascot() =>
        MascotAnimator.Sample(_liveView.GlobalPhase, MascotElapsedMs(), MascotSeed, _reduceMotion);

    /// <summary>
    /// Reinicia el reloj de fase si <see cref="LiveSessionsView.GlobalPhase"/> cambió y empuja el
    /// humor con el evento derivado de la fase. Idempotente por tick.
    /// </summary>
    private void SyncMascotPhase()
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        var phase = _liveView.GlobalPhase;
        if (phase != _mascotPhase)
        {
            _mascotPhase = phase;
            _mascotPhaseStartMs = now;
        }
        double moodDelta = now - _lastMoodUpdateMs;
        _lastMoodUpdateMs = now;
        if (moodDelta < 0) moodDelta = 0;

        // Una celebración de reset recién detectada se "consume" como evento de humor (Happy) una vez;
        // tiene prioridad sobre la fase salvo que esta pida atención (Alert manda).
        MoodEvent ev = _resetPending && !phase.NeedsAttention()
            ? MoodEvent.ResetCelebrated
            : MoodEventFor(phase);
        _resetPending = false;
        _mascotMood.Update(phase, ev, moodDelta);
    }

    private static MoodEvent MoodEventFor(SessionPhase phase) => phase switch
    {
        SessionPhase.WaitingForApproval or SessionPhase.WaitingForInput => MoodEvent.AttentionRequired,
        SessionPhase.Processing or SessionPhase.Compacting => MoodEvent.LongProcessing,
        _ => MoodEvent.None,
    };

    /// <summary>
    /// Mantiene el reloj del bote de atención: lo (re)dispara mientras la fase global pida atención y
    /// haya pasado <see cref="Motion.BounceRepeatEveryMs"/> desde el último bote; lo apaga si la fase
    /// deja de pedir atención. Elapsed-driven; con reduce-motion no se dispara (offset 0 en el paint).
    /// </summary>
    private void SyncBounce()
    {
        if (_reduceMotion || !_liveView.GlobalPhase.NeedsAttention()
            || !_cfg.LiveSessionsEnabled || !_cfg.ShowMascot)
        {
            _bounceStartMs = double.NegativeInfinity;
            return;
        }
        double now = _clock.Elapsed.TotalMilliseconds;
        bool inFlight = Bounce.IsActive(now - _bounceStartMs, Motion.BouncePeriodMs, Motion.BounceRepeats);
        bool dueAgain = now - _bounceStartMs >= Motion.BounceRepeatEveryMs;
        if (!inFlight && dueAgain) _bounceStartMs = now;
    }

    /// <summary>Offset (px ≥ 0) del bote de atención de la mascota en el instante actual (0 si reduce-motion).</summary>
    private int MascotBounceOffsetY()
    {
        if (_renderOverride is { } o) return _reduceMotion ? 0 : o.MascotBounceOffsetY;
        if (_reduceMotion || double.IsNegativeInfinity(_bounceStartMs)) return 0;
        double t = _clock.Elapsed.TotalMilliseconds - _bounceStartMs;
        return Bounce.OffsetY(t, Motion.BounceAmplitudePx, Motion.BouncePeriodMs, Motion.BounceRepeats);
    }

    /// <summary>¿Sigue activo el bote (para alimentar al scheduler)?</summary>
    private bool BounceActive() =>
        !_reduceMotion && !double.IsNegativeInfinity(_bounceStartMs)
        && Bounce.IsActive(_clock.Elapsed.TotalMilliseconds - _bounceStartMs, Motion.BouncePeriodMs, Motion.BounceRepeats);

    /// <summary>¿Está visible el destello de celebración de reset ahora mismo?</summary>
    private bool CelebrationActive() =>
        _renderOverride is { } o ? (!_reduceMotion && o.Celebrating)
        : (!_reduceMotion && _clock.Elapsed.TotalMilliseconds < _celebrationUntilMs);

    /// <summary>Texto del destello de celebración ("✓ cuota renovada" lo monta la cabecera) o null.</summary>
    private string? CelebrationText() => CelebrationActive() ? _s.QuotaRenewed : null;

    // Hover (Tarea 3): clave del rect interactivo bajo el cursor (o null). OnMouseMove la recalcula
    // sobre los diccionarios de rects ya existentes vía HoverHitTest; si cambia, repinta. La intensidad
    // del realce hace fade-in con OutQuad en FadeMs (un AnimatedValue 0→1). El realce es un fondo
    // redondeado HoverBg DETRÁS del rect, dibujado solo en la pasada de pintado: nunca toca el layout.
    private string? _hoveredKey;
    private readonly AnimatedValue _hoverIntensity = new(0.0);

    // ---- Override de tiempo de motion para el render-test (Tarea 8) ----
    // El render es offline (sin reloj de UI ni ticks): para capturar las microinteracciones en un
    // FOTOGRAMA FIJO, PrepareForRender puede inyectar un override determinista (tSinceOpen, hover,
    // tiempo/humor de la mascota, destello de celebración). Cuando está presente, los muestreadores
    // de motion leen de aquí en vez del Stopwatch vivo. En la app real es siempre null (cuelga del reloj).
    private RenderMotionOverride? _renderOverride;

    /// <summary>
    /// Estado de motion congelado que el render-test inyecta para capturar las microinteracciones a
    /// medio camino sin reloj real. Todo es puro/determinista: no toca el <see cref="Stopwatch"/>.
    /// </summary>
    public readonly record struct RenderMotionOverride(
        double TSinceOpenMs,
        string? HoveredKey = null,
        double MascotElapsedMs = 0,
        Mood? MascotMood = null,
        bool Celebrating = false,
        int MascotBounceOffsetY = 0);

    /// <summary>
    /// Resuelve el gate ÚNICO de reduce-motion para un <paramref name="cfg"/>: lee
    /// <c>cfg.ReduceMotion</c> (default <c>false</c> = animaciones ON, decisión de Yovan). Este es el
    /// único punto del que cuelga todo el colapso a estado final: <see cref="MotionState.SetTarget"/>
    /// hace snap, <see cref="MotionScheduler"/> no pide fast-tick, <see cref="MascotAnimator"/> queda
    /// en frame base y stagger/bounce/fade van a su estado final. El default NO depende del SO; existe
    /// <see cref="MotionPrefs.OsReducedMotion"/> para una futura opción "seguir Windows".
    /// </summary>
    private static bool ResolveReduceMotion(AppConfig cfg) => cfg.ReduceMotion;

    /// <summary>
    /// Tiempo (ms) transcurrido desde la apertura del panel, para la entrada escalonada (Tarea 4).
    /// En vivo = reloj − <c>_openedAtMs</c>. Con reduce-motion devuelve +∞ ⇒ todas las secciones
    /// asentadas (offset 0) por el gate de <see cref="Stagger"/>. (Tarea 8 añadirá un override de
    /// tiempo para el render-test; aquí basta con el reloj.)
    /// </summary>
    private double TSinceOpenMs() =>
        _reduceMotion ? double.PositiveInfinity
        : _renderOverride is { } o ? o.TSinceOpenMs
        : _clock.Elapsed.TotalMilliseconds - _openedAtMs;

    private DateTime _shownAtUtc = DateTime.MinValue;
    private readonly DateTime _startedAtUtc = DateTime.UtcNow; // para no marcar stale durante la 1ª fetch
    // Footer (fix F3 Tarea 8): "ahora" CONGELADO por repaint para que medir(draw=false) y pintar(draw=true)
    // del footer produzcan las mismas líneas, y firma del último footer pintado para detectar (en el tick)
    // cuándo el nº de líneas cambió (fresh→stale / cruce de wrap del relativo) y hace falta Relayout().
    private DateTime _footerNowUtc = DateTime.MinValue;
    private (int Stale, int Footer, int Seal) _lastFooterSig;
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

    // Scroll del panel de ajustes (v0.3.7): el panel se LIMITA en alto (MaxPanelHeightPct del área
    // útil) y el contenido rueda. El offset/altos viven aquí; la matemática pura en DashboardSettingsView.
    private int _settingsScroll;                 // desplazamiento actual del contenido (px, 0 = arriba)
    // Acumulador de la rueda (v0.3.7+): los trackpads de precisión mandan muchos eventos pequeños
    // (±1…±40, smooth-scrolling) en vez del diente de ±120 del ratón clásico. WheelToPixels acumula aquí
    // el delta (en su dominio escalado, opaco) y solo convierte a píxeles la parte entera, conservando el
    // resto para el siguiente evento ⇒ scroll suave con trackpad sin cambiar la sensación del ratón.
    // Se resetea junto con _settingsScroll (en ShowSettings) para no arrastrar resto entre aperturas.
    private int _settingsWheelAccum;
    private int _settingsContentH;               // alto real del contenido de ajustes (sin tope)
    private int _settingsViewportTop;            // y donde arranca la zona scrollable (bajo "‹ Ajustes")
    private const int SettingsViewportBottomPad = 12; // aire entre el final del viewport y el borde
    private readonly Dictionary<string, Rectangle> _scratchRects = new();   // dict desechable para MEDIR

    // Chart
    private Func<ChartRange, Task<List<HistoryBucket>>>? _historyProvider;
    private Func<ChartRange, Task<List<PctPoint>>>? _pctProvider;

    // Live sessions (mascot + instance list)
    private Func<LiveSessionsView>? _liveProvider;
    private LiveSessionsView _liveView = new();
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

    /// <summary>Cambia a la vista de ajustes (⚙). Resetea el scroll (y el acumulador de rueda), reajusta el alto y repinta.</summary>
    public void ShowSettings() { _viewMode = "settings"; _settingsScroll = 0; _settingsWheelAccum = 0; Relayout(); Invalidate(); }

    // ---- Ganchos SOLO para el render de GIFs (--render-gif): conducir el scroll de ajustes a mano ----
    // En vivo el scroll lo gobierna la rueda (OnMouseWheel); para el GIF de "ajustes" necesitamos barrer
    // el desplazamiento fotograma a fotograma de forma determinista. Estos dos miembros internal exponen
    // SOLO lo justo para eso, sin tocar el comportamiento en runtime: nada en la app llama a esto.

    /// <summary>
    /// Fija el scroll del panel de ajustes para el render (px desde arriba). NO resetea el acumulador de
    /// rueda ni relayoutea: a diferencia de <see cref="ShowSettings"/> (que pone el scroll a 0), aquí solo
    /// se siembra el valor y se repinta; el siguiente <c>DrawToBitmap</c> lo acota a [0, overflow] en
    /// <see cref="LayoutContent"/> y dibuja el contenido desplazado. Llamar <see cref="ShowSettings"/> UNA
    /// vez antes del barrido (resetea a 0) y luego este por cada fotograma.
    /// </summary>
    internal void SetSettingsScrollForRender(int px) { _settingsScroll = Math.Max(0, px); Invalidate(); }

    /// <summary>
    /// Overflow máximo de scroll del panel de ajustes = <c>max(0, contentH − viewportH)</c>, calculado con
    /// los campos que ya puebla <see cref="LayoutContent"/> (alto real del contenido y top del viewport) y
    /// el <see cref="Control.Height"/> ya acotado al tope. Solo es válido tras un primer
    /// <c>DrawToBitmap</c> en modo ajustes (esos campos quedan poblados). 0 si el contenido cabe entero.
    /// </summary>
    internal int SettingsMaxScrollForRender
    {
        get
        {
            // Mismo viewportH que usan OnMouseWheel/LayoutContent: alto útil bajo el chrome + "‹ Ajustes".
            int viewportH = Height - _settingsViewportTop - SettingsViewportBottomPad;
            return Math.Max(0, _settingsContentH - viewportH);
        }
    }

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

        if (_fadeOpacity.IsAnimating)
        {
            _fadeOpacity.Advance(delta);
            ApplyFadeOpacity();
        }

        // Tween de números/barras: integra el delta en todos los AnimatedValue por clave.
        _motion.Advance(delta);

        // Hover (Tarea 3): el fade-in/out del realce avanza con el mismo reloj.
        if (_hoverIntensity.IsAnimating) _hoverIntensity.Advance(delta);

        // Vida de la mascota (Tarea 5): reloj de fase + humor (histéresis/decay). El animador es
        // puro y se muestrea en el paint; aquí solo sincronizamos fase/humor con el reloj.
        SyncMascotPhase();
        // Bote de atención (Tarea 6): (re)dispara el bote mientras la fase pida atención.
        SyncBounce();
        bool mascotAlive = !_reduceMotion && _cfg.LiveSessionsEnabled && _cfg.ShowMascot
                           && MascotAnimator.IsAnimatedPhase(_liveView.GlobalPhase);

        // El countdown del footer (UpdatedAt · hace N min) cambia cada minuto: en cadencia lenta
        // repintamos igual para refrescarlo, como hacía el 1 Hz de antes.
        bool animating = _fadeOpacity.IsAnimating || _motion.IsAnimating
                         || _hoverIntensity.IsAnimating || mascotAlive
                         || BounceActive() || CelebrationActive();

        // Footer: congela SIEMPRE el "ahora" común de este repaint (medir/pintar leen el mismo instante)
        // y deja que el relativo ("hace N min") avance aunque haya una animación larga en curso. Es solo
        // una asignación, coste cero.
        _footerNowUtc = DateTime.UtcNow;

        // Red de seguridad del footer (fix F3 Tarea 8): si el nº de líneas del footer cambió desde el
        // último pintado (fresh→stale / cruce de wrap del relativo), re-mide el alto ANTES de invalidar
        // para no recortar el sello.
        // Fix F3 (minor): la MEDICIÓN (CreateGraphics()+MeasureString del footer) corre SOLO en el tick
        // lento de countdown (animating == false), no en cada fast-tick (~33 ms). El nº de líneas solo
        // cambia por el reloj de pared (flag stale / wrap del relativo), que muta ~1/min, NUNCA por las
        // animaciones, así que medirlo 30 veces/seg durante toda la animación era trabajo puro a cambio
        // de nada. Saltárselo durante animación no pierde la red de seguridad: la transición stale/wrap
        // dispara Relayout en el siguiente tick lento (1 Hz).
        if (!animating) ReconcileFooterHeight();

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
        // Vida de la mascota (Tarea 5): resincroniza el reloj de fase + humor con la nueva fase global.
        SyncMascotPhase();
        // Bote de atención (Tarea 6): (re)dispara el bote si la nueva fase pide atención.
        SyncBounce();
        Relayout();
        Invalidate();
        // Si la mascota ha cobrado vida (fase animada o bote activo) y el panel está visible, arranca el fast-tick.
        if (Visible && !_reduceMotion && _cfg.LiveSessionsEnabled && _cfg.ShowMascot
            && (MascotAnimator.IsAnimatedPhase(_liveView.GlobalPhase) || BounceActive()))
            EnsureFastTick();
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
        _reduceMotion = ResolveReduceMotion(cfg);
        BackColor = _theme.Background;

        // Tween de números/barras: apunta cada AnimatedValue al nuevo objetivo (desde el valor actual,
        // sin salto). El render muestrea Display(); el tick los Advance. Con reduce-motion colapsan.
        RetargetMotion();
        // Celebración de reset (Tarea 6): compara el ResetsAt/utilización previo y nuevo de 5h/7d. Al
        // detectar un reset enciende el destello in-panel + marca el humor Happy pendiente. NO toca el
        // sistema de notificaciones (eso es F4). Con reduce-motion se omite el destello (estado final).
        DetectQuotaReset();
        // Si algo arrancó a animar, asegúrate de que el reloj rápido esté latiendo (panel visible).
        if (Visible && (_motion.IsAnimating || CelebrationActive()) && !_reduceMotion) EnsureFastTick();

        if (IsHandleCreated)
            BeginInvoke(() =>
            {
                Relayout();
                Invalidate();
                if (Visible && cfg.ShowChart) _ = ReloadChart();
            });
    }

    /// <summary>
    /// Reapunta los <see cref="AnimatedValue"/> de las barras/números al objetivo del snapshot actual.
    /// Las claves replican lo que pinta el render: <c>bar:5h</c>/<c>bar:7d</c> (cuerpo) y <c>num:crit</c>
    /// (la barra crítica de la cabecera = la ventana de mayor utilización). Si no hay dato, no toca nada.
    /// </summary>
    private void RetargetMotion()
    {
        var usage = _snap?.Usage;
        if (usage is null) return;
        if (usage.FiveHour is { } w5) _motion.SetTarget("bar:5h", w5.UtilizationPct, _reduceMotion);
        if (usage.SevenDay is { } w7) _motion.SetTarget("bar:7d", w7.UtilizationPct, _reduceMotion);
        // Barra crítica de la cabecera: la de mayor utilización entre 5h/7d (mismo criterio que DashboardHeader).
        UsageWindow? crit = usage.FiveHour is null ? usage.SevenDay
            : usage.SevenDay is null ? usage.FiveHour
            : usage.FiveHour.UtilizationPct >= usage.SevenDay.UtilizationPct ? usage.FiveHour : usage.SevenDay;
        if (crit is not null) _motion.SetTarget("num:crit", crit.UtilizationPct, _reduceMotion);
    }

    /// <summary>
    /// Alimenta el <see cref="ResetDetector"/> con las ventanas 5h/7d del snapshot actual. Si alguna
    /// se ha reseteado (el <c>ResetsAt</c> saltó hacia adelante o la utilización cayó en picado),
    /// enciende el destello de celebración in-panel durante <see cref="Motion.CelebrationMs"/> y marca
    /// el humor Happy pendiente. Con reduce-motion no hay destello (estado final), pero el detector se
    /// alimenta igual para no disparar una celebración tardía al desactivarse reduce-motion.
    /// </summary>
    private void DetectQuotaReset()
    {
        var usage = _snap?.Usage;
        bool reset = false;
        if (usage is not null)
        {
            reset |= _resetDetector.Observe("5h", usage.FiveHour);
            reset |= _resetDetector.Observe("7d", usage.SevenDay);
        }
        if (reset && !_reduceMotion)
        {
            _celebrationUntilMs = _clock.Elapsed.TotalMilliseconds + Motion.CelebrationMs;
            _resetPending = true;
        }
    }

    /// <summary>Arranca/acelera el tick a la cadencia rápida para que el tween sea fluido.</summary>
    private void EnsureFastTick()
    {
        if (_tick.Interval != Motion.FastTickMs) _tick.Interval = Motion.FastTickMs;
        if (!_tick.Enabled)
        {
            // Reanuda el reloj sin contar como delta el tiempo en que estuvo parado.
            _lastTickMs = _clock.Elapsed.TotalMilliseconds;
            _tick.Start();
        }
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
        _reduceMotion = ResolveReduceMotion(cfg);
        BackColor = _theme.Background;
        // Fix F3 (minor): siembra el "ahora" CONGELADO del footer ANTES del primer Relayout()/Show().
        // Sin esto, en el arranque (antes del primer OnMotionTick) _footerNowUtc seguía en MinValue y
        // BuildFooterLines caía a DateTime.UtcNow fresco en CADA llamada: medir (Relayout, T_a) y pintar
        // (OnPaint, T_b) leían dos relojes distintos, y un cruce de wrap del relativo o del flag stale
        // entre T_a y T_b dejaba la ventana 1 línea corta y recortaba el sello en el primer frame.
        // Con la semilla, el primer ciclo medir+pintar comparte instante; el primer OnMotionTick lo
        // refresca como hasta ahora. El render-test usa _renderOverride y no pasa por aquí.
        _footerNowUtc = DateTime.UtcNow;
        Relayout();
        _appliedPlacement = PlacementKey(cfg);
        _shownAtUtc = DateTime.UtcNow;

        // Fade de apertura: arranca por debajo del objetivo y sube a él con OutQuad en FadeMs.
        // _openedAtMs marca el instante de apertura (lo usa el stagger de la Tarea 4). Con reduce-motion
        // (gate único) la duración es 0 ⇒ Show() directo a opacidad objetivo, sin fade ni slide.
        _openedAtMs = _clock.Elapsed.TotalMilliseconds;
        _lastTickMs = _openedAtMs;
        double fadeMs = _reduceMotion ? 0 : Motion.FadeMs;
        _fadeOpacity.Set(0.0, 0);                                  // asienta en 0 (start del fade)
        _fadeOpacity.Set(_targetOpacity, fadeMs, Easing.OutQuad);  // anima 0→objetivo (o salta si reduce-motion)
        Opacity = _reduceMotion ? _targetOpacity : 0.0;

        Show();
        BringToFront();
        Activate();
        SetForegroundWindow(Handle);
        _tick.Interval = _reduceMotion ? Motion.SlowTickMs : Motion.FastTickMs; // reduce-motion: solo countdown
        _tick.Start();
        if (cfg.ShowChart) _ = ReloadChart();
    }

    /// <summary>
    /// For offline rendering (render-test): set everything synchronously and size to fit.
    /// <para>
    /// El parámetro opcional <paramref name="motion"/> congela las microinteracciones en un FOTOGRAMA
    /// FIJO (tSinceOpen, hover, tiempo/humor de la mascota, destello de celebración) sin reloj de UI:
    /// el render-test las captura a medio camino. <paramref name="live"/> fija la fase global (mascota).
    /// Con ambos null el comportamiento es el de hoy (estado final, sin motion).
    /// </para>
    /// </summary>
    public void PrepareForRender(AppSnapshot snap, AppConfig cfg, PlanInfo plan,
        List<HistoryBucket> buckets, List<PctPoint> pct, ChartRange range,
        RenderMotionOverride? motion = null, LiveSessionsView? live = null)
    {
        _snap = snap;
        _cfg = cfg;
        _plan = plan;
        _s = Localization.ForConfig(cfg);
        _theme = ThemeResolver.Resolve(cfg);
        BackColor = _theme.Background;
        _chartMode = cfg.ChartMode;
        _chartPctWindow = cfg.ChartPctWindow;
        _reduceMotion = ResolveReduceMotion(cfg);
        _chartData = buckets;
        _pctData = pct;
        _chartRange = range;
        _chartLoading = false;
        if (live is not null) _liveView = live;
        _renderOverride = motion;

        // Tween a medio camino para el render-test: siembra cada barra/número en su VALOR DE ARRANQUE
        // (0%) y reapunta al objetivo, luego avanza el reloj de motion tSinceOpen ms. Así Display()
        // devuelve un valor intermedio (no el target sembrado de primera aparición). Con reduce-motion
        // o sin override, queda en el target final (estado de hoy).
        if (motion is { } mo && !_reduceMotion)
        {
            SeedMotionForRender();
            _motion.Advance(mo.TSinceOpenMs);
            // Fade de apertura: Opacity = OutQuad(tSinceOpen/FadeMs) · DashboardOpacity.
            double prog = Motion.FadeMs <= 0 ? 1.0 : Math.Clamp(mo.TSinceOpenMs / Motion.FadeMs, 0.0, 1.0);
            double target = Math.Clamp(cfg.DashboardOpacity, 0.3, 1.0);
            Opacity = Easing.OutQuad(prog) * target;
        }

        _ = Handle;
        using var g = CreateGraphics();
        Height = LayoutContent(g, draw: false);
    }

    /// <summary>
    /// Siembra los <see cref="AnimatedValue"/> de barras/números en su valor de arranque (0%) y los
    /// reapunta al objetivo del snapshot, para que el render-test capture el tween a medio vuelo. Solo
    /// lo usa <see cref="PrepareForRender"/> con override; la app real arranca el tween en vivo.
    /// </summary>
    private void SeedMotionForRender()
    {
        var usage = _snap?.Usage;
        if (usage is null) return;
        void Seed(string key, double target)
        {
            _motion.Display(key, 0.0, false); // siembra la clave en 0 (valor de arranque)
            _motion.SetTarget(key, target, false); // reapunta al objetivo (tween desde 0)
        }
        if (usage.FiveHour is { } w5) Seed("bar:5h", w5.UtilizationPct);
        if (usage.SevenDay is { } w7) Seed("bar:7d", w7.UtilizationPct);
        UsageWindow? crit = usage.FiveHour is null ? usage.SevenDay
            : usage.SevenDay is null ? usage.FiveHour
            : usage.FiveHour.UtilizationPct >= usage.SevenDay.UtilizationPct ? usage.FiveHour : usage.SevenDay;
        if (crit is not null) Seed("num:crit", crit.UtilizationPct);
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
        if (!Visible)
        {
            _tick.Stop();
            // Descarta el hover para que un re-open no muestre un realce obsoleto (sin tick que lo limpie).
            _hoveredKey = null;
            _hoverIntensity.Set(0.0, 0);
            _hoverIntensity.Snap();
        }
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

    /// <summary>
    /// Rueda del ratón en la vista de ajustes (v0.3.7): desplaza el contenido WheelStepPx por diente,
    /// acotado a [0, overflow]. En la vista de datos no hace nada (esa vista auto-dimensiona).
    /// <para>
    /// Compatibilidad con TRACKPADS de precisión: en vez de <c>e.Delta / 120</c> (que truncaba a 0 con
    /// los deltas pequeños del smooth-scrolling y dejaba el panel sin rodar), acumulamos el delta crudo
    /// en <see cref="_settingsWheelAccum"/> y convertimos a píxeles solo la parte entera vía
    /// <see cref="DashboardSettingsView.WheelToPixels"/>; el resto se conserva para el siguiente evento.
    /// Un diente de ratón clásico (±120) sigue desplazando exactamente <c>WheelStepPx</c> (48px).
    /// </para>
    /// </summary>
    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_viewMode != "settings") return;
        int viewportH = Height - _settingsViewportTop - SettingsViewportBottomPad;
        if (_settingsContentH <= viewportH) return;
        // Acumula el delta crudo y extrae los píxeles enteros (el resto queda para el próximo evento).
        var (px, rest) = DashboardSettingsView.WheelToPixels(_settingsWheelAccum, e.Delta);
        _settingsWheelAccum = rest;
        if (px == 0) return; // delta de trackpad aún insuficiente para 1px: sigue sumando, no repintes
        // Signo: delta positivo (rueda arriba) REDUCE el scroll (sube el contenido).
        int ns = DashboardSettingsView.ClampScroll(_settingsScroll - px, _settingsContentH, viewportH);
        if (ns != _settingsScroll) { _settingsScroll = ns; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        if (_closeRect.Contains(e.Location)) { Hide(); return; }

        // ⚙ → abrir ajustes (solo en la vista de datos). ShowSettings resetea además el scroll.
        if (_viewMode == "data" && _gearRect.Contains(e.Location))
        {
            ShowSettings(); return;
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
        // Hover (Tarea 3): clave del rect interactivo bajo el cursor (precedencia estable, ver
        // HoveredRects). Cursor Hand cuando hay clave. Si la clave cambia, arranca/rearma el fade-in
        // del realce y repinta (bajo demanda, panel visible).
        string? key = HoverHitTest.Resolve(e.Location, HoveredRects());
        Cursor = key is not null ? Cursors.Hand : Cursors.Default;
        if (key != _hoveredKey)
        {
            _hoveredKey = key;
            // Realce: 0→1 al entrar en un rect, 1→0 al salir (a hueco). OutQuad en FadeMs. Con
            // reduce-motion (Tarea 7) el gate de Set colapsa al instante.
            _hoverIntensity.Set(key is not null ? 1.0 : 0.0, _reduceMotion ? 0 : Motion.FadeMs, Easing.OutQuad);
            if (Visible && !_reduceMotion) EnsureFastTick();
            Invalidate();
        }
    }

    /// <summary>
    /// Pares (clave → rect) candidatos al hover, en <b>orden de precedencia</b> (primero gana en
    /// solapes). En la vista de datos: chrome pequeño (✕/⚙) y controles finos (tabs, modos, ventanas,
    /// filas vivas) antes que las cabeceras de sección, que son los contenedores grandes. En ajustes:
    /// ✕/‹ y las filas. Se consume desde <see cref="OnMouseMove"/> y el realce del paint.
    /// </summary>
    private IEnumerable<KeyValuePair<string, Rectangle>> HoveredRects()
    {
        yield return new("chrome:close", _closeRect);
        if (_viewMode == "settings")
        {
            yield return new("chrome:back", _backRect);
            foreach (var (k, r) in _settingsRects) yield return new("set:" + k, r);
            yield break;
        }
        yield return new("chrome:gear", _gearRect);
        foreach (var (k, r) in _tabRects) yield return new("tab:" + (int)k, r);
        foreach (var (k, r) in _modeRects) yield return new("mode:" + k, r);
        foreach (var (k, r) in _pctWinRects) yield return new("pctwin:" + k, r);
        foreach (var (k, r) in _liveRowRects) yield return new("live:" + k, r);
        foreach (var (k, r) in _sectionRects) yield return new("sec:" + k, r);
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

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        // El cursor abandonó el panel: descarta el hover (fade-out del realce a 0).
        if (_hoveredKey is null) return;
        _hoveredKey = null;
        _hoverIntensity.Set(0.0, _reduceMotion ? 0 : Motion.FadeMs, Easing.OutQuad);
        if (Visible && !_reduceMotion) EnsureFastTick();
        Invalidate();
    }

    // ---------- paint / layout ----------

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        // Realce de hover DETRÁS del contenido: usa los rects de la pasada anterior (los mismos sobre
        // los que OnMouseMove resolvió la clave). Nunca toca el layout — es decoración bajo el texto.
        DrawHoverHighlight(e.Graphics);
        LayoutContent(e.Graphics, draw: true);
    }

    /// <summary>
    /// Pinta un fondo redondeado <c>theme.HoverBg</c> detrás del rect bajo el cursor, con la
    /// intensidad eased del fade-in (alfa). Sin clave o intensidad ≈0 ⇒ no pinta. No altera el layout.
    /// T7b (§3 #6): el token sustituye a BgElevated puro (invisible en claro y CLI) y, en la vista de
    /// ajustes, el realce de las filas se recorta al viewport del scroll (la fila cortada arriba
    /// sangraba ~2px sobre el chrome "‹ Ajustes" por el Inflate).
    /// </summary>
    private void DrawHoverHighlight(Graphics g)
    {
        // En el render-test el realce se fuerza desde el override (intensidad plena, sin tick que lo anime).
        string? hovered = _renderOverride is { HoveredKey: { } hk } ? hk : _hoveredKey;
        if (hovered is null && !_hoverIntensity.IsAnimating) return;
        double intensity = _renderOverride is not null
            ? (hovered is not null ? 1.0 : 0.0)
            : _reduceMotion ? (hovered is not null ? 1.0 : 0.0) : _hoverIntensity.Value;
        if (intensity <= 0.01) return;

        // Localiza el rect de la clave activa entre los diccionarios vigentes.
        Rectangle? target = null;
        foreach (var kv in HoveredRects())
            if (kv.Key == hovered) { target = kv.Value; break; }
        if (target is not { } r || r.Width <= 0 || r.Height <= 0) return;

        // Alfa proporcional a la intensidad sobre HoverBg; un poco de aire alrededor del rect.
        int alpha = (int)Math.Round(Math.Clamp(intensity, 0.0, 1.0) * _theme.HoverBg.A);
        if (alpha <= 0) return;
        var bg = Color.FromArgb(alpha, _theme.HoverBg);
        var padded = Rectangle.Inflate(r, Spacing.Xs, Spacing.Xs / 2);
        using var b = new SolidBrush(bg);

        // Filas de ajustes: el rect ya viene intersecado con el viewport, pero el Inflate vuelve a
        // sacarlo (±2px) → clip al MISMO viewport que usa LayoutContent para pintar el contenido.
        if (_viewMode == "settings" && hovered?.StartsWith("set:", StringComparison.Ordinal) == true)
        {
            int viewportH = Math.Max(0, Height - _settingsViewportTop - SettingsViewportBottomPad);
            var prevClip = g.Clip;
            g.SetClip(new Rectangle(0, _settingsViewportTop, Width, viewportH));
            try { Shapes.FillRounded(g, b, padded, Spacing.Sm); }
            finally { g.Clip = prevClip; }
            return;
        }
        Shapes.FillRounded(g, b, padded, Spacing.Sm);
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

        // ----- Vista de ajustes: alto LIMITADO + scroll (v0.3.7) -----
        // El panel medía más que la pantalla ("de arriba a abajo"). Ahora: chrome + "‹ Ajustes" fijos
        // arriba; el contenido se mide ENTERO (pasada de medida con dict desechable), el alto de la
        // ventana se acota a MaxPanelHeightPct del área útil, y el contenido se pinta desplazado
        // -_settingsScroll con CLIP al viewport. Los rects clicables salen de la pasada de pintado (ya
        // desplazados) y se INTERSECAN con el viewport para que una fila medio fuera no responda bajo
        // el chrome. Con todo visible no hay barra ni scroll (comportamiento de siempre).
        if (_viewMode == "settings")
        {
            if (draw)
            {
                using var bb = new SolidBrush(_theme.TextSecondary);
                g.DrawString("‹ " + _s.Settings, labelFont, bb, x, y);
            }
            _backRect = new Rectangle(x, y, 80, 20);
            y += 24;
            int contentTop = y;
            _settingsViewportTop = contentTop;

            // 1) Medir el contenido completo (sin tope) — el dict es desechable: los rects buenos
            //    (desplazados) los registra la pasada de pintado de abajo.
            _scratchRects.Clear();
            _settingsContentH = DashboardSettingsView.Draw(g, draw: false, x, contentTop, w,
                _cfg, _s, _theme, labelFont, smallFont, _scratchRects) - contentTop;

            // 2) Tope de alto: % del área útil de la pantalla del panel.
            var wa = Screen.FromControl(this).WorkingArea;
            int maxH = wa.Height * DashboardSettingsView.MaxPanelHeightPct / 100;
            int fullH = contentTop + _settingsContentH + 18;
            int h = Math.Min(fullH, maxH);
            int viewportH = h - contentTop - SettingsViewportBottomPad;
            _settingsScroll = DashboardSettingsView.ClampScroll(_settingsScroll, _settingsContentH, viewportH);

            if (draw)
            {
                var viewport = new Rectangle(0, contentTop, Width, viewportH);
                var prevClip = g.Clip;
                g.SetClip(viewport);
                try
                {
                    DashboardSettingsView.Draw(g, draw: true, x, contentTop - _settingsScroll, w,
                        _cfg, _s, _theme, labelFont, smallFont, _settingsRects);
                }
                finally { g.Clip = prevClip; }

                // Filas medio fuera del viewport: recortar su zona clicable (y de hover) al viewport.
                foreach (var k in _settingsRects.Keys.ToList())
                {
                    var r = Rectangle.Intersect(_settingsRects[k], viewport);
                    if (r.Height <= 0) _settingsRects.Remove(k); else _settingsRects[k] = r;
                }

                // Barrita de scroll (solo con overflow): pista sutil + pulgar proporcional.
                if (_settingsContentH > viewportH)
                {
                    int trackX = Width - DashboardSettingsView.ScrollBarW - DashboardSettingsView.ScrollBarMargin;
                    using (var tb = new SolidBrush(Color.FromArgb(50, _theme.TextMuted)))
                        Shapes.FillRounded(g, tb, new Rectangle(trackX, contentTop, DashboardSettingsView.ScrollBarW, viewportH), 2);
                    var thumb = DashboardSettingsView.ThumbRect(trackX, contentTop, viewportH, _settingsContentH, _settingsScroll);
                    using var thb = new SolidBrush(Color.FromArgb(160, _theme.TextMuted));
                    Shapes.FillRounded(g, thb, thumb, 2);
                }
            }
            return h;
        }

        // ----- Vista de datos: cabecera de un vistazo + secciones plegables -----
        _settingsRects.Clear();
        _backRect = Rectangle.Empty;

        // Entrada escalonada (Tarea 4): cabecera=0, secciones de datos=1..n, footer=n+1. Cada bloque se
        // dibuja envuelto en una traslación vertical (OffsetY px → 0) desfasada por su índice; el y de
        // layout NO cambia. El transform solo se aplica en la pasada de pintado (medir devuelve el mismo y).
        double tSinceOpen = TSinceOpenMs();

        // Cabecera (índice 0)
        int hOff = draw && !_reduceMotion
            ? Stagger.OffsetY(Stagger.Alpha(tSinceOpen, 0, Motion.StaggerMs, Motion.StaggerDurMs), Motion.StaggerMaxOffsetPx)
            : 0;
        // try/finally (fix F3 minor, mismo patrón que DashboardDataView.Section): garantiza que el pop
        // del transform ocurra aunque DashboardHeader.Draw lance, para no contaminar frames siguientes.
        if (hOff != 0) g.TranslateTransform(0, hOff);
        try
        {
            y = DashboardHeader.Draw(g, draw, x, y, w,
                _snap, _liveView, _cfg, _s, _theme, SampleMascot(), MascotMoodCurrent(),
                labelFont, smallFont, mono, ref _gearRect,
                _motion, _reduceMotion, MascotBounceOffsetY(), CelebrationText());
        }
        finally
        {
            if (hOff != 0) g.TranslateTransform(0, -hOff);
        }

        // Secciones de datos (índices 1..n)
        y = DashboardDataView.Draw(g, draw, x, y, w,
            _snap, _liveView, _cfg, _s, _theme,
            labelFont, smallFont, tabFont,
            _chartMode, _chartRange, _chartPctWindow,
            _chartData, _pctData, _chartLoading,
            _sectionRects, _tabRects, _modeRects, _pctWinRects, _liveRowRects,
            _motion, _reduceMotion, tSinceOpen, firstSectionIndex: 1);

        // Footer (índice n+1): se desplaza después de la última sección de datos.
        int footerIndex = DashboardDataView.VisibleSectionCount(_cfg, _snap) + 1;
        int fOff = draw && !_reduceMotion
            ? Stagger.OffsetY(Stagger.Alpha(tSinceOpen, footerIndex, Motion.StaggerMs, Motion.StaggerDurMs), Motion.StaggerMaxOffsetPx)
            : 0;
        if (fOff != 0) g.TranslateTransform(0, fOff);
        try
        {

        // footer: "Actualizado · hace N min · pista", con marcador stale si el dato envejece, más el
        // sello de privacidad. F2 truncaba ambos al ancho fijo del panel (340 px); ahora se ENVUELVEN a
        // varias líneas. PERO el nº de líneas depende del reloj de pared (el flag stale + el texto del
        // relativo), y el alto de la ventana solo se recalcula en Relayout(). Para no recortar el sello
        // entre snapshots, las líneas las construye FooterLayout (PURO, único origen de verdad) con un
        // "ahora" CONGELADO por pasada (_footerNowUtc) — así medir(draw=false) y pintar(draw=true) del
        // mismo repaint coinciden — y OnMotionTick re-layouta si la firma del footer cambió (la red de
        // seguridad que faltaba). El marcador stale conserva su color Warn en su línea corta.
        y += 4;
        int lineH = (int)Math.Ceiling(smallFont.GetHeight(g));
        double Mw(string str) => g.MeasureString(str, smallFont).Width;

        var footerLines = BuildFooterLines(w, Mw);
        // Snapshot de la firma SOLO en la pasada de pintado: es la que el tick compara para decidir si
        // el alto reservado quedó obsoleto. (Medir no debe pisar la firma del último pintado.)
        if (draw) _lastFooterSig = FooterLayout.Signature(footerLines);

        bool sealStarted = false;
        foreach (var fl in footerLines)
        {
            // Aire entre el bloque footer y el sello: una sola vez, antes de la primera línea del sello.
            if (fl.Kind == FooterLayout.LineKind.Seal && !sealStarted)
            {
                y += Spacing.Xs;
                sealStarted = true;
            }
            if (draw)
            {
                Color c = fl.Kind switch
                {
                    // Marcador stale = texto pequeño → variante AA del ámbar (T6b; en claro Warn caía a 2.8:1).
                    FooterLayout.LineKind.Stale => _theme.WarnText,
                    FooterLayout.LineKind.Seal => _theme.TextMuted,
                    _ => _theme.TextSecondary,
                };
                using var brush = new SolidBrush(c);
                g.DrawString(fl.Text, smallFont, brush, x, y);
            }
            y += lineH;
        }
        return y + Spacing.Sm;
        }
        finally
        {
            if (fOff != 0) g.TranslateTransform(0, -fOff);  // deshace el desplazamiento del footer
        }
    }

    /// <summary>
    /// Construye las líneas del footer (marcador stale + estado/actualización + sello) vía
    /// <see cref="FooterLayout"/>, el ÚNICO origen de verdad compartido por el pintado y por la guardia
    /// de re-layout del tick lento. El "ahora" está congelado en <see cref="_footerNowUtc"/> (lo fija el
    /// tick antes de invalidar; en arranque/Relayout = el reloj de pared) para que medir(draw=false) y
    /// pintar(draw=true) del mismo repaint produzcan EXACTAMENTE las mismas líneas (invariante de altura).
    /// </summary>
    private List<FooterLayout.Line> BuildFooterLines(int width, Func<string, double> measure)
    {
        DateTime now = _footerNowUtc == DateTime.MinValue ? DateTime.UtcNow : _footerNowUtc;
        // ¿el dato está desfasado? No marcar durante los primeros RefreshSeconds tras arrancar (1ª fetch).
        bool grace = (now - _startedAtUtc) < TimeSpan.FromSeconds(Math.Max(15, _cfg.RefreshSeconds));
        bool stale = _snap is not null
            && _snap.LatestState == UsageFetchState.Ok
            && !grace
            && UsageFormat.IsStaleAt(_snap.UsageAtUtc, now, _cfg.RefreshSeconds);
        return FooterLayout.Build(_snap, _s, stale, _sticky, now, width, measure);
    }

    /// <summary>
    /// Red de seguridad del invariante medir/pintar para el footer (fix F3 Tarea 8): el alto del bloque
    /// footer depende del reloj (flag stale + texto del relativo), pero la ventana solo se re-mide en
    /// <see cref="Relayout"/>. Sin esto, una transición fresh→stale o un cruce de wrap del relativo entre
    /// dos repaints a 1 Hz dejaba la ventana 1 línea corta y RECORTABA el sello. Aquí, en cada tick,
    /// congelamos un "ahora" común, medimos la firma del footer con él y, si cambió respecto a la última
    /// pintada, hacemos <see cref="Relayout"/> (re-mide el alto) ANTES de invalidar. Devuelve true si
    /// re-layoutó. Barato: solo mide texto cuando hay un repaint de countdown.
    /// </summary>
    private bool ReconcileFooterHeight()
    {
        if (!IsHandleCreated || !Visible || _renderOverride is not null) return false;
        _footerNowUtc = DateTime.UtcNow;
        var prev = _lastFooterSig;
        (int, int, int) nowSig;
        using (var g = CreateGraphics())
        {
            float h = Typography.Caption.GetHeight(g); // forzar contexto válido (no usado para la firma)
            _ = h;
            int w = Width - Padding.Horizontal;
            nowSig = FooterLayout.Signature(BuildFooterLines(w, str => g.MeasureString(str, Typography.Caption).Width));
        }
        if (nowSig == prev) return false;
        Relayout();   // re-mide el alto con el footer actual; LayoutContent(draw:true) ya leerá _footerNowUtc
        return true;
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
