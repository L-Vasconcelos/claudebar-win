using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using ClaudeBarWin.Services;

namespace ClaudeBarWin.UI;

public enum UsageStatus
{
    Ok = 0,
    Warn = 1,
    Critical = 2
}

/// <summary>
/// Forma del indicador de estado, redundante con el color para accesibilidad (daltónicos):
/// Ok→círculo, Warn→triángulo, Critical→rombo. Misma semántica en el tray (overlay) y en el
/// dashboard (glifo de 1 carácter junto al %).
/// </summary>
public enum TrayShape
{
    Circle = 0,
    Triangle = 1,
    Rhombus = 2
}

/// <summary>
/// Mapeos puros y testeables del estado a forma/glifo. El estado por <b>forma</b> acompaña siempre
/// al color para que la señal sea legible también por quien no distingue colores.
/// </summary>
public static class Tray
{
    /// <summary>Forma del indicador para cada estado de cuota (Ok→círculo, Warn→triángulo, Critical→rombo).</summary>
    public static TrayShape ShapeFor(UsageStatus status) => status switch
    {
        UsageStatus.Critical => TrayShape.Rhombus,
        UsageStatus.Warn => TrayShape.Triangle,
        _ => TrayShape.Circle
    };

    /// <summary>Glifo de 1 carácter para pintar la forma junto al % en el dashboard.</summary>
    public static string ShapeGlyph(TrayShape shape) => shape switch
    {
        TrayShape.Rhombus => "◆",
        TrayShape.Triangle => "▲",
        _ => "●"
    };
}

/// <summary>
/// Draws a small badge icon (percentage + status colour) for the system tray.
/// </summary>
public static class TrayIconRenderer
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);

    public static Icon Render(int percent, Color bg, bool pending = false)
    {
        percent = Math.Clamp(percent, 0, 999);
        // Sin tema a mano: el punto de atención usa el ámbar Warn de la paleta base oscura (T9d).
        return RenderBadge(percent >= 100 ? "99+" : percent.ToString(), bg, UsageStatus.Ok, stale: false,
            pending, Theme.Dark.Warn);
    }

    /// <summary>Colorea el badge de forma continua por riesgo (Ok→Warn→Critical) según el tema y los umbrales.</summary>
    public static Icon Render(int percent, Theme theme, double warn, double crit, bool pending = false)
    {
        percent = Math.Clamp(percent, 0, 999);
        return RenderBadge(percent >= 100 ? "99+" : percent.ToString(),
            ColorMath.RiskColor(percent, theme, warn, crit), UsageStatus.Ok, stale: false, pending, theme.Warn);
    }

    /// <summary>
    /// Badge con estado por forma (overlay) + estado stale. El texto adapta su contraste al fondo
    /// (<see cref="ColorMath.Contrast"/>), de modo que es legible en barra de tareas clara u oscura.
    /// <paramref name="taskbarLight"/> (<see cref="Services.ThemeResolver.TaskbarIsLight"/> en la app)
    /// indica contra qué barra se compone el velo stale; los badges frescos no dependen de él.
    /// </summary>
    public static Icon Render(int percent, Theme theme, double warn, double crit,
        UsageStatus status, bool stale = false, bool pending = false, bool taskbarLight = false)
    {
        percent = Math.Clamp(percent, 0, 999);
        var bg = ColorMath.RiskColor(percent, theme, warn, crit);
        return RenderBadge(percent >= 100 ? "99+" : percent.ToString(), bg, status, stale, pending, theme.Warn,
            taskbarLight);
    }

    /// <summary>Neutral badge for "no data / auth expired / offline".</summary>
    public static Icon RenderError(Color bg, bool pending = false)
        => RenderBadge("!", bg, UsageStatus.Ok, stale: false, pending, Theme.Dark.Warn);

    /// <summary>Opacidad del velo de atenuación del badge stale (sobre 255).</summary>
    private const int StaleAlpha = 150;

    /// <summary>
    /// Relleno EFECTIVO del badge stale: el velo translúcido (<see cref="StaleAlpha"/>) PRE-COMPUESTO
    /// a opaco contra el color asumido de la barra de tareas (<see cref="TaskbarColors"/>). Pintar con
    /// alpha y decidir el texto sobre el color fresco (regresión de 5ef80c1, T6) dejaba el dígito a
    /// ~2:1 en barra oscura: el compuesto real caía al otro lado del flip WCAG. A opaco, el color
    /// pintado ES el color en pantalla (sin recomposición del pipeline HICON), y el lado que elige
    /// <see cref="ColorMath.Contrast"/> garantiza ≥4.58:1 sobre cualquier relleno (en el punto de
    /// cruce L≈0.179 negro y blanco empatan a (L+0.05)/0.05 ≈ 4.58).
    /// </summary>
    public static Color StaleFill(Color bg, bool taskbarLight)
        => ColorMath.Lerp(taskbarLight ? TaskbarColors.Light : TaskbarColors.Dark, bg, StaleAlpha / 255.0);

    private static Icon RenderBadge(string text, Color bg, UsageStatus status, bool stale, bool pending,
        Color pendingDot, bool taskbarLight = false)
    {
        // Render at high resolution (48px) so Windows downscales to a crisp tray icon on any DPI.
        const int size = 48;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            // Estado "stale": dato envejecido → badge atenuado hacia el color de la barra para que se
            // note de un vistazo que no es fresco. Atenuado SIN alpha (pre-compuesto a opaco, T6b):
            // sobre la barra asumida se ve igual que el velo translúcido, pero el color pintado es el
            // color real en pantalla y la decisión de contraste del texto deja de mentir.
            Color fillColor = stale ? StaleFill(bg, taskbarLight) : bg;
            using (var brush = new SolidBrush(fillColor))
                Shapes.FillRounded(g, brush, new Rectangle(0, 0, size - 1, size - 1), 11);

            // Texto con contraste calculado sobre el relleno EFECTIVO (no el fresco): legible en barra
            // clara/oscura también cuando el badge está atenuado por stale.
            Color textColor = ColorMath.Contrast(fillColor);
            // Larger glyph that fills more of the badge → readable even when shrunk into the tray.
            // 3-char ("99+") gets a smaller size and NoWrap so it stays on a single line (no "99 / +").
            float fontPx = text.Length >= 3 ? 18f : 30f;
            using var font = new Font("Segoe UI", fontPx, FontStyle.Bold, GraphicsUnit.Pixel);
            using var sf = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap
            };
            using (var textBrush = new SolidBrush(textColor))
                g.DrawString(text, font, textBrush, new RectangleF(0, -2f, size, size), sf);

            // Estado por forma (a11y): overlay de forma en la esquina inferior derecha para Warn/Critical.
            // Ok→círculo = sin overlay (el badge ya es el indicador). T9d (§3 #15): con el punto de
            // atención la forma se SUPRIME — un solo elemento decorativo por esquina; dígito + forma
            // + punto a 16-32px se solapaban en ruido ilegible.
            if (!pending)
                DrawShapeOverlay(g, Tray.ShapeFor(status), bg, textColor, size);

            if (pending)
            {
                // Punto de atención (T9d): ámbar Warn de la paleta del tema (antes #F5A623 fuera de
                // paleta con aro #1A1A1A). Más pequeño (d=10, antes 18: tapaba el dígito) y pegado a
                // la esquina superior derecha, separado del relleno por un aro KNOCKOUT: se BORRA a
                // transparente (SourceCopy), así delimita sobre cualquier color de badge y de barra
                // de tareas sin introducir un color nuevo. La geometría deja libre la caja de tinta
                // del dígito (verificado por test píxel a píxel con "88").
                const int d = 10;
                var dot = new Rectangle(size - d - 1, 1, d, d);
                var gap = Rectangle.Inflate(dot, 2, 2);
                var cm = g.CompositingMode;
                g.CompositingMode = CompositingMode.SourceCopy;   // escribe transparencia real (borra)
                using (var clear = new SolidBrush(Color.Transparent))
                    g.FillEllipse(clear, gap);
                g.CompositingMode = cm;
                using var fill = new SolidBrush(pendingDot);
                g.FillEllipse(fill, dot);
            }
        }

        // Clonamos el icono a 32bpp ARGB completo. El round-trip anterior (Icon.Save→new Icon(ms))
        // recodificaba el .ico a la paleta VGA de 16 colores: el verde (22,163,74) caía a teal
        // (0,128,128) y el verde-oliva del badge a oliva puro (128,128,0). En la bandeja a 16px casi no
        // se nota, pero al MAGNIFICAR el icono para el GIF el aplanado de color se veía amateur. Con
        // Icon.FromHandle preservamos color y alfa reales; clonamos para poder liberar el HICON.
        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            return (Icon)tmp.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    /// <summary>Pinta la forma de estado en la esquina inferior derecha. Circle (Ok) no dibuja overlay.</summary>
    private static void DrawShapeOverlay(Graphics g, TrayShape shape, Color bg, Color fg, int size)
    {
        if (shape == TrayShape.Circle) return; // Ok: el propio badge es el indicador, sin overlay.

        // Forma en el color de texto (mismo contraste que el %), centrada en una esquina.
        int s = 14;
        int cx = size - s - 1;
        int cy = size - s - 1;
        using var brush = new SolidBrush(fg);
        if (shape == TrayShape.Triangle)
        {
            var tri = new[]
            {
                new Point(cx + s / 2, cy),
                new Point(cx + s, cy + s),
                new Point(cx, cy + s),
            };
            g.FillPolygon(brush, tri);
        }
        else // Rhombus
        {
            var rho = new[]
            {
                new Point(cx + s / 2, cy),
                new Point(cx + s, cy + s / 2),
                new Point(cx + s / 2, cy + s),
                new Point(cx, cy + s / 2),
            };
            g.FillPolygon(brush, rho);
        }
    }
}
