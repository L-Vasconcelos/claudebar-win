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

    /// <summary>Dibuja el panel y registra rects clicables con clave de acción. Devuelve nuevo y.</summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w, AppConfig cfg, Strings s, Theme theme,
                           Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects)
    {
        rects.Clear();

        // -------- Secciones --------
        y = GroupHeader(g, draw, s.MenuSections, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:ShowSpend", s.ShowSpend, cfg.ShowSpendEstimate, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowHealth", s.ShowServiceStatus, cfg.ShowHealth, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowChart", s.UsageChart, cfg.ShowChart, x, y, w, theme, smallFont, rects);

        // -------- Sesiones en vivo --------
        y = GroupHeader(g, draw, s.MenuLiveSessions, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:ShowMascot", s.MenuShowMascot, cfg.ShowMascot, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:Suppress", s.MenuSuppressWhenFocused, cfg.SuppressWhenFocused, x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "mascotsize", s.MascotSizeLabel,
            new[] { ("compact", s.MascotSizeCompact), ("large", s.MascotSizeLarge) }, cfg.MascotSize, x, y, w, theme, smallFont, rects);
        // Activador de la feature = BOTÓN destacado (no una fila más): instala/quita hooks en
        // ~/.claude/settings.json con confirmación en el host. Verde = activar, rojo = desactivar (delicado).
        bool hooksOn = HookInstaller.IsInstalled();
        y = ButtonRow(g, draw, "special:hooktoggle",
            hooksOn ? s.MenuUninstallHooks : s.MenuInstallHooks,
            hooksOn ? theme.Critical : theme.Ok, x, y, w, theme, smallFont, rects);

        // -------- Notificaciones --------
        y = GroupHeader(g, draw, s.Notifications, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:Notifications", s.Enabled, cfg.NotificationsEnabled, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:PaceAlerts", s.PaceAlerts, cfg.PaceAlerts, x, y, w, theme, smallFont, rects);
        // Hitos individuales 25/50/75/95 como toggles que editan el array NotifyMilestones.
        var milestones = cfg.NotifyMilestones ?? Array.Empty<int>();
        if (draw)
        {
            using var b = new SolidBrush(theme.TextSecondary);
            g.DrawString(s.NotifyWhenReaching, smallFont, b, x, y);
        }
        DashboardDataView.DrawSegments(g, draw, smallFont, theme,
            MilestoneOptions.Select(m => ($"{m}%", $"milestone:{m}")).ToArray(),
            "", x + w, y, rightAlign: true, rects);
        // Resaltar los activos: re-pintamos el rect activo con look "on" (DrawSegments no conoce multi-activo).
        if (draw)
            foreach (var m in MilestoneOptions)
                if (milestones.Contains(m) && rects.TryGetValue($"milestone:{m}", out var mr))
                {
                    using var bg = new SolidBrush(theme.Accent);
                    Shapes.FillRounded(g, bg, mr, 4);
                    using var tb = new SolidBrush(ColorMath.Contrast(theme.Accent));
                    using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                    g.DrawString($"{m}%", smallFont, tb, mr, sf);
                }
        y += 22;

        // -------- Frecuencia de actualización --------
        y = GroupHeader(g, draw, s.UpdateFrequency, x, y, theme, labelFont);
        y = SegmentedRow(g, draw, "freq", "",
            new[] { ("30", s.Sec30), ("60", s.Min1), ("300", s.Min5), ("900", s.Min15) },
            cfg.RefreshSeconds.ToString(), x, y, w, theme, smallFont, rects);

        // -------- Icono --------
        y = GroupHeader(g, draw, s.MenuIcon, x, y, theme, labelFont);
        y = SegmentedRow(g, draw, "icon", s.IconMode,
            new[] { ("percent", "%"), ("pace", "▲"), ("both", "%▲") },
            string.IsNullOrEmpty(cfg.IconDisplayMode) ? "percent" : cfg.IconDisplayMode, x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "threshold", s.ColorThreshold,
            ThresholdOptions.Select(t => ($"{t.warn:0}/{t.crit:0}", $"{t.warn:0}/{t.crit:0}")).ToArray(),
            $"{cfg.WarnThresholdPct:0}/{cfg.CriticalThresholdPct:0}", x, y, w, theme, smallFont, rects);

        // -------- Apariencia --------
        y = GroupHeader(g, draw, s.MenuAppearance, x, y, theme, labelFont);
        y = SegmentedRow(g, draw, "theme", s.Theme,
            new[] { ("system", s.ThemeSystem), ("dark", s.ThemeDark), ("light", s.ThemeLight), ("cli", "CLI") },
            cfg.Theme, x, y, w, theme, smallFont, rects);
        y = ActionRow(g, draw, "special:importtheme", s.ImportTheme, x, y, w, theme, smallFont, rects);
        // Posición: fila que cicla (5 opciones no caben en segmentos); muestra la posición actual.
        y = CycleRow(g, draw, "cycle:position", s.Position, PosLabel(cfg.DashboardPosition, s), x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "opacity", s.Opacity, OpacitySegs, FmtOpacity(cfg.DashboardOpacity), x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:Sticky", s.Sticky, cfg.DashboardSticky, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:OnTop", s.AlwaysOnTop, cfg.DashboardAlwaysOnTop, x, y, w, theme, smallFont, rects);

        // -------- Idioma --------
        y = GroupHeader(g, draw, s.Language, x, y, theme, labelFont);
        y = CycleRow(g, draw, "cycle:lang", s.Language,
            Localization.LanguageDisplayName(cfg.Language, s), x, y, w, theme, smallFont, rects);

        // -------- Sistema --------
        y = GroupHeader(g, draw, "Sistema", x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:Startup", s.StartWithWindows, StartupManager.IsEnabled(), x, y, w, theme, smallFont, rects);

        return y;
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

    /// <summary>Encabezado de grupo (texto atenuado). Avanza 20px.</summary>
    private static int GroupHeader(Graphics g, bool draw, string title, int x, int y, Theme theme, Font f)
    {
        if (draw)
        {
            using var b = new SolidBrush(theme.TextSecondary);
            g.DrawString(title, f, b, x, y);
        }
        return y + 20;
    }

    /// <summary>Fila de toggle (☑/☐ + etiqueta). Registra rects[key] con el ancho completo de la fila.</summary>
    private static int ToggleRow(Graphics g, bool draw, string key, string label, bool on,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        var r = new Rectangle(x, y, w, 18);
        rects[key] = r;
        if (draw)
        {
            using var b = new SolidBrush(theme.TextPrimary);
            g.DrawString((on ? "☑ " : "☐ ") + label, f, b, x, y);
        }
        return y + 20;
    }

    /// <summary>Fila de acción simple (etiqueta clicable, sin estado on/off). Registra rects[key].</summary>
    private static int ActionRow(Graphics g, bool draw, string key, string label,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        var r = new Rectangle(x, y, w, 18);
        rects[key] = r;
        if (draw)
        {
            using var b = new SolidBrush(theme.TextPrimary);
            g.DrawString("› " + label, f, b, x, y);
        }
        return y + 20;
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
        return y + h + 8;
    }

    /// <summary>
    /// Fila que cicla: "Etiqueta" a la izquierda + "&lt;valor actual&gt; ›" a la derecha. Un clic en
    /// cualquier punto de la fila cicla al siguiente valor (la mutación la pone <see cref="ActionFor"/>).
    /// </summary>
    private static int CycleRow(Graphics g, bool draw, string key, string label, string current,
        int x, int y, int w, Theme theme, Font f, Dictionary<string, Rectangle> rects)
    {
        var r = new Rectangle(x, y, w, 18);
        rects[key] = r;
        if (draw)
        {
            using var fgb = new SolidBrush(theme.TextPrimary);
            using var dimb = new SolidBrush(theme.TextSecondary);
            g.DrawString(label, f, dimb, x, y);
            string right = current + "  ›";
            var sz = g.MeasureString(right, f);
            g.DrawString(right, f, fgb, x + w - sz.Width, y);
        }
        return y + 20;
    }

    /// <summary>
    /// Fila con etiqueta opcional + segmentos en fila a la derecha (look &amp; hit-test reusando
    /// <see cref="DashboardDataView.DrawSegments"/>). Cada segmento registra rects[$"{key}:{val}"].
    /// </summary>
    private static int SegmentedRow(Graphics g, bool draw, string key, string label,
        (string val, string txt)[] segs, string active, int x, int y, int w, Theme theme, Font f,
        Dictionary<string, Rectangle> rects)
    {
        if (draw && !string.IsNullOrEmpty(label))
        {
            using var b = new SolidBrush(theme.TextPrimary);
            g.DrawString(label, f, b, x, y);
        }
        // segmentos alineados a la derecha; clave compuesta "key:val", activo en theme.Ok.
        DashboardDataView.DrawSegments(g, draw, f, theme,
            segs.Select(seg => (seg.txt, $"{key}:{seg.val}")).ToArray(),
            $"{key}:{active}", x + w, y, rightAlign: true, rects);
        return y + 24;
    }
}
