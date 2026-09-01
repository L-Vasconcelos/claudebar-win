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
    // Datos del gráfico (Tabs y, desde T13a, las series dinámicas por familia) en DashboardDataView.

    private AppSnapshot? _snap;
    private AppConfig _cfg = new();
    private PlanInfo _plan = new("", "");
    private Strings _s = new();
    private Theme _theme = Theme.Dark;
    private readonly System.Windows.Forms.Timer _tick;

    // Overview section (Visão Geral)
    private UsageStats? _overviewStats;
    private bool _overviewCollapsed;
    private string _overviewRange = "all"; // "all" | "30d" | "7d"
    private readonly Dictionary<string, Rectangle> _overviewTabRects = new();

    // Records for the right column (model breakdown)
    private IReadOnlyList<Models.UsageRecord>? _overviewRecords;

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
    /// Mantiene el reloj del bote de la mascota: lo (re)dispara mientras la fase global pida atención
    /// O haya una celebración de reset en curso (T-v039 F3c), y haya pasado
    /// <see cref="Motion.BounceRepeatEveryMs"/> desde el último bote; lo apaga cuando ya no aplica
    /// ninguno de los dos. Elapsed-driven; con reduce-motion no se dispara (offset 0 en el paint).
    /// El bote de celebración da "vida" al gato Happy (antes la celebración solo cambiaba color/chip).
    /// </summary>
    private void SyncBounce()
    {
        bool wantsBounce = _liveView.GlobalPhase.NeedsAttention() || CelebrationActive();
        if (_reduceMotion || !wantsBounce || !_cfg.LiveSessionsEnabled || !_cfg.ShowMascot)
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

    // Grip de redimensionado libre (v0.5, pedido del usuario: "deixe o tamanho para eu escolher ali
    // puxando"): esquina inferior derecha, solo en la vista "meter" ("data"). Arrastrarlo recalcula
    // Dpi.UserScale en vivo a partir del ancho elegido, y al soltar lo persiste en cfg.PanelScale —
    // así comparte el mismo mecanismo que los botones 85/100/115/130% de Ajustes.
    private Rectangle _resizeRect;
    private bool _resizing;
    private int _resizeStartWidth;
    private int _resizeStartMouseX;

    /// <summary>Zona CLICABLE del grip: más generosa que su dibujo (ley de Fitts — un objetivo de 16px
    /// en una esquina es difícil de acertar, y el usuario reportó no conseguir agarrarlo).</summary>
    private Rectangle ResizeHitRect =>
        _resizeRect.IsEmpty ? Rectangle.Empty : Rectangle.Inflate(_resizeRect, Dpi.Scale(8), Dpi.Scale(8));

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
    private static int SettingsViewportBottomPad => Dpi.Scale(12); // aire entre el final del viewport y el borde (T11)
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
    public event Action<string>? OverviewRangeChanged;

    /// <summary>Emitido cuando el panel de ajustes cambia un valor: el host lo aplica vía MutateConfig.</summary>
    public event Action<Action<AppConfig>>? SettingsChanged;

    /// <summary>Emitido cuando se clica una fila del panel cuya clave NO es mutación simple de config
    /// (claves "special:*", p.ej. "special:importtheme"/"special:hooktoggle"). El host las maneja.</summary>
    public event Action<string>? SpecialActionRequested;

    /// <summary>
    /// Cambia a la vista de ajustes (⚙). F10 (v0.3.9, state-preservation): NO resetea
    /// <see cref="_settingsScroll"/> — reabrir el panel conserva la posición de scroll de la sesión
    /// (antes saltaba siempre arriba). El acumulador sub-píxel de la rueda sí se limpia (residuo de
    /// trackpad, irrelevante para la posición). <see cref="LayoutContent"/> ya ACOTA el scroll guardado
    /// con <see cref="DashboardSettingsView.ClampScroll"/> en cada pasada, así que si el contenido
    /// encogió (p.ej. menos filas) el pulgar NUNCA queda fuera de rango. Reajusta el alto y repinta.
    /// </summary>
    public void ShowSettings() { _viewMode = "settings"; _settingsWheelAccum = 0; Width = Dpi.Scale(BaseWidth); Relayout(); Invalidate(); }

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

    // ---- DPI (T11, §2 P0 #1): geometría base del panel en px de diseño (96 DPI) ----
    // Las fuentes (en puntos) escalaban solas con el DPI del Graphics; la geometría era px fijos →
    // al 125/150% el texto crecía dentro de filas que no, con solapes y un panel de 340px enano.
    // ApplyDpiScale proyecta ancho/padding con Dpi.Scale; el alto lo recalcula Relayout (auto-fit).
    private const int BaseWidth = 340, BaseHeight = 380, BasePanelPad = 18;
    private const int DividerW = 16; // vertical gap + divider between columns
    private const int DualBaseWidth = BaseWidth * 2 + DividerW;

    public DashboardForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(Dpi.Scale(DualBaseWidth), Dpi.Scale(BaseHeight));
        BackColor = _theme.Background;
        DoubleBuffered = true;
        TopMost = true;
        Padding = new Padding(Dpi.Scale(BasePanelPad));

        _tick = new System.Windows.Forms.Timer { Interval = Motion.SlowTickMs };
        _tick.Tick += (_, _) => OnMotionTick();
    }

    /// <summary>
    /// Sincroniza el factor de escala ambiente (<see cref="Dpi.Apply"/>) con el DPI real de la ventana
    /// y reaplica la geometría base (ancho fijo + padding); el alto lo recalcula <see cref="Relayout"/>.
    /// Se llama al abrir (<see cref="ShowConfigured"/>) y al cambiar de monitor (<see cref="OnDpiChanged"/>).
    /// A propósito NO se llama desde <see cref="PrepareForRender"/>: el harness de render queda a
    /// factor 1.0 determinista (PNG idénticos a 96 DPI sea cual sea el monitor de la máquina).
    /// </summary>
    private void ApplyDpiScale()
    {
        Dpi.Apply(DeviceDpi);
        Width = _viewMode == "settings" ? Dpi.Scale(BaseWidth) : Dpi.Scale(DualBaseWidth);
        Padding = new Padding(Dpi.Scale(BasePanelPad));
    }

    /// <summary>
    /// Sincroniza <see cref="Dpi.UserScale"/> con <see cref="AppConfig.PanelScale"/> (ajuste "Tamanho do
    /// painel") y reaplica la geometría base. Idempotente y barato: se llama en cada refresh de datos y
    /// al abrir el panel, así que un cambio del ajuste se refleja sin reiniciar la app.
    /// </summary>
    /// <summary>Rango de "Tamaño del panel": los botones de Ajustes ofrecen 85–130%, pero el arrastre
    /// libre del grip (v0.5) permite ir mucho más lejos — pantallas dedicadas (p.ej. una tablet como
    /// tercer monitor) piden tamaños grandes que un combo de 4 opciones no cubre.</summary>
    internal const double MinPanelScale = 0.5, MaxPanelScale = 3.0;

    private void ApplyPanelScale(AppConfig cfg)
    {
        Dpi.UserScale = (float)Math.Clamp(cfg.PanelScale, MinPanelScale, MaxPanelScale);
        ApplyDpiScale();
    }

    /// <summary>
    /// WM_DPICHANGED del form top-level (colocación/arrastre a un monitor con otro DPI bajo
    /// PerMonitorV2): re-escala la geometría y re-layouta. Nota: el <c>OnDpiChangedAfterParent</c> que
    /// cita el spec aplica a CONTROLES hijos; en un Form top-level el aviso llega por
    /// <see cref="Form.OnDpiChanged"/> — se overridean ambos para cubrir también un reparenting futuro.
    /// </summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDpiScale();
        Relayout();
        Invalidate();
    }

    /// <inheritdoc cref="OnDpiChanged"/>
    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiScale();
        Relayout();
        Invalidate();
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
        ApplyPanelScale(cfg);
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
        // T-v039 F3c: si la celebración acaba de encenderse, arranca el bote de la mascota YA (no
        // espera al siguiente fast-tick) para que el gato Happy también "salte" al renovarse la cuota.
        SyncBounce();
        // Si algo arrancó a animar, asegúrate de que el reloj rápido esté latiendo (panel visible).
        if (Visible && (_motion.IsAnimating || CelebrationActive() || BounceActive()) && !_reduceMotion) EnsureFastTick();

        if (IsHandleCreated)
            BeginInvoke(() =>
            {
                Relayout();
                Invalidate();
                if (Visible && cfg.ShowChart) _ = ReloadChart();
            });
    }

    /// <summary>
    /// Updates overview stats from transcript records. Called by TrayAppContext after each data refresh.
    /// </summary>
    public void UpdateOverviewStats(IReadOnlyList<Models.UsageRecord> records)
    {
        DateTime? since = _overviewRange switch
        {
            "30d" => DateTime.UtcNow.AddDays(-30),
            "7d" => DateTime.UtcNow.AddDays(-7),
            _ => null
        };
        _overviewStats = UsageStatsService.Compute(records, since);
        _overviewRecords = records;
        if (IsHandleCreated) BeginInvoke(() => { Relayout(); Invalidate(); });
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
        ApplyPanelScale(cfg);
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
        // T11b (§3 #18): la pantalla de referencia se captura AL ABRIR con el cursor — el usuario
        // acaba de clicar el tray en ESA pantalla. PlaceWindow y el tope de alto de ajustes la
        // comparten vía TargetScreen() (antes: PrimaryScreen vs FromControl vs FromPoint, cada uno
        // la suya → panel en la pantalla equivocada en multi-monitor).
        _openScreen = Screen.FromPoint(Cursor.Position);
        // T11a: proyecta la geometría base al DPI real de la ventana antes de medir/colocar. Si al
        // colocarse cambia de monitor (otro DPI), OnDpiChanged re-escala y re-layouta (baile PMv2).
        ApplyDpiScale();
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
        ApplyRoundedRegion();
        // Bug real reportado: arrastrar el panel a un segundo monitor con OTRO DPI dispara
        // OnDpiChanged→Relayout a MITAD del arrastre; como cfg.DashboardPosition todavía no pasó a
        // "Custom" (eso solo ocurre en OnMouseUp, al soltar), PlaceWindow lo TELEPORTABA de vuelta a la
        // posición configurada (p.ej. "BottomRight"), compitiendo con el arrastre del usuario y dando la
        // sensación de que el panel "no se deja mover" al segundo monitor. Mientras _dragging es true, la
        // posición la manda el propio arrastre (OnMouseMove) — no PlaceWindow.
        // v0.5: mientras se arrastra el grip de redimensionado (_resizing), tampoco se debe reposicionar
        // — Location se queda fijo y solo crecen Width/Height, así el panel se expande desde la esquina
        // opuesta a la que el usuario está arrastrando (comportamiento natural de resize).
        if (!_dragging && !_resizing) PlaceWindow(_cfg);
        _appliedPlacement = PlacementKey(_cfg);
    }

    private static string PlacementKey(AppConfig cfg) => $"{cfg.DashboardPosition}|{cfg.DashboardX}|{cfg.DashboardY}";

    private Screen? _openScreen; // pantalla donde se abrió por última vez (cursor en ShowConfigured)

    /// <summary>
    /// Pantalla de referencia ÚNICA del panel (T11b, §3 #18). Antes cada consumidor resolvía la suya:
    /// <c>PlaceWindow</c> SIEMPRE PrimaryScreen (panel en la pantalla equivocada en multi-monitor),
    /// el tope de alto de ajustes <c>FromControl</c> y el clamp <c>FromPoint</c>. Ahora: visible ⇒ la
    /// pantalla donde el panel ESTÁ (estable: un refresh de datos con el cursor en otro monitor no lo
    /// teletransporta); oculto ⇒ la pantalla del cursor capturada al abrir (<see cref="ShowConfigured"/>),
    /// con PrimaryScreen de fallback (p.ej. el render-test, que nunca se muestra → determinista).
    /// (El clamp de posiciones "Custom" sigue siendo por-punto en <see cref="ClampToScreen"/>: un punto
    /// guardado en el monitor 2 se acota DENTRO del monitor 2, no se arrastra a la pantalla activa.)
    /// </summary>
    private Screen TargetScreen() =>
        Visible ? Screen.FromRectangle(Bounds) : _openScreen ?? Screen.PrimaryScreen!;

    private void PlaceWindow(AppConfig cfg)
    {
        var wa = TargetScreen().WorkingArea;
        int m = Dpi.Scale(8); // margen al borde de pantalla (T11: escala con el DPI)
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

        // Grip de redimensionado (v0.5): tiene prioridad sobre el arrastre de la ventana — vive en la
        // esquina inferior derecha, zona que si no caería en el "arrastrar panel" genérico del final.
        if (_viewMode == "data" && ResizeHitRect.Contains(e.Location))
        {
            _resizing = true;
            _resizeStartWidth = Width;
            _resizeStartMouseX = Cursor.Position.X;
            Cursor = Cursors.SizeNWSE;
            return;
        }

        // ⚙ → abrir ajustes (solo en la vista de datos). ShowSettings resetea además el scroll.
        if (_viewMode == "data" && _gearRect.Contains(e.Location))
        {
            ShowSettings(); return;
        }

        // Vista de ajustes: ‹ vuelve a datos; cada fila clicada emite su mutación. Sin drag aquí.
        if (_viewMode == "settings")
        {
            if (_backRect.Contains(e.Location)) { _viewMode = "data"; Width = Dpi.Scale(DualBaseWidth); Relayout(); Invalidate(); return; }
            foreach (var (key, r) in _settingsRects)
            {
                if (r.Contains(e.Location))
                {
                    if (DashboardSettingsView.ActionFor(key) is { } a)
                    {
                        // T14: aplica a mutação LOCALMENTE antes de disparar o evento (que faz refresh
                        // assíncrono da API e pode levar segundos). Sem isso, o PanelScale/Theme/etc.
                        // só surtiria efeito após o fetch completar — o painel repintava imediatamente
                        // com o valor ANTIGO (bug real: "as fontes não se ajustam").
                        a(_cfg);
                        ApplyPanelScale(_cfg);
                        _s = Localization.ForConfig(_cfg);
                        _theme = ThemeResolver.Resolve(_cfg);
                        BackColor = _theme.Background;
                        SettingsChanged?.Invoke(a);
                    }
                    else SpecialActionRequested?.Invoke(key); // claves "special:*": diálogo/instalador en el host
                    Relayout();
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

        // Overview section: tab clicks (range selector)
        foreach (var (range, rect) in _overviewTabRects)
        {
            if (rect.Contains(e.Location))
            {
                if (_overviewRange != range)
                {
                    _overviewRange = range;
                    // Recompute stats for the new range — fire event to TrayAppContext
                    OverviewRangeChanged?.Invoke(range);
                }
                Relayout(); Invalidate();
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
        if (_resizing)
        {
            // El ancho arrastrado se traduce a factor de escala respecto al ancho de DISEÑO al DPI del
            // monitor (BaseWidth·factorDPI): así el resize es equivalente a mover el mismo Dpi.UserScale
            // que usan los botones 85/100/115/130% — una sola fuente de verdad para el tamaño, en vez de
            // un segundo mecanismo paralelo. El alto lo recalcula Relayout (auto-fit del contenido).
            int targetWidth = _resizeStartWidth + (Cursor.Position.X - _resizeStartMouseX);
            float dpiOnlyBaseW = Math.Max(1, Dpi.Scale(DualBaseWidth, Dpi.FactorFor(DeviceDpi)));
            double scale = Math.Clamp(targetWidth / dpiOnlyBaseW, MinPanelScale, MaxPanelScale);
            if (Math.Abs(scale - Dpi.UserScale) > 0.001)
            {
                Dpi.UserScale = (float)scale;
                ApplyDpiScale();   // reaplica ancho + padding con el nuevo factor
                Relayout();        // re-mide el alto (no reposiciona: _resizing lo bloquea)
                Invalidate();
            }
            return;
        }
        if (_dragging)
        {
            Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
            return;
        }
        // Hover (Tarea 3): clave del rect interactivo bajo el cursor (precedencia estable, ver
        // HoveredRects). Cursor Hand cuando hay clave. Si la clave cambia, arranca/rearma el fade-in
        // del realce y repinta (bajo demanda, panel visible).
        string? key = HoverHitTest.Resolve(e.Location, HoveredRects());
        // Cursor de resize sobre el grip (afordancia: sin esto no se ve que la esquina es arrastrable).
        bool overGrip = _viewMode == "data" && ResizeHitRect.Contains(e.Location);
        Cursor = overGrip ? Cursors.SizeNWSE : key is not null ? Cursors.Hand : Cursors.Default;
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
        if (_resizing)
        {
            _resizing = false;
            // Persistir el tamaño elegido en la MISMA clave que usan los botones de Ajustes: al reabrir
            // el panel, ApplyPanelScale(cfg) lo restaura sin lógica extra.
            _cfg.PanelScale = Dpi.UserScale;
            float chosen = Dpi.UserScale;
            SettingsChanged?.Invoke(c => c.PanelScale = chosen);
            Relayout();   // ya sin el bloqueo de _resizing: recoloca según la posición configurada
            Invalidate();
            return;
        }
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
        // Textura de fondo (estética, "sparkle"/card look): puntos diagonales muy sutiles detrás de
        // TODO — se pinta primero y el contenido normal la tapa donde haga falta, así no afecta layout.
        DrawBackgroundTexture(e.Graphics);
        // Realce de hover DETRÁS del contenido: usa los rects de la pasada anterior (los mismos sobre
        // los que OnMouseMove resolvió la clave). Nunca toca el layout — es decoración bajo el texto.
        DrawHoverHighlight(e.Graphics);
        LayoutContent(e.Graphics, draw: true);
        // Borde del "card": se pinta AL FINAL para quedar siempre nítido por encima de cualquier fila
        // que roce el borde. Puramente decorativo — no reserva espacio ni mueve el layout.
        DrawCardBorder(e.Graphics);
    }

    /// <summary>
    /// Puntos diagonales muy tenues (≈7% alfa sobre <see cref="Theme.Foreground"/>) repartidos en
    /// rejilla al tresbolillo por todo el panel: da la textura "card" estética pedida por el usuario,
    /// sin tocar ninguna medida de layout (se pinta antes que todo, el contenido la tapa encima).
    /// </summary>
    private void DrawBackgroundTexture(Graphics g)
    {
        int step = Dpi.Scale(14);
        if (step <= 0) return;
        using var dotBrush = new SolidBrush(Color.FromArgb(18, _theme.Foreground));
        int row = 0;
        for (int yy = 0; yy < Height; yy += step, row++)
        {
            int offset = (row % 2 == 0) ? 0 : step / 2;
            for (int xx = offset; xx < Width; xx += step)
                g.FillRectangle(dotBrush, xx, yy, 1, 1);
        }
    }

    /// <summary>Borde fino tipo "card" alrededor de todo el panel (estética, sin reservar espacio).</summary>
    private void DrawCardBorder(Graphics g)
    {
        using var pen = new Pen(_theme.AccentText, 1f);
        Shapes.DrawRounded(g, pen, new Rectangle(0, 0, Width, Height), Dpi.Scale(12));
    }

    /// <summary>
    /// Clips the form itself to a rounded rectangle so Windows doesn't show a square shadow.
    /// </summary>
    private void ApplyRoundedRegion()
    {
        int radius = Dpi.Scale(12);
        using var path = Shapes.RoundedRectPath(new Rectangle(0, 0, Width, Height), radius);
        Region = new Region(path);
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
        var padded = Rectangle.Inflate(r, Dpi.Scale(Spacing.Xs), Dpi.Scale(Spacing.Xs) / 2);
        using var b = new SolidBrush(bg);

        // Filas de ajustes: el rect ya viene intersecado con el viewport, pero el Inflate vuelve a
        // sacarlo (±2px) → clip al MISMO viewport que usa LayoutContent para pintar el contenido.
        if (_viewMode == "settings" && hovered?.StartsWith("set:", StringComparison.Ordinal) == true)
        {
            int viewportH = Math.Max(0, Height - _settingsViewportTop - SettingsViewportBottomPad);
            var prevClip = g.Clip;
            g.SetClip(new Rectangle(0, _settingsViewportTop, Width, viewportH));
            try { Shapes.FillRounded(g, b, padded, Dpi.Scale(Spacing.Sm)); }
            finally { g.Clip = prevClip; }
            return;
        }
        Shapes.FillRounded(g, b, padded, Dpi.Scale(Spacing.Sm));
    }

    /// <summary>Walks the sections top-to-bottom. Returns the required window height.</summary>
    private int LayoutContent(Graphics g, bool draw)
    {
        // T14: TODAS las fuentes del sistema de diseño se escalan cuando Dpi.UserScale ≠ 1.0.
        // Las cacheadas de Typography no deben disponerse nunca (viven toda la app); las escaladas
        // SÍ se disponen al final del bloque try/finally.
        bool scaleFonts = Math.Abs(Dpi.UserScale - 1f) >= 0.001f;
        Font titleFont = scaleFonts ? ScaledFont(Typography.Title) : Typography.Title;
        Font planFont = scaleFonts ? ScaledFont(Typography.Caption) : Typography.Caption;
        Font labelFont = scaleFonts ? ScaledFont(Typography.Body) : Typography.Body;
        Font smallFont = scaleFonts ? ScaledFont(Typography.Caption) : Typography.Caption;
        using var fg = new SolidBrush(_theme.TextPrimary);
        using var dim = new SolidBrush(_theme.TextSecondary);
        try
        {

        int x = Padding.Left;
        int y = Padding.Top;
        int w = Width - Padding.Horizontal;

        if (_viewMode == "settings")
        {
            // Chrome (título + plan + botón cerrar): SOLO en Ajustes. La vista "meter" (v0.4, pedida
            // por el usuario: "literalmente só essas informações, nada além") tiene su propia cabecera
            // — ver DrawMeterHeader más abajo — sin este chrome ni el resto de secciones/footer.
            if (draw)
            {
                g.DrawString("ClaudeBar", titleFont, fg, x, y);
                // T11: el chrome (botón ✕ y avance de la banda de título) escala con el DPI.
                _closeRect = new Rectangle(Width - Dpi.Scale(26), Dpi.Scale(10), Dpi.Scale(18), Dpi.Scale(18));
                // T8c: el plan ("Max 20x · resets…") se elide antes de la columna del ✕ — un display largo
                // se pintaba de largo bajo el botón y rebasaba el borde del panel. Solo pintado (el avance
                // y += 50 no cambia) → medir==pintar.
                string planShown = TextWrap.FitLine(_plan.Display, x, _closeRect.X, Dpi.Scale(Spacing.Sm),
                    t => g.MeasureString(t, planFont).Width);
                g.DrawString(planShown, planFont, dim, x, y + Dpi.Scale(24));
                using var closeFont = new Font("Segoe UI", 11f * Dpi.UserScale, FontStyle.Bold);
                g.DrawString("✕", closeFont, dim, _closeRect.X, _closeRect.Y - 2);
            }
            y += Dpi.Scale(50);

            // ----- Vista de ajustes: alto LIMITADO + scroll (v0.3.7) -----
            // El panel medía más que la pantalla ("de arriba a abajo"). Ahora: chrome + "‹ Ajustes" fijos
            // arriba; el contenido se mide ENTERO (pasada de medida con dict desechable), el alto de la
            // ventana se acota a MaxPanelHeightPct del área útil, y el contenido se pinta desplazado
            // -_settingsScroll con CLIP al viewport. Los rects clicables salen de la pasada de pintado (ya
            // desplazados) y se INTERSECAN con el viewport para que una fila medio fuera no responda bajo
            // el chrome. Con todo visible no hay barra ni scroll (comportamiento de siempre).
            // F11 (v0.3.9): la etiqueta de volver usa _s.Back ("‹ Volver"/"‹ Back"/"‹ Zurück"…), NO
            // _s.Settings ("‹ Ajustes"): el chevron + el nombre de la sección se leía como TÍTULO, no
            // como afordancia de volver. _s.Back existe en los 9 idiomas (revive el string "muerto").
            string backLabel = "‹ " + _s.Back;
            if (draw)
            {
                using var bb = new SolidBrush(_theme.TextSecondary);
                g.DrawString(backLabel, labelFont, bb, x, y);
            }
            // T8d: la zona de clic se mide con el texto localizado (el 80×20 fijo dejaba media
            // etiqueta DE/FR/NL sin responder al clic).
            _backRect = DashboardSettingsView.BackHitRect(g, backLabel, labelFont, x, y, w);
            y += Dpi.Scale(24); // T11: fila "‹ Ajustes" escalada
            int contentTop = y;
            _settingsViewportTop = contentTop;

            // 1) Medir el contenido completo (sin tope) — el dict es desechable: los rects buenos
            //    (desplazados) los registra la pasada de pintado de abajo.
            _scratchRects.Clear();
            _settingsContentH = DashboardSettingsView.Draw(g, draw: false, x, contentTop, w,
                _cfg, _s, _theme, labelFont, smallFont, _scratchRects) - contentTop;

            // 2) Tope de alto: % del área útil de la pantalla de referencia (T11b: la MISMA que usa
            //    PlaceWindow vía TargetScreen, antes FromControl podía resolver otra en multi-monitor).
            var wa = TargetScreen().WorkingArea;
            int maxH = wa.Height * DashboardSettingsView.MaxPanelHeightPct / 100;
            int fullH = contentTop + _settingsContentH + Dpi.Scale(18);
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

        // ----- Vista "meter" (v0.4) con layout de duas colunas: esquerda (gauges+overview),
        // direita (spend+chart+models). Um único form, sem painéis separados. -----
        _closeRect = Rectangle.Empty;
        _gearRect = Rectangle.Empty;
        _settingsRects.Clear();
        _backRect = Rectangle.Empty;

        // Duas colunas: a largura total é DualBaseWidth escalado
        int divGap = Dpi.Scale(DividerW);
        int colW = (Width - Padding.Horizontal - divGap) / 2;
        int leftX = x;
        int rightX = x + colW + divGap;
        int rightColW = Width - rightX - Padding.Right;
        // Left column uses colW, not the full w
        w = colW;

        // Clip left column so nothing overflows into the right
        if (draw) g.SetClip(new Rectangle(0, 0, leftX + colW + divGap / 2, Height));

        y = DrawMeterHeader(g, draw, x, y, w);

        var usage = _snap?.Usage;
        if (usage is null)
        {
            if (draw)
            {
                string msg = _snap is null ? _s.Loading : UsageFormat.StateMessage(_snap.LatestState, _s);
                g.DrawString(msg, labelFont, dim, x, y);
            }
            return y + Dpi.Scale(24) + Dpi.Scale(Spacing.Md);
        }

        // Override eased del ancho/número (color por objetivo): muestrea el MotionState por clave de
        // barra, igual que hacía DashboardDataView.DrawQuotaBody antes de que esta vista lo sustituyera.
        double? Eased(string key, UsageWindow? win) =>
            win is null ? null : _motion?.Display(key, win.UtilizationPct, _reduceMotion);

        // --- Mockup 2: dual gauge cards lado a lado (5h | 7d) ---
        int gap = QuotaGauge.CardGap;
        int cardW = (w - gap) / 2;

        QuotaGauge.DrawCard(g, draw, $"{_s.SessionWord} (5h)", usage.FiveHour, _snap?.PaceFive,
            x, y, cardW, _cfg, _s, _theme, smallFont, Eased("bar:5h", usage.FiveHour));
        QuotaGauge.DrawCard(g, draw, $"{_s.WeekWord} (7d)", usage.SevenDay, _snap?.PaceSeven,
            x + cardW + gap, y, cardW, _cfg, _s, _theme, smallFont, Eased("bar:7d", usage.SevenDay));

        y += QuotaGauge.CardHeight + Dpi.Scale(8);

        // --- Pace summary tags ---
        y = DashboardDataView.DrawPaceTags(g, draw, _snap, _s, _theme, x, y, w, smallFont);

        // --- Última atualização (timestamp discreto) ---
        if (_snap is not null)
        {
            y += Dpi.Scale(6);
            DateTime now = _footerNowUtc == DateTime.MinValue ? DateTime.UtcNow : _footerNowUtc;
            string updatedText = $"{_s.UpdatedAt} · {UsageFormat.RelativeAt(_snap.UsageAtUtc, now, _s)}";
            if (draw)
            {
                using var mutedBrush = new SolidBrush(_theme.TextMuted);
                using var updatedFont = new Font(Typography.Caption.FontFamily, 8.5f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
                var sz = g.MeasureString(updatedText, updatedFont);
                g.DrawString(updatedText, updatedFont, mutedBrush, x + (w - sz.Width) / 2, y);
            }
            y += Dpi.Scale(16);
        }

        // --- Visão Geral (Overview) section ---
        {
            // Section header: "▾ Visão Geral" + range tabs (Todos/30d/7d)
            using var overviewLabelFont = new Font(labelFont.FontFamily, 9.5f * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);
            using var overviewTabFont = new Font(Typography.Caption.FontFamily, 8.5f * Dpi.UserScale, FontStyle.Regular, GraphicsUnit.Point);
            var overviewHeaderRect = new Rectangle(x, y, w, Dpi.Scale(16));
            if (draw)
            {
                using var hdrBrush = new SolidBrush(_theme.TextPrimary);
                using var sepPen = new Pen(_theme.Separator);
                g.DrawLine(sepPen, x, y - Dpi.Scale(2), x + w, y - Dpi.Scale(2));
                g.DrawString((_overviewCollapsed ? "▸ " : "▾ ") + _s.SectionOverview, overviewLabelFont, hdrBrush, x, y);

                // Range tabs (right-aligned)
                _overviewTabRects.Clear();
                var tabs = new[] { (_s.OverviewAll, "all"), ("30d", "30d"), ("7d", "7d") };
                DashboardDataView.DrawSegments(g, draw, overviewTabFont, _theme,
                    tabs, _overviewRange, x + w, y, rightAlign: true, _overviewTabRects);
            }
            else
            {
                _overviewTabRects.Clear();
                using var tempG = Graphics.FromHwnd(IntPtr.Zero);
                var tabs = new[] { (_s.OverviewAll, "all"), ("30d", "30d"), ("7d", "7d") };
                DashboardDataView.DrawSegments(tempG, false, overviewTabFont, _theme,
                    tabs, _overviewRange, x + w, y, rightAlign: true, _overviewTabRects);
            }
            y += Dpi.Scale(20);

            if (!_overviewCollapsed)
                y = OverviewSection.Draw(g, draw, x, y, w, _overviewStats, _s, _theme, smallFont);
        }

        // Estado NO-Ok: línea discreta de aviso. En el camino feliz NO ocupa NADA — el panel queda
        // exactamente como la referencia que pidió el usuario. Pero al enjugar el panel a "solo las 2
        // barras" se perdió el footer que antes decía "⚠ Límite de peticiones · datos anteriores", y con
        // la API en 429 el panel se quedaba CONGELADO mostrando el último dato bueno (ceros y
        // "reiniciando…") sin explicar por qué (bug real reportado: "fica reiniciando e não sai disso").
        // Un panel que se congela en silencio es peor que uno con una línea de más: el aviso vuelve,
        // pero solo cuando hace falta.
        if (_snap is not null && _snap.LatestState != UsageFetchState.Ok)
        {
            y += Dpi.Scale(Spacing.Sm);
            if (draw)
            {
                string msg = $"⚠ {UsageFormat.StateMessage(_snap.LatestState, _s)} · {_s.PreviousDataFooter}";
                msg = TextWrap.FitLine(msg, x, x + w, 0, t => g.MeasureString(t, smallFont).Width);
                using var warnBrush = new SolidBrush(_theme.WarnText);
                g.DrawString(msg, smallFont, warnBrush, x, y);
            }
            y += Dpi.Scale(15);
        }

        // Restore clip before right column
        if (draw) g.ResetClip();

        // --- RIGHT COLUMN: divider + spend + chart + model breakdown ---
        {
            int ry = Padding.Top; // right column starts at the top

            // Vertical divider line (centered in the gap)
            if (draw)
            {
                int divX = leftX + colW + divGap / 2;
                using var divPen = new Pen(_theme.Separator);
                g.DrawLine(divPen, divX, Padding.Top, divX, Math.Max(y, ry + Dpi.Scale(200)));
            }

            // Clip right column
            if (draw) g.SetClip(new Rectangle(rightX, 0, rightColW + Padding.Right, Height));

            // "Detalhes" header
            if (draw)
            {
                using var detailTitleFont = new Font(Typography.Title.FontFamily, 11f * Dpi.UserScale, FontStyle.Bold, GraphicsUnit.Point);
                g.DrawString("Detalhes", detailTitleFont, fg, rightX, ry);
            }
            ry += Dpi.Scale(20);

            // Spend bars
            ry = DetailColumnRenderer.DrawSpendBars(g, draw, rightX, ry, rightColW, _snap?.Spend, _s, _theme, smallFont, dim);

            // Chart
            ry = DetailColumnRenderer.DrawChart(g, draw, rightX, ry, rightColW, _chartData, _s, _theme, smallFont, dim);

            // Model breakdown
            if (_overviewRecords is not null)
                ry = DetailColumnRenderer.DrawModelBreakdown(g, draw, rightX, ry, rightColW, _overviewRecords, _s, _theme, smallFont, dim);

            // Restore clip
            if (draw) g.ResetClip();

            // Make height = max of left and right columns
            y = Math.Max(y, ry);
        }

        y += Dpi.Scale(Spacing.Sm);
        // Grip de redimensionado (v0.5): rayas diagonales en la esquina inferior derecha. Se le reserva
        // su PROPIA fila (el return suma gripSize+margen) — antes se anclaba a "y - gripSize + 2", con
        // lo que su borde inferior caía en Height+2, es decir 2px POR DEBAJO del área visible: se veía
        // cortado y era casi imposible de agarrar (bug real reportado: "continua sem eu conseguir
        // decidir o tamanho"). Su rect se registra SIEMPRE (no solo al pintar) para que el hit-test
        // funcione en el mismo frame que se mide.
        int gripSize = Dpi.Scale(16);
        int gripMargin = Dpi.Scale(4);
        _resizeRect = new Rectangle(Width - gripSize - gripMargin, y, gripSize, gripSize);
        if (draw)
        {
            // Acento (no TextMuted) y trazo de 2px: es una afordancia, tiene que VERSE.
            using var gripPen = new Pen(_theme.AccentText, Math.Max(1.5f, Dpi.Scale(2)));
            for (int i = 1; i <= 3; i++)
            {
                int off = i * Dpi.Scale(5);
                g.DrawLine(gripPen,
                    _resizeRect.Right - off, _resizeRect.Bottom,
                    _resizeRect.Right, _resizeRect.Bottom - off);
            }
        }
        return y + gripSize + gripMargin;
        }
        finally
        {
            if (scaleFonts) { titleFont.Dispose(); planFont.Dispose(); labelFont.Dispose(); smallFont.Dispose(); }
        }
    }

    /// <summary>Clona una fuente del sistema de diseño con su tamaño multiplicado por
    /// <see cref="Dpi.UserScale"/> ("Tamaño del panel"). El llamador la dispone.</summary>
    private static Font ScaledFont(Font f) => new(f.FontFamily, f.Size * Dpi.UserScale, f.Style);

    /// <summary>
    /// Cabecera de la vista "meter": punto LIVE (verde si el último fetch fue Ok, apagado si no) +
    /// título de marca + badge "USED" decorativo, replicando el print de referencia del usuario.
    /// Altura fija (no depende de draw) → medir(draw=false)==pintar(draw=true) trivialmente.
    /// </summary>
    private int DrawMeterHeader(Graphics g, bool draw, int x, int y, int w)
    {
        if (draw)
        {
            bool live = _snap?.Usage is not null && _snap.LatestState == UsageFetchState.Ok;
            Color liveColor = live ? _theme.Ok : _theme.TextMuted;

            int dotD = Dpi.Scale(8);
            int dotY = y + Dpi.Scale(5);
            using (var dotBrush = new SolidBrush(liveColor))
                g.FillEllipse(dotBrush, x, dotY, dotD, dotD);

            // "Tamaño del panel" (Dpi.UserScale) reescala la geometría de esta cabecera (dotD, padX...)
            // vía Dpi.Scale, pero los puntos de fuente NO siguen ese factor por defecto (solo el DPI
            // real del monitor) — a 85% "AO VIVO" quedaba cortado/montado sobre el título (bug real
            // reportado). Multiplicar el tamaño en puntos por UserScale mantiene el texto proporcional
            // a la geometría reescalada.
            float fscale = Dpi.UserScale;
            using var liveFont = new Font("Segoe UI", 8.5f * fscale, FontStyle.Bold);
            using var liveBrush = new SolidBrush(liveColor);
            g.DrawString(_s.MeterLive, liveFont, liveBrush, x + dotD + Dpi.Scale(Spacing.Xs), y);

            using var titleFont2 = new Font("Segoe UI", 11f * fscale, FontStyle.Bold);
            using var titleBrush = new SolidBrush(_theme.Foreground);
            const string title = "CLAUDE CODE METER";
            var titleSz = g.MeasureString(title, titleFont2);
            float titleX = x + (w - titleSz.Width) / 2f;
            g.DrawString(title, titleFont2, titleBrush, titleX, y - Dpi.Scale(1));

            using var badgeFont = new Font("Segoe UI", 7.5f * fscale, FontStyle.Bold);
            string badge = _s.MeterUsedBadge;
            var badgeTextSz = g.MeasureString(badge, badgeFont);
            int padX = Dpi.Scale(8), padY = Dpi.Scale(2);
            var badgeRect = new Rectangle(
                x + w - (int)Math.Ceiling(badgeTextSz.Width) - padX * 2,
                y - Dpi.Scale(2),
                (int)Math.Ceiling(badgeTextSz.Width) + padX * 2,
                (int)Math.Ceiling(badgeTextSz.Height) + padY * 2);
            using (var badgeBg = new SolidBrush(Color.FromArgb(55, _theme.Accent)))
                Shapes.FillRounded(g, badgeBg, badgeRect, badgeRect.Height / 2);
            using (var badgeBorder = new Pen(_theme.AccentText, 1f))
                Shapes.DrawRounded(g, badgeBorder, badgeRect, badgeRect.Height / 2);
            using var badgeBrush = new SolidBrush(_theme.AccentText);
            g.DrawString(badge, badgeFont, badgeBrush, badgeRect.X + padX, badgeRect.Y + padY);
        }
        return y + Dpi.Scale(30);
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
            bool sf = Math.Abs(Dpi.UserScale - 1f) >= 0.001f;
            using var footerFont = sf ? ScaledFont(Typography.Caption) : null;
            Font ff = footerFont ?? Typography.Caption;
            nowSig = FooterLayout.Signature(BuildFooterLines(w, str => g.MeasureString(str, ff).Width));
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
