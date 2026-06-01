using ClaudeBarWin.Config;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

/// <summary>
/// Panel de ajustes dentro del dashboard (modo "settings"). Sin estado: dibuja las filas de
/// configuración agrupadas y registra cada control clicable en <c>rects</c> con una clave de acción
/// (p.ej. "toggle:ShowSpend", "theme:dark", "freq:60", "mascotsize:large"). <see cref="ActionFor"/>
/// traduce esa clave a la mutación de <see cref="AppConfig"/> que <c>DashboardForm</c> emite por su
/// evento <c>SettingsChanged</c>. Cada helper avanza y devuelve <c>y</c> idéntico en draw=false/true.
/// </summary>
public static class DashboardSettingsView
{
    /// <summary>Dibuja el panel y registra rects clicables con clave de acción. Devuelve nuevo y.</summary>
    public static int Draw(Graphics g, bool draw, int x, int y, int w, AppConfig cfg, Strings s, Theme theme,
                           Font labelFont, Font smallFont, Dictionary<string, Rectangle> rects)
    {
        rects.Clear();

        y = GroupHeader(g, draw, s.MenuSections, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:ShowSpend", s.ShowSpend, cfg.ShowSpendEstimate, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowHealth", s.ShowServiceStatus, cfg.ShowHealth, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:ShowChart", s.UsageChart, cfg.ShowChart, x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, s.MenuLiveSessions, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:ShowMascot", s.MenuShowMascot, cfg.ShowMascot, x, y, w, theme, smallFont, rects);
        y = SegmentedRow(g, draw, "mascotsize", "Mascot size",
            new[] { ("compact", "compact"), ("large", "large") }, cfg.MascotSize, x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, s.Notifications, x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:Notifications", s.Enabled, cfg.NotificationsEnabled, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:PaceAlerts", s.PaceAlerts, cfg.PaceAlerts, x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, s.UpdateFrequency, x, y, theme, labelFont);
        y = SegmentedRow(g, draw, "freq", "",
            new[] { ("30", s.Sec30), ("60", s.Min1), ("300", s.Min5), ("900", s.Min15) },
            cfg.RefreshSeconds.ToString(), x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, s.MenuAppearance, x, y, theme, labelFont);
        y = SegmentedRow(g, draw, "theme", s.Theme,
            new[] { ("system", s.ThemeSystem), ("dark", s.ThemeDark), ("light", s.ThemeLight), ("cli", "CLI") },
            cfg.Theme, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:Sticky", s.Sticky, cfg.DashboardSticky, x, y, w, theme, smallFont, rects);
        y = ToggleRow(g, draw, "toggle:OnTop", s.AlwaysOnTop, cfg.DashboardAlwaysOnTop, x, y, w, theme, smallFont, rects);

        y = GroupHeader(g, draw, "Sistema", x, y, theme, labelFont);
        y = ToggleRow(g, draw, "toggle:Startup", s.StartWithWindows, StartupManager.IsEnabled(), x, y, w, theme, smallFont, rects);

        return y;
    }

    /// <summary>Traduce la clave de acción de un rect clicado a la mutación de config.</summary>
    public static Action<AppConfig>? ActionFor(string key) => key switch
    {
        "toggle:ShowSpend" => c => c.ShowSpendEstimate = !c.ShowSpendEstimate,
        "toggle:ShowHealth" => c => c.ShowHealth = !c.ShowHealth,
        "toggle:ShowChart" => c => c.ShowChart = !c.ShowChart,
        "toggle:ShowMascot" => c => c.ShowMascot = !c.ShowMascot,
        "toggle:Notifications" => c => c.NotificationsEnabled = !c.NotificationsEnabled,
        "toggle:PaceAlerts" => c => c.PaceAlerts = !c.PaceAlerts,
        "toggle:Sticky" => c => c.DashboardSticky = !c.DashboardSticky,
        "toggle:OnTop" => c => c.DashboardAlwaysOnTop = !c.DashboardAlwaysOnTop,
        "mascotsize:compact" => c => c.MascotSize = "compact",
        "mascotsize:large" => c => c.MascotSize = "large",
        "theme:system" => c => c.Theme = "system",
        "theme:dark" => c => c.Theme = "dark",
        "theme:light" => c => c.Theme = "light",
        "theme:cli" => c => c.Theme = "cli",
        "freq:30" => c => c.RefreshSeconds = 30,
        "freq:60" => c => c.RefreshSeconds = 60,
        "freq:300" => c => c.RefreshSeconds = 300,
        "freq:900" => c => c.RefreshSeconds = 900,
        "toggle:Startup" => _ => StartupManager.Toggle(),
        _ => null,
    };

    // ---------------- helpers de dibujo (simetría medir/pintar; registran rects con clave) ----------------

    /// <summary>Encabezado de grupo (texto atenuado). Avanza 20px.</summary>
    private static int GroupHeader(Graphics g, bool draw, string title, int x, int y, Theme theme, Font f)
    {
        if (draw)
        {
            using var b = new SolidBrush(theme.Dim);
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
            using var b = new SolidBrush(theme.Foreground);
            g.DrawString((on ? "☑ " : "☐ ") + label, f, b, x, y);
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
            using var b = new SolidBrush(theme.Foreground);
            g.DrawString(label, f, b, x, y);
        }
        // segmentos alineados a la derecha; clave compuesta "key:val", activo en theme.Ok.
        DashboardDataView.DrawSegments(g, draw, f, theme,
            segs.Select(seg => (seg.txt, $"{key}:{seg.val}")).ToArray(),
            $"{key}:{active}", x + w, y, rightAlign: true, rects);
        return y + 24;
    }
}
