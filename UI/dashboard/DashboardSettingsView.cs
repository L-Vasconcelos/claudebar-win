using System.Drawing.Drawing2D;
using ClaudeBarWin.Config;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Panel de ajustes dentro del dashboard (modo "settings"). Sin estado: dibuja las filas de
/// configuración agrupadas y registra cada control clicable en <c>rects</c> con una clave de acción
/// (p.ej. "toggle:ShowSpend", "theme:dark", "freq:60", "cycle:position", "special:importtheme").
/// <see cref="ActionFor"/> traduce las claves que son MUTACIÓN SIMPLE de <see cref="AppConfig"/> a la
/// mutación que <c>DashboardForm</c> emite por su evento <c>SettingsChanged</c>. Las claves "special:*"
/// NO tienen <see cref="ActionFor"/>: el host las enruta por <c>SpecialActionRequested</c> (acciones que
/// necesitan diálogo/instalador, como importar un .itermcolors o instalar/quitar los hooks).
/// Cada helper avanza y devuelve <c>y</c> idéntico en draw=false/true (simetría medir/pintar).
/// </summary>
public static class DashboardSettingsView
{
    // Posiciones del dashboard en orden de ciclo (sin "Custom": ese estado lo fija arrastrar el panel).
    private static readonly string[] PositionCycle = { "BottomRight", "BottomLeft", "TopRight", "TopLeft", "Center" };
    // Opacidades ofrecidas en el panel (como en el menú original, recortado a 3 para caber).
    private static readonly (string val, string txt)[] OpacitySegs =
        { ("1", "100%"), ("0.85", "85%"), ("0.7", "70%") };
    // Combinaciones de umbral warn/critical del menú original.
    private static readonly (double warn, double crit)[] ThresholdOptions = { (70, 90), (80, 95), (60, 85) };
    // Hitos de notificación individuales (toggles sobre NotifyMilestones).
    private static readonly int[] MilestoneOptions = { 25, 50, 75, 95 };

    /// <summary>Dibuja el panel y registra rects clicables con clave de acción. Devuelve nuevo y.
    /// <paramref name="version"/> es la versión a mostrar en "Acerca de"; si es null se resuelve desde
    /// el ensamblado en ejecución (inyectable para test).</summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w, AppConfig cfg, Strings s, Theme theme,
                           Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects, string? version = null)
    {
        rects.Clear();

        // -------- Mostrar (qué se ve en el dashboard) --------
        y = SectionHeader(g, draw, s.MenuSections, x, y, w, theme, smallFont);
        // Gasto estimado lleva subtítulo aclaratorio ("Coste equivalente por modelo").
        y = ToggleRow(g, draw, "toggle:ShowSpend", s.ShowSpend, s.ShowSpendSubtitle, cfg.ShowSpendEstimate, x, y, w, theme, labelFont, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowHealth", s.ShowServiceStatus, null, cfg.ShowHealth, x, y, w, theme, labelFont, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowChart", s.UsageChart, null, cfg.ShowChart, x, y, w, theme, labelFont, smallFont, rects);

        // -------- Sesiones en vivo (master hooks + StatusBadge + mascota dependiente) --------
        // Fila MAESTRA = instalar/quitar hooks con StatusBadge; Mostrar mascota / Tamaño / Silenciar son
        // DEPENDIENTES (sangrados Lg, atenuados+inertes mientras no haya hooks). Quita el control huérfano:
        // "Mostrar mascota" ya no es una fila suelta por encima del botón. (T0 hace que el gato Idle se vea
        // con ShowMascot on aunque live esté off; aquí la jerarquía master→dependiente comunica que la
        // mascota solo cobra vida con hooks.)
        y = LiveSessionsSection(g, draw, HookInstaller.IsInstalled(), x, y, w, cfg, s, theme, labelFont, smallFont, rects);

        // -------- Notificaciones (master + dependientes) --------
        // "Notificaciones" es el MASTER; PaceAlerts y los hitos son DEPENDIENTES: sangrados Spacing.Lg,
        // atenuados e INERTES cuando el master está off (vía DrawDependent). Resuelve "lo activé y no pasa
        // nada": el dependiente solo responde si el master está encendido.
        y = SectionHeader(g, draw, s.Notifications, x, y, w, theme, smallFont);
        y = ToggleRow(g, draw, "toggle:Notifications", s.Enabled, null, cfg.NotificationsEnabled, x, y, w, theme, labelFont, smallFont, rects);
        bool notif = cfg.NotificationsEnabled;
        y = DrawDependent(notif, x, y, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:PaceAlerts", s.PaceAlerts, null, cfg.PaceAlerts,
                ix, y, iw, th, labelFont, smallFont, rects));
        // Hitos individuales 25/50/75/95 como toggles multi-activo que editan el array NotifyMilestones:
        // un único MultiSegmentRow (varios activos, mismo estilo de pill Accent+Contrast), SIN re-pintado manual.
        int milestoneY = y; // captura el y de entrada del dependiente para la lambda
        y = DrawDependent(notif, x, milestoneY, w, theme, rects,
            (ix, iw, th) => MultiSegmentRow(g, draw, "milestone", s.NotifyWhenReaching,
                MilestoneOptions.Select(m => ($"{m}", $"{m}%")).ToArray(),
                cfg.NotifyMilestones ?? Array.Empty<int>(), ix, milestoneY, iw, th, smallFont, rects));

        // -------- Frecuencia de actualización --------
        y = SectionHeader(g, draw, s.UpdateFrequency, x, y, w, theme, smallFont);
        y = SegmentedRow(g, draw, "freq", "",
            new[] { ("30", s.Sec30), ("60", s.Min1), ("300", s.Min5), ("900", s.Min15) },
            cfg.RefreshSeconds.ToString(), x, y, w, theme, smallFont, rects);

        // -------- Icono --------
        y = SectionHeader(g, draw, s.MenuIcon, x, y, w, theme, smallFont);
        y = SegmentedRow(g, draw, "icon", s.IconMode,
            new[] { ("percent", "%"), ("pace", "▲"), ("both", "%▲") },
            string.IsNullOrEmpty(cfg.IconDisplayMode) ? "percent" : cfg.IconDisplayMode, x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "threshold", s.ColorThreshold,
            ThresholdOptions.Select(t => ($"{t.warn:0}/{t.crit:0}", $"{t.warn:0}/{t.crit:0}")).ToArray(),
            $"{cfg.WarnThresholdPct:0}/{cfg.CriticalThresholdPct:0}", x, y, w, theme, smallFont, rects);

        // -------- Apariencia --------
        // "Importar .itermcolors" se RETIRA de aquí (lo consolida T8 en "Acerca de"): aquí solo preferencias.
        y = SectionHeader(g, draw, s.MenuAppearance, x, y, w, theme, smallFont);
        y = SegmentedRow(g, draw, "theme", s.Theme,
            new[] { ("system", s.ThemeSystem), ("dark", s.ThemeDark), ("light", s.ThemeLight), ("cli", "CLI") },
            cfg.Theme, x, y, w, theme, smallFont, rects);
        // Posición: fila que cicla (5 opciones no caben en segmentos); muestra la posición actual.
        y = CycleRow(g, draw, "cycle:position", s.Position, PosLabel(cfg.DashboardPosition, s), x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "opacity", s.Opacity, OpacitySegs, FmtOpacity(cfg.DashboardOpacity), x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:Sticky", s.Sticky, null, cfg.DashboardSticky, x, y, w, theme, labelFont, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:OnTop", s.AlwaysOnTop, null, cfg.DashboardAlwaysOnTop, x, y, w, theme, labelFont, smallFont, rects);
        // "Reducir movimiento" lleva subtítulo aclaratorio ("Desactiva las animaciones").
        y = ToggleRow(g, draw, "toggle:ReduceMotion", s.ReduceMotion, s.ReduceMotionSubtitle, cfg.ReduceMotion, x, y, w, theme, labelFont, smallFont, rects);

        // -------- Sistema --------
        // i18n: el título sale de Strings (fin del literal hardcodeado). Agrupa Arrancar con Windows + Idioma
        // (el ciclo de idioma se trae aquí desde su antigua sección suelta: una sola pantalla, menos cabeceras).
        y = SectionHeader(g, draw, s.MenuSystem, x, y, w, theme, smallFont);
        y = ToggleRow(g, draw, "toggle:Startup", s.StartWithWindows, null, StartupManager.IsEnabled(), x, y, w, theme, labelFont, smallFont, rects);
        y = CycleRow(g, draw, "cycle:lang", s.Language,
            Localization.LanguageDisplayName(cfg.Language, s), x, y, w, theme, smallFont, rects);

        // -------- Acerca de (acciones al final, separadas de las preferencias) --------
        // Versión (texto, no clicable) + Importar .itermcolors (ButtonRow). "Importar tema" se consolida aquí
        // (sacado de Apariencia en T7) para no mezclar una ACCIÓN con las preferencias. Logs/GitHub solo se
        // pintarían si el host enrutara sus "special:*"; TrayAppContext NO los enruta hoy → no se dibujan (sin
        // botón muerto).
        y = SectionHeader(g, draw, s.About, x, y, w, theme, smallFont);
        y = InfoRow(g, draw, s.VersionLabel, "v" + (version ?? ResolveVersion()), x, y, w, theme, labelFont, rects);
        y = ButtonRow(g, draw, "special:importtheme", s.ImportTheme, theme.Accent, x, y, w, theme, labelFont, rects);

        return y;
    }

    /// <summary>Versión a mostrar en "Acerca de" cuando no se inyecta una: Major.Minor.Build del ensamblado.</summary>
    private static string ResolveVersion()
    {
        var v = typeof(DashboardSettingsView).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    /// <summary>
    /// Traduce la clave de un rect clicado a la mutación de config. Devuelve null para claves "special:*"
    /// (las enruta el host por SpecialActionRequested) o desconocidas.
    /// </summary>
    public static Action<AppConfig>? ActionFor(string key)
    {
        switch (key)
        {
            case "toggle:ShowSpend": return c => c.ShowSpendEstimate = !c.ShowSpendEstimate;
            case "toggle:ShowHealth": return c => c.ShowHealth = !c.ShowHealth;
            case "toggle:ShowChart": return c => c.ShowChart = !c.ShowChart;
            case "toggle:ShowMascot": return c => c.ShowMascot = !c.ShowMascot;
            case "toggle:Suppress": return c => c.SuppressWhenFocused = !c.SuppressWhenFocused;
            case "toggle:Notifications": return c => c.NotificationsEnabled = !c.NotificationsEnabled;
            case "toggle:PaceAlerts": return c => c.PaceAlerts = !c.PaceAlerts;
            case "toggle:Sticky": return c => c.DashboardSticky = !c.DashboardSticky;
            case "toggle:OnTop": return c => c.DashboardAlwaysOnTop = !c.DashboardAlwaysOnTop;
            case "toggle:ReduceMotion": return c => c.ReduceMotion = !c.ReduceMotion;
            case "toggle:Startup": return _ => StartupManager.Toggle();

            case "mascotsize:compact": return c => c.MascotSize = "compact";
            case "mascotsize:large": return c => c.MascotSize = "large";

            case "theme:system": return c => c.Theme = "system";
            case "theme:dark": return c => c.Theme = "dark";
            case "theme:light": return c => c.Theme = "light";
            case "theme:cli": return c => c.Theme = "cli";

            case "icon:percent": return c => c.IconDisplayMode = "percent";
            case "icon:pace": return c => c.IconDisplayMode = "pace";
            case "icon:both": return c => c.IconDisplayMode = "both";

            case "freq:30": return c => c.RefreshSeconds = 30;
            case "freq:60": return c => c.RefreshSeconds = 60;
            case "freq:300": return c => c.RefreshSeconds = 300;
            case "freq:900": return c => c.RefreshSeconds = 900;

            case "cycle:lang": return c => c.Language = Localization.NextLanguage(c.Language);
            case "cycle:position": return c => c.DashboardPosition = NextPosition(c.DashboardPosition);
        }

        // Opacidad: "opacity:<double>"
        if (key.StartsWith("opacity:") && double.TryParse(key.AsSpan("opacity:".Length),
                System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var op))
            return c => c.DashboardOpacity = op;

        // Umbral: "threshold:<warn>/<crit>"
        if (key.StartsWith("threshold:"))
        {
            var parts = key.AsSpan("threshold:".Length);
            int slash = parts.IndexOf('/');
            if (slash > 0
                && double.TryParse(parts[..slash], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var warn)
                && double.TryParse(parts[(slash + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var crit))
                return c => { c.WarnThresholdPct = warn; c.CriticalThresholdPct = crit; };
        }

        // Hito individual: "milestone:<pct>" (toggle dentro del array).
        if (key.StartsWith("milestone:") && int.TryParse(key.AsSpan("milestone:".Length), out var pct))
            return c =>
            {
                var list = (c.NotifyMilestones ?? Array.Empty<int>()).ToList();
                if (list.Contains(pct)) list.Remove(pct); else list.Add(pct);
                c.NotifyMilestones = list.Distinct().OrderBy(v => v).ToArray();
            };

        return null; // "special:*" o desconocida → no es mutación simple
    }

    // ---------------- ciclos / etiquetas ----------------

    private static string NextPosition(string current)
    {
        int i = Array.IndexOf(PositionCycle, string.IsNullOrEmpty(current) ? "BottomRight" : current);
        return PositionCycle[(i < 0 ? 0 : (i + 1) % PositionCycle.Length)];
    }

    private static string PosLabel(string key, Strings s) => key switch
    {
        "BottomRight" => s.PosBottomRight,
        "BottomLeft" => s.PosBottomLeft,
        "TopRight" => s.PosTopRight,
        "TopLeft" => s.PosTopLeft,
        "Center" => s.PosCenter,
        "Custom" => s.PosCustom,
        _ => key,
    };

    private static string FmtOpacity(double op)
    {
        double v = op <= 0 ? 1.0 : op;
        // Casa con OpacitySegs ("1"/"0.85"/"0.7") usando invariant culture.
        if (Math.Abs(v - 1.0) < 0.001) return "1";
        if (Math.Abs(v - 0.85) < 0.001) return "0.85";
        if (Math.Abs(v - 0.7) < 0.001) return "0.7";
        return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // ---------------- helpers de dibujo (simetría medir/pintar; registran rects con clave) ----------------

    // -------- ritmo vertical sobre rejilla 8pt (sin literales mágicos) --------
    // Alto de contenido de una fila estándar de 1 línea; el avance suma Spacing.Sm entre filas.
    private const int RowContentHeight = 18;
    private static int RowAdvance => RowContentHeight + Spacing.Sm;   // fila estándar = alto + Sm
    // Alto de un bloque de segmentos (coincide con DashboardDataView.DrawSegments: h=18).
    private const int SegmentHeight = 18;
    private static int SegmentRowAdvance => SegmentHeight + Spacing.Sm;

    // -------- TogglePill: cápsula+knob dibujada (sustituye ☑/☐) sobre rejilla 8pt --------
    // Track de 36×20 (múltiplos de 4) con knob circular ligeramente menor que el alto del track.
    private const int PillTrackW = 36;
    private const int PillTrackH = 20;
    private const int PillKnobInset = 2;                       // holgura del knob dentro del track
    private static int PillKnobDiameter => PillTrackH - PillKnobInset * 2;

    /// <summary>
    /// Encabezado de sección estilo Apple: caption en MAYÚSCULAS, tenue (<c>TextMuted</c>, más pequeña
    /// que el body), con un Divider de 1px (<c>Theme.Separator</c>) debajo. Reserva <c>Spacing.Md</c> de
    /// aire arriba y <c>Spacing.Sm</c> abajo. Mide==pinta: el avance es idéntico en ambas pasadas.
    /// </summary>
    internal static int SectionHeader(Graphics g, bool draw, string title, int x, int y, int w, Theme theme, Font f)
    {
        y += Spacing.Md; // aire arriba (separa del grupo anterior)
        int textH = (int)Math.Ceiling(g.MeasureString(title, f).Height);
        if (draw)
        {
            using var b = new SolidBrush(theme.TextMuted);
            g.DrawString(title.ToUpperInvariant(), f, b, x, y);
        }
        y += textH;
        if (draw)
        {
            // Divisor 1px dentro de [x, x+w], centrado en el aire inferior.
            int dy = y + Spacing.Sm / 2;
            using var pen = new Pen(theme.Separator, 1);
            g.DrawLine(pen, x, dy, x + w, dy);
        }
        return y + Spacing.Sm; // aire abajo (separa de la primera fila)
    }

    // -------- DrawDependent: sub-ajuste dependiente de un master (indent + atenuación + inercia) --------

    /// <summary>
    /// Atenúa un tema a ~0.5 hacia su fondo (para dibujar dependientes "apagados" cuando el master está
    /// off). PURO: interpola por canal los tokens de texto/acento/separador/elevado hacia
    /// <c>Background</c>; la geometría de las filas NO depende del color, así que medir==pintar se
    /// mantiene aunque el dependiente cambie de tema según el estado del master.
    /// </summary>
    private static Theme Dimmed(Theme t)
    {
        const double k = 0.5; // ~50% de opacidad sobre el fondo
        Color D(Color c) => ColorMath.Lerp(c, t.Background, k);
        return new Theme
        {
            Id = t.Id,
            Background = t.Background,
            Foreground = D(t.Foreground),
            Dim = D(t.Dim),
            Track = D(t.Track),
            Ok = D(t.Ok),
            Warn = D(t.Warn),
            Critical = D(t.Critical),
            Neutral = D(t.Neutral),
            Accent = D(t.Accent),
            BgElevated = D(t.BgElevated),
            TextMuted = D(t.TextMuted),
            Separator = D(t.Separator),
        };
    }

    /// <summary>
    /// Dibuja un sub-ajuste DEPENDIENTE de un control maestro: sangrado <c>Spacing.Lg</c> (indent), y
    /// cuando el master está <b>off</b> (<paramref name="active"/>=false) se pinta <b>atenuado</b>
    /// (~0.5 vía <see cref="Dimmed"/>) e <b>inerte</b> (sus rects NO quedan registrados → un clic no
    /// muta nada). La sangría reduce el ancho útil a <c>w - Spacing.Lg</c>. El <paramref name="drawInner"/>
    /// recibe el x sangrado, el ancho reducido y el tema efectivo, y devuelve el nuevo <c>y</c>; el
    /// avance es IDÉNTICO esté el master on u off (solo cambian color e inercia, no la geometría), de
    /// modo que medir==pintar y nada se mueve al activar/desactivar el master. Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int DrawDependent(bool active, int x, int y, int w, Theme theme,
        Dictionary<string, Rectangle> rects, Func<int, int, Theme, int> drawInner)
    {
        int ix = x + Spacing.Lg;        // sangría del dependiente
        int iw = w - Spacing.Lg;        // ancho útil reducido por la sangría
        Theme eff = active ? theme : Dimmed(theme);
        // Snapshot de claves para poder retirar las nuevas (inercia) si el master está off.
        var before = active ? null : new HashSet<string>(rects.Keys);
        int ny = drawInner(ix, iw, eff);
        if (!active && before is not null)
            foreach (var k in rects.Keys.Where(k => !before.Contains(k)).ToList())
                rects.Remove(k);        // inerte: el dependiente no dispara mutación con el master off
        return ny;
    }

    /// <summary>
    /// Centro X del knob del TogglePill dado el rect del track y el estado. Helper PURO (geometría,
    /// sin dibujo) para test e implementación: knob a la izquierda si OFF, a la derecha si ON.
    /// </summary>
    internal static int PillKnobCenterX(Rectangle track, bool on)
    {
        int rad = PillKnobDiameter / 2;
        int left = track.X + PillKnobInset + rad;
        int right = track.Right - PillKnobInset - rad;
        return on ? right : left;
    }

    /// <summary>
    /// Cápsula con knob deslizante (estilo iOS), dibujada a mano (GDI+), anclada por su borde DERECHO a
    /// <paramref name="rightX"/> con margen interno de seguridad <c>Spacing.Sm</c>. Track <c>Theme.Accent</c>
    /// cuando ON y <c>Theme.Separator</c> cuando OFF; knob circular claro a izquierda (OFF) / derecha (ON).
    /// Sustituye los glifos Unicode ☑/☐. Devuelve el rect del track (idéntico en medir y pintar).
    /// </summary>
    internal static Rectangle TogglePill(Graphics g, bool draw, bool on, int rightX, int y, int rowH, Theme theme)
    {
        // Anclado a la derecha con margen interno ≥ Spacing.Sm; centrado verticalmente en la fila.
        int tx = rightX - Spacing.Sm - PillTrackW;
        int ty = y + (rowH - PillTrackH) / 2;
        var track = new Rectangle(tx, ty, PillTrackW, PillTrackH);
        if (draw)
        {
            using (var bg = new SolidBrush(on ? theme.Accent : theme.Separator))
                Shapes.FillRounded(g, bg, track, PillTrackH / 2);
            int d = PillKnobDiameter;
            int cx = PillKnobCenterX(track, on);
            var knob = new Rectangle(cx - d / 2, track.Y + PillKnobInset, d, d);
            var sm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var kb = new SolidBrush(on ? ColorMath.Contrast(theme.Accent) : theme.TextPrimary))
                g.FillEllipse(kb, knob);
            g.SmoothingMode = sm;
        }
        return track;
    }

    // -------- StatusBadge: cápsula semántica de estado (Activas/Instalar) a la derecha de la fila --------
    // Padding interno horizontal del badge (reusa la geometría de chip de DrawSegments → un solo lenguaje).
    private const int BadgePadX = SegPadX;
    // Alto del badge sobre la rejilla 8pt (coincide con el alto de un chip de segmento).
    private const int BadgeHeight = SegmentHeight;
    private const int BadgeRadius = 4;

    /// <summary>
    /// Texto que el <see cref="StatusBadge"/> muestra realmente dentro del rango útil <c>[x, rightX]</c>:
    /// el texto original o, si no cabe, recortado por la cola con <b>elipsis medida</b>
    /// (<see cref="TextWrap.Ellipsize"/>). PURO y determinista (misma medición en draw=false/true → mismo
    /// resultado, clave para medir==pintar). El ancho útil de texto descuenta el padding del badge a ambos
    /// lados y el margen de seguridad derecho <c>Spacing.Sm</c>; nunca invade a la izquierda de <paramref name="x"/>.
    /// </summary>
    internal static string StatusBadgeShownText(Graphics g, string text, int x, int rightX, Font f)
    {
        text ??= string.Empty;
        // Espacio disponible para el badge completo: desde contentLeft (x) hasta rightX con margen Sm.
        int maxBadgeW = (rightX - Spacing.Sm) - x;
        int maxTextW = maxBadgeW - BadgePadX * 2;
        if (maxTextW <= 0) return TextWrap.Ellipsis;
        return TextWrap.Ellipsize(text, maxTextW, t => g.MeasureString(t, f).Width);
    }

    /// <summary>
    /// Badge semántico de estado (mini RoundedRect + texto de 1 línea centrado), anclado por su borde
    /// DERECHO a <paramref name="rightX"/> con margen interno de seguridad <c>Spacing.Sm</c>. Relleno con
    /// el color semántico dado (<c>Theme.Ok</c> "Activas" / <c>Theme.Warn</c> "Instalar"); el texto usa
    /// <see cref="ColorMath.Contrast"/> del relleno y se recorta por la cola (<see cref="StatusBadgeShownText"/>)
    /// para nunca rebasar su ancho. El badge nunca empieza a la izquierda de <paramref name="x"/>
    /// (contentLeft). Centrado verticalmente en la fila de alto <paramref name="rowH"/>. Devuelve el rect del
    /// badge (idéntico en medir y pintar: la decisión de recorte es la misma en ambas pasadas).
    /// </summary>
    internal static Rectangle StatusBadge(Graphics g, bool draw, string text, Color color,
        int x, int rightX, int y, int rowH, Theme theme, Font f)
    {
        string shown = StatusBadgeShownText(g, text, x, rightX, f);
        int textW = (int)Math.Ceiling(g.MeasureString(shown, f).Width);
        int badgeW = textW + BadgePadX * 2;
        int rightEdge = rightX - Spacing.Sm;             // margen derecho de seguridad
        int bx = rightEdge - badgeW;
        if (bx < x) { bx = x; badgeW = Math.Max(0, rightEdge - bx); } // clamp izquierdo a contentLeft
        int by = y + (rowH - BadgeHeight) / 2;            // centrado vertical en la fila
        var badge = new Rectangle(bx, by, badgeW, BadgeHeight);
        if (draw && badgeW > 0)
        {
            using (var bg = new SolidBrush(color))
                Shapes.FillRounded(g, bg, badge, BadgeRadius);
            using var tb = new SolidBrush(ColorMath.Contrast(color));
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.None,
                FormatFlags = StringFormatFlags.NoWrap
            };
            g.DrawString(shown, f, tb, badge, sf);
        }
        return badge;
    }

    /// <summary>
    /// Fila MAESTRA de una integración (p.ej. "Activar sesiones en vivo" = instalar/quitar hooks): título
    /// (<c>labelFont</c>/<c>TextPrimary</c>) + subtítulo opcional debajo (<c>smallFont</c>/<c>TextMuted</c>) a la
    /// izquierda + <see cref="StatusBadge"/> semántico a la DERECHA (verde "Activas" / ámbar "Instalar"). A
    /// diferencia de <see cref="ToggleRow"/>, NO lleva pill: la fila completa es clicable y su clave es
    /// normalmente "special:*" (la enruta el host: instalar/quitar hooks con confirmación). El hit-test es el
    /// rect COMPLETO de la fila. Mide==pinta: la geometría del badge se decide igual en ambas pasadas, así que
    /// el rect y el <c>y</c> de salida son idénticos. Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int MasterRowWithBadge(Graphics g, bool draw, string key, string label, string? subtitle,
        string badgeText, Color badgeColor, int x, int y, int w, Theme theme, Font labelFont, Font smallFont,
        Dictionary<string, Rectangle> rects)
    {
        int titleH = (int)Math.Ceiling(g.MeasureString(label, labelFont).Height);
        bool hasSub = !string.IsNullOrEmpty(subtitle);
        int subH = hasSub ? (int)Math.Ceiling(g.MeasureString(subtitle, smallFont).Height) : 0;
        int contentH = Math.Max(BadgeHeight, titleH + subH);

        var r = new Rectangle(x, y, w, contentH);
        rects[key] = r;
        // El badge se ancla a la derecha; su geometría se decide igual en ambas pasadas (medir==pintar).
        var badge = StatusBadge(g, draw, badgeText, badgeColor, x, x + w, y, contentH, theme, smallFont);
        // ANTI-TRUNCAMIENTO: la columna de texto izquierda no debe invadir el badge. Mide el ancho útil
        // hasta el borde izquierdo del badge con gutter Spacing.Md y elide título/subtítulo (elipsis medida,
        // misma decisión en draw=false/true → medir==pintar). Si no hay badge, usa todo el ancho.
        int textColW = (badge.Width > 0 ? badge.X : x + w) - Spacing.Md - x;
        if (textColW <= 0) textColW = Math.Max(0, x + w - x); // defensa: nunca negativo
        if (draw)
        {
            string shownLabel = TextWrap.Ellipsize(label, textColW, t => g.MeasureString(t, labelFont).Width);
            using (var b = new SolidBrush(theme.TextPrimary))
                g.DrawString(shownLabel, labelFont, b, x, y);
            if (hasSub)
            {
                string shownSub = TextWrap.Ellipsize(subtitle!, textColW, t => g.MeasureString(t, smallFont).Width);
                using var sb = new SolidBrush(theme.TextMuted);
                g.DrawString(shownSub, smallFont, sb, x, y + titleH);
            }
        }
        return y + contentH + Spacing.Sm;
    }

    /// <summary>
    /// Sección "SESIONES EN VIVO": cabecera + fila MAESTRA = instalar/quitar hooks (<c>special:hooktoggle</c>)
    /// con <see cref="StatusBadge"/> a la derecha (verde "Activas" si <paramref name="hooksInstalled"/>, ámbar
    /// "Instalar" si no) y subtítulo aclaratorio; debajo, los DEPENDIENTES (Mostrar mascota, Tamaño, Silenciar)
    /// vía <see cref="DrawDependent"/>: sangrados <c>Spacing.Lg</c>, y cuando los hooks NO están instalados se
    /// pintan atenuados (~0.5) e INERTES (sus rects no se registran → un clic no muta nada). La fila maestra
    /// SIEMPRE responde (es la que instala los hooks). Esto elimina el duplicado/huérfano: "Mostrar mascota"
    /// deja de ser una fila suelta por encima del master y pasa a depender visiblemente de él. La geometría no
    /// depende de <paramref name="hooksInstalled"/> (solo color + inercia) → medir==pintar y el avance es
    /// idéntico esté instalado o no. <paramref name="hooksInstalled"/> se inyecta para poder testear ambas
    /// ramas de forma determinista (el <see cref="Draw"/> pasa <c>HookInstaller.IsInstalled()</c>).
    /// </summary>
    internal static int LiveSessionsSection(Graphics g, bool draw, bool hooksInstalled, int x, int y, int w,
        AppConfig cfg, Strings s, Theme theme, Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects)
    {
        y = SectionHeader(g, draw, s.MenuLiveSessions, x, y, w, theme, smallFont);
        // Fila maestra: instala/quita hooks (delicado → diálogo en el host). Título ACCIONABLE (distinto de la
        // cabecera de sección, sin duplicar texto) + StatusBadge semántico a la derecha (Activas/Instalar).
        y = MasterRowWithBadge(g, draw, "special:hooktoggle",
            hooksInstalled ? s.MenuUninstallHooks : s.MenuInstallHooks, s.LiveSessionsSubtitle,
            hooksInstalled ? s.BadgeActive : s.BadgeInstall,
            hooksInstalled ? theme.Ok : theme.Warn,
            x, y, w, theme, labelFont, smallFont, rects);
        // Dependientes: atenuados + inertes mientras no haya hooks (la mascota Idle solo cobra vida con hooks).
        y = DrawDependent(hooksInstalled, x, y, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:ShowMascot", s.MenuShowMascot, null, cfg.ShowMascot,
                ix, y, iw, th, labelFont, smallFont, rects));
        int sizeY = y;
        y = DrawDependent(hooksInstalled, x, sizeY, w, theme, rects,
            (ix, iw, th) => SegmentedRow(g, draw, "mascotsize", s.MascotSizeLabel,
                new[] { ("compact", s.MascotSizeCompact), ("large", s.MascotSizeLarge) }, cfg.MascotSize,
                ix, sizeY, iw, th, smallFont, rects));
        int supY = y;
        y = DrawDependent(hooksInstalled, x, supY, w, theme, rects,
            (ix, iw, th) => ToggleRow(g, draw, "toggle:Suppress", s.MenuSuppressWhenFocused, null,
                cfg.SuppressWhenFocused, ix, supY, iw, th, labelFont, smallFont, rects));
        return y;
    }

    /// <summary>
    /// Fila de toggle estilo Apple: título (<c>labelFont</c>/<c>TextPrimary</c>) a la izquierda + subtítulo
    /// opcional debajo (<c>smallFont</c>/<c>TextMuted</c>, 1 línea corta) + <see cref="TogglePill"/> a la
    /// derecha (sin glifos Unicode). El hit-test es el rect COMPLETO de la fila (clic en cualquier punto).
    /// Alto = 1 línea, o título+subtítulo si hay subtítulo. Mide==pinta: el avance es idéntico en ambas pasadas.
    /// </summary>
    internal static int ToggleRow(Graphics g, bool draw, string key, string label, string? subtitle, bool on,
        int x, int y, int w, Theme theme, Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects)
    {
        int titleH = (int)Math.Ceiling(g.MeasureString(label, labelFont).Height);
        bool hasSub = !string.IsNullOrEmpty(subtitle);
        int subH = hasSub ? (int)Math.Ceiling(g.MeasureString(subtitle, smallFont).Height) : 0;
        int contentH = Math.Max(PillTrackH, titleH + subH);

        var r = new Rectangle(x, y, w, contentH);
        rects[key] = r;
        if (draw)
        {
            using (var b = new SolidBrush(theme.TextPrimary))
                g.DrawString(label, labelFont, b, x, y);
            if (hasSub)
                using (var sb = new SolidBrush(theme.TextMuted))
                    g.DrawString(subtitle!, smallFont, sb, x, y + titleH);
            TogglePill(g, draw: true, on, x + w, y, contentH, theme);
        }
        return y + contentH + Spacing.Sm;
    }

    /// <summary>Fila de acción simple (etiqueta clicable, sin estado on/off). Registra rects[key].</summary>
    private static int ActionRow(Graphics g, bool draw, string key, string label,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        var r = new Rectangle(x, y, w, RowContentHeight);
        rects[key] = r;
        if (draw)
        {
            using var b = new SolidBrush(theme.TextPrimary);
            g.DrawString("› " + label, f, b, x, y);
        }
        return y + RowAdvance;
    }

    /// <summary>
    /// Geometría medida de un <see cref="InfoRow"/>: etiqueta izquierda (en <c>x</c>) y valor derecho
    /// (anclado a <c>x+w-Spacing.Sm</c>, YA elidido si no cabía). PURO (sin dibujo) para test e
    /// implementación: el valor nunca invade la etiqueta (gutter ≥ <c>Spacing.Md</c>) ni rebasa el margen
    /// derecho de seguridad (<c>Spacing.Sm</c>). Devuelve (labelX, labelW, valueX, valueW).
    /// </summary>
    internal static (int lx, int lw, int rx, int rw) InfoRowLayout(Graphics g, string label, string value,
        int x, int w, Font f)
    {
        int labelW = (int)Math.Ceiling(g.MeasureString(label, f).Width);
        int rightEdge = x + w - Spacing.Sm;            // margen derecho de seguridad
        int valueLeftBound = x + labelW + Spacing.Md;  // gutter mínimo etiqueta↔valor
        int maxValueW = Math.Max(0, rightEdge - valueLeftBound);
        string shown = ShownInfoValue(g, value, maxValueW, f);
        int valueW = (int)Math.Ceiling(g.MeasureString(shown, f).Width);
        if (valueW > maxValueW) valueW = maxValueW;    // defensa: nunca exceder
        int valueX = rightEdge - valueW;               // anclado a la derecha con margen
        if (valueX < valueLeftBound) valueX = valueLeftBound;
        return (x, labelW, valueX, valueW);
    }

    /// <summary>Valor que un <see cref="InfoRow"/> muestra realmente: el valor o, si no cabe en
    /// <paramref name="maxValueW"/>, recortado por la cola con elipsis MEDIDA. Determinista (misma medición
    /// en draw=false/true → mismo resultado, clave para medir==pintar).</summary>
    private static string ShownInfoValue(Graphics g, string value, int maxValueW, Font f)
    {
        value ??= string.Empty;
        if (maxValueW <= 0) return string.Empty;
        if ((int)Math.Ceiling(g.MeasureString(value, f).Width) <= maxValueW) return value;
        return TextWrap.Ellipsize(value, maxValueW, t => g.MeasureString(t, f).Width);
    }

    /// <summary>
    /// Fila INFORMATIVA (no clicable): etiqueta a la izquierda (<c>TextMuted</c>) + valor a la derecha
    /// (<c>TextPrimary</c>), p.ej. "Versión" / "v0.3.5". NO registra rect (no es accionable). El valor se
    /// elide con elipsis medida si no cabe (nunca solapa la etiqueta ni rebasa el margen derecho). Mide==pinta:
    /// la decisión de elidir es la misma en ambas pasadas → mismo <c>y</c> de salida. Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int InfoRow(Graphics g, bool draw, string label, string value,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        if (draw)
        {
            var (_, _, rx, _) = InfoRowLayout(g, label, value, x, w, f);
            using (var lb = new SolidBrush(theme.TextMuted))
                g.DrawString(label, f, lb, x, y);
            int rightEdge = x + w - Spacing.Sm;
            int labelW = (int)Math.Ceiling(g.MeasureString(label, f).Width);
            int maxValueW = Math.Max(0, rightEdge - (x + labelW + Spacing.Md));
            string shown = ShownInfoValue(g, value, maxValueW, f);
            using var vb = new SolidBrush(theme.TextPrimary);
            g.DrawString(shown, f, vb, rx, y);
        }
        return y + RowAdvance;
    }

    /// <summary>
    /// Botón destacado de ancho completo (relleno tenue + borde + texto del color de acento) para acciones
    /// que deben "verse como botón" y no como una fila más. Registra rects[key]. Avanza h+8.
    /// </summary>
    private static int ButtonRow(Graphics g, bool draw, string key, string label, Color accent,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        const int h = 28;
        var r = new Rectangle(x, y, w, h);
        rects[key] = r;
        if (draw)
        {
            using var path = Shapes.RoundedRectPath(new Rectangle(x, y, w - 1, h - 1), 7);
            using (var fill = new SolidBrush(theme.BgElevated))
                g.FillPath(fill, path);
            using (var pen = new Pen(accent, 1.5f))
                g.DrawPath(pen, path);
            using var tb = new SolidBrush(accent);
            using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(label, f, tb, r, sf);
        }
        return y + h + Spacing.Sm;
    }

    // Sufijo del valor de un CycleRow (chevron). El valor a su izquierda es lo que se elide.
    private const string CycleChevron = "  ›";

    /// <summary>
    /// Geometría medida de un <see cref="CycleRow"/>: posición/ancho de la etiqueta izquierda y del
    /// valor derecho (YA elidido si no cabía). PURO (sin dibujo) para test e implementación: garantiza
    /// que etiqueta y valor no se solapan (gutter ≥ <c>Spacing.Md</c>) y que el valor deja margen
    /// derecho ≥ <c>Spacing.Sm</c>. Devuelve (labelX, labelW, valueX, valueW).
    /// </summary>
    internal static (int lx, int lw, int rx, int rw) CycleRowLayout(Graphics g, string label, string current,
        int x, int w, Font f)
    {
        int labelW = (int)Math.Ceiling(g.MeasureString(label, f).Width);
        int rightEdge = x + w - Spacing.Sm;            // margen derecho de seguridad
        // El valor (con chevron) puede ocupar como mucho desde labelRight+gutter hasta rightEdge.
        int valueLeftBound = x + labelW + Spacing.Md;
        int maxValueW = rightEdge - valueLeftBound;
        string shown = ShownCycleValue(g, label, current, x, w, f);
        int valueW = (int)Math.Ceiling(g.MeasureString(shown, f).Width);
        if (valueW > maxValueW) valueW = Math.Max(0, maxValueW); // defensa: nunca exceder
        int valueX = rightEdge - valueW;               // anclado a la derecha con margen
        if (valueX < valueLeftBound) valueX = valueLeftBound;
        return (x, labelW, valueX, valueW);
    }

    /// <summary>
    /// Texto del valor que un <see cref="CycleRow"/> muestra realmente: el valor actual + chevron,
    /// elidido con elipsis MEDIDA si la suma etiqueta+gutter+valor+margen excede <paramref name="w"/>.
    /// PURO y determinista (misma medición en draw=false/true → mismo resultado, clave para medir==pintar).
    /// </summary>
    internal static string CycleRowShownValue(Graphics g, string label, string current, int x, int w, Font f)
        => ShownCycleValue(g, label, current, x, w, f);

    private static string ShownCycleValue(Graphics g, string label, string current, int x, int w, Font f)
    {
        int labelW = (int)Math.Ceiling(g.MeasureString(label, f).Width);
        int rightEdge = x + w - Spacing.Sm;
        int valueLeftBound = x + labelW + Spacing.Md;
        int maxValueW = rightEdge - valueLeftBound;
        string full = current + CycleChevron;
        if ((int)Math.Ceiling(g.MeasureString(full, f).Width) <= maxValueW) return full;
        // No cabe → elidir SOLO el valor, conservando el chevron a la derecha del valor recortado.
        double chevronW = g.MeasureString(CycleChevron, f).Width;
        string clipped = TextWrap.Ellipsize(current, maxValueW - chevronW, x2 => g.MeasureString(x2, f).Width);
        return clipped.Length == 0 ? TextWrap.Ellipsis : clipped + CycleChevron;
    }

    /// <summary>
    /// Fila que cicla: "Etiqueta" a la izquierda + "&lt;valor actual&gt; ›" a la derecha (elidido si no
    /// cabe; nunca solapa la etiqueta ni rebasa el margen derecho). Un clic en cualquier punto de la fila
    /// cicla al siguiente valor (la mutación la pone <see cref="ActionFor"/>). Mide==pinta: la decisión de
    /// elidir es la misma en ambas pasadas, así que el rect y el <c>y</c> de salida son idénticos.
    /// </summary>
    internal static int CycleRow(Graphics g, bool draw, string key, string label, string current,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        var r = new Rectangle(x, y, w, RowContentHeight);
        rects[key] = r;
        if (draw)
        {
            var (_, _, rx, _) = CycleRowLayout(g, label, current, x, w, f);
            using var fgb = new SolidBrush(theme.TextPrimary);
            using var dimb = new SolidBrush(theme.TextSecondary);
            g.DrawString(label, f, dimb, x, y);
            string right = ShownCycleValue(g, label, current, x, w, f);
            g.DrawString(right, f, fgb, rx, y);
        }
        return y + RowAdvance;
    }

    // Geometría de los chips de segmento — MISMA que DashboardDataView.DrawSegments (un solo estilo).
    private const int SegGap = 3, SegPadX = 7;

    /// <summary>Ancho total (incl. gaps) de una fila de segmentos con las etiquetas dadas.</summary>
    private static int SegmentsTotalWidth(Graphics g, Font f, IReadOnlyList<string> labels)
    {
        if (labels.Count == 0) return 0;
        int total = 0;
        for (int i = 0; i < labels.Count; i++)
            total += (int)g.MeasureString(labels[i], f).Width + SegPadX * 2;
        return total + SegGap * (labels.Count - 1);
    }

    /// <summary>Avance de una fila de segmentos de UN renglón (para test del wrap a 2 filas).</summary>
    internal static int SegmentRowAdvanceForTest => SegmentRowAdvance;

    /// <summary>
    /// Fila con etiqueta opcional + segmentos a la derecha (mismo look &amp; hit-test que
    /// <see cref="DashboardDataView.DrawSegments"/>: activo en Accent + texto Contrast). ANTI-TRUNCAMIENTO:
    /// mide el ancho total en draw=false; si los segmentos NO caben en el espacio útil
    /// (<c>[labelRight+gutter, x+w-Spacing.Sm]</c>) <b>envuelve a 2 filas</b> alineadas a
    /// <c>contentLeft</c>; si caben, se anclan a la derecha dejando margen ≥ <c>Spacing.Sm</c>. Ningún chip
    /// se pinta con <c>x &lt; contentLeft</c>. La decisión (1 fila vs 2) es idéntica en medir/pintar →
    /// mismo <c>y</c> de salida. Cada segmento registra rects[$"{key}:{val}"].
    /// </summary>
    internal static int SegmentedRow(Graphics g, bool draw, string key, string label,
        (string val, string txt)[] segs, string active, int x, int y, int w, Theme theme, Font f,
        Dictionary<string, Rectangle> rects)
    {
        if (draw && !string.IsNullOrEmpty(label))
        {
            using var b = new SolidBrush(theme.TextPrimary);
            g.DrawString(label, f, b, x, y);
        }

        var keyed = segs.Select(seg => (seg.txt, $"{key}:{seg.val}")).ToArray();
        string activeKey = $"{key}:{active}";
        int labelRight = string.IsNullOrEmpty(label)
            ? x
            : x + (int)Math.Ceiling(g.MeasureString(label, f).Width) + Spacing.Md;
        int rightEdge = x + w - Spacing.Sm;            // margen derecho de seguridad

        int total = SegmentsTotalWidth(g, f, segs.Select(s => s.txt).ToList());
        int avail = rightEdge - Math.Max(labelRight, x);

        if (total <= avail)
        {
            // Cabe en un renglón: anclar a la derecha (deja margen ≥ Sm; primer chip ≥ labelRight ≥ x).
            DashboardDataView.DrawSegments(g, draw, f, theme, keyed, activeKey,
                rightEdge, y, rightAlign: true, rects);
            return y + SegmentRowAdvance;
        }

        // No cabe → envolver a 2 filas, alineadas a contentLeft (ningún chip a la izquierda de x).
        // Reparto: tantos segmentos como quepan en el ancho útil de una fila (desde x), resto a la 2ª.
        int rowWidth = x + w - x; // ancho útil del renglón empezando en contentLeft
        int split = SplitIndexForWrap(g, f, segs.Select(s => s.txt).ToList(), rowWidth);
        var first = keyed.Take(split).ToArray();
        var second = keyed.Skip(split).ToArray();

        DashboardDataView.DrawSegments(g, draw, f, theme, first, activeKey,
            x, y, rightAlign: false, rects);
        DashboardDataView.DrawSegments(g, draw, f, theme, second, activeKey,
            x, y + SegmentHeight + Spacing.Sm, rightAlign: false, rects);
        return y + (SegmentHeight + Spacing.Sm) * 2;
    }

    /// <summary>
    /// Índice de corte para envolver a 2 filas: cuántos segmentos (≥1) caben en <paramref name="rowWidth"/>
    /// empezando en contentLeft. Determinista (misma medición en ambas pasadas).
    /// </summary>
    private static int SplitIndexForWrap(Graphics g, Font f, IReadOnlyList<string> labels, int rowWidth)
    {
        int count = 0, acc = 0;
        for (int i = 0; i < labels.Count; i++)
        {
            int wseg = (int)g.MeasureString(labels[i], f).Width + SegPadX * 2;
            int next = acc + (count > 0 ? SegGap : 0) + wseg;
            if (count > 0 && next > rowWidth) break;
            acc = next; count++;
        }
        return Math.Max(1, count); // al menos 1 por fila para no perder segmentos
    }

    /// <summary>
    /// Fila de segmentos con <b>MÚLTIPLES activos</b> (p.ej. hitos 25/50/75/95): a diferencia de
    /// <see cref="DashboardDataView.DrawSegments"/> (un solo activo), aquí <paramref name="active"/> es un
    /// conjunto y CADA valor presente se pinta en estado "on" con el MISMO lenguaje visual (Accent +
    /// <see cref="ColorMath.Contrast"/>), un único estilo de pill. Esto sustituye al re-pintado manual por
    /// encima de <c>DrawSegments</c>. Geometría de chip idéntica a <c>DrawSegments</c>
    /// (<c>SegGap</c>/<c>SegPadX</c>/<c>SegmentHeight</c>). Etiqueta opcional a la izquierda; segmentos
    /// anclados a la derecha dejando margen ≥ <c>Spacing.Sm</c> y nunca con <c>x &lt; contentLeft</c>. Cada
    /// segmento registra <c>rects[$"{key}:{val}"]</c>. Mide==pinta: el avance es idéntico en ambas pasadas
    /// (la geometría no depende de <paramref name="draw"/>). Devuelve el nuevo <c>y</c>.
    /// </summary>
    internal static int MultiSegmentRow(Graphics g, bool draw, string key, string label,
        (string val, string txt)[] segs, IEnumerable<int> active, int x, int y, int w, Theme theme, Font f,
        Dictionary<string, Rectangle> rects)
    {
        if (draw && !string.IsNullOrEmpty(label))
        {
            using var b = new SolidBrush(theme.TextPrimary);
            g.DrawString(label, f, b, x, y);
        }

        var activeSet = new HashSet<string>((active ?? Enumerable.Empty<int>())
            .Select(v => v.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        int labelRight = string.IsNullOrEmpty(label)
            ? x
            : x + (int)Math.Ceiling(g.MeasureString(label, f).Width) + Spacing.Md;
        int rightEdge = x + w - Spacing.Sm;            // margen derecho de seguridad

        // Anclado a la derecha (deja margen ≥ Sm; primer chip ≥ max(labelRight, x) ≥ contentLeft).
        int total = SegmentsTotalWidth(g, f, segs.Select(s => s.txt).ToList());
        int sx = Math.Max(rightEdge - total, Math.Max(labelRight, x));
        foreach (var (val, txt) in segs)
        {
            int chipW = (int)g.MeasureString(txt, f).Width + SegPadX * 2;
            var rect = new Rectangle(sx, y, chipW, SegmentHeight);
            bool on = activeSet.Contains(val);
            if (draw)
            {
                using var bg = new SolidBrush(on ? theme.Accent : theme.BgElevated);
                Shapes.FillRounded(g, bg, rect, 4);
                using var tb = new SolidBrush(on ? ColorMath.Contrast(theme.Accent) : theme.TextPrimary);
                using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(txt, f, tb, rect, sf);
            }
            rects[$"{key}:{val}"] = rect;
            sx += chipW + SegGap;
        }
        return y + SegmentRowAdvance;
    }
}
