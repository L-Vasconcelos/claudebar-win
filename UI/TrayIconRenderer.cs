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
        return RenderBadge(percent >= 100 ? "99+" : percent.ToString(), bg, UsageStatus.Ok, stale: false, pending);
    }

    /// <summary>Colorea el badge de forma continua por riesgo (Ok→Warn→Critical) según el tema y los umbrales.</summary>
    public static Icon Render(int percent, Theme theme, double warn, double crit, bool pending = false)
        => Render(percent, ColorMath.RiskColor(percent, theme, warn, crit), pending);

    /// <summary>
    /// Badge con estado por forma (overlay) + estado stale. El texto adapta su contraste al fondo
    /// (<see cref="ColorMath.Contrast"/>), de modo que es legible en barra de tareas clara u oscura.
    /// </summary>
    public static Icon Render(int percent, Theme theme, double warn, double crit,
        UsageStatus status, bool stale = false, bool pending = false)
    {
        percent = Math.Clamp(percent, 0, 999);
        var bg = ColorMath.RiskColor(percent, theme, warn, crit);
        return RenderBadge(percent >= 100 ? "99+" : percent.ToString(), bg, status, stale, pending);
    }

    /// <summary>Neutral badge for "no data / auth expired / offline".</summary>
    public static Icon RenderError(Color bg, bool pending = false)
        => RenderBadge("!", bg, UsageStatus.Ok, stale: false, pending);

    private static Icon RenderBadge(string text, Color bg, UsageStatus status, bool stale, bool pending)
    {
        // Render at high resolution (48px) so Windows downscales to a crisp tray icon on any DPI.
        const int size = 48;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            // Estado "stale": dato envejecido → badge atenuado para que se note de un vistazo que no es fresco.
            Color fillColor = stale ? Color.FromArgb(150, bg) : bg;
            using (var brush = new SolidBrush(fillColor))
                Shapes.FillRounded(g, brush, new Rectangle(0, 0, size - 1, size - 1), 11);

            // Texto con contraste calculado sobre el fondo (no blanco fijo): legible en barra clara/oscura.
            Color textColor = ColorMath.Contrast(bg);
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
            // Ok→círculo = sin overlay (el badge ya es el indicador). Reusa el patrón del badge "pending".
            DrawShapeOverlay(g, Tray.ShapeFor(status), bg, textColor, size);

            if (pending)
            {
                var amber = Color.FromArgb(0xF5, 0xA6, 0x23);
                int d = 18;
                var badge = new Rectangle(size - d - 1, 0, d, d);
                using var fill = new SolidBrush(amber);
                using var ring = new Pen(Color.FromArgb(0x1A, 0x1A, 0x1A), 2.5f);
                g.FillEllipse(fill, badge);
                g.DrawEllipse(ring, badge);
            }
        }

        IntPtr hIcon = bmp.GetHicon();
        try
        {
            using var tmp = Icon.FromHandle(hIcon);
            using var ms = new MemoryStream();
            tmp.Save(ms);
            ms.Position = 0;
            return new Icon(ms);
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
